"""Optional GitHub/Jira Kafka env validation; errors contain key names only."""
from __future__ import annotations

import argparse
import base64
from datetime import datetime, timezone
import ipaddress
import json
from pathlib import Path
import re
import subprocess

AZURE_ROOT = Path(__file__).resolve().parents[1]
MANIFEST = AZURE_ROOT / "vm/kafka/topics.json"
CA_LOCATION = "/tmp/researchtrack-kafka/ca.crt"
KEYS = {
    "Kafka__Enabled", "Kafka__BootstrapServers", "Kafka__SecurityProtocol",
    "Kafka__SslCaLocation", "Kafka__SslCaCertificateBase64",
    "Kafka__EnableSslCertificateVerification", "Kafka__SslEndpointIdentificationAlgorithm",
    "Kafka__Topic", "Kafka__ContractVersion", "Kafka__ConsumerGroupId",
}


def validate(service: str, values: dict[str, str], production: bool,
             manifest: Path = MANIFEST) -> None:
    def fail(key: str) -> None:
        raise ValueError(f"{service}: invalid or missing {key} for Kafka configuration")

    supplied = {key for key in values if key.startswith("Kafka__")}
    if service not in {"github", "jira"}:
        if supplied:
            fail("Kafka__ (service has no agreed Kafka role)")
        return
    if supplied - KEYS:
        fail("Kafka__ (unsupported key)")
    enabled = values.get("Kafka__Enabled", "false").lower()
    if enabled not in {"true", "false"}:
        fail("Kafka__Enabled")
    if enabled == "false":
        return  # Older env files need no Kafka keys, trust or credentials.
    for key in {"Kafka__BootstrapServers", "Kafka__Topic", "Kafka__ContractVersion", "Kafka__SslCaLocation"}:
        if not values.get(key) or re.search(r"CHANGE_ME|__SET_ME__|__GENERATE__|YOUR_|[<>]", values[key], re.I):
            fail(key)
    if values.get("Kafka__SecurityProtocol", "SSL") != "SSL":
        fail("Kafka__SecurityProtocol")
    if values.get("Kafka__EnableSslCertificateVerification", "true").lower() != "true":
        fail("Kafka__EnableSslCertificateVerification")
    if values.get("Kafka__SslEndpointIdentificationAlgorithm", "https") != "https":
        fail("Kafka__SslEndpointIdentificationAlgorithm")
    for endpoint in values["Kafka__BootstrapServers"].split(","):
        match = re.fullmatch(r"([A-Za-z0-9.-]+):([0-9]{1,5})", endpoint)
        if not match or not 1 <= int(match[2]) <= 65535 or int(match[2]) == 29092:
            fail("Kafka__BootstrapServers")
        host, port = match.groups()
        if not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?", host) or ".." in host:
            fail("Kafka__BootstrapServers")
        if production:
            try:
                address = ipaddress.IPv4Address(host)
            except ipaddress.AddressValueError:
                fail("Kafka__BootstrapServers")
            if port != "9092" or not any(address in ipaddress.ip_network(net) for net in
                                        ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16")):
                fail("Kafka__BootstrapServers")
    if not values["Kafka__SslCaLocation"].startswith("/"):
        fail("Kafka__SslCaLocation")
    encoded = values.get("Kafka__SslCaCertificateBase64", "")
    if production and (not encoded or values["Kafka__SslCaLocation"] != CA_LOCATION):
        fail("Kafka__SslCaCertificateBase64 / Kafka__SslCaLocation")
    if encoded:
        try:
            pem = base64.b64decode(encoded, validate=True)
            if not re.fullmatch(rb"\s*-----BEGIN CERTIFICATE-----[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----\s*", pem):
                fail("Kafka__SslCaCertificateBase64")
            certificate = subprocess.run(["openssl", "x509", "-noout", "-text", "-startdate", "-checkend", "0"],
                                         input=pem, capture_output=True, timeout=10)
            if certificate.returncode or b"CA:TRUE" not in certificate.stdout:
                fail("Kafka__SslCaCertificateBase64")
            start = re.search(rb"notBefore=(.+)", certificate.stdout)
            if not start or datetime.strptime(start[1].decode(), "%b %d %H:%M:%S %Y GMT").replace(tzinfo=timezone.utc) > datetime.now(timezone.utc):
                fail("Kafka__SslCaCertificateBase64")
        except (ValueError, OSError, subprocess.SubprocessError):
            fail("Kafka__SslCaCertificateBase64")
    check = subprocess.run(["jq", "-e", "-f", str(AZURE_ROOT / "vm/scripts/kafka-topic-manifest.jq"),
                            str(manifest)], capture_output=True, timeout=10)
    if check.returncode:
        fail("topic registry")
    topics = json.loads(manifest.read_text())["topics"]
    agreed = next((topic for topic in topics if topic["approved"] and topic["domain"] == service
                   and topic["name"] == values["Kafka__Topic"]), None)
    if not agreed or agreed["contractVersion"] != values["Kafka__ContractVersion"]:
        fail("Kafka__Topic / Kafka__ContractVersion (approved contract required)")
    group = values.get("Kafka__ConsumerGroupId", "")
    if group != (agreed["consumerGroupId"] or ""):
        fail("Kafka__ConsumerGroupId (must match approved consumer)")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("env_dir", type=Path)
    parser.add_argument("--environment", choices=["test", "production"], required=True)
    args = parser.parse_args()
    from importlib.util import module_from_spec, spec_from_file_location
    spec = spec_from_file_location("renderer", AZURE_ROOT / "scripts/render-containerapp.py")
    renderer = module_from_spec(spec)
    spec.loader.exec_module(renderer)
    for path in args.env_dir.glob("*.env"):
        validate(path.stem, dict(renderer.parse_env_file(str(path))), args.environment == "production")
    print("PASS: optional Kafka environment configuration")


if __name__ == "__main__":
    try:
        main()
    except ValueError as error:
        raise SystemExit(str(error))
    except (OSError, subprocess.SubprocessError):
        # Values and certificate material must never appear in errors.
        raise SystemExit("FAIL: Kafka environment configuration invalid; check enabled service keys and approved registry.")

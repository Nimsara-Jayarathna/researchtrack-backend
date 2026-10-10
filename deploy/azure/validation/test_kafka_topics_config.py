#!/usr/bin/env python3
"""Offline tests. Temporary approved contracts are fixtures, never repository approvals."""
import base64
import contextlib
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
SCRIPTS = ROOT / "deploy/azure/scripts"
sys.path.insert(0, str(SCRIPTS))
import kafka_config as config

spec = importlib.util.spec_from_file_location("renderer", SCRIPTS / "render-containerapp.py")
renderer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(renderer)


class KafkaTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.work = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)
        self.registry = json.loads(config.MANIFEST.read_text())
        # Exercise the approval gate independently of the owner's repository approvals.
        for topic in self.registry["topics"][1:]:
            topic.update(approved=False, consumerOwner=None, consumerGroupId=None,
                         contractVersion=None, contractReference=None)
        self.manifest = self.work / "topics.json"
        self.scripts = self.work / "scripts"
        self.scripts.mkdir()
        for name in ("kafka-topics.sh", "kafka-topic-manifest.jq"):
            shutil.copy(ROOT / "deploy/azure/vm/scripts" / name, self.scripts)
        self.cli = self.scripts / "kafka-topics.sh"
        self.state = self.work / "state.json"
        self.state.write_text("{}")
        self.calls = self.work / "calls.jsonl"
        # Emulate the broker's actual CLI surface, including persistence between invocations.
        compose = self.scripts / "compose.sh"
        compose.write_text('''#!/usr/bin/env python3
import json, os, pathlib, sys
a = sys.argv[1:]
w = pathlib.Path(os.environ["KAFKA_FIXTURE"])
with (w / "calls.jsonl").open("a") as f: f.write(json.dumps(a) + "\\n")
if (w / "unavailable").exists(): sys.exit(1)
s = json.loads((w / "state.json").read_text())
def arg(k): return a[a.index(k)+1]
if "--list" in a: print("\\n".join(s))
elif "--create" in a:
    n = arg("--topic")
    s.setdefault(n, {"partitions": int(arg("--partitions")), "rf": int(arg("--replication-factor")),
                     "retention": int(arg("--config").split("=")[1])})
    (w / "state.json").write_text(json.dumps(s))
elif "--entity-name" in a:
    n = arg("--entity-name")
    print("  retention.ms=" + str(s[n]["retention"]) + " sensitive=false synonyms={DEFAULT_CONFIG:log.retention.ms=259200000}")
elif "--describe" in a:
    n = arg("--topic"); t = s[n]
    print(f"Topic: {n} PartitionCount: {t['partitions']} ReplicationFactor: {t['rf']} Configs: retention.ms={t['retention']}")
else: sys.exit(9)
''')
        compose.chmod(0o755)
        # macOS ships no timeout; retain real coreutils timeout when available.
        if not shutil.which("timeout"):
            timeout = self.scripts / "timeout"
            if shutil.which("gtimeout"):
                timeout.symlink_to(shutil.which("gtimeout"))
            else:
                timeout.write_text('#!/bin/sh\nshift\nexec "$@"\n')
                timeout.chmod(0o755)  # Outer subprocess still enforces a bounded offline test.
        self.save()

    def save(self):
        self.manifest.write_text(json.dumps(self.registry))

    def run_cli(self, *args):
        return subprocess.run([str(self.cli), *args, str(self.manifest)], capture_output=True, text=True,
                              env={**os.environ, "KAFKA_FIXTURE": str(self.work),
                                   "PATH": str(self.scripts) + os.pathsep + os.environ["PATH"]}, timeout=20)

    def approve_fixtures(self):
        for topic in self.registry["topics"][1:]:
            topic.update(approved=True, consumerOwner="offline-test", consumerGroupId="fixture-" + topic["domain"],
                         contractVersion="1", contractReference="offline synthetic fixture only")
        self.save()

    def test_manifest_valid_without_broker_access(self):
        self.assertEqual(0, self.run_cli("validate-manifest").returncode)
        self.assertFalse(self.calls.exists())

    def test_invalid_manifest_rejected_before_broker_access(self):
        mutations = [("name", "bad name"), ("name", "."), ("partitions", 0), ("partitions", 1.5),
                     ("partitions", "1"), ("replicationFactor", 3), ("retentionHours", -1),
                     ("retentionHours", 0), ("retentionHours", "24"), ("approved", "true"),
                     ("consumerOwner", None), ("contractReference", None), ("domain", "wrong")]
        original = copy.deepcopy(self.registry)
        for key, value in mutations:
            with self.subTest(key=key, value=value):
                self.registry = copy.deepcopy(original)
                self.registry["topics"][0][key] = value
                self.save()
                self.assertEqual(2, self.run_cli("reconcile", "--apply").returncode)
        for key in original["topics"][0]:
            with self.subTest(missing=key):
                self.registry = copy.deepcopy(original)
                del self.registry["topics"][0][key]
                self.save()
                self.assertEqual(2, self.run_cli("validate-manifest").returncode)
        self.registry = original
        self.registry["topics"].append(copy.deepcopy(original["topics"][0]))
        self.save()
        self.assertEqual(2, self.run_cli("validate-manifest").returncode)
        self.assertFalse(self.calls.exists())

    def test_default_dry_run_and_check_never_write(self):
        result = self.run_cli("reconcile")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("MISSING: researchtrack.deployment-smoke", result.stdout)
        self.assertEqual({}, json.loads(self.state.read_text()))
        self.assertNotIn("github.events", result.stdout)
        self.assertEqual(1, self.run_cli("reconcile", "--check").returncode)

    def test_apply_creates_only_approved_and_is_repeatable(self):
        result = self.run_cli("reconcile", "--apply")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["researchtrack.deployment-smoke"], list(json.loads(self.state.read_text())))
        self.assertEqual(0, self.run_cli("reconcile", "--apply").returncode)
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()]
        self.assertEqual(1, sum("--create" in call for call in calls))
        self.assertFalse(any("--delete" in call or "--alter" in call for call in calls))

    def test_drift_prevents_all_creation(self):
        self.approve_fixtures()
        for field, value in (("partitions", 2), ("rf", 2), ("retention", 864000000)):
            with self.subTest(field=field):
                state = {"researchtrack.deployment-smoke": {"partitions": 1, "rf": 1, "retention": 86400000}}
                state["researchtrack.deployment-smoke"][field] = value
                self.state.write_text(json.dumps(state))
                result = self.run_cli("reconcile", "--apply")
                self.assertEqual(1, result.returncode)
                self.assertIn("DRIFT", result.stderr)
                self.assertEqual(state, json.loads(self.state.read_text()))

    def test_broker_failure_is_clear_and_non_destructive(self):
        (self.work / "unavailable").touch()
        result = self.run_cli("reconcile", "--apply")
        self.assertEqual(1, result.returncode)
        self.assertIn("broker topic query failed", result.stderr)
        self.assertEqual({}, json.loads(self.state.read_text()))

    def settings(self, domain):
        self.approve_fixtures()
        pem = self.work / "ca.crt"
        subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1",
                        "-subj", "/CN=Offline CA", "-addext", "basicConstraints=critical,CA:TRUE",
                        "-keyout", str(self.work / "ca.key"), "-out", str(pem)],
                       check=True, capture_output=True)
        return {"Kafka__Enabled": "true", "Kafka__BootstrapServers": "10.20.10.4:9092",
                "Kafka__SecurityProtocol": "SSL", "Kafka__SslCaLocation": config.CA_LOCATION,
                "Kafka__SslCaCertificateBase64": base64.b64encode(pem.read_bytes()).decode(),
                "Kafka__EnableSslCertificateVerification": "true", "Kafka__SslEndpointIdentificationAlgorithm": "https",
                "Kafka__Topic": f"researchtrack.{domain}.events.v1", "Kafka__ContractVersion": "1",
                "Kafka__ConsumerGroupId": f"fixture-{domain}"}

    def test_disabled_and_older_configuration_remain_compatible(self):
        for service in ("github", "jira"):
            config.validate(service, {}, True)
            config.validate(service, {"Kafka__Enabled": "false", "Kafka__SslCaCertificateBase64": "CHANGE_ME"}, True)
        for service in ("auth", "project", "gateway", "meeting", "submission"):
            config.validate(service, {}, True)
            with self.assertRaises(ValueError):
                config.validate(service, {"Kafka__Enabled": "false"}, True)

    def test_production_rejects_unsafe_and_missing_settings(self):
        values = self.settings("github")
        invalid = [("Kafka__BootstrapServers", value) for value in
                   ("localhost:9092", "127.0.0.1:9092", "8.8.8.8:9092", "10.20.10.4:29092",
                    "10.20.10.4:9093", "http://10.20.10.4:9092", "10.20.10.4:0", "<private-ip>:9092")]
        invalid += [("Kafka__SecurityProtocol", "PLAINTEXT"), ("Kafka__EnableSslCertificateVerification", "false"),
                    ("Kafka__SslEndpointIdentificationAlgorithm", "none"), ("Kafka__ConsumerGroupId", "unrelated"),
                    ("Kafka__Topic", "researchtrack.jira.events.v1"), ("Kafka__ContractVersion", "2"),
                    ("Kafka__SslCaCertificateBase64", "invalid"), ("Kafka__SslCaLocation", "/absent/ca.crt")]
        for key, value in invalid:
            with self.subTest(key=key, value=value), self.assertRaises(ValueError) as failure:
                config.validate("github", {**values, key: value}, True, self.manifest)
            self.assertNotIn(values["Kafka__SslCaCertificateBase64"], str(failure.exception))
        for key in ("Kafka__BootstrapServers", "Kafka__Topic", "Kafka__ContractVersion", "Kafka__SslCaLocation", "Kafka__SslCaCertificateBase64"):
            with self.subTest(missing=key), self.assertRaises(ValueError):
                config.validate("github", {**values, key: ""}, True, self.manifest)
        with self.assertRaises(ValueError):
            config.validate("github", {**values, "Kafka__Topic": "researchtrack.github.unapproved.v1"}, True)

    def test_local_test_support_external_tls_endpoints(self):
        values = self.settings("jira")
        for endpoint in ("localhost:19092", "test-kafka.internal:9092"):
            config.validate("jira", {**values, "Kafka__BootstrapServers": endpoint}, False, self.manifest)

    def test_private_material_is_not_accepted_as_ca(self):
        values = self.settings("github")
        private = base64.b64encode((self.work / "ca.key").read_bytes()).decode()
        with self.assertRaises(ValueError):
            config.validate("github", {**values, "Kafka__SslCaCertificateBase64": private}, True, self.manifest)

    def test_real_renderer_passes_service_keys_and_ca_as_secret(self):
        for service in ("github", "jira"):
            values = self.settings(service)
            env = self.work / f"{service}.env"
            env.write_text("\n".join(f"{key}={value}" for key, value in values.items()))
            for kind in ("app", "job"):
                output = self.work / f"{service}.{kind}.json"
                argv = ["render", "--kind", kind, "--service", service, "--name", f"rt-{service}-prod",
                        "--location", "offline", "--environment-id", "/offline", "--image", "offline@sha256:0",
                        "--env-file", str(env), "--output", str(output)]
                with patch.object(sys, "argv", argv), patch.object(renderer, "validate_kafka",
                     side_effect=lambda s, v, p: config.validate(s, v, p, self.manifest)), contextlib.redirect_stdout(io.StringIO()) as printed:
                    renderer.main()
                document = json.loads(output.read_text())
                entries = document["parameters"]["env"]["value"] if kind == "app" else document["properties"]["template"]["containers"][0]["env"]
                mapped = {entry["name"]: entry for entry in entries}
                self.assertEqual(values["Kafka__Topic"], mapped["Kafka__Topic"]["value"])
                self.assertIn("secretRef", mapped["Kafka__SslCaCertificateBase64"])
                self.assertNotIn(values["Kafka__SslCaCertificateBase64"], printed.getvalue())
                self.assertEqual(0o600, output.stat().st_mode & 0o777)

    def test_bundle_and_startup_keep_provisioning_separate(self):
        for script in ("reconcile-stack.sh",):
            self.assertNotIn("reconcile --apply", (ROOT / "deploy/azure/vm/scripts" / script).read_text())
        for service in ("GitHub", "Jira"):
            program = (ROOT / f"src/Services/ResearchTrack.{service}Service/Program.cs").read_text()
            self.assertIn("AddResearchTrackKafkaConfiguration", program)
            self.assertNotIn("kafka-topics", program)
        bundle = (SCRIPTS / "build-vm-bundle.sh").read_text()
        self.assertIn("kafka-topic-manifest.jq", bundle)
        self.assertIn("validate-manifest", bundle)
        self.assertNotIn("--apply", bundle)

    def test_runtime_configuration_changes_affect_only_github_and_jira_images(self):
        impact_spec = importlib.util.spec_from_file_location("service_impact", ROOT / "deploy/build/service-impact.py")
        impact = importlib.util.module_from_spec(impact_spec)
        sys.modules[impact_spec.name] = impact
        impact_spec.loader.exec_module(impact)
        class WorkingTree:
            def read(self, path):
                file = ROOT / path
                return file.read_text() if file.exists() else None
        affected = [service for service in impact.SERVICES if impact.build_scope(WorkingTree(), service).reason(
            "src/BuildingBlocks/Kafka/KafkaRuntimeOptions.cs")]
        self.assertEqual(["github", "jira"], affected)

    def test_renderer_rejects_unapproved_before_writing_spec(self):
        values = self.settings("github")
        values["Kafka__Topic"] = "researchtrack.github.unapproved.v1"
        env = self.work / "github.env"
        env.write_text("\n".join(f"{key}={value}" for key, value in values.items()))
        output = self.work / "blocked.json"
        result = subprocess.run([sys.executable, str(SCRIPTS / "render-containerapp.py"), "--service", "github",
                                 "--name", "offline", "--location", "offline", "--environment-id", "/offline",
                                 "--image", "offline", "--env-file", str(env), "--output", str(output)],
                                capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("approved contract required", result.stderr)
        self.assertFalse(output.exists())
        self.assertNotIn(values["Kafka__SslCaCertificateBase64"], result.stderr)

    def test_test_vps_validator_preserves_legacy_and_gates_enabled_kafka(self):
        env_dir = self.work / "test-env"
        env_dir.mkdir()
        services = ("auth", "project", "github", "jira", "meeting", "submission")
        for component in ("mysql", "shared", "gateway", *services, "grafana"):
            values = dict(renderer.parse_env_file(str(ROOT / f"config/env/{component}/.env.example")))
            for key, value in list(values.items()):
                if key.startswith("Kafka__"):
                    del values[key]  # Existing bundles predate optional Kafka settings.
                elif "CHANGE_ME" in value or not value:
                    values[key] = "synthetic-value"
            if component in services:
                values["ConnectionStrings__DefaultConnection"] = (
                    f"Server=mysql;Port=3306;Database=researchtrack_{component};User=rt_{component};Password=synthetic-value;SslMode=Disabled")
                values["Services__Project__BaseUrl"] = "http://project:8080"
                values["Services__Auth__BaseUrl"] = "http://auth:8080"
            if component == "mysql":
                for service in services:
                    values[service.upper() + "_DB_NAME"] = "researchtrack_" + service
                    values[service.upper() + "_DB_USER"] = "rt_" + service
                    values[service.upper() + "_DB_PASSWORD"] = "synthetic-value"
            if component == "shared":
                values["Jwt__SigningKey"] = "synthetic-signing-key-" * 3
            if component == "gateway":
                for service in services:
                    values[service.upper() + "_SERVICE_URL"] = f"http://{service}:8080"
            if component == "jira":
                values["Jira__TokenEncryptionKey"] = base64.b64encode(b"s" * 32).decode()
                values["Jira__WebhookUrl"] = "https://api.example.test/api/v1/jira/webhooks"
            if "ASPNETCORE_ENVIRONMENT" in values:
                values.update(ASPNETCORE_ENVIRONMENT="Test", DOTNET_ENVIRONMENT="Test", ASPNETCORE_URLS="http://+:8080")
            name = "shared-auth" if component == "shared" else component
            (env_dir / f"{name}.env").write_text("\n".join(f"{key}={value}" for key, value in values.items()) + "\n")
        def run():
            return subprocess.run(["bash", str(ROOT / "deploy/validate-env-files.sh"), str(env_dir),
                                   str(ROOT / "config/env"), "test"], capture_output=True, text=True, timeout=30)
        result = run()
        self.assertEqual(0, result.returncode, result.stderr)
        with (env_dir / "github.env").open("a") as handle:
            handle.write("Kafka__Enabled=true\n")
        result = run()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("invalid or missing Kafka__", result.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)

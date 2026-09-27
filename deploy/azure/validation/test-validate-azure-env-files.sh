#!/usr/bin/env bash
# Tests validate-azure-env-files.sh with SYNTHETIC values generated from the
# committed config/env/*/.env.example contracts. Never reads real env files.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
validator="$repo_root/deploy/azure/validation/validate-azure-env-files.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

generate() {
  python3 - "$repo_root/config/env" "$1" <<'PY'
import sys
contracts, out = sys.argv[1], sys.argv[2]
files = {"mysql": "mysql", "shared": "shared-auth", "gateway": "gateway", "auth": "auth",
         "project": "project", "github": "github", "jira": "jira", "meeting": "meeting",
         "submission": "submission", "grafana": "grafana"}
services = ["auth", "project", "github", "jira", "meeting", "submission"]
fake_pw = {s: f"synthetic-{s}-password" for s in services}
public = {
    "FRONTEND_ORIGIN": "https://researchtrack.example.test",
    "PasswordReset__FrontendBaseUrl": "https://researchtrack.example.test",
    "GitHub__FrontendReturnOrigin": "https://researchtrack.example.test",
    "GitHub__SetupCallbackUrl": "https://api.example.test/api/github/access-source/install/callback",
    "Jira__RedirectUri": "https://api.example.test/api/v1/jira/callback",
    "GF_SERVER_ROOT_URL": "https://grafana.example.test",
}
for contract, name in files.items():
    lines = []
    for raw in open(f"{contracts}/{contract}/.env.example", encoding="utf-8"):
        raw = raw.rstrip("\n")
        if not raw or raw.lstrip().startswith("#") or "=" not in raw:
            continue
        key, value = raw.split("=", 1)
        if key in ("ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT"):
            value = "Production"
        elif key == "ASPNETCORE_URLS":
            value = "http://+:8080"
        elif key.endswith("_SERVICE_URL"):
            value = f"http://rt-{key.split('_')[0].lower()}-prod"
        elif key == "Services__Auth__BaseUrl":
            value = "http://rt-auth-prod/"
        elif key == "Services__Project__BaseUrl":
            value = "http://rt-project-prod/"
        elif key == "ConnectionStrings__DefaultConnection":
            value = (f"Server=10.20.10.4;Port=3306;Database=researchtrack_{contract};"
                     f"User=rt_{contract};Password={fake_pw[contract]};SslMode=Required")
        elif key.endswith("_DB_PASSWORD"):
            value = fake_pw[key.split("_")[0].lower()]
        elif key == "MYSQL_ROOT_PASSWORD":
            value = "synthetic-root-password"
        elif key == "Jwt__SigningKey":
            value = "s" * 48
        elif key == "Jira__TokenEncryptionKey":
            value = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="
        elif key == "Cookie__Secure":
            value = "true"
        elif key in public:
            value = public[key]
        elif key.startswith("Storage__"):
            pass  # deferred Blob integration: placeholders allowed
        elif "CHANGE_ME" in value:
            value = "synthetic-" + key.lower().replace("__", "-")
        lines.append(f"{key}={value}")
    with open(f"{out}/{name}.env", "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
PY
}

run() {
  EXPECTED_MYSQL_HOST=10.20.10.4 EXPECTED_GRAFANA_HOSTNAME=grafana.example.test \
    FORBIDDEN_HOSTS=test.example.test "$@" "$validator" "$case_dir" "$repo_root/config/env" >"$work/out" 2>&1
}

failures=0
expect() {
  local outcome="$1" name="$2" pattern="${3:-}"
  shift $(( $# < 3 ? $# : 3 ))
  if [[ "$outcome" == pass ]]; then
    if run env "$@"; then echo "ok   $name"; else echo "FAIL $name (expected pass)"; cat "$work/out"; failures=$((failures + 1)); fi
  else
    if run env "$@"; then
      echo "FAIL $name (expected rejection)"; failures=$((failures + 1))
    elif grep -q -- "$pattern" "$work/out"; then
      echo "ok   $name"
    else
      echo "FAIL $name (wrong error)"; cat "$work/out"; failures=$((failures + 1))
    fi
  fi
}

new_case() {
  case_dir="$work/$1"
  mkdir -p "$case_dir"
  generate "$case_dir"
}

set_key() {
  local file="$case_dir/$1" key="$2" value="$3"
  python3 - "$file" "$key" "$value" <<'PY'
import sys
path, key, value = sys.argv[1:]
lines = open(path).read().splitlines()
lines = [f"{key}={value}" if l.startswith(key + "=") else l for l in lines]
open(path, "w").write("\n".join(lines) + "\n")
PY
}

new_case valid
expect pass "valid synthetic production configuration"
expect pass "infra scope (mysql.env + grafana.env only)" "" VALIDATE_SCOPE=infra

# Shared JWT composition is reported by name for every consumer, never by value.
new_case shared-auth-report
if run env; then
  for service in auth project github jira meeting submission; do
    grep -q "^shared auth for $service (shared-auth.env $service.env): Jwt__Issuer: present, Jwt__Audience: present, Jwt__SigningKey: present$" "$work/out" \
      || { echo "FAIL shared-auth report for $service"; cat "$work/out"; failures=$((failures + 1)); }
  done
  if grep -q '^shared auth for gateway' "$work/out"; then
    echo "FAIL gateway does not use shared JWT validation"; failures=$((failures + 1))
  fi
  if grep -q "$(printf 's%.0s' {1..48})" "$work/out"; then
    echo "FAIL signing key value printed"; failures=$((failures + 1))
  fi
  echo "ok   shared JWT keys reported present for all six consumers, values never printed"
else
  echo "FAIL shared-auth report case"; cat "$work/out"; failures=$((failures + 1))
fi

new_case jira-owns-jwt
printf 'Jwt__SigningKey=%s\n' "$(printf 'j%.0s' {1..48})" >> "$case_dir/jira.env"
expect fail "service-specific copy of shared JWT key rejected" "jira.env defines shared-auth key 'Jwt__SigningKey'"

new_case shared-auth-missing
set_key shared-auth.env Jwt__Audience ""
expect fail "missing shared JWT key rejected for consumers" "jira uses shared JWT authentication but its composed environment (shared-auth.env jira.env) lacks 'Jwt__Audience'"

new_case compose-url
set_key gateway.env AUTH_SERVICE_URL "http://auth:8080"
expect fail "Compose service URL rejected" "must address the Container App rt-auth-prod"

new_case ssl-disabled
set_key jira.env ConnectionStrings__DefaultConnection \
  "Server=10.20.10.4;Port=3306;Database=researchtrack_jira;User=rt_jira;Password=synthetic-jira-password;SslMode=Disabled"
expect fail "MySQL without TLS rejected" "SslMode must be Required"

new_case compose-db
set_key auth.env ConnectionStrings__DefaultConnection \
  "Server=mysql;Port=3306;Database=researchtrack_auth;User=rt_auth;Password=synthetic-auth-password;SslMode=Required"
expect fail "Compose MySQL host rejected" "Compose/local host"

new_case test-env
set_key meeting.env ASPNETCORE_ENVIRONMENT Test
expect fail "non-Production runtime environment rejected" "ASPNETCORE_ENVIRONMENT must be 'Production'"

new_case placeholder
set_key github.env GitHub__WebhookSecret CHANGE_ME
expect fail "placeholder rejected" "still contains a placeholder"

new_case test-host
set_key gateway.env FRONTEND_ORIGIN "https://test.example.test"
expect fail "Test hostname rejected" "references Test host"

new_case grafana-url
set_key grafana.env GF_SERVER_ROOT_URL "http://localhost:3000"
expect fail "Grafana root URL must be the public HTTPS hostname" "GF_SERVER_ROOT_URL must be"

new_case insecure-cookie
set_key auth.env Cookie__Secure false
expect fail "insecure cookies rejected" "Cookie__Secure must be 'true'"

new_case invalid-jira-token-key
set_key jira.env Jira__TokenEncryptionKey "not-a-32-byte-base64-key"
expect fail "invalid Jira token encryption key rejected" "must be valid Base64 that decodes to exactly 32 bytes"

new_case missing-key
sed -i.bak '/^Jira__ClientSecret=/d' "$case_dir/jira.env" && rm -f "$case_dir/jira.env.bak"
expect fail "missing contract key rejected" "missing 'Jira__ClientSecret'"

if ((failures > 0)); then
  echo "$failures validator test(s) failed." >&2
  exit 1
fi
echo "All Azure validator tests passed."

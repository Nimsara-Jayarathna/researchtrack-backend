#!/usr/bin/env bash
# Reconciles the infrastructure VM stack to the desired state in
# /opt/researchtrack. Idempotent: safe to run on every deployment. Persistent
# state under /data/researchtrack is never deleted or reformatted here.
#
# Runs as root through Azure Run Command, after configure-vm.sh and after the
# deployment bundle has been installed (see install-bundle.sh).
set -Eeuo pipefail

opt=/opt/researchtrack
data=/data/researchtrack
compose="$opt/scripts/compose.sh"
kafka_image="apache/kafka:3.9.1"

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }

mountpoint -q "$data" || { echo "$data is not mounted; run configure-vm.sh first." >&2; exit 1; }
for file in runtime/infra.env runtime/mysql.env runtime/grafana.env compose.yml; do
  [[ -f "$opt/$file" ]] || { echo "Missing $opt/$file" >&2; exit 1; }
done

set -a
# shellcheck disable=SC1091
. "$opt/runtime/infra.env"
set +a

for key in INFRA_PRIVATE_IP API_HOSTNAME GRAFANA_HOSTNAME ACA_ENV_DOMAIN ACA_TLS_VERIFY KAFKA_RETENTION_HOURS; do
  [[ -n "${!key:-}" ]] || { echo "infra.env $key is required." >&2; exit 1; }
done
hostname_re='^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$'
for key in API_HOSTNAME GRAFANA_HOSTNAME ACA_ENV_DOMAIN; do
  [[ "${!key}" =~ $hostname_re ]] || { echo "infra.env $key is not a valid hostname." >&2; exit 1; }
done
[[ "$ACA_TLS_VERIFY" == "on" || "$ACA_TLS_VERIFY" == "off" ]] || { echo "ACA_TLS_VERIFY must be on or off." >&2; exit 1; }
[[ "$KAFKA_RETENTION_HOURS" =~ ^[0-9]+$ ]] || { echo "KAFKA_RETENTION_HOURS must be numeric." >&2; exit 1; }
ip -4 -o addr show | grep -qw "inet $INFRA_PRIVATE_IP/[0-9]*" \
  || { echo "INFRA_PRIVATE_IP $INFRA_PRIVATE_IP is not assigned to this VM." >&2; exit 1; }

gateway_fqdn="rt-gateway-prod.$ACA_ENV_DOMAIN"

# ---------------------------------------------------------------------------
log "[1/9] Persistent directories"
# Container users: Kafka 1000 (set in compose.yml), Prometheus nobody, Grafana 472.
# MySQL's entrypoint owns its datadir itself.
install -d -m 750 "$data/mysql"
install -d -m 750 -o 1000 -g 1000 "$data/kafka/data"
install -d -m 700 "$data/kafka/tls"
install -d -m 750 -o 65534 -g 65534 "$data/prometheus"
install -d -m 750 -o 472 -g 0 "$data/grafana"
install -d -m 700 "$data/backups"
ensure_owner() {
  local path="$1" owner="$2"
  [[ "$(stat -c %u:%g "$path")" == "$owner" ]] || chown -R "$owner" "$path"
}
ensure_owner "$data/kafka/data" 1000:1000
ensure_owner "$data/prometheus" 65534:65534
ensure_owner "$data/grafana" 472:0

# ---------------------------------------------------------------------------
log "[2/9] Kafka identity and TLS material"
if [[ ! -s "$data/kafka/cluster-id" ]]; then
  # KRaft metadata is bound to this ID; it is generated once and never replaced.
  docker run --rm "$kafka_image" /opt/kafka/bin/kafka-storage.sh random-uuid > "$data/kafka/cluster-id.tmp"
  mv "$data/kafka/cluster-id.tmp" "$data/kafka/cluster-id"
fi
chmod 644 "$data/kafka/cluster-id"

tls="$data/kafka/tls"
if [[ ! -s "$tls/ca.key" || ! -s "$tls/ca.crt" ]]; then
  log "      creating Kafka CA"
  openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 3650 \
    -subj "/CN=ResearchTrack Kafka CA (production)" \
    -keyout "$tls/ca.key" -out "$tls/ca.crt" 2>/dev/null
fi
[[ -s "$tls/keystore.pass" ]] || openssl rand -hex 24 > "$tls/keystore.pass"
chmod 600 "$tls/ca.key" "$tls/keystore.pass"
chmod 644 "$tls/ca.crt"
keystore_password="$(cat "$tls/keystore.pass")"

broker_cert_valid() {
  [[ -s "$tls/broker.crt" && -s "$tls/broker.p12" ]] || return 1
  # Renew 30 days before expiry, or if the private IP changed.
  openssl x509 -in "$tls/broker.crt" -noout -checkend $((30 * 86400)) >/dev/null || return 1
  openssl x509 -in "$tls/broker.crt" -noout -ext subjectAltName 2>/dev/null | grep -q "IP Address:$INFRA_PRIVATE_IP" || return 1
  openssl verify -CAfile "$tls/ca.crt" "$tls/broker.crt" >/dev/null 2>&1
}
if ! broker_cert_valid; then
  log "      issuing Kafka broker certificate for $INFRA_PRIVATE_IP"
  openssl req -newkey rsa:3072 -sha256 -nodes \
    -subj "/CN=$INFRA_PRIVATE_IP" \
    -keyout "$tls/broker.key" -out "$tls/broker.csr" 2>/dev/null
  openssl x509 -req -sha256 -days 825 \
    -in "$tls/broker.csr" -CA "$tls/ca.crt" -CAkey "$tls/ca.key" -CAcreateserial \
    -extfile <(printf 'subjectAltName=IP:%s\nextendedKeyUsage=serverAuth\n' "$INFRA_PRIVATE_IP") \
    -out "$tls/broker.crt" 2>/dev/null
  openssl pkcs12 -export -name kafka \
    -inkey "$tls/broker.key" -in "$tls/broker.crt" -certfile "$tls/ca.crt" \
    -passout "pass:$keystore_password" -out "$tls/broker.p12"
  rm -f "$tls/broker.csr"
fi
chmod 600 "$tls/broker.key"
chown 1000:1000 "$tls/broker.p12"
chmod 400 "$tls/broker.p12"

# ---------------------------------------------------------------------------
log "[3/9] Render configuration"
render() {
  sed \
    -e "s|__INFRA_PRIVATE_IP__|$INFRA_PRIVATE_IP|g" \
    -e "s|__API_HOSTNAME__|$API_HOSTNAME|g" \
    -e "s|__GRAFANA_HOSTNAME__|$GRAFANA_HOSTNAME|g" \
    -e "s|__GATEWAY_FQDN__|$gateway_fqdn|g" \
    -e "s|__ACA_ENV_DOMAIN__|$ACA_ENV_DOMAIN|g" \
    -e "s|__ACA_TLS_VERIFY__|$ACA_TLS_VERIFY|g" \
    -e "s|__ACA_TLS_INSECURE__|$([[ "$ACA_TLS_VERIFY" == on ]] && echo false || echo true)|g" \
    -e "s|__KAFKA_RETENTION_HOURS__|$KAFKA_RETENTION_HOURS|g" \
    "$1"
}

kafka_props="$opt/runtime/kafka-server.properties"
umask 077
render "$opt/kafka/server.properties.template" \
  | sed "s|__KAFKA_KEYSTORE_PASSWORD__|$keystore_password|g" > "$kafka_props.next"
umask 022
mv "$kafka_props.next" "$kafka_props"
chown root:1000 "$kafka_props"
chmod 640 "$kafka_props"

render "$opt/prometheus/prometheus.yml.template" > "$opt/prometheus/prometheus.yml"

has_certificate() { [[ -s "/etc/letsencrypt/live/$1/fullchain.pem" ]]; }
render_edge() {
  local conf="$opt/nginx/conf.d" www="$opt/nginx/www"
  install -d -m 755 "$conf" "$www"
  cp "$opt/nginx/00-common.conf" "$opt/nginx/tls.inc" "$opt/nginx/security-headers.inc" \
     "$opt/nginx/grafana-proxy.inc" "$conf/"
  render "$opt/nginx/gateway-proxy.inc.template" > "$conf/gateway-proxy.inc"
  local site host mode
  for site in api grafana; do
    [[ "$site" == api ]] && host="$API_HOSTNAME" || host="$GRAFANA_HOSTNAME"
    has_certificate "$host" && mode=https || mode=http
    render "$opt/nginx/$site-$mode.conf.template" > "$conf/$site.conf"
    log "      $host: $mode"
  done
}
render_edge

"$compose" config --quiet

# ---------------------------------------------------------------------------
log "[4/9] Pull images"
"$compose" pull --quiet

wait_healthy() {
  local service="$1" attempts="$2" id status
  id="$("$compose" ps -q "$service")"
  for _ in $(seq 1 "$attempts"); do
    status="$(docker inspect --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$id")"
    [[ "$status" == healthy ]] && return 0
    [[ "$status" == unhealthy ]] && break
    sleep 3
  done
  docker logs --tail 80 "$id" >&2 || true
  echo "$service did not become healthy." >&2
  return 1
}

# ---------------------------------------------------------------------------
log "[5/9] MySQL"
"$compose" up -d mysql
wait_healthy mysql 60
# Streamed over stdin rather than bind-mounted, so the running container always
# executes the current script (a replaced file would keep its old inode mounted).
"$compose" exec -T mysql bash -s < "$opt/mysql/reconcile-databases.sh"

# ---------------------------------------------------------------------------
log "[6/9] Kafka"
# Kafka's inputs are file bind mounts; recreate when any of them changed so the
# container never keeps a stale inode.
kafka_inputs_hash="$(cat "$kafka_props" "$opt/kafka/log4j.properties" "$opt/kafka/client-ssl.properties" \
  "$tls/broker.p12" "$tls/ca.crt" | sha256sum | cut -d' ' -f1)"
if [[ "$(cat "$data/kafka/.inputs-hash" 2>/dev/null || true)" != "$kafka_inputs_hash" ]]; then
  "$compose" up -d --force-recreate kafka
else
  "$compose" up -d kafka
fi
wait_healthy kafka 40
printf '%s\n' "$kafka_inputs_hash" > "$data/kafka/.inputs-hash"

# ---------------------------------------------------------------------------
log "[7/9] Prometheus and Grafana"
# Bind-mounted configuration is only guaranteed active after recreation (same
# rule as the Test stack). Their data directories persist on the data disk.
"$compose" up -d --force-recreate prometheus grafana

# ---------------------------------------------------------------------------
log "[8/9] Nginx and certificates"
"$compose" up -d nginx
"$opt/scripts/reload-nginx.sh"

email_args=(--register-unsafely-without-email)
[[ -n "${LETSENCRYPT_EMAIL:-}" ]] && email_args=(--email "$LETSENCRYPT_EMAIL" --no-eff-email)
issued=false
for host in "$API_HOSTNAME" "$GRAFANA_HOSTNAME"; do
  has_certificate "$host" && continue
  # Expected to fail until public DNS points at this VM (cutover step).
  if certbot certonly --webroot -w "$opt/nginx/www" -d "$host" --cert-name "$host" \
      --agree-tos --non-interactive "${email_args[@]}" \
      --deploy-hook "$opt/scripts/reload-nginx.sh" >/dev/null 2>&1; then
    log "      certificate issued for $host"
    issued=true
  else
    log "      WARNING: no certificate for $host yet (DNS not pointed here?). Staying on restricted HTTP."
  fi
done
if [[ "$issued" == true ]]; then
  render_edge
  "$opt/scripts/reload-nginx.sh"
fi

# ---------------------------------------------------------------------------
log "[9/9] Timers and cleanup"
"$opt/scripts/install-timers.sh"
docker image prune -f >/dev/null

"$compose" ps
log "Infrastructure stack reconciled."

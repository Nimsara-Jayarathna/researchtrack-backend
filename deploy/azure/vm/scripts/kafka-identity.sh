#!/usr/bin/env bash
# Guard persistent identity before reconciliation can generate anything.
# shellcheck shell=bash
kafka_identity_safe() {
  local root="$1" persisted metadata entries
  if [[ ! -s "$root/cluster-id" ]]; then
    entries="$(find "$root/data" -mindepth 1 -print -quit 2>/dev/null)" || return 1
    if [[ -n "$entries" || -e "$root/.inputs-hash" || -e "$root/cluster-id" || -e "$root/tls/ca.crt" || -e "$root/tls/broker.p12" ]]; then
      echo 'Kafka identity missing/empty with existing state; restore the original cluster-id, never generate a replacement.' >&2
      return 1
    fi
    return 0 # Truly empty first deployment only.
  fi
  persisted="$(cat "$root/cluster-id")"
  [[ "$persisted" =~ ^[A-Za-z0-9_-]{22}$ ]] || { echo 'Invalid persisted Kafka cluster-id.' >&2; return 1; }
  if [[ -f "$root/data/meta.properties" ]]; then
    metadata="$(sed -n 's/^cluster.id=//p' "$root/data/meta.properties" | tr -d '\r')"
    [[ "$persisted" == "$metadata" ]] || { echo 'Kafka cluster-id does not match data/meta.properties; refusing reconciliation.' >&2; return 1; }
  elif [[ -e "$root/.inputs-hash" ]]; then
    echo 'Previously deployed Kafka has no data/meta.properties; check the disk and restore data before proceeding.' >&2
    return 1
  fi
}

# Fail closed if established TLS trust or a keystore password is lost.
kafka_tls_identity_safe() {
  local root="$1" tls="$1/tls"
  if [[ -e "$root/.inputs-hash" || -e "$tls/ca.key" || -e "$tls/ca.crt" ||
        -e "$tls/broker.p12" || -e "$tls/container/broker.p12" ]]; then
    [[ -s "$tls/ca.key" && -s "$tls/ca.crt" ]] || {
      echo 'Kafka CA material is incomplete; restore the original CA pair.' >&2; return 1;
    }
  fi
  if [[ -e "$root/.inputs-hash" || -e "$tls/broker.p12" || -e "$tls/container/broker.p12" || -e "$tls/keystore.pass" ]]; then
    [[ -s "$tls/keystore.pass" ]] || {
      echo 'Existing Kafka keystore password is missing/empty; restore it.' >&2; return 1;
    }
  fi
}

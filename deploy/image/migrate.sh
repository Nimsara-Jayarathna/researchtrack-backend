#!/bin/sh
set -eu

if [ -z "${ConnectionStrings__DefaultConnection:-}" ]; then
  echo "ConnectionStrings__DefaultConnection is missing." >&2
  exit 1
fi

exec /app/efbundle --connection "$ConnectionStrings__DefaultConnection"

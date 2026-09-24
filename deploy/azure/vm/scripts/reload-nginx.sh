#!/usr/bin/env bash
# Certbot deploy hook: reload Nginx after a certificate is issued or renewed.
# A failed config test leaves the running configuration untouched.
set -euo pipefail
/opt/researchtrack/scripts/compose.sh exec -T nginx nginx -t -q
/opt/researchtrack/scripts/compose.sh exec -T nginx nginx -s reload

#!/usr/bin/env bash
# Certbot dns-01 CLEANUP hook (Hetzner Cloud DNS). Deletes the _acme-challenge TXT rrset + the temp values file.
# Requires env: HETZNER_API_TOKEN.
set -euo pipefail
: "${HETZNER_API_TOKEN:?set HETZNER_API_TOKEN}"

API="https://api.hetzner.cloud/v1"
ZONE_ID="457682"          # solutionbox.cz
ZONE="solutionbox.cz"

SUB="${CERTBOT_DOMAIN%.$ZONE}"
if [ "$SUB" = "$CERTBOT_DOMAIN" ]; then REC="_acme-challenge"; else REC="_acme-challenge.$SUB"; fi

curl -s -o /dev/null -X DELETE -H "Authorization: Bearer $HETZNER_API_TOKEN" \
  "$API/zones/$ZONE_ID/rrsets/$REC/TXT" || true
rm -f "/tmp/acme-${REC//\//_}.values" /tmp/acme_body.json

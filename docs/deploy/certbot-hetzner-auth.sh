#!/usr/bin/env bash
# Certbot dns-01 AUTH hook for the Hetzner Cloud DNS API (api.hetzner.cloud/v1).
# Creates/updates the _acme-challenge.<sub> TXT record for the ACME challenge, then waits for propagation.
#
# A wildcard cert (`-d mp.solutionbox.cz -d *.mp.solutionbox.cz`) produces TWO challenges at the SAME
# _acme-challenge.mp name with two different values — BOTH must be present at validation. The Hetzner rrset
# UPDATE endpoint is finicky (422), but POST-create and DELETE both work, so we accumulate the values in a temp
# file across the (sequential) hook invocations and DELETE+re-POST the rrset with ALL values each time.
#
# Requires env: HETZNER_API_TOKEN. Zone id is verified-constant for solutionbox.cz.
set -euo pipefail
: "${HETZNER_API_TOKEN:?set HETZNER_API_TOKEN}"

API="https://api.hetzner.cloud/v1"
ZONE_ID="457682"          # solutionbox.cz (verified via GET /zones?name=solutionbox.cz)
ZONE="solutionbox.cz"

SUB="${CERTBOT_DOMAIN%.$ZONE}"
if [ "$SUB" = "$CERTBOT_DOMAIN" ]; then REC="_acme-challenge"; else REC="_acme-challenge.$SUB"; fi
ACC="/tmp/acme-${REC//\//_}.values"

echo "$CERTBOT_VALIDATION" >> "$ACC"

# Delete the current challenge rrset (ignore if absent), then recreate it with ALL accumulated values.
curl -s -o /dev/null -X DELETE -H "Authorization: Bearer $HETZNER_API_TOKEN" \
  "$API/zones/$ZONE_ID/rrsets/$REC/TXT" || true

REC="$REC" ACC="$ACC" python3 - <<'PY' > /tmp/acme_body.json
import json, os
recs = [{"value": '"%s"' % v.strip()} for v in open(os.environ["ACC"]) if v.strip()]
print(json.dumps({"name": os.environ["REC"], "type": "TXT", "ttl": 60, "records": recs}))
PY

curl -s -o /dev/null -w 'create TXT -> HTTP %{http_code}\n' \
  -X POST -H "Authorization: Bearer $HETZNER_API_TOKEN" -H "Content-Type: application/json" \
  --data @/tmp/acme_body.json "$API/zones/$ZONE_ID/rrsets"

# Wait for the Hetzner authoritative nameservers to serve the new TXT before ACME validates.
sleep 60

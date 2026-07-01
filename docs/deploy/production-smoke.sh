#!/usr/bin/env bash
# Production smoke gate for the compose deployment.
#
# Run on solutionbox2 from /opt/modularplatform after .env is filled:
#   bash docs/deploy/production-smoke.sh
#
# Use --no-build when images were already built in the same deploy.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

BUILD=1
if [[ "${1:-}" == "--no-build" ]]; then
  BUILD=0
elif [[ "${1:-}" != "" ]]; then
  echo "Usage: $0 [--no-build]" >&2
  exit 64
fi

fail() {
  echo "SMOKE FAIL: $*" >&2
  exit 1
}

require_file() {
  [[ -f "$1" ]] || fail "missing $1"
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "missing command: $1"
}

require_cmd docker
require_file .env
require_file docker-compose.yml

if grep -Eq '(^|=)__[A-Z0-9_]+__($|[[:space:]])' .env; then
  fail ".env still contains placeholder values"
fi

grep -q "ConnectionStrings__Write=Host=postgres;" .env || fail ".env must use compose Postgres for Write"
grep -q "ConnectionStrings__Read=Host=postgres;" .env || fail ".env must use compose Postgres for Read"
grep -q "ForwardedHeaders__KnownNetworks__0=172.16.0.0/12" .env || fail ".env must trust Docker private bridge range"
grep -Eq "^(OTEL_EXPORTER_OTLP_ENDPOINT|OpenTelemetry__Otlp__Endpoint)=" .env || fail ".env must configure an OTLP endpoint"

docker compose config >/dev/null

if [[ "$BUILD" -eq 1 ]]; then
  bash docs/deploy/build-images.sh
fi

docker compose up -d postgres
docker compose run --rm migrator
docker compose up -d api worker jobs web

echo ">> Waiting for Api readiness"
for _ in {1..60}; do
  if docker compose exec -T api curl -fsS http://localhost:8080/health/ready >/dev/null; then
    break
  fi
  sleep 2
done
docker compose exec -T api curl -fsS http://localhost:8080/health/ready >/dev/null || fail "Api readiness did not become healthy"

echo ">> Checking BFF through the host port"
for _ in {1..30}; do
  if curl -fsS -H "Host: mp.solutionbox.cz" http://127.0.0.1:16013/ >/dev/null; then
    break
  fi
  sleep 2
done
curl -fsS -H "Host: mp.solutionbox.cz" http://127.0.0.1:16013/ >/dev/null || fail "BFF did not respond on 127.0.0.1:16013"

echo ">> Compose status"
docker compose ps

echo "SMOKE OK: migrator exited, Api is ready, BFF responds on 127.0.0.1:16013"

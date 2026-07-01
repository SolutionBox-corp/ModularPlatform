# Deploy ModularPlatform to solutionbox2 (`mp.solutionbox.cz`)

Production deploy of the .NET 10 backend (Api/Worker/Jobs + one-shot MigrationService), a dedicated compose
Postgres instance, and the Next.js 16 BFF frontend onto **solutionbox2** (`46.224.177.212`, Docker + nginx + Certbot), behind
a wildcard-TLS nginx vhost. Architecture is **subdomain-per-tenant**: `mp.solutionbox.cz` (apex → `demo`
tenant), `admin.mp.solutionbox.cz` (platform admin), `{tenant}.mp.solutionbox.cz` (tenant apps).

Artifacts (in this repo): `Dockerfile.runtime` (thin .NET host image), `frontend/Dockerfile`,
`docker-compose.yml`, the two `.dockerignore`s, and `docs/deploy/` (`build-images.sh`, `.env.example`, nginx
vhost, certbot Hetzner hooks).

> **Build note (.NET 10 overlay glob bug):** compiling the .NET hosts INSIDE a Docker image layer fails on overlay
> filesystems — the SDK false-positives the default `**/*.cs` / `**/*.resx` globs as "drive-enumerating" and leaks
> the literal to csc (CS2001/CS2021/MSB3552). So `docs/deploy/build-images.sh` PUBLISHES each host via a
> **bind-mounted** SDK container (glob expansion works there), then bakes the output into thin runtime images
> (`Dockerfile.runtime`, glob-free COPY). **Always build with `build-images.sh`, never a bare `docker compose build`.**

> nginx terminates TLS and proxies **only** to the Next BFF on `127.0.0.1:16013`. The Api is never reached
> by nginx — the BFF proxies `/api/bff/*` → `/v1` server-side. Redis is omitted (single-instance fallbacks).

## 0. Prereqs
- `ssh solutionbox2` works (key-based).
- `HETZNER_API_TOKEN` exported in the shell running certbot (DNS lives on Hetzner Cloud DNS).
- Docker + docker compose v2 + certbot on the server.

## 1. Database model
Production uses the `postgres` service from `docker-compose.yml`, persisted in the `modularplatform-pgdata`
Docker volume. The application, migrator, healthcheck and backup script all talk to this same container database.
Do not point `.env` back to `host.docker.internal` unless you also remove the compose Postgres service and rewrite
the backup/health gates; splitting those would let readiness pass against one database while the app uses another.

## 2. DNS (Hetzner Cloud API)
Add two A records in zone `solutionbox.cz` → `46.224.177.212`:
- `mp` (apex of this deployment)
- `*.mp` (wildcard for every tenant + `admin`)

Confirm: `dig +short mp.solutionbox.cz`, `dig +short admin.mp.solutionbox.cz`, `dig +short x.mp.solutionbox.cz`.

## 3. Wildcard TLS cert (dns-01)
Wildcard requires dns-01 (http-01 can't). Copy the hooks to the server, make them executable, then:
```bash
export HETZNER_API_TOKEN=...        # from credentials.md
chmod +x /opt/modularplatform/deploy/certbot-hetzner-*.sh
certbot certonly --manual --preferred-challenges dns \
  --manual-auth-hook    /opt/modularplatform/deploy/certbot-hetzner-auth.sh \
  --manual-cleanup-hook /opt/modularplatform/deploy/certbot-hetzner-cleanup.sh \
  -d mp.solutionbox.cz -d '*.mp.solutionbox.cz'
```
The apex `mp.solutionbox.cz` is a separate SAN (a wildcard does NOT cover the apex). Cert lands at
`/etc/letsencrypt/live/mp.solutionbox.cz/`. For unattended renewal, persist the hook paths via
`/etc/letsencrypt/renewal/mp.solutionbox.cz.conf` (certbot records them) and ensure `HETZNER_API_TOKEN` is
available to the renew timer (e.g. an `EnvironmentFile` for the certbot systemd unit).

> First time: confirm the Hetzner rrsets endpoint shape with a read-only `GET /zones/{id}/rrsets` and tweak
> the hooks if the live API differs from the documented `/zones/{id}/rrsets` route.

## 4. Ship code + build (build runs in Docker; server needs only Docker + the source)
```bash
ssh solutionbox2 'mkdir -p /opt/modularplatform'
# from the repo root on the dev machine (working tree on `main`):
rsync -az --delete \
  --exclude '.git/' --exclude '**/bin/' --exclude '**/obj/' \
  --exclude '.env' --exclude '*.env' --exclude 'backups/' \
  --exclude 'frontend/node_modules/' --exclude 'frontend/.next/' \
  ./ solutionbox2:/opt/modularplatform/
# put the deploy hooks where the runbook expects them
ssh solutionbox2 'cp /opt/modularplatform/docs/deploy/certbot-hetzner-*.sh /opt/modularplatform/deploy/ 2>/dev/null || (mkdir -p /opt/modularplatform/deploy && cp /opt/modularplatform/docs/deploy/certbot-hetzner-*.sh /opt/modularplatform/deploy/)'
```
Create `/opt/modularplatform/.env` from `docs/deploy/.env.example`, fill the generated secrets, `chmod 600 .env`.
The DB host must stay `postgres`; the generated Postgres password goes into both `POSTGRES_PASSWORD` and the two
connection strings. `solutionbox2` already runs `grafana/otel-lgtm` on host ports 4317/4318, so keep
`OTEL_EXPORTER_OTLP_ENDPOINT=http://host.docker.internal:4317` unless the collector is moved.
```bash
# Publishes the 4 .NET hosts via a bind-mounted SDK (dodges the overlay glob bug) + builds thin runtime images + web.
ssh solutionbox2 'cd /opt/modularplatform && bash docs/deploy/build-images.sh'
```

## 5. Migrate + start
```bash
ssh solutionbox2 'cd /opt/modularplatform && docker compose run --rm migrator'   # expect exit 0
ssh solutionbox2 'cd /opt/modularplatform && docker compose up -d api worker jobs web'
ssh solutionbox2 'cd /opt/modularplatform && docker compose ps'                  # all healthy
ssh solutionbox2 'cd /opt/modularplatform && docker compose exec api curl -fsS http://localhost:8080/health/ready'
ssh solutionbox2 'cd /opt/modularplatform && bash docs/deploy/production-smoke.sh --no-build'
```

## 6. nginx vhost
```bash
ssh solutionbox2 'cp /opt/modularplatform/docs/deploy/nginx-mp.solutionbox.cz.conf /etc/nginx/sites-available/mp.solutionbox.cz \
  && ln -sf /etc/nginx/sites-available/mp.solutionbox.cz /etc/nginx/sites-enabled/ \
  && nginx -t && systemctl reload nginx'
```

## 7. Bootstrap first tenant + platform admin
No tenant is seeded on a fresh DB; `IdentitySeeder` DOES auto-seed all permissions + the system `admin` role on
startup. A user whose email is in `Identity:Auth:AdminEmails` gets `platform.tenants.manage` **on login**.
Tenants default to `RegistrationMode=InviteOnly` (secure) → the first member of a tenant needs an invite.
Reserved subdomains are `admin`/`www`/`api` (so `demo` — the apex fallback tenant — is allowed).

Do it via the UI once §6 is live (admin registers/logs in on `admin.mp...`, provisions a tenant + invite from
the platform console, the first user registers on `{tenant}.mp...` with the invite). Or scripted via the
internal Api (Host header drives tenant resolution; `localhost` = SYSTEM/admin plane):
```bash
C='docker compose exec -T api curl -fsS'
# 1) platform admin (SYSTEM plane: Host=localhost → no tenant). Email MUST match Identity:Auth:AdminEmails.
$C -X POST http://localhost:8080/v1/identity/users -H 'Content-Type: application/json' \
  -d '{"email":"miroslav.lalik@solutionbox.cz","password":"<ADMIN_PWD>","displayName":"Admin"}'
# 2) login → token (login grants the admin role on first login)
TOKEN=$($C -X POST http://localhost:8080/v1/identity/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"miroslav.lalik@solutionbox.cz","password":"<ADMIN_PWD>"}' | python3 -c 'import json,sys;print(json.load(sys.stdin)["data"]["accessToken"])')
# 3) provision the apex-fallback tenant "demo" (+ default entitlements: billing,notifications,files,operations,gdpr,marketing)
TID=$($C -X POST http://localhost:8080/v1/tenant/admin/tenants -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"name":"Demo","subdomain":"demo"}' | python3 -c 'import json,sys;print(json.load(sys.stdin)["data"]["tenantId"])')
# 4) mint a single-use invite (InviteOnly default)
$C -X POST "http://localhost:8080/v1/tenant/admin/tenants/$TID/invites" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"expiresInDays":7}'
# 5) register the first tenant user (Host = the tenant subdomain so the middleware resolves "demo")
$C -X POST http://localhost:8080/v1/identity/users -H 'Host: demo.mp.solutionbox.cz' \
  -H 'Content-Type: application/json' \
  -d '{"email":"user@demo.test","password":"<USER_PWD>","displayName":"User","inviteToken":"<TOKEN_FROM_STEP_4>"}'
```
(Verified against `IdentitySeeder`, `LoginHandler.EnsureConfiguredAdminAsync`, `ProvisionTenant*`,
`CreateTenantInvite*`, `TenantResolutionMiddleware`, `RegisterUserHandler`. The `ApiResponse<T>` envelope wraps
payloads under `data` — adjust the `python3` extractors if the shape differs.)

## 8. Verify (end-to-end)
1. `https://mp.solutionbox.cz` → login, valid TLS + HSTS.
2. `https://admin.mp.solutionbox.cz` → admin login → `/platform` console.
3. Register/login on `https://demo.mp.solutionbox.cz` → dashboard → /billing, /files, /notifications.
4. SSE: UI shows "Realtime: Live"; `curl -N https://demo.mp.solutionbox.cz/api/bff/realtime/stream` streams.
5. `docker compose logs --tail=100` clean; `api` `/health/ready` Healthy.
6. Welcome notification fires after registration (Worker drains the durable queue — confirms Balanced messaging).

## Rollback
`docker compose down` (data is in host PG — survives). Remove the nginx symlink + `systemctl reload nginx` to
detach the vhost. DNS/cert can stay.

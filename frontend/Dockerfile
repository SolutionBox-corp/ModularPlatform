# syntax=docker/dockerfile:1
# Next.js 16 BFF frontend. Build context = ./frontend (see docker-compose.yml `web` service).
# next.config.ts has NO `output: 'standalone'`, so the runtime needs the full node_modules + .next +
# the source `lib/` (next.config imports ./lib/origins + next-intl ./lib/i18n at start time). We therefore
# carry the whole built /app into the runtime stage rather than pruning — reliable over lean for a first deploy.
# Debian (bookworm-slim), not Alpine: avoids musl/native (sharp) surprises with Next 16.

FROM node:22-bookworm-slim AS build
WORKDIR /app
# pnpm 10 — required because frontend/pnpm-workspace.yaml uses `ignoredBuiltDependencies` (a pnpm 10 setting);
# pnpm 9 rejects that file with "packages field missing or empty". Pinned to the lockfile-producing version.
RUN npm install -g pnpm@10.30.3
ENV NEXT_TELEMETRY_DISABLED=1
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY . .
# `next build` loads the server config module (lib/config.ts), which fail-fasts on a missing/short
# SESSION_PASSWORD and a missing BACKEND_URL. These are SERVER-ONLY (not NEXT_PUBLIC_*) so they are NOT baked
# into the build — `next start` re-reads the REAL values from the container env (compose env_file) at runtime.
# The build-time placeholders only satisfy module-load validation. (NEXT_PHASE=phase-production-build skips the
# dev-default secret check, so a clearly-fake placeholder is fine here.)
ENV SESSION_PASSWORD=build-time-only-placeholder-not-a-real-secret-0001 \
    BACKEND_URL=http://localhost:8080
RUN pnpm build

FROM node:22-bookworm-slim AS runtime
WORKDIR /app
RUN npm install -g pnpm@10.30.3
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000
COPY --from=build /app ./
EXPOSE 3000
# `next start` reads next.config.ts (and the lib files it imports) at boot — all present in /app.
CMD ["pnpm", "start"]

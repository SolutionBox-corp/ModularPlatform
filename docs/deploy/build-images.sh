#!/usr/bin/env bash
# Build all ModularPlatform images for deployment.
#
# The four .NET hosts are PUBLISHED via a bind-mounted SDK container (NOT compiled inside a Docker image layer):
# on overlay filesystems the .NET 10 SDK false-positives the default recursive globs (**/*.cs, **/*.resx) as
# "drive-enumerating" and leaks the literal pattern to csc (CS2001/CS2021/MSB3552). A bind-mounted `dotnet publish`
# does not hit that. We then bake the published output into thin runtime images (Dockerfile.runtime, glob-free COPY).
# The web (Next.js) image has no MSBuild globs, so it builds normally via frontend/Dockerfile.
#
# Run from anywhere; resolves the repo root relative to this script. Requires Docker + docker compose v2.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

SDK_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0.102"
HOSTS=(
  ModularPlatform.MigrationService
  ModularPlatform.Api
  ModularPlatform.Worker
  ModularPlatform.Jobs
)

echo ">> Publishing .NET hosts via bind-mounted SDK (deterministic; sidesteps the overlay glob bug)"
for H in "${HOSTS[@]}"; do
  echo "   - $H"
  docker run --rm \
    -e MSBUILDFAILONDRIVEENUMERATINGWILDCARD=0 \
    -v "$REPO_ROOT":/src \
    -v mp-nuget:/root/.nuget/packages \
    -w /src \
    "$SDK_IMAGE" \
    dotnet publish "src/hosts/$H/$H.csproj" -c Release -o "/src/artifacts/$H"
done

echo ">> Building images (thin .NET runtime images + web)"
docker compose build

echo ">> Done. Images:"
docker images --format '{{.Repository}}:{{.Tag}}\t{{.Size}}' | grep -E '^mp-' | sort

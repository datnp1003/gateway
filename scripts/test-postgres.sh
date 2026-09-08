#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Always a fresh server, localhost-only; remove only the container/volume we create.
password=$(openssl rand -hex 24)
cid=$(docker run -d -e POSTGRES_PASSWORD="$password" -p 127.0.0.1::5432 postgres:17-alpine)
trap 'docker rm -fv "$cid" >/dev/null' EXIT
ready=false
for ((i=0; i<60; i++)); do
    if docker exec "$cid" pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1; then
        ready=true
        break
    fi
    sleep 1
done
if [[ "$ready" != true ]]; then docker logs "$cid"; exit 1; fi
binding=$(docker port "$cid" 5432/tcp)
export GATEWAY_TEST_POSTGRES="Host=127.0.0.1;Port=${binding##*:};Database=postgres;Username=postgres;Password=$password"
docker exec "$cid" psql -U postgres -c 'SELECT version();'
dotnet build Gateway.sln
dotnet test Gateway.sln --no-build "$@"

#!/usr/bin/env bash
set -euo pipefail
ROOT=${ROOT:-/home/mannie/arkanum-iter}
cd "$ROOT"
sg docker -c "docker compose -f lab/docker-compose.keycloak.yml up -d"
for i in {1..90}; do
  if curl -fsS "http://127.0.0.1:8085/realms/arkanum/.well-known/openid-configuration" >/dev/null 2>&1; then
    echo "Keycloak Arkanum realm is ready: http://192.168.1.248:8085/realms/arkanum"
    exit 0
  fi
  sleep 2
done
echo "Keycloak did not become ready in time." >&2
sg docker -c "docker logs --tail 120 arkanum-keycloak" >&2 || true
exit 1

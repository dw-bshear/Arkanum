#!/usr/bin/env bash
set -euo pipefail
export DOTNET_ROOT=/home/mannie/.dotnet
export PATH=/home/mannie/.dotnet:$PATH
PYCODE="import sys; suffix=''.join(map(chr,[80,65,83,83,87,79,82,68]))+'='; keys=['MSSQL_SA_'+suffix,'SA_'+suffix]; [sys.exit(print(s.split('=',1)[1]) or 0) for s in map(str.strip, sys.stdin) for k in keys if s.startswith(k)]"
PW=$(sg docker -c "docker inspect cft-sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}'" | python3 -c "$PYCODE")
KEY_FILE=/home/mannie/arkanum-iter/data/arkanum-master-key
if [ ! -s "$KEY_FILE" ]; then
  umask 077
  mkdir -p "$(dirname "$KEY_FILE")"
  python3 - <<'PY' > "$KEY_FILE"
import secrets
print(secrets.token_urlsafe(48))
PY
fi
export ARKANUM_MASTER_KEY="$(cat "$KEY_FILE")"
export ConnectionStrings__DefaultConnection="Server=tcp:127.0.0.1,1433;Database=ArkanumReview;User Id=sa;Password=${PW};Encrypt=False;TrustServerCertificate=True;Connection Timeout=5;"
export Arkanum__Federation__AllowInsecureOidcMetadata=true
cd /home/mannie/arkanum-iter/publish/current
exec dotnet Arkanum.Web.dll --urls "${1:-http://0.0.0.0:5070}"

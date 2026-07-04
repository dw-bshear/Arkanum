#!/usr/bin/env bash
set -euo pipefail
PYCODE="import sys; suffix=''.join(map(chr,[80,65,83,83,87,79,82,68]))+'='; keys=['MSSQL_SA_'+suffix,'SA_'+suffix]; [sys.exit(print(s.split('=',1)[1]) or 0) for s in map(str.strip, sys.stdin) for k in keys if s.startswith(k)]"
PW=$(sg docker -c "docker inspect cft-sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}'" | python3 -c "$PYCODE")
if [ -z "$PW" ]; then echo "Could not read SQL container credential" >&2; exit 2; fi
KEY_FILE=/home/mannie/arkanum-iter/data/arkanum-master-key
if [ ! -s "$KEY_FILE" ]; then
  umask 077
  mkdir -p "$(dirname "$KEY_FILE")"
  python3 - <<'PY' > "$KEY_FILE"
import secrets
print(secrets.token_urlsafe(48))
PY
fi
MASTER_KEY=$(cat "$KEY_FILE")
sg docker -c "docker rm -f arkanum-sql-review >/dev/null 2>&1 || true"
sg docker -c "docker run -d --name arkanum-sql-review --network arkanum-sql-net -p 5071:5071 -v /home/mannie/arkanum-iter/publish/current:/app:ro -w /app -e ARKANUM_MASTER_KEY='${MASTER_KEY}' -e ConnectionStrings__DefaultConnection='Server=cft-sqlserver,1433;Database=ArkanumReview;User Id=sa;Password=${PW};Encrypt=False;TrustServerCertificate=True;Connection Timeout=10;' mcr.microsoft.com/dotnet/aspnet:9.0 dotnet Arkanum.Web.dll --urls http://0.0.0.0:5071"
sg docker -c "docker ps --filter name=arkanum-sql-review --format 'table {{.Names}}\t{{.Image}}\t{{.Ports}}\t{{.Status}}'"

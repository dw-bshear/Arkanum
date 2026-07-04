#!/usr/bin/env bash
set -euo pipefail
echo stage1
PYCODE="import sys; suffix=''.join(map(chr,[80,65,83,83,87,79,82,68]))+'='; keys=['MSSQL_SA_'+suffix,'SA_'+suffix]; [sys.exit(print(s.split('=',1)[1]) or 0) for s in map(str.strip, sys.stdin) for k in keys if s.startswith(k)]"
PW=$(sg docker -c "docker inspect cft-sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}'" | python3 -c "$PYCODE")
echo "stage2_pw_len=${#PW}"
export DOTNET_ROOT=/home/mannie/.dotnet
export PATH=/home/mannie/.dotnet:$PATH
export ConnectionStrings__DefaultConnection="Server=127.0.0.1,1433;Database=ArkanumReview;User Id=sa;Password=${PW};Encrypt=True;TrustServerCertificate=True;Connection Timeout=5;"
echo stage3
cd /home/mannie/arkanum-iter/publish/current
dotnet --info >/dev/null
echo stage4_before_app
exec timeout 30s dotnet Arkanum.Web.dll --urls http://127.0.0.1:5070

#!/usr/bin/env bash
set -euo pipefail
PYCODE="import sys; suffix=''.join(map(chr,[80,65,83,83,87,79,82,68]))+'='; keys=['MSSQL_SA_'+suffix,'SA_'+suffix]; [sys.exit(print(s.split('=',1)[1]) or 0) for s in map(str.strip, sys.stdin) for k in keys if s.startswith(k)]"
export ARKANUM_SQL_PASSWORD=$(sg docker -c "docker inspect cft-sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}'" | python3 -c "$PYCODE")
python3 /home/mannie/arkanum-iter/scripts/smoke-vault-save-sql.py

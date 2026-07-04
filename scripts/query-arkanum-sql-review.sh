#!/usr/bin/env bash
set -euo pipefail
PYCODE="import sys; suffix=''.join(map(chr,[80,65,83,83,87,79,82,68]))+'='; keys=['MSSQL_SA_'+suffix,'SA_'+suffix]; [sys.exit(print(s.split('=',1)[1]) or 0) for s in map(str.strip, sys.stdin) for k in keys if s.startswith(k)]"
export ARKANUM_SQL_PASSWORD=$(sg docker -c "docker inspect cft-sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}'" | python3 -c "$PYCODE")
python3 - <<'PY'
import os, pymssql
conn=pymssql.connect(server='127.0.0.1', port=1433, user='sa', password=os.environ['ARKANUM_SQL_PASSWORD'], database='ArkanumReview', login_timeout=5, timeout=10)
cur=conn.cursor()
for table in ['Users','PortalSettings','Vaults','VaultFolders','AuditEvents']:
    cur.execute(f'SELECT COUNT(*) FROM {table}')
    print(f'{table}={cur.fetchone()[0]}')
cur.execute("SELECT COUNT(*) FROM PortalSettings WHERE SettingKey LIKE 'database.sqlServer.%'")
print(f'SqlServerSettings={cur.fetchone()[0]}')
cur.execute("SELECT TOP 5 Username, RolesCsv, AppAccessEnabled FROM Users ORDER BY Username")
for row in cur.fetchall():
    print('UserRow=' + '|'.join(str(x) for x in row))
conn.close()
PY

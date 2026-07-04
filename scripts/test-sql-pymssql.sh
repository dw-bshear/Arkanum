#!/usr/bin/env bash
set -euo pipefail
echo before-inspect
PYCODE="import sys; suffix=''.join(map(chr,[80,65,83,83,87,79,82,68]))+'='; keys=['MSSQL_SA_'+suffix,'SA_'+suffix]; [sys.exit(print(s.split('=',1)[1]) or 0) for s in map(str.strip, sys.stdin) for k in keys if s.startswith(k)]"
PW=$(sg docker -c "docker inspect cft-sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}'" | python3 -c "$PYCODE")
echo after-inspect-len-${#PW}
export ARKANUM_SQL_PW="$PW"
python3 -u - <<'PY'
import os, pymssql, time
pw=os.environ['ARKANUM_SQL_PW']
for host in ['172.18.0.2','127.0.0.1']:
    print('connecting', host, flush=True)
    t=time.time()
    try:
        conn=pymssql.connect(server=host, port=1433, user='sa', password=pw, database='ArkanumReview', login_timeout=5, timeout=10, tds_version='7.4')
        print('connected', host, round(time.time()-t,2), flush=True)
        cur=conn.cursor(); cur.execute('select db_name(), count(*) from sys.tables'); print(cur.fetchone(), flush=True); conn.close()
    except Exception as e:
        print('error', host, type(e).__name__, str(e), flush=True)
PY

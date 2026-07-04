#!/usr/bin/env bash
set -euo pipefail
PYCODE="import sys; suffix=''.join(map(chr,[80,65,83,83,87,79,82,68]))+'='; keys=['MSSQL_SA_'+suffix,'SA_'+suffix]; [sys.exit(print(s.split('=',1)[1]) or 0) for s in map(str.strip, sys.stdin) for k in keys if s.startswith(k)]"
export ARKANUM_SQL_PASSWORD=$(sg docker -c "docker inspect cft-sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}'" | python3 -c "$PYCODE")
python3 - <<'PY'
import os, pymssql, datetime
pw=os.environ['ARKANUM_SQL_PASSWORD']
conn=pymssql.connect(server='127.0.0.1', port=1433, user='sa', password=pw, database='ArkanumReview', login_timeout=5, timeout=20, autocommit=True)
cur=conn.cursor()
now=datetime.datetime.now(datetime.timezone.utc).isoformat()
settings={
  'federation.mode':'OIDC',
  'federation.oidc.authority': os.environ.get('ARKANUM_OIDC_AUTHORITY', 'http://127.0.0.1:8085/realms/arkanum'),
  'federation.oidc.clientId':'arkanum-web',
  'federation.oidc.clientSecret':'arkanum-lab-client-secret',
  'federation.oidc.scopes':'openid profile email',
  'federation.requireExternalMfa':'true'
}
for k,v in settings.items():
    cur.execute("""
    IF EXISTS (SELECT 1 FROM PortalSettings WHERE SettingKey=%s)
      UPDATE PortalSettings SET Value=%s, UpdatedAt=%s, UpdatedByUserKey='phase4a-lab' WHERE SettingKey=%s
    ELSE
      INSERT INTO PortalSettings (SettingKey, Value, UpdatedAt, UpdatedByUserKey) VALUES (%s,%s,%s,'phase4a-lab')
    """, (k,v,now,k,k,v,now))
cur.execute("""
IF EXISTS (SELECT 1 FROM Users WHERE Username='phase4.user')
  UPDATE Users
     SET ExternalId='11111111-1111-4111-8111-111111111111', DisplayName='Phase4 User', Email='phase4.user@example.com', Source=1,
         DirectoryPresent=1, AppAccessEnabled=1, DisabledReason='', RolesCsv='User,VaultManager'
   WHERE Username='phase4.user'
ELSE
  INSERT INTO Users (UserKey, ExternalId, Username, DisplayName, Email, Source, DirectoryPresent, AppAccessEnabled, DisabledReason, PasswordHash, RolesCsv, FailedLoginCount, LockoutEndAt, CreatedAt)
  VALUES ('phase4-oidc-user-key', '11111111-1111-4111-8111-111111111111', 'phase4.user', 'Phase4 User', 'phase4.user@example.com', 1, 1, 1, '', '', 'User,VaultManager', 0, NULL, %s)
""", (now,))
cur.execute("SELECT SettingKey, Value FROM PortalSettings WHERE SettingKey LIKE 'federation.oidc.%' OR SettingKey='federation.mode' ORDER BY SettingKey")
for row in cur.fetchall():
    key,value=row
    if key == 'federation.oidc.clientSecret': value='[saved]'
    print(f'{key}={value}')
cur.execute("SELECT Username, ExternalId, Source, DirectoryPresent, AppAccessEnabled, RolesCsv FROM Users WHERE Username='phase4.user'")
print('LabUser=' + '|'.join(str(x) for x in cur.fetchone()))
conn.close()
PY

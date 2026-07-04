#!/usr/bin/env bash
set -euo pipefail
PW=$(docker inspect cft-sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}' | sed -n 's/^MSSQL_SA_PASSWORD=//p; s/^SA_PASSWORD=//p' | head -1)
if [ -z "$PW" ]; then
  echo "No SQL Server SA password found in container environment" >&2
  exit 2
fi
cat > /tmp/arkanum-create-db.sql <<'SQL'
IF DB_ID('ArkanumReview') IS NULL
BEGIN
    CREATE DATABASE [ArkanumReview];
END;
SELECT name FROM sys.databases WHERE name IN ('ArkanumReview', 'FileTransferIntakePortal') ORDER BY name;
SQL
docker cp /tmp/arkanum-create-db.sql cft-sqlserver:/tmp/arkanum-create-db.sql >/dev/null
docker exec -e SQLCMDPASSWORD="$PW" cft-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -i /tmp/arkanum-create-db.sql -h -1 -W

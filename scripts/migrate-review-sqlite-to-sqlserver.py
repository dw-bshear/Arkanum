import os
import sqlite3
import pymssql
from pathlib import Path

sqlite_path = Path('/home/mannie/arkanum-iter/data/arkanum-review.db')
if not sqlite_path.exists():
    raise SystemExit(f'Missing SQLite review DB: {sqlite_path}')
password = os.environ.get('ARKANUM_SQL_PASSWORD')
if not password:
    raise SystemExit('ARKANUM_SQL_PASSWORD is required')

source = sqlite3.connect(sqlite_path)
source.row_factory = sqlite3.Row
target = pymssql.connect(server='127.0.0.1', port=1433, user='sa', password=password, database='ArkanumReview', login_timeout=10, timeout=30)

tables = [
    'PortalSettings',
    'DashboardWidgets',
    'Users',
    'Vaults',
    'VaultFolders',
    'VaultMembers',
    'UserGroups',
    'UserGroupMembers',
    'UserGroupManagers',
    'AuditEvents',
]

delete_order = list(reversed(tables))

def sql_columns(table):
    cur = target.cursor(as_dict=True)
    cur.execute("""
        SELECT COLUMN_NAME
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = %s
        ORDER BY ORDINAL_POSITION
    """, (table,))
    return [row['COLUMN_NAME'] for row in cur.fetchall()]

with target.cursor() as cur:
    for table in delete_order:
        cols = sql_columns(table)
        if cols:
            cur.execute(f'DELETE FROM [{table}]')
    target.commit()

summary = []
with target.cursor() as cur:
    for table in tables:
        cols = sql_columns(table)
        if not cols:
            summary.append((table, 'missing', 0))
            continue
        source_cols = [row[1] for row in source.execute(f'PRAGMA table_info({table})').fetchall()]
        common = [c for c in cols if c in source_cols]
        rows = source.execute(f"SELECT {', '.join(common)} FROM {table}").fetchall()
        if rows:
            col_sql = ', '.join(f'[{c}]' for c in common)
            placeholders = ', '.join(['%s'] * len(common))
            sql = f'INSERT INTO [{table}] ({col_sql}) VALUES ({placeholders})'
            values = []
            for row in rows:
                vals = []
                for c in common:
                    v = row[c]
                    if c in {'Enabled','DirectoryPresent','AppAccessEnabled','CanInvite'} and v is not None:
                        v = bool(v)
                    vals.append(v)
                values.append(tuple(vals))
            cur.executemany(sql, values)
        summary.append((table, 'copied', len(rows)))
    target.commit()

for table, status, count in summary:
    print(f'{table}: {status} {count}')

import base64
import hashlib
import os
import re
import secrets
import uuid
from datetime import datetime, timezone

import pymssql
import requests

BASE = 'http://127.0.0.1:5068'
PASSWORD = 'SmokeVaultSave!2026'
USERNAME = 'smoke-vault-save'
USER_KEY = 'smoke-vault-save-user'
VAULT_ID = str(uuid.uuid4())
FOLDER_ID = str(uuid.uuid4())
NOW = datetime.now(timezone.utc)
SQL_PASSWORD = os.environ['ARKANUM_SQL_PASSWORD']

def hash_password(password: str) -> str:
    salt = secrets.token_bytes(16)
    key = hashlib.pbkdf2_hmac('sha256', password.encode(), salt, 210_000, dklen=32)
    return f"pbkdf2-sha256$210000${base64.b64encode(salt).decode()}${base64.b64encode(key).decode()}"

conn = pymssql.connect(server='127.0.0.1', port=1433, user='sa', password=SQL_PASSWORD, database='ArkanumReview', login_timeout=5, timeout=20)
try:
    with conn.cursor() as cur:
        cur.execute("DELETE FROM VaultItems WHERE Title LIKE 'Smoke SQL %'")
        cur.execute("DELETE FROM VaultMembers WHERE VaultId IN (SELECT Id FROM Vaults WHERE Name LIKE 'Smoke SQL Vault%') OR UserKey=%s", (USER_KEY,))
        cur.execute("DELETE FROM VaultFolders WHERE VaultId IN (SELECT Id FROM Vaults WHERE Name LIKE 'Smoke SQL Vault%')")
        cur.execute("DELETE FROM Vaults WHERE Name LIKE 'Smoke SQL Vault%'")
        cur.execute("DELETE FROM Users WHERE UserKey=%s OR Username=%s", (USER_KEY, USERNAME))
        cur.execute("INSERT INTO Users (UserKey, ExternalId, Username, DisplayName, Email, Source, DirectoryPresent, AppAccessEnabled, DisabledReason, PasswordHash, RolesCsv, CreatedAt) VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)", (USER_KEY, None, USERNAME, 'Smoke Vault Save', 'smoke-vault-save@email.com', 0, False, True, '', hash_password(PASSWORD), 'User', NOW))
        cur.execute("INSERT INTO Vaults (Id, Name, Description, CreatedByUserKey, CreatedAt) VALUES (%s,%s,%s,%s,%s)", (VAULT_ID, 'Smoke SQL Vault', 'Temporary SQL-backed vault save smoke test', USER_KEY, NOW))
        cur.execute("INSERT INTO VaultFolders (Id, VaultId, Name, CreatedAt) VALUES (%s,%s,%s,%s)", (FOLDER_ID, VAULT_ID, 'General', NOW))
        cur.execute("INSERT INTO VaultMembers (VaultId, UserKey, CanInvite, AddedByUserKey, AddedAt) VALUES (%s,%s,%s,%s,%s)", (VAULT_ID, USER_KEY, True, USER_KEY, NOW))
        conn.commit()

    s = requests.Session()
    login = s.get(f'{BASE}/Login', timeout=10)
    login.raise_for_status()
    token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', login.text)
    if not token:
        token = re.search(r'type="hidden" name="__RequestVerificationToken" value="([^"]+)"', login.text)
    if not token:
        raise RuntimeError('Could not find login antiforgery token')
    resp = s.post(f'{BASE}/Login', data={'Username': USERNAME, 'Password': PASSWORD, '__RequestVerificationToken': token.group(1)}, allow_redirects=True, timeout=10)
    print('login', resp.status_code, resp.url)
    if 'Sign in to Arkanum' in resp.text:
        raise RuntimeError('Login did not authenticate smoke user')

    detail_url = f'{BASE}/Vaults/Details/{VAULT_ID}'
    detail = s.get(detail_url, timeout=10)
    print('detail', detail.status_code, 'bytes', len(detail.text))
    detail.raise_for_status()
    token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', detail.text) or re.search(r'type="hidden" name="__RequestVerificationToken" value="([^"]+)"', detail.text)
    if not token:
        raise RuntimeError('Could not find vault antiforgery token')
    token = token.group(1)

    cred = s.post(f'{BASE}/Vaults/Details/{VAULT_ID}?handler=CreateLogin', data={'FolderId': FOLDER_ID, 'Title': 'Smoke SQL Credential', 'Username': 'smoke@example.com', 'SecretValue': 'temporary-secret', '__RequestVerificationToken': token}, allow_redirects=True, timeout=10)
    print('credential_post', cred.status_code, cred.url, 'error_page', 'Development Mode' in cred.text or 'An error occurred' in cred.text)
    if cred.status_code >= 500 or 'Development Mode' in cred.text or 'An error occurred' in cred.text:
        raise RuntimeError('Credential save still returns error page')

    detail2 = s.get(f'{BASE}/Vaults/Details/{VAULT_ID}', timeout=10)
    token2 = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', detail2.text) or re.search(r'type="hidden" name="__RequestVerificationToken" value="([^"]+)"', detail2.text)
    if not token2:
        raise RuntimeError('Could not find second vault antiforgery token')
    note = s.post(f'{BASE}/Vaults/Details/{VAULT_ID}?handler=CreateNote', data={'FolderId': FOLDER_ID, 'Title': 'Smoke SQL Note', 'SecretValue': 'temporary note body', '__RequestVerificationToken': token2.group(1)}, allow_redirects=True, timeout=10)
    print('note_post', note.status_code, note.url, 'error_page', 'Development Mode' in note.text or 'An error occurred' in note.text)
    if note.status_code >= 500 or 'Development Mode' in note.text or 'An error occurred' in note.text:
        raise RuntimeError('Note save still returns error page')

    with conn.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM VaultItems WHERE VaultId=%s AND Title IN ('Smoke SQL Credential','Smoke SQL Note')", (VAULT_ID,))
        count = cur.fetchone()[0]
        print('saved_items', count)
        if count != 2:
            raise RuntimeError(f'Expected 2 saved items, got {count}')
finally:
    with conn.cursor() as cur:
        cur.execute("DELETE FROM VaultItems WHERE VaultId=%s", (VAULT_ID,))
        cur.execute("DELETE FROM VaultMembers WHERE VaultId=%s OR UserKey=%s", (VAULT_ID, USER_KEY))
        cur.execute("DELETE FROM VaultFolders WHERE VaultId=%s", (VAULT_ID,))
        cur.execute("DELETE FROM Vaults WHERE Id=%s", (VAULT_ID,))
        cur.execute("DELETE FROM Users WHERE UserKey=%s", (USER_KEY,))
        conn.commit()
    conn.close()

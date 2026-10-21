"""Opt-in credential history upgrade/retention test on disposable SQL Server.

Requires Docker with Microsoft SQL Server image, requests, and a published app.
Does not connect to an existing server, read credentials, or print secret data.
"""
import argparse
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import tempfile
import time
import uuid
import requests


def main(publish, dotnet):
    name = 'arkanum-history-smoke-' + uuid.uuid4().hex[:12]
    password = secrets.token_urlsafe(30) + '!Aa1'
    runtime_password = secrets.token_urlsafe(30) + '!Aa1'
    env = dict(os.environ, MSSQL_SA_PASSWORD=password, SQLCMDPASSWORD=password)
    web = None
    created = False

    def run(argv, child_env=env, check=True):
        result = subprocess.run(argv, env=child_env, capture_output=True, text=True, timeout=120)
        if check and result.returncode:
            raise RuntimeError('Disposable test command failed (output suppressed): ' + argv[0])
        return result

    def query(sql):
        return run(['docker', 'exec', '-e', 'SQLCMDPASSWORD', name, '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-b', '-d', 'HistoryProbe', '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + sql]).stdout.strip()

    try:
        run(['docker', 'run', '-d', '--name', name, '--label', 'arkanum.disposable-test=true', '-e', 'ACCEPT_EULA=Y', '-e', 'MSSQL_PID=Developer', '-e', 'MSSQL_SA_PASSWORD', '-p', '127.0.0.1::1433', 'mcr.microsoft.com/mssql/server:2022-latest'])
        created = True
        port = run(['docker', 'port', name, '1433/tcp']).stdout.strip().rsplit(':', 1)[1]
        for _ in range(60):
            result = run(['docker', 'exec', '-e', 'SQLCMDPASSWORD', name, '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-b', '-Q', 'SELECT 1'], check=False)
            if result.returncode == 0:
                break
            time.sleep(2)
        else:
            raise RuntimeError('Disposable SQL readiness timed out')
        endpoint = f'Server=127.0.0.1,{port};Encrypt=True;TrustServerCertificate=True;Connection Timeout=5;'
        setup_env = {k:v for k,v in env.items() if not k.endswith('_FILE') and not k.startswith('ARKANUM_MASTER_KEY')}
        setup_env.update(ARKANUM_SETUP_CONNECTION=endpoint+'Database=master;User ID=sa;Password='+password,
                         ARKANUM_RUNTIME_CONNECTION=endpoint+'Database=HistoryProbe;User ID=history_probe;Password='+runtime_password,
                         ARKANUM_RUNTIME_LOGIN='history_probe', ARKANUM_RUNTIME_PASSWORD=runtime_password)
        def setup(mode, success=True):
            result = run([dotnet, str(publish/'Arkanum.Web.dll'), '--database-setup', mode], child_env=setup_env, check=False)
            assert (result.returncode == 0) == success, 'Unexpected setup/preflight result'
        setup('provision')
        setup('preflight')
        with tempfile.TemporaryDirectory(prefix='history-sql-', dir=os.environ['TMPDIR']) as tmp:
            with socket.socket() as sock:
                sock.bind(('127.0.0.1', 0)); web_port = sock.getsockname()[1]
            base = f'http://127.0.0.1:{web_port}'
            web_env = {k:v for k,v in env.items() if not k.endswith('_FILE') and not k.startswith('ARKANUM_MASTER_KEY')}
            web_env.update(ConnectionStrings__DefaultConnection=setup_env['ARKANUM_RUNTIME_CONNECTION'],
                           ARKANUM_MASTER_KEY=secrets.token_urlsafe(48), ASPNETCORE_URLS=base,
                           ASPNETCORE_ENVIRONMENT='Development', DOTNET_ENVIRONMENT='Development',
                           Arkanum__CookieSecurePolicy='None', HOME=tmp, Logging__LogLevel__Default='Warning')
            log = open(Path(tmp)/'host.log', 'w')
            def start():
                nonlocal web
                web = subprocess.Popen([dotnet,str(publish/'Arkanum.Web.dll')], cwd=publish, env=web_env, stdout=log, stderr=subprocess.STDOUT)
                for _ in range(100):
                    assert web.poll() is None, 'Disposable web startup failed'
                    try:
                        if requests.get(base+'/health', timeout=1).status_code == 200:
                            return
                    except requests.RequestException:
                        pass
                    time.sleep(.1)
                raise RuntimeError('Web readiness timeout')
            session = requests.Session()
            def post(path, data):
                response = session.get(base+path.split('?')[0], timeout=10)
                token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"',response.text)
                assert token, 'Missing authenticated antiforgery form'
                return session.post(base+path, data=dict(data,__RequestVerificationToken=token.group(1)), allow_redirects=False, timeout=15)
            start()
            login_password = secrets.token_urlsafe(24)
            assert post('/Setup',dict(Username='history-admin',Password=login_password,ConfirmPassword=login_password,RecoveryKeySavedAcknowledged='true')).status_code == 200
            assert post('/ManualLogin',dict(Username='history-admin',Password=login_password)).status_code == 302
            assert post('/Vaults?handler=Create',dict(Name='History probe',Description='Disposable SQL history verification')).status_code == 302
            vault = query('SELECT CONVERT(varchar(36),Id) FROM Vaults;')
            folder = query('SELECT CONVERT(varchar(36),Id) FROM VaultFolders;')
            path = '/Vaults/Details/'+vault
            original = secrets.token_urlsafe(24)
            assert post(path+'?handler=CreateLogin',dict(FolderId=folder,Title='Probe credential',Username='probe',SecretValue=original)).status_code == 302
            item = query('SELECT CONVERT(varchar(36),Id) FROM VaultItems;')
            # Simulate an existing pre-history database without changing current credential bytes.
            assert web is not None
            web.terminate(); web.wait(timeout=10); web = None
            before = query("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',EncryptedSecret),2) FROM VaultItems;")
            query('DROP TABLE CredentialVersions;')
            setup('preflight', success=False)
            setup('provision'); setup('provision'); setup('preflight')
            assert before == query("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',EncryptedSecret),2) FROM VaultItems;")
            start()
            assert post('/ManualLogin',dict(Username='history-admin',Password=login_password)).status_code == 302
            first = secrets.token_urlsafe(24)
            assert post(path+'?handler=EditCredential',dict(ItemId=item,FolderId=folder,Title='First edit',Username='probe',SecretValue=first)).status_code == 302
            assert query('SELECT COUNT(*) FROM CredentialVersions;') == '2'
            old_version = query('SELECT CONVERT(varchar(36),Id) FROM CredentialVersions WHERE Revision=1;')
            response = post(path+'?handler=RevealHistory',dict(ItemId=item,VersionId=old_version))
            assert response.status_code == 200 and original in response.text and 'no-store' in response.headers['Cache-Control']
            for n in range(2,8):
                assert post(path+'?handler=EditCredential',dict(ItemId=item,FolderId=folder,Title=f'Edit {n}',Username='probe',SecretValue=secrets.token_urlsafe(24))).status_code == 302
            assert query('SELECT COUNT(*) FROM CredentialVersions;') == '5'
            assert query('SELECT STRING_AGG(CONVERT(varchar(20),Revision),\',\') WITHIN GROUP (ORDER BY Revision DESC) FROM CredentialVersions;') == '8,7,6,5,4'
            response = post(path+'?handler=History',dict(ItemId=item))
            assert response.status_code == 200 and response.text.count('Reveal saved version') == 5
            assert original not in response.text
            # Successful prune/edit establishes runtime INSERT/UPDATE/DELETE grants.
            assert web is not None
            web.terminate(); web.wait(timeout=10); web = None
            log.close()
        print('PASS: SQL Server additive upgrade is idempotent, detects missing schema, preserves ciphertext; limited runtime login edits/reveals/prunes five encrypted versions; disposable container cleaned on exit.')
    finally:
        if web and web.poll() is None:
            web.kill(); web.wait(timeout=10)
        if created:
            run(['docker','rm','-f','-v',name])
            assert not run(['docker','ps','-a','--filter','name=^/'+name+'$','--format','{{.ID}}']).stdout.strip(), 'Container cleanup failed'


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish',type=Path,required=True)
    parser.add_argument('--dotnet',default='dotnet')
    args=parser.parse_args()
    main(args.publish.resolve(),args.dotnet)

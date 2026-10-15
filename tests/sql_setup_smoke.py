#!/usr/bin/env python3
"""Opt-in integration smoke using a disposable SQL Server 2022 Developer container.

Requires Docker, a .NET 10 host, and a real Linux publish directory. Never reuses
an existing database/container or reads host credentials. No secret output.
"""
import argparse
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import urllib.request
import uuid


def run(args, *, env=None, check=True, timeout=120):
    result = subprocess.run(args, env=env, text=True, capture_output=True, timeout=timeout)
    if check and result.returncode:
        raise RuntimeError('Command failed: ' + args[0] + ' (output suppressed to protect credentials)')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish', required=True, type=Path)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    app = args.publish.resolve() / 'Arkanum.Web.dll'
    if not app.is_file():
        raise SystemExit('Publish Arkanum for Linux before running this test.')
    name = 'arkanum-setup-smoke-' + uuid.uuid4().hex[:12]
    admin_password = secrets.token_urlsafe(30) + '!Aa1'
    runtime_password = secrets.token_urlsafe(30) + '!Aa1'
    container_env = os.environ.copy()
    container_env['MSSQL_SA_PASSWORD'] = admin_password
    web = None
    created = False
    try:
        run(['docker', 'run', '-d', '--name', name, '--label', 'arkanum.disposable-test=true', '-e', 'ACCEPT_EULA=Y', '-e', 'MSSQL_PID=Developer', '-e', 'MSSQL_SA_PASSWORD', '-p', '127.0.0.1::1433', 'mcr.microsoft.com/mssql/server:2022-latest'], env=container_env)
        created = True
        port = run(['docker', 'port', name, '1433/tcp']).stdout.strip().rsplit(':', 1)[1]
        sql_env = os.environ.copy()
        sql_env['SQLCMDPASSWORD'] = admin_password
        deadline = time.monotonic() + 120
        while True:
            ready = run(['docker', 'exec', '-e', 'SQLCMDPASSWORD', name, '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-b', '-Q', 'SELECT 1'], env=sql_env, check=False, timeout=15)
            if ready.returncode == 0:
                break
            if time.monotonic() >= deadline:
                raise RuntimeError('Disposable SQL Server readiness timed out.')
            time.sleep(2)
        endpoint = 'Server=127.0.0.1,' + port + ';Encrypt=True;TrustServerCertificate=True;Connection Timeout=5;'
        # Trust bypass is isolated to this throwaway localhost test certificate.
        env = os.environ.copy()
        for key in ('ARKANUM_MASTER_KEY_FILE', 'ARKANUM_MASTER_KEY', 'ConnectionStrings__DefaultConnection_FILE', 'ConnectionStrings__DefaultConnection'):
            env.pop(key, None)
        env.update(ARKANUM_SETUP_CONNECTION=endpoint + 'Database=master;User ID=sa;Password=' + admin_password,
                   ARKANUM_RUNTIME_CONNECTION=endpoint + 'Database=ArkanumSetupSmoke;User ID=arkanum_smoke;Password=' + runtime_password,
                   ARKANUM_RUNTIME_LOGIN='arkanum_smoke', ARKANUM_RUNTIME_PASSWORD=runtime_password)
        def setup(mode, extra=None, success=True):
            child = env.copy()
            if extra:
                child.update(extra)
            result = run([args.dotnet, str(app), '--database-setup', mode], env=child, check=False)
            if (result.returncode == 0) != success:
                raise RuntimeError('Database setup smoke unexpected result: ' + mode + ' code=' + str(result.returncode) + ' (details suppressed)')
            for value in (admin_password, runtime_password, env['ARKANUM_SETUP_CONNECTION'], env['ARKANUM_RUNTIME_CONNECTION']):
                if value in result.stdout + result.stderr:
                    raise RuntimeError('Database setup exposed a secret.')
            return result
        setup('provision')
        print('PASS: actual SQL schema/login provisioning')
        setup('preflight')
        print('PASS: actual schema and least-privilege runtime preflight')
        setup('provision', {'ARKANUM_RUNTIME_PASSWORD': 'WrongPassword!Aa123', 'ARKANUM_RUNTIME_CONNECTION': endpoint + 'Database=ArkanumSetupSmoke;User ID=arkanum_smoke;Password=WrongPassword!Aa123'}, success=False)
        print('PASS: existing-login password mismatch fails')
        with tempfile.TemporaryDirectory(prefix='arkanum-setup-smoke-') as scratch:
            keyfile = Path(scratch) / 'test-recovery-key.txt'
            keyfile.write_text('ark-recovery-v1.' + secrets.token_urlsafe(48))
            keyfile.chmod(0o600)
            setup('preflight', {'ARKANUM_MASTER_KEY_FILE': str(keyfile)}, success=False)
            print('PASS: restore without existing sentinel fails closed')
            with socket.socket() as listener:
                listener.bind(('127.0.0.1', 0))
                web_port = listener.getsockname()[1]
            web_env = env.copy()
            web_env.update(ConnectionStrings__DefaultConnection=env['ARKANUM_RUNTIME_CONNECTION'], ARKANUM_MASTER_KEY_FILE=str(keyfile), ASPNETCORE_ENVIRONMENT='Production')
            web_env.pop('ARKANUM_SETUP_CONNECTION', None)
            # Runtime log is confined to disposable scratch, never printed.
            with (Path(scratch) / 'runtime.log').open('w') as log:
                web = subprocess.Popen([args.dotnet, str(app), '--urls', 'http://127.0.0.1:' + str(web_port)], cwd=scratch, env=web_env, stdout=log, stderr=log)
                deadline = time.monotonic() + 40
                while True:
                    if web.poll() is not None:
                        raise RuntimeError('Disposable SQL-backed web startup failed (log suppressed).')
                    try:
                        with urllib.request.urlopen('http://127.0.0.1:' + str(web_port) + '/health', timeout=2) as response:
                            if json.load(response).get('status') == 'Alive':
                                break
                    except Exception:
                        pass
                    if time.monotonic() >= deadline:
                        raise RuntimeError('Disposable SQL-backed web health timed out.')
                    time.sleep(0.5)
                setup('preflight', {'ARKANUM_MASTER_KEY_FILE': str(keyfile)})
                print('PASS: SQL-backed web startup/liveness and correct Recovery Key sentinel')
                wrong = Path(scratch) / 'wrong-key.txt'
                wrong.write_text('ark-recovery-v1.' + secrets.token_urlsafe(48))
                wrong.chmod(0o600)
                setup('preflight', {'ARKANUM_MASTER_KEY_FILE': str(wrong)}, success=False)
                print('PASS: incorrect Recovery Key rejected independently of web liveness')
                web.terminate()
                web.wait(timeout=15)
                web = None
        print('SQL setup smoke passed; disposable resources cleaned on exit.')
    finally:
        if web and web.poll() is None:
            web.kill()
            web.wait(timeout=15)
        if created:
            run(['docker', 'rm', '-f', '-v', name])
            if run(['docker', 'ps', '-a', '--filter', 'name=^/' + name + '$', '--format', '{{.ID}}']).stdout.strip():
                raise RuntimeError('Disposable test container cleanup verification failed.')


if __name__ == '__main__':
    main()

"""Explicit opt-in smoke of a published app, disposable SQLite and loopback SMTP.

Run: python3 tests/feature_110_smoke.py --publish /absolute/publish/directory
Never points at an existing database or mail server. Requires requests.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import socket
import socketserver
import sqlite3
import subprocess
import tempfile
import threading
import time
from email import policy
from email.parser import BytesParser
import requests


class SmtpCapture(socketserver.StreamRequestHandler):
    messages = []

    def handle(self):
        self.connection.settimeout(15)
        self.wfile.write(b'220 localhost disposable SMTP\r\n')
        payload = []
        in_data = False
        while line := self.rfile.readline():
            if in_data:
                if line == b'.\r\n':
                    self.messages.append(BytesParser(policy=policy.default).parsebytes(b''.join(payload)))
                    in_data = False
                    self.wfile.write(b'250 accepted\r\n')
                else:
                    payload.append(line[1:] if line.startswith(b'..') else line)
            elif line.upper().startswith(b'DATA'):
                in_data = True
                self.wfile.write(b'354 continue\r\n')
            elif line.upper().startswith(b'QUIT'):
                self.wfile.write(b'221 goodbye\r\n')
                break
            else:
                self.wfile.write(b'250 localhost\r\n')


def run(publish, dotnet):
    scratch = os.environ.get('TMPDIR', str(Path.home() / '.hermes/cache/scratch'))
    with tempfile.TemporaryDirectory(prefix='arkanum-110-smoke-', dir=scratch) as directory:
        root = Path(directory)
        with socket.socket() as listener:
            listener.bind(('127.0.0.1', 0))
            port = listener.getsockname()[1]
        base = f'http://127.0.0.1:{port}'
        env = dict(os.environ, DOTNET_ENVIRONMENT='Development', ASPNETCORE_ENVIRONMENT='Development',
                   ASPNETCORE_URLS=base, ConnectionStrings__DefaultConnection='',
                   ConnectionStrings__SqliteFallback=f'Data Source={root / "probe.db"}',
                   ARKANUM_MASTER_KEY=secrets.token_urlsafe(48), HOME=str(root), Arkanum__CookieSecurePolicy='None',
                   Logging__LogLevel__Default='Warning', Logging__LogLevel__Microsoft='Warning')
        for key in list(env):
            if key.endswith('_FILE') or key.startswith('ARKANUM_SEED'):
                del env[key]
        smtp = socketserver.ThreadingTCPServer(('127.0.0.1', 0), SmtpCapture)
        smtp.daemon_threads = True
        threading.Thread(target=smtp.serve_forever, daemon=True).start()
        SmtpCapture.messages = []
        password = secrets.token_urlsafe(24)
        admin, user, anonymous = requests.Session(), requests.Session(), requests.Session()
        checks = []

        def token(response):
            match = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', response.text)
            assert match, f'Missing antiforgery token: status={response.status_code}, path={response.url.split(base)[-1]}, title={re.findall(r"<title>(.*?)</title>", response.text)}'
            return match.group(1)

        def post(session, path, data, source=None):
            page = session.get(base + (source or path.split('?')[0]), timeout=10)
            return session.post(base + path, data=dict(data, __RequestVerificationToken=token(page)), timeout=15, allow_redirects=False)

        log = (root / 'host.log').open('w+')
        process = subprocess.Popen([dotnet, str(publish / 'Arkanum.Web.dll')], cwd=publish, env=env, stdout=log, stderr=subprocess.STDOUT)
        try:
            for _ in range(100):
                assert process.poll() is None, 'Host exited'
                try:
                    if anonymous.get(base + '/health', timeout=1).status_code == 200:
                        break
                except requests.RequestException:
                    pass
                time.sleep(.1)
            else:
                raise AssertionError('Host startup timeout')
            response = post(admin, '/Setup', dict(Username='probe-admin', Password=password, ConfirmPassword=password, RecoveryKeySavedAcknowledged='true'))
            assert response.status_code == 200 and 'Setup complete' in response.text
            assert post(admin, '/ManualLogin', dict(Username='probe-admin', Password=password)).status_code == 302
            db = sqlite3.connect(root / 'probe.db')
            # A separate real account exercises user HTML rather than assuming admin output is private.
            salt = secrets.token_bytes(16)
            digest = hashlib.pbkdf2_hmac('sha256', password.encode(), salt, 210000, 32)
            encoded = f'pbkdf2-sha256$210000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}'
            db.execute('INSERT INTO Users (UserKey,Username,DisplayName,Email,Source,DirectoryPresent,AppAccessEnabled,DisabledReason,PasswordHash,RolesCsv,CreatedAt,FailedLoginCount) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)',
                       ('probe-user', 'probe-user', 'Probe User', 'user@email.com', 0, 1, 1, '', encoded, 'User', '2026-10-21 00:00:00+00:00', 0))
            db.commit()
            assert post(user, '/ManualLogin', dict(Username='probe-user', Password=password)).status_code == 302
            assert 'data-feedback-open' not in user.get(base + '/', timeout=10).text
            assert 'data-feedback-open' not in anonymous.get(base + '/', timeout=10).text
            checks.append('feedback disabled by default and absent anonymously')
            invalid = post(admin, '/Vaults?handler=Create', dict(Name='Blank description probe', Description='  '))
            assert invalid.status_code == 200 and ('Vault description is required.' in invalid.text or 'The Description field is required.' in invalid.text), (invalid.status_code, re.findall(r'<title>(.*?)</title>', invalid.text))
            assert db.execute('SELECT COUNT(*) FROM Vaults WHERE Name=?', ('Blank description probe',)).fetchone()[0] == 0
            assert "Probe User&#x27;s personal vault. Invite other users if you want to share your entries." in user.get(base + '/Vaults', timeout=10).text
            checks.append('required description and personalized automatic vault')
            settings = admin.get(base + '/Settings', timeout=10)
            assert settings.status_code == 200 and 'while we build the control surface' not in settings.text
            assert post(admin, '/Settings?handler=Mail', dict(MailSmtpHost='127.0.0.1', MailSmtpPort=str(smtp.server_address[1]), MailFromAddress='sender@email.com', MailFromName='Arkanum probe')).status_code == 302
            recipient = 'private-feedback@email.com'
            assert post(admin, '/Settings?handler=Feedback', dict(FeedbackEnabled='true', FeedbackRecipient=recipient, FeedbackQuestions='What happened?\nHow can we improve?')).status_code == 302
            assert dict(db.execute('SELECT SettingKey, Value FROM PortalSettings'))['feedback.recipient'] == recipient
            body = user.get(base + '/', timeout=10)
            assert 'data-feedback-open' in body.text and 'What happened?' in body.text and recipient not in body.text
            assert 'maxlength="1000"' in body.text
            assert post(user, '/Settings?handler=Feedback', dict(FeedbackEnabled='false', FeedbackRecipient='forged@email.com'), source='/').status_code in (302, 403)
            assert dict(db.execute('SELECT SettingKey, Value FROM PortalSettings'))['feedback.recipient'] == recipient
            assert anonymous.post(base + '/Feedback', json={'text':'test','answers':[],'screenshots':[]}, timeout=10, allow_redirects=False).status_code in (302, 400, 401)
            assert user.post(base + '/Feedback', json={'text':'test','answers':[],'screenshots':[]}, timeout=10).status_code == 400
            checks.append('admin-only settings, recipient HTML secrecy, authentication and antiforgery')
            png = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ1sAAAAASUVORK5CYII=')
            payload = dict(text='Disposable feedback smoke', answers=['The button works', 'Keep it simple'], screenshots=[dict(name='../../probe.png', data=base64.b64encode(png).decode())], recipient='forged@email.com')
            response = user.post(base + '/Feedback', json=payload, headers={'X-CSRF-TOKEN':token(body)}, timeout=15)
            assert response.status_code == 200 and response.json()['success'], (response.status_code, response.text)
            assert recipient not in response.text
            assert len(SmtpCapture.messages) == 1
            message = SmtpCapture.messages[0]
            assert str(message['To']) == recipient
            assert 'Disposable feedback smoke' in message.get_body(preferencelist=('plain',)).get_content()
            attachment = list(message.iter_attachments())[0]
            assert attachment.get_filename() == 'screenshot-1.png' and attachment.get_payload(decode=True) == png
            checks.append('real HTTP to SMTP MIME delivery with exact screenshot bytes and server-chosen recipient')
            again = user.post(base + '/Feedback', json=payload, headers={'X-CSRF-TOKEN':token(body)}, timeout=15)
            assert again.status_code == 400 and not again.json()['success'] and len(SmtpCapture.messages) == 1
            # A different authenticated account avoids cooldown hiding the independent length assertion.
            admin_page = admin.get(base + '/', timeout=10)
            oversized = admin.post(base + '/Feedback', json=dict(text='x'*1001, answers=[], screenshots=[]), headers={'X-CSRF-TOKEN':token(admin_page)}, timeout=10)
            assert oversized.status_code == 400 and '1000' in oversized.text and len(SmtpCapture.messages) == 1
            checks.append('cooldown and server-side 1000-character limit')
            assert post(admin, '/Settings?handler=Feedback', dict(FeedbackEnabled='false', FeedbackRecipient=recipient, FeedbackQuestions='')).status_code == 302
            assert 'data-feedback-open' not in user.get(base + '/', timeout=10).text
            disabled = user.post(base + '/Feedback', json=payload, headers={'X-CSRF-TOKEN':token(body)}, timeout=10)
            assert disabled.status_code == 400 and len(SmtpCapture.messages) == 1
            checks.append('disabled state hides button and rejects stale form')
            for asset in ('/css/feedback.css', '/js/feedback.js'):
                response = anonymous.get(base + asset, timeout=10)
                assert response.status_code == 200 and len(response.content) > 100
            db.close()
            print(json.dumps(dict(result='PASS', artifact=str(publish), datastore='disposable SQLite', checks=checks)))
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
            log.close()
            smtp.shutdown()
            smtp.server_close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    run(args.publish.resolve(), args.dotnet)

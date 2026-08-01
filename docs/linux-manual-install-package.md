# Arkanum Linux Manual Install Package

This package is intended for a manual installation rehearsal on a controlled or air-gapped Linux host. It contains a published Arkanum web application, Linux installer scripts, documentation, and checksums. It does **not** contain site secrets, SQL passwords, a database backup, or a recovery key.

## 1. Verify package integrity

From the directory containing the archive and checksum file:

```bash
sha256sum -c Arkanum-linux-manual-test.tar.gz.sha256
```

After extraction, from the package root:

```bash
sha256sum -c SHA256SUMS.txt
```

## 2. Verify prerequisites on the target machine

```bash
dotnet --list-runtimes
systemctl --version
python3 --version
curl --version
rsync --version
```

Required runtime:

```text
Microsoft.AspNetCore.App 9.x
Microsoft.NETCore.App 9.x
```

## 3. Prepare SQL Server

Arkanum Phase 1 Linux installs require SQL Server. Use either a reachable SQL Server instance or a lab SQL Server on the test machine.

Create/identify:

- database name, for example `Arkanum`;
- SQL login/user for the application;
- connection string available only during install.

Example connection string shape:

```text
Server=tcp:sql01,1433;Database=Arkanum;User Id=arkanum_app;Password=...;Encrypt=True;TrustServerCertificate=False;
```

For a local lab SQL Server without a trusted certificate, use an isolated development/test-only connection string and never reuse it as a production example:

```text
Server=tcp:127.0.0.1,1433;Database=Arkanum;User Id=sa;Password=...;Encrypt=False;TrustServerCertificate=False;
```

## 4. Install Arkanum

From the extracted package root:

```bash
sudo env \
  ARKANUM_SQL_CONNECTION='Server=tcp:<sql-host>,1433;Database=Arkanum;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;' \
  APP_URLS='https://127.0.0.1:5068' \
  ARKANUM_HTTPS_CERTIFICATE_PATH='/etc/arkanum/tls/arkanum.pfx' \
  installers/linux/install-arkanum.sh
```

When `APP_URLS` uses `https://`, provide a trusted Kestrel PFX certificate with `ARKANUM_HTTPS_CERTIFICATE_PATH` and, when required, `ARKANUM_HTTPS_CERTIFICATE_PASSWORD`. If TLS terminates at a reverse proxy, bind Arkanum only to an internal URL such as `http://127.0.0.1:5068` behind that proxy.

To make Arkanum reachable from another machine on the test network, use a trusted certificate for the service name and bind HTTPS to all interfaces:

```bash
APP_URLS='https://0.0.0.0:5068'
ARKANUM_HTTPS_CERTIFICATE_PATH='/etc/arkanum/tls/arkanum.pfx'
```

Then connect to:

```text
https://<ubuntu-test-machine-name>:5068
```

## 5. Verify install

```bash
sudo systemctl status arkanum.service --no-pager
APP_URL=https://127.0.0.1:5068 installers/linux/verify-arkanum.sh
```

Expected outcome:

```text
[OK] arkanum.service is active
[INFO] status=Healthy
```

## 6. First-run setup

Open:

```text
https://127.0.0.1:5068/Setup
```

Create the first local administrator and confirm setup locks afterward.

## 7. Recovery material

The installer creates the site-wide Arkanum recovery/master key at:

```text
/etc/arkanum/master-key
```

Back this up with the SQL database. Arkanum recovery is all-or-nothing:

```text
Recoverable = SQL database backup + matching /etc/arkanum/master-key
Not recoverable = SQL database backup alone
```

Protect this key as highly sensitive. Anyone with the database and matching key may be able to restore/decrypt vault contents through an Arkanum instance.

## 8. Useful installed paths

```text
/opt/arkanum                       Published application
/etc/arkanum/master-key            Site-wide recovery/master key
/etc/arkanum/arkanum.env           systemd environment file
/var/lib/arkanum                   service data directory
/var/log/arkanum                   service log directory
/etc/systemd/system/arkanum.service
```

## 9. Air-gap test observations to capture

Please note:

- Did any step require internet access after the package arrived?
- Were prerequisites clear?
- Was SQL setup clear enough?
- Did any command expose secrets unnecessarily?
- Did `/health` provide useful non-secret status?
- Could you identify and protect the recovery key?
- Could a second administrator repeat the install using only this package and local SQL details?

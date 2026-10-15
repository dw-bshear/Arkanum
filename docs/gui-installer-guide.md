# Arkanum GUI installer guide

Arkanum release packages now include platform-specific GUI installer entry points while keeping the scriptable installers for headless or automated environments.

## Package assets

GitHub and BlackBox releases should publish separate platform assets:

```text
Arkanum-<version>-linux-x64.tar.gz
Arkanum-<version>-linux-x64.tar.gz.sha256
Arkanum-<version>-windows-x64.zip
Arkanum-<version>-windows-x64.zip.sha256
```

The GitHub tag remains platform-neutral:

```text
v<version>
```

Example:

```text
v2026.0.2
```

The GitHub Release then attaches both the Windows installation package and the Linux installation package.

## Linux installation package

Entry points:

```text
installers/linux/arkanum-gui-install.py
installers/linux/install-arkanum.sh
installers/linux/verify-arkanum.sh
```

Use the GUI installer from a graphical Ubuntu desktop session:

```bash
python3 installers/linux/arkanum-gui-install.py
```

The GUI prompts for:

- install directory;
- configuration directory;
- data directory;
- log directory;
- service user;
- HTTPS listen URL and certificate binding;
- SQL Server host, port, database, username, and password;
- SQL encryption and certificate validation options;
- Recovery Key acknowledgment.

The GUI invokes the packaged script installer with administrator elevation. If the target is a headless server or does not have Tkinter available, use the script installer directly:

```bash
sudo env \
  ARKANUM_SQL_CONNECTION='Server=tcp:<sql-host>,1433;Database=Arkanum;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;' \
  APP_URLS='https://127.0.0.1:5068' \
  ARKANUM_HTTPS_CERTIFICATE_PATH='/etc/arkanum/tls/arkanum.pfx' \
  installers/linux/install-arkanum.sh
```

For Linux HTTPS service binding, provide a trusted Kestrel PFX certificate with `ARKANUM_HTTPS_CERTIFICATE_PATH` and optional `ARKANUM_HTTPS_CERTIFICATE_PASSWORD`. If TLS is terminated by a reverse proxy, bind Arkanum only to an internal `http://127.0.0.1` URL behind that proxy.

The Linux installer configures:

- `/opt/arkanum` by default;
- `/etc/arkanum` by default;
- `/var/lib/arkanum` by default;
- `/var/log/arkanum` by default;
- `arkanum` service user by default;
- `arkanum.service` systemd unit.

## Windows installation package

Entry points:

```text
installers/windows/Install-ArkanumGui.ps1
installers/windows/Install-Arkanum.ps1
installers/windows/Test-ArkanumInstall.ps1
```

Run **64-bit Windows PowerShell 5.1** as Administrator (not `pwsh`/PowerShell 7), then from the extracted package root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\installers\windows\Install-ArkanumGui.ps1
```

The Windows GUI prompts for:

- install path;
- ProgramData path;
- IIS site name;
- IIS application pool name;
- HTTPS binding, host name, certificate thumbprint, and port;
- SQL Server host, port, database, username, and password;
- SQL encryption and certificate validation options;
- Recovery Key acknowledgment.

The Windows installer configures:

- published app files under `C:\Program Files\Arkanum\Web` by default;
- runtime config/secrets/log folders under `C:\ProgramData\Arkanum` by default;
- IIS application pool;
- IIS site with an HTTPS binding;
- `web.config` environment variables for SQL and recovery key material.

Windows full and update packages include the Microsoft .NET 10 Hosting Bundle. The Windows wizard offers explicit consent for missing IIS features and Hosting Bundle installation, an optional offline Windows feature source, and acknowledgment of possible IIS service interruption. See `windows-server-2022-setup.md` before using a shared IIS server. Setup validates the bundle hash and Microsoft Authenticode signature and never downloads prerequisites on the target. Windows SQL setup and preflight use the packaged application command, not `sqlcmd`. Command previews omit passwords, connection strings, and pasted Recovery Keys.

Production Windows Server 2022 deployments should bind IIS to a trusted server certificate and enable TLS 1.2/1.3 at the OS/IIS layer. Production SQL Server connections should use `Encrypt=True;TrustServerCertificate=False;` with a certificate chain trusted by the application host; the trust bypass is for isolated development/testing only.

## Recovery Key presentation

The native installer creates or preserves the site-wide Recovery Key. The WebUI first-run setup then displays the key again and requires this acknowledgment before the first administrator can be created:

```text
I have saved this recovery key in an approved offline or backup-safe location.
```

This reduces the risk that an administrator misses the key in installer output.

## First administrator

For this release, first administrator creation remains in the WebUI first-run setup instead of the native installer. This keeps application-state initialization inside Arkanum and allows the setup page to enforce Recovery Key acknowledgment immediately before the route locks.

## Best practice for GitHub releases

Use one source tag and one GitHub Release per product version:

```text
source commit -> annotated tag vYYYY.MAJOR.RELEASE -> GitHub Release -> platform assets
```

Attach all platform packages to the same release:

```text
Arkanum-2026.0.2-linux-x64.tar.gz
Arkanum-2026.0.2-linux-x64.tar.gz.sha256
Arkanum-2026.0.2-windows-x64.zip
Arkanum-2026.0.2-windows-x64.zip.sha256
```

Mark `YYYY.0.x` releases as prereleases.

# Arkanum Phase C installer, upgrade, and native packaging guide

Version: `1.1.0`

For the current Windows Server 2022 flow, see `windows-server-2022-setup.md`: elevated 64-bit Windows PowerShell 5.1, offline .NET 10 Hosting Bundle, explicit IIS preparation/maintenance consent, and application-provided SQL setup without Windows `sqlcmd`. MSI remains staging only. Existing published releases are not modified by these source changes.

This is the current 1.1.0 operator guide. The historical Phase C headings identify the installer tracks, not a claim that native platform acceptance is complete. See `versioning.md` for the explicit 2026.1.2 -> 1.1.0 transition and independent schema/key checks.

## Phase C.1 — GUI installers

Both platform packages include GUI installer entry points:

```text
installers/windows/Install-ArkanumGui.ps1
installers/linux/arkanum-gui-install.py
```

The GUI installers collect:

- install action;
- application/install paths;
- SQL Server host, port, database, username, and password;
- SQL encryption/trust choices;
- service/IIS settings;
- Recovery Key acknowledgment.

For the Windows x64 VMware test, extract the Windows package, open PowerShell as Administrator, then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\installers\windows\Install-ArkanumGui.ps1
```

## Phase C.2 — full vs update package model

Release assets are now split into first-time **full** packages and existing-install **update** packages:

```text
Arkanum-1.1.0-full-windows-x64.zip
Arkanum-1.1.0-update-windows-x64.zip
Arkanum-1.1.0-full-linux-x64.tar.gz
Arkanum-1.1.0-update-linux-x64.tar.gz
```

Use this decision table for controlled environments:

| Task | Asset and action |
| --- | --- |
| First install on a new server | Full package, `install` or `auto` |
| Existing install upgrade | Update package, `upgrade` or `auto` |
| Rebuild from backup | Full package, `restore` with SQL database backup + matching Arkanum Recovery Key |
| Side-by-side test | Full package, `side-by-side` |
| Repair current application files | Update package for the current install, or full package when rebuilding |

Each package includes `package-manifest.json` with `packageKind`, `version`, `platform`, `requiresExistingInstall`, and preservation metadata. Update packages are intentionally existing-install-only: they detect the installed state and installed version, preserve configuration, preserve Recovery Key/master-key material, preserve database connectivity settings, preserve service binding settings, create a pre-upgrade backup/report, then run health validation.

Run preflight before the maintenance window:

```powershell
.\installers\windows\Install-Arkanum.ps1 -PackageKind update -PreflightOnly
```

```bash
sudo PACKAGE_KIND=update PREFLIGHT_ONLY=true installers/linux/install-arkanum.sh
```

Expected preflight/report fields include:

```text
Current version:
Target version:
Package kind:
Backup destination:
No SQL passwords or Recovery Key values are written to this report.
```

The script installers support these actions:

```text
auto
install
upgrade
repair
side-by-side
restore
```

`auto` detects an existing installation by checking for `install-state.json` or existing app files. In an update package, `auto` becomes an upgrade and fails before changing files if no existing installation, Recovery Key, or database connection state is found.

State manifests are written here:

```text
Windows: C:\ProgramData\Arkanum\config\install-state.json
Linux:   /etc/arkanum/install-state.json
```

Upgrade and repair actions create pre-change backups:

```text
Windows: C:\ProgramData\Arkanum\backups\<old>-before-1.1.0-<timestamp>
Linux:   /var/backups/arkanum/<old>-before-1.1.0-<timestamp>
```

Upgrade reports are written here:

```text
Windows: C:\ProgramData\Arkanum\logs\upgrade-report-1.1.0.txt
Linux:   /var/log/arkanum/upgrade-report-1.1.0.txt
```

Each installer attempts a post-install `/health` check and records the result. If health validation fails, the report includes the backup path. Rollback app files are operator-controlled because database rollback must be paired with the correct SQL Server backup restore.

## Phase C.3 — native package path

Phase C.3 remains a staging track; ZIP/TAR CLI and GUI installers are the supported deployment paths.

Current release assets remain ZIP/TAR for immediate deployment packaging:

```text
Arkanum-1.1.0-full-windows-x64.zip
Arkanum-1.1.0-update-windows-x64.zip
Arkanum-1.1.0-full-linux-x64.tar.gz
Arkanum-1.1.0-update-linux-x64.tar.gz
```

Native package staging scripts are included:

```text
installers/windows/Build-ArkanumMsi.ps1
installers/linux/build-native-package.sh
```

Expected future native assets:

```text
Arkanum-1.1.0-windows-x64.msi
Arkanum-1.1.0-linux-x64.deb
```

The Windows MSI script writes staging instructions only, even if WiX is installed. Its old incomplete WiX template had no verified payload or calendar-to-SemVer native upgrade mapping; no MSI upgrade is claimed.

The Linux DEB script uses `dpkg-deb`, expects a published Linux app directory, and maps final product `1.1.0` to native version `1:1.1.0` using epoch 1. It stages app files under `/usr/lib/arkanum/app`, not the live `/opt/arkanum` path, with product-version metadata consumed by the CLI/GUI. Run the regular installer after staging. Prereleases/build metadata are rejected by this native builder; TAR supports SemVer prereleases. Legacy native DEB ownership migration is unsupported and rejected by preinst before unpack because dpkg could remove legacy-owned live `/opt/arkanum` files. Do not remove a legacy DEB while its files are the live app; use the TAR product-upgrade path and plan ownership migration separately.

## GitHub release organization

Use one source tag and one GitHub release per product version:

```text
source commit -> v1.1.0 -> GitHub release -> platform assets
```

Attach Windows and Linux packages to the same release. Mark suffixed SemVer test versions as prereleases; `1.1.0` is a final-version release. Preserve historical `2026.0.x` prerelease records unchanged.

## Windows x64 VMware test checklist

1. Install prerequisites on the VM:
   - Windows Server/IIS or Windows with IIS enabled;
   - .NET 10 Hosting Bundle (included in Windows packages; installation requires consent);
   - SQL Server reachable from the VM;
   - 64-bit Windows PowerShell 5.1 (Desktop edition).
2. Download `Arkanum-1.1.0-full-windows-x64.zip` and matching `.sha256` when published.
3. Verify checksum.
4. Extract the ZIP.
5. Run PowerShell as Administrator.
6. Start the GUI installer:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\installers\windows\Install-ArkanumGui.ps1
```

7. Choose `Auto detect` for first install or `Upgrade existing installation` for the authorized `2026.1.2` -> `1.1.0` transition. Other legacy source versions are not silently assumed compatible. `Repair` requires equal version precedence.
8. Complete SQL/IIS settings.
9. Confirm Recovery Key acknowledgment.
10. After install, open:

```text
https://localhost:443/health
https://localhost:443/setup
```

11. Confirm the WebUI presents the Recovery Key and requires acknowledgment before first administrator creation.
12. Save these files from the VM for evidence:

```text
C:\ProgramData\Arkanum\config\install-state.json
C:\ProgramData\Arkanum\logs\upgrade-report-1.1.0.txt
```

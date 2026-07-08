# Arkanum Phase C installer, upgrade, and native packaging guide

Version: `2026.0.6`

This release addresses the urgent Phase C path for a Windows x64 VMware Workstation test machine while preserving Linux parity.

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

## Phase C.2 — upgrade and patch mode

The script installers now support these actions:

```text
auto
install
upgrade
repair
side-by-side
```

`auto` detects an existing installation by checking for `install-state.json` or existing app files.

State manifests are written here:

```text
Windows: C:\ProgramData\Arkanum\config\install-state.json
Linux:   /etc/arkanum/install-state.json
```

Upgrade and repair actions create pre-change backups:

```text
Windows: C:\ProgramData\Arkanum\backups\<old>-before-2026.0.6-<timestamp>
Linux:   /var/backups/arkanum/<old>-before-2026.0.6-<timestamp>
```

Upgrade reports are written here:

```text
Windows: C:\ProgramData\Arkanum\logs\upgrade-report-2026.0.6.txt
Linux:   /var/log/arkanum/upgrade-report-2026.0.6.txt
```

Each installer attempts a post-install `/health` check and records the result. If health validation fails, the report includes the backup path. Rollback app files are operator-controlled because database rollback must be paired with the correct SQL Server backup restore.

## Phase C.3 — native package path

Phase C.3 starts the native packaging path without blocking tomorrow's Windows VM test.

Current release assets remain ZIP/TAR for immediate testability:

```text
Arkanum-2026.0.6-windows-x64.zip
Arkanum-2026.0.6-linux-x64.tar.gz
```

Native package staging scripts are included:

```text
installers/windows/Build-ArkanumMsi.ps1
installers/linux/build-native-package.sh
```

Expected future native assets:

```text
Arkanum-2026.0.6-windows-x64.msi
Arkanum-2026.0.6-linux-x64.deb
```

The Windows MSI script uses WiX when available. If WiX is not installed, it writes staging instructions and leaves the ZIP package as the supported test package.

The Linux DEB script uses `dpkg-deb` and expects a published Linux app directory.

## GitHub release organization

Use one source tag and one GitHub release per product version:

```text
source commit -> v2026.0.6 -> GitHub prerelease -> platform assets
```

Attach Windows and Linux packages to the same release. During testing, mark `2026.0.x` as prerelease.

## Windows x64 VMware test checklist

1. Install prerequisites on the VM:
   - Windows Server/IIS or Windows with IIS enabled;
   - ASP.NET Core Runtime 9;
   - SQL Server reachable from the VM;
   - PowerShell 5.1+.
2. Download `Arkanum-2026.0.6-windows-x64.zip` and matching `.sha256`.
3. Verify checksum.
4. Extract the ZIP.
5. Run PowerShell as Administrator.
6. Start the GUI installer:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\installers\windows\Install-ArkanumGui.ps1
```

7. Choose `Auto detect` for first install or `Upgrade existing installation` if rerunning over `2026.0.2`.
8. Complete SQL/IIS settings.
9. Confirm Recovery Key acknowledgment.
10. After install, open:

```text
http://localhost:5068/health
http://localhost:5068/setup
```

11. Confirm the WebUI presents the Recovery Key and requires acknowledgment before first administrator creation.
12. Save these files from the VM for evidence:

```text
C:\ProgramData\Arkanum\config\install-state.json
C:\ProgramData\Arkanum\logs\upgrade-report-2026.0.6.txt
```

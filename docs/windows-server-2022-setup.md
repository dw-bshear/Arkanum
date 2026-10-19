# Windows Server 2022 setup

Applies to the 1.0.0 source/package contract. Existing published releases do not acquire these changes automatically.

## What the administrator supplies

- Windows Server 2022 x64 and a local administrator session.
- A reachable SQL Server instance (including an independently installed SQL Server Express instance, if appropriate). Arkanum does not bundle/install SQL Server itself.
- Database connection details; a setup/admin SQL identity only when choosing **Create new database**, and a separate runtime login/password. Setup credentials are not persisted.
- A DNS/site name and a trusted HTTPS server certificate with its private key installed in `LocalMachine\My`. Supply the certificate thumbprint. SQL Server must also present a certificate chain trusted by the application server.
- Windows feature-source media if the OS cannot obtain missing IIS payloads from its approved component store/update source.
- A maintenance window for changes to IIS or shared .NET components.

No separate PowerShell 7, .NET SDK, or Windows `sqlcmd` installation is required. The full **and update** Windows packages contain the official Microsoft .NET 10 Hosting Bundle, its pinned manifest, the application, and SQL setup/check code. This does not install .NET into Windows PowerShell: the scripts remain compatible with .NET Framework and Windows PowerShell 5.1, while Arkanum runs on .NET 10.

## Start setup

Extract the complete ZIP; do not run scripts inside the ZIP. Verify the outer SHA-256 against the approved release checksum. Use **64-bit Windows PowerShell 5.1** (`powershell.exe`, Desktop edition) elevated as Administrator, not `pwsh`/PowerShell 7 or the 32-bit host.

Desktop Experience:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\installers\windows\Install-ArkanumGui.ps1
```

Group Policy execution restrictions take precedence; use the organization's approved script execution policy rather than disabling security controls. The scripts are not product-signed by this change.

**Server Core:** use `Install-Arkanum.ps1`; the WinForms wizard requires a graphical desktop and is not the Server Core entry point. Native Server Core execution has not yet been verified.

## Consent and offline prerequisite handling

The GUI presents the same explicit options as the CLI:

- `-EnableIisFeatures`: enable the required IIS/web-management features if missing.
- `-WindowsFeatureSource`: optional approved feature-source path passed to Windows feature installation, for example the appropriate `sources\sxs` or WIM source for the installed OS build. Arkanum does not include Windows installation media.
- `-InstallHostingBundle`: install/repair the bundled .NET 10 Hosting Bundle if required.
- `-AllowIisServiceInterruption`: acknowledge possible IIS service interruption on an already-running IIS host when installing/repairing the bundle.

Consent defaults off. A missing prerequisite stops setup with guidance rather than silently changing the server. IIS is prepared **before** the Hosting Bundle so the ASP.NET Core IIS module is installed. The bundle is verified using the pinned SHA-512 and a valid Microsoft Authenticode signature before execution. A hash/signature failure stops installation; there is no trust bypass or automatic target-side download. Disconnected networks must pre-stage approved Microsoft certificate trust/revocation material required by their validation policy.

The Microsoft bundle is launched quietly with `/norestart`. **This is not a guarantee that IIS services remain running.** Use a maintenance window and the explicit interruption acknowledgment for a shared IIS host. Arkanum does not run a server-wide `iisreset` automatically. Restart-required outcomes (including exit code `3010`, or `1641`) stop setup before application deployment; perform the approved restart and rerun setup. IIS feature installation can also require a restart.

## Preflight and database operations

Use `-PreflightOnly` before the maintenance window. It must not install prerequisites or change application files. Missing prerequisites can prevent live database checks; treat that result as incomplete readiness, not a successful database check. After prerequisites are present, rerun preflight with the intended connection details.

The Windows installer invokes the packaged application:

```text
dotnet app/Arkanum.Web.dll --database-setup provision
dotnet app/Arkanum.Web.dll --database-setup preflight
```

These are internal setup entry points; the installer scopes credentials to temporary child-process environment variables and restores the previous environment afterward. Do not put passwords/connection strings on a command line or in tickets. Reports and GUI previews must not include SQL passwords, connection strings, or pasted Recovery Keys.

Provisioning creates the database/schema and a separate runtime login with DML grants and denied audit-row UPDATE/DELETE; it does not grant broad database roles. Preflight verifies database reachability, expected schema and effective runtime permissions before application files are replaced. An existing runtime login with a different password must be reconciled explicitly; setup does not silently change it.

Restore still requires a database already restored by a DBA and the matching Arkanum Recovery Key. Automated `.bak` restore and MDF/LDF attach remain staged/not supported. Recovery sentinel validation must succeed independently of web liveness; a public `/health` response cannot prove that a restored key matches the database.

## Completion and ongoing maintenance

- `/health` returns `Alive`: the web process responds, not a complete readiness attestation.
- Sign in as Administrator/Auditor and verify `/health/details` is `Healthy`.
- Complete first-run setup, acknowledge and back up the Recovery Key, and test saving/revealing a non-production credential.
- Keep SQL backups and the matching Recovery Key together in the approved recovery process.
- Back up before upgrades; preserve configuration, Recovery Key, database connectivity, and site bindings.
- Schedule ongoing Microsoft Hosting Bundle security updates. Bundling a pinned runtime removes a manual installation step, not the need to patch it.

## Build/package preparation (connected release host only)

Use the .NET 10 SDK pinned by `global.json` and locked NuGet dependencies. Fetch the official prerequisite on the connected build host, not on the installation target:

```bash
python3 scripts/fetch-windows-prerequisites.py
dotnet restore Arkanum.sln --locked-mode
dotnet publish Arkanum.Web -c Release -r win-x64 --self-contained false -p:RestoreLockedMode=true -o publish/arkanum-windows-x64
dotnet publish Arkanum.Web -c Release -r linux-x64 --self-contained false -p:RestoreLockedMode=true -o publish/arkanum-linux-x64
python3 scripts/create-release-packages.py --version 1.0.0 --windows-prerequisites artifacts/prerequisites
```

The package builder requires a hash-validated Hosting Bundle cache and includes it only in Windows assets. Binaries and build caches stay out of Git. Linux continues to require an approved ASP.NET Core 10 runtime and its existing SQL tool prerequisites; Windows automation does not imply Linux prerequisite automation.

Run the cross-platform regression checks on the build host:

```bash
dotnet test Arkanum.sln --no-restore
python3 -m unittest discover -s tests -v
```

The Python suite uses `pwsh` for parser/mocked native-boundary checks; this is not a claim that the product installer supports PowerShell 7. For opt-in live database integration, use a Docker-capable lab host with the SQL Server 2022 image and run `python3 tests/sql_setup_smoke.py --publish publish/arkanum-linux-x64`. The smoke creates its own isolated Developer SQL container with generated credentials, exercises provision/preflight/web startup/correct and incorrect recovery keys, and removes its own container afterward. It never reuses an existing database.

## Native acceptance gate

Linux-host .NET tests, PowerShell parsing/helper tests, and ZIP/TAR payload validation are not equivalent to installing on Server 2022. Before production deployment, record fresh install, existing-IIS consent/interruption behavior, offline trust failure, missing Windows source, restart/resume, .NET 9-to-10 upgrade, repair, restore success/mismatch, and Server Core CLI results. These native platform acceptance checks are **not yet** certified by this source change.

## Primary references

- Microsoft ASP.NET Core Hosting Bundle: https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0
- Microsoft .NET deployment models: https://learn.microsoft.com/dotnet/core/deploying/
- Microsoft .NET release metadata: https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json

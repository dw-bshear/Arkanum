#requires -Version 5.1
<#
Arkanum Windows GUI installer wrapper.

This WinForms wizard collects installation path, IIS/site settings, SQL Server
connection information, and recovery-key acknowledgment, then invokes the
packaged Install-Arkanum.ps1 script. Run from the extracted Windows package as
Administrator.
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$script:PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:CliInstaller = Join-Path $PSScriptRoot 'Install-Arkanum.ps1'

function New-Label($text, $x, $y, $w = 180) {
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $text
    $label.Location = New-Object System.Drawing.Point($x, $y)
    $label.Size = New-Object System.Drawing.Size($w, 22)
    return $label
}

function New-TextBox($text, $x, $y, $w = 420, [bool]$password = $false) {
    $box = New-Object System.Windows.Forms.TextBox
    $box.Text = $text
    $box.Location = New-Object System.Drawing.Point($x, $y)
    $box.Size = New-Object System.Drawing.Size($w, 24)
    if ($password) { $box.UseSystemPasswordChar = $true }
    return $box
}

function Require-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
        [System.Windows.Forms.MessageBox]::Show('Run Arkanum Setup as Administrator.', 'Arkanum Setup', 'OK', 'Error') | Out-Null
        exit 5
    }
}

function Build-ConnectionString {
    param($SqlHost, $SqlPort, $Database, $User, $Password, [bool]$Encrypt, [bool]$TrustCert)
    if ([string]::IsNullOrWhiteSpace($SqlHost) -or [string]::IsNullOrWhiteSpace($Database) -or [string]::IsNullOrWhiteSpace($User) -or [string]::IsNullOrWhiteSpace($Password)) {
        throw 'SQL Server host, database, username, and password are required.'
    }
    if ([string]::IsNullOrWhiteSpace($SqlPort)) { $SqlPort = '1433' }
    $encryptText = if ($Encrypt) { 'True' } else { 'False' }
    $trustText = if ($TrustCert) { 'True' } else { 'False' }
    return "Server=tcp:$SqlHost,$SqlPort;Database=$Database;User Id=$User;Password=$Password;Encrypt=$encryptText;TrustServerCertificate=$trustText;"
}

Require-Admin
if (!(Test-Path $script:CliInstaller)) {
    [System.Windows.Forms.MessageBox]::Show("Missing CLI installer: $script:CliInstaller", 'Arkanum Setup', 'OK', 'Error') | Out-Null
    exit 2
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Arkanum Windows Setup'
$form.Size = New-Object System.Drawing.Size(820, 690)
$form.StartPosition = 'CenterScreen'

$title = New-Object System.Windows.Forms.Label
$title.Text = 'Arkanum Windows installation package'
$title.Font = New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Bold)
$title.Location = New-Object System.Drawing.Point(16, 12)
$title.Size = New-Object System.Drawing.Size(760, 34)
$form.Controls.Add($title)

$tabs = New-Object System.Windows.Forms.TabControl
$tabs.Location = New-Object System.Drawing.Point(16, 56)
$tabs.Size = New-Object System.Drawing.Size(770, 520)
$form.Controls.Add($tabs)

$installTab = New-Object System.Windows.Forms.TabPage
$installTab.Text = 'Install and IIS'
$tabs.Controls.Add($installTab)
$action = New-Object System.Windows.Forms.ComboBox
$action.DropDownStyle = 'DropDownList'
[void]$action.Items.AddRange(@('Auto detect', 'Install new instance', 'Upgrade existing installation', 'Repair current installation', 'Install side-by-side'))
$action.SelectedIndex = 0
$action.Location = New-Object System.Drawing.Point(220, 24)
$action.Size = New-Object System.Drawing.Size(300, 24)
$installPath = New-TextBox 'C:\Program Files\Arkanum\Web' 220 64 470
$programData = New-TextBox 'C:\ProgramData\Arkanum' 220 104 470
$siteName = New-TextBox 'Arkanum' 220 144 250
$appPool = New-TextBox 'ArkanumAppPool' 220 184 250
$port = New-TextBox '5068' 220 224 120
foreach ($pair in @(
    @('Install action', $action, 24),
    @('Install path', $installPath, 64),
    @('ProgramData path', $programData, 104),
    @('IIS site name', $siteName, 144),
    @('IIS app pool', $appPool, 184),
    @('HTTP port', $port, 224)
)) {
    $installTab.Controls.Add((New-Label $pair[0] 24 $pair[2])); $installTab.Controls.Add($pair[1])
}
$installHelp = New-Object System.Windows.Forms.Label
$installHelp.Text = 'The installer configures IIS, the application pool, ProgramData folders, runtime configuration, site permissions, upgrade backups, and install-state.json.'
$installHelp.Location = New-Object System.Drawing.Point(24, 270)
$installHelp.Size = New-Object System.Drawing.Size(700, 70)
$installTab.Controls.Add($installHelp)

$sqlTab = New-Object System.Windows.Forms.TabPage
$sqlTab.Text = 'SQL Server'
$tabs.Controls.Add($sqlTab)
$sqlHost = New-TextBox '' 220 24 350
$sqlPort = New-TextBox '1433' 220 64 120
$sqlDb = New-TextBox 'Arkanum' 220 104 260
$sqlUser = New-TextBox 'arkanum_app' 220 144 260
$sqlPassword = New-TextBox '' 220 184 260 $true
$encrypt = New-Object System.Windows.Forms.CheckBox
$encrypt.Text = 'Encrypt SQL connection'
$encrypt.Checked = $true
$encrypt.Location = New-Object System.Drawing.Point(220, 224)
$encrypt.Size = New-Object System.Drawing.Size(240, 24)
$trust = New-Object System.Windows.Forms.CheckBox
$trust.Text = 'Trust SQL Server certificate'
$trust.Checked = $true
$trust.Location = New-Object System.Drawing.Point(220, 254)
$trust.Size = New-Object System.Drawing.Size(240, 24)
foreach ($pair in @(
    @('SQL Server host', $sqlHost, 24),
    @('SQL Server port', $sqlPort, 64),
    @('Database name', $sqlDb, 104),
    @('SQL username', $sqlUser, 144),
    @('SQL password', $sqlPassword, 184)
)) {
    $sqlTab.Controls.Add((New-Label $pair[0] 24 $pair[2])); $sqlTab.Controls.Add($pair[1])
}
$sqlTab.Controls.Add($encrypt); $sqlTab.Controls.Add($trust)

$recoveryTab = New-Object System.Windows.Forms.TabPage
$recoveryTab.Text = 'Recovery Key'
$tabs.Controls.Add($recoveryTab)
$recoveryText = New-Object System.Windows.Forms.Label
$recoveryText.Text = "Recovery Key`r`n`r`nThe Windows installer creates C:\ProgramData\Arkanum\secrets\master-key.txt if one does not already exist. The WebUI first-run setup will display the Arkanum Recovery Key again and require an acknowledgment before the first administrator can be created.`r`n`r`nRecoverable = SQL database backup + matching Recovery Key.`r`nNot recoverable = SQL database backup alone."
$recoveryText.Location = New-Object System.Drawing.Point(24, 24)
$recoveryText.Size = New-Object System.Drawing.Size(700, 180)
$recoveryTab.Controls.Add($recoveryText)
$ack = New-Object System.Windows.Forms.CheckBox
$ack.Text = 'I understand the Recovery Key must be saved before operational use.'
$ack.Location = New-Object System.Drawing.Point(24, 220)
$ack.Size = New-Object System.Drawing.Size(680, 28)
$recoveryTab.Controls.Add($ack)

$summaryTab = New-Object System.Windows.Forms.TabPage
$summaryTab.Text = 'Run'
$tabs.Controls.Add($summaryTab)
$commandPreview = New-Object System.Windows.Forms.TextBox
$commandPreview.Location = New-Object System.Drawing.Point(24, 24)
$commandPreview.Size = New-Object System.Drawing.Size(700, 300)
$commandPreview.Multiline = $true
$commandPreview.ScrollBars = 'Vertical'
$summaryTab.Controls.Add($commandPreview)

$status = New-Object System.Windows.Forms.Label
$status.Location = New-Object System.Drawing.Point(16, 585)
$status.Size = New-Object System.Drawing.Size(760, 32)
$status.Text = 'Complete the installer pages, then generate or run setup.'
$form.Controls.Add($status)

function Get-InstallerAction {
    switch ($action.SelectedItem) {
        'Install new instance' { return 'install' }
        'Upgrade existing installation' { return 'upgrade' }
        'Repair current installation' { return 'repair' }
        'Install side-by-side' { return 'side-by-side' }
        default { return 'auto' }
    }
}

function Build-CommandPreview {
    $conn = Build-ConnectionString $sqlHost.Text $sqlPort.Text $sqlDb.Text $sqlUser.Text $sqlPassword.Text $encrypt.Checked $trust.Checked
    $selectedAction = Get-InstallerAction
    $args = @(
        '-ExecutionPolicy', 'Bypass',
        '-File', "`"$script:CliInstaller`"",
        '-PackagePath', "`"$script:PackageRoot\app`"",
        '-InstallPath', "`"$($installPath.Text)`"",
        '-ProgramDataPath', "`"$($programData.Text)`"",
        '-SiteName', "`"$($siteName.Text)`"",
        '-Port', $port.Text,
        '-SqlConnectionString', "`"$conn`"",
        '-AppPoolName', "`"$($appPool.Text)`"",
        '-PackageVersion', '2026.0.3',
        '-Action', $selectedAction
    )
    $commandPreview.Text = 'powershell.exe ' + ($args -join ' ')
    return $conn
}

$generate = New-Object System.Windows.Forms.Button
$generate.Text = 'Generate command preview'
$generate.Location = New-Object System.Drawing.Point(24, 340)
$generate.Size = New-Object System.Drawing.Size(190, 32)
$generate.Add_Click({
    try { Build-CommandPreview | Out-Null; $status.Text = 'Command preview generated.' }
    catch { [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Arkanum Setup', 'OK', 'Error') | Out-Null }
})
$summaryTab.Controls.Add($generate)

$run = New-Object System.Windows.Forms.Button
$run.Text = 'Run installer'
$run.Location = New-Object System.Drawing.Point(230, 340)
$run.Size = New-Object System.Drawing.Size(150, 32)
$run.Add_Click({
    if (-not $ack.Checked) {
        [System.Windows.Forms.MessageBox]::Show('Acknowledge the Recovery Key requirement before running setup.', 'Arkanum Setup', 'OK', 'Error') | Out-Null
        return
    }
    try {
        $conn = Build-CommandPreview
        $params = @{
            PackagePath = (Join-Path $script:PackageRoot 'app')
            InstallPath = $installPath.Text
            ProgramDataPath = $programData.Text
            SiteName = $siteName.Text
            Port = [int]$port.Text
            SqlConnectionString = $conn
            AppPoolName = $appPool.Text
            PackageVersion = '2026.0.3'
            Action = Get-InstallerAction
        }
        & $script:CliInstaller @params
        [System.Windows.Forms.MessageBox]::Show('Arkanum installation completed. Open the WebUI first-run setup to save the Recovery Key and create the first administrator.', 'Arkanum Setup', 'OK', 'Information') | Out-Null
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Arkanum Setup failed', 'OK', 'Error') | Out-Null
    }
})
$summaryTab.Controls.Add($run)

[void]$form.ShowDialog()

#requires -Version 5.1
# Product precedence only. Database schema/sentinel preflight is independent.
function ConvertTo-ArkanumVersion {
    param([string]$Value)
    if ($Value -ceq '2026.1.2') { return @{ Family=0; Core=@('2026','1','2'); Pre=@() } }
    $pattern = '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z'
    if ($Value -cnotmatch $pattern) { throw 'Malformed or unsupported version family; no changes made.' }
    $core = @($Matches[1], $Matches[2], $Matches[3])
    $pre = @()
    if ($Matches[4]) { $pre = @($Matches[4].Split('.')) }
    # Compare decimal strings without Int32/Int64 overflow.
    if ($core[0].Length -gt 4 -or [int]$core[0] -lt 1 -or [int]$core[0] -ge 2000) { throw 'Unsupported version family; no changes made.' }
    foreach ($part in $pre) { if ($part -cmatch '^0[0-9]+$') { throw 'Malformed numeric prerelease identifier.' } }
    return @{ Family=1; Core=$core; Pre=$pre }
}

function Compare-ArkanumDecimal {
    param([string]$Left, [string]$Right)
    if ($Left.Length -ne $Right.Length) { return [Math]::Sign($Left.Length - $Right.Length) }
    return [Math]::Sign([string]::CompareOrdinal($Left, $Right))
}

function Compare-ArkanumVersion {
    param([string]$Left, [string]$Right)
    $a = ConvertTo-ArkanumVersion $Left
    $b = ConvertTo-ArkanumVersion $Right
    if ($a.Family -ne $b.Family) { return [Math]::Sign($a.Family - $b.Family) }
    for ($i=0; $i -lt 3; $i++) {
        $order = Compare-ArkanumDecimal $a.Core[$i] $b.Core[$i]
        if ($order -ne 0) { return $order }
    }
    if ($a.Pre.Count -eq 0 -or $b.Pre.Count -eq 0) {
        if ($a.Pre.Count -eq $b.Pre.Count) { return 0 }
        if ($a.Pre.Count -eq 0) { return 1 } else { return -1 }
    }
    for ($i=0; $i -lt [Math]::Min($a.Pre.Count, $b.Pre.Count); $i++) {
        $x = $a.Pre[$i]; $y = $b.Pre[$i]
        $xn = $x -cmatch '^[0-9]+$'; $yn = $y -cmatch '^[0-9]+$'
        if ($xn -and $yn) { $order = Compare-ArkanumDecimal $x $y }
        elseif ($xn -ne $yn) { if ($xn) { $order=-1 } else { $order=1 } }
        else { $order = [Math]::Sign([string]::CompareOrdinal($x, $y)) }
        if ($order -ne 0) { return $order }
    }
    return [Math]::Sign($a.Pre.Count - $b.Pre.Count)
}

function Assert-ArkanumPackageVersion {
    param([string]$PackageVersion)
    if ((ConvertTo-ArkanumVersion $PackageVersion).Family -ne 1) { throw 'Legacy releases are immutable, not new package targets.' }
}

function Assert-ArkanumVersionTransition {
    param([string]$InstalledVersion, [string]$PackageVersion, [string]$Action, [string]$MinimumInstalledVersion = '')
    Assert-ArkanumPackageVersion $PackageVersion
    $order = Compare-ArkanumVersion $PackageVersion $InstalledVersion
    if ($InstalledVersion -ceq '2026.1.2' -and (Compare-ArkanumVersion $PackageVersion '1.0.0') -lt 0) { throw 'Legacy transition requires 1.0.0 or later, not its prerelease.' }
    if ($order -lt 0) { throw 'Downgrade rejected; use the documented backup rollback procedure.' }
    if ($Action -eq 'repair' -and $order -ne 0) { throw 'Repair requires the same version; use upgrade for a newer release.' }
    if ($Action -notin @('upgrade','repair')) { throw 'Existing installation requires upgrade or same-version repair.' }
    if ($MinimumInstalledVersion -and (Compare-ArkanumVersion $InstalledVersion $MinimumInstalledVersion) -lt 0) { throw 'Installed version is below minimumInstalledVersion.' }
}

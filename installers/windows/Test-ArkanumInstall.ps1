param(
    [string]$Url = 'https://localhost:443/health'
)

$ErrorActionPreference = 'Stop'

$response = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 15
Write-Host "[INFO] status=$($response.status) version=$($response.version)"
foreach ($check in $response.checks) {
    Write-Host "[$($check.status)] $($check.name): $($check.message)"
}
Write-Host '[INFO] Public health is liveness, not Recovery Key/sentinel verification.'
if ($response.status -ne 'Alive' -and $response.status -ne 'Healthy') {
    throw "Arkanum health check failed."
}

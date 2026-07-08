param(
    [string]$Url = 'http://localhost:5068/health'
)

$ErrorActionPreference = 'Stop'

$response = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 15
Write-Host "[INFO] status=$($response.status) version=$($response.version)"
foreach ($check in $response.checks) {
    Write-Host "[$($check.status)] $($check.name): $($check.message)"
}
if ($response.status -ne 'Healthy') {
    throw "Conclave health check failed."
}

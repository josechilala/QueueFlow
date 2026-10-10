[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

# Inspect shape only. Never print the connection string, host, database, username or password.
$apiProject = Join-Path $PSScriptRoot '../src/QueueFlow.Api/QueueFlow.Api.csproj'
[xml]$project = Get-Content -Raw -LiteralPath $apiProject
$secretId = [string]$project.Project.PropertyGroup.UserSecretsId
$secretFile = Join-Path $env:APPDATA "Microsoft/UserSecrets/$secretId/secrets.json"
if (-not (Test-Path -LiteralPath $secretFile)) {
    Write-Output 'API User Secrets file not found.'
    return
}
try {
    $secrets = Get-Content -Raw -LiteralPath $secretFile | ConvertFrom-Json
    $connection = $secrets.'ConnectionStrings:QueueFlowDatabase'
    if (-not $connection -and $secrets.ConnectionStrings) { $connection = $secrets.ConnectionStrings.QueueFlowDatabase }
    $parser = New-Object System.Data.Common.DbConnectionStringBuilder
    $parser.set_ConnectionString([string]$connection)
    $hostValue = if ($parser.ContainsKey('Host')) { [string]$parser['Host'] } else { '' }
    [pscustomobject]@{
        InspectedSource = 'API User Secrets (not a claim of effective runtime configuration)'
        ConnectionPresent = [bool]$connection
        Keys = (@($parser.Keys) | Sort-Object) -join ','
        HostPresent = [bool]$hostValue
        Loopback = $hostValue -in @('localhost', '127.0.0.1', '::1')
        EnvironmentOverridePresent = [bool][Environment]::GetEnvironmentVariable('ConnectionStrings__QueueFlowDatabase') -or
            [bool][Environment]::GetEnvironmentVariable('ConnectionStrings:QueueFlowDatabase')
        DevelopmentIsolationProven = $false
    }
}
catch { Write-Output 'Connection configuration could not be inspected; values and exception details suppressed.' }

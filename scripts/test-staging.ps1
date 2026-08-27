[CmdletBinding()]
param(
    [string]$ProjectName = 'queueflow-staging',
    [string]$EnvironmentFile = '.env.staging',
    [string]$ApiUrl = 'http://localhost:18080',
    [string]$AdminUrl = 'http://localhost:13000',
    [string]$CustomerUrl = 'http://localhost:13001',
    [string]$DisplayUrl = 'http://localhost:13002',
    [string]$PrometheusUrl = 'http://localhost:19090',
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$compose = @('--project-name', $ProjectName, '--env-file', $EnvironmentFile, '--file', 'docker-compose.yml', '--file', 'docker-compose.staging.yml')

function Wait-HttpOk([string]$Name, [string]$Url) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10
            if ($response.StatusCode -eq 200) { Write-Host "[ok] $Name - $Url"; return }
        } catch { Start-Sleep -Seconds 3 }
    } while ((Get-Date) -lt $deadline)
    throw "$Name did not become healthy at $Url within $TimeoutSeconds seconds."
}

$migration = (& docker compose @compose ps --all migrate --format json | ConvertFrom-Json)
if ($migration.ExitCode -ne 0) { throw "Migration container did not finish successfully. ExitCode=$($migration.ExitCode)" }
Write-Host '[ok] migrations'

Wait-HttpOk 'API liveness' "$ApiUrl/health/live"
Wait-HttpOk 'API readiness (PostgreSQL + Redis)' "$ApiUrl/health"
Wait-HttpOk 'Admin Web' $AdminUrl
Wait-HttpOk 'Customer Web' $CustomerUrl
Wait-HttpOk 'Display Web' $DisplayUrl
Wait-HttpOk 'Prometheus' "$PrometheusUrl/-/healthy"

$correlationResponse = Invoke-WebRequest -Uri "$ApiUrl/health/live" -UseBasicParsing -Headers @{ 'X-Correlation-ID' = 'staging-smoke-test' } -TimeoutSec 15
if ($correlationResponse.Headers['X-Correlation-ID'] -ne 'staging-smoke-test') { throw 'Correlation ID was not propagated.' }
Write-Host '[ok] correlation ID'

$signalR = Invoke-RestMethod -Method Post -Uri "$ApiUrl/hubs/queue/negotiate?negotiateVersion=1" -ContentType 'application/json' -Body '{}' -TimeoutSec 15
if (-not ($signalR.availableTransports.transport -contains 'WebSockets')) { throw 'SignalR negotiation did not advertise WebSockets.' }
Write-Host '[ok] SignalR negotiation + WebSockets transport'

$targets = Invoke-RestMethod -Uri "$PrometheusUrl/api/v1/targets" -TimeoutSec 15
$telemetry = $targets.data.activeTargets | Where-Object { $_.labels.job -eq 'queueflow-telemetry' }
if (-not $telemetry -or $telemetry.health -ne 'up') { throw 'Prometheus telemetry target is not up.' }
Write-Host '[ok] OpenTelemetry -> Prometheus'

Write-Host 'Staging smoke test passed.'

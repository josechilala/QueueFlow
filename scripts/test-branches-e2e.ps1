[CmdletBinding()]
param([int]$ApiPort = 5196)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$temporary = Join-Path $env:TEMP ('queueflow-branches-e2e-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null

function Json-File([string]$Name, [hashtable]$Value) {
    $path = Join-Path $temporary $Name
    $Value | ConvertTo-Json -Compress | Set-Content -LiteralPath $path -Encoding Ascii
    return $path
}

function Call([string[]]$Arguments) {
    & curl.exe -sS --max-time 20 @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'curl failed.' }
}

function Wait-Api([string]$Url) {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $code = & curl.exe -sS --connect-timeout 1 --max-time 2 -o NUL -w '%{http_code}' $Url
        if ($LASTEXITCODE -eq 0 -and $code -eq '200') { return }
        Start-Sleep -Milliseconds 500
    }
    throw 'API startup timed out.'
}

function Register-And-Login([string]$Prefix, [string]$Password, [long]$Stamp) {
    $email = "$Prefix-$Stamp@branches.local"
    $register = Json-File "$Prefix-register.json" @{ name = "$Prefix $Stamp"; slug = "$Prefix-$Stamp"; timeZone = 'America/Sao_Paulo'; adminName = "$Prefix Owner"; adminEmail = $email; password = $Password }
    $status = Call @('-o', 'NUL', '-w', '%{http_code}', '-H', 'Content-Type: application/json', '--data-binary', "@$register", "http://127.0.0.1:$ApiPort/api/v1/auth/register")
    if ($status -ne '201') { throw "Registration returned $status." }
    $login = Json-File "$Prefix-login.json" @{ email = $email; password = $Password }
    $tokenFile = Join-Path $temporary "$Prefix-token.json"
    $status = Call @('-o', $tokenFile, '-w', '%{http_code}', '-H', 'Content-Type: application/json', '--data-binary', "@$login", "http://127.0.0.1:$ApiPort/api/v1/auth/login")
    if ($status -ne '200') { throw "Login returned $status." }
    return (Get-Content $tokenFile -Raw | ConvertFrom-Json).accessToken
}

$apiOut = Join-Path $temporary 'api.out'
$apiErr = Join-Path $temporary 'api.err'
$apiDll = (Resolve-Path (Join-Path $root 'src\QueueFlow.Api\bin\Debug\net10.0\QueueFlow.Api.dll')).Path
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$api = Start-Process dotnet -ArgumentList ('"' + $apiDll + '" --urls http://127.0.0.1:' + $ApiPort) -WorkingDirectory (Join-Path $root 'src\QueueFlow.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput $apiOut -RedirectStandardError $apiErr

try {
    Wait-Api "http://127.0.0.1:$ApiPort/health/live"
    $stamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $password = 'Qf!Branches-' + [Guid]::NewGuid().ToString('N')
    $ownerToken = Register-And-Login 'branch-owner' $password $stamp
    $otherToken = Register-And-Login 'branch-other' $password ($stamp + 1)

    $create = Json-File 'create.json' @{ name = 'Unidade Persistente'; address = 'Rua de Teste, 10'; timeZone = 'America/Sao_Paulo' }
    $createdFile = Join-Path $temporary 'created.json'
    $createdStatus = Call @('-o', $createdFile, '-w', '%{http_code}', '-H', "Authorization: Bearer $ownerToken", '-H', 'Content-Type: application/json', '--data-binary', "@$create", "http://127.0.0.1:$ApiPort/api/v1/branches")
    $created = Get-Content $createdFile -Raw | ConvertFrom-Json
    $listFile = Join-Path $temporary 'list.json'
    $listStatus = Call @('-o', $listFile, '-w', '%{http_code}', '-H', "Authorization: Bearer $ownerToken", "http://127.0.0.1:$ApiPort/api/v1/branches")
    $listed = @(Get-Content $listFile -Raw | ConvertFrom-Json) | Where-Object { $_.id -eq $created.id }

    $update = Json-File 'update.json' @{ name = 'Unidade Atualizada'; address = 'Avenida Persistente, 20'; timeZone = 'America/Sao_Paulo' }
    $updatedFile = Join-Path $temporary 'updated.json'
    $updatedStatus = Call @('-o', $updatedFile, '-w', '%{http_code}', '-X', 'PUT', '-H', "Authorization: Bearer $ownerToken", '-H', 'Content-Type: application/json', '--data-binary', "@$update", "http://127.0.0.1:$ApiPort/api/v1/branches/$($created.id)")
    $statusBody = Json-File 'status.json' @{ isActive = $false }
    $statusFile = Join-Path $temporary 'status-response.json'
    $disabledStatus = Call @('-o', $statusFile, '-w', '%{http_code}', '-X', 'PATCH', '-H', "Authorization: Bearer $ownerToken", '-H', 'Content-Type: application/json', '--data-binary', "@$statusBody", "http://127.0.0.1:$ApiPort/api/v1/branches/$($created.id)/status")
    $reloadedFile = Join-Path $temporary 'reloaded.json'
    $reloadStatus = Call @('-o', $reloadedFile, '-w', '%{http_code}', '-H', "Authorization: Bearer $ownerToken", "http://127.0.0.1:$ApiPort/api/v1/branches/$($created.id)")
    $reloaded = Get-Content $reloadedFile -Raw | ConvertFrom-Json
    $isolatedStatus = Call @('-o', 'NUL', '-w', '%{http_code}', '-H', "Authorization: Bearer $otherToken", "http://127.0.0.1:$ApiPort/api/v1/branches/$($created.id)")

    $checks = [ordered]@{
        'Create returns 201' = $createdStatus -eq '201'
        'Fresh list contains created branch' = $listStatus -eq '200' -and $listed.Count -eq 1
        'Update returns 200' = $updatedStatus -eq '200'
        'Deactivate returns 200' = $disabledStatus -eq '200'
        'Fresh detail persists update and status' = $reloadStatus -eq '200' -and $reloaded.name -eq 'Unidade Atualizada' -and $reloaded.isActive -eq $false
        'Other tenant cannot read branch' = $isolatedStatus -eq '404'
    }
    $checks.GetEnumerator() | ForEach-Object { Write-Output ($_.Key + ': ' + $_.Value) }
    if ($checks.Values -contains $false) { throw 'One or more branch E2E checks failed.' }
}
catch {
    Get-Content $apiOut -ErrorAction SilentlyContinue
    Get-Content $apiErr -ErrorAction SilentlyContinue
    throw
}
finally {
    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue }
}

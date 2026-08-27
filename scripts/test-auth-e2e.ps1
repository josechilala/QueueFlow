[CmdletBinding()]
param([int]$ApiPort = 5188, [int]$WebPort = 3100)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$temporary = Join-Path $env:TEMP ('queueflow-auth-e2e-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null

function Status([string[]]$Arguments) {
    & curl.exe -sS --max-time 15 @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'curl failed.' }
}

function Json-File([string]$Name, [hashtable]$Value) {
    $path = Join-Path $temporary $Name
    $Value | ConvertTo-Json -Compress | Set-Content -LiteralPath $path -Encoding Ascii
    return $path
}

function Require-Status([string]$Actual, [string]$Expected, [string]$Step) {
    if ($Actual -ne $Expected) { throw "$Step returned HTTP $Actual; expected $Expected." }
}

function Wait-Url([string]$Url) {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $code = & curl.exe -sS --connect-timeout 1 --max-time 2 -o NUL -w '%{http_code}' $Url
        if ($LASTEXITCODE -eq 0 -and [int]$code -lt 500) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "Timeout waiting for $Url"
}

$apiOut = Join-Path $temporary 'api.out'
$apiErr = Join-Path $temporary 'api.err'
$webOut = Join-Path $temporary 'web.out'
$webErr = Join-Path $temporary 'web.err'
$apiDll = (Resolve-Path (Join-Path $root 'src\QueueFlow.Api\bin\Debug\net10.0\QueueFlow.Api.dll')).Path
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$api = Start-Process dotnet -ArgumentList ('"' + $apiDll + '" --urls http://127.0.0.1:' + $ApiPort) -WorkingDirectory (Join-Path $root 'src\QueueFlow.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput $apiOut -RedirectStandardError $apiErr
$env:QUEUEFLOW_API_URL = "http://127.0.0.1:$ApiPort"
$env:QUEUEFLOW_SECURE_COOKIES = 'false'
$web = Start-Process npm.cmd -ArgumentList @('run', 'start', '--workspace', '@queueflow/admin-web', '--', '--port', $WebPort) -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput $webOut -RedirectStandardError $webErr

try {
    Wait-Url "http://127.0.0.1:$ApiPort/health/live"
    Wait-Url "http://127.0.0.1:$WebPort/login"
    $stamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    $password = 'Qf!' + [Convert]::ToBase64String($bytes)
    $email = "owner-$stamp@auth.local"
    $name = "Admin Validation $stamp"

    $unauthenticated = Status @('-o', 'NUL', '-w', '%{http_code}', "http://127.0.0.1:$WebPort/dashboard")
    $invalidBody = Json-File 'invalid.json' @{ email = $email; password = 'definitely-wrong-password' }
    $invalid = Status @('-o', 'NUL', '-w', '%{http_code}', '-H', 'Content-Type: application/json', '--data-binary', "@$invalidBody", "http://127.0.0.1:$WebPort/api/auth/login")
    Require-Status $invalid '401' 'Invalid login'
    $registerBody = Json-File 'register.json' @{ name = "Auth Validation $stamp"; slug = "auth-validation-$stamp"; timeZone = 'America/Sao_Paulo'; adminName = $name; adminEmail = $email; password = $password }
    $registered = Status @('-o', 'NUL', '-w', '%{http_code}', '-H', 'Content-Type: application/json', '--data-binary', "@$registerBody", "http://127.0.0.1:$ApiPort/api/v1/auth/register")
    Require-Status $registered '201' 'Registration'
    $cookieJar = Join-Path $temporary 'owner.cookies'
    $loginHeaders = Join-Path $temporary 'login.headers'
    $loginBody = Json-File 'login.json' @{ email = $email; password = $password }
    $login = Status @('-D', $loginHeaders, '-c', $cookieJar, '-o', 'NUL', '-w', '%{http_code}', '-H', 'Content-Type: application/json', '--data-binary', "@$loginBody", "http://127.0.0.1:$WebPort/api/auth/login")
    Require-Status $login '200' 'Valid login'
    $dashboardFile = Join-Path $temporary 'dashboard.html'
    $dashboard = Status @('-b', $cookieJar, '-o', $dashboardFile, '-w', '%{http_code}', "http://127.0.0.1:$WebPort/dashboard")
    $session = Status @('-b', $cookieJar, '-o', 'NUL', '-w', '%{http_code}', "http://127.0.0.1:$WebPort/api/auth/session")
    $logout = Status @('-b', $cookieJar, '-c', $cookieJar, '-o', 'NUL', '-w', '%{http_code}', '-X', 'POST', "http://127.0.0.1:$WebPort/api/auth/logout")
    $afterLogout = Status @('-b', $cookieJar, '-o', 'NUL', '-w', '%{http_code}', "http://127.0.0.1:$WebPort/dashboard")
    $invalidHeaders = Join-Path $temporary 'invalid.headers'
    $invalidSession = Status @('-D', $invalidHeaders, '-b', 'queueflow_access=invalid-token', '-o', 'NUL', '-w', '%{http_code}', "http://127.0.0.1:$WebPort/api/auth/session")

    $forbiddenEmail = "attendant-$stamp@auth.local"
    $forbiddenBody = Json-File 'forbidden-register.json' @{ name = "Forbidden Validation $stamp"; slug = "forbidden-validation-$stamp"; timeZone = 'America/Sao_Paulo'; adminName = "Attendant Validation $stamp"; adminEmail = $forbiddenEmail; password = $password }
    $forbiddenRegistered = Status @('-o', 'NUL', '-w', '%{http_code}', '-H', 'Content-Type: application/json', '--data-binary', "@$forbiddenBody", "http://127.0.0.1:$ApiPort/api/v1/auth/register")
    Require-Status $forbiddenRegistered '201' 'Forbidden-role registration'
    $project = [xml](Get-Content (Join-Path $root 'src\QueueFlow.Api\QueueFlow.Api.csproj') -Raw)
    $secretId = $project.Project.PropertyGroup.UserSecretsId | Where-Object { $_ } | Select-Object -First 1
    $secrets = Get-Content (Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretId\secrets.json") -Raw | ConvertFrom-Json
    $parts = @{}
    $secrets.'ConnectionStrings:QueueFlowDatabase'.Split(';') | ForEach-Object { $pair = $_.Split('=', 2); if ($pair.Count -eq 2) { $parts[$pair[0]] = $pair[1] } }
    $env:PGPASSWORD = $parts.Password
    try {
        $sql = 'UPDATE "Users" SET "Role" = 3 WHERE "Email" = ''' + $forbiddenEmail + ''';'
        $sql | & 'C:\Program Files\PostgreSQL\17\bin\psql.exe' -h $parts.Host -p $parts.Port -U $parts.Username -d $parts.Database -v ON_ERROR_STOP=1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Role update failed.' }
    }
    finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
    $forbiddenJar = Join-Path $temporary 'forbidden.cookies'
    $forbiddenLoginBody = Json-File 'forbidden-login.json' @{ email = $forbiddenEmail; password = $password }
    $forbiddenLogin = Status @('-c', $forbiddenJar, '-o', 'NUL', '-w', '%{http_code}', '-H', 'Content-Type: application/json', '--data-binary', "@$forbiddenLoginBody", "http://127.0.0.1:$WebPort/api/auth/login")
    Require-Status $forbiddenLogin '200' 'Forbidden-role login'
    $forbiddenFile = Join-Path $temporary 'forbidden.html'
    $forbiddenDashboard = Status @('-b', $forbiddenJar, '-o', $forbiddenFile, '-w', '%{http_code}', "http://127.0.0.1:$WebPort/dashboard")

    $checks = [ordered]@{
        'Unauthenticated dashboard redirects' = $unauthenticated -eq '307'
        'Invalid credentials rejected' = $invalid -eq '401'
        'Valid registration accepted' = $registered -eq '201'
        'Valid BFF login accepted' = $login -eq '200'
        'HttpOnly cookie issued' = (Get-Content $loginHeaders -Raw) -match 'HttpOnly'
        'Authenticated dashboard served' = $dashboard -eq '200'
        'Dashboard contains authenticated user' = Select-String -Path $dashboardFile -SimpleMatch -Pattern $name -Quiet
        'Authenticated session endpoint served' = $session -eq '200'
        'Logout redirects' = $logout -eq '303'
        'Dashboard redirects after logout' = $afterLogout -eq '307'
        'Invalid token returns 401' = $invalidSession -eq '401'
        'Invalid cookie cleared' = (Get-Content $invalidHeaders -Raw) -match 'queueflow_access=;'
        'Forbidden-role login succeeds' = $forbiddenLogin -eq '200'
        'Forbidden panel rendered' = $forbiddenDashboard -eq '200' -and (Select-String -Path $forbiddenFile -Pattern 'Acesso n.o permitido' -Quiet)
    }
    $checks.GetEnumerator() | ForEach-Object { Write-Output ($_.Key + ': ' + $_.Value) }
    if ($checks.Values -contains $false) { throw 'One or more authentication E2E checks failed.' }
}
catch {
    Write-Output '--- API LOG ---'; Get-Content $apiOut -ErrorAction SilentlyContinue; Get-Content $apiErr -ErrorAction SilentlyContinue
    Write-Output '--- WEB LOG ---'; Get-Content $webOut -ErrorAction SilentlyContinue; Get-Content $webErr -ErrorAction SilentlyContinue
    throw
}
finally {
    foreach ($port in @($ApiPort, $WebPort)) { Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue } }
    foreach ($process in @($api, $web)) { if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } }
    Remove-Item Env:QUEUEFLOW_API_URL, Env:QUEUEFLOW_SECURE_COOKIES -ErrorAction SilentlyContinue
}

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\src\QueueFlow.Api\QueueFlow.Api.csproj'
$secureAdminPassword = Read-Host 'Senha do administrador PostgreSQL postgres' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureAdminPassword)

try {
    $adminPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $randomBytes = New-Object byte[] 48
    $randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $randomGenerator.GetBytes($randomBytes)
    }
    finally {
        $randomGenerator.Dispose()
    }

    $databasePassword = [Convert]::ToBase64String($randomBytes)
    $psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
    $env:PGPASSWORD = $adminPassword
    "ALTER ROLE queueflow WITH PASSWORD '$databasePassword';" | & $psql -h localhost -U postgres -d postgres -v ON_ERROR_STOP=1

    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível atualizar a senha do usuário queueflow no PostgreSQL.'
    }

    $connectionString = "Host=localhost;Port=5432;Database=queueflow;Username=queueflow;Password=$databasePassword"

    & dotnet user-secrets set 'ConnectionStrings:QueueFlowDatabase' $connectionString --project $projectPath

    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível armazenar a connection string no User Secrets.'
    }

    Write-Host 'Senha do QueueFlow sincronizada e connection string armazenada com sucesso.' -ForegroundColor Green
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    $adminPassword = $null
    $databasePassword = $null
    $connectionString = $null
}

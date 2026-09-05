$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$databaseRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$testFile = Join-Path $PSScriptRoot 'receiver_id_migration_test.sql'
$containerName = "storage-receiver-migration-test-$PID"

if (-not (Test-Path -LiteralPath $testFile)) {
    throw 'SQL-Testdatei wurde nicht gefunden.'
}

try {
    & docker version --format '{{.Server.Version}}' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Desktop ist nicht bereit. Docker zuerst starten.'
    }

    Write-Host 'Starte eine neue, temporaere PostgreSQL-Testdatenbank.'
    $containerId = & docker run --rm -d --name $containerName `
        -e POSTGRES_PASSWORD=receiver_test_password `
        -e POSTGRES_DB=receiver_test `
        -v "${databaseRoot}:/database:ro" `
        postgres:17-alpine
    if ($LASTEXITCODE -ne 0 -or $containerId -notmatch '^[a-f0-9]{12,64}$') {
        throw 'Temporäre PostgreSQL-Testdatenbank konnte nicht gestartet werden.'
    }

    $ready = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        & docker exec $containerName pg_isready -U postgres -d receiver_test | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $ready = $true
            break
        }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) {
        throw 'Temporäre PostgreSQL-Testdatenbank wurde nicht bereit.'
    }

    & docker exec $containerName psql -U postgres -d receiver_test `
        -v ON_ERROR_STOP=1 -f /database/Tests/receiver_id_migration_test.sql
    if ($LASTEXITCODE -ne 0) {
        throw 'SQL-Migrationstest ist fehlgeschlagen.'
    }
    Write-Host 'Migrationstest erfolgreich. Supabase wurde nicht verändert.' -ForegroundColor Green
}
finally {
    $runningContainer = & docker ps --filter "name=^/$containerName$" --format '{{.Names}}' 2>$null
    if ($runningContainer -eq $containerName) {
        & docker stop $containerName | Out-Null
    }
}

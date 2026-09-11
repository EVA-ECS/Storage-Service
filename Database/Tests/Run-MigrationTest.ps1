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
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        $probe = & docker exec $containerName psql -U postgres -d receiver_test `
            -tAc 'select 1' 2>$null
        if ($LASTEXITCODE -eq 0 -and ([string]$probe).Trim() -eq '1') {
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

    Write-Host 'Pruefe zwei gleichzeitig eintreffende erste Nachrichten.'
    & docker exec $containerName pgbench -U postgres -d receiver_test `
        -n -c 2 -j 2 -t 1 -f /database/Tests/concurrent_private_room_test.sql | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Paralleltest für die automatische Raumerstellung ist fehlgeschlagen.'
    }

    $roomCountQuery = @'
SELECT count(*)
FROM public.rooms AS room
WHERE room.is_group = false
  AND EXISTS (
      SELECT 1 FROM public.room_members AS member
      WHERE member.room_id = room.id
        AND member.user_id = '22222222-2222-2222-2222-222222222222'
  )
  AND EXISTS (
      SELECT 1 FROM public.room_members AS member
      WHERE member.room_id = room.id
        AND member.user_id = '33333333-3333-3333-3333-333333333333'
  )
  AND (SELECT count(*) FROM public.room_members AS member WHERE member.room_id = room.id) = 2;
'@
    $parallelRoomCount = & docker exec $containerName psql -U postgres -d receiver_test `
        -tAc $roomCountQuery
    if ($LASTEXITCODE -ne 0 -or ([string]$parallelRoomCount).Trim() -ne '1') {
        throw 'Parallele erste Nachrichten haben nicht genau einen privaten Raum erzeugt.'
    }

    Write-Host 'Migrationstest erfolgreich. Supabase wurde nicht verändert.' -ForegroundColor Green
}
finally {
    $runningContainer = & docker ps --filter "name=^/$containerName$" --format '{{.Names}}' 2>$null
    if ($runningContainer -eq $containerName) {
        & docker stop $containerName | Out-Null
    }
}

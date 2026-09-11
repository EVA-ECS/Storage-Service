param(
    [string]$ComposeFile = $env:E2E_COMPOSE_FILE,
    [string]$Email = $env:E2E_EMAIL,
    [string]$TargetEmail = $env:E2E_TARGET_EMAIL,
    [string]$TargetId = $env:E2E_TARGET_ID,
    [string]$RoomId = $env:E2E_ROOM_ID,
    [string]$BrowserChannel = 'msedge',
    [string]$NuGetConfig,
    [switch]$UseLocalStackConfig,
    [switch]$ShowBrowser
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$githubRoot = Split-Path -Parent $projectRoot
$localSetup = Join-Path $githubRoot 'EVA-ECS-Project\Local-Storage-Test'
if (-not $ComposeFile -and (Test-Path -LiteralPath (Join-Path $localSetup 'compose.yaml'))) {
    $ComposeFile = Join-Path $localSetup 'compose.yaml'
}
if (-not $ComposeFile -or -not (Test-Path -LiteralPath $ComposeFile)) {
    throw 'Compose-Datei der laufenden lokalen Testumgebung mit -ComposeFile angeben.'
}
$ComposeFile = (Resolve-Path -LiteralPath $ComposeFile).Path
$dotnetPath = Join-Path $githubRoot '.dotnet-sdk\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$testProject = Join-Path $PSScriptRoot 'Storage-Service.Tests.csproj'
if (-not $NuGetConfig -and (Test-Path -LiteralPath (Join-Path $localSetup 'NuGet.Local.config'))) {
    $NuGetConfig = Join-Path $localSetup 'NuGet.Local.config'
}

function Read-SecretValue([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$variableNames = @('E2E_RUN','E2E_ALLOW_TEST_WRITES','E2E_COMPOSE_FILE','E2E_EMAIL','E2E_PASSWORD',
    'E2E_TARGET_EMAIL','E2E_TARGET_ID','E2E_ROOM_ID','E2E_BROWSER_CHANNEL','E2E_HEADED','E2E_RESULTS_DIR',
    'E2E_RABBIT_USER','E2E_RABBIT_PASSWORD','SUPABASE_URL','SUPABASE_SECRET_KEY')
$previousValues = @{}
foreach ($variableName in $variableNames) { $previousValues[$variableName] = [Environment]::GetEnvironmentVariable($variableName, 'Process') }
try {
    if ($UseLocalStackConfig) {
        # Nur der ausdrücklich gewählte lokale Compose-Stack, keine Passwortdateien oder Browser-Sessions.
        $projectName = if ($env:E2E_PROJECT) { $env:E2E_PROJECT } else { 'eva-storage-e2e' }
        if (-not $projectName.StartsWith('eva-storage-e2e')) { throw 'Es ist ein eigener eva-storage-e2e-Teststack erforderlich.' }
        $storageContainer = (& docker compose -f $ComposeFile -p $projectName ps -q storage).Trim()
        if ($LASTEXITCODE -ne 0 -or $storageContainer -notmatch '^[a-f0-9]{12,64}$') { throw 'Storage muss bereits im lokalen Teststack laufen.' }
        $stackConfig = @{}
        try {
            $stackLines = & docker inspect $storageContainer --format '{{json .Config.Env}}' | ConvertFrom-Json
            if ($LASTEXITCODE -ne 0) { throw 'Lokale Container-Konfiguration konnte nicht gelesen werden.' }
            foreach ($stackLine in $stackLines) { $stackParts = $stackLine -split '=',2; $stackConfig[$stackParts[0]] = $stackParts[1] }
            $env:SUPABASE_URL = $stackConfig['Supabase__Url']
            $env:SUPABASE_SECRET_KEY = $stackConfig['Supabase__SecretKey']
            $env:E2E_RABBIT_USER = $stackConfig['RabbitMQ__Username']
            $env:E2E_RABBIT_PASSWORD = $stackConfig['RabbitMQ__Password']
        } finally { $stackConfig.Clear(); $stackLines=$null; $stackLine=$null; $stackParts=$null }
    }

    Write-Host 'Echter Anwendungstest: Drei neue Nachrichten werden in eurem Supabase-Testprojekt gespeichert.'
    Write-Host 'Storage wird kurz gestoppt und wieder gestartet. Vorhandene Nachrichten werden nicht geloescht.'
    if (-not $Email) { $Email = Read-Host 'E-Mail des Chat-Testkontos' }
    if (-not $env:E2E_PASSWORD) { $env:E2E_PASSWORD = Read-SecretValue 'Passwort dieses Chat-Testkontos (verdeckt)' }
    if (-not $TargetEmail) { $TargetEmail = Read-Host 'E-Mail des Empfaenger-Testkontos' }
    if (-not $TargetId) { $TargetId = Read-Host 'Benutzer-ID des Empfaengers (UUID)' }
    if (-not $env:SUPABASE_URL) { $env:SUPABASE_URL = Read-Host 'Supabase-Projekt-URL (ohne /rest/v1)' }
    if (-not $env:SUPABASE_SECRET_KEY) { $env:SUPABASE_SECRET_KEY = Read-SecretValue 'Supabase Secret Key (verdeckt)' }
    if (-not $env:E2E_RABBIT_PASSWORD) { $env:E2E_RABBIT_PASSWORD = Read-SecretValue 'RabbitMQ-Passwort des lokalen Testbrokers (verdeckt)' }

    $env:E2E_RUN = '1'
    $env:E2E_ALLOW_TEST_WRITES = '1'
    $env:E2E_COMPOSE_FILE = $ComposeFile
    $env:E2E_EMAIL = $Email.Trim()
    $env:E2E_TARGET_EMAIL = $TargetEmail.Trim()
    $env:E2E_TARGET_ID = ([guid]$TargetId).ToString('D')
    if ($RoomId) {
        $env:E2E_ROOM_ID = ([guid]$RoomId).ToString('D')
    } else {
        Remove-Item Env:E2E_ROOM_ID -ErrorAction SilentlyContinue
    }
    $env:E2E_BROWSER_CHANNEL = $BrowserChannel
    $env:E2E_HEADED = if ($ShowBrowser) { '1' } else { '0' }
    $env:E2E_RESULTS_DIR = Join-Path $PSScriptRoot 'TestResults'
    $restoreArgs = @('restore', $testProject)
    if ($NuGetConfig) { $restoreArgs += @('--configfile', $NuGetConfig) }
    & $dotnetPath @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'NuGet-Restore fehlgeschlagen. Paketquellen/Zugang zum Contracts-Paket pruefen.' }
    & $dotnetPath build $testProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build der Tests fehlgeschlagen.' }
    if (-not $BrowserChannel) {
        & (Join-Path $PSScriptRoot 'bin\Release\net8.0\playwright.ps1') install chromium
        if ($LASTEXITCODE -ne 0) { throw 'Playwright-Browserinstallation fehlgeschlagen.' }
    }
    # Ohne Filter: alle Unit-Tests UND der Anwendungstest laufen zusammen.
    & $dotnetPath test $testProject -c Release --no-build --no-restore --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { throw 'Mindestens ein Test ist fehlgeschlagen. Siehe Testausgabe oben.' }
    Write-Host 'Alle Unit-Tests und der Anwendungstest sind erfolgreich.' -ForegroundColor Green
} finally {
    foreach ($variableName in $variableNames) {
        [Environment]::SetEnvironmentVariable($variableName, $previousValues[$variableName], 'Process')
    }
    $previousValues.Clear()
}

<#
  Prova vera dell'aggiornamento (#92) su Windows, con processi veri: un Mira in esecuzione si chiude, il programma di
  appoggio (Mira.Updater) sostituisce la cartella, riavvia Mira e controlla /api/version; se la versione nuova non risponde
  rimette la vecchia.

  Usa il server autonomo (Mira.Server.exe) al posto del guscio desktop: il programma di appoggio non sa quale eseguibile
  sta aggiornando (glielo dice il parametro --exe), quindi la sostituzione, l'attesa e il ripristino sono gli stessi, ma si
  compila in un minuto invece che con tutto MAUI. Prima di lanciarla serve il frontend compilato (web: npm ci && npm run build).

  Scenari:
    1. aggiornamento riuscito 0.0.1 → 0.0.2: la nuova risponde, la vecchia resta in «Mira.previous», i dati non cambiano;
    2. ripristino: la nuova non risponde con la versione attesa, torna la 0.0.1 (e riparte), i dati non cambiano.

  Con -Desktop la prova usa invece il guscio desktop vero (Mira.exe, MAUI): serve il carico di lavoro MAUI e ci mette di più.

  Uso:  pwsh tests/update-e2e/prova-aggiornamento.ps1 [-Desktop] [-Work <cartella>]
#>
param(
    [switch]$Desktop,
    [string]$Work = (Join-Path ([IO.Path]::GetTempPath()) ("mira-e2e-" + [guid]::NewGuid().ToString("N").Substring(0, 8)))
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$port = 5098
$base = "http://127.0.0.1:$port"
$env:MIRA_URLS = $base
$env:MIRA_DATA_DIR = Join-Path $Work 'dati'
New-Item -ItemType Directory -Force -Path $env:MIRA_DATA_DIR | Out-Null
Set-Content -Path (Join-Path $env:MIRA_DATA_DIR 'config.json') -Value '{ "marcatore": "dati-utente" }'

$exeName = if ($Desktop) { 'Mira.exe' } else { 'Mira.Server.exe' }
$procName = [IO.Path]::GetFileNameWithoutExtension($exeName)

function Publish-Server($version, $out) {
    if ($Desktop) {
        dotnet publish "$repo/src/Mira.Desktop" -c Release --framework net10.0-windows10.0.19041.0 -r win-x64 --self-contained true "-p:Version=$version" -o $out | Out-Null
    } else {
        dotnet publish "$repo/src/Mira.Server" -c Release -r win-x64 --self-contained true "-p:Version=$version" -o $out | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw "publish $version non riuscito" }
}

function Get-Version {
    try { (Invoke-RestMethod "$base/api/version" -TimeoutSec 2).version } catch { $null }
}

function Wait-Version($expected, $seconds) {
    for ($i = 0; $i -lt $seconds; $i++) {
        if ((Get-Version) -eq $expected) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Stop-Mira {
    Get-Process -Name $procName -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill($true); $_.WaitForExit(5000) | Out-Null } catch {} }
}

function Assert($condition, $message) {
    if (-not $condition) { throw "PROVA FALLITA: $message" }
    Write-Host "  ok: $message"
}

function New-Install($from) {
    $install = Join-Path $Work 'Mira'
    foreach ($d in @($install, "$install.update", "$install.previous", "$install.failed")) {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    }
    Copy-Item $from $install -Recurse
    return $install
}

function Invoke-Update($install, $staged, $expectedVersion, $healthSeconds) {
    $server = Start-Process -FilePath (Join-Path $install $exeName) -WorkingDirectory $install -PassThru
    Assert (Wait-Version '0.0.1' 40) 'la versione 0.0.1 parte e risponde'

    # Come fa il server di Mira: il programma di appoggio parte da una copia fuori dall'installazione.
    $helperDir = Join-Path $Work ("helper-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
    New-Item -ItemType Directory -Force -Path $helperDir | Out-Null
    Copy-Item $updater (Join-Path $helperDir 'Mira.Updater.exe')
    $result = Join-Path $env:MIRA_DATA_DIR 'update-result.json'
    if (Test-Path $result) { Remove-Item $result }
    $helperArgs = @(
        '--pid', $server.Id, '--install', $install, '--staged', $staged, '--backup', "$install.previous",
        '--exe', $exeName, '--version', $expectedVersion, '--health-url', "$base/api/version",
        '--cleanup', "$install.update", '--result', $result, '--log', (Join-Path $env:MIRA_DATA_DIR 'update.log'),
        '--health-timeout-seconds', $healthSeconds
    )
    $helper = Start-Process -FilePath (Join-Path $helperDir 'Mira.Updater.exe') -ArgumentList $helperArgs -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 1
    # Mira si chiude da sola, come quando l'utente conferma «Aggiorna».
    $server.Kill($true)
    if (-not $helper.WaitForExit(180000)) { throw 'il programma di appoggio non ha finito in tempo' }
    return $helper.ExitCode
}

Write-Host "Cartella di lavoro: $Work"
try {
    Write-Host '== Compilo due versioni del server (0.0.1 e 0.0.2) e il programma di appoggio'
    Publish-Server '0.0.1' (Join-Path $Work 'a')
    Publish-Server '0.0.2' (Join-Path $Work 'b')
    $updaterDir = Join-Path $Work 'updater'
    dotnet publish "$repo/src/Mira.Updater" -c Release -r win-x64 --self-contained true -o $updaterDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'publish del programma di appoggio non riuscito' }
    $updater = Join-Path $updaterDir 'Mira.Updater.exe'
    Write-Host ("   programma di appoggio: {0:N1} MB" -f ((Get-Item $updater).Length / 1MB))

    Write-Host '== Scenario 1: aggiornamento riuscito 0.0.1 -> 0.0.2'
    $install = New-Install (Join-Path $Work 'a')
    $staged = "$install.update"
    Copy-Item (Join-Path $Work 'b') $staged -Recurse
    $code = Invoke-Update $install $staged '0.0.2' 60
    Assert ($code -eq 0) 'il programma di appoggio finisce con successo'
    Assert ((Get-Version) -eq '0.0.2') 'ora risponde la 0.0.2'
    Assert (Test-Path (Join-Path "$install.previous" $exeName)) 'la versione vecchia è conservata in Mira.previous'
    Assert (-not (Test-Path $staged)) 'la cartella di appoggio dell''estrazione è stata tolta'
    $json = Get-Content (Join-Path $env:MIRA_DATA_DIR 'update-result.json') -Raw | ConvertFrom-Json
    Assert ($json.ok -eq $true -and $json.result -eq 'Updated') 'l''esito «Updated» è scritto nella cartella dei dati'
    Assert ((Get-Content (Join-Path $env:MIRA_DATA_DIR 'config.json') -Raw) -match 'dati-utente') 'i dati dell''utente non sono cambiati'
    Stop-Mira

    Write-Host '== Scenario 2: la nuova non risponde, si ripristina la vecchia'
    $install = New-Install (Join-Path $Work 'a')
    $staged = "$install.update"
    Copy-Item (Join-Path $Work 'b') $staged -Recurse
    # Si chiede di aspettare una versione che la 0.0.2 non dichiara mai: per il programma di appoggio è «non risponde».
    $code = Invoke-Update $install $staged '9.9.9' 8
    Assert ($code -eq 1) 'il programma di appoggio segnala il fallimento'
    Assert (Wait-Version '0.0.1' 40) 'la 0.0.1 è tornata, riparte e risponde'
    Assert (-not (Test-Path "$install.failed")) 'la versione nuova che non andava è stata tolta'
    $json = Get-Content (Join-Path $env:MIRA_DATA_DIR 'update-result.json') -Raw | ConvertFrom-Json
    Assert ($json.ok -eq $false -and $json.result -eq 'RestoredPrevious') 'l''esito «RestoredPrevious» è scritto nella cartella dei dati'
    Assert ((Get-Content (Join-Path $env:MIRA_DATA_DIR 'config.json') -Raw) -match 'dati-utente') 'i dati dell''utente non sono cambiati'
    Stop-Mira

    Write-Host 'TUTTO OK'
}
finally {
    Stop-Mira
    if ($env:MIRA_E2E_KEEP -ne '1') { Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue }
}

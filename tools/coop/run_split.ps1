# Prova dello schermo condiviso (un solo gioco, una sola finestra): lancia il gioco di verifica con -seatstest -splitscreen N e ne legge l'esito.
# La prova gira da dentro il gioco (Scripts/SeatsTest.cs): struttura (corpi, camere, HUD, strati, un solo AudioListener), controller
# indipendenti per giocatore, punti e contatori, bersagli dei nemici, danno, morte e rinascita, pausa con Start, fine dello schermo condiviso.
# Esce con 0 solo se il gioco scrive "seatstest RISULTATO SUPERATA" e termina con codice 0; 1 se qualcosa fallisce o scade il tempo;
# 3 se non si puo' partire (una copia dello stesso exe e' aperta, o con -Graphics un altro gioco gira).
#
#   .\tools\coop\run_split.ps1 -Players 3              senza finestra (batchmode, niente GPU): circa 30 secondi
#   .\tools\coop\run_split.ps1 -Players 4 -Graphics    con la finestra (1280x720, a finestra) e le foto dello schermo in unity_logs\seats
#                                                      (usa la GPU e occupa lo schermo per circa un minuto: non lanciarlo mentre si gioca)
#   .\tools\coop\run_split.ps1 -Players 2 -Candles     col vecchio ciclo a candele: prova anche Provoke e la vista dei nemici
#   .\tools\coop\run_split.ps1 -Players 3 -NoMenu      la partita parte senza menu (-autotest): salta la pausa
#
# Il gioco e' la build di verifica (Builds\Audit), mai Builds\PryingEyes.exe (e' quello dell'utente). I controller sono finti (uno stato XInput
# iniettato per ogni giocatore): la lettura vera di un controller fisico non si prova qui. Con -Graphics la finestra riscrive i valori
# "Screenmanager" del registro di Unity (HKCU\Software\DefaultCompany\PryingEyes): lo script li salva con reg export e li rimette a posto alla fine.
# Log in -LogDir: seatsN.log.
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\unity\PryingEyes\Builds\Audit\PryingEyes.exe'),
    [string]$LogDir = (Join-Path $env:TEMP 'pryingeyes-split'),
    [ValidateRange(2, 4)][int]$Players = 3,
    [switch]$Graphics,
    [switch]$Candles,
    [switch]$NoMenu,
    [int]$TimeoutSec = 240
)
$ErrorActionPreference = 'Continue'
$exe = (Resolve-Path $Exe).Path
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (Get-Process PryingEyes -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { 'una copia di questo exe e'' aperta: non parto'; exit 3 }
if ($Graphics -and (Get-Process PryingEyes -ErrorAction SilentlyContinue)) { 'un altro PryingEyes e'' aperto: con -Graphics non parto (GPU e schermo)'; exit 3 }
New-Item -ItemType Directory -Force $LogDir | Out-Null
$log = Join-Path $LogDir "seats$Players.log"
Remove-Item $log -ErrorAction SilentlyContinue

# argomenti: dal menu (come il bottone) oppure, con -NoMenu, dal gancio di GameManager.Start; sempre il seme 1000 al piano 1
$argv = @('-seed', '1000', '-floor', '1', '-seatstest', '-splitscreen', "$Players", '-logFile', "`"$log`"")
if ($NoMenu) { $argv += @('-autotest', '-nofire', '-noenemies') } else { $argv += @('-menureview', '-nowaves') }
if ($Candles) { $argv += '-candles' }
if ($Graphics) { $argv += @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-seatsshots') }
else { $argv = @('-batchmode', '-nographics') + $argv }

# con la finestra il registro cambia: copia prima, ripristino dopo
$regKey = 'HKCU\Software\DefaultCompany\PryingEyes'
$backup = Join-Path $LogDir 'pryingeyes_registro.reg'
$restore = $false
if ($Graphics) {
    reg export $regKey $backup /y 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $restore = $true } else { 'nel registro non c''e'' ancora la chiave del gioco: niente da ripristinare' }
}

$timedOut = $false
$sw = [Diagnostics.Stopwatch]::StartNew()
try {
    $p = Start-Process -FilePath $exe -ArgumentList $argv -WorkingDirectory $repo -PassThru -WindowStyle $(if ($Graphics) { 'Normal' } else { 'Hidden' })
    "partito (pid $($p.Id)): $Players giocatori, $(if ($Graphics) { 'con la finestra' } else { 'senza grafica' }), $(if ($NoMenu) { 'senza menu' } else { 'dal menu' })$(if ($Candles) { ', ciclo a candele' })"
    if (-not $p.WaitForExit($TimeoutSec * 1000)) { $timedOut = $true; try { $p.Kill() } catch { } }
    else { $p.WaitForExit() }
}
finally {
    if ($restore) { reg import $backup 2>$null | Out-Null; 'registro del gioco rimesso com''era' }
}

$text = if (Test-Path $log) { [IO.File]::ReadAllText($log) } else { '' }
$ok = ([regex]::Matches($text, 'seatstest ok ')).Count
foreach ($m in [regex]::Matches($text, '\[PryingEyes\] seatstest (FALLITO|registro:)[^\r\n]*')) { $m.Value }
"controlli superati: $ok, tempo $([int]$sw.Elapsed.TotalSeconds) s, log $log"
if ($timedOut) { "TIMEOUT: nessun risultato entro $TimeoutSec s"; exit 1 }
$result = [regex]::Match($text, 'seatstest RISULTATO (SUPERATA|FALLITA): (\d+)/(\d+)')
if (-not $result.Success) { 'il gioco non ha scritto il risultato: vedi il log'; exit 1 }
$result.Value
if ($result.Groups[1].Value -eq 'SUPERATA' -and $p.ExitCode -eq 0) { exit 0 }
exit 1

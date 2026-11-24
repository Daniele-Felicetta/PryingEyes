# Prova della cooperativa a piu' processi: un ospite e N ospitati dalla build di verifica (Builds\Audit), tutti senza grafica, ognuno col suo log.
# Esce con 0 solo se tutti i processi hanno chiuso con 0 (le prove con -cooptest escono 0 se superate, 1 se fallite).
#
#   .\tools\coop\run_coop.ps1 -Clients 1 -HostArgs '-cooptest','-seed','2961' -ClientArgs '-cooptest' -TimeoutSec 420
#   .\tools\coop\run_coop.ps1 -Clients 2 -HostArgs '-cooptest','-coopplayers','3','-seed','1000' -ClientArgs '-cooptest','-coopplayers','3'
#   .\tools\coop\run_coop.ps1 -Name tardi -HostArgs '-cooptest','-seed','1000' -ClientArgs '-cooptest','-cooplate'
#   .\tools\coop\run_coop.ps1 -Name foto -HostGraphics -HostArgs '-cooptest','-cooppics','-seed','1000' -ClientArgs '-cooptest','-cooppics'
#                                          (foto del compagno in <LogDir>\foto; usa la GPU: controlla prima che nessun altro la stia usando)
#   .\tools\coop\run_coop.ps1 -Port 7810 -HostArgs '-cooptest','-seed','2961' -ClientArgs '-cooptest'      (a port of your choice)
#
# A port and a log folder per run (T11), so that two checkouts can run co-op checks at the same time:
#   -Port 0 (default)  a free UDP port, found by binding it on 127.0.0.1 and on ::1 (where the host listens); it is printed on the first line
#   -Port N            exactly N (7807 is the game's own default): if it is taken the script says so and exits 3 without starting anything
#   -LogDir            by default unity_logs\coop of the checkout this script is in (git ignores that folder), not a folder shared through %TEMP%
# The host and the guests get the port as -coopport (Coop.BootFromArgs reads it).
#
# Gli ospitati partono -JoinDelaySec secondi dopo l'ospite (+2 s per ciascuno, oppure -ClientDelays). Il gioco lo si compila a parte
# (BuildPlayer.Torre con -buildOut ...\Builds\Audit, vedi README principale; Builds\PryingEyes.exe e' il gioco dell'utente e non va usato per le prove,
# si passa con -Exe solo di proposito). Non parte se una copia di quell'exe e' gia' aperta.
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\unity\PryingEyes\Builds\Audit\PryingEyes.exe'),
    [string]$LogDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'unity_logs\coop'),
    [string]$Name = 'prova',
    [int]$Clients = 1,
    [string[]]$HostArgs = @(),
    [string[]]$ClientArgs = @(),
    [int]$TimeoutSec = 180,
    [int]$JoinDelaySec = 8,
    [int[]]$ClientDelays = @(),
    [string]$Pattern = 'cooptest|coop:|error CS|Exception',
    [ValidateRange(0, 65535)][int]$Port = 0,      # UDP port of the host, passed to every process as -coopport; 0 = a free one for this run
    [switch]$HostGraphics      # l'ospite con grafica (senza -nographics, per le foto di -cooppics); gli ospitati restano senza
)
# The host listens on 127.0.0.1 and on ::1 (-cooplocal), so a port is usable only if both can be bound; a bind that fails
# (taken by another process, or reserved by Windows) means "not free".
function Test-UdpPortFree([int]$Number) {
    $held = New-Object System.Collections.Generic.List[object]
    try {
        $addresses = @([Net.IPAddress]::Loopback)
        if ([Net.Sockets.Socket]::OSSupportsIPv6) { $addresses += [Net.IPAddress]::IPv6Loopback }
        foreach ($a in $addresses) { $held.Add([Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new($a, $Number))) }
        return $true
    }
    catch { return $false }
    finally { foreach ($u in $held) { $u.Close() } }
}
# A free port: let Windows pick an unused one for 127.0.0.1, then check that the same number is free on ::1 too.
function Get-FreeUdpPort {
    for ($try = 0; $try -lt 30; $try++) {
        $u = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
        $candidate = $u.Client.LocalEndPoint.Port
        $u.Close()
        if (Test-UdpPortFree $candidate) { return $candidate }
    }
    throw 'no free UDP port found in 30 tries'
}
$exe = (Resolve-Path $Exe).Path
if (Get-Process PryingEyes -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { 'una copia di questo exe e aperta: non parto'; exit 3 }
if ($Port -eq 0) { $Port = Get-FreeUdpPort; $portNote = 'free, chosen for this run' }
elseif (Test-UdpPortFree $Port) { $portNote = 'asked with -Port' }
else { "UDP port $Port is taken by another process: not starting"; exit 3 }
New-Item -ItemType Directory -Force $LogDir | Out-Null
"UDP port $Port ($portNote), logs in $LogDir"
$common = @('-batchmode', '-nographics', '-autotest')
$procs = @()
$hostLog = Join-Path $LogDir "coop_${Name}_host.txt"
Get-ChildItem $LogDir -Filter "coop_${Name}_*.txt" -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
$sw = [Diagnostics.Stopwatch]::StartNew()
$hostCommon = if ($HostGraphics) { @('-batchmode', '-autotest', '-coopout', (Join-Path $LogDir 'foto')) } else { $common }
$ph = Start-Process -FilePath $exe -ArgumentList ($hostCommon + @('-coophost', '-cooplocal', '-coopname', 'Alice', '-coopport', "$Port", '-logFile', $hostLog) + $HostArgs) -PassThru -WindowStyle Hidden
$ph.PriorityClass = 'BelowNormal'
$procs += $ph
$names = @('Bob', 'Carla', 'Dario')      # nomi di prova: non si usa mai il nome dell'utente di Windows
$next = 0
while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec -and ($next -lt $Clients -or ($procs | Where-Object { -not $_.HasExited }))) {
    if ($next -lt $Clients) {
        $delay = if ($next -lt $ClientDelays.Count) { $ClientDelays[$next] } else { $JoinDelaySec + 2 * $next }
        if ($sw.Elapsed.TotalSeconds -ge $delay) {
            $log = Join-Path $LogDir "coop_${Name}_c$($next + 1).txt"
            $pc = Start-Process -FilePath $exe -ArgumentList ($common + @('-coopjoin', '127.0.0.1', '-coopname', $names[$next], '-coopport', "$Port", '-logFile', $log) + $ClientArgs) -PassThru -WindowStyle Hidden
            $pc.PriorityClass = 'BelowNormal'
            $procs += $pc
            $next++
        }
    }
    Start-Sleep -Milliseconds 500
}
$stuck = @($procs | Where-Object { -not $_.HasExited })
$stuckIds = @($stuck | ForEach-Object { $_.Id })
if ($stuck.Count) { "tempo scaduto: chiudo $($stuck.Count) processi"; $stuck | ForEach-Object { $_.Kill() } }
$codes = @($procs | ForEach-Object { if ($stuckIds -contains $_.Id) { 'ucciso' } else { $_.ExitCode } })
'uscite: ' + ($codes -join ', ') + " in $([int]$sw.Elapsed.TotalSeconds) s"
foreach ($f in (Get-ChildItem $LogDir -Filter "coop_${Name}_*.txt" | Sort-Object Name)) {
    "==== $($f.Name) ($([int]($f.Length / 1024)) KB)"
    Select-String -Path $f.FullName -Pattern $Pattern | Select-Object -First 90 | ForEach-Object { $_.Line }
}
$expectCrash = $ClientArgs -contains '-coopcrash'      # il processo che deve cadere (-coopcrash) esce con un codice diverso da 0, e va bene
$bad = $stuck.Count -gt 0
for ($i = 0; $i -lt $codes.Count; $i++) {
    if ("$($codes[$i])" -eq '0') { continue }
    if ($i -ge 1 -and $expectCrash -and "$($codes[$i])" -ne 'ucciso') { continue }
    $bad = $true
}
if ($bad) { exit 1 }
exit 0

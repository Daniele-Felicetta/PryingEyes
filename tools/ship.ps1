<#
ship.ps1 - commit, build, prova di fumo, push e aggiornamento dell'exe di Prying Eyes, tutto in un passo.

Perche': l'utente (2026-11-13) vuole che a fine di ogni compito commit, build e push li esegua un sottoagente Haiku. Per non
dipendere dal giudizio del sottoagente, la sequenza con i controlli e le trappole gia' risolte sta in questo script.

Uso (il sottoagente lancia due comandi; il secondo si ripete finche' stampa "SHIP: OK" o "SHIP: FALLITO"):
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\ship.ps1 -Detach -MessageFile C:\...\msg.txt -Paths "a/b.cs,c/d.md"
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\ship.ps1 -Wait 540
Senza -Detach lo script gira in primo piano. Per provare senza cambiare nulla: -DryRun.

Passi, nell'ordine:
  controlli  ramo, Unity non aperto sul progetto, nessuna copia del gioco in esecuzione dalla cartella della build
  commit     solo i percorsi di -Paths (mai git add -A), messaggio da -MessageFile, indice vuoto in partenza;
             senza -Paths non si committa nulla
  build      Unity in batchmode a priorita' bassa (CPU, disco, memoria) in Builds\Audit (o -OutDir). -Mode auto: completa
             (BuildPlayer.Windows) se dall'ultimo ship e' cambiato un file che non sia .cs, altrimenti solo script
             (BuildPlayer.Torre -scriptsOnly, ~2 MB scritti invece di centinaia). Il marcatore sta in unity_logs\ship_built.json
  prova      tools\check_navigation.py su -SmokeFloors piani (seme -SmokeSeed), senza grafica: se fallisce non si pusha
  push       origin <ramo corrente>, mai forzato, con i parametri per i file grandi; poi controlla che il remoto abbia lo stesso HEAD
  exe        copia la build in Builds\Prova\PryingEyes.exe (l'exe usato dall'utente) SOLO se il suo gioco e' chiuso: non lo chiude mai.
             Con -WaitGameMin N aspetta fino a N minuti che l'utente lo chiuda

Non annulla mai nulla: le modifiche che build e prova lasciano nell'albero vengono solo elencate come AVVISO.
File: unity_logs\ship_last.log (log), ship_last.status (stato), ship_build.log (Unity), ship_built.json (marcatore).
Uscita 0 solo con "SHIP: OK". Tutto ASCII di proposito: PowerShell 5.1 legge male gli accenti senza BOM.
#>
param(
    [string]$MessageFile = '',
    [string[]]$Paths = @(),
    [ValidateSet('auto', 'full', 'scripts')][string]$Mode = 'auto',
    [string]$OutDir = '',
    [int]$SmokeFloors = 2,
    [int]$SmokeSeed = 1000,
    [int]$BuildTimeoutSec = 1500,
    [int]$WaitGameMin = 0,
    [switch]$NoBuild,
    [switch]$NoPush,
    [switch]$NoExe,
    [switch]$DryRun,
    [switch]$Detach,
    [switch]$Child,
    [int]$Wait = 0
)

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'unity\PryingEyes'
$userBuilds = (Join-Path $proj 'Builds').TrimEnd('\')
$userRun = Join-Path $userBuilds 'Prova'
$logDir = Join-Path $repo 'unity_logs'
$logFile = Join-Path $logDir 'ship_last.log'
$statusFile = Join-Path $logDir 'ship_last.status'
$markerFile = Join-Path $logDir 'ship_built.json'
$buildLog = Join-Path $logDir 'ship_build.log'
$attribution = 'Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>'
if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
if ($OutDir -eq '') { $buildOut = Join-Path $userBuilds 'Audit' } else { $buildOut = [IO.Path]::GetFullPath($OutDir) }
$buildOut = $buildOut.TrimEnd('\')

function Get-Status {
    if (-not (Test-Path -LiteralPath $statusFile)) { return '' }
    try { return (Get-Content -LiteralPath $statusFile -Raw -ErrorAction Stop).Trim() } catch { return $null }
}
function Test-Alive($st) {
    $m = [regex]::Match([string]$st, 'pid=(\d+)')
    if (-not $m.Success) { return $false }
    return [bool](Get-Process -Id ([int]$m.Groups[1].Value) -ErrorAction SilentlyContinue)
}

# ---------------------------------------------------------------- -Wait: aspetta la fine di un -Detach e stampa il riepilogo
if ($Wait -gt 0) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    if ((Get-Status) -eq '') { 'SHIP: nessuna esecuzione registrata (lancia prima con -Detach)'; exit 3 }
    $st = ''
    while ($true) {
        $st = Get-Status
        if ($st -like 'FINITO*') { break }
        if ($null -ne $st -and -not (Test-Alive $st)) {
            Start-Sleep -Seconds 3
            $st = Get-Status
            if ($st -like 'FINITO*') { break }
            "SHIP: INTERROTTO - il processo e' sparito senza finire (stato: $st). Ultime righe del log:"
            Get-Content -LiteralPath $logFile -Tail 25
            exit 3
        }
        if ($sw.Elapsed.TotalSeconds -ge $Wait) {
            "ANCORA IN CORSO ($st). Rilancia lo stesso comando -Wait $Wait."
            Get-Content -LiteralPath $logFile -Tail 3
            exit 2
        }
        Start-Sleep -Seconds 5
    }
    $lines = @(Get-Content -LiteralPath $logFile)
    $idx = -1
    for ($k = $lines.Count - 1; $k -ge 0; $k--) { if ($lines[$k] -like '=== RIEPILOGO*') { $idx = $k; break } }
    $ok = ($st -like '*SHIP: OK*')
    if (-not $ok -and $idx -gt 0) {
        '--- ultime righe prima del riepilogo:'
        $lines[([Math]::Max(0, $idx - 40))..($idx - 1)]
    }
    if ($idx -ge 0) { $lines[$idx..($lines.Count - 1)] } else { $lines | Select-Object -Last 30 }
    exit ([int](-not $ok))
}

# ---------------------------------------------------------------- -Detach: rilancia se stesso nascosto e torna subito
if ($Detach) {
    $st = Get-Status
    if ($st -like 'IN CORSO*' -and (Test-Alive $st)) { "GIA' IN CORSO ($st): non ne avvio un altro. Usa -Wait 540."; exit 4 }
    Set-Content -LiteralPath $logFile -Value ('ship ' + (Get-Date -Format 's') + ' (avvio in disparte)') -Encoding UTF8
    $argLine = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Child'
    foreach ($kv in $PSBoundParameters.GetEnumerator()) {
        if ($kv.Key -in @('Detach', 'Wait', 'Child')) { continue }
        if ($kv.Value -is [System.Management.Automation.SwitchParameter]) {
            if ($kv.Value.IsPresent) { $argLine += ' -' + $kv.Key }
        } elseif ($kv.Value -is [array]) {
            $argLine += ' -' + $kv.Key + ' "' + ($kv.Value -join ',') + '"'
        } else {
            $argLine += ' -' + $kv.Key + ' "' + $kv.Value + '"'
        }
    }
    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argLine -WindowStyle Hidden -PassThru
    Set-Content -LiteralPath $statusFile -Value ('IN CORSO: avvio pid=' + $p.Id) -Encoding UTF8
    "AVVIATO (pid $($p.Id)). Ora lancia: powershell -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Wait 540"
    exit 0
}

# ---------------------------------------------------------------- corpo
if (-not $Child) {
    $st0 = Get-Status
    if ($st0 -like 'IN CORSO*' -and (Test-Alive $st0) -and ($st0 -notmatch ('pid=' + $PID + '\b'))) { "Un altro ship e' in corso ($st0): non parto."; exit 4 }
}
$env:GIT_TERMINAL_PROMPT = '0'      # niente richieste interattive: se servono credenziali il push fallisce subito invece di restare appeso
$env:GCM_INTERACTIVE = 'never'

if (-not ('ShipPrio' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShipPrio {
    [DllImport("ntdll.dll")] static extern int NtSetInformationProcess(IntPtr h, int cls, ref int val, int len);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    // classe 33 = priorita I/O (0 molto bassa, 1 bassa, 2 normale): usare 1, con 0 le scritture non finiscono piu; classe 39 = priorita di memoria (0..7, normale 5)
    public static int Set(int pid, int cls, int val) {
        IntPtr h = OpenProcess(0x0200, false, pid);
        if (h == IntPtr.Zero) return -1;
        int v = val;
        int r = NtSetInformationProcess(h, cls, ref v, 4);
        CloseHandle(h);
        return r;
    }
}
'@
}
[Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'    # i figli (git, robocopy, python, Unity) la ereditano
$null = [ShipPrio]::Set($PID, 33, 1)

$summary = [ordered]@{}
$warns = New-Object System.Collections.Generic.List[string]
$swAll = [Diagnostics.Stopwatch]::StartNew()
$script:step = 'avvio'
$script:gitExit = 0
$script:builtNow = $false
Set-Content -LiteralPath $logFile -Value ('ship ' + (Get-Date -Format 's') + '  pid=' + $PID + $(if ($DryRun) { '  PROVA A SECCO' } else { '' })) -Encoding UTF8

function Log([string]$m) {
    Write-Host $m
    try { Add-Content -LiteralPath $logFile -Value $m -Encoding UTF8 -ErrorAction Stop } catch { }
}
function Set-Status([string]$s) { try { Set-Content -LiteralPath $statusFile -Value $s -Encoding UTF8 -ErrorAction Stop } catch { } }
function Step([string]$name) {
    $script:step = $name
    Set-Status ("IN CORSO: $name pid=$PID")
    Log ''
    Log ("--- $name (" + (Get-Date -Format 'HH:mm:ss') + ')')
}
function Finish([bool]$ok, [string]$why) {
    Log ''
    Log '=== RIEPILOGO SHIP ==='
    foreach ($k in $summary.Keys) { Log (('{0,-8}' -f ($k + ':')) + ' ' + $summary[$k]) }
    foreach ($w in $warns) { Log ('AVVISO   ' + $w) }
    Log ('durata   ' + [int]$swAll.Elapsed.TotalSeconds + ' s' + $(if ($DryRun) { '  (PROVA A SECCO: non e'' stato cambiato nulla)' } else { '' }))
    if ($ok) { $final = 'SHIP: OK' } else { $final = 'SHIP: FALLITO - ' + $why }
    Log $final
    Set-Status ('FINITO: ' + $final)
    exit ([int](-not $ok))
}
trap {
    Log ('ECCEZIONE nel passo ' + $script:step + ': ' + $_.Exception.Message + ' (riga ' + $_.InvocationInfo.ScriptLineNumber + ')')
    Finish $false ('eccezione nel passo ' + $script:step)
}

# git locale: restituisce le righe (stdout+stderr), l'uscita e' in $script:gitExit. Sempre @(GitLocal ...) per contare.
# Attenzione: NON chiamarla Git: PowerShell non distingue maiuscole e minuscole e una funzione batte git.exe (ricorsione infinita).
function GitLocal([string[]]$a) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $lines = @(& git -C $repo @a 2>&1 | ForEach-Object { "$_".TrimEnd() })
    $script:gitExit = $LASTEXITCODE
    $ErrorActionPreference = $old
    return $lines
}
# git di rete (ls-remote, push): con tempo massimo, processo e figli uccisi allo scadere (sono processi miei)
function GitNet([string[]]$a, [int]$timeoutSec) {
    $so = Join-Path $logDir 'ship_git_out.txt'
    $se = Join-Path $logDir 'ship_git_err.txt'
    foreach ($f in @($so, $se)) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }
    $q = @('-C', ('"' + $repo + '"')) + $a
    $p = Start-Process -FilePath 'git' -ArgumentList $q -NoNewWindow -PassThru -RedirectStandardOutput $so -RedirectStandardError $se
    $null = $p.Handle
    if (-not $p.WaitForExit($timeoutSec * 1000)) {
        & taskkill /F /T /PID $p.Id 2>&1 | Out-Null
        $script:gitExit = -1
        return @("tempo scaduto dopo $timeoutSec s")
    }
    $script:gitExit = $p.ExitCode
    $lines = @()
    foreach ($f in @($so, $se)) {
        if (Test-Path -LiteralPath $f) { $lines += @(Get-Content -LiteralPath $f | ForEach-Object { "$_".TrimEnd() }) }
    }
    return $lines
}
function Get-Dirty { return @(GitLocal @('status', '--porcelain') | Where-Object { $_ }) }
function Short([string]$h) { if ($h.Length -ge 7) { return $h.Substring(0, 7) } else { return $h } }

# ---------------------------------------------------------------- controlli
Step 'controlli'
$branch = @(GitLocal @('rev-parse', '--abbrev-ref', 'HEAD'))[0]
if ($script:gitExit -ne 0 -or $branch -eq 'HEAD') { Finish $false 'controlli: ramo non valido (HEAD staccato?)' }
$headStart = @(GitLocal @('rev-parse', 'HEAD'))[0]
$unityExe = $null
$ver = (Get-Content -LiteralPath (Join-Path $proj 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1)
if ($ver) { $unityExe = 'C:\Program Files\Unity\Hub\Editor\' + (($ver -split ':')[1].Trim()) + '\Editor\Unity.exe' }
Log "repo $repo, ramo $branch, HEAD $(Short $headStart), uscita build $buildOut"
Log ('disco libero: ' + [int]((Get-PSDrive -Name $repo.Substring(0, 1)).Free / 1GB) + ' GB')
if (-not $NoBuild) {
    if (-not $unityExe -or -not (Test-Path -LiteralPath $unityExe)) { Finish $false "controlli: Unity non trovato ($unityExe)" }
    foreach ($u in @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'")) {
        $cl = [string]$u.CommandLine -replace '/', '\'
        if ($cl.IndexOf($proj, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Finish $false "controlli: Unity e' gia' aperto su questo progetto (pid $($u.ProcessId)): non lo chiudo" }
    }
}
if ($buildOut -eq $userBuilds) { Finish $false "controlli: la cartella di uscita e' quella dell'utente (Builds): non si costruisce li'" }
if ($buildOut -eq $userRun) { Finish $false "controlli: la cartella di uscita e' Prova, usata dall'utente: non si costruisce li'" }

# ---------------------------------------------------------------- commit
function Do-Commit {
    Step 'commit'
    $want = @()
    foreach ($p0 in $Paths) { foreach ($p1 in ($p0 -split ',')) { $t = $p1.Trim().Trim('"').Trim("'"); if ($t) { $want += $t } } }
    if ($want.Count -eq 0) {
        $summary['commit'] = 'nessun percorso indicato (-Paths): niente da committare, HEAD ' + (Short $headStart)
        return
    }
    $repoFwd = $repo.Replace('\', '/').TrimEnd('/') + '/'
    $norm = @()
    foreach ($t in $want) {
        $x = $t.Replace('\', '/')
        if ($x.StartsWith($repoFwd, [StringComparison]::OrdinalIgnoreCase)) { $x = $x.Substring($repoFwd.Length) }
        if ($x -eq '.' -or $x -match '[\*\?\[]' -or $x.StartsWith(':') -or $x.StartsWith('/') -or $x -match '^[A-Za-z]:' -or $x -match '(^|/)\.\.(/|$)' -or $x.StartsWith('-')) {
            Finish $false "commit: percorso non ammesso '$t' (servono percorsi relativi precisi, mai '.', '*' o fuori dal repo)"
        }
        $norm += $x
    }
    $pre = @(GitLocal @('diff', '--cached', '--name-only') | Where-Object { $_ })
    if ($pre.Count -gt 0) { Finish $false ("commit: l'indice non e' vuoto ($($pre.Count) file gia' in stage, es. $($pre[0])): non committo roba che non so cos'e'") }
    if (-not $MessageFile -or -not (Test-Path -LiteralPath $MessageFile)) { Finish $false 'commit: manca -MessageFile (file col messaggio) o non esiste' }
    $msg = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $MessageFile).Path).TrimEnd()
    if ($msg -notmatch '(?m)^Co-Authored-By:') { $msg += "`n`n" + $attribution }
    $msgTmp = Join-Path $logDir 'ship_commit_msg.txt'
    [IO.File]::WriteAllText($msgTmp, ($msg + "`n"), (New-Object Text.UTF8Encoding($false)))
    Log ('messaggio: ' + ($msg -split "`n")[0])
    if ($DryRun) {
        $o = @(GitLocal (@('add', '-n', '--') + $norm))
        $o | Select-Object -First 20 | ForEach-Object { Log ('  ' + $_) }
        if ($script:gitExit -ne 0) { Finish $false 'commit: (prova a secco) git add -n ha fallito: percorso inesistente?' }
        $summary['commit'] = '(prova a secco) metterei in stage ' + $norm.Count + ' percorsi: ' + (($norm | Select-Object -First 4) -join ', ')
        return
    }
    $o = @(GitLocal (@('add', '--') + $norm))
    if ($script:gitExit -ne 0) {
        $o | ForEach-Object { Log ('  ' + $_) }
        $null = GitLocal (@('reset', '-q', 'HEAD', '--') + $norm)
        Finish $false 'commit: git add ha fallito (percorso inesistente?); indice ripristinato'
    }
    $st = @(GitLocal @('diff', '--cached', '--name-status') | Where-Object { $_ })
    if ($st.Count -eq 0) {
        $summary['commit'] = 'i percorsi indicati non hanno modifiche: niente da committare, HEAD ' + (Short $headStart)
        return
    }
    Log ("in stage: $($st.Count) file")
    $st | Select-Object -First 40 | ForEach-Object { Log ('  ' + $_) }
    if ($st.Count -gt 40) { Log '  ...' }
    $o = @(GitLocal @('commit', '-F', $msgTmp))
    if ($script:gitExit -ne 0) {
        $o | ForEach-Object { Log ('  ' + $_) }
        $null = GitLocal (@('reset', '-q', 'HEAD', '--') + $norm)
        Finish $false 'commit: git commit ha fallito; indice ripristinato'
    }
    $summary['commit'] = (@(GitLocal @('log', '-1', '--format=%h %s'))[0]) + " ($($st.Count) file)"
}

# ---------------------------------------------------------------- build
function Get-BuildMode {
    $resA = Join-Path $buildOut 'PryingEyes_Data\resources.assets'
    if ($Mode -eq 'full') { return @('full', 'richiesta') }
    if (-not (Test-Path -LiteralPath (Join-Path $buildOut 'PryingEyes.exe')) -or -not (Test-Path -LiteralPath $resA)) { return @('full', "nessuna build completa in $buildOut") }
    if ($Mode -eq 'scripts') { return @('scripts', 'richiesta') }
    if (-not (Test-Path -LiteralPath $markerFile)) { return @('full', 'nessun marcatore di un ship precedente: non so se i dati sono aggiornati') }
    try { $m = Get-Content -LiteralPath $markerFile -Raw | ConvertFrom-Json } catch { return @('full', 'marcatore illeggibile') }
    if ($m.out -ne $buildOut) { return @('full', "il marcatore riguarda un'altra cartella ($($m.out))") }
    if ([string]$m.resTicks -ne [string](Get-Item -LiteralPath $resA).LastWriteTimeUtc.Ticks) { return @('full', "la cartella e' stata ricostruita da altri dopo l'ultimo ship") }
    $null = GitLocal @('cat-file', '-e', ([string]$m.commit + '^{commit}'))
    if ($script:gitExit -ne 0) { return @('full', 'il commit del marcatore non esiste piu') }
    $changed = @(GitLocal @('diff', '--name-only', [string]$m.commit, '--', 'unity/PryingEyes/Assets', 'unity/PryingEyes/ProjectSettings', 'unity/PryingEyes/Packages') | Where-Object { $_ })
    $data = @($changed | Where-Object { $_ -notmatch '\.cs$' -and $_ -notmatch '\.cs\.meta$' })
    if ($data.Count -gt 0) { return @('full', "dati cambiati dall'ultimo ship ($($data.Count) file, es. $($data[0]))") }
    return @('scripts', "dall'ultimo ship ($(Short ([string]$m.commit))) e' cambiato solo codice ($($changed.Count) file .cs)")
}

function Test-Fresh {
    # vero se buildOut e' stata costruita da questo script a partire dal HEAD attuale (serve solo a chi salta la build ma vuole l'exe)
    $resA = Join-Path $buildOut 'PryingEyes_Data\resources.assets'
    if (-not (Test-Path -LiteralPath $markerFile) -or -not (Test-Path -LiteralPath $resA)) { return $false }
    try { $m = Get-Content -LiteralPath $markerFile -Raw | ConvertFrom-Json } catch { return $false }
    if ($m.out -ne $buildOut) { return $false }
    if ([string]$m.resTicks -ne [string](Get-Item -LiteralPath $resA).LastWriteTimeUtc.Ticks) { return $false }
    return ([string]$m.commit -eq @(GitLocal @('rev-parse', 'HEAD'))[0])
}

function Do-Build {
    if ($NoBuild) { $summary['build'] = 'saltata (-NoBuild)'; $summary['prova'] = 'saltata (-NoBuild)'; return }
    Step 'build'
    $exeOut = Join-Path $buildOut 'PryingEyes.exe'
    foreach ($pr in @(Get-Process PryingEyes -ErrorAction SilentlyContinue)) {
        if ($pr.Path -and $pr.Path.StartsWith($buildOut + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Finish $false "build: una copia del gioco gira da $buildOut (pid $($pr.Id)): non la chiudo"
        }
    }
    $d = Get-BuildMode
    $mode2 = [string]$d[0]
    Log ("modo: $mode2 - " + $d[1])
    if ($mode2 -eq 'scripts') { $method = 'PryingEyes.EditorTools.BuildPlayer.Torre' } else { $method = 'PryingEyes.EditorTools.BuildPlayer.Windows' }
    $a = @('-batchmode', '-projectPath', ('"' + $proj + '"'), '-executeMethod', $method, '-buildOut', ('"' + $buildOut + '"'), '-quit', '-logFile', ('"' + $buildLog + '"'))
    if ($mode2 -eq 'scripts') { $a += '-scriptsOnly' }
    Log ('unity: ' + ($a -join ' '))
    if ($DryRun) { $summary['build'] = "(prova a secco) $mode2 in $buildOut ($($d[1]))"; return }
    $before = Get-Dirty
    if (Test-Path -LiteralPath $buildLog) { Remove-Item -LiteralPath $buildLog -Force }    # un log vecchio fa credere che sia andata bene
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $unityExe -ArgumentList $a -PassThru -WindowStyle Hidden
    $null = $p.Handle
    $p.PriorityClass = 'BelowNormal'
    $io = [ShipPrio]::Set($p.Id, 33, 1)
    $mem = [ShipPrio]::Set($p.Id, 39, 2)
    Log "unity pid $($p.Id): CPU BelowNormal, disco bassa (esito $io), memoria bassa (esito $mem)"
    $lastNote = 0
    while (-not $p.HasExited) {
        if ($sw.Elapsed.TotalSeconds -gt $BuildTimeoutSec) { Finish $false "build: tempo scaduto dopo $BuildTimeoutSec s; Unity (pid $($p.Id)) e' ancora attivo, non l'ho chiuso" }
        Start-Sleep -Seconds 2
        if ($sw.Elapsed.TotalSeconds - $lastNote -ge 60) {
            $lastNote = $sw.Elapsed.TotalSeconds
            Set-Status ('IN CORSO: build ' + [int]$sw.Elapsed.TotalSeconds + ' s pid=' + $PID)
        }
    }
    $p.WaitForExit()
    $code = $p.ExitCode
    $res = @()
    if (Test-Path -LiteralPath $buildLog) { $res = @(Select-String -LiteralPath $buildLog -Pattern 'error CS\d+|\[PryingEyes\] Build result|Build Finished') }
    Log ("unity finito: uscita $code in " + [int]$sw.Elapsed.TotalSeconds + ' s')
    $res | Select-Object -Last 6 | ForEach-Object { Log ('  ' + $_.Line.Trim()) }
    $csErr = @($res | Where-Object { $_.Line -match 'error CS\d+' })
    if ($csErr.Count -gt 0) { Finish $false ("build: errori di compilazione ($($csErr.Count)), primo: " + $csErr[0].Line.Trim()) }
    $okLine = @($res | Where-Object { $_.Line -match '\[PryingEyes\] Build result: Succeeded' }).Count -gt 0
    if ($code -ne 0 -or -not $okLine) { Finish $false "build: Unity uscito con $code e senza 'Build result: Succeeded' (vedi unity_logs\ship_build.log)" }
    $dll = Join-Path $buildOut 'PryingEyes_Data\Managed\Assembly-CSharp.dll'
    $resA = Join-Path $buildOut 'PryingEyes_Data\resources.assets'
    foreach ($f in @($exeOut, $dll, $resA)) { if (-not (Test-Path -LiteralPath $f)) { Finish $false "build: dopo la build manca $f" } }
    $di = Get-Item -LiteralPath $dll
    Log ('Assembly-CSharp.dll: ' + $di.LastWriteTime.ToString('s') + ', ' + $di.Length + ' byte')
    $mk = [ordered]@{
        out = $buildOut
        commit = @(GitLocal @('rev-parse', 'HEAD'))[0]
        resTicks = [string](Get-Item -LiteralPath $resA).LastWriteTimeUtc.Ticks
        mode = $mode2
        when = (Get-Date -Format 's')
    }
    ($mk | ConvertTo-Json) | Set-Content -LiteralPath $markerFile -Encoding UTF8
    $script:builtNow = $true
    $summary['build'] = "$mode2 in " + [int]$sw.Elapsed.TotalSeconds + " s -> $buildOut (Assembly-CSharp.dll del " + $di.LastWriteTime.ToString('s') + ')'
    $new = @(Get-Dirty | Where-Object { $before -notcontains $_ })
    if ($new.Count -gt 0) { $warns.Add("la build ha lasciato $($new.Count) modifiche nell'albero (non le ho annullate): " + (($new | Select-Object -First 6) -join ' | ')) }
}

# ---------------------------------------------------------------- prova di fumo
function Do-Smoke {
    if ($NoBuild) { return }
    if ($SmokeFloors -le 0) { $summary['prova'] = 'saltata (-SmokeFloors 0)'; return }
    Step 'prova'
    $py = Get-Command python -ErrorAction SilentlyContinue
    if (-not $py) { Finish $false 'prova: python non trovato nel PATH' }
    $nav = Join-Path $logDir 'navigation_ship'
    $a = @('-I', (Join-Path $repo 'tools\check_navigation.py'), '--exe', (Join-Path $buildOut 'PryingEyes.exe'), '--seeds', "$SmokeSeed", '--floors', "$SmokeFloors", '--jobs', '1', '--out', $nav, '--timeout', '900')
    Log ('python ' + ($a -join ' '))
    if ($DryRun) { $summary['prova'] = "(prova a secco) camminata rigorosa, seme $SmokeSeed, $SmokeFloors piani"; return }
    $before = Get-Dirty
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $o = @(& python @a 2>&1 | ForEach-Object { "$_".TrimEnd() })
    $code = $LASTEXITCODE
    $o | Select-Object -First 60 | ForEach-Object { Log ('  ' + $_) }
    $last = [string]($o | Where-Object { $_ -match 'esecuzioni riuscite' } | Select-Object -Last 1)
    if ($code -ne 0) { Finish $false "prova: la camminata di fumo e' fallita (uscita $code): $last" }
    $summary['prova'] = "camminata rigorosa, seme $SmokeSeed, $SmokeFloors piani: $last (" + [int]$sw.Elapsed.TotalSeconds + ' s)'
    $new = @(Get-Dirty | Where-Object { $before -notcontains $_ })
    if ($new.Count -gt 0) { $warns.Add("la prova ha lasciato $($new.Count) modifiche nell'albero (non le ho annullate): " + (($new | Select-Object -First 6) -join ' | ')) }
}

# ---------------------------------------------------------------- push
function Do-Push {
    if ($NoPush) { $summary['push'] = 'saltato (-NoPush)'; return }
    Step 'push'
    $local = @(GitLocal @('rev-parse', 'HEAD'))[0]
    $rem = @(GitNet @('ls-remote', 'origin', "refs/heads/$branch") 60)
    if ($script:gitExit -ne 0) { Finish $false ('push: non riesco a leggere il remoto: ' + ($rem | Select-Object -Last 1)) }
    $rb = ''
    if ($rem.Count -gt 0 -and $rem[0] -match '^[0-9a-f]{40}') { $rb = ($rem[0] -split '\s+')[0] }
    if ($rb -eq $local) { $summary['push'] = "niente da pushare: origin/$branch e' gia' a " + (Short $local); return }
    if ($rb -eq '') {
        $ahead = 'ramo nuovo'
    } else {
        $ahead = @(GitLocal @('rev-list', '--count', "$rb..$local"))[0]
        if ($script:gitExit -ne 0) { $ahead = '?' }
    }
    Log ('remoto ' + $(if ($rb) { Short $rb } else { '(ramo assente)' }) + ', locale ' + (Short $local) + ", commit da pushare: $ahead")
    $flags = @('-c', 'http.postBuffer=524288000', '-c', 'http.lowSpeedLimit=0', '-c', 'http.lowSpeedTime=999999', '-c', 'pack.threads=0', '-c', 'core.compression=1')
    $cmd = $flags + @('push') + $(if ($DryRun) { @('--dry-run') } else { @() }) + @('origin', $branch)
    $o = @(GitNet $cmd 1200)
    $o | Select-Object -First 15 | ForEach-Object { Log ('  ' + $_) }
    if ($script:gitExit -ne 0) { Finish $false ("push: rifiutato o fallito (uscita $($script:gitExit)): " + ($o | Select-Object -Last 1)) }
    if ($DryRun) { $summary['push'] = '(prova a secco) pushabile: ' + $(if ($rb) { Short $rb } else { '(ramo nuovo)' }) + '..' + (Short $local) + " ($ahead commit)"; return }
    $rem2 = @(GitNet @('ls-remote', 'origin', "refs/heads/$branch") 60)
    $ra = ''
    if ($rem2.Count -gt 0 -and $rem2[0] -match '^[0-9a-f]{40}') { $ra = ($rem2[0] -split '\s+')[0] }
    if ($ra -ne $local) { Finish $false "push: dopo il push il remoto e' a '$ra' invece di $local" }
    $summary['push'] = "origin/$branch " + $(if ($rb) { Short $rb } else { '(nuovo)' }) + '..' + (Short $local) + " ($ahead commit), il remoto ha lo stesso HEAD"
}

# ---------------------------------------------------------------- exe dell'utente
function Test-GameOpen {
    $mine = @((Join-Path $userRun 'PryingEyes.exe'), (Join-Path $userRun 'UnityCrashHandler64.exe'))
    foreach ($n in 'PryingEyes', 'UnityCrashHandler64') {
        foreach ($pr in @(Get-Process $n -ErrorAction SilentlyContinue)) {
            if ($pr.Path -and ($mine -contains $pr.Path)) { return $true }
        }
    }
    return $false
}

function Do-Exe {
    if ($NoExe) { $summary['exe'] = 'saltato (-NoExe)'; return }
    Step 'exe'
    $src = $buildOut
    $dst = $userRun
    if (-not $DryRun -and -not $script:builtNow -and -not (Test-Fresh)) {
        $summary['exe'] = "NON aggiornato: la build in $src non e' allineata al HEAD attuale (rifai la build)"
        $warns.Add($summary['exe'])
        return
    }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (Test-GameOpen) {
        if ($WaitGameMin -le 0 -or $sw.Elapsed.TotalMinutes -ge $WaitGameMin) {
            $summary['exe'] = "NON aggiornato: il gioco dell'utente e' aperto da Builds\Prova (non l'ho chiuso)"
            $warns.Add($summary['exe'] + '; va copiato da ' + $src + ' quando lo chiude')
            return
        }
        Set-Status ("IN CORSO: exe, aspetto che l'utente chiuda il gioco pid=$PID")
        Start-Sleep -Seconds 3
    }
    if ($DryRun) { $summary['exe'] = "(prova a secco) il gioco e' chiuso: copierei $src -> $dst"; return }
    if ($sw.Elapsed.TotalSeconds -gt 1) { Start-Sleep -Seconds 2 }
    $dirs = @('D3D12', 'MonoBleedingEdge', 'PryingEyes_Data')
    foreach ($x in @(Get-ChildItem -LiteralPath $src -Directory | Where-Object { $dirs -notcontains $_.Name -and $_.Name -notlike '*BackUpThisFolder*' -and $_.Name -notlike '*BurstDebugInformation*' })) {
        $warns.Add("la build ha una cartella che non copio: $($x.Name)")
    }
    $rootFiles = @(Get-ChildItem -LiteralPath $src -File | Where-Object { $_.Extension -in '.exe', '.dll' } | ForEach-Object { $_.Name })
    $rc = @('/J', '/R:3', '/W:5', '/NP', '/NFL', '/NDL', '/NJH')
    $bad = 0
    $t = [Diagnostics.Stopwatch]::StartNew()
    $o = @(& robocopy $src $dst @rootFiles @rc 2>&1 | ForEach-Object { "$_".TrimEnd() })
    if ($LASTEXITCODE -ge 8) { $bad++ }
    $o | Where-Object { $_ -match 'Files :|Bytes :|ERROR' } | ForEach-Object { Log ('  ' + $_.Trim()) }
    foreach ($d in $dirs) {
        $o = @(& robocopy (Join-Path $src $d) (Join-Path $dst $d) /MIR @rc 2>&1 | ForEach-Object { "$_".TrimEnd() })
        if ($LASTEXITCODE -ge 8) { $bad++ }
        Log ("  $d")
        $o | Where-Object { $_ -match 'Files :|Bytes :|ERROR' } | ForEach-Object { Log ('    ' + $_.Trim()) }
    }
    Log ('copia finita in ' + [int]$t.Elapsed.TotalSeconds + " s, errori di robocopy: $bad")
    $n = 0
    $mis = 0
    foreach ($f in $rootFiles) {
        $n++
        $o2 = Join-Path $dst $f
        if (-not (Test-Path -LiteralPath $o2) -or (Get-Item -LiteralPath $o2).Length -ne (Get-Item -LiteralPath (Join-Path $src $f)).Length) { $mis++; Log "  DIVERSO: $f" }
    }
    foreach ($d in $dirs) {
        foreach ($f in @(Get-ChildItem -LiteralPath (Join-Path $src $d) -Recurse -File -Force)) {
            $n++
            $rel = $f.FullName.Substring($src.Length + 1)
            $o2 = Join-Path $dst $rel
            if (-not (Test-Path -LiteralPath $o2) -or (Get-Item -LiteralPath $o2).Length -ne $f.Length) { $mis++; Log "  DIVERSO: $rel" }
        }
        foreach ($f in @(Get-ChildItem -LiteralPath (Join-Path $dst $d) -Recurse -File -Force)) {
            $rel = $f.FullName.Substring($dst.Length + 1)
            if (-not (Test-Path -LiteralPath (Join-Path $src $rel))) { $mis++; Log "  IN PIU: $rel" }
        }
    }
    $hashOk = $true
    foreach ($k in 'PryingEyes_Data\Managed\Assembly-CSharp.dll', 'PryingEyes_Data\resources.assets', 'PryingEyes_Data\level0') {
        $h1 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $src $k)).Hash
        $h2 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $dst $k)).Hash
        Log (('  {0,-46} {1}' -f $k, $(if ($h1 -eq $h2) { 'uguale' } else { 'DIVERSO' })))
        if ($h1 -ne $h2) { $hashOk = $false; $mis++ }
    }
    $summary['exe'] = "aggiornato in ${dst}: $n file controllati, $mis differenze, SHA-256 di dll/resources/level0 " + $(if ($hashOk) { 'uguali' } else { 'DIVERSI' })
    if ($bad -gt 0 -or $mis -gt 0) { Finish $false "exe: la copia ha problemi ($bad errori di robocopy, $mis differenze)" }
}

Do-Commit
Do-Build
Do-Smoke
Do-Push
Do-Exe

Step 'chiusura'
$headEnd = @(GitLocal @('rev-parse', '--short', 'HEAD'))[0]
$summary.Insert(0, 'ramo', "$branch @ $headEnd")
$left = Get-Dirty
if ($left.Count -gt 0) { $warns.Add("nell'albero restano $($left.Count) voci non committate (non le ho toccate), es.: " + (($left | Select-Object -First 6) -join ' | ')) }
Finish $true ''

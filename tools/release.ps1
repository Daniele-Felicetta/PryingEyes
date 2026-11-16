<#
release.ps1 - prepara una release di Prying Eyes: controlli, build di DISTRIBUZIONE, ricerca dei percorsi del PC nei file da spedire,
zip con checksum e prova di fumo dell'exe del pacchetto. NON pubblica niente: la bozza e la pubblicazione su GitHub restano a mano
(docs/engineering/building-and-shipping.md, sezione Releases). Una release si fa solo su richiesta dell'utente.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 0.6.2                  tutti i passi, nell'ordine sotto
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 0.6.2 -Step check      solo i controlli, non costruisce

La versione e' bundleVersion di unity\PryingEyes\ProjectSettings\ProjectSettings.asset (Player Settings > Version, x.y.z): il menu la scrive,
il tag e' v<versione>, lo zip PryingEyes-v<versione>-Windows-x64.zip. Alzarla in un commit, pushare con tools\ship.ps1 -Mode full e aspettare
la CI verde PRIMA di lanciare questo script.

Passi (-Step: all, check, build, scan, zip, smoke; con 'all' vanno in questo ordine, e build include check):
  check  bundleVersion = -Version; ramo -Branch con HEAD uguale a origin/<ramo> (la release si fa da un commit pushato); nessun sorgente non
         committato sotto unity/PryingEyes/{Assets,Packages,ProjectSettings} (stesso criterio di ship.ps1: derivati e differenze di sola fine
         riga esclusi); Unity trovato e non aperto sul progetto; il modello del LEGGIMI c'e'; la cartella di uscita non esiste; disco libero.
  build  Unity in batchmode a priorita' bassa: BuildPlayer.Windows con -distribution, in unity\PryingEyes\Builds\Release-v<versione>.
         Esige 'Build result: Succeeded', nessun errore di compilazione, BuildScrub a 0 percorsi rimasti e ancora nessun sorgente non committato.
         Scrive unity_logs\release_v<versione>.json (commit costruito, cartella): da li' partono scan, zip e smoke.
  scan   cerca la cartella del repository e la cartella utente di QUESTO PC (barre rovesciate e normali; UTF-8 e UTF-16) in tutti i file che
         finiranno nello zip. BuildScrub guarda solo gli assembly gestiti.
  zip    <Releases>\PryingEyes-v<versione>-Windows-x64.zip: i file della build nella radice, LEGGIMI.txt aggiunto (da tools\release\LEGGIMI.txt,
         con {VERSION} e {COMMIT} sostituiti), senza le cartelle *_BackUpThisFolder_ButDontShipItWithYourGame e *_BurstDebugInformation_DoNotShip;
         poi l'hash di OGNI voce dello zip contro il file di origine e <Releases>\v<versione>\SHA256SUMS.txt. Non sovrascrive niente.
  smoke  l'exe del pacchetto senza grafica (-batchmode -nographics), cinque scenari di -SmokeSeconds secondi ciascuno: il menu, il menu con
         -padauto e lo schermo condiviso a 2, 3 e 4 giocatori (-splitscreen N -menureview -nowaves). Cerca nel log del giocatore la riga
         '[PryingEyes] VERSIONE <versione>', per lo schermo condiviso la riga '[PryingEyes] split: N giocatori' e nessuna eccezione.
         Gira DOPO lo zip perche' far girare l'exe puo' aggiungere file alla cartella. Non apre finestre, e -batchmode e -menureview
         impediscono al gioco di applicare o salvare le impostazioni dello schermo (VideoSettings.Automated).

-UnityExe, -Repo, -Branch, -LeggimiFile, -ReleasesDir (dove vanno lo zip e i checksum, default Builds\Releases), -SmokeSeconds,
-BuildTimeoutSec e -MinFreeGB servono ai test e a chi ha il progetto in un altro posto. Esce con 0 solo se tutti i passi richiesti sono andati
bene; con 1 se qualcosa fallisce (ultima riga: 'RELEASE: FALLITO - <motivo>'). Il file e' ASCII di proposito: Windows PowerShell 5.1 legge male
gli accenti in un file senza BOM.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [ValidateSet('all', 'check', 'build', 'scan', 'zip', 'smoke')][string]$Step = 'all',
    [string]$Repo = '',
    [string]$Branch = 'preview',
    [string]$UnityExe = '',
    [string]$LeggimiFile = '',
    [string]$ReleasesDir = '',
    [int]$SmokeSeconds = 35,
    [int]$BuildTimeoutSec = 1500,
    [int]$MinFreeGB = 6
)
$ErrorActionPreference = 'Continue'

function Fail([string]$m) { 'RELEASE: FALLITO - ' + $m; exit 1 }
function Short([string]$sha) { if ($sha.Length -ge 7) { return $sha.Substring(0, 7) } return $sha }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fail "la versione deve essere x.y.z (ricevuto '$Version')" }
if (-not $Repo) { $Repo = Join-Path $PSScriptRoot '..' }
if (-not (Test-Path -LiteralPath $Repo)) { Fail "il repository non esiste: $Repo" }
$Repo = (Resolve-Path -LiteralPath $Repo).Path.TrimEnd('\')
$proj = Join-Path $Repo 'unity\PryingEyes'
$builds = Join-Path $proj 'Builds'
$outDir = Join-Path $builds ('Release-v' + $Version)
if (-not $ReleasesDir) { $ReleasesDir = Join-Path $builds 'Releases' }
$zipName = 'PryingEyes-v' + $Version + '-Windows-x64.zip'
$zipPath = Join-Path $ReleasesDir $zipName
$sumDir = Join-Path $ReleasesDir ('v' + $Version)
$sumPath = Join-Path $sumDir 'SHA256SUMS.txt'
$logDir = Join-Path $Repo 'unity_logs'
$buildLog = Join-Path $logDir ('release_build_v' + $Version + '.log')
$markerFile = Join-Path $logDir ('release_v' + $Version + '.json')
$smokeDir = Join-Path $logDir ('release_smoke_v' + $Version)
if (-not $LeggimiFile) { $LeggimiFile = Join-Path $PSScriptRoot 'release\LEGGIMI.txt' }
$roots = @('unity/PryingEyes/Assets', 'unity/PryingEyes/Packages', 'unity/PryingEyes/ProjectSettings')

$unity = $UnityExe
if (-not $unity) {
    $ver = Get-Content -LiteralPath (Join-Path $proj 'ProjectSettings\ProjectVersion.txt') -ErrorAction SilentlyContinue | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    if ($ver) { $unity = 'C:\Program Files\Unity\Hub\Editor\' + (($ver -split ':')[1].Trim()) + '\Editor\Unity.exe' }
}

# ---------------------------------------------------------------- sorgenti non committati (stesso criterio di ship.ps1)
function Test-Derived([string]$p) {
    if ($p -match '\.mat(\.meta)?$') { return $true }
    if ($p -match '/Scenes/(Citta|Torre)\.unity(\.meta)?$') { return $true }
    if ($p -match '/Scenes/Labs(/|\.meta$)') { return $true }
    return $false
}
function Get-Sources {
    $q = @('-C', $Repo, '-c', 'core.quotepath=false')
    $status = @{}
    foreach ($l in @(& git @q diff --name-status --no-renames HEAD -- @roots)) { if ($l -match '^([A-Z])\t(.+)$') { $status[$Matches[2]] = $Matches[1] } }
    $real = @{}
    foreach ($l in @(& git @q diff --numstat --no-renames --ignore-cr-at-eol HEAD -- @roots)) { if ($l -match '^(\d+|-)\t(\d+|-)\t(.+)$') { $real[$Matches[3]] = $true } }
    foreach ($l in @(& git @q ls-files --others --exclude-standard -- @roots)) { if ($l -and -not $status.ContainsKey($l)) { $status[$l] = '??' } }
    $out = @()
    foreach ($p in ($status.Keys | Sort-Object)) {
        if ($status[$p] -eq 'M' -and -not $real.ContainsKey($p)) { continue }
        if (Test-Derived $p) { continue }
        $out += ($status[$p] + ' ' + $p)
    }
    return $out
}

# ---------------------------------------------------------------- strumenti nativi (priorita' di disco e memoria, ricerca nei binari)
$script:nativeReady = $false
function Initialize-Native {
    if ($script:nativeReady) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
public static class ReleaseNative {
    [DllImport("ntdll.dll")] static extern int NtSetInformationProcess(IntPtr h, int cls, ref int val, int len);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    // classe 33 = priorita' del disco, 39 = della memoria; 1 = bassa (la "molto bassa" con altro disco in uso fa morire il processo di fame)
    public static int Priority(int pid, int cls, int val) {
        IntPtr h = OpenProcess(0x0200, false, pid);
        if (h == IntPtr.Zero) return -1;
        int v = val;
        int r = NtSetInformationProcess(h, cls, ref v, 4);
        CloseHandle(h);
        return r;
    }
    // Quante volte compare ogni ago nel file (maiuscole e minuscole uguali), come UTF-8 e come UTF-16 LE. Legge a blocchi di 4 MB.
    public static string[] Scan(string file, string[] needles) {
        List<string> hits = new List<string>();
        List<byte[]> patterns = new List<byte[]>();
        List<string> labels = new List<string>();
        foreach (string n in needles) {
            string lower = n.ToLowerInvariant();
            patterns.Add(Encoding.UTF8.GetBytes(lower)); labels.Add(n + " [utf8]");
            patterns.Add(Encoding.Unicode.GetBytes(lower)); labels.Add(n + " [utf16]");
        }
        int maxLen = 0;
        foreach (byte[] p in patterns) { if (p.Length > maxLen) maxLen = p.Length; }
        int[] counts = new int[patterns.Count];
        byte[] buf = new byte[(4 << 20) + maxLen];
        int carry = 0;
        using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20)) {
            int read;
            while ((read = fs.Read(buf, carry, buf.Length - carry)) > 0) {
                int len = carry + read;
                for (int i = carry; i < len; i++) { byte b = buf[i]; if (b >= 65 && b <= 90) buf[i] = (byte)(b + 32); }
                for (int k = 0; k < patterns.Count; k++) {
                    byte[] p = patterns[k];
                    int last = len - p.Length;
                    for (int i = 0; i <= last; i++) {
                        if (buf[i] != p[0]) continue;
                        int j = 1;
                        while (j < p.Length && buf[i + j] == p[j]) j++;
                        if (j == p.Length && i + p.Length > carry) counts[k]++;
                    }
                }
                carry = Math.Min(maxLen - 1, len);
                Buffer.BlockCopy(buf, len - carry, buf, 0, carry);
            }
        }
        for (int k = 0; k < patterns.Count; k++) { if (counts[k] > 0) hits.Add(labels[k] + " x" + counts[k]); }
        return hits.ToArray();
    }
}
'@
    $script:nativeReady = $true
}

function Get-Hex([byte[]]$b) { return (($b | ForEach-Object { $_.ToString('x2') }) -join '') }
function Get-FileSha([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $fs = [IO.File]::OpenRead($path)
    try { return (Get-Hex ($sha.ComputeHash($fs))) } finally { $fs.Dispose(); $sha.Dispose() }
}

# i file della build che finiscono nello zip (le cartelle di copia di Burst e dei suoi simboli non si spediscono)
function Get-ShippedFiles {
    $root = $outDir
    return @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | Where-Object {
        $r = $_.FullName.Substring($root.Length + 1)
        $r -notlike '*BackUpThisFolder_ButDontShipItWithYourGame*' -and $r -notlike '*BurstDebugInformation_DoNotShip*'
    })
}

function Read-Marker {
    if (-not (Test-Path -LiteralPath $markerFile)) { Fail "manca ${markerFile}: il passo build non e' stato fatto per la versione $Version" }
    $m = $null
    try { $m = Get-Content -LiteralPath $markerFile -Raw | ConvertFrom-Json } catch { Fail "$markerFile non si legge" }
    if ([string]$m.version -ne $Version) { Fail "$markerFile e' della versione $($m.version), non della $Version" }
    if (-not (Test-Path -LiteralPath (Join-Path $outDir 'PryingEyes.exe'))) { Fail "manca la build in $outDir" }
    return $m
}

# ---------------------------------------------------------------- check
function Do-Check {
    $asset = Join-Path $proj 'ProjectSettings\ProjectSettings.asset'
    if (-not (Test-Path -LiteralPath $asset)) { Fail "manca $asset" }
    $m = Select-String -LiteralPath $asset -Pattern '^\s*bundleVersion:\s*(\S+)' | Select-Object -First 1
    if (-not $m) { Fail 'bundleVersion non trovato in ProjectSettings.asset' }
    $bv = $m.Matches[0].Groups[1].Value
    "bundleVersion = $bv"
    if ($bv -ne $Version) { Fail "bundleVersion ($bv) diverso dalla versione chiesta ($Version): va alzato in un commit, pushato, con la CI verde" }
    $head = [string](@(& git -C $Repo rev-parse HEAD)[0])
    $branch = [string](@(& git -C $Repo rev-parse --abbrev-ref HEAD)[0])
    $remote = ([string](@(& git -C $Repo ls-remote origin $Branch)[0]) -split '\s+')[0]
    "HEAD $(Short $head) ($branch), origin/$Branch $(Short $remote)"
    if ($branch -ne $Branch) { Fail "il ramo e' $branch, non $Branch" }
    if (-not $remote -or $head -ne $remote) { Fail "HEAD diverso da origin/${Branch}: la release si fa da un commit pushato" }
    $dirty = @(Get-Sources)
    "sorgenti non committati (derivati esclusi): $($dirty.Count)"
    if ($dirty.Count -gt 0) {
        $dirty | Select-Object -First 12 | ForEach-Object { '  ' + $_ }
        Fail "ci sono $($dirty.Count) sorgenti non committati: la build non sarebbe quella del commit"
    }
    if (-not $unity -or -not (Test-Path -LiteralPath $unity)) { Fail "Unity non trovato ($unity)" }
    foreach ($u in @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'")) {
        $cl = ([string]$u.CommandLine) -replace '/', '\'
        if ($cl.IndexOf($proj, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Fail "Unity e' gia' aperto su questo progetto (pid $($u.ProcessId)): non lo chiudo" }
    }
    if (-not (Test-Path -LiteralPath $LeggimiFile)) { Fail "manca il modello del LEGGIMI: $LeggimiFile" }
    $tpl = [IO.File]::ReadAllText($LeggimiFile)
    if ($tpl.IndexOf('{VERSION}') -lt 0 -or $tpl.IndexOf('{COMMIT}') -lt 0) { Fail "il modello del LEGGIMI deve contenere {VERSION} e {COMMIT}: $LeggimiFile" }
    if (Test-Path -LiteralPath $outDir) { Fail "$outDir esiste gia': non lo tocco" }
    $free = [int]((Get-PSDrive -Name $Repo.Substring(0, 1)).Free / 1GB)
    "disco libero: $free GB"
    if ($free -lt $MinFreeGB) { Fail "meno di $MinFreeGB GB liberi" }
    $script:head = $head
    'check: ok'
}

# ---------------------------------------------------------------- build
function Do-Build {
    Initialize-Native
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $a = @('-batchmode', '-projectPath', ('"' + $proj + '"'), '-executeMethod', 'PryingEyes.EditorTools.BuildPlayer.Windows',
           '-buildOut', ('"' + $outDir + '"'), '-distribution', '-quit', '-logFile', ('"' + $buildLog + '"'))
    if (Test-Path -LiteralPath $buildLog) { Remove-Item -LiteralPath $buildLog -Force }
    'unity: ' + ($a -join ' ')
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $unity -ArgumentList $a -PassThru -WindowStyle Hidden
    $null = $p.Handle
    $p.PriorityClass = 'BelowNormal'
    "priorita' disco (esito): " + [ReleaseNative]::Priority($p.Id, 33, 1) + ", memoria (esito): " + [ReleaseNative]::Priority($p.Id, 39, 2)
    while (-not $p.HasExited) {
        if ($sw.Elapsed.TotalSeconds -gt $BuildTimeoutSec) { Fail "tempo scaduto ($BuildTimeoutSec s): Unity (pid $($p.Id)) e' ancora attivo, non l'ho chiuso" }
        Start-Sleep -Seconds 2
    }
    $p.WaitForExit()
    "Unity finito: uscita $($p.ExitCode) in $([int]$sw.Elapsed.TotalSeconds) s"
    $res = @()
    if (Test-Path -LiteralPath $buildLog) { $res = @(Select-String -LiteralPath $buildLog -Pattern 'error CS\d+|\[PryingEyes\] Build result|\[PryingEyes\] distribution') }
    $res | ForEach-Object { '  ' + $_.Line.Trim() }
    if (@($res | Where-Object { $_.Line -match 'error CS\d+' }).Count -gt 0) { Fail "errori di compilazione (vedi $buildLog)" }
    if (@($res | Where-Object { $_.Line -match 'Build result: Succeeded' }).Count -eq 0) { Fail "manca 'Build result: Succeeded' (vedi $buildLog)" }
    if ($p.ExitCode -ne 0) { Fail "Unity e' uscito con $($p.ExitCode)" }
    $scrub = @($res | Where-Object { $_.Line -match 'distribution: debug path removed from (\d+) assembly\(ies\), (\d+) still contain' })
    if ($scrub.Count -eq 0) { Fail 'manca la riga di BuildScrub (distribution: debug path removed ...)' }
    if ($scrub[0].Line -notmatch ', 0 still contain') { Fail "BuildScrub: il percorso del checkout e' rimasto in un assembly" }
    foreach ($f in @('PryingEyes.exe', 'PryingEyes_Data\Managed\Assembly-CSharp.dll', 'PryingEyes_Data\resources.assets')) {
        if (-not (Test-Path -LiteralPath (Join-Path $outDir $f))) { Fail "dopo la build manca $f" }
    }
    $after = @(Get-Sources)
    if ($after.Count -ne 0) { $after | Select-Object -First 12 | ForEach-Object { '  ' + $_ }; Fail 'dopo la build ci sono sorgenti non committati' }
    $size = [int]((Get-ChildItem -LiteralPath $outDir -Recurse -File -Force | Measure-Object Length -Sum).Sum / 1MB)
    "cartella: $size MB in $outDir"
    $marker = [ordered]@{ version = $Version; commit = $script:head; outDir = $outDir; builtAt = (Get-Date -Format 's'); buildLog = $buildLog }
    [IO.File]::WriteAllText($markerFile, (($marker | ConvertTo-Json) + "`r`n"), (New-Object Text.UTF8Encoding($false)))
    "build: ok (commit $(Short $script:head))"
}

# ---------------------------------------------------------------- scan
function Do-Scan {
    $null = Read-Marker
    Initialize-Native
    $needles = @()
    foreach ($base in @($Repo, $env:USERPROFILE)) {
        if ($base -and $base.Length -ge 6) { $needles += $base.TrimEnd('\'); $needles += ($base.TrimEnd('\') -replace '\\', '/') }
    }
    $needles = @($needles | Select-Object -Unique)
    'cerco: ' + ($needles -join ' | ')
    [Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
    $files = @(Get-ShippedFiles)
    $found = 0
    foreach ($f in $files) {
        foreach ($h in [ReleaseNative]::Scan($f.FullName, [string[]]$needles)) { $found++; '  ' + $f.FullName.Substring($outDir.Length + 1) + ': ' + $h }
    }
    "file esaminati: $($files.Count), occorrenze: $found"
    if ($found -gt 0) { Fail 'nei file da spedire ci sono percorsi di questo PC (cartella del repository o cartella utente)' }
    'scan: ok'
}

# ---------------------------------------------------------------- zip
function Do-Zip {
    $marker = Read-Marker
    Initialize-Native
    if (Test-Path -LiteralPath $zipPath) { Fail "$zipPath esiste gia': non lo sovrascrivo" }
    if (Test-Path -LiteralPath $sumPath) { Fail "$sumPath esiste gia': non lo sovrascrivo" }
    if (-not (Test-Path -LiteralPath $LeggimiFile)) { Fail "manca il modello del LEGGIMI: $LeggimiFile" }
    $commit = Short ([string]$marker.commit)
    $text = [IO.File]::ReadAllText($LeggimiFile).Replace('{VERSION}', $Version).Replace('{COMMIT}', $commit).Replace("`r`n", "`n").Replace("`n", "`r`n")
    if ($text -match '\{[A-Z]+\}') { Fail 'nel LEGGIMI e'' rimasto un segnaposto non sostituito' }
    $leggimi = (New-Object Text.UTF8Encoding($false)).GetBytes($text)
    [Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
    "priorita' disco (esito): " + [ReleaseNative]::Priority($PID, 33, 1)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $files = @(Get-ShippedFiles)
    $expected = @{}
    foreach ($f in $files) { $expected[($f.FullName.Substring($outDir.Length + 1)) -replace '\\', '/'] = $f.FullName }
    if ($expected.ContainsKey('LEGGIMI.txt')) { Fail "nella build c'e' gia' un LEGGIMI.txt" }
    "file da impacchettare: $($files.Count) (piu' LEGGIMI.txt, commit $commit)"
    New-Item -ItemType Directory -Force -Path $ReleasesDir, $sumDir | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $fs = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    $zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $e = $zip.CreateEntry('LEGGIMI.txt', [IO.Compression.CompressionLevel]::Optimal)
        $es = $e.Open(); $es.Write($leggimi, 0, $leggimi.Length); $es.Dispose()
        foreach ($rel in ($expected.Keys | Sort-Object)) {
            $fi = Get-Item -LiteralPath $expected[$rel]
            $e = $zip.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
            $e.LastWriteTime = $fi.LastWriteTime
            $es = $e.Open()
            $in = [IO.File]::OpenRead($fi.FullName)
            try { $in.CopyTo($es, 1MB) } finally { $in.Dispose(); $es.Dispose() }
        }
    } finally {
        $zip.Dispose(); $fs.Dispose()
    }
    'zip scritto in {0} s: {1} ({2:N0} byte)' -f [int]$sw.Elapsed.TotalSeconds, $zipPath, (Get-Item -LiteralPath $zipPath).Length
    # verifica voce per voce: l'hash di ogni voce dello zip contro il file di origine (o il testo del LEGGIMI), e le voci che mancano
    $sha = [Security.Cryptography.SHA256]::Create()
    $a = [IO.Compression.ZipFile]::OpenRead($zipPath)
    $bad = 0; $n = 0; $names = @{}
    try {
        foreach ($en in $a.Entries) {
            $n++
            $names[$en.FullName] = $true
            $s = $en.Open(); $h1 = Get-Hex ($sha.ComputeHash($s)); $s.Dispose()
            if ($en.FullName -eq 'LEGGIMI.txt') { $h2 = Get-Hex ($sha.ComputeHash($leggimi)) }
            elseif ($expected.ContainsKey($en.FullName)) { $h2 = Get-FileSha $expected[$en.FullName] }
            else { $h2 = 'voce non prevista' }
            if ($h1 -ne $h2) { $bad++; 'DIVERSA: ' + $en.FullName }
        }
    } finally { $a.Dispose() }
    $missing = @($expected.Keys | Where-Object { -not $names.ContainsKey($_) })
    $missing | ForEach-Object { 'MANCA: ' + $_ }
    "voci nello zip: $n (attese $($files.Count + 1)), diverse: $bad, mancanti: $($missing.Count)"
    if ($bad -ne 0 -or $missing.Count -ne 0 -or $n -ne ($files.Count + 1)) { Fail "la verifica voce per voce dello zip non e' pulita" }
    $hash = Get-FileSha $zipPath
    [IO.File]::WriteAllText($sumPath, ($hash + '  ' + $zipName + "`n"), (New-Object Text.UTF8Encoding($false)))
    "SHA-256: $hash"
    "checksum: $sumPath ($((Get-Item -LiteralPath $sumPath).Length) byte)"
    $script:zipHash = $hash
    'zip: ok'
}

# ---------------------------------------------------------------- smoke
function Do-Smoke {
    $null = Read-Marker
    $exe = (Resolve-Path -LiteralPath (Join-Path $outDir 'PryingEyes.exe')).Path
    foreach ($pr in @(Get-Process PryingEyes -ErrorAction SilentlyContinue)) {
        if ($pr.Path -and $pr.Path -eq $exe) { Fail "una copia di questo exe e' gia' aperta (pid $($pr.Id))" }
    }
    New-Item -ItemType Directory -Force -Path $smokeDir | Out-Null
    $scenarios = @(
        @{ name = 'menu';   args = @('-menureview', '-nowaves');                                                      expect = @() },
        @{ name = 'pad';    args = @('-menureview', '-nowaves', '-padauto');                                          expect = @() },
        @{ name = 'split2'; args = @('-seed', '1000', '-floor', '1', '-splitscreen', '2', '-menureview', '-nowaves'); expect = @('\[PryingEyes\] split: 2 giocatori') },
        @{ name = 'split3'; args = @('-seed', '1000', '-floor', '1', '-splitscreen', '3', '-menureview', '-nowaves'); expect = @('\[PryingEyes\] split: 3 giocatori') },
        @{ name = 'split4'; args = @('-seed', '1000', '-floor', '1', '-splitscreen', '4', '-menureview', '-nowaves'); expect = @('\[PryingEyes\] split: 4 giocatori') }
    )
    $bad = 0
    foreach ($sc in $scenarios) {
        $log = Join-Path $smokeDir ($sc.name + '.log')
        Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
        $argv = @('-batchmode', '-nographics') + $sc.args + @('-logFile', ('"' + $log + '"'))
        $p = Start-Process -FilePath $exe -ArgumentList $argv -WorkingDirectory (Split-Path $exe -Parent) -PassThru -WindowStyle Hidden
        $null = $p.Handle
        try { $p.PriorityClass = 'BelowNormal' } catch { }
        $exited = $p.WaitForExit($SmokeSeconds * 1000)
        $code = ''
        if ($exited) { $code = $p.ExitCode } else { try { $p.Kill() } catch { }; $p.WaitForExit() }
        $text = ''
        if (Test-Path -LiteralPath $log) {
            $fsr = [IO.File]::Open($log, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            $sr = New-Object IO.StreamReader($fsr); $text = $sr.ReadToEnd(); $sr.Dispose(); $fsr.Dispose()
        }
        $problems = @()
        if ($exited) { $problems += "il gioco e' uscito da solo prima di $SmokeSeconds s (codice $code)" }
        if ($text -notmatch ('\[PryingEyes\] VERSIONE ' + [regex]::Escape($Version))) { $problems += "manca la riga '[PryingEyes] VERSIONE $Version'" }
        foreach ($ex in $sc.expect) { if ($text -notmatch $ex) { $problems += "manca '$ex'" } }
        $sus = @([regex]::Matches($text, '(?m)^.*(Exception|NullReference|Assertion failed|\[Error\]).*$') | ForEach-Object { $_.Value.Trim() })
        if ($sus.Count -gt 0) { $problems += "$($sus.Count) righe sospette (eccezioni)" }
        '--- {0}: {1} s, log {2} byte; {3}' -f $sc.name, $SmokeSeconds, $text.Length, $(if ($problems.Count -eq 0) { 'ok' } else { 'PROBLEMI' })
        $v = [regex]::Match($text, '\[PryingEyes\] VERSIONE [^\r\n]*').Value
        $sp = [regex]::Match($text, '\[PryingEyes\] split:[^\r\n]*').Value
        if ($v) { '    ' + $v }
        if ($sp) { '    ' + $sp }
        $problems | ForEach-Object { '    ! ' + $_ }
        $sus | Select-Object -First 5 | ForEach-Object { '    > ' + $_ }
        if ($problems.Count -gt 0) { $bad++ }
    }
    if ($bad -gt 0) { Fail "$bad scenari della prova di fumo con problemi (log in $smokeDir)" }
    "smoke: ok ($($scenarios.Count) scenari, nessuna eccezione, versione $Version nel log)"
}

# ---------------------------------------------------------------- orchestrazione
if ($Step -eq 'all') { $steps = @('check', 'build', 'scan', 'zip', 'smoke') }
elseif ($Step -eq 'build') { $steps = @('check', 'build') }
else { $steps = @($Step) }
foreach ($s in $steps) {
    "=== $s ==="
    if ($s -eq 'check') { Do-Check }
    elseif ($s -eq 'build') { Do-Build }
    elseif ($s -eq 'scan') { Do-Scan }
    elseif ($s -eq 'zip') { Do-Zip }
    elseif ($s -eq 'smoke') { Do-Smoke }
}
"RELEASE: OK (versione $Version, passi: $($steps -join ', '))"
if ($steps -contains 'zip') {
    ''
    'Prossimi passi, a mano (docs/engineering/building-and-shipping.md, sezione Releases):'
    '  1. scrivere le note della release (italiano: cosa cambia e cosa e'' stato provato, e dove)'
    "  2. gh release create v$Version `"$zipPath`" `"$sumPath`" --target <sha completo del commit costruito> --title `"...`" --notes-file <note> --draft"
    '  3. controllare nome, dimensione e digest dei due asset della bozza, poi pubblicarla: gh api -X PATCH repos/<owner>/<repo>/releases/<id> -F draft=false -f make_latest=true'
    "  4. cancellare $outDir (si ricostruisce; il contenuto e' nello zip verificato)"
}
exit 0

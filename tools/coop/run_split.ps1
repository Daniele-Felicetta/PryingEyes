# Prova dello split screen a piu' processi: l'ospite parte con -splitscreen N (come se avesse premuto il bottone del menu), lancia da solo
# gli altri giocatori, e si controlla che tutti entrino nella partita; poi si chiude l'ospite e si controlla che anche gli altri escano.
# Esce con 0 solo se ogni controllo e' passato.
#
#   .\tools\coop\run_split.ps1 -Players 2                    senza grafica (batchmode): lancio, collegamento, uscita insieme
#   .\tools\coop\run_split.ps1 -Players 4 -Graphics -Shot    con le finestre vere: posizione e bordi di ognuna, foto dello schermo
#                                                            (usa la GPU e occupa lo schermo per circa un minuto: non lanciarlo mentre si gioca)
#
# Il gioco e' la build di verifica (Builds\Audit), mai Builds\PryingEyes.exe (e' quello dell'utente). I controller sono finti: con -PadSim
# ogni giocatore oltre il primo "muove" lo stick (avanti | giro) cosi' l'ospite li vede camminare; la lettura vera di XInput non si prova qui.
# Log in -LogDir: split_host.log (ospite) e giocatore2.log .. giocatore4.log (gli altri).
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\unity\PryingEyes\Builds\Audit\PryingEyes.exe'),
    [string]$LogDir = (Join-Path $env:TEMP 'pryingeyes-split'),
    [ValidateRange(2, 4)][int]$Players = 2,
    [switch]$Graphics,
    [switch]$Shot,
    [ValidateSet('', 'avanti', 'giro')][string]$PadSim = 'giro',
    [switch]$HostPad,          # anche il giocatore 1 usa un controller (-splitpad1)
    [int]$TimeoutSec = 150,
    [int]$SettleSec = 12       # con le finestre vere: secondi di gioco prima della foto
)
$ErrorActionPreference = 'Continue'
$exe = (Resolve-Path $Exe).Path
if (Get-Process PryingEyes -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { 'una copia di questo exe e aperta: non parto'; exit 3 }
New-Item -ItemType Directory -Force $LogDir | Out-Null
Get-ChildItem $LogDir -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "^(split_host|giocatore\d)\.log$|^split_$Players\.png$" } | Remove-Item -ErrorAction SilentlyContinue

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class SplitWin {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
  // x, y, larghezza, altezza e stile della finestra di gioco del processo (null se non ne ha)
  public static long[] Info(int pid) {
    long[] found = null; var sb = new StringBuilder(64);
    EnumProc cb = (h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != (uint)pid || !IsWindowVisible(h)) return true;
      sb.Length = 0; GetClassNameW(h, sb, 64);
      if (sb.ToString() != "UnityWndClass") return true;
      RECT r; GetWindowRect(h, out r);
      found = new long[] { r.l, r.t, r.r - r.l, r.b - r.t, GetWindowLongPtr(h, -16).ToInt64() & 0xFFFFFFFFL };
      return false;
    };
    EnumWindows(cb, IntPtr.Zero); GC.KeepAlive(cb);
    return found;
  }
}
'@
Add-Type -AssemblyName System.Drawing
[void][SplitWin]::SetProcessDpiAwarenessContext([IntPtr](-4))     # coordinate in pixel veri, non scalate

$fails = New-Object System.Collections.Generic.List[string]
function Nums($m) { @(1..4 | ForEach-Object { [int]$m.Groups[$_].Value }) }
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $line = ('{0} {1}' -f $(if ($ok) { 'ok     ' } else { 'FALLITO' }), $name) + $(if ($detail) { "  ($detail)" } else { '' })
    $line
    if (-not $ok) { $fails.Add($name) }
}

$hostLog = Join-Path $LogDir 'split_host.log'
$extra = @('-splitlog')
if (-not $Graphics) { $extra += @('-batchmode', '-nographics') }
if ($PadSim) { $extra += @('-padsim', $PadSim) }
$hostArgs = @()
if (-not $Graphics) { $hostArgs += @('-batchmode', '-nographics') }
# Start-Process (PowerShell 5.1) unisce gli argomenti con uno spazio senza virgolette: i valori con spazi vanno virgolettati a mano
$hostArgs += @('-seed', '1000', '-splitscreen', "$Players", '-splitlog', '-splitlogdir', "`"$LogDir`"", '-logFile', "`"$hostLog`"")
if ($extra.Count) { $hostArgs += @('-splitextra', ('"' + ($extra -join ' ') + '"')) }
if ($HostPad) { $hostArgs += '-splitpad1' }

$sw = [Diagnostics.Stopwatch]::StartNew()
$ph = Start-Process -FilePath $exe -ArgumentList $hostArgs -PassThru
"ospite avviato (pid $($ph.Id)), $Players giocatori, $(if ($Graphics) { 'con finestre vere' } else { 'senza grafica' })"

# Unity tiene aperto il suo log in scrittura: ReadAllText (FileShare.Read) fallirebbe, serve ReadWrite
function ReadShared([string]$f) {
    if (-not (Test-Path $f)) { return '' }
    try {
        $fs = [IO.File]::Open($f, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try { return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
    } catch { return '' }
}
function HostLogText { ReadShared $hostLog }
function ChildLogText([int]$k) { ReadShared (Join-Path $LogDir "giocatore$k.log") }

# 1) l'ospite lancia gli altri e li saluta tutti
$greeted = 0
while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
    if ($ph.HasExited) { break }
    $greeted = ([regex]::Matches((HostLogText), 'salutato: piano')).Count
    if ($greeted -ge ($Players - 1)) { break }
    Start-Sleep -Milliseconds 700
}
"giocatori salutati dall'ospite: $greeted su $($Players - 1) dopo $([int]$sw.Elapsed.TotalSeconds) s"
$text = HostLogText
Check 'l''ospite ha avviato lo split screen' ($text -match 'split: \d giocatori, porta \d+')
$started = [regex]::Matches($text, 'giocatore (\d) avviato \(pid (\d+)\): (.*)')
Check "l'ospite ha lanciato gli altri $($Players - 1) giocatori" ($started.Count -eq ($Players - 1)) "lanciati $($started.Count)"
Check "tutti i giocatori sono entrati nella partita" ($greeted -ge ($Players - 1))

# 2) ognuno dice chi e' e dove sta
$childPids = @{}
foreach ($m in $started) { $childPids[[int]$m.Groups[1].Value] = [int]$m.Groups[2].Value }
foreach ($k in 2..$Players) {
    $log = ChildLogText $k
    Check "giocatore ${k}: sa il suo posto e il suo controller" ($log -match "split: finestra $k di $Players") ($(([regex]::Match($log, 'split: finestra[^\r\n]*')).Value))
    Check "giocatore ${k}: ammesso dall'ospite" ($log -match 'coop: ammesso come slot \d')
    Check "giocatore ${k}: nessuna eccezione nel gioco" ($log.Length -gt 0 -and $log -notmatch 'Exception')
}
$hostText = HostLogText
Check 'ospite: nessuna eccezione nel gioco' ($hostText.Length -gt 0 -and $hostText -notmatch 'Exception')
if ($PadSim) {
    # un controller finto muove il giocatore: l'ospite deve vederlo spostarsi (due righe di stato a tempo diverso con posizioni diverse)
    Start-Sleep -Seconds 9
    $posLines = [regex]::Matches((HostLogText), 'compagno Giocatore 2\[\d\] pos (?!0\.0 0\.0 0\.0 )(-?[\d.]+) (-?[\d.]+) (-?[\d.]+)')    # niente righe a 0 0 0: e' il pupazzo prima del primo aggiornamento
    $moved = $false
    if ($posLines.Count -ge 2) {
        $a = $posLines[0]; $b = $posLines[$posLines.Count - 1]
        $inv = [Globalization.CultureInfo]::InvariantCulture
        $dx = [double]::Parse($b.Groups[1].Value, $inv) - [double]::Parse($a.Groups[1].Value, $inv)
        $dz = [double]::Parse($b.Groups[3].Value, $inv) - [double]::Parse($a.Groups[3].Value, $inv)
        $moved = ([math]::Sqrt($dx * $dx + $dz * $dz) -gt 1.0)
    }
    Check 'il giocatore 2 (controller finto) si e'' mosso, visto dall''ospite' $moved "$($posLines.Count) righe di posizione$(if ($posLines.Count) { ', ultima ' + $posLines[$posLines.Count - 1].Value })"
}

# 3) finestre vere: ognuna nella sua parte, senza bordo
if ($Graphics) {
    Start-Sleep -Seconds $SettleSec
    $expect = @{}
    $mine = [regex]::Match($text, 'il mio posto \(x:(-?\d+), y:(-?\d+), width:(\d+), height:(\d+)\)')
    if ($mine.Success) { $expect[1] = Nums $mine }
    foreach ($m in $started) {
        $t = [regex]::Match($m.Groups[3].Value, '-splittile (-?\d+),(-?\d+),(\d+),(\d+)')
        if ($t.Success) { $expect[[int]$m.Groups[1].Value] = Nums $t }      # il numero nel log e' gia' quello del giocatore (2..4)
    }
    $pidOf = @{ 1 = $ph.Id }
    foreach ($k in $childPids.Keys) { $pidOf[$k] = $childPids[$k] }
    $minX = [int]::MaxValue; $minY = [int]::MaxValue; $maxX = [int]::MinValue; $maxY = [int]::MinValue
    foreach ($k in 1..$Players) {
        $info = if ($pidOf.ContainsKey($k)) { [SplitWin]::Info($pidOf[$k]) } else { $null }
        if ($null -eq $info) { Check "giocatore ${k}: ha una finestra" $false; continue }
        $e = $expect[$k]
        $okPos = $e -and ([math]::Abs($info[0] - $e[0]) -le 2) -and ([math]::Abs($info[1] - $e[1]) -le 2) -and ([math]::Abs($info[2] - $e[2]) -le 2) -and ([math]::Abs($info[3] - $e[3]) -le 2)
        $borderless = (($info[4] -band 0x00C00000) -eq 0) -and (($info[4] -band 0x00040000) -eq 0)
        Check "giocatore ${k}: finestra nella sua parte, senza titolo ne' bordo" ($okPos -and $borderless) "trovata $($info[0]),$($info[1]) $($info[2])x$($info[3]); attesa $($e -join ',')"
        $minX = [math]::Min($minX, $info[0]); $minY = [math]::Min($minY, $info[1])
        $maxX = [math]::Max($maxX, $info[0] + $info[2]); $maxY = [math]::Max($maxY, $info[1] + $info[3])
    }
    if ($Shot -and $maxX -gt $minX) {
        $png = Join-Path $LogDir "split_$Players.png"
        $bmp = New-Object Drawing.Bitmap ($maxX - $minX), ($maxY - $minY)
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($minX, $minY, 0, 0, $bmp.Size)
        $g.Dispose(); $bmp.Save($png, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        "foto dello schermo diviso: $png ($($maxX - $minX)x$($maxY - $minY))"
    }
}

# 4) si chiude l'ospite: gli altri devono uscire da soli
$kids = @($childPids.Values | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } | Where-Object { $_ })
Check 'prima di chiudere, tutte le finestre degli altri sono ancora aperte' ($kids.Count -eq ($Players - 1)) "aperte $($kids.Count)"
if (-not $ph.HasExited) { try { $ph.Kill() } catch { } ; $ph.WaitForExit(5000) | Out-Null }
$t0 = [Diagnostics.Stopwatch]::StartNew()
while ($t0.Elapsed.TotalSeconds -lt 20 -and ($kids | Where-Object { -not $_.HasExited })) { Start-Sleep -Milliseconds 300 }
$left = @($kids | Where-Object { -not $_.HasExited })
Check 'chiuso il giocatore 1, anche gli altri escono da soli' ($left.Count -eq 0) "usciti in $([math]::Round($t0.Elapsed.TotalSeconds, 1)) s"
foreach ($p in $left) { try { $p.Kill() } catch { } }

if ($fails.Count) { "RISULTATO FALLITA: $($fails.Count) controlli ($($fails -join '; '))"; exit 1 }
'RISULTATO SUPERATA'
exit 0

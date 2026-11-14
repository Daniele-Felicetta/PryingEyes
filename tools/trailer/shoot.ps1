<#
shoot.ps1 - runs one trailer job (tools/trailer/make_shots.py writes it) in the built player, at low priority, and waits for it.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\trailer\shoot.ps1 -Job unity_logs\trailer\prev2.json

The player needs the real GPU (no -nographics) and a build that contains the recorder (Builds\Audit after a normal build).
It refuses to start while another PryingEyes.exe is running (the GPU is shared with whoever is playing). The player log goes next to
the job (<job>_player.log); the recorder lines ("trailer:") are echoed at the end. All ASCII on purpose.
#>
param(
    [Parameter(Mandatory = $true)][string]$Job,
    [string]$Exe = '',
    [int]$TimeoutSec = 3300
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Exe) { $Exe = Join-Path $repo 'unity\PryingEyes\Builds\Audit\PryingEyes.exe' }
$job = (Resolve-Path -LiteralPath $Job).Path
$log = [IO.Path]::ChangeExtension($job, $null).TrimEnd('.') + '_player.log'
if (Get-Process PryingEyes -ErrorAction SilentlyContinue) { 'A PRYINGEYES PROCESS IS RUNNING: not starting (shared GPU).'; exit 4 }
if (-not (Test-Path -LiteralPath $Exe)) { "PLAYER NOT FOUND: $Exe"; exit 3 }
if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
$a = @('-batchmode', '-autotest', '-nofire', '-nowaves', '-seed', '1000', '-floor', '1', '-trailer', $job, '-logFile', $log)
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $Exe -ArgumentList $a -PassThru -WindowStyle Hidden
$null = $p.Handle
$p.PriorityClass = 'BelowNormal'
if (-not $p.WaitForExit($TimeoutSec * 1000)) { try { $p.Kill() } catch {}; "TIMEOUT after $TimeoutSec s (player killed)"; exit 2 }
"PLAYER exit=$($p.ExitCode) in $([math]::Round($sw.Elapsed.TotalSeconds, 1)) s  log=$log"
if (Test-Path -LiteralPath $log) {
    Select-String -LiteralPath $log -Pattern 'trailer:|error CS|Exception' | ForEach-Object { $_.Line.Trim() } | Select-Object -Last 60
} else { 'LOG MISSING' }
exit $p.ExitCode

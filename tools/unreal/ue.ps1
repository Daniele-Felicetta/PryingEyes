# Runs Unreal Engine's own command-line tools on the Unreal prototype (unreal/PryingEyes) at low CPU and I/O priority,
# so a build or a photo never starves the rest of the machine. Nothing here needs a window or the editor UI.
#
#   tools/unreal/ue.ps1 build                          compile the editor target (UnrealBuildTool)
#   tools/unreal/ue.ps1 python <script.py> [name=val]  run a Python script inside the editor (commandlet, no window);
#                                                      the name=val pairs reach the script as PE_<NAME> environment variables
#   tools/unreal/ue.ps1 photo [-pe_xxx=value ...]      run the game with a hidden window: it builds the scene named by
#                                                      -pe_scene, photographs it, times the frames and exits
#   tools/unreal/ue.ps1 game  [-pe_xxx=value ...]      same as photo but with a visible window (for looking by eye)
#
# UE_ROOT overrides the engine folder (default: <ProgramFiles>\Epic Games\UE_5.8). Logs go to unity_logs/unreal (git-ignored).
# Exit code: the tool's own, or 124 when -TimeoutSec ran out (the process is then killed).
param(
    [Parameter(Mandatory = $true, Position = 0)][ValidateSet('build', 'python', 'photo', 'game')][string]$Action,
    [int]$TimeoutSec = 1800,
    [int]$ResX = 1920,
    [int]$ResY = 1080,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repo 'unreal\PryingEyes\PryingEyes.uproject'
$ue = if ($env:UE_ROOT) { $env:UE_ROOT } else { Join-Path $env:ProgramFiles 'Epic Games\UE_5.8' }
$logDir = Join-Path $repo 'unity_logs\unreal'
New-Item -ItemType Directory -Force $logDir | Out-Null
if (-not (Test-Path $project)) { throw "missing $project" }
if (-not (Test-Path $ue)) { throw "Unreal Engine not found at $ue (set UE_ROOT)" }

if (-not ('UePrio' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class UePrio {
    [DllImport("ntdll.dll")] static extern int NtSetInformationProcess(IntPtr h, int cls, ref int val, int len);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    // class 33 = I/O priority: use 1 (low); 0 (very low) makes writes crawl
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
# the children (cmd, UnrealBuildTool, cl, link, shader workers) inherit these
[Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
$null = [UePrio]::Set($PID, 33, 1)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $logDir "$Action-$stamp.log"
switch ($Action) {
    'build' {
        $exe = Join-Path $ue 'Engine\Build\BatchFiles\Build.bat'
        $args1 = "PryingEyesEditor Win64 Development -Project=`"$project`" -WaitMutex -NoHotReload"
    }
    'python' {
        if (-not $Rest -or $Rest.Count -lt 1) { throw 'python needs a script path' }
        $script = (Resolve-Path $Rest[0]).Path
        foreach ($kv in ($Rest | Select-Object -Skip 1)) {
            $i = $kv.IndexOf('=')
            if ($i -lt 1) { throw "argument '$kv' is not name=value" }
            [Environment]::SetEnvironmentVariable('PE_' + $kv.Substring(0, $i).ToUpperInvariant(), $kv.Substring($i + 1))
        }
        $exe = Join-Path $ue 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
        $args1 = "`"$project`" -run=pythonscript -script=`"$script`" -unattended -nosplash -nullrdr -nopause -stdout -FullStdOutLogOutput"
    }
    default {
        $exe = Join-Path $ue 'Engine\Binaries\Win64\UnrealEditor.exe'
        $hidden = if ($Action -eq 'photo') { '-RenderOffscreen ' } else { '' }
        $extra = ($Rest -join ' ')
        $args1 = "`"$project`" /Engine/Maps/Entry -game ${hidden}-ForceRes -ResX=$ResX -ResY=$ResY -windowed -nosound -unattended -nosplash -nopause -stdout -FullStdOutLogOutput $extra"
    }
}
if (-not (Test-Path $exe)) { throw "missing $exe" }

$sw = [Diagnostics.Stopwatch]::StartNew()
$psi = New-Object Diagnostics.ProcessStartInfo
$psi.FileName = $env:ComSpec
$psi.Arguments = "/c `"`"$exe`" $args1 > `"$log`" 2>&1`""
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$p = [Diagnostics.Process]::Start($psi)
if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    & taskkill /PID $($p.Id) /T /F | Out-Null
    "TIMEOUT after $TimeoutSec s (log: $log)"
    exit 124
}
"exit $($p.ExitCode) in $([int]$sw.Elapsed.TotalSeconds) s (log: $log)"
exit $p.ExitCode

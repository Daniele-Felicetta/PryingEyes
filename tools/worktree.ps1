<#
worktree.ps1 - a git worktree that is ready for Unity, and a safe way to remove it (task T10 of docs/engineering/modular-roadmap.md).

A new worktree has no unity/PryingEyes/Library, so the editor imports every asset (tens of minutes, gigabytes written) before it can
compile. Copying the Library of a checkout that has the same Unity version and no editor open takes about a minute, and Unity then only
recompiles the scripts. Rules 9 and 12 of AGENTS.md send every agent that does not own the editor to a worktree and ask that it be removed
afterwards: this script does both ends. It prints what it did as ONE JSON document on stdout (schemaVersion 1); progress goes to stderr.
Exit code 0 = done (or, with -DryRun, possible), 1 = refused or failed; the JSON says why in "problems".

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\worktree.ps1 -New t07-harness
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\worktree.ps1 -New t07-harness -From room-variety -DryRun
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\worktree.ps1 -New t05-manifest -NoLibrary
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\worktree.ps1 -Remove t07-harness

-New <name>   `git worktree add` of <WorktreesDir>\<name> on a NEW branch (-Branch, default claude/<name>) cut from origin/preview, which
              is fetched first. The branch has no upstream: the first push names it. Then, unless -NoLibrary, the Library of a donor
              checkout is copied in. The donor is -From (a path, or the name of a worktree) or, without -From, the first checkout in
              this order that qualifies: the main checkout, then the other worktrees, the most recently used Library first. A donor
              qualifies when it has a Library, its ProjectSettings\ProjectVersion.txt has the same m_EditorVersion as origin/preview,
              it has no Temp\UnityLockfile and no Unity.exe runs on it. Otherwise nothing is created and the command refuses. The copy
              (robocopy /E /MT:4, folders named Temp left out) runs at below-normal CPU, low disk and low memory priority into
              Temp\Library.copy of the new project and is renamed to Library only when every file and byte arrived and the donor still has
              no lockfile, so a Library that exists is a whole one. -NoLibrary makes a plain worktree (for work that never opens Unity).
-Remove <name>
              The worktree is <WorktreesDir>\<name>, or any registered worktree of that folder name or path. It is removed with
              `git worktree remove` WITHOUT --force, and only when nothing would be lost: no uncommitted change (a change that is only
              line endings is not one; it is restored first), every commit published (on a branch of origin, or the head of a pull
              request there: git ls-remote origin refs/pull/*/head) and no Unity.exe or PryingEyes.exe running from it. The main checkout,
              a locked worktree and the folder this script runs from are refused. The local branch is deleted afterwards (git branch -D)
              only when it is merged: an ancestor of origin/preview, or the head of pull request N whose squash commit "(#N)" is on
              origin/preview (a squash merge makes git call the branch unmerged). A branch that is not merged is kept and said so.
              Nothing is ever pushed, and no branch of origin is deleted.
-DryRun       Checks everything and changes nothing (no fetch either: refs are as of the last fetch).

-From, -Branch, -WorktreesDir (default <main checkout>\.claude\worktrees), -Repo (the checkout whose worktrees are managed, default the
one this script is in), -MinFreeGB (free space that must remain after the copy, default 10), -RenameTries and -RenameWait (how many times,
and how many seconds apart, to ask Windows to rename the finished copy while a scanner may hold a file in it: 12 and 5) serve people with
another layout and the tests. A copy that arrived whole is never thrown away because of the rename: it stays in Temp\Library.copy.
No other worktree is ever touched; a donor is only read. The file is ASCII on purpose: Windows PowerShell 5.1 misreads accents in a file
without a BOM.
#>
param(
    [string]$New = '',
    [string]$Remove = '',
    [string]$From = '',
    [string]$Branch = '',
    [string]$Repo = '',
    [string]$WorktreesDir = '',
    [int]$MinFreeGB = 10,
    [int]$RenameTries = 12,
    [int]$RenameWait = 5,
    [switch]$NoLibrary,
    [switch]$FullSpeed,
    [switch]$DryRun
)
$ErrorActionPreference = 'Continue'
$env:GIT_TERMINAL_PROMPT = '0'      # no prompts: a fetch that needs credentials fails at once instead of hanging
$env:GCM_INTERACTIVE = 'never'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$Remote = 'origin'
$BaseBranch = 'preview'
$BaseRef = $Remote + '/' + $BaseBranch
$ProjectRel = 'unity/PryingEyes'
$ProjectWin = 'unity\PryingEyes'
$NetworkGuard = @('-c', 'http.lowSpeedLimit=1000', '-c', 'http.lowSpeedTime=30')    # give up on a stalled transfer instead of hanging

$problems = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$skipped = New-Object System.Collections.Generic.List[string]
$action = $null
if ($New -and -not $Remove) { $action = 'new' } elseif ($Remove -and -not $New) { $action = 'remove' }
$result = [ordered]@{ schemaVersion = 1; action = $action; ok = $false; dryRun = [bool]$DryRun }
$clock = [Diagnostics.Stopwatch]::StartNew()

function Say([string]$m) { [Console]::Error.WriteLine(('worktree: [{0,5:N1}s] ' -f $clock.Elapsed.TotalSeconds) + $m) }
function Fail([string]$m) { $problems.Add($m) }
function Finish([bool]$ok) {
    $result['ok'] = $ok
    $result['problems'] = @($problems)
    $result['warnings'] = @($warnings)
    $result['seconds'] = [math]::Round($clock.Elapsed.TotalSeconds, 1)
    $json = $result | ConvertTo-Json -Depth 8
    # Windows PowerShell 5.1 writes < > ' & as \u00XX: put them back, they read better and are valid in a JSON string
    $json = $json -replace '\\u003c', '<' -replace '\\u003e', '>' -replace '\\u0027', "'" -replace '\\u0026', '&'
    # ASCII on stdout whatever the console code page is: commit subjects and paths may carry accents
    $json = [regex]::Replace($json, '[^\x00-\x7F]', { param($m) '\u' + ([int][char]$m.Value).ToString('x4') })
    [Console]::Out.WriteLine($json)
    if ($ok) { exit 0 } else { exit 1 }
}

# ---------------------------------------------------------------- git and paths
$script:gitCode = 0
function Lines($o) { $r = @(); foreach ($x in @($o)) { if ($null -ne $x) { $r += [string]$x } }; return $r }
# core.longpaths: a Unity Library has files 186 characters below its folder and a worktree puts its own folder in front of them, so without it
# `git worktree remove` stops at 260 characters with "Filename too long" and leaves the folder behind
function GitQ([string]$dir, [string[]]$a) {      # stdout lines only; the exit code stays in $script:gitCode
    $o = & git -C $dir -c core.quotepath=false -c core.longpaths=true @a 2>$null
    $script:gitCode = $LASTEXITCODE
    return (Lines $o)
}
function GitR([string]$dir, [string[]]$a) {      # stdout and stderr, for the text of an error
    $o = & git -C $dir -c core.quotepath=false -c core.longpaths=true @a 2>&1
    $script:gitCode = $LASTEXITCODE
    return (Lines $o)
}
function Norm([string]$p) { return ([IO.Path]::GetFullPath($p)).TrimEnd('\', '/') }
function Same([string]$a, [string]$b) { return [string]::Equals((Norm $a), (Norm $b), [StringComparison]::OrdinalIgnoreCase) }
function Inside([string]$child, [string]$parent) {
    $c = Norm $child
    $p = Norm $parent
    return ($c -ieq $p) -or $c.StartsWith($p + '\', [StringComparison]::OrdinalIgnoreCase)
}
function Slash([string]$p) { return ([string]$p) -replace '\\', '/' }
function Short([string]$sha) { if ($sha.Length -ge 7) { return $sha.Substring(0, 7) } return $sha }

function Get-Worktrees {
    $out = New-Object System.Collections.Generic.List[object]
    $cur = $null
    foreach ($l in @(GitQ $Repo @('worktree', 'list', '--porcelain'))) {
        if ($l -match '^worktree (.+)$') {
            $cur = [pscustomobject]@{ path = (Norm $Matches[1]); head = ''; branch = ''; detached = $false; locked = $false; prunable = $false }
            $out.Add($cur)
        } elseif ($null -ne $cur) {
            if ($l -match '^HEAD (\S+)') { $cur.head = $Matches[1] }
            elseif ($l -match '^branch refs/heads/(.+)$') { $cur.branch = $Matches[1] }
            elseif ($l -eq 'detached') { $cur.detached = $true }
            elseif ($l -match '^locked') { $cur.locked = $true }
            elseif ($l -match '^prunable') { $cur.prunable = $true }
        }
    }
    return $out.ToArray()
}

function Get-UnityVersion($lines) {
    foreach ($l in @($lines)) { if ([string]$l -match '^m_EditorVersion:\s*(\S+)') { return $Matches[1] } }
    return ''
}

# the processes that make a checkout busy: an editor or a player started from it (the command line or the exe names its folder)
$script:procs = $null
function Get-Busy([string]$dir) {
    if ($null -eq $script:procs) {
        $script:procs = @()
        # WMI is slow: ask it only when an editor or a player runs at all
        if (([Diagnostics.Process]::GetProcessesByName('Unity').Count + [Diagnostics.Process]::GetProcessesByName('PryingEyes').Count) -gt 0) {
            foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe' OR Name='PryingEyes.exe'" -ErrorAction SilentlyContinue)) {
                $script:procs += [pscustomobject]@{ id = $p.ProcessId; name = $p.Name; text = (([string]$p.CommandLine) + ' ' + ([string]$p.ExecutablePath)) -replace '/', '\' }
            }
        }
    }
    $hit = @()
    $named = [regex]::Escape((Norm $dir)) + '($|[\\" ''])'      # the folder, then the end of a path: not the start of a longer folder name
    foreach ($p in $script:procs) {
        if ([regex]::IsMatch($p.text, $named, [Text.RegularExpressions.RegexOptions]::IgnoreCase)) { $hit += $p }
    }
    return $hit
}

# ---------------------------------------------------------------- native helpers: the priority of a copy, and its summary
function Initialize-Native {
    if ('WorktreeNative' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WorktreeNative {
    [DllImport("ntdll.dll")] static extern int NtSetInformationProcess(IntPtr h, int cls, ref int val, int len);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    // class 33 = I/O priority (0 very low, 1 low, 2 normal): use 1, with 0 the writes may never finish; class 39 = memory priority (0..7, normal 5)
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

# robocopy of the Library; -ListOnly measures without copying. /BYTES makes the summary plain integers in every language.
function Invoke-Robocopy([string]$src, [string]$dst, [bool]$listOnly) {
    $a = @($src, $dst, '/E', '/XD', 'Temp', '/XJ', '/BYTES', '/R:1', '/W:1', '/NJH', '/NDL', '/NFL', '/NP')
    if ($listOnly) { $a += '/L' } else { $a += '/MT:4' }
    $native = $false
    if (-not $listOnly -and -not $FullSpeed) {
        try { Initialize-Native; $native = $true } catch { $warnings.Add('the disk and memory priority of the copy could not be set: ' + $_.Exception.Message) }
    }
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = 'robocopy.exe'
    $psi.Arguments = (($a | ForEach-Object { if ($_ -match '[\s"]') { '"' + $_ + '"' } else { $_ } }) -join ' ')
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $p = [Diagnostics.Process]::Start($psi)
    try {
        if (-not $FullSpeed) { $p.PriorityClass = 'BelowNormal' }
        if ($native) {
            $io = [WorktreeNative]::Set($p.Id, 33, 1)
            $mem = [WorktreeNative]::Set($p.Id, 39, 2)
            if ($io -ne 0 -or $mem -ne 0) { $warnings.Add("the disk or memory priority of the copy was not set (NT status $io and $mem)") }
        }
    } catch { }
    $text = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit()
    return [pscustomobject]@{ code = $p.ExitCode; text = $text }
}
# the three rows "<label>: total copied skipped mismatched failed extras" of the summary are dirs, files and bytes, in that order
function Read-Totals([string]$text) {
    $rows = @()
    foreach ($l in ($text -split "`r?`n")) {
        if ($l -match '^\s*[^:\d]+:\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*$') {
            $rows += , @([long]$Matches[1], [long]$Matches[2], [long]$Matches[3], [long]$Matches[4], [long]$Matches[5], [long]$Matches[6])
        }
    }
    if ($rows.Count -lt 3) { return $null }
    return [pscustomobject]@{ files = $rows[1]; bytes = $rows[2] }
}
function Measure-Library([string]$donorDir) {
    $lib = Join-Path $donorDir ($ProjectWin + '\Library')
    $nowhere = Join-Path ([IO.Path]::GetTempPath()) ('worktree-measure-' + [guid]::NewGuid().ToString('N'))
    $r = Invoke-Robocopy $lib $nowhere $true
    $t = Read-Totals $r.text
    if ($r.code -ge 8 -or $null -eq $t) { return $null }
    return [pscustomobject]@{ files = $t.files[0]; bytes = $t.bytes[0] }
}
function Get-FreeBytes([string]$path) {
    try {
        $root = [IO.Path]::GetPathRoot((Norm $path))
        if ($root -match '^[A-Za-z]:\\$') { return (New-Object IO.DriveInfo $root).AvailableFreeSpace }
    } catch { }
    return -1
}

# ---------------------------------------------------------------- usage, and the checkout this script manages
if (-not $action) { Fail 'give exactly one of -New <name> and -Remove <name>'; Finish $false }
if (-not $Repo) { $Repo = Join-Path $PSScriptRoot '..' }
if (-not (Test-Path -LiteralPath $Repo -PathType Container)) { Fail "the repository does not exist: $Repo"; Finish $false }
$Repo = (Resolve-Path -LiteralPath $Repo).Path.TrimEnd('\')
$wts = @(Get-Worktrees)
if ($wts.Count -eq 0) { Fail "not a git repository: $Repo"; Finish $false }
$mainPath = $wts[0].path                  # git lists the main checkout first
if (-not $WorktreesDir) { $WorktreesDir = Join-Path $mainPath '.claude\worktrees' }
$WorktreesDir = Norm $WorktreesDir

# a published commit: is the tip the head of a pull request on origin? Returns its number, 0 when none, -1 when origin cannot be asked.
function Find-PullHead([string]$tip) {
    $l = @(GitR $Repo ($NetworkGuard + @('ls-remote', $Remote, 'refs/pull/*/head')))
    if ($script:gitCode -ne 0) { return -1 }
    foreach ($x in $l) {
        if ($x -match '^([0-9a-f]{40})\s+refs/pull/(\d+)/head$' -and $Matches[1] -eq $tip) { return [int]$Matches[2] }
    }
    return 0
}
function Update-Base {
    if ($DryRun) { $warnings.Add("-DryRun does not fetch: $BaseRef is as of the last fetch"); return $false }
    Say "fetching $Remote $BaseBranch"
    $null = GitR $Repo ($NetworkGuard + @('fetch', '--quiet', $Remote, $BaseBranch))
    if ($script:gitCode -ne 0) { $warnings.Add("git fetch $Remote $BaseBranch failed: $BaseRef is as of the last fetch"); return $false }
    return $true
}

# ================================================================ -New
if ($action -eq 'new') {
    if ($New -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,48}$') { Fail "-New takes a name of letters, digits, '-' and '_' (got '$New')"; Finish $false }
    if (-not $Branch) { $Branch = 'claude/' + $New }
    $wtPath = Join-Path $WorktreesDir $New
    $result['name'] = $New
    $result['path'] = Slash $wtPath
    $result['branch'] = $Branch

    if (Test-Path -LiteralPath $wtPath) { Fail "$(Slash $wtPath) already exists: nothing is touched" }
    foreach ($w in $wts) { if ($w.branch -eq $Branch) { Fail "branch $Branch is already checked out in $(Slash $w.path)" } }
    foreach ($ref in @(GitQ $Repo @('for-each-ref', '--format=%(refname)', "refs/heads/$Branch", "refs/remotes/$Remote/$Branch"))) {
        if ($ref -eq "refs/heads/$Branch") { Fail "branch $Branch already exists: nothing is touched" }
        else { Fail "branch $Branch already exists on ${Remote}: pick another name or -Branch" }
    }
    if ($problems.Count -gt 0) { Finish $false }

    $fetched = Update-Base
    $baseSha = [string](@(GitQ $Repo @('rev-parse', '--verify', '--quiet', "refs/remotes/$Remote/$BaseBranch"))[0])
    if (-not $baseSha) { Fail "$BaseRef does not exist: git fetch $Remote $BaseBranch first"; Finish $false }
    $result['base'] = [ordered]@{ ref = $BaseRef; commit = $baseSha; fetched = $fetched }

    # ---- the donor of the Library
    $version = ''
    $donor = $null
    $measure = $null
    if ($NoLibrary) {
        $result['library'] = [ordered]@{ copied = $false; reason = '-NoLibrary' }
    } else {
        $version = Get-UnityVersion @(GitQ $Repo @('show', "${BaseRef}:$ProjectRel/ProjectSettings/ProjectVersion.txt"))
        if (-not $version) { Fail "cannot read the Unity version of $BaseRef ($ProjectRel/ProjectSettings/ProjectVersion.txt): -NoLibrary skips the Library"; Finish $false }
        $result['unityVersion'] = $version

        # '' when the checkout can give its Library, otherwise the reason it cannot
        function Test-Donor([string]$dir) {
            $proj = Join-Path $dir $ProjectWin
            if (-not (Test-Path -LiteralPath (Join-Path $proj 'Library') -PathType Container)) { return 'no Library' }
            $v = Get-UnityVersion @(Get-Content -LiteralPath (Join-Path $proj 'ProjectSettings\ProjectVersion.txt') -ErrorAction SilentlyContinue)
            if ($v -ne $version) { return "Unity $v, not $version" }
            if (Test-Path -LiteralPath (Join-Path $proj 'Temp\UnityLockfile')) { return 'Temp/UnityLockfile exists: an editor has it open' }
            $busy = @(Get-Busy $proj | Where-Object { $_.name -eq 'Unity.exe' })
            if ($busy.Count -gt 0) { return "Unity (pid $($busy[0].id)) is running on it" }
            return ''
        }

        if ($From) {
            $dir = $null
            if (Test-Path -LiteralPath $From -PathType Container) { $dir = Norm (Resolve-Path -LiteralPath $From).Path }
            else { foreach ($w in $wts) { if ((Split-Path $w.path -Leaf) -ieq $From) { $dir = $w.path; break } } }
            if (-not $dir) { Fail "-From '$From' is neither a folder nor the name of a worktree"; Finish $false }
            $why = Test-Donor $dir
            if ($why) { Fail "donor $(Slash $dir): $why"; Finish $false }
            $donor = $dir
        } else {
            $ranked = @($wts | Select-Object -Skip 1 | Sort-Object -Descending { $p = Join-Path $_.path ($ProjectWin + '\Library'); if (Test-Path -LiteralPath $p) { (Get-Item -LiteralPath $p).LastWriteTime } else { [datetime]::MinValue } })
            foreach ($w in (@($wts[0]) + $ranked)) {
                $why = Test-Donor $w.path
                if (-not $why) { $donor = $w.path; break }
                if ($why -ne 'no Library') { $skipped.Add("$(Split-Path $w.path -Leaf): $why") }
            }
            if (-not $donor) {
                Fail ('no checkout can give its Library (needs Unity ' + $version + ', no editor open on it): ' + $(if ($skipped.Count -gt 0) { $skipped -join '; ' } else { 'none has a Library' }) + '. Pass -From <checkout>, or -NoLibrary for a worktree that never opens Unity')
                Finish $false
            }
        }
        $measure = Measure-Library $donor
        if ($null -eq $measure) { Fail "cannot measure the Library of $(Slash $donor)"; Finish $false }
        $free = Get-FreeBytes $WorktreesDir
        if ($free -ge 0 -and $free -lt ($measure.bytes + [long]$MinFreeGB * 1GB)) {
            Fail ("not enough disk: the Library is {0:N1} GB, {1:N1} GB are free and {2} GB must remain" -f ($measure.bytes / 1GB), ($free / 1GB), $MinFreeGB)
            Finish $false
        }
        $lib = [ordered]@{ copied = $false; donor = (Slash $donor); files = $measure.files; bytes = $measure.bytes }
        if ($skipped.Count -gt 0) { $lib['skippedDonors'] = @($skipped) }
        $result['library'] = $lib
    }

    if ($DryRun) {
        if ($donor) { $result['library']['wouldCopy'] = $true }
        Finish $true
    }

    # ---- the worktree
    Say "creating $(Slash $wtPath) on $Branch from $BaseRef ($(Short $baseSha))"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $o = @(GitR $Repo @('worktree', 'add', '--no-track', '-b', $Branch, $wtPath, $BaseRef))
    if ($script:gitCode -ne 0) { Fail ('git worktree add failed: ' + ($o -join ' | ')); Finish $false }
    $result['worktreeSeconds'] = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    $result['created'] = $true

    # ---- the Library: into Temp\Library.copy (ignored by git, on the same volume), renamed when it is whole
    if ($donor) {
        $proj = Join-Path $wtPath $ProjectWin
        $stage = Join-Path $proj 'Temp\Library.copy'
        $final = Join-Path $proj 'Library'
        $donorLock = Join-Path $donor ($ProjectWin + '\Temp\UnityLockfile')
        $donorLib = Join-Path $donor ($ProjectWin + '\Library')
        Say ("copying the Library of $(Slash $donor): {0:N0} files, {1:N2} GB" -f $measure.files, ($measure.bytes / 1GB))
        $sw = [Diagnostics.Stopwatch]::StartNew()
        [void](New-Item -ItemType Directory -Force -Path $stage)
        $r = Invoke-Robocopy $donorLib $stage $false
        $t = Read-Totals $r.text
        $seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        $whole = ($r.code -lt 8) -and ($null -ne $t) -and ($t.files[1] -eq $measure.files) -and ($t.bytes[1] -eq $measure.bytes) -and ($t.files[4] -eq 0) -and ($t.files[3] -eq 0)
        $keepStage = $false
        if (-not $whole) {
            Fail ('the copy of the Library is incomplete (robocopy exit ' + $r.code + '): ' + $(if ($t) { "copied $($t.files[1]) of $($measure.files) files, failed $($t.files[4])" } else { 'no summary' }))
        } elseif (Test-Path -LiteralPath $donorLock) {
            $whole = $false
            Fail 'an editor opened the donor while its Library was being copied: the copy may be inconsistent and was dropped'
        } else {
            # Windows refuses to rename a folder while something holds a file inside it (a scanner looking at the 30 thousand files just written):
            # ask again for a while. A copy that arrived whole is never thrown away for this: it stays where it is, and the answer says so.
            $tries = 0
            $moveError = ''
            $renamed = $false
            while (-not $renamed -and $tries -lt $RenameTries) {
                $tries++
                try { [IO.Directory]::Move($stage, $final); $renamed = $true }
                catch {
                    $moveError = $_.Exception.Message
                    if ($tries -lt $RenameTries) { Say "the copy cannot be renamed yet ($moveError): try $tries of $RenameTries, again in $RenameWait s"; Start-Sleep -Seconds $RenameWait }
                }
            }
            $result['library']['renameTries'] = $tries
            if (-not $renamed) {
                $whole = $false
                $keepStage = $true
                Fail ("the copy arrived whole but cannot be renamed to Library after $tries tries ($moveError): it is left at $(Slash $stage), rename it once nothing holds a file in it")
            }
        }
        if (-not $whole -and -not $keepStage -and (Inside $stage $wtPath) -and (Test-Path -LiteralPath $stage)) {
            # \\?\ lifts the 260 character limit that Remove-Item has in Windows PowerShell 5.1 (a Library goes past it inside a worktree)
            try { [IO.Directory]::Delete('\\?\' + $stage, $true) } catch { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
        }
        $tempDir = Join-Path $proj 'Temp'
        if ((Test-Path -LiteralPath $tempDir) -and -not (Get-ChildItem -LiteralPath $tempDir -Force)) { Remove-Item -LiteralPath $tempDir -Force -ErrorAction SilentlyContinue }
        $result['library']['copied'] = $whole
        $result['library']['seconds'] = $seconds
        $result['library']['priority'] = $(if ($FullSpeed) { 'normal' } else { 'low' })
        if (-not $whole) {
            $warnings.Add($(if ($keepStage) { 'the worktree has its Library only as Temp\Library.copy: rename it, or remove the worktree (-Remove) and try again' } else { 'the worktree exists without a Library: remove it (-Remove) and try again, or open it in Unity and wait for the import' }))
            Finish $false
        }
    }
    Finish $true
}

# ================================================================ -Remove
$found = @()
$byDefault = Join-Path $WorktreesDir $Remove
foreach ($w in $wts) { if (Same $w.path $byDefault) { $found = @($w); break } }
if ($found.Count -eq 0) {
    $asPath = $null
    if (Test-Path -LiteralPath $Remove -PathType Container) { $asPath = (Resolve-Path -LiteralPath $Remove).Path }
    foreach ($w in $wts) {
        if (($asPath -and (Same $w.path $asPath)) -or ((Split-Path $w.path -Leaf) -ieq $Remove)) { $found += $w }
    }
}
if ($found.Count -eq 0) { Fail "no worktree is named or located at '$Remove' (git worktree list shows them)"; Finish $false }
if ($found.Count -gt 1) { Fail ("'$Remove' names more than one worktree: " + (($found | ForEach-Object { Slash $_.path }) -join ', ') + ': give the full path'); Finish $false }
$target = $found[0]
$result['name'] = Split-Path $target.path -Leaf
$result['path'] = Slash $target.path
$result['branch'] = $(if ($target.branch) { $target.branch } else { $null })
$result['removed'] = $false

if (Same $target.path $mainPath) { Fail 'this is the main checkout, not a linked worktree: it is never removed'; Finish $false }
if ($target.locked) { Fail 'the worktree is locked (git worktree unlock first)'; Finish $false }
if (-not (Test-Path -LiteralPath $target.path)) { Fail 'the folder is missing: `git worktree prune` drops the record of it'; Finish $false }
if ((Inside (Get-Location).Path $target.path) -or (Inside $Repo $target.path)) { Fail 'this shell or this script is inside the worktree: run the script from another checkout (the main one)'; Finish $false }
foreach ($p in @(Get-Busy $target.path)) { Fail "$($p.name) (pid $($p.id)) is running from this worktree: close it first" }
if ($problems.Count -gt 0) { Finish $false }

$null = Update-Base

# uncommitted work is what `git status` lists: it is the test that git worktree remove makes itself, and with core.autocrlf=true it lists a file that
# Unity rewrote with other line endings although `git diff HEAD` shows nothing (the first version of this script asked `git diff` and was refused by git)
function Get-Dirty([string]$dir) {            # path -> the two-letter code of `git status --porcelain`; the exit code of git stays in $script:gitCode
    $d = @{}
    foreach ($l in @(GitQ $dir @('status', '--porcelain', '--no-renames', '--untracked-files=all'))) {
        if ($l -match '^(..) (.+)$') {
            $p = $Matches[2]
            if ($p.Length -ge 2 -and $p.StartsWith('"') -and $p.EndsWith('"')) { $p = ($p.Substring(1, $p.Length - 2) -replace '\\t', "`t") -replace '\\(["\\])', '$1' }      # a name with a space is quoted
            $d[$p] = $Matches[1]
        }
    }
    return $d
}
$dirty = Get-Dirty $target.path
if ($script:gitCode -ne 0) { Fail 'cannot read the status of the worktree'; Finish $false }
# a file that differs from HEAD only in its line endings is not work: git diff ignoring them lists nothing for it, and it is put back from HEAD below
$real = @{}
$numstat = @(GitQ $target.path @('diff', '--numstat', '--no-renames', '--ignore-cr-at-eol', 'HEAD', '--'))
$numstatOk = ($script:gitCode -eq 0)
foreach ($l in $numstat) { if ($l -match '^(\d+|-)\t(\d+|-)\t(.+)$') { $real[$Matches[3]] = $true } }
$names = [string[]]@($dirty.Keys)
[Array]::Sort($names, [StringComparer]::Ordinal)
$work = @()
$eolOnly = @()
foreach ($p in $names) {
    if ($numstatOk -and $dirty[$p] -eq ' M' -and -not $real.ContainsKey($p)) { $eolOnly += $p; continue }
    $work += (([string]$dirty[$p]).Trim() + ' ' + $p)
}
if ($work.Count -gt 0) {
    $result['uncommitted'] = @($work | Select-Object -First 50)
    Fail ("$($work.Count) uncommitted change(s): commit them, or restore what is only noise with git -C " + (Slash $target.path) + ' checkout -- <file>')
}

# published work: every commit of HEAD is on a branch of origin, or HEAD is the head of a pull request there
$tip = [string]$target.head
$count = @(GitQ $target.path @('rev-list', '--count', 'HEAD', '--not', "--remotes=$Remote"))
if ($script:gitCode -ne 0 -or $count.Count -eq 0 -or -not $tip) { Fail 'cannot read the history of the worktree'; Finish $false }
$unpushed = [int]$count[0]
$null = GitQ $Repo @('merge-base', '--is-ancestor', $tip, $BaseRef)
$merged = ($script:gitCode -eq 0)
$pull = 0
if (-not $merged) {
    $pull = Find-PullHead $tip
    if ($pull -lt 0) { $warnings.Add("origin could not be asked for its pull requests: a squash-merged or unpushed branch cannot be told apart"); $pull = 0 }
}
if ($unpushed -gt 0 -and $pull -le 0) {
    $result['unpushed'] = @(GitQ $target.path @('log', '--format=%h %s', '-n', '20', 'HEAD', '--not', "--remotes=$Remote"))
    Fail "$unpushed commit(s) are on no branch of ${Remote}: push them (tools/ship.ps1 does), or if the pull request was merged and its branch deleted, its head must still be on $Remote (git ls-remote $Remote 'refs/pull/*/head')"
}
if (-not $merged -and $pull -gt 0) {
    foreach ($subject in @(GitQ $Repo @('log', $BaseRef, '--format=%s', '--fixed-strings', '--grep', "(#$pull)", '-n', '20'))) {
        if ($subject.EndsWith("(#$pull)")) { $merged = $true; break }       # what GitHub's squash merge makes of "<title>"
    }
}
$result['commit'] = $tip
$result['published'] = $(if ($unpushed -eq 0) { 'on a branch of ' + $Remote } elseif ($pull -gt 0) { "head of pull request #$pull" } else { 'no' })
$result['merged'] = $merged
if ($problems.Count -gt 0) { Finish $false }

if ($DryRun) {
    $result['removed'] = $false
    $result['wouldRemove'] = $true
    $result['wouldDeleteBranch'] = [bool]($merged -and $target.branch)
    Finish $true
}

# a change of line endings only is noise that git would still call dirty: put HEAD back (the content is the same), then remove without force
for ($i = 0; $i -lt $eolOnly.Count; $i += 100) {
    $batch = @($eolOnly[$i..([math]::Min($i + 99, $eolOnly.Count - 1))])
    $o = @(GitR $target.path (@('checkout', 'HEAD', '--') + $batch))
    if ($script:gitCode -ne 0) { Fail ('cannot restore the files that differ only in line endings: ' + ($o -join ' | ')); Finish $false }
}
if ($eolOnly.Count -gt 0) {
    $result['lineEndingsRestored'] = $eolOnly.Count
    # git must agree that the worktree is clean now: it checks the same thing before it deletes, and a refusal there is better said here
    $left = Get-Dirty $target.path
    if ($left.Count -gt 0) {
        $result['uncommitted'] = @($left.Keys | Sort-Object | Select-Object -First 50 | ForEach-Object { ([string]$left[$_]).Trim() + ' ' + $_ })
        Fail "git still lists $($left.Count) change(s) after the files that differ only in line endings were put back from HEAD"
        Finish $false
    }
}

Say "removing $(Slash $target.path)"
$sw = [Diagnostics.Stopwatch]::StartNew()
$o = @(GitR $Repo @('worktree', 'remove', $target.path))
if ($script:gitCode -ne 0 -or (Test-Path -LiteralPath $target.path)) {
    $why = ($o -join ' | ')
    $registered = (@(Get-Worktrees | Where-Object { Same $_.path $target.path })).Count -gt 0
    if (-not $registered -and (Test-Path -LiteralPath $target.path)) {
        # git drops its record of the worktree first and deletes the folder second: what it could not delete (a file held open) stays, unregistered
        $result['leftover'] = Slash $target.path
        Fail ("git removed the record of the worktree but could not delete its folder, which is left at $(Slash $target.path): delete it by hand once what stops git is gone" + $(if ($target.branch) { ", then git branch -D $($target.branch) if it is merged" } else { '' }) + " ($why)")
    } else {
        Fail ('git worktree remove failed: ' + $why)
    }
    $result['removed'] = $false
    Finish $false
}
$result['removed'] = $true
$result['removeSeconds'] = [math]::Round($sw.Elapsed.TotalSeconds, 1)

if ($target.branch -and $merged) {
    $o = @(GitR $Repo @('branch', '-D', $target.branch))
    if ($script:gitCode -eq 0) { $result['branchDeleted'] = [ordered]@{ name = $target.branch; was = $tip } }
    else { $warnings.Add("the branch $($target.branch) could not be deleted: " + ($o -join ' | ')) }
} elseif ($target.branch) {
    $result['branchKept'] = "$($target.branch) is not merged into ${BaseRef}: delete it with git branch -D once its pull request is merged"
}
Finish $true

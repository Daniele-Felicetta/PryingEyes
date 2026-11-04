Add-Type -MemberDefinition '[DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h, out RECT r); [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h); [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h); public struct RECT { public int L; public int T; public int R; public int B; }' -Name Q1 -Namespace X1
$p = Get-Process -Name PryingEyes -ErrorAction SilentlyContinue
if (-not $p) { Write-Output 'NOT RUNNING'; exit 0 }
$h = $p.MainWindowHandle
$r = New-Object X1.Q1+RECT
[X1.Q1]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hh = $r.B - $r.T
$ico = [X1.Q1]::IsIconic($h); $vis = [X1.Q1]::IsWindowVisible($h)
Write-Output "RECT $($r.L),$($r.T) ${w}x${hh} iconic=$ico visible=$vis"

Add-Type -MemberDefinition '[DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h, int n); [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);' -Name W3 -Namespace X3
$p = Get-Process | Where-Object { $_.MainWindowTitle -match 'PryingEyes' } | Select-Object -First 1
if (-not $p) { Write-Output "NO-GAME-WINDOW"; exit 1 }
$h = $p.MainWindowHandle
if ([X3.W3]::IsIconic($h)) { [X3.W3]::ShowWindow($h, 9) | Out-Null }
[X3.W3]::ShowWindow($h, 1) | Out-Null
Start-Sleep -Milliseconds 500
[X3.W3]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1500
Write-Output "FOCUSED"

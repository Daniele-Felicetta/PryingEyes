Add-Type -AssemblyName UIAutomationClient
$ws = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
  [System.Windows.Automation.TreeScope]::Children,
  [System.Windows.Automation.Condition]::TrueCondition)
foreach ($w in $ws) {
  try {
    $t = $w.Current.Name
    if ($t -like '*Unreal Editor*' -or $t -like '*PryingEyes*') {
      $h = $w.Current.NativeWindowHandle
      Add-Type -MemberDefinition '[DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h, int n); [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);' -Name W2 -Namespace X2
      $iconic = [X2.W2]::IsIconic($h)
      if ($iconic) { [X2.W2]::ShowWindow($h, 9) | Out-Null }
      Start-Sleep -Milliseconds 300
      [X2.W2]::SetForegroundWindow($h) | Out-Null
      Write-Output "FOCUSED-OK: $t (wasMinimized=$iconic)"
      exit 0
    }
  } catch { }
}
Write-Output "NOT-FOUND"
exit 1

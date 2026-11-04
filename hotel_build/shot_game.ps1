$code = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class WinCap {
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, int f);
  public struct RECT { public int L,T,R,B; }
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing
$h = [WinCap]::FindWindow($null, "PryingEyes")
if ($h -eq [IntPtr]::Zero) {
  $p = Get-Process | Where-Object { $_.MainWindowTitle -match 'PryingEyes' } | Select-Object -First 1
  if ($p) { $h = $p.MainWindowHandle; Write-Output "HANDLE-FROM-PROC" }
  else { Write-Output "NO-WINDOW"; exit 1 }
}
$r = New-Object WinCap+RECT
[WinCap]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hh = $r.B - $r.T
Write-Output "RECT ${w}x${hh}"
$bmp = New-Object Drawing.Bitmap($w, $hh)
$g = [Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[WinCap]::PrintWindow($h, $dc, 0) | Out-Null
$g.ReleaseHdc($dc)
$bmp.Save("C:\Users\danie\Desktop\prying-eyes\hotel_build\unity_run1.png", [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "SAVED"

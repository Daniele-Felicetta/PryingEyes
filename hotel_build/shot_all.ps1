$code = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class ScrCap2 {
  [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr h);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing
# virtual screen (all monitors)
$vx = [ScrCap2]::GetSystemMetrics(76); $vy = [ScrCap2]::GetSystemMetrics(77)
$vw = [ScrCap2]::GetSystemMetrics(78); $vh = [ScrCap2]::GetSystemMetrics(79)
Write-Output "VSCREEN ${vx},${vy} ${vw}x${vh}"
# bring game front (no restore, just foreground)
$p = Get-Process | Where-Object { $_.MainWindowTitle -match 'PryingEyes' } | Select-Object -First 1
if ($p) {
  [ScrCap2]::ShowWindow($p.MainWindowHandle, 1) | Out-Null
  [ScrCap2]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 1200
}
$desk = [ScrCap2]::GetDesktopWindow()
$src = [ScrCap2]::GetWindowDC($desk)
$bmp = New-Object Drawing.Bitmap($vw, $vh)
$g = [Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[ScrCap2]::BitBlt($dc, 0, 0, $vw, $vh, $src, $vx, $vy, 13369376) | Out-Null
$g.ReleaseHdc($dc)
[ScrCap2]::ReleaseDC($desk, $src) | Out-Null
$bmp.Save("C:\Users\danie\Desktop\prying-eyes\hotel_build\unity_run3.png", [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "SAVED"

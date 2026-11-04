$code = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class ScrCap {
  [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr h);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int n);
}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing
$W = [ScrCap]::GetSystemMetrics(0); $H = [ScrCap]::GetSystemMetrics(1)
$desk = [ScrCap]::GetDesktopWindow()
$src = [ScrCap]::GetWindowDC($desk)
$bmp = New-Object Drawing.Bitmap($W, $H)
$g = [Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[ScrCap]::BitBlt($dc, 0, 0, $W, $H, $src, 0, 0, 13369376) | Out-Null
$g.ReleaseHdc($dc)
[ScrCap]::ReleaseDC($desk, $src) | Out-Null
$bmp.Save("C:\Users\danie\Desktop\prying-eyes\hotel_build\unity_run2.png", [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "SAVED ${W}x${H}"

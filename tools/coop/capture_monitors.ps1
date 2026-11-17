# Foto di ogni monitor del PC, a grandezza vera, dal sistema (non dal gioco: il gioco non puo' leggere cio' che Unity mostra sugli altri monitor).
# Lo usa tools/coop/run_split.ps1 -Graphics -Monitors N mentre la prova dello schermo condiviso e' ferma ad aspettare; si puo' lanciare anche a mano.
# Scrive <Prefix>1.png, <Prefix>2.png ... in -OutDir (uno per monitor, nell'ordine di Windows) e stampa una riga per monitor.
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\unity_logs\seats'),
    [string]$Prefix = 'monitor'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CaptureDpi {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    // per monitor (v2) se c'e', altrimenti del sistema: serve perche' le coordinate dei monitor siano i pixel veri e non quelli ridotti dalla scala di Windows
    public static void Enable() {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (Exception) { }
        try { SetProcessDPIAware(); } catch (Exception) { }
    }
}
'@
[CaptureDpi]::Enable()
New-Item -ItemType Directory -Force $OutDir | Out-Null
$i = 0
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    $i++
    $b = $s.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.X, $b.Y, 0, 0, $b.Size)
    $path = Join-Path $OutDir ('{0}{1}.png' -f $Prefix, $i)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    'monitor {0}: {1}x{2} a {3},{4}{5} -> {6}' -f $i, $b.Width, $b.Height, $b.X, $b.Y, $(if ($s.Primary) { ' (principale)' } else { '' }), $path
}

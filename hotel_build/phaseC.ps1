# Fase C — gabbia di vetro per coccodrilli (angolo NW) + 2 coccodrilli
. "$PSScriptRoot\ue_common.ps1"

$F = "Hotel/CrocCage"
$M = @{
  Base   = "/Game/Hotel/Materials/MI_WallPlaster.MI_WallPlaster"
  Glass  = "/Game/Hotel/Materials/MI_GlassFake.MI_GlassFake"
  Water  = "/Game/Hotel/Materials/MI_WaterFake.MI_WaterFake"
  Skin   = "/Game/Hotel/Materials/MI_CrocSkin.MI_CrocSkin"
  Dark   = "/Game/Hotel/Materials/MI_CrocDark.MI_CrocDark"
  Eye    = "/Game/Hotel/Materials/MI_CrocEye.MI_CrocEye"
  Tooth  = "/Game/Hotel/Materials/MI_Paper.MI_Paper"
  Wood   = "/Game/Hotel/Materials/MI_WoodDark.MI_WoodDark"
  Gold   = "/Game/Hotel/Materials/MI_Gold.MI_Gold"
  Red    = "/Game/Hotel/Materials/MI_RedGlow.MI_RedGlow"
  Carpet = "/Game/Hotel/Materials/MI_Carpet.MI_Carpet"
}

$CX = -2900; $CZ = -1850  # centro gabbia: x -3400..-2400, y -2200..-1500

# basamento + acqua + isolotto + rocce
Box "Croc_Base" @($CX, $CZ, 25) @(10, 7, 0.5) $M.Base $F "cube"
Box "Croc_Water" @($CX, $CZ, 52) @(9.4, 6.4, 0.1) $M.Water $F "cube"
Box "Croc_Island" @(-3050, -1900, 60) @(2.4, 1.8, 0.5) $M.Base $F "sphere"
Box "Croc_Rock1" @(($CX + 250), ($CZ + 150), 60) @(0.5, 0.45, 0.5) $M.Dark $F "sphere"
Box "Croc_Rock2" @(($CX + 320), ($CZ - 120), 55) @(0.35, 0.3, 0.35) $M.Dark $F "sphere"
Box "Croc_Rock3" @(($CX - 350), ($CZ + 180), 58) @(0.4, 0.35, 0.4) $M.Dark $F "sphere"

# vetri h=2.2m (z 50..270)
Box "Croc_Glass_N" @($CX, -2197, 160) @(10, 0.06, 2.2) $M.Glass $F "cube"
Box "Croc_Glass_S" @($CX, -1503, 160) @(10, 0.06, 2.2) $M.Glass $F "cube"
Box "Croc_Glass_W" @(-3397, $CZ, 160) @(0.06, 7, 2.2) $M.Glass $F "cube"
Box "Croc_Glass_E" @(-2403, $CZ, 160) @(0.06, 7, 2.2) $M.Glass $F "cube"

# montanti + cornice
foreach ($p in @(@(-3400, -2200), @(-2900, -2200), @(-2400, -2200), @(-3400, -1500), @(-2900, -1500), @(-2400, -1500), @(-3400, -1850), @(-2400, -1850))) {
  Box "Croc_Post_$($p[0])_$($p[1])" @($p[0], $p[1], 160) @(0.14, 0.14, 2.6) $M.Wood $F "cube"
}
Box "Croc_Rim_N" @($CX, -2197, 278) @(10.2, 0.12, 0.12) $M.Wood $F "cube"
Box "Croc_Rim_S" @($CX, -1503, 278) @(10.2, 0.12, 0.12) $M.Wood $F "cube"

# lampada riscaldante: palo + bulbo rosso
Box "Croc_HeatPole" @(-2450, -1600, 160) @(0.1, 0.1, 3.2) $M.Wood $F "cylinder"
Box "Croc_HeatBulb" @(-2450, -1600, 290) @(0.18, 0.18, 0.18) $M.Red $F "sphere"

# targa ottone sul fronte
Box "Croc_Plaque" @($CX, -1494, 105) @(1.6, 0.1, 0.8) $M.Gold $F "cube"

# transenne con corda: paletti + corde (rettangolo x -3450..-2350, y -2260..-1440)
$posts = @()
foreach ($x in @(-3450, -3175, -2900, -2625, -2350)) { $posts += ,@($x, -2260); $posts += ,@($x, -1440) }
foreach ($y in @(-2057, -1853, -1647)) { $posts += ,@(-3450, $y); $posts += ,@(-2350, $y) }
$pi = 0
foreach ($p in $posts) {
  Box "Croc_Pole_$pi" @($p[0], $p[1], 47) @(0.1, 0.1, 0.95) $M.Gold $F "cylinder"
  $pi++
}
function Rope($n, $x1, $y1, $x2, $y2) {
  $len = [Math]::Sqrt((($x2 - $x1) * ($x2 - $x1)) + (($y2 - $y1) * ($y2 - $y1))) / 100.0
  $cx = ($x1 + $x2) / 2; $cy = ($y1 + $y2) / 2
  if ([Math]::Abs($x2 - $x1) -ge [Math]::Abs($y2 - $y1)) {
    Box $n @($cx, $cy, 85) @($len, 0.06, 0.06) $M.Carpet $F "cube"
  }
  else {
    Box $n @($cx, $cy, 85) @(0.06, $len, 0.06) $M.Carpet $F "cube"
  }
}
Rope "Croc_Rope_N1" -3450 -2260 -2900 -2260
Rope "Croc_Rope_N2" -2900 -2260 -2350 -2260
Rope "Croc_Rope_S1" -3450 -1440 -2900 -1440
Rope "Croc_Rope_S2" -2900 -1440 -2350 -1440
Rope "Croc_Rope_W" -3450 -2260 -3450 -1440
Rope "Croc_Rope_E" -2350 -2260 -2350 -1440

# ---- coccodrillo (parti come attori separati; $D=1 muso +X, $D=-1 muso -X) ----
function Croc($Tag, $Ox, $Oy, $Oz, $D, $S) {
  $P = "Hotel/Crocs"
  $sk = $M.Skin; $dk = $M.Dark
  Box "${Tag}_Body" @(($Ox), $Oy, ($Oz + 30 * $S)) @((1.5 * $S), (0.55 * $S), (0.35 * $S)) $sk $P "cube"
  Box "${Tag}_Tail1" @(($Ox - $D * 1.0 * $S), $Oy, ($Oz + 28 * $S)) @((0.5 * $S), (0.34 * $S), (0.22 * $S)) $sk $P "cube"
  Box "${Tag}_Tail2" @(($Ox - $D * 1.45 * $S), $Oy, ($Oz + 28 * $S)) @((0.45 * $S), (0.24 * $S), (0.16 * $S)) $sk $P "cube"
  Box "${Tag}_Tail3" @(($Ox - $D * 1.85 * $S), $Oy, ($Oz + 28 * $S)) @((0.4 * $S), (0.14 * $S), (0.1 * $S)) $sk $P "cube"
  Box "${Tag}_Head" @(($Ox + $D * 0.95 * $S), $Oy, ($Oz + 32 * $S)) @((0.45 * $S), (0.4 * $S), (0.24 * $S)) $sk $P "cube"
  Box "${Tag}_Snout" @(($Ox + $D * 1.4 * $S), $Oy, ($Oz + 28 * $S)) @((0.5 * $S), (0.3 * $S), (0.15 * $S)) $sk $P "cube"
  Box "${Tag}_Jaw" @(($Ox + $D * 1.42 * $S), $Oy, ($Oz + 19 * $S)) @((0.55 * $S), (0.27 * $S), (0.07 * $S)) $dk $P "cube"
  Box "${Tag}_EyeL" @(($Ox + $D * 0.95 * $S), ($Oy + 0.12 * $S), ($Oz + 47 * $S)) @((0.09 * $S), (0.09 * $S), (0.09 * $S)) $M.Eye $P "sphere"
  Box "${Tag}_EyeR" @(($Ox + $D * 0.95 * $S), ($Oy - 0.12 * $S), ($Oz + 47 * $S)) @((0.09 * $S), (0.09 * $S), (0.09 * $S)) $M.Eye $P "sphere"
  foreach ($lx in @(0.45, -0.45)) {
    foreach ($ly in @(0.28, -0.28)) {
      Box "${Tag}_Leg_${lx}_${ly}" @(($Ox + $D * $lx * $S), ($Oy + $ly * $S), ($Oz + 13 * $S)) @((0.22 * $S), (0.14 * $S), (0.26 * $S)) $sk $P "cube"
    }
  }
  foreach ($tx in @(1.25, 1.45)) {
    foreach ($s in @(1, -1)) {
      Box "${Tag}_Tooth_${tx}_${s}" @(($Ox + $D * $tx * $S), ($Oy + $s * 0.15 * $S), ($Oz + 22 * $S)) @((0.04 * $S), (0.04 * $S), (0.07 * $S)) $M.Tooth $P "cube"
    }
  }
}
Croc "Croc1" -2750 -1800 38 1 1.0
Croc "Croc2" -3050 -1900 73 -1 0.75
Write-Output "PHASE-C-OK"

# Fase C2 — corde transenne + coccodrilli (recupero: numeri sempre parentetizzati)
. "$PSScriptRoot\ue_common.ps1"

$F = "Hotel/CrocCage"
$M = @{
  Skin   = "/Game/Hotel/Materials/MI_CrocSkin.MI_CrocSkin"
  Dark   = "/Game/Hotel/Materials/MI_CrocDark.MI_CrocDark"
  Eye    = "/Game/Hotel/Materials/MI_CrocEye.MI_CrocEye"
  Tooth  = "/Game/Hotel/Materials/MI_Paper.MI_Paper"
  Carpet = "/Game/Hotel/Materials/MI_Carpet.MI_Carpet"
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
Rope "Croc_Rope_N1" (-3450) (-2260) (-2900) (-2260)
Rope "Croc_Rope_N2" (-2900) (-2260) (-2350) (-2260)
Rope "Croc_Rope_S1" (-3450) (-1440) (-2900) (-1440)
Rope "Croc_Rope_S2" (-2900) (-1440) (-2350) (-1440)
Rope "Croc_Rope_W" (-3450) (-2260) (-3450) (-1440)
Rope "Croc_Rope_E" (-2350) (-2260) (-2350) (-1440)

function Croc($Tag, $Ox, $Oy, $Oz, $D, $S) {
  $P = "Hotel/Crocs"
  $sk = $M.Skin; $dk = $M.Dark
  Box "${Tag}_Body" @($Ox, $Oy, ($Oz + 30 * $S)) @((1.5 * $S), (0.55 * $S), (0.35 * $S)) $sk $P "cube"
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
Croc "Croc1" (-2750) (-1800) 38 1 1.0
Croc "Croc2" (-3050) (-1900) 73 (-1) 0.75
Write-Output "PHASE-C2-OK"

# Fase B2 — ascensori, lampadari, carrello, poltrone, pianta, carte, calcinacci
. "$PSScriptRoot\ue_common.ps1"

$F = "Hotel/Furniture"
$M = @{
  Wood   = "/Game/Hotel/Materials/MI_WoodDark.MI_WoodDark"
  Steel  = "/Game/Hotel/Materials/MI_Steel.MI_Steel"
  Red    = "/Game/Hotel/Materials/MI_RedGlow.MI_RedGlow"
  Warm   = "/Game/Hotel/Materials/MI_WarmBulb.MI_WarmBulb"
  Carpet = "/Game/Hotel/Materials/MI_Carpet.MI_Carpet"
  Sheet  = "/Game/Hotel/Materials/MI_Sheet.MI_Sheet"
  Paper  = "/Game/Hotel/Materials/MI_Paper.MI_Paper"
  Wall   = "/Game/Hotel/Materials/MI_WallPlaster.MI_WallPlaster"
}

# ascensori parete ovest (x=-3500, fronte a -3470)
foreach ($z in @(-400, 0, 400)) {
  $glow = $M.Red
  if ($z -eq 0) { $glow = $M.Warm }
  Box "H_ElevFrame_$z" @(-3480, $z, 170) @(0.5, 3.0, 3.4) $M.Wood $F "cube"
  Box "H_ElevDoor_$z" @(-3470, $z, 145) @(0.25, 2.3, 2.9) $M.Steel $F "cube"
  Box "H_ElevInd_$z" @(-3465, $z, 315) @(0.15, 0.3, 0.12) $glow $F "cube"
}

# lampadari: stelo + bulbo (la luce vera arriva in fase D)
foreach ($p in @(@(0, 400), @(-2200, 400), @(2200, 400))) {
  Box "H_ChandStem_$($p[0])" @($p[0], $p[1], 775) @(0.08, 0.08, 2.5) $M.Wood $F "cylinder"
  Box "H_ChandBulb_$($p[0])" @($p[0], $p[1], 640) @(0.35, 0.35, 0.35) $M.Warm $F "sphere"
}

# carrello portabagagli vicino ingresso
Box "H_CartBase" @(600, 1700, 35) @(1.6, 1.0, 0.1) $M.Steel $F "cube"
foreach ($p in @(@(-0.7, -0.45), @(0.7, -0.45), @(-0.7, 0.45), @(0.7, 0.45))) {
  Box "H_CartPost_$($p[0])_$($p[1])" @((600 + $p[0] * 100), (1700 + $p[1] * 100), 80) @(0.06, 0.06, 0.9) $M.Steel $F "cylinder"
}
Box "H_Case1" @(565, 1705, 65) @(0.7, 0.5, 0.45) $M.Wood $F "cube"
Box "H_Case2" @(640, 1700, 60) @(0.6, 0.4, 0.4) $M.Wood $F "cube"

# poltrone ai lati del tavolino
Box "H_ArmW_Base" @(-1600, 900, 40) @(1.0, 1.0, 0.45) $M.Carpet $F "cube"
Box "H_ArmW_Back" @(-1650, 900, 85) @(0.28, 1.0, 0.85) $M.Carpet $F "cube"
Box "H_ArmE_Base" @(-800, 900, 40) @(1.0, 1.0, 0.45) $M.Carpet $F "cube"
Box "H_ArmE_Back" @(-750, 900, 85) @(0.28, 1.0, 0.85) $M.Carpet $F "cube"

# pianta morta: vaso + steli
Box "H_Pot" @(-1800, 1500, 25) @(0.7, 0.7, 0.5) $M.Wood $F "cylinder"
for ($i = 0; $i -lt 5; $i++) {
  Box "H_Stem_$i" @((-1800 + ($i - 2) * 12), (1500 + (($i * 37) % 3 - 1) * 10), 110) @(0.04, 0.04, 1.4) $M.Wood $F "cylinder"
}

# carte sparse + calcinacci
$paperPos = @(@(500, 1200), @(-300, 400), @(1200, -300), @(-900, 1500), @(200, -800), @(2500, 900), @(-2000, -500), @(800, 1800))
$pi = 0
foreach ($p in $paperPos) {
  Box "H_Paper_$pi" @($p[0], $p[1], 2) @(0.3, 0.4, 0.02) $M.Paper $F "cube"
  $pi++
}
$ci = 0
foreach ($p in @(@(-1500, -300), @(900, 600), @(3000, -800), @(-500, -1500), @(1500, 1200))) {
  $s = 0.25 + ($ci % 3) * 0.1
  Box "H_Chunk_$ci" @($p[0], $p[1], ($s * 50)) @($s, $s, ($s * 0.7)) $M.Wall $F "cube"
  $ci++
}
Write-Output "PHASE-B2-OK"

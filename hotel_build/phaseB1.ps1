# Fase B1 — guscio: pavimento, soffitto, muri, colonne, soppalco, scalone, reception
. "$PSScriptRoot\ue_common.ps1"

$F = "Hotel/Shell"
$M = @{
  Floor = "/Game/Hotel/Materials/MI_FloorMarble.MI_FloorMarble"
  Wall  = "/Game/Hotel/Materials/MI_Wallpaper.MI_Wallpaper"
  Ceil  = "/Game/Hotel/Materials/MI_Ceiling.MI_Ceiling"
  Wood  = "/Game/Hotel/Materials/MI_WoodDark.MI_WoodDark"
}

# pavimento + soffitto (top pavimento a z=0)
Box "H_Floor" @(0, 0, -25) @(70, 46, 0.5) $M.Floor $F "cube"
Box "H_Ceiling" @(0, 0, 925) @(70, 46, 0.5) $M.Ceil $F "cube"

# muri perimetrali h=10m
Box "H_Wall_N" @(0, -2330, 500) @(71.2, 0.6, 10) $M.Wall $F "cube"
Box "H_Wall_S" @(0, 2330, 500) @(71.2, 0.6, 10) $M.Wall $F "cube"
Box "H_Wall_W" @(-3530, 0, 500) @(0.6, 47.2, 10) $M.Wall $F "cube"
Box "H_Wall_E" @(3530, 0, 500) @(0.6, 47.2, 10) $M.Wall $F "cube"

# colonne marmo (6)
foreach ($cx in @(-1200, 1200)) {
  foreach ($cy in @(-800, 0, 800)) {
    Box "H_Column_${cx}_${cy}" @($cx, $cy, 500) @(1.3, 1.3, 10) $M.Floor $F "cube"
  }
}

# soppalco nord (top a z=420)
Box "H_Mezz" @(0, -1700, 400) @(70, 12, 0.4) $M.Wood $F "cube"
# ringhiere (gap centrale per lo scalone x -400..400)
Box "H_Rail_L" @(-1950, -1100, 475) @(31, 0.3, 1.1) $M.Wood $F "cube"
Box "H_Rail_R" @(1950, -1100, 475) @(31, 0.3, 1.1) $M.Wood $F "cube"

# scalone: 12 gradini (alzata 35cm, pedata 75cm), da y=-200 a y=-1100
for ($i = 0; $i -lt 12; $i++) {
  $top = 35 * ($i + 1)
  $cy = -200 - 75 * $i - 37.5
  Box "H_Step_$i" @(0, $cy, ($top / 2)) @(8, 0.75, ($top / 100)) $M.Floor $F "cube"
}

# reception est: bancone + top + ritorno
Box "H_DeskFront" @(2600, 200, 55) @(8, 1.2, 1.1) $M.Wood $F "cube"
Box "H_DeskTop" @(2600, 200, 114) @(8.4, 1.5, 0.08) $M.Floor $F "cube"
Box "H_DeskSide" @(2150, 0, 55) @(1.2, 4, 1.1) $M.Wood $F "cube"

# salotto ovest: 2 divani + tavolino
Box "H_SofaA_Base" @(-1200, 600, 45) @(3, 1.05, 0.5) $M.Wall $F "cube"
Box "H_SofaA_Back" @(-1200, 552, 90) @(3, 0.28, 0.9) $M.Wall $F "cube"
Box "H_SofaB_Base" @(-1200, 1200, 45) @(3, 1.05, 0.5) $M.Wall $F "cube"
Box "H_SofaB_Back" @(-1200, 1248, 90) @(3, 0.28, 0.9) $M.Wall $F "cube"
Box "H_CoffeeTable" @(-1200, 900, 50) @(2.4, 1.2, 0.12) $M.Wood $F "cube"

# pulizia attore di test
UeScene "remove_from_scene" @{actor = @{refPath = "/Game/Maps/HotelPryingEyes.HotelPryingEyes:PersistentLevel.StaticMeshActor_1" } } | Out-Null
Write-Output "PHASE-B1-OK"

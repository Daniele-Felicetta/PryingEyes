# Fase A2 — istanze materiale per ogni finitura dell'hotel
. "$PSScriptRoot\ue_common.ps1"

UeAsset "delete" @{path = "/Game/Hotel/Materials/MI_TestInst" } | Out-Null

function NewMI($Name, $C, $E, $Metal, $Rough) {
    $i = UeMI "create" @{folder_path = "/Game/Hotel/Materials"; asset_name = $Name; parent = (Ref "/Game/Hotel/Materials/M_HotelMaster.M_HotelMaster") }
    $ref = $i.refPath
    UeMI "set_vector_parameter" @{instance = (Ref $ref); name = "Color"; value = @{r = $C[0]; g = $C[1]; b = $C[2]; a = 1.0 } } | Out-Null
    UeMI "set_vector_parameter" @{instance = (Ref $ref); name = "Emissive"; value = @{r = $E[0]; g = $E[1]; b = $E[2]; a = 1.0 } } | Out-Null
    UeMI "set_vector_parameter" @{instance = (Ref $ref); name = "PBR"; value = @{r = $Metal; g = $Rough; b = 0.0; a = 1.0 } } | Out-Null
    Write-Output "MI: $Name"
}
$B = @(0.0, 0.0, 0.0)
NewMI "MI_FloorMarble" @(0.72, 0.70, 0.64) $B 0.05 0.30
NewMI "MI_WallPlaster" @(0.52, 0.47, 0.40) $B 0.0 0.90
NewMI "MI_Wallpaper" @(0.30, 0.10, 0.12) $B 0.0 0.95
NewMI "MI_Ceiling" @(0.07, 0.07, 0.09) $B 0.0 0.90
NewMI "MI_WoodDark" @(0.15, 0.09, 0.05) $B 0.0 0.70
NewMI "MI_Gold" @(0.55, 0.38, 0.15) $B 1.0 0.35
NewMI "MI_GlassFake" @(0.55, 0.72, 0.78) $B 0.9 0.12
NewMI "MI_WaterFake" @(0.03, 0.11, 0.10) $B 0.85 0.10
NewMI "MI_CrocSkin" @(0.20, 0.21, 0.10) $B 0.0 0.65
NewMI "MI_CrocDark" @(0.10, 0.10, 0.06) $B 0.0 0.85
NewMI "MI_CrocEye" @(0.10, 0.08, 0.02) @(0.9, 0.6, 0.1) 0.0 0.30
NewMI "MI_RedGlow" @(0.15, 0.01, 0.01) @(4.0, 0.12, 0.10) 0.0 0.50
NewMI "MI_WarmBulb" @(0.25, 0.16, 0.08) @(3.0, 1.8, 0.9) 0.0 0.50
NewMI "MI_Carpet" @(0.30, 0.04, 0.07) $B 0.0 1.00
NewMI "MI_Sheet" @(0.60, 0.58, 0.53) $B 0.0 1.00
NewMI "MI_Steel" @(0.40, 0.42, 0.44) $B 0.9 0.40
NewMI "MI_Paper" @(0.68, 0.66, 0.58) $B 0.0 1.00
Write-Output "PHASE-A2-OK"


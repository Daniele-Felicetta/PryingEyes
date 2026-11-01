# Fase F2 — coccodrilli puliti + vetro traslucido + salvataggio totale
. "$PSScriptRoot\ue_common.ps1"

# ---------- coccodrilli (coordinate parentetizzate) ----------
function Croc($Tag, $Ox, $Oy, $Oz, $D, $S) {
  $P = "Hotel/Crocs"
  $sk = "/Game/Hotel/Materials/MI_CrocSkin.MI_CrocSkin"
  $dk = "/Game/Hotel/Materials/MI_CrocDark.MI_CrocDark"
  $ey = "/Game/Hotel/Materials/MI_CrocEye.MI_CrocEye"
  $th = "/Game/Hotel/Materials/MI_Paper.MI_Paper"
  Box "${Tag}_Body" @($Ox, $Oy, ($Oz + 30 * $S)) @((1.5 * $S), (0.55 * $S), (0.35 * $S)) $sk $P "cube"
  Box "${Tag}_Tail1" @(($Ox + $D * -1.0 * $S), $Oy, ($Oz + 28 * $S)) @((0.5 * $S), (0.34 * $S), (0.22 * $S)) $sk $P "cube"
  Box "${Tag}_Tail2" @(($Ox + $D * -1.45 * $S), $Oy, ($Oz + 28 * $S)) @((0.45 * $S), (0.24 * $S), (0.16 * $S)) $sk $P "cube"
  Box "${Tag}_Tail3" @(($Ox + $D * -1.85 * $S), $Oy, ($Oz + 28 * $S)) @((0.4 * $S), (0.14 * $S), (0.1 * $S)) $sk $P "cube"
  Box "${Tag}_Head" @(($Ox + $D * 0.95 * $S), $Oy, ($Oz + 32 * $S)) @((0.45 * $S), (0.4 * $S), (0.24 * $S)) $sk $P "cube"
  Box "${Tag}_Snout" @(($Ox + $D * 1.4 * $S), $Oy, ($Oz + 28 * $S)) @((0.5 * $S), (0.3 * $S), (0.15 * $S)) $sk $P "cube"
  Box "${Tag}_Jaw" @(($Ox + $D * 1.42 * $S), $Oy, ($Oz + 19 * $S)) @((0.55 * $S), (0.27 * $S), (0.07 * $S)) $dk $P "cube"
  Box "${Tag}_EyeL" @(($Ox + $D * 0.95 * $S), ($Oy + 0.12 * $S), ($Oz + 47 * $S)) @((0.09 * $S), (0.09 * $S), (0.09 * $S)) $ey $P "sphere"
  Box "${Tag}_EyeR" @(($Ox + $D * 0.95 * $S), ($Oy - 0.12 * $S), ($Oz + 47 * $S)) @((0.09 * $S), (0.09 * $S), (0.09 * $S)) $ey $P "sphere"
  foreach ($lx in @(0.45, -0.45)) {
    foreach ($ly in @(0.28, -0.28)) {
      Box "${Tag}_Leg" @((($Ox + $D * $lx * $S)), ($Oy + $ly * $S), ($Oz + 13 * $S)) @((0.22 * $S), (0.14 * $S), (0.26 * $S)) $sk $P "cube"
    }
  }
  foreach ($tx in @(1.25, 1.45)) {
    foreach ($sd in @(1, -1)) {
      Box "${Tag}_Tooth" @(($Ox + $D * $tx * $S), ($Oy + $sd * 0.15 * $S), ($Oz + 22 * $S)) @((0.04 * $S), (0.04 * $S), (0.07 * $S)) $th $P "cube"
    }
  }
}
# NOTA: nomi senza suffissi numerici (niente piu' duplicati ambigui)
Croc "CrocA" (-2750) (-1800) 38 1 1.0
Croc "CrocB" (-3050) (-1900) 73 (-1) 0.75
Write-Output "CROCS-OK"

# ---------- vetro vero ----------
$m = UeMat "create_material" @{folder_path = "/Game/Hotel/Materials"; asset_name = "M_GlassMaster" }
$gm = $m.refPath
UeObj "set_properties" @{instance = @{refPath = $gm }; values = (@{BlendMode = "BLEND_Translucent" } | ConvertTo-Json -Compress) } | Out-Null
$vC = UeMat "add_expression" @{material_or_function = @{refPath = $gm }; expression_class = @{refPath = "/Script/Engine.MaterialExpressionVectorParameter" } }
$vP = UeMat "add_expression" @{material_or_function = @{refPath = $gm }; expression_class = @{refPath = "/Script/Engine.MaterialExpressionVectorParameter" } }
UeMat "connect_to_output" @{expression = @{refPath = $vC.refPath }; output_name = "RGB"; material_property = "MP_BaseColor" } | Out-Null
UeMat "connect_to_output" @{expression = @{refPath = $vP.refPath }; output_name = "R"; material_property = "MP_Metallic" } | Out-Null
UeMat "connect_to_output" @{expression = @{refPath = $vP.refPath }; output_name = "G"; material_property = "MP_Roughness" } | Out-Null
UeMat "connect_to_output" @{expression = @{refPath = $vP.refPath }; output_name = "B"; material_property = "MP_Opacity" } | Out-Null
function SV2($R, $H) { UeObj "set_properties" @{instance = @{refPath = $R }; values = ($H | ConvertTo-Json -Compress -Depth 5) } | Out-Null }
SV2 $vC.refPath @{ParameterName = "Color"; DefaultValue = @{R = 0.6; G = 0.72; B = 0.75; A = 1.0 } }
SV2 $vP.refPath @{ParameterName = "PBR"; DefaultValue = @{R = 0.0; G = 0.08; B = 0.14; A = 1.0 } }
UeMat "recompile" @{material_or_function = @{refPath = $gm } } | Out-Null
$gi = UeMI "create" @{folder_path = "/Game/Hotel/Materials"; asset_name = "MI_GlassReal"; parent = @{refPath = $gm } }
Write-Output "GLASS-MASTER-OK"

$glassMat = "/Game/Hotel/Materials/MI_GlassReal.MI_GlassReal"
foreach ($n in @("Croc_Glass_N", "Croc_Glass_S", "Croc_Glass_W", "Croc_Glass_E")) {
  $a = UeScene "find_actors" @{name = $n; tag = ""; collision_channels = @() }
  foreach ($act in $a) {
    UeObj "set_properties" @{instance = @{refPath = ($act.refPath + ".Prim") }; values = (@{overrideMaterials = @(@{refPath = $glassMat }) } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
  }
}
Write-Output "GLASS-ASSIGNED"

# ---------- salvataggio totale: mappa + tutti i materiali ----------
UeAsset "save_assets" @{asset_paths = @("/Game/Maps/HotelPryingEyes") } | Out-Null
$mats = UeAsset "find_assets" @{folder_path = "/Game/Hotel/Materials"; name = "" }
UeAsset "save_assets" @{asset_paths = $mats } | Out-Null
Write-Output "PHASE-F2-OK SAVED-ALL"

# Fase A1 — master material M_HotelMaster (Color/Metallic/Roughness/Emissive)
. "$PSScriptRoot\ue_common.ps1"

$m = UeMat "create_material" @{folder_path = "/Game/Hotel/Materials"; asset_name = "M_HotelMaster" }
$matRef = $m.refPath
Write-Output "MASTER: $matRef"

$vColor = UeMat "add_expression" @{material_or_function = (Ref $matRef); expression_class = (Ref "/Script/Engine.MaterialExpressionVectorParameter") }
$vEmis = UeMat "add_expression" @{material_or_function = (Ref $matRef); expression_class = (Ref "/Script/Engine.MaterialExpressionVectorParameter") }
$sMetal = UeMat "add_expression" @{material_or_function = (Ref $matRef); expression_class = (Ref "/Script/Engine.MaterialExpressionScalarParameter") }
$sRough = UeMat "add_expression" @{material_or_function = (Ref $matRef); expression_class = (Ref "/Script/Engine.MaterialExpressionScalarParameter") }

UeMat "connect_to_output" @{expression = (Ref $vColor.refPath); output_name = "RGB"; material_property = "MP_BaseColor" } | Out-Null
UeMat "connect_to_output" @{expression = (Ref $vEmis.refPath); output_name = "RGB"; material_property = "MP_EmissiveColor" } | Out-Null
UeMat "connect_to_output" @{expression = (Ref $sMetal.refPath); output_name = "R"; material_property = "MP_Metallic" } | Out-Null
UeMat "connect_to_output" @{expression = (Ref $sRough.refPath); output_name = "R"; material_property = "MP_Roughness" } | Out-Null

function SetVals($ref, $h) {
    UeObj "set_properties" @{instance = (Ref $ref); values = ($h | ConvertTo-Json -Compress -Depth 5) } | Out-Null
}
SetVals $vColor.refPath @{ParameterName = "Color"; DefaultValue = @{r = 0.8; g = 0.8; b = 0.8; a = 1.0 } }
SetVals $vEmis.refPath @{ParameterName = "Emissive"; DefaultValue = @{r = 0.0; g = 0.0; b = 0.0; a = 1.0 } }
SetVals $sMetal.refPath @{ParameterName = "Metallic"; DefaultValue = 0.0 }
SetVals $sRough.refPath @{ParameterName = "Roughness"; DefaultValue = 0.9 }
UeMat "recompile" @{material_or_function = (Ref $matRef) } | Out-Null
Write-Output "MASTER OK"

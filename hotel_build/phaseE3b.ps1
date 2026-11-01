# Fase E3b — grunge procedurale nel master material (noise -> lerp tinta)
. "$PSScriptRoot\ue_common.ps1"
$mat = "/Game/Hotel/Materials/M_HotelMaster.M_HotelMaster"

$noise = UeMat "add_expression" @{material_or_function = @{refPath = $mat }; expression_class = @{refPath = "/Script/Engine.MaterialExpressionVectorNoise" } }
$lerp = UeMat "add_expression" @{material_or_function = @{refPath = $mat }; expression_class = @{refPath = "/Script/Engine.MaterialExpressionLinearInterpolate" } }
$tint = UeMat "add_expression" @{material_or_function = @{refPath = $mat }; expression_class = @{refPath = "/Script/Engine.MaterialExpressionVectorParameter" } }
Write-Output "NODES-OK"

UeMat "connect_expressions" @{from_expression = @{refPath = "/Game/Hotel/Materials/M_HotelMaster.M_HotelMaster:MaterialExpressionVectorParameter_0" }; from_output_name = "RGB"; to_expression = @{refPath = $lerp.refPath }; to_input_name = "A" } | Out-Null
UeMat "connect_expressions" @{from_expression = @{refPath = $tint.refPath }; from_output_name = "RGB"; to_expression = @{refPath = $lerp.refPath }; to_input_name = "B" } | Out-Null
UeMat "connect_expressions" @{from_expression = @{refPath = $noise.refPath }; from_output_name = "R"; to_expression = @{refPath = $lerp.refPath }; to_input_name = "Alpha" } | Out-Null
Write-Output "CHAIN-OK"

UeObj "set_properties" @{instance = @{refPath = $tint.refPath }; values = (@{ParameterName = "GrungeTint"; DefaultValue = @{R = 0.45; G = 0.42; B = 0.4; A = 1.0 } } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
UeMat "disconnect_from_output" @{material = @{refPath = $mat }; material_property = "MP_BaseColor" } | Out-Null
UeMat "connect_to_output" @{expression = @{refPath = $lerp.refPath }; output_name = "RGB"; material_property = "MP_BaseColor" } | Out-Null
UeMat "recompile" @{material_or_function = @{refPath = $mat } } | Out-Null
Write-Output "GRUNGE-OK"
UeAsset "save_assets" @{asset_paths = @("/Game/Hotel/Materials/M_HotelMaster") } | Out-Null
Write-Output SAVED

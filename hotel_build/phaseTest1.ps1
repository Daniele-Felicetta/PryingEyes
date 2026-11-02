# Sandbox PIE test: piazza l'attore di test e avvia il gioco
. "$PSScriptRoot\ue_common.ps1"
Ue "EditorToolset.EditorAppToolset" "StopPIE" @{} | Out-Null
Write-Output "PIE-STOPPED"
$a = UeScene "add_to_scene_from_asset" @{asset_path = "/Game/Hotel/Tests/BP_Sandbox"; name = "TEST_Sandbox"; xform = @{location = @{x = 0.0; y = 1500.0; z = 100.0 } } }
Write-Output ("PLACED: " + $a.refPath)
UeAsset "save_assets" @{asset_paths = @("/Game/Maps/HotelPryingEyes", "/Game/Hotel/Tests/BP_Sandbox") } | Out-Null
Write-Output SAVED

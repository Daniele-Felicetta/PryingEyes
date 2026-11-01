# Fase D — luci, luna fioca, PlayerStart, salvataggio livello
. "$PSScriptRoot\ue_common.ps1"

function Light($Name, $Type, $X, $Y, $Z, $R, $G, $B, $Intensity) {
  $a = UeScene "add_to_scene_from_class" @{actor_type = @{refPath = "/Script/Engine.$Type" }; name = $Name; xform = @{location = @{x = [double]$X; y = [double]$Y; z = [double]$Z } } }
  $aref = $a.refPath
  $comps = UeActor "get_components" @{actor = @{refPath = $aref } }
  $lc = ($comps | Where-Object { $_.refPath -match 'LightComponent' } | Select-Object -First 1).refPath
  UeObj "set_properties" @{instance = @{refPath = $lc }; values = (@{Intensity = [double]$Intensity; LightColor = @{r = [double]$R; g = [double]$G; b = [double]$B; a = 1.0 } } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
  UeActor "set_label" @{actor = @{refPath = $aref }; label = $Name } | Out-Null
  UeScene "set_actor_folder" @{actor = @{refPath = $aref }; folder_path = "Hotel/Lights" } | Out-Null
  Write-Output "Light: $Name"
  return $aref
}

# luna fioca (direzionale esistente del template)
UeObj "set_properties" @{instance = @{refPath = "/Game/Maps/HotelPryingEyes.HotelPryingEyes:PersistentLevel.DirectionalLight_0.LightComponent0" }; values = (@{Intensity = 0.35; LightColor = @{r = 0.62; g = 0.72; b = 0.9; a = 1.0 } } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
Write-Output "Moon dimmed"

Light "L_Chandelier_C" "PointLight" 0 400 620 1.0 0.72 0.42 2500
Light "L_Chandelier_W1" "PointLight" -2200 400 620 1.0 0.72 0.42 1200
Light "L_Chandelier_W2" "PointLight" 2200 400 620 1.0 0.72 0.42 1200
Light "L_Emerg_Entry" "PointLight" 0 2100 340 1.0 0.08 0.08 400
Light "L_Emerg_Elev" "PointLight" -3300 0 340 1.0 0.08 0.08 400
Light "L_HeatLamp" "PointLight" -2450 -1600 280 1.0 0.3 0.1 250

# PlayerStart ingresso sud, rivolto a nord (-Y)
$a = UeScene "add_to_scene_from_class" @{actor_type = @{refPath = "/Script/Engine.PlayerStart" }; name = "P_Start"; xform = @{location = @{x = 0.0; y = 1900.0; z = 0.0 }; rotation = @{pitch = 0.0; yaw = -90.0; roll = 0.0 } } }
UeScene "set_actor_folder" @{actor = @{refPath = $a.refPath }; folder_path = "Hotel" } | Out-Null
Write-Output "PlayerStart OK"

# GameMode FPS (best effort: se fallisce, si imposta a mano nel progetto)
try {
  UeObj "set_properties" @{instance = @{refPath = "/Game/Maps/HotelPryingEyes.HotelPryingEyes" }; values = (@{DefaultGameMode = @{refPath = "/Game/FirstPerson/Blueprints/BP_FirstPersonGameMode.BP_FirstPersonGameMode_C" } } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
  Write-Output "GameMode OK"
}
catch {
  Write-Output "GameMode SKIP: $($_.Exception.Message)"
}

UeAsset "save_assets" @{asset_paths = @("/Game/Maps/HotelPryingEyes") } | Out-Null
Write-Output "PHASE-D-OK SAVED"

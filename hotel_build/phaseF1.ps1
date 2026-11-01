# Fase F1 — azzera luci e coccodrilli, ricrea TUTTE le luci in un colpo solo
. "$PSScriptRoot\ue_common.ps1"

foreach ($fld in @("Hotel/Lights", "Hotel/Crocs")) {
  $acts = UeScene "get_actors_in_folder" @{folder_path = $fld; recursive = $true }
  foreach ($a in $acts) {
    UeScene "remove_from_scene" @{actor = $a } | Out-Null
  }
  Write-Output "WIPED $fld"
}

function PtLight($Name, $X, $Y, $Z, $R, $G, $B, $I) {
  $a = UeScene "add_to_scene_from_class" @{actor_type = @{refPath = "/Script/Engine.PointLight" }; name = $Name; xform = @{location = @{x = [double]$X; y = [double]$Y; z = [double]$Z } } }
  $lc = ((UeActor "get_components" @{actor = @{refPath = $a.refPath } }) | Where-Object { $_.refPath -match 'LightComponent' } | Select-Object -First 1).refPath
  UeObj "set_properties" @{instance = @{refPath = $lc }; values = (@{Intensity = [double]$I; LightColor = @{r = [double]$R; g = [double]$G; b = [double]$B; a = 1.0 } } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
  UeActor "set_label" @{actor = @{refPath = $a.refPath }; label = $Name } | Out-Null
  UeScene "set_actor_folder" @{actor = @{refPath = $a.refPath }; folder_path = "Hotel/Lights" } | Out-Null
}
PtLight "L_Chandelier_C" 0 400 620 1.0 0.72 0.42 2500
PtLight "L_Chandelier_W1" -2200 400 620 1.0 0.72 0.42 1200
PtLight "L_Chandelier_W2" 2200 400 620 1.0 0.72 0.42 1200
PtLight "L_Emerg_Entry" 0 2150 420 1.0 0.08 0.08 120
PtLight "L_Emerg_Elev" -3300 0 340 1.0 0.08 0.08 400
PtLight "L_HeatLamp" -2450 -1600 280 1.0 0.3 0.1 120
Write-Output "POINTS-OK"

function SpotL($Name, $X, $Y, $Z, $R, $G, $B, $I, $Inner, $Outer) {
  $a = UeScene "add_to_scene_from_class" @{actor_type = @{refPath = "/Script/Engine.SpotLight" }; name = $Name; xform = @{location = @{x = [double]$X; y = [double]$Y; z = [double]$Z } } }
  $lc = ((UeActor "get_components" @{actor = @{refPath = $a.refPath } }) | Where-Object { $_.refPath -match 'LightComponent' } | Select-Object -First 1).refPath
  UeObj "set_properties" @{instance = @{refPath = $lc }; values = (@{Intensity = [double]$I; LightColor = @{r = [double]$R; g = [double]$G; b = [double]$B; a = 1.0 }; InnerConeAngle = [double]$Inner; OuterConeAngle = [double]$Outer } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
  UeActor "set_label" @{actor = @{refPath = $a.refPath }; label = $Name } | Out-Null
  UeScene "set_actor_folder" @{actor = @{refPath = $a.refPath }; folder_path = "Hotel/Lights" } | Out-Null
}
SpotL "L_MoonShaft" 0 -600 1400 0.75 0.85 1.0 22000 18 32
SpotL "L_WinPool1" 3100 -600 600 0.55 0.68 0.9 6000 25 45
SpotL "L_WinPool2" 3100 300 600 0.55 0.68 0.9 6000 25 45
Write-Output "SPOTS-OK"

function RectL($Name, $X, $Y, $Z) {
  $a = UeScene "add_to_scene_from_class" @{actor_type = @{refPath = "/Script/Engine.RectLight" }; name = $Name; xform = @{location = @{x = [double]$X; y = [double]$Y; z = [double]$Z }; rotation = @{pitch = 0.0; yaw = 180.0; roll = 0.0 } } }
  $lc = ((UeActor "get_components" @{actor = @{refPath = $a.refPath } }) | Where-Object { $_.refPath -match 'LightComponent' } | Select-Object -First 1).refPath
  UeObj "set_properties" @{instance = @{refPath = $lc }; values = (@{Intensity = 900.0; LightColor = @{r = 0.6; g = 0.72; b = 0.92; a = 1.0 }; SourceWidth = 300.0; SourceHeight = 500.0 } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
  UeActor "set_label" @{actor = @{refPath = $a.refPath }; label = $Name } | Out-Null
  UeScene "set_actor_folder" @{actor = @{refPath = $a.refPath }; folder_path = "Hotel/Lights" } | Out-Null
}
RectL "L_WinRect1" 3350 -600 450
RectL "L_WinRect2" 3350 300 450
Write-Output "RECTS-OK"

# luna fioca
UeObj "set_properties" @{instance = @{refPath = "/Game/Maps/HotelPryingEyes.HotelPryingEyes:PersistentLevel.DirectionalLight_0.LightComponent0" }; values = (@{Intensity = 0.35; LightColor = @{r = 0.62; g = 0.72; b = 0.92; a = 1.0 } } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
Write-Output "MOON-OK"
Write-Output "PHASE-F1-OK"

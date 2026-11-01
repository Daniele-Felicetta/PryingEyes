# Fase E2 — apertura lucernario, shaft di luna, luci finestre (RectLight), volumetric setup
. "$PSScriptRoot\ue_common.ps1"

$M = @{
  Ceil = "/Game/Hotel/Materials/MI_Ceiling.MI_Ceiling"
}

# rimuovi soffitto pieno
$found = UeScene "find_actors" @{name = "H_Ceiling"; tag = ""; collision_channels = @() }
foreach ($a in $found) {
  UeScene "remove_from_scene" @{actor = $a } | Out-Null
  Write-Output "Removed ceiling"
}

# 4 lastre attorno all'apertura X -400..400, Y -900..-300
function Slab($n, $x, $y, $sx, $sy) {
  $a = UeScene "add_to_scene_from_class" @{actor_type = @{refPath = "/Script/Engine.StaticMeshActor" }; name = $n; xform = @{location = @{x = [double]$x; y = [double]$y; z = 925.0 }; scale = @{x = [double]$sx; y = [double]$sy; z = 0.5 } } }
  $c = Ue "editor_toolset.toolsets.primitive.PrimitiveTools" "add_cube" @{actor = @{refPath = $a.refPath }; name = "Prim" }
  UeObj "set_properties" @{instance = $c; values = (@{overrideMaterials = @(@{refPath = $M.Ceil }) } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
  UeActor "set_label" @{actor = $a; label = $n } | Out-Null
  UeScene "set_actor_folder" @{actor = $a; folder_path = "Hotel/Shell" } | Out-Null
}
Slab "H_Slab_S" 0 1000 70 26
Slab "H_Slab_N" 0 -1600 70 14
Slab "H_Slab_W" -1950 -600 31 6
Slab "H_Slab_E" 1950 -600 31 6
Write-Output "Slabs OK"

# SpotLight lama di luna dal lucernario (punta in basso di default)
function Spot($Name, $X, $Y, $Z, $R, $G, $B, $I, $Inner, $Outer) {
  $a = UeScene "add_to_scene_from_class" @{actor_type = @{refPath = "/Script/Engine.SpotLight" }; name = $Name; xform = @{location = @{x = [double]$X; y = [double]$Y; z = [double]$Z } } }
  $comps = UeActor "get_components" @{actor = @{refPath = $a.refPath } }
  $lc = ($comps | Where-Object { $_.refPath -match 'LightComponent' } | Select-Object -First 1).refPath
  UeObj "set_properties" @{instance = @{refPath = $lc }; values = (@{Intensity = [double]$I; LightColor = @{r = [double]$R; g = [double]$G; b = [double]$B; a = 1.0 }; InnerConeAngle = [double]$Inner; OuterConeAngle = [double]$Outer } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
  UeActor "set_label" @{actor = @{refPath = $a.refPath }; label = $Name } | Out-Null
  UeScene "set_actor_folder" @{actor = @{refPath = $a.refPath }; folder_path = "Hotel/Lights" } | Out-Null
  Write-Output "Spot: $Name"
}
Spot "L_MoonShaft" 0 -600 1400 0.75 0.85 1.0 22000 18 32
Spot "L_WinPool1" 3100 -600 600 0.55 0.68 0.9 6000 25 45
Spot "L_WinPool2" 3100 300 600 0.55 0.68 0.9 6000 25 45

# RectLight alle vetrate (wash morbido verso ovest)
function RectL($Name, $X, $Y, $Z, $W, $Hh) {
  $a = UeScene "add_to_scene_from_class" @{actor_type = @{refPath = "/Script/Engine.RectLight" }; name = $Name; xform = @{location = @{x = [double]$X; y = [double]$Y; z = [double]$Z }; rotation = @{pitch = 0.0; yaw = 180.0; roll = 0.0 } } }
  $comps = UeActor "get_components" @{actor = @{refPath = $a.refPath } }
  $lc = ($comps | Where-Object { $_.refPath -match 'LightComponent' } | Select-Object -First 1).refPath
  UeObj "set_properties" @{instance = @{refPath = $lc }; values = (@{Intensity = 900.0; LightColor = @{r = 0.6; g = 0.72; b = 0.92; a = 1.0 }; SourceWidth = [double]$W; SourceHeight = [double]$Hh } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
  UeActor "set_label" @{actor = @{refPath = $a.refPath }; label = $Name } | Out-Null
  UeScene "set_actor_folder" @{actor = @{refPath = $a.refPath }; folder_path = "Hotel/Lights" } | Out-Null
  Write-Output "Rect: $Name"
}
RectL "L_WinRect1" 3350 -600 450 300 500
RectL "L_WinRect2" 3350 300 450 300 500
Write-Output "PHASE-E2-OK"

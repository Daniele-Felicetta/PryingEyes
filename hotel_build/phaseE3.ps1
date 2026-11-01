# Fase E3 — fog, volumetric, postprocess cinematografico, dust Niagara
. "$PSScriptRoot\ue_common.ps1"

# --- fog esistente: alza densita' ---
$comps = UeActor "get_components" @{actor = @{refPath = "/Game/Maps/HotelPryingEyes.HotelPryingEyes:PersistentLevel.ExponentialHeightFog_0" } }
$fogc = ($comps | Where-Object { $_.refPath -match 'Component' } | Select-Object -First 1).refPath
Write-Output "FogComp: $fogc"
UeObj "set_properties" @{instance = @{refPath = $fogc }; values = (@{FogDensity = 0.035 } | ConvertTo-Json -Compress) } | Out-Null
Write-Output "FogDensity OK"

# --- PostProcessVolume globale ---
$pp = UeScene "add_to_scene_from_class" @{actor_type = @{refPath = "/Script/Engine.PostProcessVolume" }; name = "PP_Global"; xform = @{location = @{x = 0.0; y = 0.0; z = 400.0 }; scale = @{x = 200.0; y = 200.0; z = 50.0 } } }
$ppref = $pp.refPath
UeObj "set_properties" @{instance = @{refPath = $ppref }; values = (@{bUnbound = $true } | ConvertTo-Json -Compress) } | Out-Null
$set = @{
  bOverride_BloomIntensity   = $true; BloomIntensity = 1.35
  bOverride_BloomThreshold   = $true; BloomThreshold = 0.85
  bOverride_AutoExposureBias = $true; AutoExposureBias = 0.4
  bOverride_VignetteIntensity = $true; VignetteIntensity = 0.45
  bOverride_FilmGrainIntensity = $true; FilmGrainIntensity = 0.35
  bOverride_SceneFringeIntensity = $true; SceneFringeIntensity = 0.6
  bOverride_ColorSaturation  = $true; ColorSaturation = @{x = 1.06; y = 1.06; z = 1.1; w = 1.0 }
  bOverride_ColorContrast    = $true; ColorContrast = @{x = 1.04; y = 1.04; z = 1.04; w = 1.0 }
  bOverride_ColorGamma       = $true; ColorGamma = @{x = 0.96; y = 0.98; z = 1.0; w = 1.0 }
}
UeObj "set_properties" @{instance = @{refPath = $ppref }; values = ($set | ConvertTo-Json -Compress -Depth 5) } | Out-Null
Write-Output "PostProcess OK"

# --- dust motes: istanzia il sistema Niagara demo in 2 punti ---
foreach ($p in @(@(0, -400, 500), @(-2600, -1800, 300))) {
  $n = UeScene "add_to_scene_from_asset" @{asset_path = "/Game/DemoTemplate/Niagara/NS_DemoNiagara"; name = "FX_Dust"; xform = @{location = @{x = [double]$p[0]; y = [double]$p[1]; z = [double]$p[2] } } }
  UeActor "set_label" @{actor = $n; label = "FX_Dust" } | Out-Null
  UeScene "set_actor_folder" @{actor = $n; folder_path = "Hotel/FX" } | Out-Null
  Write-Output ("Niagara: " + $n.refPath)
}

UeAsset "save_assets" @{asset_paths = @("/Game/Maps/HotelPryingEyes") } | Out-Null
Write-Output "PHASE-E3-OK SAVED"

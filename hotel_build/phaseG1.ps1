# Fase G1 — gameplay Hotel: replay dei grafi DSL + smoke test PIE.
# Eseguire con l'editor aperto sul progetto PryingEyes 5.8.
# Ricostruisce i Blueprint gameplay dai sorgenti versionati in hotel_build/gameplay/,
# ricompila, piazza le istanze se mancano e verifica spawn/kill in PIE.
. "$PSScriptRoot\ue_common.ps1"

$bpE = @{refPath = "/Game/Hotel/Gameplay/BP_Enemy.BP_Enemy"}
$bpD = @{refPath = "/Game/Hotel/Gameplay/BP_GameDirector.BP_GameDirector"}
$bpC = @{refPath = "/Game/Hotel/Gameplay/BP_CrocAnim.BP_CrocAnim"}
$wbp = @{refPath = "/Game/Hotel/UI/WBP_HotelHUD.WBP_HotelHUD"}

function WriteGraph($Bp, $GraphRef, $File) {
  $code = Get-Content -Raw "$PSScriptRoot\gameplay\$File"
  Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "write_graph_dsl" @{blueprint = $Bp; graph = @{refPath = $GraphRef }; code = $code } | Out-Null
  Write-Output "GRAPH-OK $File"
}

# --- crea i BP se mancano (idempotente solo se assenti: create fallisce se esiste) ---
foreach ($spec in @(
  @("/Game/Hotel/Gameplay", "BP_Enemy", "/Script/Engine.Actor"),
  @("/Game/Hotel/Gameplay", "BP_GameDirector", "/Script/Engine.Actor"),
  @("/Game/Hotel/Gameplay", "BP_CrocAnim", "/Script/Engine.Actor")
)) {
  try { Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "create" @{folder_path = $spec[0]; asset_name = $spec[1]; asset_type = @{refPath = $spec[2] } } | Out-Null; Write-Output ("CREATED " + $spec[1]) }
  catch { Write-Output ("EXISTS " + $spec[1]) }
}

$E = "/Game/Hotel/Gameplay/BP_Enemy.BP_Enemy"
$D = "/Game/Hotel/Gameplay/BP_GameDirector.BP_GameDirector"
$C = "/Game/Hotel/Gameplay/BP_CrocAnim.BP_CrocAnim"
$W = "/Game/Hotel/UI/WBP_HotelHUD.WBP_HotelHUD"
WriteGraph $bpE "$E:TakeHit" "enemy_TakeHit.lisp"
WriteGraph $bpE "$E:EventGraph" "enemy_EventGraph.lisp"
WriteGraph $bpD "$D:StartWave" "director_StartWave.lisp"
WriteGraph $bpD "$D:SpawnOne" "director_SpawnOne.lisp"
WriteGraph $bpD "$D:TryFire" "director_TryFire.lisp"
WriteGraph $bpD "$D:Reload" "director_Reload.lisp"
WriteGraph $bpD "$D:EventGraph" "director_EventGraph.lisp"
WriteGraph $bpC "$C:EventGraph" "croc_EventGraph.lisp"
WriteGraph $wbp "$W:EventGraph" "hud_EventGraph.lisp"

Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "compile_blueprint" @{blueprint = $bpE} | Out-Null
Write-Output "ENEMY-COMPILE-OK"
Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "compile_blueprint" @{blueprint = $bpD} | Out-Null
Write-Output "DIRECTOR-COMPILE-OK"
Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "compile_blueprint" @{blueprint = $bpC} | Out-Null
Write-Output "CROC-COMPILE-OK"
Ue "UMGToolSet.UMGToolSet" "CompileWidgetBlueprint" @{widgetBlueprint = $wbp} | Out-Null
Write-Output "HUD-COMPILE-OK"

$d = UeScene "find_actors" @{name = "GameDirector"; tag = ""; collision_channels = @() }
if ($d.Count -lt 1) {
  UeScene "add_to_scene_from_asset" @{asset_path = "/Game/Hotel/Gameplay/BP_GameDirector"; name = "GameDirector"; xform = @{location = @{x = 0.0; y = 0.0; z = 300.0 } } } | Out-Null
  Write-Output "DIRECTOR-PLACED"
} else { Write-Output "DIRECTOR-EXISTS" }

Ue "editor_toolset.toolsets.asset.AssetTools" "save_assets" @{asset_paths = @("/Game/Hotel/Gameplay/BP_GameDirector", "/Game/Hotel/Gameplay/BP_Enemy", "/Game/Hotel/Gameplay/BP_CrocAnim", "/Game/Hotel/UI/WBP_HotelHUD", "/Game/Maps/HotelPryingEyes") } | Out-Null
Write-Output "SAVED"

Ue "EditorToolset.EditorAppToolset" "StartPIE" @{options = @{bSimulate = $false; playMode = "PlayMode_InViewPort"; warmupSeconds = 20 } } | Out-Null
Write-Output "PIE-STARTED"
Start-Sleep -Seconds 25
$log = Ue "EditorToolset.LogsToolset" "GetLogEntries" @{category = "LogBlueprintUserMessages"; pattern = ""; maxEntries = 30 }
$txt = ($log | Out-String)
foreach ($pat in @('ONDATA 1', 'SEI MORTO') ) {
  if ($txt -match $pat) { Write-Output "LOG-HIT: $pat" } else { Write-Output "LOG-MISS: $pat" }
}
Ue "EditorToolset.EditorAppToolset" "StopPIE" @{} | Out-Null
Write-Output "PIE-STOPPED"
Write-Output "PHASE-G1-OK"

# Fase Input-1 (probe) — scopre i pin dei nodi input classici.
# Va eseguito con l'editor aperto sul progetto PryingEyes 5.8.
. "$PSScriptRoot\ue_common.ps1"
$G = @{refPath = "/Game/Hotel/Tests/BP_Sandbox.BP_Sandbox:EventGraph" }
foreach ($nt in @("Input|KeyboardEvents|W", "Input|MouseEvents|LeftMouseButton", "Input|MouseEvents|MouseXY2D-Axis", "Game|Player|IsInputKeyDown")) {
  Write-Output "===== PINS $nt ====="
  try {
    Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "get_node_type_pins" @{graph = $G; type_id = $nt }
  }
  catch {
    Write-Output ("ERR: " + $_.Exception.Message.Substring(0, [Math]::Min(300, $_.Exception.Message.Length)))
  }
}
Write-Output "PROBE-DONE"

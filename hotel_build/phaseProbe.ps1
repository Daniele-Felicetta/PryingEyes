# Probe: quali AddEvent esistono nel grafo sandbox
. "$PSScriptRoot\ue_common.ps1"
$G = @{refPath = "/Game/Hotel/Tests/BP_Sandbox.BP_Sandbox:EventGraph" }
$res = Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "find_node_types" @{
  graph = $G
  type_id_filter = "AddEvent"
  context_pins = @()
}
$res | Out-File -Encoding utf8 "$env:TEMP\addevents.txt"
Write-Output ("TROVATI: " + $res.Count)

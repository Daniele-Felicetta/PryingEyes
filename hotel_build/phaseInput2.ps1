# Test input grezzi: click mouse + tasto W con log di verifica in PIE
. "$PSScriptRoot\ue_common.ps1"
$Code = @'
(event EventBeginPlay
  (Development|PrintString "SANDBOX ALIVE"))
(event Input|MouseEvents|LeftMouseButton
  (:Pressed
    (Development|PrintString "CLICK-FIRE")))
(event Input|KeyboardEvents|W
  (:Pressed
    (Development|PrintString "W-DOWN"))
  (:Released
    (Development|PrintString "W-UP")))
'@
Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "write_graph_dsl" @{
  blueprint = @{refPath = "/Game/Hotel/Tests/BP_Sandbox.BP_Sandbox" }
  graph = @{refPath = "/Game/Hotel/Tests/BP_Sandbox.BP_Sandbox:EventGraph" }
  code = $Code
}
Write-Output "WRITE-OK"
Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "compile_blueprint" @{
  blueprint = @{refPath = "/Game/Hotel/Tests/BP_Sandbox.BP_Sandbox" }
}
Write-Output "COMPILE-OK"

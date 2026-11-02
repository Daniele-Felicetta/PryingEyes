# Fase InputTest: scrive il grafo di test nel sandbox, compila, abilita input, salva
. "$PSScriptRoot\ue_common.ps1"
$Code = @'
(event EventBeginPlay
  (Development|PrintString "SANDBOX ALIVE"))
(event Input|MouseEvents|LeftMouseButton
  (:Pressed
    (Development|PrintString "CLICK")))
(event Input|KeyboardEvents|W
  (:Pressed
    (Development|PrintString "W-DOWN"))
  (:Released
    (Development|PrintString "W-UP")))
(event Input|MouseEvents|MouseXY2D-Axis
  (:then
    (Development|PrintString "MOUSE-MOVE")))
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
Ue "editor_toolset.toolsets.object.ObjectTools" "set_properties" @{
  instance = @{refPath = "/Game/Maps/HotelPryingEyes.HotelPryingEyes:PersistentLevel.TEST_Sandbox" }
  values = (@{autoReceiveInput = "Player0" } | ConvertTo-Json -Compress)
}
Write-Output "INPUT-ENABLED"
Ue "EditorToolset.EditorAppToolset" "StartPIE" @{
  options = @{bSimulate = $false; playMode = "PlayMode_InViewPort"; warmupSeconds = 2.0 }
}
Write-Output "PIE-STARTED"

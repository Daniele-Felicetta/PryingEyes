# Probe: cerca nodi evento per input raw (tastiera/mouse)
. "$PSScriptRoot\ue_common.ps1"
$G = @{refPath = "/Game/Hotel/Tests/BP_Sandbox.BP_Sandbox:EventGraph" }
foreach ($f in @("KeyEvent", "InputKey", "Pressed", "Mouse Button", "Keyboard")) {
  Write-Output "===== filtro: $f ====="
  try {
    $r = Ue "editor_toolset.toolsets.blueprint.BlueprintTools" "find_node_types" @{graph = $G; type_id_filter = $f; context_pins = @() }
    $r | Select-Object -First 12 | ForEach-Object { Write-Output ("  " + $_) }
  } catch { Write-Output ("  ERR: " + $_.Exception.Message.Substring(0, 150)) }
}
Write-Output "PROBE-DONE"

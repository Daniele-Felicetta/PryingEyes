# ue_common.ps1 — helper dot-sourced dagli script di build
$script:SID = ""
function UeInitSession() {
    $p = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"hotelbuild","version":"1.0"}}}'
    $p | Set-Content -NoNewline "$env:TEMP\mcp_init.json"
    curl.exe -s -m 15 -D "$env:TEMP\mcp_init_hdr.txt" -X POST http://127.0.0.1:8001/mcp `
        -H "Content-Type: application/json" `
        -H "Accept: application/json, text/event-stream" `
        --data "@$env:TEMP\mcp_init.json" | Out-Null
    $hdr = Get-Content "$env:TEMP\mcp_init_hdr.txt" | Where-Object { $_ -match '(?i)^Mcp-Session-Id:\s*(\S+)' }
    if (-not $hdr) { throw "Init fallita: nessuna sessione" }
    $script:SID = $Matches[1].Trim()
    Write-Output "SID: $script:SID"
}
UeInitSession
$script:UEID = 1000
function Ue($Toolset, $Tool, $Params) {
    $script:UEID++
    $payload = @{jsonrpc = "2.0"; id = $script:UEID; method = "tools/call";
        params = @{name = "call_tool"; arguments = @{toolset_name = $Toolset; tool_name = $Tool; arguments = $Params }
        } } | ConvertTo-Json -Depth 14 -Compress
    $payload | Set-Content -NoNewline "$env:TEMP\mcp_c.json"
    $r = curl.exe -s -m 40 -X POST http://127.0.0.1:8001/mcp `
        -H "Content-Type: application/json" `
        -H "Accept: application/json, text/event-stream" `
        -H "Mcp-Session-Id: $($script:SID)" --data "@$env:TEMP\mcp_c.json"
    $j = $r | ConvertFrom-Json
    if ($j.error) { throw "ERR $Tool : $($j.error.message)" }
    $txt = $j.result.content[0].text
    try { $p = $txt | ConvertFrom-Json }
    catch { throw "ERR ${Tool}: non-JSON: $txt" }
    if ($p.PSObject.Properties.Name -notcontains 'returnValue') { throw "ERR ${Tool}: no returnValue: $txt" }
    return $p.returnValue
}
function UeScene($Tool, $Params) { return Ue "editor_toolset.toolsets.scene.SceneTools" $Tool $Params }
function UeActor($Tool, $Params) { return Ue "editor_toolset.toolsets.actor.ActorTools" $Tool $Params }
function UeObj($Tool, $Params) { return Ue "editor_toolset.toolsets.object.ObjectTools" $Tool $Params }
function UeMat($Tool, $Params) { return Ue "editor_toolset.toolsets.material.MaterialTools" $Tool $Params }
function UeMI($Tool, $Params) { return Ue "editor_toolset.toolsets.material_instance.MaterialInstanceTools" $Tool $Params }
function UeAsset($Tool, $Params) { return Ue "editor_toolset.toolsets.asset.AssetTools" $Tool $Params }
function Ref($p) { return @{refPath = $p} }
function Xf($x, $y, $z, $sx, $sy, $sz, $yaw) {
    $h = @{location = @{x = [double]$x; y = [double]$y; z = [double]$z } }
    if ($PSBoundParameters.ContainsKey('sx')) {
        $h.scale = @{x = [double]$sx; y = [double]$sy; z = [double]$sz }
    }
    if ($PSBoundParameters.ContainsKey('yaw')) {
        $h.rotation = @{pitch = 0.0; yaw = [double]$yaw; roll = 0.0 }
    }
    return $h
}
# Crea un attore StaticMesh con un componente primitivo, trasforma, assegna materiale+folder+label.
# Restituisce @{actor=ref; comp=ref}. $kind: cube|sphere|cylinder|cone
function Box($Name, $Loc, $Scale, $Mat, $Folder, $Kind, $Yaw) {
    $xf = @{location = @{x = [double]$Loc[0]; y = [double]$Loc[1]; z = [double]$Loc[2] };
        scale     = @{x = [double]$Scale[0]; y = [double]$Scale[1]; z = [double]$Scale[2] } }
    if ($PSBoundParameters.ContainsKey('Yaw')) {
        $xf.rotation = @{pitch = 0.0; yaw = [double]$Yaw; roll = 0.0 }
    }
    $a = UeScene "add_to_scene_from_class" @{actor_type = (Ref "/Script/Engine.StaticMeshActor"); name = $Name; xform = $xf }
    $aref = $a.refPath
    $prim = "add_cube"
    if ($Kind -eq "sphere") { $prim = "add_sphere" }
    elseif ($Kind -eq "cylinder") { $prim = "add_cylinder" }
    elseif ($Kind -eq "cone") { $prim = "add_cone" }
    $c = Ue "editor_toolset.toolsets.primitive.PrimitiveTools" $prim @{actor = (Ref $aref); name = "Prim" }
    $cref = $c.refPath
    if ($Mat) {
        UeObj "set_properties" @{instance = (Ref $cref); values = (@{overrideMaterials = @((Ref $Mat)) } | ConvertTo-Json -Compress -Depth 5) } | Out-Null
    }
    UeActor "set_label" @{actor = (Ref $aref); label = $Name } | Out-Null
    if ($Folder) { UeScene "set_actor_folder" @{actor = (Ref $aref); folder_path = $Folder } | Out-Null }
    return @{actor = $aref; comp = $cref }
}


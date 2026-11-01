import json


def call(tool, args):
    return execute_tool(tool, json.dumps(args))


def run():
    out = {}
    a = call("editor_toolset.toolsets.scene.SceneTools.add_to_scene_from_class", {
        "actor_type": {"refPath": "/Script/Engine.StaticMeshActor"},
        "name": "Hotel_Floor",
        "xform": {"location": {"x": 0.0, "y": 0.0, "z": -25.0},
                  "scale": {"x": 70.0, "y": 46.0, "z": 0.5}}
    })
    out["actor"] = a
    actor_ref = a["returnValue"]["refPath"]
    c = call("editor_toolset.toolsets.primitive.PrimitiveTools.add_cube", {
        "actor": {"refPath": actor_ref}, "name": "Box"})
    out["comp"] = c
    m = call("editor_toolset.toolsets.material.MaterialTools.create_material", {
        "folder_path": "/Game/Hotel/Materials", "asset_name": "M_Pilot"})
    out["mat"] = m
    mat_ref = m["returnValue"]["refPath"]
    e1 = call("editor_toolset.toolsets.material.MaterialTools.add_expression", {
        "material_or_function": {"refPath": mat_ref},
        "expression_class": {"refPath": "/Script/Engine.MaterialExpressionVectorParameter"}})
    out["expr1"] = e1
    exps = call("editor_toolset.toolsets.material.MaterialTools.get_expressions", {
        "material_or_function": {"refPath": mat_ref}})
    out["exprs"] = exps
    return out

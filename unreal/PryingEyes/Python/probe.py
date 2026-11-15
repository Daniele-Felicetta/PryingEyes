"""Probe: which editor Python APIs does this engine build expose? Prints one line per check. Run it with
tools/unreal/ue.ps1 python unreal/PryingEyes/Python/probe.py"""
import unreal

print("PE_PROBE engine", unreal.SystemLibrary.get_engine_version())
print("PE_PROBE project", unreal.Paths.get_project_file_path() if hasattr(unreal, "Paths") else "?")

names = [
    "AssetImportTask", "AssetToolsHelpers", "FbxImportUI", "FbxStaticMeshImportData", "FbxSkeletalMeshImportData",
    "FbxAnimSequenceImportData", "FbxTextureImportData", "FbxMeshImportData",
    "InterchangeAssetImportData", "InterchangeGenericAssetsPipeline", "InterchangeGenericMaterialPipeline",
    "InterchangePipelineBase", "MaterialEditingLibrary", "MaterialFactoryNew", "MaterialInstanceConstantFactoryNew",
    "MaterialExpressionTextureSampleParameter2D", "MaterialExpressionScalarParameter", "MaterialExpressionVectorParameter",
    "EditorAssetLibrary", "EditorLevelLibrary", "EditorActorSubsystem", "StaticMeshEditorSubsystem",
    "GeometryScriptLibrary_MeshBasicEditFunctions", "GeometryScriptLibrary_StaticMeshFunctions",
    "GeometryScriptLibrary_CreateNewAssetFunctions", "DynamicMesh", "PEGameMode",
    "ScriptingSystemLibrary", "TextureFactory", "MeshDescription", "StaticMeshDescription",
]
for n in names:
    print("PE_PROBE class", n, "yes" if hasattr(unreal, n) else "NO")

# the import-related enums we will need
for n in ("MaterialSamplerType", "TextureCompressionSettings", "TextureGroup", "FBXImportType", "MaterialParameterAssociation",
          "ELightUnits", "ComponentMobility"):
    print("PE_PROBE enum", n, "yes" if hasattr(unreal, n) else "NO")

# Interchange or the legacy importer?
try:
    cvar = unreal.SystemLibrary.get_console_variable_bool_value("Interchange.FeatureFlags.Import.FBX")
    print("PE_PROBE interchange_fbx_cvar", cvar)
except Exception as e:  # noqa: BLE001
    print("PE_PROBE interchange_fbx_cvar error", e)

content = unreal.Paths.project_content_dir()
print("PE_PROBE content_dir", content)

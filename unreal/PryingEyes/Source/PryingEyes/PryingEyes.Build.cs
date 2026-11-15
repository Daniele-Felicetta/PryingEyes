using UnrealBuildTool;

public class PryingEyes : ModuleRules
{
	public PryingEyes(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "InputCore", "RenderCore", "RHI", "Json" });

		// the import helpers build meshes inside the editor only
		if (Target.bBuildEditor)
		{
			PrivateDependencyModuleNames.AddRange(new string[] { "MeshDescription", "StaticMeshDescription", "AssetRegistry" });
		}
	}
}

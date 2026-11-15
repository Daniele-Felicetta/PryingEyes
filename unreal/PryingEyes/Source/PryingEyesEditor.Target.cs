// Prying Eyes - Unreal prototype. Editor target (what the command-line import and photo runs use).

using UnrealBuildTool;
using System.Collections.Generic;

public class PryingEyesEditorTarget : TargetRules
{
	public PryingEyesEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V7;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
		ExtraModuleNames.Add("PryingEyes");
	}
}

// Prying Eyes - Unreal prototype.

#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "PEImportLibrary.generated.h"

class UStaticMesh;

/** Editor-time helpers that the import scripts (Python/import_scene.py) call. They do nothing in a packaged game. */
UCLASS()
class UPEImportLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/**
	 * Builds a static mesh asset (not yet saved) from a .bin written by Unity's UnrealExport.cs: positions, normals, UVs and one
	 * index list per submesh, in Unity's axes and metres. Axes become Unreal's (X forward, Y right, Z up), the unit the centimetre.
	 * PackagePath is the asset path, for example /Game/Scene/x/SM_3. Returns null on failure.
	 */
	UFUNCTION(BlueprintCallable, Category = "PryingEyes")
	static UStaticMesh* BuildStaticMeshFromFile(const FString& FilePath, const FString& PackagePath, bool bNanite);
};

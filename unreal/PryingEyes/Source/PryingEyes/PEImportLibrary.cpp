// Prying Eyes - Unreal prototype.

#include "PEImportLibrary.h"

#include "Engine/StaticMesh.h"
#include "Misc/FileHelper.h"
#include "Misc/PackageName.h"
#include "Serialization/MemoryReader.h"
#include "UObject/Package.h"
#if WITH_EDITOR
#include "AssetRegistry/AssetRegistryModule.h"
#include "MeshDescription.h"
#include "StaticMeshAttributes.h"
#endif

DEFINE_LOG_CATEGORY_STATIC(LogPEImport, Log, All);

UStaticMesh* UPEImportLibrary::BuildStaticMeshFromFile(const FString& FilePath, const FString& PackagePath, bool bNanite)
{
#if WITH_EDITOR
	TArray<uint8> Bytes;
	if (!FFileHelper::LoadFileToArray(Bytes, *FilePath))
	{
		UE_LOG(LogPEImport, Error, TEXT("cannot read %s"), *FilePath);
		return nullptr;
	}
	FMemoryReader Reader(Bytes);
	int32 Magic = 0, VertexCount = 0, SubCount = 0, HasNormals = 0, HasUVs = 0;
	Reader << Magic << VertexCount << SubCount << HasNormals << HasUVs;
	if (Magic != 0x314D4550 || VertexCount <= 0 || SubCount <= 0)
	{
		UE_LOG(LogPEImport, Error, TEXT("bad mesh file %s"), *FilePath);
		return nullptr;
	}

	// Unity (x right, y up, z forward, metres) -> Unreal (X forward, Y right, Z up, centimetres): (x, y, z) -> (z, x, y).
	// The permutation is even, so the handedness and the winding order stay as they are.
	TArray<FVector3f> Positions, Normals;
	TArray<FVector2f> UVs;
	Positions.SetNumUninitialized(VertexCount);
	Normals.SetNumZeroed(VertexCount);
	UVs.SetNumZeroed(VertexCount);
	for (int32 I = 0; I < VertexCount; ++I)
	{
		float X, Y, Z;
		Reader << X << Y << Z;
		Positions[I] = FVector3f(Z * 100.f, X * 100.f, Y * 100.f);
	}
	if (HasNormals)
	{
		for (int32 I = 0; I < VertexCount; ++I)
		{
			float X, Y, Z;
			Reader << X << Y << Z;
			Normals[I] = FVector3f(Z, X, Y);
		}
	}
	if (HasUVs)
	{
		for (int32 I = 0; I < VertexCount; ++I)
		{
			float U, V;
			Reader << U << V;
			UVs[I] = FVector2f(U, 1.f - V);    // Unity's V starts at the bottom, Unreal's at the top
		}
	}

	FMeshDescription MeshDescription;
	FStaticMeshAttributes Attributes(MeshDescription);
	Attributes.Register();
	TVertexAttributesRef<FVector3f> VertexPositions = Attributes.GetVertexPositions();
	TVertexInstanceAttributesRef<FVector3f> InstanceNormals = Attributes.GetVertexInstanceNormals();
	TVertexInstanceAttributesRef<FVector3f> InstanceTangents = Attributes.GetVertexInstanceTangents();
	TVertexInstanceAttributesRef<float> InstanceSigns = Attributes.GetVertexInstanceBinormalSigns();
	TVertexInstanceAttributesRef<FVector2f> InstanceUVs = Attributes.GetVertexInstanceUVs();
	TPolygonGroupAttributesRef<FName> SlotNames = Attributes.GetPolygonGroupMaterialSlotNames();
	InstanceUVs.SetNumChannels(1);

	MeshDescription.ReserveNewVertices(VertexCount);
	MeshDescription.ReserveNewVertexInstances(VertexCount);
	TArray<FVertexInstanceID> Instances;
	Instances.Reserve(VertexCount);
	for (int32 I = 0; I < VertexCount; ++I)
	{
		const FVertexID Vertex = MeshDescription.CreateVertex();
		VertexPositions[Vertex] = Positions[I];
		const FVertexInstanceID Instance = MeshDescription.CreateVertexInstance(Vertex);
		InstanceNormals[Instance] = Normals[I];
		InstanceTangents[Instance] = FVector3f::ZeroVector;    // recomputed by the build (MikkTSpace)
		InstanceSigns[Instance] = 1.f;
		InstanceUVs.Set(Instance, 0, UVs[I]);
		Instances.Add(Instance);
	}

	int32 TriangleCount = 0;
	for (int32 Sub = 0; Sub < SubCount; ++Sub)
	{
		int32 IndexCount = 0;
		Reader << IndexCount;
		const FPolygonGroupID Group = MeshDescription.CreatePolygonGroup();
		SlotNames[Group] = FName(*FString::Printf(TEXT("s%d"), Sub));
		for (int32 I = 0; I + 2 < IndexCount; I += 3)
		{
			int32 A, B, C;
			Reader << A << B << C;
			if (A == B || B == C || A == C || A >= VertexCount || B >= VertexCount || C >= VertexCount) continue;
			// Unity's front faces are wound clockwise, Unreal's counter-clockwise (the axes keep their handedness)
			MeshDescription.CreateTriangle(Group, { Instances[A], Instances[C], Instances[B] });
			++TriangleCount;
		}
	}
	if (TriangleCount == 0)
	{
		UE_LOG(LogPEImport, Warning, TEXT("%s has no usable triangle"), *FilePath);
		return nullptr;
	}

	UPackage* Package = CreatePackage(*PackagePath);
	const FString Name = FPackageName::GetShortName(PackagePath);
	UStaticMesh* Mesh = NewObject<UStaticMesh>(Package, FName(*Name), RF_Public | RF_Standalone);
	Mesh->SetNumSourceModels(1);
	FMeshBuildSettings& Settings = Mesh->GetSourceModel(0).BuildSettings;
	Settings.bRecomputeNormals = (HasNormals == 0);
	Settings.bRecomputeTangents = true;
	Settings.bUseMikkTSpace = true;
	Settings.bGenerateLightmapUVs = false;    // Lumen needs none
	Mesh->NaniteSettings.bEnabled = bNanite;

	UStaticMesh::FBuildMeshDescriptionsParams Params;
	Params.bMarkPackageDirty = true;
	Params.bBuildSimpleCollision = false;
	TArray<const FMeshDescription*> Descriptions;
	Descriptions.Add(&MeshDescription);
	if (!Mesh->BuildFromMeshDescriptions(Descriptions, Params))
	{
		UE_LOG(LogPEImport, Error, TEXT("BuildFromMeshDescriptions failed for %s"), *FilePath);
		return nullptr;
	}
	FAssetRegistryModule::AssetCreated(Mesh);
	Package->MarkPackageDirty();
	return Mesh;
#else
	return nullptr;
#endif
}

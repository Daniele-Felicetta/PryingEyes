// Prying Eyes - Unreal prototype.

#include "PEGameMode.h"

#include "Animation/AnimSequence.h"
#include "Animation/SkeletalMeshActor.h"
#include "Camera/CameraActor.h"
#include "Camera/CameraComponent.h"
#include "Components/DirectionalLightComponent.h"
#include "Components/ExponentialHeightFogComponent.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Components/PointLightComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/SpotLightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/DirectionalLight.h"
#include "Engine/Engine.h"
#include "Engine/ExponentialHeightFog.h"
#include "Engine/GameViewportClient.h"
#include "Engine/PointLight.h"
#include "Engine/PostProcessVolume.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/SpotLight.h"
#include "Engine/StaticMesh.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/Texture.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformTime.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Materials/MaterialInterface.h"
#include "Misc/App.h"
#include "Misc/CommandLine.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/PackageName.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"
#include "RHI.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "UnrealClient.h"
#if WITH_EDITOR
#include "AssetCompilingManager.h"
#include "ShaderCompiler.h"
#endif

DEFINE_LOG_CATEGORY_STATIC(LogPE, Log, All);

namespace
{
	float Percentile(TArray<float> Values, float P)
	{
		if (Values.Num() == 0) return 0.f;
		Values.Sort();
		const int32 Index = FMath::Clamp(FMath::RoundToInt((Values.Num() - 1) * P), 0, Values.Num() - 1);
		return Values[Index];
	}

	float Average(const TArray<float>& Values)
	{
		double Sum = 0.0;
		for (float V : Values) Sum += V;
		return Values.Num() ? float(Sum / Values.Num()) : 0.f;
	}

	TSharedPtr<FJsonObject> LoadJsonFile(const FString& Path)
	{
		FString Text;
		if (!FFileHelper::LoadFileToString(Text, *Path))
		{
			UE_LOG(LogPE, Error, TEXT("cannot read %s"), *Path);
			return nullptr;
		}
		TSharedPtr<FJsonObject> Root;
		TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Text);
		if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
		{
			UE_LOG(LogPE, Error, TEXT("cannot parse %s"), *Path);
			return nullptr;
		}
		return Root;
	}

	// "/Game/Dir/Name" -> "/Game/Dir/Name.Name"
	FString ObjectPath(const FString& PackagePath)
	{
		if (PackagePath.Contains(TEXT("."))) return PackagePath;
		return PackagePath + TEXT(".") + FPackageName::GetShortName(PackagePath);
	}

	FVector ReadVec(const TSharedPtr<FJsonObject>& Object, const TCHAR* Field)
	{
		const TArray<TSharedPtr<FJsonValue>>* Array = nullptr;
		if (Object.IsValid() && Object->TryGetArrayField(Field, Array) && Array->Num() >= 3)
			return FVector((*Array)[0]->AsNumber(), (*Array)[1]->AsNumber(), (*Array)[2]->AsNumber());
		return FVector::ZeroVector;
	}

	double Number(const TSharedPtr<FJsonObject>& Object, const TCHAR* Field, double Default = 0.0)
	{
		double V = Default;
		if (Object.IsValid()) Object->TryGetNumberField(Field, V);
		return V;
	}

	float ArgFloat(const TCHAR* Name, float Default)
	{
		float V = Default;
		FParse::Value(FCommandLine::Get(), Name, V);
		return V;
	}
}

APEGameMode::APEGameMode()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.bStartWithTickEnabled = true;
	DefaultPawnClass = nullptr;
	HUDClass = nullptr;
}

void APEGameMode::BeginPlay()
{
	Super::BeginPlay();

	const TCHAR* Cmd = FCommandLine::Get();
	SceneName = TEXT("monk");
	FParse::Value(Cmd, TEXT("-pe_scene="), SceneName);
	OutDir = FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("PE"));
	FParse::Value(Cmd, TEXT("-pe_out="), OutDir);
	FParse::Value(Cmd, TEXT("-pe_warm="), WarmFrames);
	FParse::Value(Cmd, TEXT("-pe_measure="), MeasureFrames);
	FParse::Value(Cmd, TEXT("-pe_res_tag="), Tag);
	FParse::Value(Cmd, TEXT("-pe_animtime="), PhotoAnimTime);
	OutDir = FPaths::ConvertRelativePathToFull(OutDir);
	IFileManager::Get().MakeDirectory(*OutDir, true);
	PhotoPath = FPaths::Combine(OutDir, FString::Printf(TEXT("pe_%s.png"), *SceneName));
	IFileManager::Get().Delete(*PhotoPath, false, true, true);

	UE_LOG(LogPE, Log, TEXT("scene=%s warm=%d measure=%d out=%s"), *SceneName, WarmFrames, MeasureFrames, *OutDir);

	if (SceneName == TEXT("monk"))
	{
		BuildMonkScene();
	}
	else if (SceneName == TEXT("room"))
	{
		FString File;
		FParse::Value(Cmd, TEXT("-pe_file="), File);
		BuildRoomScene(File);
	}
	else if (SceneName == TEXT("ramp"))
	{
		FString File;
		FParse::Value(Cmd, TEXT("-pe_file="), File);
		BuildRampScene(File);
	}
	else if (SceneName == TEXT("lab"))
	{
		FString File;
		FParse::Value(Cmd, TEXT("-pe_file="), File);
		BuildLabScene(File);
	}
	else UE_LOG(LogPE, Error, TEXT("unknown scene '%s'"), *SceneName);

	StageStart = FPlatformTime::Seconds();
}

void APEGameMode::SpawnCamera(const FVector& Location, const FRotator& Rotation, float VerticalFov)
{
	// Unreal's field of view is the horizontal one: convert Unity's vertical angle through the window's aspect ratio
	float ResX = 1920.f, ResY = 1080.f;
	FParse::Value(FCommandLine::Get(), TEXT("ResX="), ResX);
	FParse::Value(FCommandLine::Get(), TEXT("ResY="), ResY);
	const float Aspect = ResY > 0.f ? ResX / ResY : 16.f / 9.f;
	const float Horizontal = FMath::RadiansToDegrees(2.f * FMath::Atan(FMath::Tan(FMath::DegreesToRadians(VerticalFov) * 0.5f) * Aspect));

	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	ACameraActor* Cam = GetWorld()->SpawnActor<ACameraActor>(Location, Rotation, Params);
	if (Cam)
	{
		UCameraComponent* Comp = Cam->GetCameraComponent();
		Comp->SetFieldOfView(Horizontal);
		Comp->bConstrainAspectRatio = false;
		CameraActor = Cam;
		UE_LOG(LogPE, Log, TEXT("camera at %s rotation %s, vertical fov %.1f -> horizontal %.1f"), *Location.ToString(), *Rotation.ToString(), VerticalFov, Horizontal);
	}
}

void APEGameMode::SpawnPostProcess(float ExposureStops, float Saturation, float Contrast, float Bloom, float Vignette, bool bFitUnity)
{
	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	APostProcessVolume* PP = GetWorld()->SpawnActor<APostProcessVolume>(FVector::ZeroVector, FRotator::ZeroRotator, Params);
	if (!PP) return;
	PP->bUnbound = true;
	FPostProcessSettings& S = PP->Settings;
	// a fixed exposure, so that two photos compare (the camera's own settings give about EV 9.9 at bias 0)
	S.bOverride_AutoExposureMethod = true;
	S.AutoExposureMethod = AEM_Manual;
	S.bOverride_AutoExposureBias = true;
	S.AutoExposureBias = ExposureStops;
	S.bOverride_ColorSaturation = true;
	S.ColorSaturation = FVector4(Saturation, Saturation, Saturation, 1.0);
	S.bOverride_ColorContrast = true;
	S.ColorContrast = FVector4(Contrast, Contrast, Contrast, 1.0);
	S.bOverride_BloomIntensity = true;
	S.BloomIntensity = Bloom;
	S.bOverride_VignetteIntensity = true;
	S.VignetteIntensity = Vignette;
	S.bOverride_MotionBlurAmount = true;
	S.MotionBlurAmount = 0.f;
	if (GradeMaterial) S.AddBlendable(GradeMaterial, 1.f);
	if (ArgFloat(TEXT("-pe_gi="), 1.f) < 0.5f)
	{
		// Unity has no global illumination: with this the room is lit by the same direct light, flat ambient and reflection cube
		// as its Unity photo, which is how the light's scale (K) is checked
		S.bOverride_DynamicGlobalIlluminationMethod = true;
		S.DynamicGlobalIlluminationMethod = EDynamicGlobalIlluminationMethod::None;
		S.bOverride_ReflectionMethod = true;
		S.ReflectionMethod = EReflectionMethod::None;
		bGiOff = true;
	}
	if (bFitUnity)
	{
		// Unreal's filmic curve against URP's ACES: these four, with the exposure offset in SpawnGrade and the contrast and Split Toning
		// of M_UnityGrade, bring Unreal's response on the tone ramp (BuildRampScene) to a weighted mean of 1.4 display levels from
		// Unity's. Found by tools/unreal/fit_ramp.py (run it again, and copy the numbers here, if the game's look changes)
		S.bOverride_FilmToe = true; S.FilmToe = 0.7f;
		S.bOverride_FilmSlope = true; S.FilmSlope = 0.6f;
		S.bOverride_FilmShoulder = true; S.FilmShoulder = 0.46f;
		S.bOverride_FilmWhiteClip = true; S.FilmWhiteClip = 0.0f;
	}
	// the filmic curve and the colour grade, for matching the game's tone response (see BuildRampScene); each is optional
#define PE_OPT(ARG, FIELD) { float V = 0.f; if (FParse::Value(FCommandLine::Get(), TEXT(ARG), V)) { S.bOverride_##FIELD = true; S.FIELD = V; } }
	PE_OPT("-pe_toe=", FilmToe);
	PE_OPT("-pe_slope=", FilmSlope);
	PE_OPT("-pe_shoulder=", FilmShoulder);
	PE_OPT("-pe_blackclip=", FilmBlackClip);
	PE_OPT("-pe_whiteclip=", FilmWhiteClip);
#undef PE_OPT
	float Gamma = 0.f;
	if (FParse::Value(FCommandLine::Get(), TEXT("-pe_gamma="), Gamma))
	{
		S.bOverride_ColorGamma = true;
		S.ColorGamma = FVector4(Gamma, Gamma, Gamma, 1.0);
	}
}

void APEGameMode::SpawnGrade(const TSharedPtr<FJsonObject>& Look, bool bNoEffects)
{
	const float EvAdd = ArgFloat(TEXT("-pe_ev="), 0.f);
	const float Saturation = ArgFloat(TEXT("-pe_saturation="), 1.f + float(Number(Look, TEXT("saturation"), 0.0)) / 100.f);
	const float Contrast = ArgFloat(TEXT("-pe_contrast="), 1.f + float(Number(Look, TEXT("contrast"), 0.0)) / 100.f);
	// URP's Color Adjustments "Post Exposure" is in stops, applied before the tone mapper: it becomes Unreal's exposure bias
	const float PostExposure = float(Number(Look, TEXT("postExposure"), 0.0));
	constexpr float FitEv = -0.6f;    // with the film curve in SpawnPostProcess (bFitUnity): the grey ramp's best fit
	// URP's Split Toning has no counterpart in Unreal's own grade: a post-process material does it, in the same units (see pe_unity.py)
	GradeMaterial = nullptr;
	if (Look.IsValid() && Look->HasField(TEXT("splitShadows")) && ArgFloat(TEXT("-pe_split="), 1.f) > 0.5f)
	{
		if (UMaterialInterface* Grade = LoadObject<UMaterialInterface>(nullptr, TEXT("/Game/Imported/Materials/M_UnityGrade.M_UnityGrade")))
		{
			const FVector Shadows = ReadVec(Look, TEXT("splitShadows")), Highlights = ReadVec(Look, TEXT("splitHighlights"));
			GradeMaterial = UMaterialInstanceDynamic::Create(Grade, this);
			GradeMaterial->SetScalarParameterValue(TEXT("ToUnity"), FMath::Pow(2.f, PostExposure) / FMath::Max(RadianceScale, 1e-3f));
			GradeMaterial->SetScalarParameterValue(TEXT("Contrast"), Contrast);    // Unity's contrast comes before the split, so it lives here
			GradeMaterial->SetScalarParameterValue(TEXT("Balance"), float(Number(Look, TEXT("splitBalance"), 0.0)) / 100.f);
			GradeMaterial->SetVectorParameterValue(TEXT("SplitShadows"), FLinearColor(Shadows.X, Shadows.Y, Shadows.Z));
			GradeMaterial->SetVectorParameterValue(TEXT("SplitHighlights"), FLinearColor(Highlights.X, Highlights.Y, Highlights.Z));
			UE_LOG(LogPE, Log, TEXT("split toning: shadows %s highlights %s balance %.2f"), *Shadows.ToString(), *Highlights.ToString(), float(Number(Look, TEXT("splitBalance"), 0.0)) / 100.f);
		}
		else UE_LOG(LogPE, Warning, TEXT("split toning: M_UnityGrade missing (run Python/import_grade.py)"));
	}
	const float Bloom = bNoEffects ? 0.f : float(Number(Look, TEXT("bloom"), 0.5)) * 0.5f;
	const float Vignette = bNoEffects ? 0.f : float(Number(Look, TEXT("vignette"), 0.3));
	SpawnPostProcess(EvAdd + PostExposure + FitEv, Saturation, GradeMaterial ? 1.f : Contrast, Bloom, Vignette, true);
}

void APEGameMode::BuildRampScene(const FString& File)
{
	// the game's tone response, measured the same way in both engines: thirty unlit patches of known value (a factor 1.4 apart) in front
	// of the camera, in six columns and five rows, with the room's grade (Unity: UnrealExport -ramp 1, which writes ramp_unity.txt).
	// A value v is picture-referred: here it is emissive radiance v x K / pi, which the exposure brings back to v
	constexpr int32 Count = 30;
	const TSharedPtr<FJsonObject> J = LoadJsonFile(File);
	const TSharedPtr<FJsonObject>* LookPtr = nullptr;
	if (J.IsValid()) J->TryGetObjectField(TEXT("look"), LookPtr);
	const TSharedPtr<FJsonObject> Look = LookPtr ? *LookPtr : nullptr;
	RadianceScale = ArgFloat(TEXT("-pe_lightk="), 3600.f) / PI;
	UWorld* World = GetWorld();
	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	UMaterialInterface* Master = LoadObject<UMaterialInterface>(nullptr, TEXT("/Game/Imported/Materials/M_UnityLit.M_UnityLit"));
	UStaticMesh* Plane = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane"));
	if (!Master || !Plane) { UE_LOG(LogPE, Error, TEXT("ramp: master material or plane missing")); return; }
	for (int32 I = 0; I < Count; ++I)
	{
		const float V = 0.0005f * FMath::Pow(1.4f, float(I));
		AStaticMeshActor* Patch = World->SpawnActor<AStaticMeshActor>(FVector(1000.f, (I % 6 - 2.5f) * 200.f, (2.f - float(I / 6)) * 200.f), FRotator(90.f, 0.f, 0.f), Params);
		UStaticMeshComponent* C = Patch->GetStaticMeshComponent();
		C->SetMobility(EComponentMobility::Movable);
		C->SetStaticMesh(Plane);
		C->SetCastShadow(false);
		Patch->SetActorScale3D(FVector(1.4f, 1.4f, 1.f));
		UMaterialInstanceDynamic* Dyn = UMaterialInstanceDynamic::Create(Master, Patch);
		Dyn->SetVectorParameterValue(TEXT("BaseColor"), FLinearColor::Black);
		Dyn->SetVectorParameterValue(TEXT("EmissionColor"), FLinearColor(V, V, V));
		Dyn->SetScalarParameterValue(TEXT("RadianceScale"), RadianceScale);
		C->SetMaterial(0, Dyn);
	}
	SpawnGrade(Look, true);
	SpawnCamera(FVector::ZeroVector, FRotator::ZeroRotator, 90.f);
}

void APEGameMode::BuildLabScene(const FString& File)
{
	// the light scale, measured without a Unity scene: a white plane facing the camera, one light at the camera, no ambient, no
	// reflections, no global illumination. Unity would show a value of I / d^2 (albedo 1, normal incidence, intensity I, distance d in
	// metres) at the plane's centre; the photo's centre pixel, read through the tone ramp, says what Unreal shows
	const TSharedPtr<FJsonObject> J = LoadJsonFile(File);
	const TSharedPtr<FJsonObject>* LookPtr = nullptr;
	if (J.IsValid()) J->TryGetObjectField(TEXT("look"), LookPtr);
	const TSharedPtr<FJsonObject> Look = LookPtr ? *LookPtr : nullptr;
	const float LightK = ArgFloat(TEXT("-pe_lightk="), 3600.f);
	const float I = ArgFloat(TEXT("-pe_labi="), 1.f);
	const float D = ArgFloat(TEXT("-pe_labd="), 3.f);
	RadianceScale = LightK / PI;
	bDress = true;
	UWorld* World = GetWorld();
	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	UMaterialInterface* Master = LoadObject<UMaterialInterface>(nullptr, TEXT("/Game/Imported/Materials/M_UnityLit.M_UnityLit"));
	UStaticMesh* Plane = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane"));
	if (!Master || !Plane) { UE_LOG(LogPE, Error, TEXT("lab: master material or plane missing")); return; }
	AStaticMeshActor* Wall = World->SpawnActor<AStaticMeshActor>(FVector(D * 100.f, 0.f, 0.f), FRotator(90.f, 0.f, 0.f), Params);
	UStaticMeshComponent* C = Wall->GetStaticMeshComponent();
	C->SetMobility(EComponentMobility::Movable);
	C->SetStaticMesh(Plane);
	Wall->SetActorScale3D(FVector(20.f, 20.f, 1.f));
	UMaterialInstanceDynamic* Mat = Cast<UMaterialInstanceDynamic>(Dress(Master));
	Mat->SetVectorParameterValue(TEXT("BaseColor"), FLinearColor::White);
	Mat->SetScalarParameterValue(TEXT("Metallic"), 0.f);
	Mat->SetScalarParameterValue(TEXT("Smoothness"), 0.f);
	C->SetMaterial(0, Mat);
	if (ArgFloat(TEXT("-pe_labspot="), 0.f) > 0.5f)
	{
		ASpotLight* A = World->SpawnActor<ASpotLight>(FVector::ZeroVector, FRotator::ZeroRotator, Params);
		A->SetMobility(EComponentMobility::Movable);
		USpotLightComponent* L = Cast<USpotLightComponent>(A->GetLightComponent());
		L->SetWorldRotation(FRotator::ZeroRotator);
		L->SetIntensityUnits(ELightUnits::Candelas);
		L->SetIntensity(I * LightK);
		L->SetAttenuationRadius(10000.f);
		L->SetSourceRadius(4.f);
		L->SetOuterConeAngle(45.f);
		L->SetInnerConeAngle(10.f);
		L->SetCastShadows(false);
	}
	else
	{
		APointLight* A = World->SpawnActor<APointLight>(FVector::ZeroVector, FRotator::ZeroRotator, Params);
		A->SetMobility(EComponentMobility::Movable);
		UPointLightComponent* L = A->PointLightComponent;
		L->SetIntensityUnits(ELightUnits::Candelas);
		L->SetIntensity(I * LightK);
		L->SetAttenuationRadius(10000.f);
		L->SetSourceRadius(4.f);
		L->SetCastShadows(false);
	}
	SpawnGrade(Look, true);
	SpawnCamera(FVector::ZeroVector, FRotator::ZeroRotator, 60.f);
}

UMaterialInterface* APEGameMode::Dress(UMaterialInterface* Source)
{
	if (!bDress || !Source) return Source;
	if (const TObjectPtr<UMaterialInstanceDynamic>* Found = Dressed.Find(Source)) return *Found;
	UMaterialInstanceDynamic* Dyn = UMaterialInstanceDynamic::Create(Source, this);
	Dyn->SetScalarParameterValue(TEXT("RadianceScale"), RadianceScale);
	Dyn->SetScalarParameterValue(TEXT("EnvIntensity"), EnvironmentIntensity);
	if (EnvironmentTexture) Dyn->SetTextureParameterValue(TEXT("EnvMap"), EnvironmentTexture);
	Dyn->SetScalarParameterValue(TEXT("DebugMode"), ArgFloat(TEXT("-pe_debug="), 0.f));    // 1 uv, 2 reflection vector, 3 cube colour, 4 specular term, 5 normal, 6 fresnel
	Dyn->SetVectorParameterValue(TEXT("AmbSky"), AmbientSky);
	Dyn->SetVectorParameterValue(TEXT("AmbEquator"), AmbientEquator);
	Dyn->SetVectorParameterValue(TEXT("AmbGround"), AmbientGround);
	Dressed.Add(Source, Dyn);
	return Dyn;
}

USkeletalMeshComponent* APEGameMode::SpawnMonk(const FVector& Location, float Yaw, const FString& AnimName)
{
	UWorld* World = GetWorld();
	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;

	// the importer (Python/import_monk.py) leaves the asset paths in Saved/PE/monk.json
	const TSharedPtr<FJsonObject> Monk = LoadJsonFile(FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("PE"), TEXT("monk.json")));
	USkeletalMesh* Mesh = Monk.IsValid() ? LoadObject<USkeletalMesh>(nullptr, *ObjectPath(Monk->GetStringField(TEXT("mesh")))) : nullptr;
	if (!Mesh)
	{
		UE_LOG(LogPE, Error, TEXT("monk mesh missing: run the import first (Python/import_monk.py)"));
		return nullptr;
	}
	ASkeletalMeshActor* Actor = World->SpawnActor<ASkeletalMeshActor>(Location, FRotator(0.f, Yaw, 0.f), Params);
	USkeletalMeshComponent* Comp = Actor->GetSkeletalMeshComponent();
	Comp->SetMobility(EComponentMobility::Movable);
	Comp->SetSkeletalMesh(Mesh);
	const TSharedPtr<FJsonObject>* Slots = nullptr;
	if (Monk->TryGetObjectField(TEXT("slots"), Slots))
	{
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : (*Slots)->Values)
		{
			const int32 Index = Comp->GetMaterialIndex(FName(*Pair.Key));
			UMaterialInterface* M = LoadObject<UMaterialInterface>(nullptr, *ObjectPath(Pair.Value->AsString()));
			if (Index != INDEX_NONE && M) Comp->SetMaterial(Index, Dress(M));
			else UE_LOG(LogPE, Warning, TEXT("slot '%s' not bound (index %d, material %s)"), *Pair.Key, Index, M ? TEXT("ok") : TEXT("missing"));
		}
	}
	// fermo | cammina | attacco | colpito | morte | mischia
	UAnimSequence* Anim = nullptr;
	const TSharedPtr<FJsonObject>* Anims = nullptr;
	if (Monk->TryGetObjectField(TEXT("anims"), Anims))
	{
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : (*Anims)->Values)
		{
			if (Pair.Key.EndsWith(TEXT("_") + AnimName) || Pair.Key.EndsWith(TEXT("/") + AnimName))
			{
				Anim = LoadObject<UAnimSequence>(nullptr, *ObjectPath(Pair.Key));
				if (Anim) break;
			}
		}
	}
	if (Anim)
	{
		Comp->SetAnimationMode(EAnimationMode::AnimationSingleNode);
		Comp->SetAnimation(Anim);
		Comp->Play(true);
		UE_LOG(LogPE, Log, TEXT("monk animation %s (%.2f s)"), *Anim->GetName(), Anim->GetPlayLength());
	}
	else UE_LOG(LogPE, Warning, TEXT("no animation named '%s'"), *AnimName);
	const FBoxSphereBounds B = Mesh->GetBounds();
	UE_LOG(LogPE, Log, TEXT("monk at %s yaw %.1f, bounds extent %s"), *Location.ToString(), Yaw, *B.BoxExtent.ToString());
	MonkComp = Comp;
	return Comp;
}

void APEGameMode::BuildMonkScene()
{
	UWorld* World = GetWorld();
	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	const TSharedPtr<FJsonObject> Monk = LoadJsonFile(FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("PE"), TEXT("monk.json")));

	// the floor: 12 x 12 m, the material tiled once per 2 m
	if (UStaticMesh* Plane = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane")))
	{
		AStaticMeshActor* Floor = World->SpawnActor<AStaticMeshActor>(FVector::ZeroVector, FRotator::ZeroRotator, Params);
		UStaticMeshComponent* FloorComp = Floor->GetStaticMeshComponent();
		FloorComp->SetMobility(EComponentMobility::Movable);
		FloorComp->SetStaticMesh(Plane);
		Floor->SetActorScale3D(FVector(12.f, 12.f, 1.f));
		FString FloorPath;
		if (Monk.IsValid() && Monk->TryGetStringField(TEXT("floor"), FloorPath))
		{
			if (UMaterialInterface* M = LoadObject<UMaterialInterface>(nullptr, *ObjectPath(FloorPath)))
			{
				UMaterialInstanceDynamic* Dyn = UMaterialInstanceDynamic::Create(M, Floor);
				Dyn->SetScalarParameterValue(TEXT("TileX"), 6.f);
				Dyn->SetScalarParameterValue(TEXT("TileY"), 6.f);
				FloorComp->SetMaterial(0, Dyn);
			}
		}
	}

	FString AnimName = TEXT("fermo");
	FParse::Value(FCommandLine::Get(), TEXT("-pe_anim="), AnimName);
	SpawnMonk(FVector::ZeroVector, 180.f, AnimName);

	// two lights: a warm one beside the camera, a cold weak one behind the monk
	auto AddLight = [&](const FVector& Where, const FLinearColor& Color, float Candelas, float Radius)
	{
		APointLight* L = World->SpawnActor<APointLight>(Where, FRotator::ZeroRotator, Params);
		L->SetMobility(EComponentMobility::Movable);
		UPointLightComponent* C = L->PointLightComponent;
		C->SetIntensityUnits(ELightUnits::Candelas);
		C->SetIntensity(Candelas);
		C->SetLightColor(Color);
		C->SetAttenuationRadius(Radius);
		C->SetSourceRadius(6.f);
		C->SetCastShadows(true);
	};
	AddLight(FVector(-250.f, 120.f, 240.f), FLinearColor(1.f, 0.72f, 0.45f), 900.f, 1500.f);
	AddLight(FVector(250.f, -150.f, 200.f), FLinearColor(0.45f, 0.6f, 1.f), 120.f, 900.f);

	if (AExponentialHeightFog* Fog = World->SpawnActor<AExponentialHeightFog>(FVector(0.f, 0.f, 100.f), FRotator::ZeroRotator, Params))
	{
		UExponentialHeightFogComponent* F = Fog->GetComponent();
		F->SetFogDensity(0.02f);
		F->SetVolumetricFog(true);
		F->SetFogInscatteringColor(FLinearColor(0.12f, 0.1f, 0.09f));
	}
	SpawnPostProcess(ArgFloat(TEXT("-pe_ev="), 0.9f), 1.f, 1.f, 0.675f, 0.4f);

	FVector Pos(-420.f, 0.f, 150.f);
	FRotator Rot(-4.f, 0.f, 0.f);
	float Fov = 70.f;
	FString Spec;
	if (FParse::Value(FCommandLine::Get(), TEXT("-pe_cam="), Spec))
	{
		TArray<FString> P;
		Spec.ParseIntoArray(P, TEXT(","));
		if (P.Num() >= 6)
		{
			Pos = FVector(FCString::Atof(*P[0]), FCString::Atof(*P[1]), FCString::Atof(*P[2]));
			Rot = FRotator(FCString::Atof(*P[3]), FCString::Atof(*P[4]), 0.f);
			Fov = FCString::Atof(*P[5]);
		}
	}
	SpawnCamera(Pos, Rot, Fov);
}

void APEGameMode::BuildRoomScene(const FString& File)
{
	const TSharedPtr<FJsonObject> J = LoadJsonFile(File);
	if (!J.IsValid()) return;
	UWorld* World = GetWorld();
	FActorSpawnParameters Params;
	Params.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;

	const float LightK = ArgFloat(TEXT("-pe_lightk="), 3600.f);    // candelas (or lux) per Unity unit: pi x 1.2 x 2^EV, EV 9.9 being the camera's own
	const float FogK = ArgFloat(TEXT("-pe_fogk="), 10.f);
	const float MonkYawOffset = ArgFloat(TEXT("-pe_monkyaw="), 0.f);
	const TSharedPtr<FJsonObject>* LookPtr = nullptr;
	J->TryGetObjectField(TEXT("look"), LookPtr);
	const TSharedPtr<FJsonObject> Look = LookPtr ? *LookPtr : nullptr;

	// Unity's colours are picture-referred, Unreal's radiance goes through the camera exposure: the materials' emission, the flat
	// ambient and the fog colour are multiplied by K / pi (what a surface of albedo 1 lit by one Unity unit gives back)
	RadianceScale = LightK / PI;
	if (Look.IsValid() && ArgFloat(TEXT("-pe_ambient="), 1.f) > 0.5f && Look->HasField(TEXT("ambSky")))
	{
		const float AmbientIntensity = float(Number(Look, TEXT("ambIntensity"), 1.0));
		auto Amb = [&](const TCHAR* Field)
		{
			const FVector V = ReadVec(Look, Field);
			return FLinearColor(V.X, V.Y, V.Z) * AmbientIntensity;
		};
		AmbientSky = Amb(TEXT("ambSky"));
		AmbientEquator = Amb(TEXT("ambEquator"));
		AmbientGround = Amb(TEXT("ambGround"));
	}
	FString EnvFile;
	if (Look.IsValid() && Look->TryGetStringField(TEXT("env"), EnvFile) && !EnvFile.IsEmpty() && ArgFloat(TEXT("-pe_env="), 1.f) > 0.5f)
		EnvironmentIntensity = float(Number(Look, TEXT("envIntensity"), 1.0));
	if (EnvironmentIntensity > 0.f) EnvironmentTexture = LoadObject<UTexture>(nullptr, TEXT("/Game/Imported/Env/T_Env.T_Env"));    // written by import_scene.py
	bDress = true;
	UE_LOG(LogPE, Log, TEXT("look: radiance scale %.1f, ambient sky %s equator %s ground %s, environment '%s' x %.2f"), RadianceScale,
		*AmbientSky.ToString(), *AmbientEquator.ToString(), *AmbientGround.ToString(), *EnvFile, EnvironmentIntensity);

	// ---- geometry: one instanced component per (mesh, materials, shadow) group, all static so that Lumen can trace them
	struct FGroup
	{
		UStaticMesh* Mesh = nullptr;
		TArray<UMaterialInterface*> Mats;
		bool bShadow = true;
		TArray<FTransform> Xf;
	};
	TMap<FString, FGroup> Groups;
	TMap<FString, UStaticMesh*> MeshCache;
	TMap<FString, UMaterialInterface*> MatCache;
	int32 Missing = 0;
	const TArray<TSharedPtr<FJsonValue>>* Nodes = nullptr;
	if (J->TryGetArrayField(TEXT("nodes"), Nodes))
	{
		for (const TSharedPtr<FJsonValue>& V : *Nodes)
		{
			const TSharedPtr<FJsonObject> N = V->AsObject();
			const FString MeshPath = N->GetStringField(TEXT("mesh"));
			UStaticMesh** MeshSlot = MeshCache.Find(MeshPath);
			if (!MeshSlot) MeshSlot = &MeshCache.Add(MeshPath, LoadObject<UStaticMesh>(nullptr, *ObjectPath(MeshPath)));
			if (!*MeshSlot) { ++Missing; continue; }
			TArray<UMaterialInterface*> Mats;
			FString Key = MeshPath;
			const TArray<TSharedPtr<FJsonValue>>* MatArray = nullptr;
			if (N->TryGetArrayField(TEXT("mats"), MatArray))
			{
				for (const TSharedPtr<FJsonValue>& MV : *MatArray)
				{
					const FString MP = MV->AsString();
					UMaterialInterface* M = nullptr;
					if (!MP.IsEmpty())
					{
						UMaterialInterface** Slot = MatCache.Find(MP);
						if (!Slot) Slot = &MatCache.Add(MP, Dress(LoadObject<UMaterialInterface>(nullptr, *ObjectPath(MP))));
						M = *Slot;
					}
					Mats.Add(M);
					Key += TEXT("|") + MP;
				}
			}
			const bool bShadow = N->HasField(TEXT("shadow")) ? N->GetBoolField(TEXT("shadow")) : true;
			Key += bShadow ? TEXT("|s") : TEXT("|n");
			const FMatrix Matrix(ReadVec(N, TEXT("x")), ReadVec(N, TEXT("y")), ReadVec(N, TEXT("z")), ReadVec(N, TEXT("o")));
			FGroup& G = Groups.FindOrAdd(Key);
			G.Mesh = *MeshSlot;
			G.Mats = Mats;
			G.bShadow = bShadow;
			G.Xf.Add(FTransform(Matrix));
			++NodeCount;
		}
	}
	AActor* SceneActor = World->SpawnActor<AActor>(AActor::StaticClass(), FTransform::Identity, Params);
	USceneComponent* RootComp = NewObject<USceneComponent>(SceneActor, TEXT("Root"));
	RootComp->SetMobility(EComponentMobility::Static);    // the instanced components below are static: they cannot hang from a movable root
	SceneActor->SetRootComponent(RootComp);
	RootComp->RegisterComponent();
	for (TPair<FString, FGroup>& Pair : Groups)
	{
		FGroup& G = Pair.Value;
		UInstancedStaticMeshComponent* C = NewObject<UInstancedStaticMeshComponent>(SceneActor);
		C->SetMobility(EComponentMobility::Static);
		C->SetStaticMesh(G.Mesh);
		for (int32 I = 0; I < G.Mats.Num(); ++I)
			if (G.Mats[I]) C->SetMaterial(I, G.Mats[I]);
		C->SetCastShadow(G.bShadow);
		C->SetupAttachment(RootComp);
		C->AddInstances(G.Xf, false, true);
		C->RegisterComponent();
		InstanceCount += G.Xf.Num();
	}
	UE_LOG(LogPE, Log, TEXT("scene: %d nodes in %d instanced components, %d without a mesh"), NodeCount, Groups.Num(), Missing);

	// ---- lights. Unity's intensity is a multiplier of the colour; K turns it into candelas (points, spots) or lux (sun)
	const TArray<TSharedPtr<FJsonValue>>* Lights = nullptr;
	// -pe_only=<name> / -pe_skip=<name>: keep or drop the lights whose Unity name contains the text (a trailing ! asks for the exact name);
	// a debugging aid to see what each family of lights does to the picture
	FString OnlyName, SkipName;
	FParse::Value(FCommandLine::Get(), TEXT("-pe_only="), OnlyName);
	FParse::Value(FCommandLine::Get(), TEXT("-pe_skip="), SkipName);
	auto NameMatches = [](const FString& Name, FString Pattern)
	{
		if (Pattern.IsEmpty()) return false;
		if (Pattern.EndsWith(TEXT("!"))) { Pattern.LeftChopInline(1); return Name.Equals(Pattern, ESearchCase::IgnoreCase); }
		return Name.Contains(Pattern, ESearchCase::IgnoreCase);
	};
	if (J->TryGetArrayField(TEXT("lights"), Lights))
	{
		for (const TSharedPtr<FJsonValue>& V : *Lights)
		{
			const TSharedPtr<FJsonObject> L = V->AsObject();
			const FString LightName = L->HasField(TEXT("name")) ? L->GetStringField(TEXT("name")) : FString();
			if (!OnlyName.IsEmpty() && !NameMatches(LightName, OnlyName)) continue;
			if (NameMatches(LightName, SkipName)) continue;
			const FString Type = L->GetStringField(TEXT("type"));
			const FVector Pos = ReadVec(L, TEXT("pos"));
			const FVector Dir = ReadVec(L, TEXT("dir"));
			const FLinearColor Color(ReadVec(L, TEXT("color")).X, ReadVec(L, TEXT("color")).Y, ReadVec(L, TEXT("color")).Z);
			const float Intensity = float(Number(L, TEXT("intensity"), 1.0)) * LightK;
			const float Range = float(Number(L, TEXT("range"), 10.0)) * 100.f;
			const bool bShadows = L->HasField(TEXT("shadows")) && L->GetBoolField(TEXT("shadows"));
			if (Type == TEXT("Point"))
			{
				APointLight* A = World->SpawnActor<APointLight>(Pos, FRotator::ZeroRotator, Params);
				A->SetMobility(EComponentMobility::Movable);
				UPointLightComponent* C = A->PointLightComponent;
				C->SetIntensityUnits(ELightUnits::Candelas);
				C->SetIntensity(Intensity);
				C->SetLightColor(Color);
				C->SetAttenuationRadius(Range);
				C->SetSourceRadius(4.f);
				C->SetCastShadows(bShadows);
			}
			else if (Type == TEXT("Spot"))
			{
				ASpotLight* A = World->SpawnActor<ASpotLight>(Pos, Dir.Rotation(), Params);
				A->SetMobility(EComponentMobility::Movable);
				USpotLightComponent* C = Cast<USpotLightComponent>(A->GetLightComponent());
				C->SetWorldRotation(Dir.Rotation());    // the actor's component points down by default (relative pitch -90): aim it ourselves
				C->SetIntensityUnits(ELightUnits::Candelas);
				C->SetIntensity(Intensity);
				C->SetLightColor(Color);
				C->SetAttenuationRadius(Range);
				C->SetSourceRadius(4.f);
				C->SetOuterConeAngle(FMath::Clamp(float(Number(L, TEXT("spot"), 60.0)) * 0.5f, 1.f, 80.f));
				C->SetInnerConeAngle(FMath::Clamp(float(Number(L, TEXT("inner"), 30.0)) * 0.5f, 0.f, 79.f));
				C->SetCastShadows(bShadows);
				if (FParse::Param(FCommandLine::Get(), TEXT("-pe_loglights")))
					UE_LOG(LogPE, Log, TEXT("spot '%s' at %s forward %s: intensity %.0f cd, radius %.0f, cones %.1f/%.1f, visible %d, shadows %d, mobility %d, affects world %d"),
						*LightName, *Pos.ToString(), *C->GetForwardVector().ToString(), C->Intensity, C->AttenuationRadius, C->InnerConeAngle, C->OuterConeAngle,
						C->IsVisible(), C->CastShadows, int32(C->Mobility), C->bAffectsWorld);
			}
			else if (Type == TEXT("Directional"))
			{
				ADirectionalLight* A = World->SpawnActor<ADirectionalLight>(FVector::ZeroVector, Dir.Rotation(), Params);
				A->SetMobility(EComponentMobility::Movable);
				UDirectionalLightComponent* C = Cast<UDirectionalLightComponent>(A->GetLightComponent());
				C->SetWorldRotation(Dir.Rotation());
				C->SetIntensity(Intensity);
				C->SetLightColor(Color);
				C->SetCastShadows(bShadows);
			}
			++LightCount;
		}
	}
	UE_LOG(LogPE, Log, TEXT("scene: %d lights, light K %.0f, fog K %.1f"), LightCount, LightK, FogK);

	// ---- atmosphere and grading, from Unity's fog settings and volume
	if (Look.IsValid() && Look->HasField(TEXT("fog")) && Look->GetBoolField(TEXT("fog")))
	{
		if (AExponentialHeightFog* Fog = World->SpawnActor<AExponentialHeightFog>(FVector(0.f, 0.f, 100.f), FRotator::ZeroRotator, Params))
		{
			UExponentialHeightFogComponent* F = Fog->GetComponent();
			F->SetFogDensity(float(Number(Look, TEXT("fogDensity"), 0.02)) * FogK);
			F->SetFogHeightFalloff(0.0001f);    // Unity's fog does not depend on height
			const FVector FC = ReadVec(Look, TEXT("fogColor"));
			F->SetFogInscatteringColor(FLinearColor(FC.X, FC.Y, FC.Z) * RadianceScale);
			F->SetVolumetricFog(false);
		}
	}
	SpawnGrade(Look);

	// ---- the monk in his pose
	const TSharedPtr<FJsonObject>* MonkPtr = nullptr;
	if (J->TryGetObjectField(TEXT("monk"), MonkPtr))
	{
		FString Anim;
		(*MonkPtr)->TryGetStringField(TEXT("anim"), Anim);
		const FVector Pos = ReadVec(*MonkPtr, TEXT("pos"));
		const float Yaw = float(Number(*MonkPtr, TEXT("yaw"), 0.0)) + MonkYawOffset;
		SpawnMonk(Pos, Yaw, Anim);
		if (PhotoAnimTime < 0.f) PhotoAnimTime = float(Number(*MonkPtr, TEXT("time"), 0.0));
	}

	// ---- the camera, in Unreal's axes already
	const TSharedPtr<FJsonObject>* CamPtr = nullptr;
	if (J->TryGetObjectField(TEXT("camera"), CamPtr))
	{
		const FVector Pos = ReadVec(*CamPtr, TEXT("pos"));
		const FVector Fwd = ReadVec(*CamPtr, TEXT("fwd"));
		const FVector Up = ReadVec(*CamPtr, TEXT("up"));
		SpawnCamera(Pos, FRotationMatrix::MakeFromXZ(Fwd, Up).Rotator(), float(Number(*CamPtr, TEXT("fov"), 74.0)));
	}
}

bool APEGameMode::AssetsReady() const
{
#if WITH_EDITOR
	if (GShaderCompilingManager && GShaderCompilingManager->IsCompiling()) return false;
	if (FAssetCompilingManager::Get().GetNumRemainingAssets() > 0) return false;
#endif
	return true;
}

void APEGameMode::Photo()
{
	// a still pose for the photo (-pe_animtime=seconds); the animation runs again while the frames are timed
	if (MonkComp.IsValid() && PhotoAnimTime >= 0.f)
	{
		MonkComp->SetPosition(PhotoAnimTime, false);
		MonkComp->Stop();
	}
	FScreenshotRequest::RequestScreenshot(PhotoPath, false, false);
	UE_LOG(LogPE, Log, TEXT("photo requested: %s"), *PhotoPath);
}

void APEGameMode::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	++Frame;

	if (!bViewSet)
	{
		APlayerController* PC = UGameplayStatics::GetPlayerController(GetWorld(), 0);
		if (PC && CameraActor.IsValid())
		{
			PC->SetViewTarget(CameraActor.Get());
			bViewSet = true;
		}
	}

	switch (Stage)
	{
	case EStage::Warm:
		// the first run of a scene compiles shaders, distance fields and Nanite data: the photo waits for all of it
		if (!AssetsReady())
		{
			const double Now = FPlatformTime::Seconds();
			if (Now - LastWaitLog > 5.0)
			{
				LastWaitLog = Now;
				UE_LOG(LogPE, Log, TEXT("waiting for shaders and assets (%.0f s)"), Now - StageStart);
			}
			ReadyFrames = 0;
			break;
		}
		++ReadyFrames;
		if (ReadyFrames >= WarmFrames)
		{
			Photo();
			Stage = EStage::Photo;
			StageStart = FPlatformTime::Seconds();
		}
		break;
	case EStage::Photo:
		if (!FScreenshotRequest::IsScreenshotRequested() && Frame >= WarmFrames + 10)
		{
			if (MonkComp.IsValid() && PhotoAnimTime >= 0.f) MonkComp->Play(true);
			Stage = EStage::Measure;
			MeasureLeft = MeasureFrames;
		}
		break;
	case EStage::Measure:
		FrameMs.Add(DeltaSeconds * 1000.f);
		GpuMs.Add(float(FPlatformTime::ToMilliseconds(RHIGetGPUFrameCycles())));
		if (--MeasureLeft <= 0) Stage = EStage::Finish;
		break;
	case EStage::Finish:
		WriteReport();
		Stage = EStage::Done;
		StageStart = FPlatformTime::Seconds();
		break;
	case EStage::Done:
		// give the asynchronous PNG writer time to finish before quitting
		if (IFileManager::Get().FileExists(*PhotoPath) || FPlatformTime::Seconds() - StageStart > 15.0)
		{
			FPlatformMisc::RequestExit(false);
		}
		break;
	}
}

void APEGameMode::WriteReport()
{
	FIntPoint Size(0, 0);
	if (GEngine && GEngine->GameViewport && GEngine->GameViewport->Viewport) Size = GEngine->GameViewport->Viewport->GetSizeXY();
	const float Avg = Average(FrameMs);
	auto Cv = [](const TCHAR* Name) -> FString
	{
		IConsoleVariable* V = IConsoleManager::Get().FindConsoleVariable(Name);
		return V ? V->GetString() : FString(TEXT("?"));
	};
	const FString Settings = FString::Printf(TEXT("gpu=\"%s\" gi=%s reflections=%s virtual_shadow_maps=%s distance_fields=%s screen_percentage=%s%s"),
		*GRHIAdapterName, *Cv(TEXT("r.DynamicGlobalIlluminationMethod")), *Cv(TEXT("r.ReflectionMethod")), *Cv(TEXT("r.Shadow.Virtual.Enable")),
		*Cv(TEXT("r.GenerateMeshDistanceFields")), *Cv(TEXT("r.ScreenPercentage")), bGiOff ? TEXT(" (-pe_gi=0: global illumination and reflections off in the post-process volume)") : TEXT(""));
	const FString Report = FString::Printf(
		TEXT("scene=%s tag=%s resolution=%dx%d frames=%d nodes=%d instances=%d lights=%d\nframe_ms avg=%.2f p50=%.2f p95=%.2f p99=%.2f max=%.2f fps_avg=%.1f\ngpu_ms avg=%.2f p95=%.2f\nsettings: %s\nphoto=%s\n"),
		*SceneName, *Tag, Size.X, Size.Y, FrameMs.Num(), NodeCount, InstanceCount, LightCount, Avg, Percentile(FrameMs, 0.5f), Percentile(FrameMs, 0.95f),
		Percentile(FrameMs, 0.99f), Percentile(FrameMs, 1.f), Avg > 0.f ? 1000.f / Avg : 0.f, Average(GpuMs), Percentile(GpuMs, 0.95f), *Settings, *PhotoPath);
	UE_LOG(LogPE, Log, TEXT("REPORT\n%s"), *Report);
	FFileHelper::SaveStringToFile(Report, *FPaths::Combine(OutDir, FString::Printf(TEXT("pe_%s_report.txt"), *SceneName)));
}

// Prying Eyes - Unreal prototype.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "PEGameMode.generated.h"

class FJsonObject;
class UMaterialInterface;
class UMaterialInstanceDynamic;
class USkeletalMeshComponent;
class UTexture;

/**
 * Prototype director. Everything is driven from the command line, like the Unity "-xxxtest" runs; the run builds the scene,
 * photographs it, reports the frame times and exits. No Blueprints are involved.
 *   -pe_scene=monk|room   monk: a floor, two lights and the monk. room: the scene file written by Python/import_scene.py
 *                         ramp: thirty unlit patches for fitting the tone response; lab: a white plane lit by one light of Unity
 *                         intensity -pe_labi (default 1) from -pe_labd metres (default 3), to check the light scale K (-pe_labspot=1: a spot)
 *   -pe_file=<json>       the room's scene file (Saved/PE/scene_<tag>.json)
 *   -pe_out=<folder>      where the photo and the report go (default <project>/Saved/PE)
 *   -pe_warm=<frames>     frames to wait, once every shader is compiled, before the photo (default 240)
 *   -pe_measure=<frames>  frames timed after the photo (default 300)
 *   -pe_cam=x,y,z,pitch,yaw,fov   camera in centimetres and degrees (monk scene)
 *   -pe_anim=<take>       the monk's animation; -pe_animtime=<seconds> freezes it for the photo
 *   -pe_ev=<stops>        exposure compensation added to the room's own;  -pe_lightk=<n> light intensity per Unity unit
 *   -pe_fogk=<n>          Unreal fog density per Unity density;           -pe_monkyaw=<deg> the monk's facing offset
 *   -pe_ambient=<0|1>     0 leaves out the scene's flat ambient (default 1);  -pe_env=<0|1> the same for its environment reflection
 *   -pe_only=<name> -pe_skip=<name>   keep / drop the lights whose Unity name contains the text (a trailing ! = exact name)
 *   -pe_loglights         log every spot light's settings
 *   -pe_split=<0|1>       0 leaves out the look's Split Toning (default 1)
 *   -pe_gi=<0|1>          0 turns Lumen global illumination and reflections off (a post-process override), as in Unity (default 1)
 *   -pe_ev=<stops> -pe_saturation=<n> -pe_contrast=<n> and the film curve (-pe_toe -pe_slope -pe_shoulder -pe_blackclip -pe_whiteclip, -pe_gamma)
 *                         override the grade, for fitting it against Unity's tone ramp (-pe_scene=ramp)
 *   -pe_res_tag=<text>    label written in the report
 */
UCLASS()
class APEGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	APEGameMode();
	virtual void BeginPlay() override;
	virtual void Tick(float DeltaSeconds) override;

private:
	enum class EStage { Warm, Photo, Measure, Finish, Done };

	void BuildMonkScene();
	void BuildRoomScene(const FString& File);
	void BuildRampScene(const FString& File);
	void BuildLabScene(const FString& File);
	/** The room's exposure, curve and colour grade; bNoEffects leaves out bloom and vignette (the tone ramp measures the curve alone). */
	void SpawnGrade(const TSharedPtr<FJsonObject>& Look, bool bNoEffects = false);
	USkeletalMeshComponent* SpawnMonk(const FVector& Location, float Yaw, const FString& AnimName);
	void SpawnCamera(const FVector& Location, const FRotator& Rotation, float VerticalFov);
	void SpawnPostProcess(float ExposureStops, float Saturation, float Contrast, float Bloom, float Vignette, bool bFitUnity = false);
	void Photo();
	void WriteReport();
	bool AssetsReady() const;
	/** The room's materials get a dynamic instance carrying the radiance scale and the flat ambient (see the master material). */
	UMaterialInterface* Dress(UMaterialInterface* Source);

	/** The game's Split Toning, a post-process material (Python/pe_unity.py build_grade_material) set up by SpawnGrade; null with -pe_split=0. */
	UPROPERTY(Transient)
	TObjectPtr<UMaterialInstanceDynamic> GradeMaterial = nullptr;

	bool bGiOff = false;
	bool bDress = false;
	float RadianceScale = 1.f;
	float EnvironmentIntensity = 0.f;
	UPROPERTY(Transient)
	TObjectPtr<UTexture> EnvironmentTexture = nullptr;
	FLinearColor AmbientSky = FLinearColor::Black, AmbientEquator = FLinearColor::Black, AmbientGround = FLinearColor::Black;
	UPROPERTY(Transient)
	TMap<TObjectPtr<UMaterialInterface>, TObjectPtr<UMaterialInstanceDynamic>> Dressed;

	EStage Stage = EStage::Warm;
	int32 Frame = 0;
	int32 WarmFrames = 240;
	int32 MeasureFrames = 300;
	int32 MeasureLeft = 0;
	int32 ReadyFrames = 0;
	double StageStart = 0.0;
	double LastWaitLog = 0.0;
	FString SceneName;
	FString OutDir;
	FString PhotoPath;
	FString Tag;
	bool bViewSet = false;
	TWeakObjectPtr<AActor> CameraActor;
	TWeakObjectPtr<USkeletalMeshComponent> MonkComp;
	float PhotoAnimTime = -1.f;
	int32 NodeCount = 0;
	int32 InstanceCount = 0;
	int32 LightCount = 0;
	TArray<float> FrameMs;
	TArray<float> GpuMs;
};

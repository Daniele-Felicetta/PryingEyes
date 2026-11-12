#if UNITY_EDITOR && PRYINGEYES_FISHNET
using System;
using System.IO;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Server;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using System.Linq;
using PryingEyes.Proc;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PryingEyes.Multiplayer.FishNetBackend.EditorTools
{
    /// <summary>Explicit menu only. Never imports packages, changes build settings, or starts play mode.</summary>
    internal static class CreateMultiplayerProbe
    {
        const string Menu = "Prying Eyes/Multiplayer/Create isolated probe";

        [MenuItem(Menu, true)]
        static bool Available() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        [MenuItem(Menu)]
        public static void Create()
        {
            if (!Available()) throw new InvalidOperationException("Wait for edit mode and compilation to finish.");
            SampleWorkspaceGuard.AssertCanCreate(Path.GetDirectoryName(Application.dataPath), Application.dataPath);
            string parent = "Assets/_PryingEyes/Multiplayer";
            string destination = AssetDatabase.GenerateUniqueAssetPath(parent + "/GeneratedProbe");
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(destination));
            string folder = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException("Could not create a new probe asset folder.");

            var previous = SceneManager.GetActiveScene();
            // A fresh batch process starts with an unsaved Untitled scene, which cannot accept additive scenes.
            // Only replace that process-owned startup scene; interactive unsaved scenes are preserved.
            bool batchStartup = Application.isBatchMode && SceneManager.sceneCount == 1 && string.IsNullOrEmpty(previous.path);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batchStartup ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                CopyProceduralKit();
                Material bodyMaterial = CreateMaterial(folder);
                NetworkObject avatar = CreateAvatar(folder, bodyMaterial);
                NetworkObject world = CreateWorld(folder);
                // They were authored in a temporary scene, but will be spawned as prefabs, never scene objects.
                world.SetSceneId(NetworkObject.UNSET_SCENEID_VALUE);
                avatar.SetSceneId(NetworkObject.UNSET_SCENEID_VALUE);
                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.AddObject(world); prefabs.AddObject(avatar);
                prefabs.InitializePrefabRange(0);
                AssetDatabase.CreateAsset(prefabs, folder + "/ProbePrefabs.asset");
                // Persist only these newly created prefab assets, never unrelated dirty assets.
                EditorUtility.SetDirty(world.gameObject); EditorUtility.SetDirty(avatar.gameObject);
                PrefabUtility.SavePrefabAsset(world.gameObject);
                PrefabUtility.SavePrefabAsset(avatar.gameObject);

                var root = new GameObject("Multiplayer Probe Bootstrap");
                var transport = root.AddComponent<Tugboat>();
                root.AddComponent<TransportManager>().Transport = transport;
                var server = root.AddComponent<ServerManager>();
                server.SetStartOnHeadless(false);
                var admission = root.AddComponent<FishNetSeatAuthenticator>();
                var manager = root.AddComponent<NetworkManager>();
                manager.SpawnablePrefabs = prefabs;
                SetReference(server, "_authenticator", admission);
                SetBool(manager, "_dontDestroyOnLoad", false);
                var launcher = root.AddComponent<FishNetSessionLauncher>();
                launcher.avatarPrefab = avatar; launcher.worldPrefab = world;
                launcher.inputs = CreateInputs(root.transform);

                var lobby = new GameObject("Lobby Camera").AddComponent<Camera>();
                lobby.clearFlags = CameraClearFlags.SolidColor;
                lobby.backgroundColor = new Color(.035f, .045f, .065f);
                lobby.cullingMask = 0; lobby.depth = -10;
                var menu = root.AddComponent<FishNetProbeMenu>();
                menu.session = launcher; menu.lobbyCamera = lobby;
                var light = new GameObject("Probe Sun").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = .7f;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
                string scenePath = folder + "/MultiplayerProbe.unity";
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new InvalidOperationException("Could not save the new scene.");
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                Debug.Log("Created multiplayer probe at " + scenePath + ". No connection or play mode was started.");
            }
            finally
            {
                if (!batchStartup && previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (!batchStartup) EditorSceneManager.CloseScene(scene, true);
                // Close only our temporary scene. The generated scene must later be opened on its own for testing.
                // Original scenes are never saved or closed; no dirty user assets are saved globally.
            }
        }

        internal static void CopyProceduralKit()
        {
            const string sourcePath = "Assets/_PryingEyes/Scenes/Torre.unity";
            var destination = SceneManager.GetActiveScene();
            // Read the serialized kit already authored by Claude. FillKit would reimport models,
            // regenerate assets and save unrelated dirty assets, so it is intentionally not called.
            var source = EditorSceneManager.OpenPreviewScene(sourcePath);
            try
            {
                var original = source.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ProcKit>(true)).Single();
                if (original.mats.Length == 0 || original.prefabs.Length == 0 ||
                    original.mats.Any(m => m.mat == null) || original.prefabs.Any(p => p.prefab == null))
                    throw new InvalidOperationException("Claude's Torre kit is incomplete; build its assets before creating the multiplayer scene.");
                var copy = new GameObject("Procedural Kit").AddComponent<ProcKit>();
                SceneManager.MoveGameObjectToScene(copy.gameObject, destination);
                copy.mats = (ProcKit.MatRef[])original.mats.Clone();
                copy.prefabs = (ProcKit.PrefabRef[])original.prefabs.Clone();
            }
            finally { EditorSceneManager.ClosePreviewScene(source); }
        }

        static Material CreateMaterial(string folder)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is required for this project probe.");
            var material = new Material(shader);
            material.SetColor("_BaseColor", new Color(.22f, .48f, .66f));
            AssetDatabase.CreateAsset(material, folder + "/AvatarBody.mat");
            return material;
        }

        static NetworkObject CreateAvatar(string folder, Material material)
        {
            var root = new GameObject("Seat Avatar");
            try
            {
                root.AddComponent<NetworkObject>();
                root.AddComponent<FishNetSeatAvatar>();
                var controller = root.GetComponent<CharacterController>();
                controller.height = 1.8f; controller.radius = .35f; controller.center = new Vector3(0, .9f, 0);
                controller.skinWidth = .03f; controller.stepOffset = .25f;
                var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "Body Visual"; visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.up * .9f;
                visual.transform.localScale = new Vector3(.7f, .9f, .7f);
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.GetComponent<Renderer>().sharedMaterial = material;
                var cameraObject = new GameObject("Seat Camera");
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.transform.localPosition = new Vector3(0, 1.62f, 0);
                var camera = cameraObject.AddComponent<Camera>();
                camera.nearClipPlane = .03f; camera.farClipPlane = 200; camera.enabled = false;
                var listener = cameraObject.AddComponent<AudioListener>(); listener.enabled = false;
                var hud = new GameObject("Seat HUD", typeof(RectTransform)).AddComponent<Canvas>();
                hud.transform.SetParent(root.transform, false);
                hud.renderMode = RenderMode.ScreenSpaceCamera; hud.worldCamera = camera;
                hud.planeDistance = .15f; hud.enabled = false;
                var reticle = new GameObject("Reticle", typeof(RectTransform)).AddComponent<Text>();
                reticle.transform.SetParent(hud.transform, false);
                reticle.text = "+"; reticle.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                reticle.fontSize = 24; reticle.alignment = TextAnchor.MiddleCenter;
                reticle.rectTransform.anchorMin = reticle.rectTransform.anchorMax = new Vector2(.5f, .5f);
                reticle.rectTransform.sizeDelta = new Vector2(48, 48);
                var view = root.GetComponent<LocalSeatView>();
                SetReference(view, "seatCamera", camera); SetReference(view, "seatListener", listener); SetReference(view, "seatHud", hud);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/SeatAvatar.prefab");
                if (prefab == null) throw new InvalidOperationException("Could not create avatar prefab.");
                return prefab.GetComponent<NetworkObject>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        static NetworkObject CreateWorld(string folder)
        {
            var root = new GameObject("Procedural Network World");
            try
            {
                root.AddComponent<NetworkObject>();
                root.AddComponent<FishNetWorldState>(); root.AddComponent<FishNetProceduralFloor>();
                root.AddComponent<FishNetTowerObjectives>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/ProceduralWorld.prefab");
                if (prefab == null) throw new InvalidOperationException("Could not create world prefab.");
                return prefab.GetComponent<NetworkObject>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        static LocalSeatInput[] CreateInputs(Transform parent)
        {
            var maps = new[]
            {
                new[] { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.Space },
                new[] { KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Home, KeyCode.End, KeyCode.RightControl },
                new[] { KeyCode.I, KeyCode.K, KeyCode.J, KeyCode.L, KeyCode.U, KeyCode.O, KeyCode.Return },
                new[] { KeyCode.T, KeyCode.G, KeyCode.F, KeyCode.H, KeyCode.R, KeyCode.Y, KeyCode.Backspace }
            };
            var sources = new LocalSeatInput[4];
            var uses = new[] { KeyCode.Tab, KeyCode.RightShift, KeyCode.RightAlt, KeyCode.LeftAlt };
            for (int index = 0; index < sources.Length; index++)
            {
                var source = new GameObject("Seat Input " + (index + 1)).AddComponent<LocalSeatInput>();
                source.transform.SetParent(parent, false); source.seat = index;
                source.forward = maps[index][0]; source.back = maps[index][1]; source.left = maps[index][2]; source.right = maps[index][3];
                source.lookLeft = maps[index][4]; source.lookRight = maps[index][5]; source.fire = maps[index][6];
                source.use = uses[index];
                sources[index] = source;
            }
            return sources;
        }

        static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException("Missing serialized field " + field);
            property.objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static void SetBool(Object target, string field, bool value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException("Missing serialized field " + field);
            property.boolValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif

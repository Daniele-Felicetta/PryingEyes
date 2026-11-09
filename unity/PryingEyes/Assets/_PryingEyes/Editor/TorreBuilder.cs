using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes.EditorTools
{
    /// <summary>Costruisce la scena "Torre" (giocatore, HUD, GameManager, FloorDirector, ambiente) e il kit di materiali e
    /// arredi che il generatore usa a runtime. I piani non stanno nella scena: li genera FloorDirector mentre si gioca.
    /// Nessun bake: la luce e' tutta in tempo reale.</summary>
    public static class TorreBuilder
    {
        public const string ScenePath = "Assets/_PryingEyes/Scenes/Torre.unity";
        const string TexDir = "Assets/_PryingEyes/Textures/Hotel/";
        const string MatDir = "Assets/_PryingEyes/Materials/";
        const string KitDir = "Assets/_PryingEyes/Materials/Kit/";
        const string PropDir = "Assets/_PryingEyes/Prefabs/Props/";
        static readonly string[] ModelDirs = { "Assets/_PryingEyes/Models/Arredi", "Assets/_PryingEyes/Models/Chiesa" };

        public static void Build()
        {
            SetupURP.Configure();
            HotelBuilder.EnsureTagsLayers();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            scene.name = "Torre";

            Environment();
            var kitGo = new GameObject("Kit");
            var kit = kitGo.AddComponent<ProcKit>();
            FillKit(kit);

            HotelBuilder.PlayerAndGame();
            var gm = Object.FindFirstObjectByType<GameManager>();
            var dir = gm.gameObject.AddComponent<FloorDirector>();
            gm.gameObject.AddComponent<LightPool>();
            var player = GameObject.Find("Player");
            player.transform.position = new Vector3(0f, 0.1f, 0f);

            if (!Directory.Exists("Assets/_PryingEyes/Scenes")) Directory.CreateDirectory("Assets/_PryingEyes/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[PryingEyes] Torre scene built OK");
        }

        // ------------------------------------------------------------------ ambiente
        static void Environment()
        {
            // niente luce cotta: ambiente scuro e freddo, nebbia fitta, cielo notturno
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.13f, 0.15f, 0.21f);
            RenderSettings.ambientEquatorColor = new Color(0.095f, 0.095f, 0.11f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.046f, 0.05f);
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "NightSky.mat");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.045f, 0.055f, 0.08f);
            RenderSettings.fogDensity = 0.022f;
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/_PryingEyes/Settings/HallProbe.exr");
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
            }
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_PryingEyes/Settings/HotelVolume.asset");
            var volGo = new GameObject("Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = prof;
        }

        // ------------------------------------------------------------------ materiali
        static Material Existing(string file)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + file + ".mat");
            if (m == null) Debug.LogWarning("[PryingEyes] materiale esistente mancante: " + file + " (lanciare HotelBuilder.Build)");
            return m;
        }

        static Material KitMat(string asset, Shader sh)
        {
            if (!AssetDatabase.IsValidFolder(KitDir.TrimEnd('/')))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/Materials")) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Materials");
                AssetDatabase.CreateFolder("Assets/_PryingEyes/Materials", "Kit");
            }
            string path = KitDir + asset + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else m.shader = sh;
            return m;
        }

        static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + file);

        /// <summary>Materiale PBR da un set di texture del kit (colore, normal map, maschera con metallo/occlusione/lucidita'). tile = metri per ripetizione.</summary>
        static Material Pbr(string asset, string set, Color tint, float tile, float smoothMul = 1f)
        {
            var m = KitMat(asset, Shader.Find("Universal Render Pipeline/Lit"));
            m.color = tint;
            m.SetTexture("_BaseMap", Tex(set + "_base.png"));
            m.SetTextureScale("_BaseMap", Vector2.one / tile);
            var nm = Tex(set + "_normal.png");
            if (nm != null) { m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            var mk = Tex(set + "_mask.png");
            if (mk != null)
            {
                m.SetTexture("_MetallicGlossMap", mk);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetTexture("_OcclusionMap", mk);
                m.EnableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_OcclusionStrength", 1f);
                m.SetFloat("_Smoothness", smoothMul);
                m.SetFloat("_SmoothnessTextureChannel", 0f);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Flat(string asset, Color c, float metallic, float smooth)
        {
            var m = KitMat(asset, Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Emit(string asset, Color baseCol, Color hdr)
        {
            var m = Flat(asset, baseCol, 0f, 0.3f);
            m.SetColor("_EmissionColor", hdr);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material StainedGlass(int i)
        {
            var m = KitMat("vetrata" + i, Shader.Find("Universal Render Pipeline/Lit"));
            var tex = Tex("vetrata" + i + ".png");
            m.SetTexture("_BaseMap", tex);
            m.color = Color.white;
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.75f);
            m.SetTexture("_EmissionMap", tex);
            m.SetColor("_EmissionColor", Color.white * 1.35f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void FillKit(ProcKit kit)
        {
            HotelBuilder.ImportHotelTextures();
            var mats = new List<ProcKit.MatRef>();
            void M(string n, Material m) { if (m != null) mats.Add(new ProcKit.MatRef { name = n, mat = m }); }

            // pavimenti
            M("pav_marmo", Existing("Floor"));
            M("pav_moquette", Existing("Carpet"));
            M("pav_parquet", Pbr("kit_parquet", "parquet", Color.white, 1.2f));
            M("pav_piastrelle", Pbr("kit_piastrelle_pav", "piastrelle_pav", Color.white, 1.2f));
            M("pav_lastre", Pbr("kit_lastre", "pietra_lastre", Color.white, 1.8f));
            M("pav_cripta", Pbr("kit_cripta", "pietra_cripta", new Color(0.85f, 0.85f, 0.85f), 2.0f));
            M("pav_servizio", Pbr("kit_cemento", "cemento", Color.white, 2.0f));
            // muri
            M("mur_damasco", Existing("Wall"));
            M("mur_damasco_verde", Pbr("kit_damasco_verde", "damasco", new Color(0.55f, 0.85f, 0.62f), 0.64f));
            M("mur_damasco_ocra", Pbr("kit_damasco_ocra", "damasco", new Color(1.0f, 0.82f, 0.45f), 0.64f));
            M("mur_intonaco", Pbr("kit_intonaco", "intonaco", new Color(0.72f, 0.70f, 0.66f), 2.0f));
            M("mur_pietra", Pbr("kit_pietra", "pietra_muro", Color.white, 2.4f));
            M("mur_cripta", Pbr("kit_cripta_muro", "pietra_cripta", new Color(0.8f, 0.8f, 0.8f), 2.0f));
            M("mur_piastrelle", Pbr("kit_piastrelle", "piastrelle", Color.white, 1.2f));
            M("mur_boiserie", Existing("Wood"));
            // soffitti e travi
            M("sof_intonaco", Existing("Ceil"));
            M("sof_legno", Pbr("kit_soffitto_legno", "noce", new Color(0.55f, 0.55f, 0.55f), 1.0f));
            M("sof_volta", Pbr("kit_volta", "mattoni_volta", Color.white, 1.6f));
            M("trave", Pbr("kit_trave", "noce", new Color(0.62f, 0.60f, 0.58f), 1.0f));
            M("cornice_legno", Existing("Wood"));
            M("cornice_pietra", Pbr("kit_cornice_pietra", "pietra_muro", new Color(1.05f, 1.0f, 0.95f), 1.2f));
            // vetri e vetrate
            M("vetro_albergo", Existing("Glass"));
            M("vetro_buio", Emit("kit_vetro_buio", new Color(0.01f, 0.012f, 0.02f), new Color(0.02f, 0.03f, 0.05f)));
            for (int i = 0; i < 4; i++) M("vetrata" + i, StainedGlass(i));
            // metalli e luci
            M("acciaio", Existing("Steel"));
            M("ottone", Existing("Gold"));
            M("ferro", Flat("kit_ferro", new Color(0.07f, 0.07f, 0.08f), 0.85f, 0.35f));
            M("lampadina", Existing("Bulb"));
            M("lampadina_spenta", Existing("BulbDead"));
            M("paralume", Existing("LampShade"));
            M("paralume_spento", Flat("kit_paralume_spento", new Color(0.20f, 0.16f, 0.11f), 0f, 0.1f));
            M("fiamma", Emit("fiamma", new Color(0.05f, 0.03f, 0.01f), new Color(5.5f, 2.6f, 0.55f)));
            M("pannello_luce", Existing("CabLamp"));
            M("indicatore", Existing("ElevArrow"));
            M("occhi_buio", Existing("EyeGlow"));
            kit.mats = mats.ToArray();

            // arredi: un prefab per FBX, con i materiali del kit al posto di quelli del ruolo
            var roles = new Dictionary<string, Material>
            {
                { "A_velluto_v", Existing("VelvetG") }, { "A_velluto", Existing("VelvetR") }, { "A_noce", Existing("Wood") },
                { "A_legno", Pbr("kit_legno_scuro", "noce", new Color(0.55f, 0.52f, 0.5f), 0.8f) },
                { "A_ottone", Existing("Gold") }, { "A_oro", Existing("Gold") }, { "A_marmo", Existing("Marble") }, { "A_cuoio", Existing("Leather") },
                { "A_vaso", Existing("Pot") }, { "A_terra", Existing("Soil") }, { "A_foglia", Existing("Leaf") }, { "A_paralume", Existing("LampShade") },
                { "A_vetro", Existing("Glass") }, { "A_scuro", Existing("Dark") }, { "A_fondo", Existing("Felt") }, { "A_quadrante", Existing("ClockFace") },
                { "A_acciaio", Existing("Steel") },
                { "A_pietra", Pbr("kit_pietra_scultura", "pietra_muro", new Color(1.1f, 1.05f, 0.98f), 1.0f, 0.9f) },
                { "A_ferro", Flat("kit_ferro", new Color(0.07f, 0.07f, 0.08f), 0.85f, 0.35f) },
                { "A_fiamma", Emit("fiamma", new Color(0.05f, 0.03f, 0.01f), new Color(5.5f, 2.6f, 0.55f)) },
                { "A_cera", Flat("kit_cera", new Color(0.78f, 0.72f, 0.58f), 0f, 0.35f) },
                { "A_tessuto", Pbr("kit_tessuto", "velluto", new Color(0.34f, 0.08f, 0.10f), 0.3f) },
                { "A_lenzuolo", Pbr("kit_lenzuolo", "velluto", new Color(0.62f, 0.60f, 0.54f), 0.4f) },
                { "A_porcellana", Flat("kit_porcellana", new Color(0.78f, 0.78f, 0.75f), 0f, 0.8f) },
                { "A_osso", Flat("kit_osso", new Color(0.62f, 0.58f, 0.48f), 0f, 0.3f) },
                { "A_canne", Pbr("kit_canne", "ottone", new Color(0.8f, 0.78f, 0.7f), 0.5f) },
                { "A_lacca", Flat("kit_lacca", new Color(0.02f, 0.02f, 0.025f), 0.1f, 0.85f) },
            };
            var prefabs = new List<ProcKit.PrefabRef>();
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/Prefabs")) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Prefabs");
            if (!AssetDatabase.IsValidFolder(PropDir.TrimEnd('/'))) AssetDatabase.CreateFolder("Assets/_PryingEyes/Prefabs", "Props");
            foreach (var dir in ModelDirs)
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                ImportModels(dir);
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = Path.GetFileNameWithoutExtension(path);
                    var pf = MakePropPrefab(name, path, roles);
                    if (pf != null) prefabs.Add(new ProcKit.PrefabRef { name = name, prefab = pf });
                }
            }
            kit.prefabs = prefabs.ToArray();
            AssetDatabase.SaveAssets();
            kit.Index();
            Debug.Log($"[PryingEyes] kit: {mats.Count} materiali, {prefabs.Count} arredi");
        }

        static void ImportModels(string dir)
        {
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
            {
                var mi = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
                if (mi == null) continue;
                if (mi.materialImportMode == ModelImporterMaterialImportMode.ImportViaMaterialDescription
                    && mi.animationType == ModelImporterAnimationType.None && !mi.importCameras) continue;
                mi.globalScale = 1f;
                mi.useFileScale = true;
                mi.bakeAxisConversion = true;
                mi.importCameras = false;
                mi.importLights = false;
                mi.isReadable = false;
                mi.importNormals = ModelImporterNormals.Import;
                mi.importTangents = ModelImporterTangents.CalculateMikk;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                mi.importAnimation = false;
                mi.animationType = ModelImporterAnimationType.None;
                mi.SaveAndReimport();
            }
        }

        static GameObject MakePropPrefab(string name, string fbxPath, Dictionary<string, Material> roles)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) return null;
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] != null ? ms[i].name : "";
                    Material found = null;
                    // il nome piu' lungo che combacia col prefisso: A_velluto_v prima di A_velluto
                    int best = -1;
                    foreach (var kv in roles) if (n.StartsWith(kv.Key) && kv.Key.Length > best && kv.Value != null) { best = kv.Key.Length; found = kv.Value; }
                    if (found != null) ms[i] = found;
                    else if (n.StartsWith("A_")) Debug.LogWarning($"[PryingEyes] ruolo senza materiale: {n} in {name}");
                }
                r.sharedMaterials = ms;
            }
            string path = PropDir + name + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }
    }
}

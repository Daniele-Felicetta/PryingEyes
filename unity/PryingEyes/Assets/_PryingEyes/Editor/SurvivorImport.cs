using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa i quattro sopravvissuti esportati da Blender (modelli/personaggi/genera_personaggi.py -- export): texture,
    /// materiali URP Lit (pelle, vestiti, capelli, occhi), rig Generic senza clip (si anima da codice, SurvivorRig) e prefab in
    /// Resources/Survivors/&lt;id&gt;. Shot() li fotografa a riposo in una scena vuota (non salvata).
    /// Da CLI: Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.SurvivorImport.RunAndShot -quit</summary>
    public static class SurvivorImport
    {
        const string Dir = "Assets/_PryingEyes/Models/Survivors";
        const string TexDir = Dir + "/Textures";
        const string MatDir = Dir + "/Materials";
        const string ResDir = "Assets/_PryingEyes/Resources/Survivors";
        static readonly string[] Groups = { "skin", "outfit", "hair", "eyes" };

        public static void RunAndShot()
        {
            Run();
            Shot();
        }

        static IEnumerable<string> Ids()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".fbx")) yield return Path.GetFileNameWithoutExtension(p);
            }
        }

        public static void Run()
        {
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
                ConfigureTexture((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Dir, "Materials");
            foreach (var id in Ids())
            {
                var mats = SetupMaterials(id);
                SetupModel(id, mats);
                BuildPrefab(id);
            }
            AssetDatabase.SaveAssets();
        }

        static void ConfigureTexture(TextureImporter ti)
        {
            string path = ti.assetPath;
            bool normal = path.Contains("_normal.");
            bool mask = path.Contains("_mask.");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(normal || mask);
            ti.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        /// <summary>Per ogni renderer del prefab: materiali, shader e texture di base (serve a vedere se le texture arrivano ai materiali).</summary>
        public static void Diag()
        {
            AssetDatabase.Refresh();
            foreach (var id in Ids())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ResDir}/{id}.prefab");
                if (prefab == null) { Debug.Log($"[Survivor] {id}: prefab mancante"); continue; }
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        Texture bm = m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                        Debug.Log($"[Survivor] {id}: {r.name} -> {(m != null ? m.name : "null")} shader={(m != null ? m.shader.name : "-")} base={(bm != null ? bm.name : "null")} asset={(m != null ? AssetDatabase.GetAssetPath(m) : "")}");
                    }
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{Dir}/{id}.fbx"))
                    if (o is Material fm) Debug.Log($"[Survivor] {id}: l'FBX contiene il materiale '{fm.name}'");
            }
        }

        static Texture2D Tex(string name, string ext) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.{ext}");

        static Dictionary<string, Material> SetupMaterials(string id)
        {
            var d = new Dictionary<string, Material>();
            foreach (var g in Groups)
            {
                string name = $"Survivor_{id}_{g}";
                string path = $"{MatDir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(m, path);
                }
                m.shader = Shader.Find("Universal Render Pipeline/Lit");
                m.SetTexture("_BaseMap", Tex($"{id}_{g}_base", "jpg"));
                m.SetColor("_BaseColor", Color.white);
                var n = Tex($"{id}_{g}_normal", "jpg");
                if (n != null)
                {
                    m.SetTexture("_BumpMap", n);
                    m.SetFloat("_BumpScale", 1f);
                    m.EnableKeyword("_NORMALMAP");
                }
                m.SetTexture("_MetallicGlossMap", Tex($"{id}_{g}_mask", "png"));
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Metallic", 1f);
                m.SetFloat("_Smoothness", 1f);
                m.SetFloat("_SmoothnessTextureChannel", 0f);
                EditorUtility.SetDirty(m);
                d[g] = m;
            }
            return d;
        }

        static void SetupModel(string id, Dictionary<string, Material> mats)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath($"{Dir}/{id}.fbx");
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            // i materiali dentro l'FBX portano il nome di Blender, spesso col suffisso .001 (ne esisteva gia' uno con quel nome): si
            // rimappa per nome ripulito, con il nome vero come sorgente (la mappa resta nell'importatore: le volte dopo non serve)
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{Dir}/{id}.fbx"))
            {
                if (!(o is Material fm)) continue;
                string clean = System.Text.RegularExpressions.Regex.Replace(fm.name, @"\.\d+$", "");
                foreach (var kv in mats)
                    if (clean == $"{id}_{kv.Key}") mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), fm.name), kv.Value);
            }
            foreach (var kv in mats)
                mi.RemoveRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), $"{id}_{kv.Key}"));      // la mappa sbagliata della prima versione
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.skinWeights = ModelImporterSkinWeights.Standard;     // 4 ossa per vertice, come i pesi fatti in Blender
            mi.optimizeGameObjects = false;                          // le ossa servono come Transform (SurvivorRig)
            mi.importAnimation = false;
            mi.SaveAndReimport();
        }

        static Renderer FindRenderer(GameObject root, string contains)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.Contains(contains)) return r;
            return null;
        }

        static void BuildPrefab(string id)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/Resources")) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Resources");
            if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder("Assets/_PryingEyes/Resources", "Survivors");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{id}.fbx");
            // radice pulita: la radice dell'FBX porta la conversione degli assi di Blender e non va toccata
            var go = new GameObject("Survivor_" + id);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Model";
            inst.transform.SetParent(go.transform, false);
            var eyes = FindRenderer(go, "_eyes");
            Transform hips = null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Hips") hips = t;
            if (eyes != null && hips != null)
            {
                // il volto deve guardare verso +Z: gli occhi stanno davanti al bacino
                float dz = eyes.bounds.center.z - hips.position.z;
                Debug.Log($"[Survivor] {id}: occhi davanti al bacino dz={dz:F3}");
                if (dz < 0f) inst.transform.RotateAround(Vector3.zero, Vector3.up, 180f);
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var b = smr.localBounds;
                b.Expand(1.2f);
                smr.localBounds = b;
                smr.updateWhenOffscreen = false;
            }
            var rends = go.GetComponentsInChildren<Renderer>();
            var bounds = rends[0].bounds;
            foreach (var r in rends) bounds.Encapsulate(r.bounds);
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            Debug.Log($"[Survivor] {id}: bounds={bounds.size} mesh={smrs.Length} ossa={smrs[0].bones.Length}");
            PrefabUtility.SaveAsPrefabAsset(go, $"{ResDir}/{id}.prefab");
            Object.DestroyImmediate(go);
        }

        static Material Flat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        static void Save(RenderTexture rt, string path)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>Foto a riposo di ogni sopravvissuto: davanti, tre quarti, dietro e il volto. hotel_build/survivors/&lt;id&gt;_*.png</summary>
        public static void Shot()
        {
            ShaderUtil.allowAsyncCompilation = false;
            string outDir = TestPaths.Out("survivors");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.20f, 0.20f, 0.22f);
            RenderSettings.skybox = null;
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.color = new Color(1f, 0.93f, 0.84f);
            key.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            key.shadows = LightShadows.Soft;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.13f, 0.13f, 0.14f));
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f; cam.fieldOfView = 32f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
            var ad = cam.GetUniversalAdditionalCameraData();
            ad.renderPostProcessing = false;
            ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rt = new RenderTexture(900, 1100, 24);
            cam.targetTexture = rt;
            foreach (var id in Ids())
            {
                var prefab = Resources.Load<GameObject>("Survivors/" + id);
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab);
                var views = new (string name, Vector3 pos, Vector3 look, float fov)[]
                {
                    ("fronte", new Vector3(0f, 1.0f, 4.6f), new Vector3(0f, 0.9f, 0f), 32f),
                    ("tre_quarti", new Vector3(3.0f, 1.2f, 3.5f), new Vector3(0f, 0.9f, 0f), 32f),
                    ("retro", new Vector3(-1.2f, 1.2f, -4.2f), new Vector3(0f, 0.9f, 0f), 32f),
                    ("volto", new Vector3(0.3f, 1.66f, 1.3f), new Vector3(0f, 1.62f, 0f), 30f),
                };
                bool warm = false;
                foreach (var v in views)
                {
                    camGo.transform.position = v.pos;
                    camGo.transform.LookAt(v.look);
                    cam.fieldOfView = v.fov;
                    if (!warm) { cam.Render(); warm = true; }
                    cam.Render();
                    Save(rt, $"{outDir}/{id}_{v.name}.png");
                }
                Debug.Log("[Survivor] foto: " + id);
                Object.DestroyImmediate(go);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }
    }
}

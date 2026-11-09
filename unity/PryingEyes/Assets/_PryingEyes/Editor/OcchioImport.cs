using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa l'occhio volante da Blender (modelli/occhio/genera_occhio.py -- export): texture, materiali URP,
    /// rig Generic senza clip (le anima EyeRig a codice) e prefab in Resources (lo usa Enemy al posto del drone).
    /// Shot() lo fotografa nella hall. Da CLI:
    /// Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.OcchioImport.RunAndShot -quit</summary>
    public static class OcchioImport
    {
        const string Dir = "Assets/_PryingEyes/Models/Occhio";
        const string TexDir = "Assets/_PryingEyes/Textures/Occhio";
        const string MatDir = Dir + "/Materials";
        const string Fbx = Dir + "/occhio.fbx";
        const string ResDir = "Assets/_PryingEyes/Resources";
        const string PrefabPath = ResDir + "/Occhio.prefab";

        public static void RunAndShot() { Run(); Shot(); }

        public static void Run()
        {
            AssetDatabase.Refresh();
            SetupTextures();
            var mats = SetupMaterials();
            SetupModel(mats);
            BuildPrefab();
        }

        static void SetupTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                bool normal = path.EndsWith("_normal.png"), mask = path.EndsWith("_mask.png");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal && !mask;
                ti.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.wrapMode = path.Contains("carne") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.maxTextureSize = 2048;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

        static Material Asset(string name)
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Dir, "Materials");
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            return m;
        }

        /// <summary>Lit con mappa colore, normali e maschera (R metallico, G occlusione, A lucidita').</summary>
        static Material Pbr(string name, string set, Color tint)
        {
            var m = Asset(name);
            m.SetTexture("_BaseMap", Tex(set + "_base"));
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BumpMap", Tex(set + "_normal"));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            var mask = Tex(set + "_mask");
            m.SetTexture("_MetallicGlossMap", mask);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_OcclusionMap", mask);
            m.EnableKeyword("_OCCLUSIONMAP");
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Dictionary<string, Material> SetupMaterials()
        {
            var iris = Pbr("Occhio_Occhio", "occhio", Color.white);
            iris.SetTexture("_EmissionMap", Tex("occhio_emiss"));
            iris.SetColor("_EmissionColor", Color.white * 0.7f);
            iris.EnableKeyword("_EMISSION");
            iris.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            var flesh = Pbr("Occhio_Carne", "carne", Color.white);
            flesh.SetColor("_EmissionColor", new Color(0.035f, 0.035f, 0.04f));
            flesh.EnableKeyword("_EMISSION");
            flesh.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            var rim = Asset("Occhio_Bordo");
            rim.SetColor("_BaseColor", new Color(0.36f, 0.05f, 0.06f));
            rim.SetFloat("_Metallic", 0f);
            rim.SetFloat("_Smoothness", 0.82f);
            rim.SetColor("_EmissionColor", new Color(0.035f, 0.035f, 0.04f));
            rim.EnableKeyword("_EMISSION");
            rim.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            // cornea: guscio trasparente lucidissimo; "premoltiplicato" tiene pieni i riflessi (luci e finestre) e
            // lascia vedere l'iride sotto
            var cor = Asset("Occhio_Cornea");
            cor.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.06f, 0.07f));
            cor.SetFloat("_Surface", 1f);
            cor.SetFloat("_Blend", 1f);
            cor.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            cor.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            cor.SetFloat("_ZWrite", 0f);
            cor.SetFloat("_Metallic", 0f);
            cor.SetFloat("_Smoothness", 0.98f);
            cor.SetOverrideTag("RenderType", "Transparent");
            cor.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            cor.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            cor.DisableKeyword("_ALPHATEST_ON");
            cor.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(cor);

            AssetDatabase.SaveAssets();
            return new Dictionary<string, Material>
            {
                { "M_occhio", iris }, { "M_carne", flesh }, { "M_bordo", rim }, { "M_cornea", cor },
            };
        }

        static void SetupModel(Dictionary<string, Material> mats)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
            mi.skinWeights = ModelImporterSkinWeights.Standard;
            mi.SaveAndReimport();
        }

        static void BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Resources");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var go = new GameObject("Occhio");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(go.transform, false);
            Transform globe = null, tip = null;
            foreach (var tr in go.GetComponentsInChildren<Transform>())
            {
                if (tr.name == "occhio") globe = tr;
                if (tr.name == "occhio_punta") tip = tr;
            }
            if (globe == null || tip == null) { Debug.LogError("[Occhio] ossa occhio/occhio_punta non trovate"); return; }
            foreach (var an in go.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(an);   // le ossa le muove EyeRig
            // l'iride deve guardare verso +Z (lo sguardo del nemico e' il suo asse Z)
            if ((tip.position - globe.position).z < 0f)
            {
                inst.transform.RotateAround(Vector3.zero, Vector3.up, 180f);
                Debug.Log("[Occhio] ruotato di 180 gradi per guardare verso +Z");
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var b = smr.localBounds;
                b.Expand(1.4f);                    // i tentacoli ondeggiano fuori dai limiti della posa a riposo
                smr.localBounds = b;
                smr.updateWhenOffscreen = false;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var cornea = FindRenderer(go, "cornea");
            if (cornea != null) cornea.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Vector3 center = globe.position;
            Vector3 fwd = (tip.position - globe.position).normalized;
            // collisioni: corpo sferico + testa sull'iride (colpo in testa = colpo nell'occhio)
            var body = go.AddComponent<SphereCollider>();
            body.center = center;
            body.radius = 0.33f;
            var headGo = new GameObject("Iride");
            headGo.transform.SetParent(go.transform, false);
            headGo.transform.position = center + fwd * 0.27f;
            headGo.tag = "Head";
            var hc = headGo.AddComponent<SphereCollider>();
            hc.radius = 0.12f;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false; rb.isKinematic = true;
            go.AddComponent<EyeRig>();
            // l'iride illumina d'ambra i dintorni
            var lg = new GameObject("LuceIride");
            lg.transform.SetParent(go.transform, false);
            lg.transform.position = center + fwd * 0.95f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.62f, 0.2f);
            l.range = 3.2f;
            l.intensity = 0.35f;
            l.shadows = LightShadows.None;
            var rends = go.GetComponentsInChildren<Renderer>();
            var bounds = rends[0].bounds;
            foreach (var r in rends) bounds.Encapsulate(r.bounds);
            Debug.Log($"[Occhio] bounds={bounds.size} ossa={go.GetComponentsInChildren<SkinnedMeshRenderer>()[0].bones.Length} centro={center}");
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Occhio] prefab salvato: {PrefabPath}");
        }

        static Renderer FindRenderer(GameObject root, string name)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.name == name) return r;
            return null;
        }

        /// <summary>Diagnostica: gerarchia, ossa delle palpebre, rotazioni prima e dopo qualche Tick.</summary>
        public static void Diag()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var eye = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var rig = eye.GetComponent<EyeRig>();
            rig.Init();
            var sb = new System.Text.StringBuilder();
            foreach (var smr in eye.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var names = new List<string>();
                foreach (var b in smr.bones) names.Add(b != null ? b.name : "null");
                sb.AppendLine($"SMR {smr.name} verts={smr.sharedMesh.vertexCount} bones={smr.bones.Length} [{string.Join(",", names.GetRange(0, Mathf.Min(8, names.Count)))}] root={(smr.rootBone ? smr.rootBone.name : "null")} mats={smr.sharedMaterials.Length} bounds={smr.localBounds.size}");
            }
            foreach (var t in eye.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("palpebra") || t.name == "occhio" || t.name == "radice")
                    sb.AppendLine($"T {t.name} parent={t.parent.name} pos={t.position} rot={t.rotation.eulerAngles}");
            Debug.Log("[Occhio] diag:\n" + sb);
            rig.dead = true;
            for (int i = 0; i < 90; i++) rig.Tick(1f / 60f);
            var sb2 = new System.Text.StringBuilder();
            foreach (var t in eye.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("palpebra")) sb2.AppendLine($"T {t.name} rot={t.rotation.eulerAngles}");
            Debug.Log("[Occhio] dopo il tick (morto):\n" + sb2);
        }

        /// <summary>Mette l'occhio nella hall e lo fotografa da vari lati e con varie palpebre. La scena non viene salvata.</summary>
        public static void Shot()
        {
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene("Assets/_PryingEyes/Scenes/Hotel.unity");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var eye = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            eye.transform.position = new Vector3(0f, 1.6f, 16.4f);
            eye.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            var rig = eye.GetComponent<EyeRig>();
            rig.Init();

            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            var main = GameObject.Find("MainCamera");
            var mainCam = main != null ? main.GetComponent<Camera>() : null;
            if (mainCam != null)
            {
                cam.CopyFrom(mainCam);
                var src = mainCam.GetUniversalAdditionalCameraData();
                var dst = cam.GetUniversalAdditionalCameraData();
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.antialiasingQuality = src.antialiasingQuality;
            }
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 200f;
            cam.fieldOfView = 50f;
            var rt = new RenderTexture(960, 720, 24);
            cam.targetTexture = rt;
            var key = new GameObject("LuceFoto").AddComponent<Light>();
            key.type = LightType.Spot;
            key.spotAngle = 60f;
            key.range = 12f;
            key.intensity = 5f;
            key.color = new Color(1f, 0.85f, 0.7f);
            key.transform.position = new Vector3(-1.8f, 3.2f, 19.0f);
            key.transform.LookAt(new Vector3(0f, 1.6f, 16.4f));

            // lo sguardo punta alla camera (come in gioco punta alla testa del giocatore)
            rig.lookAt = camGo.transform;
            foreach (var smr in eye.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
            var shots = new (string name, Vector3 pos, float yaw, float dead, float stare, float hit)[]
            {
                ("fronte", new Vector3(0f, 1.6f, 18.3f), 0f, 0f, 0f, 0f),
                ("tre_quarti", new Vector3(-1.6f, 1.9f, 18.0f), 0f, 0f, 0f, 0f),
                ("fissa", new Vector3(0.8f, 1.7f, 18.4f), 0f, 0f, 1f, 0f),
                ("colpito", new Vector3(-0.8f, 1.7f, 18.4f), 0f, 0f, 0f, 1f),
                ("retro", new Vector3(1.2f, 2.2f, 14.2f), 0f, 0f, 0f, 0f),
                ("morto", new Vector3(0.6f, 1.9f, 18.0f), 0f, 1f, 0f, 0f),
            };
            bool warm = false;
            foreach (var s in shots)
            {
                camGo.transform.position = s.pos;
                camGo.transform.LookAt(eye.transform.position + Vector3.up * 0.1f);
                rig.dead = s.dead > 0f;
                if (s.stare > 0f) rig.Stare(5f);
                if (s.hit > 0f) rig.Hit();
                for (int i = 0; i < 90; i++) rig.Tick(1f / 60f);       // lascia assestare saccadi, palpebre e tentacoli
                if (s.hit > 0f) { rig.Hit(); rig.Tick(1f / 60f); rig.Tick(1f / 60f); }
                if (!warm) { cam.Render(); warm = true; }
                cam.Render();
                cam.Render();
                var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                System.IO.File.WriteAllBytes("C:/Users/danie/Desktop/prying-eyes/hotel_build/shot_occhio_" + s.name + ".png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[Occhio] foto salvata: " + s.name);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }
    }
}

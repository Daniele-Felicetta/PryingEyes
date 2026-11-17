using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa la medusa volante da Blender (modelli/medusa/genera_medusa.py -- export): texture, materiali URP (gel
    /// traslucido a due passate, velo a merletto), rig Generic senza clip (lo anima JellyRig a codice) e prefab in Resources.
    /// Shot() la fotografa nella hall. Da CLI:
    /// Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.MedusaImport.RunAndShot -quit</summary>
    public static class MedusaImport
    {
        const string Dir = "Assets/_PryingEyes/Models/Medusa";
        const string TexDir = "Assets/_PryingEyes/Textures/Medusa";
        const string MatDir = Dir + "/Materials";
        const string Fbx = Dir + "/medusa.fbx";
        const string ResDir = "Assets/_PryingEyes/Resources";
        const string PrefabPath = ResDir + "/Medusa.prefab";

        public static void RunAndShot() { Run(); Shot(); }

        public static void Run()
        {
            AssetDatabase.Refresh();
            SetupTextures();
            var mats = SetupMaterials();
            SetupModel(mats);
            BuildPrefab();
        }

        // ---------------------------------------------------------------- texture
        static readonly HashSet<string> WithAlpha = new HashSet<string> { "gel_base", "cappa_base", "velo_base" };

        static void SetupTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                bool normal = file.EndsWith("_normal"), mask = file.EndsWith("_mask"), emiss = file.EndsWith("_emiss");
                string set = file.Substring(0, file.LastIndexOf('_'));
                bool alpha = WithAlpha.Contains(file);
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal && !mask;
                ti.alphaSource = mask || alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = alpha;
                bool disc = set == "gel" || set == "cappa" || set == "interno";      // UV a disco: niente ripetizione
                ti.wrapMode = disc ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.maxTextureSize = (set == "gel" || set == "cappa") && !normal && !mask ? 2048 : 1024;
                if (emiss) ti.maxTextureSize = 512;
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
        static Material Pbr(string name, string set, Color tint, float smoothness = 1f)
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
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Emit(Material m, string tex, Color color)
        {
            m.SetTexture("_EmissionMap", Tex(tex));
            m.SetColor("_EmissionColor", color);
            m.EnableKeyword("_EMISSION");
        }

        /// <summary>Passa il materiale a trasparente "premoltiplicato" (come la cornea dell'occhio): i riflessi restano pieni e
        /// l'emissione si somma intera, cosi' gli organi luminosi traspaiono sotto il gel. cull: 0 = due facce, 1 = scarta le
        /// facce davanti, 2 = scarta quelle dietro.</summary>
        static Material Translucent(Material m, int cull, int queue)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", cull);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = queue;
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material gelBack, gelFront;

        static Dictionary<string, Material> SetupMaterials()
        {
            // campana: due passate dello stesso guscio (prima le facce di dietro, poi quelle davanti) per non avere l'ordine dei triangoli
            gelBack = Translucent(Pbr("Medusa_GelDietro", "gel", new Color(0.92f, 0.86f, 0.80f, 1f), 0.9f), 1, 3000);
            Emit(gelBack, "gel_emiss", new Color(0.60f, 1f, 0.45f) * 0.30f);          // JellyRig.Glow riscrive questi valori a ogni passo
            gelFront = Translucent(Pbr("Medusa_Gel", "gel", Color.white, 0.95f), 2, 3001);
            Emit(gelFront, "gel_emiss", new Color(0.60f, 1f, 0.45f) * 0.55f);

            var cappa = Translucent(Pbr("Medusa_Cappa", "cappa", Color.white, 0.8f), 0, 3002);
            var velo = Translucent(Pbr("Medusa_Velo", "velo", Color.white, 0.85f), 0, 3003);

            var inner = Pbr("Medusa_Interno", "interno", Color.white, 0.9f);
            Emit(inner, "interno_emiss", new Color(0.55f, 1f, 0.18f) * 1.1f);
            inner.SetFloat("_Cull", 0f);                       // si vede dal di sotto e, attraverso il gel, dal di fuori

            var flesh = Pbr("Medusa_Carne", "carne", Color.white, 1f);
            flesh.SetColor("_EmissionColor", new Color(0.04f, 0.02f, 0.02f));
            flesh.EnableKeyword("_EMISSION");

            var teeth = Pbr("Medusa_Denti", "dente", Color.white, 0.85f);

            var glow = Pbr("Medusa_Luce", "luce", Color.white, 0.7f);
            Emit(glow, "luce_emiss", new Color(0.62f, 1f, 0.16f) * 1.8f);

            AssetDatabase.SaveAssets();
            return new Dictionary<string, Material>
            {
                { "M_gel", gelFront }, { "M_cappa", cappa }, { "M_velo", velo }, { "M_carne", flesh },
                { "M_interno", inner }, { "M_denti", teeth }, { "M_luce", glow },
            };
        }

        // ---------------------------------------------------------------- modello
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
            mi.generateMeshLods = false;                  // niente LOD automatici: semplificano male funi sottili, merletto e denti (66 mila triangoli bastano)
            mi.SaveAndReimport();
        }

        static Transform FindBone(GameObject root, string name)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (tr.name == name) return tr;
            return null;
        }

        static void BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Resources");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var go = new GameObject("Medusa");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(go.transform, false);
            Transform body = FindBone(go, "corpo"), front = FindBone(go, "fronte"), mouth = FindBone(go, "bocca");
            if (body == null || front == null || mouth == null) { Debug.LogError("[Medusa] ossa corpo/fronte/bocca non trovate"); return; }
            foreach (var an in go.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(an);     // le ossa le muove JellyRig
            // il davanti (la fronte) deve guardare verso +Z (la direzione del nemico e' il suo asse Z)
            if ((front.position - body.position).z < 0f)
            {
                inst.transform.RotateAround(Vector3.zero, Vector3.up, 180f);
                Debug.Log("[Medusa] ruotata di 180 gradi per guardare verso +Z");
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var b = smr.localBounds;
                b.Expand(1.4f);                              // tentacoli e drappi ondeggiano fuori dai limiti della posa a riposo
                smr.localBounds = b;
                smr.updateWhenOffscreen = false;
                smr.receiveShadows = true;
                bool glass = smr.name == "m_gel" || smr.name == "m_cappa" || smr.name == "m_velo";
                smr.shadowCastingMode = glass ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                if (smr.name == "m_gel") smr.sharedMaterials = new[] { gelBack, gelFront };
            }
            var gel = FindBone(go, "m_gel").GetComponent<SkinnedMeshRenderer>();
            Vector3 center = body.position + Vector3.up * 0.12f;
            // collisioni: campana sferica + testa sulla bocca (colpo in bocca = punto debole)
            var bodyCol = go.AddComponent<SphereCollider>();
            bodyCol.center = center;
            bodyCol.radius = 0.52f;
            var headGo = new GameObject("Bocca");
            headGo.transform.SetParent(go.transform, false);
            headGo.transform.position = mouth.position + Vector3.down * 0.06f;
            headGo.tag = "Head";
            var hc = headGo.AddComponent<SphereCollider>();
            hc.radius = 0.17f;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false; rb.isKinematic = true;
            // gli organi illuminano di verde acido i dintorni
            var lg = new GameObject("LuceOrgani");
            lg.transform.SetParent(go.transform, false);
            lg.transform.position = center;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.55f, 1f, 0.2f);
            l.range = 4.6f;
            l.intensity = 0.9f;
            l.shadows = LightShadows.None;
            go.AddComponent<JellyRig>();
            var rends = go.GetComponentsInChildren<Renderer>();
            var bounds = rends[0].bounds;
            foreach (var r in rends) bounds.Encapsulate(r.bounds);
            Debug.Log($"[Medusa] bounds={bounds.size} centro={center} ossa={gel.bones.Length}");
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Medusa] prefab salvato: {PrefabPath}");
        }

        /// <summary>Diagnostica: renderer, vertici, ossa e posizioni dei punti notevoli.</summary>
        public static void Diag()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var sb = new System.Text.StringBuilder();
            foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var names = new List<string>();
                foreach (var b in smr.bones) names.Add(b != null ? b.name : "null");
                sb.AppendLine($"SMR {smr.name} verts={smr.sharedMesh.vertexCount} tris={smr.sharedMesh.triangles.Length / 3} lods={smr.sharedMesh.lodCount} bones={smr.bones.Length} mats={smr.sharedMaterials.Length} bounds={smr.localBounds.size}");
            }
            foreach (var t in obj.GetComponentsInChildren<Transform>())
                if (t.name == "radice" || t.name == "corpo" || t.name == "fronte" || t.name == "bocca" || t.name == "tent_0_0" || t.name == "tent_0_7" || t.name == "campana_0_2")
                    sb.AppendLine($"T {t.name} parent={t.parent.name} pos={t.position} rot={t.rotation.eulerAngles}");
            Debug.Log("[Medusa] diag:\n" + sb);
            Object.DestroyImmediate(obj);
        }

        /// <summary>Mette la medusa nella hall e la fotografa in sei assetti (riposo, raccolta, affondo, recupero, colpita, morta) da
        /// due lati. L'animazione gira a passi di 1/60 s come in gioco; gli effetti (Fx) non si vedono, servono i test del player
        /// (-jellytest -jellyshots). La scena non viene salvata.</summary>
        public static void Shot()
        {
            ShaderUtil.allowAsyncCompilation = false;
            JellyFx.Enabled = false;                          // Fx vive solo in play mode
            EditorSceneManager.OpenScene("Assets/_PryingEyes/Scenes/Hotel.unity");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var jelly = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            jelly.transform.position = new Vector3(0f, 1.9f, 16.4f);
            jelly.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            var rig = jelly.GetComponent<JellyRig>();

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
            key.spotAngle = 70f;
            key.range = 14f;
            key.intensity = 6f;
            key.color = new Color(1f, 0.85f, 0.7f);
            key.transform.position = new Vector3(-2.2f, 3.8f, 19.4f);
            key.transform.LookAt(new Vector3(0f, 1.6f, 16.4f));

            foreach (var smr in jelly.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
            var views = new (string name, Vector3 pos)[]
            {
                ("fronte", new Vector3(-1.4f, 1.5f, 19.0f)),
                ("lato", new Vector3(3.4f, 1.9f, 16.4f)),
            };
            // (nome, secondi di quiete prima, tempo di affondo da raggiungere (-1 = nessuno), colpita, morta)
            var poses = new (string name, float idle, float flight, bool hit, bool die)[]
            {
                ("riposo", 2.0f, -1f, false, false),
                ("raccolta", 1.0f, 0.52f, false, false),
                ("affondo", 1.0f, 0.70f, false, false),
                ("recupero", 1.0f, 0.95f, false, false),
                ("colpita", 1.0f, -1f, true, false),
                ("morta", 1.0f, -1f, false, true),
            };
            const float dt = 1f / 60f;
            bool warm = false;
            foreach (var pose in poses)
            {
                rig.Init();
                for (float t = 0f; t < pose.idle; t += dt) { rig.Drive(1f, 0.5f, true, false, -1f); rig.Tick(dt); }
                if (pose.flight >= 0f)
                    for (float t = 0f; t <= pose.flight + 1e-4f; t += dt) { rig.Drive(1f, 0.5f, true, true, t); rig.Tick(dt); }
                if (pose.hit) { rig.Hit(jelly.transform.position + new Vector3(0.2f, 0.3f, 0.5f)); for (int i = 0; i < 6; i++) { rig.Drive(1f, 0.5f, true, false, -1f); rig.Tick(dt); } }
                if (pose.die) { rig.Die(); for (int i = 0; i < 150; i++) { rig.Drive(1f, 0f, false, false, -1f); rig.Tick(dt); } }
                foreach (var v in views)
                {
                    camGo.transform.position = v.pos;
                    camGo.transform.LookAt(jelly.transform.position + Vector3.down * 0.5f);
                    if (!warm) { cam.Render(); warm = true; }
                    cam.Render();
                    cam.Render();
                    var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    System.IO.File.WriteAllBytes(TestPaths.Out("shot_medusa_" + pose.name + "_" + v.name + ".png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                Debug.Log("[Medusa] foto salvate: " + pose.name);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            JellyFx.Enabled = true;
        }
    }
}

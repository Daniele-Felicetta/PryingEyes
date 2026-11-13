using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa i quattro nemici dei piani a tema (modelli/temi): Maschera e Direttore (teatro), Infermiere e Primario
    /// (sanatorio). Sono il monaco con un altro vestito: stesso FBX, stesso scheletro, stesse clip, stesso Animator
    /// (Models/Monaco). Cambiano la stoffa (texture dipinte da genera_costumi_tex.py, normal map del monaco), gli accessori
    /// rigidi (modelli/temi/genera_accessori.py -> accessori_temi.fbx, attaccati alle ossa 'bastone', 'testa', 'petto'), le
    /// luci e la scala. Prefab in Resources (li usa Enemy.BuildThemed). Shot() li fotografa nella hall. Da CLI:
    /// Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.NemiciTemiImport.RunAndShot -quit</summary>
    public static class NemiciTemiImport
    {
        const string Dir = "Assets/_PryingEyes/Models/Temi";
        const string TexDir = Dir + "/Textures";
        const string MatDir = Dir + "/Materials";
        const string AccFbx = Dir + "/accessori_temi.fbx";
        const string MonkDir = "Assets/_PryingEyes/Models/Monaco";
        const string MonkFbx = MonkDir + "/monaco.fbx";
        const string MonkCtrl = MonkDir + "/Monaco.controller";
        const string MonkTex = MonkDir + "/Textures";
        const string ResDir = "Assets/_PryingEyes/Resources";

        readonly struct Acc
        {
            public readonly string name, bone;
            public Acc(string name, string bone) { this.name = name; this.bone = bone; }
        }

        class Look
        {
            public string name, costume;
            public Vector3 scale;
            public Color tint;
            public Acc[] accessories;
            // luce attaccata all'osso: centrata sulla mesh 'lightMesh' (vuoto = nessuna luce)
            public string lightMesh, lightBone;
            public Color lightColor;
            public float lightRange, lightIntensity, spotAngle, lightForward;
        }

        static readonly Look[] Looks =
        {
            new Look
            {
                name = "Maschera", costume = "maschera", scale = new Vector3(0.97f, 1.06f, 0.97f), tint = Color.white,
                accessories = new[] { new Acc("acc_lanterna", "bastone"), new Acc("acc_maschera", "testa") },
                lightMesh = "acc_lanterna_luce_calda", lightBone = "bastone", lightColor = new Color(1f, 0.60f, 0.25f),
                lightRange = 6f, lightIntensity = 1.1f,
            },
            new Look
            {
                name = "Direttore", costume = "direttore", scale = new Vector3(1.12f, 1.18f, 1.12f), tint = Color.white,
                accessories = new[] { new Acc("acc_diapason", "bastone"), new Acc("acc_cilindro", "testa"), new Acc("acc_farfallino", "petto") },
            },
            new Look
            {
                name = "Infermiere", costume = "infermiere", scale = Vector3.one, tint = Color.white,
                accessories = new[] { new Acc("acc_flebo", "bastone") },
                lightMesh = "acc_flebo_luce_verde", lightBone = "bastone", lightColor = new Color(0.45f, 1f, 0.35f),
                lightRange = 4f, lightIntensity = 0.5f,
            },
            new Look
            {
                name = "Primario", costume = "primario", scale = new Vector3(1.30f, 1.25f, 1.30f), tint = Color.white,
                accessories = new[] { new Acc("acc_scialitica", "testa"), new Acc("acc_specchio", "testa"), new Acc("acc_uncino", "bastone") },
                lightMesh = "acc_scialitica_luce_fredda", lightBone = "testa", lightColor = new Color(0.85f, 0.95f, 1f),
                lightRange = 9f, lightIntensity = 2.4f, spotAngle = 95f, lightForward = 0.25f,
            },
        };

        public static void RunAndShot() { Run(); Shot(); }

        public static void Run()
        {
            AssetDatabase.Refresh();
            SetupTextures();
            var acc = SetupAccessoryMaterials();
            SetupAccessoryModel(acc);
            foreach (var look in Looks) BuildPrefab(look);
        }

        // ------------------------------------------------------------------ texture e materiali
        static void SetupTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                bool mask = path.EndsWith("_mask_unity.png");
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = !mask;
                ti.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.maxTextureSize = mask ? 1024 : 2048;      // la maschera e' fatta di zone larghe: un quarto dei pixel bastano
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
        }

        static Texture2D Tex(string dir, string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}.png");

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

        /// <summary>Stoffa di un costume: come Monaco_Stoffa (normal map e dettaglio della tela sono quelli del monaco), con la
        /// base e la maschera dipinte per il costume.</summary>
        static Material Cloth(string name, string costume, Color tint)
        {
            var m = Asset(name);
            m.SetTexture("_BaseMap", Tex(TexDir, costume + "_stoffa_base"));
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BumpMap", Tex(MonkTex, "monaco_stoffa_normal"));
            m.SetFloat("_BumpScale", 1.28f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", Tex(TexDir, costume + "_stoffa_mask_unity"));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 0.67f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            var detail = Tex(MonkTex, "monaco_tela_detail");
            var dnorm = Tex(MonkTex, "monaco_tela_detail_normal");
            if (detail != null && dnorm != null)
            {
                m.SetTexture("_DetailAlbedoMap", detail);
                m.SetTextureScale("_DetailAlbedoMap", new Vector2(4f, 4f));
                m.SetTexture("_DetailNormalMap", dnorm);
                m.SetFloat("_DetailAlbedoMapScale", 1f);
                m.SetFloat("_DetailNormalMapScale", 0.52f);
                m.EnableKeyword("_DETAIL_MULX2");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Flat(string name, Color color, float metallic, float smoothness, Color? emission = null)
        {
            var m = Asset(name);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                m.SetColor("_EmissionColor", emission.Value);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Un materiale per ruolo ('A_*' nell'FBX). I nomi delle luci contengono "_Luce": l'Enemy li lascia fuori dal lampo del colpo.</summary>
        static Dictionary<string, Material> SetupAccessoryMaterials()
        {
            var d = new Dictionary<string, Material>
            {
                { "A_acciaio", Flat("Temi_Acciaio", new Color(0.62f, 0.64f, 0.66f), 1f, 0.72f) },
                { "A_legno_scuro", Flat("Temi_Ebano", new Color(0.075f, 0.045f, 0.03f), 0f, 0.52f) },
                { "A_ottone", Flat("Temi_Ottone", new Color(0.72f, 0.52f, 0.17f), 1f, 0.68f) },
                { "A_oro", Flat("Temi_Oro", new Color(0.80f, 0.60f, 0.22f), 1f, 0.78f) },
                { "A_gomma", Flat("Temi_Gomma", new Color(0.03f, 0.03f, 0.035f), 0f, 0.35f) },
                { "A_seta_nera", Flat("Temi_Seta", new Color(0.02f, 0.02f, 0.026f), 0f, 0.78f) },
                { "A_nastro", Flat("Temi_Nastro", new Color(0.30f, 0.03f, 0.045f), 0f, 0.62f) },
                { "A_porcellana", Flat("Temi_Porcellana", new Color(0.82f, 0.80f, 0.74f), 0f, 0.88f) },
                { "A_stoffa_bianca", Flat("Temi_Bianco", new Color(0.80f, 0.80f, 0.76f), 0f, 0.15f) },
                { "A_plastica", Flat("Temi_Plastica", new Color(0.78f, 0.84f, 0.78f), 0f, 0.72f) },
                { "A_specchio", Flat("Temi_Specchio", new Color(0.55f, 0.58f, 0.60f), 1f, 0.96f) },
                { "A_cuoio", Flat("Temi_Cuoio", new Color(0.30f, 0.15f, 0.07f), 0f, 0.42f) },
                { "A_luce_calda", Flat("Temi_LuceCalda", new Color(0.5f, 0.3f, 0.1f), 0f, 0.4f, new Color(1f, 0.55f, 0.18f) * 2.2f) },
                { "A_luce_fredda", Flat("Temi_LuceFredda", new Color(0.8f, 0.9f, 0.95f), 0f, 0.4f, new Color(0.8f, 0.95f, 1f) * 3f) },
                { "A_luce_verde", Flat("Temi_LuceVerde", new Color(0.06f, 0.22f, 0.05f), 0f, 0.5f, new Color(0.35f, 1f, 0.2f) * 1.1f) },
            };
            AssetDatabase.SaveAssets();
            return d;
        }

        static void SetupAccessoryModel(Dictionary<string, Material> mats)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(AccFbx);
            if (mi == null) { Debug.LogError("[Temi] manca " + AccFbx + " (modelli/temi/export/accessori_temi.fbx)"); return; }
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;                  // come il monaco: stessi assi, gli accessori cadono dove li ho disegnati
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.generateMeshLods = true;                    // LOD automatici, come il monaco (vedi MonacoImport)
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ prefab
        static Transform FindBone(GameObject root, string name)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (tr.name == name && tr.GetComponent<Renderer>() == null) return tr;       // l'osso, non la mesh omonima
            return null;
        }

        static Renderer FindRenderer(GameObject root, string name)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name == name) return r;
            return null;
        }

        static void BuildPrefab(Look look)
        {
            if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Resources");
            var monk = AssetDatabase.LoadAssetAtPath<GameObject>(MonkFbx);
            var accModel = AssetDatabase.LoadAssetAtPath<GameObject>(AccFbx);
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MonkCtrl);
            if (monk == null || accModel == null || ctrl == null) { Debug.LogError("[Temi] mancano monaco.fbx, accessori_temi.fbx o Monaco.controller"); return; }

            var go = new GameObject(look.name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(monk);
            inst.transform.SetParent(go.transform, false);
            Transform eye = FindBone(go, "occhio"), head = FindBone(go, "testa"), orb = null;
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) if (tr.name == "occhietto") orb = tr;
            var eyeMesh = FindRenderer(go, "occhio");
            if (eyeMesh != null && eyeMesh.bounds.center.z < 0f) inst.transform.RotateAround(Vector3.zero, Vector3.up, 180f);   // deve guardare verso +Z

            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var b = smr.localBounds;
                b.Expand(1.6f);                         // la morte lo porta fino a 1.5 m avanti: niente sparizioni
                smr.localBounds = b;
            }

            // il bastone di legno del monaco sparisce (le aste degli accessori hanno lo stesso raggio di presa)
            var staff = FindRenderer(go, "legno");
            if (staff != null) staff.gameObject.SetActive(false);
            else Debug.LogWarning("[Temi] mesh 'legno' non trovata");

            // costume: la stoffa prende le texture dipinte
            var cloth = Cloth("Temi_" + look.name + "_Stoffa", look.costume, look.tint);
            var clothIn = Cloth("Temi_" + look.name + "_StoffaInterno", look.costume, new Color(look.tint.r * 0.5f, look.tint.g * 0.5f, look.tint.b * 0.5f, 1f));
            int swapped = 0;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var ms = smr.sharedMaterials;
                bool any = false;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i] == null) continue;
                    if (ms[i].name == "Monaco_StoffaInterno") { ms[i] = clothIn; any = true; swapped++; }
                    else if (ms[i].name == "Monaco_Stoffa" || ms[i].name == "Monaco_Fibre") { ms[i] = cloth; any = true; swapped++; }
                }
                if (any) smr.sharedMaterials = ms;
            }
            if (swapped == 0) Debug.LogError("[Temi] nessuna stoffa sostituita in " + look.name);

            // accessori: l'FBX intero in posa, sciolto dal prefab, poi ogni pezzo all'osso giusto (la posizione nel mondo resta)
            var accInst = (GameObject)PrefabUtility.InstantiatePrefab(accModel);
            accInst.transform.SetParent(go.transform, false);
            accInst.transform.localPosition = inst.transform.localPosition;
            accInst.transform.localRotation = inst.transform.localRotation;
            accInst.transform.localScale = inst.transform.localScale;
            PrefabUtility.UnpackPrefabInstance(accInst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var a in look.accessories)
            {
                var piece = accInst.transform.Find(a.name);
                var bone = FindBone(inst, a.bone);
                if (piece == null || bone == null) { Debug.LogError($"[Temi] {look.name}: manca {(piece == null ? a.name : a.bone)}"); continue; }
                piece.SetParent(bone, true);
                var pr = piece.GetComponentInChildren<Renderer>();
                if (pr != null) Debug.Log($"[Temi] {look.name} {a.name} -> {a.bone}: centro {go.transform.InverseTransformPoint(pr.bounds.center):F3}, osso {go.transform.InverseTransformPoint(bone.position):F3}");
            }
            Object.DestroyImmediate(accInst);

            // luce attaccata all'osso (lanterna, flebo, scialitica)
            if (!string.IsNullOrEmpty(look.lightMesh))
            {
                var r = FindRenderer(go, look.lightMesh);
                var bone = FindBone(inst, look.lightBone);
                if (r != null && bone != null)
                {
                    var lg = new GameObject("Luce" + look.name);
                    lg.transform.SetParent(bone, false);
                    lg.transform.position = r.bounds.center + go.transform.forward * look.lightForward;
                    lg.transform.rotation = Quaternion.LookRotation(go.transform.forward, Vector3.up);
                    var l = lg.AddComponent<Light>();
                    l.type = look.spotAngle > 0f ? LightType.Spot : LightType.Point;
                    if (look.spotAngle > 0f) l.spotAngle = look.spotAngle;
                    l.color = look.lightColor;
                    l.range = look.lightRange;
                    l.intensity = look.lightIntensity;
                    l.shadows = LightShadows.None;
                }
                else Debug.LogError($"[Temi] {look.name}: luce senza mesh '{look.lightMesh}' o osso '{look.lightBone}'");
            }

            // collisioni: corpo + occhio (colpo in testa = occhio, segue l'osso)
            var cc = go.AddComponent<CapsuleCollider>();
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.height = 1.8f;
            cc.radius = 0.3f;
            if (eye != null)
            {
                var sc = eye.gameObject.AddComponent<SphereCollider>();
                sc.radius = 0.09f;
                sc.center = Vector3.zero;
                eye.gameObject.tag = "Head";
            }
            else Debug.LogError("[Temi] osso 'occhio' non trovato");
            if (orb != null)
            {
                var lg = new GameObject("LuceOcchietto");
                lg.transform.SetParent(head != null ? head : go.transform, false);
                lg.transform.position = orb.GetComponent<Renderer>().bounds.center + Vector3.forward * 0.12f;
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.25f, 0.6f, 1f);
                l.range = 0.5f;
                l.intensity = 0.012f;
                l.shadows = LightShadows.None;
            }
            go.transform.localScale = look.scale;
            var rends = go.GetComponentsInChildren<Renderer>();
            var bounds = rends[0].bounds;
            foreach (var r in rends) bounds.Encapsulate(r.bounds);
            Debug.Log($"[Temi] {look.name}: bounds={bounds.size} stoffe={swapped} luci={go.GetComponentsInChildren<Light>().Length} renderer={rends.Length}");
            PrefabUtility.SaveAsPrefabAsset(go, $"{ResDir}/{look.name}.prefab");
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
        }

        static AnimationClip Clip(string name)
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(MonkFbx))
                if (a is AnimationClip c && c.name == name) return c;
            Debug.LogError("[Temi] clip non trovata: " + name);
            return null;
        }

        // ------------------------------------------------------------------ foto
        /// <summary>Mette ogni nemico davanti alla posa di spawn della hall e lo fotografa (hotel_build/shot_temi_*.png). La scena non viene salvata.</summary>
        public static void Shot()
        {
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene("Assets/_PryingEyes/Scenes/Hotel.unity");
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
            key.spotAngle = 55f;
            key.range = 14f;
            key.intensity = 6f;
            key.color = new Color(1f, 0.85f, 0.7f);
            key.transform.position = new Vector3(-2.0f, 3.6f, 19.4f);
            key.transform.LookAt(new Vector3(0f, 1.1f, 16.4f));

            var shots = new (string clip, float frame, Vector3 pos, Vector3 look, string tag)[]
            {
                ("fermo", 0, new Vector3(-0.9f, 1.55f, 19.4f), new Vector3(0f, 1.25f, 16.4f), "fronte"),
                ("fermo", 0, new Vector3(-3.2f, 1.5f, 18.6f), new Vector3(0f, 1.2f, 16.4f), "tre_quarti"),
                ("attacco", 16, new Vector3(-1.5f, 1.5f, 19.4f), new Vector3(0f, 1.2f, 16.6f), "attacco"),
                ("fermo", 0, new Vector3(-0.35f, 1.7f, 17.9f), new Vector3(0f, 1.62f, 16.4f), "volto"),
                ("fermo", 0, new Vector3(3.0f, 1.5f, 18.4f), new Vector3(0f, 1.2f, 16.4f), "tre_quarti_d"),
                ("fermo", 0, new Vector3(0.6f, 1.6f, 13.4f), new Vector3(0f, 1.4f, 16.4f), "dietro"),
            };
            foreach (var look in Looks)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ResDir}/{look.name}.prefab");
                if (prefab == null) continue;
                var e = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                e.transform.position = new Vector3(0f, 0f, 16.4f);
                e.transform.rotation = Quaternion.identity;
                var animGo = e.GetComponentInChildren<Animator>().gameObject;
                foreach (var smr in e.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
                bool warm = false;
                foreach (var s in shots)
                {
                    var clip = Clip(s.clip);
                    if (clip == null) continue;
                    clip.SampleAnimation(animGo, s.frame / 30f);
                    camGo.transform.position = s.pos;
                    camGo.transform.LookAt(s.look);
                    if (!warm) { cam.Render(); warm = true; }
                    cam.Render();
                    cam.Render();
                    var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    System.IO.File.WriteAllBytes($"C:/Users/danie/Desktop/prying-eyes/hotel_build/shot_temi_{look.costume}_{s.tag}.png", tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                Debug.Log("[Temi] foto salvate: " + look.name);
                Object.DestroyImmediate(e);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }
    }
}

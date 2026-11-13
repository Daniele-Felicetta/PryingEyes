using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa il monaco dall'occhio esportato da Blender (modelli/monaco/genera_monaco.py -- rig):
    /// texture, materiali URP Lit, rig Generic con 5 clip, Animator e prefab in Resources (lo usa Enemy).
    /// Shot() lo fotografa nella hall in varie pose (scena non salvata). Da CLI:
    /// Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.MonacoImport.RunAndShot -quit</summary>
    public static class MonacoImport
    {
        const string Dir = "Assets/_PryingEyes/Models/Monaco";
        const string TexDir = Dir + "/Textures";
        const string MatDir = Dir + "/Materials";
        const string Fbx = Dir + "/monaco.fbx";
        const string ControllerPath = Dir + "/Monaco.controller";
        const string ResDir = "Assets/_PryingEyes/Resources";
        const string PrefabPath = ResDir + "/Monaco.prefab";

        // clip: nome dell'azione in Blender, ciclica?
        static readonly (string name, bool loop)[] Clips =
        {
            ("fermo", true), ("cammina", true), ("attacco", false), ("colpito", false), ("morte", false),
        };

        public static void RunAndShot()
        {
            Run();
            Shot();
        }

        public static void Run()
        {
            AssetDatabase.Refresh();
            SetupTextures();
            var mats = SetupMaterials();
            SetupModel(mats);
            var ctrl = BuildController();
            BuildPrefab(ctrl);
        }

        public static void RefreshMaterials()
        {
            foreach (string name in new[] { "monaco_iride_base", "monaco_tela_detail", "monaco_tela_detail_normal" })
                if (AssetImporter.GetAtPath($"{TexDir}/{name}.png") is TextureImporter texture) ConfigureTexture(texture);
            SetupMaterials();
            AssetDatabase.SaveAssets();
        }

        static void SetupTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
                ConfigureTexture((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
        }

        static void ConfigureTexture(TextureImporter ti)
        {
            string path = ti.assetPath;
            bool normal = path.EndsWith("_normal.png");
            bool mask = path.EndsWith("_mask_unity.png");
            bool linear = normal || mask || path.EndsWith("_rough.png") || path.EndsWith("_detail.png");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !linear;
            ti.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.maxTextureSize = 4096;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static System.Collections.Generic.Dictionary<string, Material> SetupMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Dir, "Materials");
            return new System.Collections.Generic.Dictionary<string, Material>
            {
                { "M_stoffa", Cloth("Monaco_Stoffa", new Color(1.20f, 1.12f, 0.98f)) },
                { "M_stoffa_interno", Cloth("Monaco_StoffaInterno", new Color(0.55f, 0.53f, 0.49f)) },
                { "M_fibre", Cloth("Monaco_Fibre", new Color(1.42f, 1.26f, 1.04f)) },
                { "M_pelle", Lit("Monaco_Pelle", "pelle", Color.white) },
                { "M_volto", Lit("Monaco_Volto", "volto", Color.white) },
                { "M_occhio", Eye() },
                { "M_legno", Lit("Monaco_Legno", "legno", Color.white) },
                { "M_sottoveste", Lit("Monaco_Sottoveste", "sottoveste", Color.white) },
                { "M_metallo", Lit("Monaco_Metallo", "metallo", Color.white) },
                { "M_occhietto", Glow("Monaco_Occhietto") },
            };
        }

        static void SetupModel(System.Collections.Generic.Dictionary<string, Material> mats)
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
            // rig Generic: lo scheletro non e' umano (occhio, bastone), le clip restano come fatte in Blender
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.motionNodeName = "";
            mi.skinWeights = ModelImporterSkinWeights.Standard;     // 4 ossa per vertice, come i pesi fatti in Blender
            mi.importAnimation = true;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.SaveAndReimport();                                    // prima passata: Unity legge le take dell'FBX

            var takes = mi.defaultClipAnimations;
            var list = new System.Collections.Generic.List<ModelImporterClipAnimation>();
            foreach (var (name, loop) in Clips)
            {
                ModelImporterClipAnimation src = null;
                foreach (var t in takes)
                    if (t.takeName == name || t.takeName.EndsWith("|" + name)) src = t;
                if (src == null)
                {
                    Debug.LogError("[Monaco] take mancante nell'FBX: " + name + " (take presenti: " +
                                   string.Join(", ", System.Array.ConvertAll(takes, x => x.takeName)) + ")");
                    continue;
                }
                src.name = name;
                src.loopTime = loop;
                src.loopPose = false;
                src.lockRootRotation = false;
                src.lockRootHeightY = false;
                src.lockRootPositionXZ = false;
                list.Add(src);
            }
            mi.clipAnimations = list.ToArray();
            mi.SaveAndReimport();
        }

        static AnimationClip Clip(string name)
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(Fbx))
                if (a is AnimationClip c && c.name == name) return c;
            Debug.LogError("[Monaco] clip non trovata: " + name);
            return null;
        }

        /// <summary>Fermo <-> Cammina con "Velocita"; Attacca/Colpito trigger; Morto bool. "PassoVel" accelera il passo.</summary>
        static AnimatorController BuildController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Velocita", AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "PassoVel", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            ctrl.AddParameter("Attacca", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Colpito", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Morto", AnimatorControllerParameterType.Bool);
            var sm = ctrl.layers[0].stateMachine;
            var idle = sm.AddState("Fermo", new Vector3(300, 0, 0));
            idle.motion = Clip("fermo");
            var walk = sm.AddState("Cammina", new Vector3(300, 120, 0));
            walk.motion = Clip("cammina");
            walk.speedParameterActive = true;
            walk.speedParameter = "PassoVel";
            var atk = sm.AddState("Attacco", new Vector3(560, 0, 0));
            atk.motion = Clip("attacco");
            var hit = sm.AddState("Colpito", new Vector3(560, 120, 0));
            hit.motion = Clip("colpito");
            var death = sm.AddState("Morte", new Vector3(560, 240, 0));
            death.motion = Clip("morte");
            sm.defaultState = idle;

            var t = idle.AddTransition(walk);
            t.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Velocita");
            t.hasExitTime = false; t.duration = 0.2f;
            t = walk.AddTransition(idle);
            t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Velocita");
            t.hasExitTime = false; t.duration = 0.25f;

            var a = sm.AddAnyStateTransition(atk);
            a.AddCondition(AnimatorConditionMode.If, 0, "Attacca");
            a.AddCondition(AnimatorConditionMode.IfNot, 0, "Morto");
            a.hasExitTime = false; a.duration = 0.1f; a.canTransitionToSelf = false;
            t = atk.AddTransition(idle);
            t.hasExitTime = true; t.exitTime = 0.92f; t.duration = 0.15f;

            var h = sm.AddAnyStateTransition(hit);
            h.AddCondition(AnimatorConditionMode.If, 0, "Colpito");
            h.AddCondition(AnimatorConditionMode.IfNot, 0, "Morto");
            h.hasExitTime = false; h.duration = 0.05f; h.canTransitionToSelf = true;
            t = hit.AddTransition(idle);
            t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.12f;

            var d = sm.AddAnyStateTransition(death);
            d.AddCondition(AnimatorConditionMode.If, 0, "Morto");
            d.hasExitTime = false; d.duration = 0.1f; d.canTransitionToSelf = false;
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            EnemyMeleeImport.Configure(ctrl, Clip("attacco"));
            return ctrl;
        }

        static void BuildPrefab(AnimatorController ctrl)
        {
            if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Resources");
            AssetDatabase.DeleteAsset(Dir + "/Monaco.prefab");        // vecchio prefab statico
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            // radice pulita: la radice dell'FBX porta la conversione degli assi di Blender e non va toccata;
            // l'Animator sta sull'istanza dell'FBX perche' i percorsi delle clip partono da li'
            var go = new GameObject("Monaco");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(go.transform, false);
            Transform eye = null, head = null, orb = null;
            foreach (var tr in go.GetComponentsInChildren<Transform>())
            {
                if (tr.name == "occhio" && tr.GetComponent<Renderer>() == null) eye = tr;      // l'osso, non la mesh
                if (tr.name == "testa" && tr.GetComponent<Renderer>() == null) head = tr;
                if (tr.name == "occhietto") orb = tr;
            }
            var eyeMesh = FindRenderer(go, "occhio");
            if (eyeMesh != null && eyeMesh.bounds.center.z < 0f)
            {
                inst.transform.RotateAround(Vector3.zero, Vector3.up, 180f);   // deve guardare verso +Z
                Debug.Log("[Monaco] ruotato di 180 gradi per guardare verso +Z");
            }
            var anim = inst.GetComponent<Animator>();          // niente ??: nell'editor un componente mancante e' un "finto null"
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var b = smr.localBounds;
                b.Expand(1.6f);                    // la morte lo porta fino a 1.5 m avanti: niente sparizioni
                smr.localBounds = b;
            }
            // collisioni: corpo + occhio (colpo in testa = occhio, segue l'osso anche quando si gonfia)
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
            else Debug.LogError("[Monaco] osso 'occhio' non trovato");
            // luce blu dell'occhietto, attaccata alla testa: cala col quadrato della distanza, va tenuta debole
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
            var rends = go.GetComponentsInChildren<Renderer>();
            var bounds = rends[0].bounds;
            foreach (var r in rends) bounds.Encapsulate(r.bounds);
            Debug.Log($"[Monaco] bounds={bounds.size} ossa={go.GetComponentsInChildren<SkinnedMeshRenderer>()[0].bones.Length} occhio_osso={(eye ? eye.position : Vector3.zero)}");
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Monaco] prefab salvato: {PrefabPath}");
        }

        static Renderer FindRenderer(GameObject root, string name)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.name == name) return r;
            return null;
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

        static Material Asset(string name, Shader sh)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = sh;
            return m;
        }

        static Material Lit(string name, string set, Color tint)
        {
            var m = Asset(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", Tex($"monaco_{set}_base"));
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BumpMap", Tex($"monaco_{set}_normal"));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", Tex($"monaco_{set}_mask_unity"));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Cloth(string name, Color tint)
        {
            var m = Lit(name, "stoffa", tint);
            m.SetFloat("_BumpScale", 1.28f);
            m.SetFloat("_Smoothness", 0.67f);
            var detail = Tex("monaco_tela_detail");
            var normal = Tex("monaco_tela_detail_normal");
            if (detail != null && normal != null)
            {
                m.SetTexture("_DetailAlbedoMap", detail);
                m.SetTextureScale("_DetailAlbedoMap", new Vector2(4f, 4f));
                m.SetTexture("_DetailNormalMap", normal);
                m.SetFloat("_DetailAlbedoMapScale", 1f);
                m.SetFloat("_DetailNormalMapScale", 0.52f);
                m.EnableKeyword("_DETAIL_MULX2");
            }
            return m;
        }

        static Material Eye()
        {
            var m = Lit("Monaco_Occhio", "occhio", Color.white);
            var iris = Tex("monaco_iride_base");
            if (iris != null) m.SetTexture("_BaseMap", iris);
            m.SetFloat("_Smoothness", 0.79f);
            return m;
        }

        static Material Glow(string name)
        {
            var m = Asset(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.08f, 0.25f, 0.55f));
            m.SetFloat("_Smoothness", 0.9f);
            m.SetColor("_EmissionColor", new Color(0.075f, 0.39f, 1f) * 5.2f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Mette il prefab davanti alla posa di spawn della hall e lo fotografa in varie pose
        /// (clip campionate a mano). PNG in hotel_build/. La scena non viene salvata.</summary>
        public static void Shot()
        {
            EditorSceneManager.OpenScene("Assets/_PryingEyes/Scenes/Hotel.unity");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var monk = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            monk.transform.position = new Vector3(0f, 0f, 16.4f);
            monk.transform.rotation = Quaternion.identity;
            var animGo = monk.GetComponentInChildren<Animator>().gameObject;

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
            cam.fieldOfView = 45f;
            var rt = new RenderTexture(960, 720, 24);
            cam.targetTexture = rt;
            // luce di servizio: la hall e' buia proprio dove sta il monaco
            var key = new GameObject("LuceFoto").AddComponent<Light>();
            key.type = LightType.Spot;
            key.spotAngle = 50f;
            key.range = 12f;
            key.intensity = 6f;
            key.color = new Color(1f, 0.85f, 0.7f);
            key.transform.position = new Vector3(-1.6f, 3.2f, 19.2f);
            key.transform.LookAt(new Vector3(0f, 0.9f, 16.4f));

            var shots = new (string clip, float frame, Vector3 pos, Vector3 look)[]
            {
                ("fermo", 0, new Vector3(-1.3f, 1.35f, 19.6f), new Vector3(0f, 0.9f, 16.4f)),
                ("cammina", 9, new Vector3(-2.6f, 1.2f, 17.6f), new Vector3(0f, 0.85f, 16.4f)),
                ("attacco", 13, new Vector3(-1.3f, 1.35f, 19.6f), new Vector3(0f, 1.0f, 16.4f)),
                ("attacco", 16, new Vector3(-1.3f, 1.35f, 19.6f), new Vector3(0f, 1.0f, 16.6f)),
                ("colpito", 4, new Vector3(-1.3f, 1.35f, 19.6f), new Vector3(0f, 0.9f, 16.4f)),
                ("morte", 72, new Vector3(-2.4f, 1.6f, 19.4f), new Vector3(0f, 0.3f, 17.2f)),
            };
            bool warm = false;
            foreach (var s in shots)
            {
                var clip = Clip(s.clip);
                if (clip == null) continue;
                clip.SampleAnimation(animGo, s.frame / 30f);
                camGo.transform.position = s.pos;
                camGo.transform.LookAt(s.look);
                if (!warm) { cam.Render(); warm = true; }   // il primo render dopo OpenScene puo' uscire senza texture
                cam.Render();
                var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                var name = $"monaco_anim_{s.clip}_{(int)s.frame:00}";
                System.IO.File.WriteAllBytes("C:/Users/danie/Desktop/prying-eyes/hotel_build/shot_" + name + ".png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[Monaco] foto salvata: " + name);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }
    }
}

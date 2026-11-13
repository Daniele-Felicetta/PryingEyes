using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa l'abate (modelli/abate/genera_abate.py): il monaco ingrandito, nero e oro, con aureola e
    /// ostensorio. Stessa mesh, UV, scheletro e clip del monaco: dello stesso monaco riusa anche quasi tutte le texture
    /// (Models/Monaco/Textures); nuove solo la stoffa ricamata e l'oro. Materiali URP, Animator, prefab in Resources
    /// (lo usa Enemy al posto del bruto). Shot() lo fotografa nella hall. Da CLI:
    /// Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.AbateImport.RunAndShot -quit</summary>
    public static class AbateImport
    {
        const string Dir = "Assets/_PryingEyes/Models/Abate";
        const string TexDir = Dir + "/Textures";
        const string MonkTex = "Assets/_PryingEyes/Models/Monaco/Textures";
        const string MatDir = Dir + "/Materials";
        const string Fbx = Dir + "/abate.fbx";
        const string ControllerPath = Dir + "/Abate.controller";
        const string ResDir = "Assets/_PryingEyes/Resources";
        const string PrefabPath = ResDir + "/Abate.prefab";
        /// <summary>Scala del prefab: piu' alto e piu' largo del monaco (1.8 m -> 2.25 m).</summary>
        public static readonly Vector3 Scale = new Vector3(1.30f, 1.25f, 1.30f);

        static readonly (string name, bool loop)[] Clips =
        {
            ("fermo", true), ("cammina", true), ("attacco", false), ("colpito", false), ("morte", false),
        };

        public static void RunAndShot() { Run(); Shot(); }

        public static void Run()
        {
            AssetDatabase.Refresh();
            SetupTextures();
            var mats = SetupMaterials();
            SetupModel(mats);
            var ctrl = BuildController();
            BuildPrefab(ctrl);
        }

        static void SetupTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                bool normal = path.EndsWith("_normal.png");
                bool mask = path.EndsWith("_mask_unity.png");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal && !mask;
                ti.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.maxTextureSize = 2048;
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

        /// <summary>Lit con base, normali e maschera (R metallico, A lucidita'); le texture da 'dir' con prefisso 'set'.</summary>
        static Material Pbr(string name, string dir, string set, Color tint)
        {
            var m = Asset(name);
            m.SetTexture("_BaseMap", Tex(dir, set + "_base"));
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BumpMap", Tex(dir, set + "_normal"));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", Tex(dir, set + "_mask_unity"));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Dictionary<string, Material> SetupMaterials()
        {
            var gold = Pbr("Abate_Oro", TexDir, "abate_oro", Color.white);
            // un filo di autoilluminazione: l'oro si legge anche nel buio, e il lampo del colpo parte da qui
            gold.SetColor("_EmissionColor", new Color(0.05f, 0.032f, 0.008f));
            gold.EnableKeyword("_EMISSION");
            gold.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            var eye = Pbr("Abate_Occhio", MonkTex, "monaco_occhio", new Color(1.0f, 0.52f, 0.46f));   // iniettato di sangue
            var orb = Asset("Abate_Occhietto");
            orb.SetColor("_BaseColor", new Color(0.5f, 0.2f, 0.04f));
            orb.SetFloat("_Smoothness", 0.9f);
            orb.SetColor("_EmissionColor", new Color(1f, 0.52f, 0.12f) * 3f);
            orb.EnableKeyword("_EMISSION");
            orb.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(orb);

            AssetDatabase.SaveAssets();
            return new Dictionary<string, Material>
            {
                { "M_stoffa", Pbr("Abate_Stoffa", TexDir, "abate_stoffa", Color.white) },
                { "M_stoffa_interno", Pbr("Abate_StoffaInterno", TexDir, "abate_stoffa", new Color(0.5f, 0.5f, 0.5f)) },
                { "M_pelle", Pbr("Abate_Pelle", MonkTex, "monaco_pelle", Color.white) },
                { "M_volto", Pbr("Abate_Volto", MonkTex, "monaco_volto", Color.white) },
                { "M_occhio", eye },
                { "M_legno", Pbr("Abate_Legno", MonkTex, "monaco_legno", Color.white) },
                { "M_sottoveste", Pbr("Abate_Sottoveste", MonkTex, "monaco_sottoveste", Color.white) },
                { "M_metallo", Pbr("Abate_Metallo", MonkTex, "monaco_metallo", Color.white) },
                { "M_occhietto", orb },
                { "M_oro", gold },
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
            mi.motionNodeName = "";
            mi.skinWeights = ModelImporterSkinWeights.Standard;
            mi.importAnimation = true;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.SaveAndReimport();

            var takes = mi.defaultClipAnimations;
            var list = new List<ModelImporterClipAnimation>();
            foreach (var (name, loop) in Clips)
            {
                ModelImporterClipAnimation src = null;
                foreach (var t in takes)
                    if (t.takeName == name || t.takeName.EndsWith("|" + name)) src = t;
                if (src == null)
                {
                    Debug.LogError("[Abate] take mancante nell'FBX: " + name + " (presenti: " +
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
            Debug.LogError("[Abate] clip non trovata: " + name);
            return null;
        }

        /// <summary>Stesso grafo del monaco: Fermo <-> Cammina con "Velocita"; Attacca/Colpito trigger; Morto bool.</summary>
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
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var go = new GameObject("Abate");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(go.transform, false);
            Transform eye = null, head = null, orb = null;
            foreach (var tr in go.GetComponentsInChildren<Transform>())
            {
                if (tr.name == "occhio" && tr.GetComponent<Renderer>() == null) eye = tr;
                if (tr.name == "testa" && tr.GetComponent<Renderer>() == null) head = tr;
                if (tr.name == "occhietto") orb = tr;
            }
            var eyeMesh = FindRenderer(go, "occhio");
            if (eyeMesh != null && eyeMesh.bounds.center.z < 0f)
            {
                inst.transform.RotateAround(Vector3.zero, Vector3.up, 180f);
                Debug.Log("[Abate] ruotato di 180 gradi per guardare verso +Z");
            }
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var b = smr.localBounds;
                b.Expand(1.6f);
                smr.localBounds = b;
            }
            // collisioni (nel riferimento non scalato: la scala della radice le porta alla misura vera)
            var cc = go.AddComponent<CapsuleCollider>();
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.height = 1.8f;
            cc.radius = 0.3f;
            if (eye != null)
            {
                var sc = eye.gameObject.AddComponent<SphereCollider>();
                sc.radius = 0.095f;
                sc.center = Vector3.zero;
                eye.gameObject.tag = "Head";
            }
            else Debug.LogError("[Abate] osso 'occhio' non trovato");
            if (orb != null)
            {
                var lg = new GameObject("LuceOcchietto");
                lg.transform.SetParent(head != null ? head : go.transform, false);
                lg.transform.position = orb.GetComponent<Renderer>().bounds.center + Vector3.forward * 0.12f;
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.55f, 0.15f);
                l.range = 0.5f;
                l.intensity = 0.012f;
                l.shadows = LightShadows.None;
            }
            go.transform.localScale = Scale;
            var rends = go.GetComponentsInChildren<Renderer>();
            var bounds = rends[0].bounds;
            foreach (var r in rends) bounds.Encapsulate(r.bounds);
            Debug.Log($"[Abate] bounds={bounds.size} ossa={go.GetComponentsInChildren<SkinnedMeshRenderer>()[0].bones.Length}");
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Abate] prefab salvato: {PrefabPath}");
        }

        static Renderer FindRenderer(GameObject root, string name)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.name == name) return r;
            return null;
        }

        /// <summary>Mette l'abate davanti alla posa di spawn della hall e lo fotografa in varie pose. La scena non viene salvata.</summary>
        public static void Shot()
        {
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene("Assets/_PryingEyes/Scenes/Hotel.unity");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var abbot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            abbot.transform.position = new Vector3(0f, 0f, 16.4f);
            abbot.transform.rotation = Quaternion.identity;
            var animGo = abbot.GetComponentInChildren<Animator>().gameObject;
            foreach (var smr in abbot.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;

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
                ("fermo", 0, new Vector3(-1.2f, 1.6f, 19.8f), new Vector3(0f, 1.2f, 16.4f), "fronte"),
                ("fermo", 0, new Vector3(-3.4f, 1.5f, 18.4f), new Vector3(0f, 1.2f, 16.4f), "tre_quarti"),
                ("cammina", 9, new Vector3(-0.2f, 1.7f, 20.6f), new Vector3(0f, 1.2f, 16.4f), "cammina"),
                ("attacco", 16, new Vector3(-1.5f, 1.5f, 19.8f), new Vector3(0f, 1.2f, 16.6f), "attacco"),
                ("morte", 72, new Vector3(-2.6f, 1.8f, 19.6f), new Vector3(0f, 0.3f, 17.2f), "morte"),
            };
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
                System.IO.File.WriteAllBytes("C:/Users/danie/Desktop/prying-eyes/hotel_build/shot_abate_" + s.tag + ".png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[Abate] foto salvata: " + s.tag);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }
    }
}

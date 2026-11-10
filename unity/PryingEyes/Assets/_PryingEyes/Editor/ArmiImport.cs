using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa le armi da modelli/armi (genera_armi.py + pack_tex.py): copia FBX e texture, crea i materiali URP
    /// (atlante PBR, vetro, punto luminoso, reticoli collimati, lente dell'ottica ingrandita) e i prefab in Resources/Armi,
    /// piu' i materiali degli effetti (fiammata, polvere, scintille, tracce, fori, ottone).
    /// Da CLI: Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.ArmiImport.Run [-armi id1,id2] -quit</summary>
    public static class ArmiImport
    {
        const string Src = "C:/Users/danie/Desktop/prying-eyes/modelli/armi";
        const string Dir = "Assets/_PryingEyes/Models/Armi";
        const string MatDir = Dir + "/Materiali";
        const string ResDir = "Assets/_PryingEyes/Resources/Armi";

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static void Run()
        {
            Folder(Dir); Folder(MatDir); Folder(ResDir);
            string only = Arg("-armi");
            var ids = new List<string>();
            foreach (var f in Directory.GetFiles(Src + "/export", "*.fbx"))
            {
                string id = Path.GetFileNameWithoutExtension(f);
                if (only == null || System.Array.IndexOf(only.Split(','), id) >= 0) ids.Add(id);
            }
            ShaderUtil.allowAsyncCompilation = false;
            BuildFxMaterials();
            foreach (var id in ids) Import(id);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Armi] importate " + ids.Count + " armi: " + string.Join(", ", ids));
        }

        // ------------------------------------------------------------------ una arma
        static void Copy(string from, string to)
        {
            if (!File.Exists(from)) return;
            string full = Path.GetFullPath(to);
            if (File.Exists(full) && File.GetLastWriteTimeUtc(full) >= File.GetLastWriteTimeUtc(from) && new FileInfo(full).Length == new FileInfo(from).Length) return;
            File.Copy(from, full, true);
        }

        static void Import(string id)
        {
            int di = WeaponDefs.IndexOf(id);
            var def = di >= 0 ? WeaponDefs.All[di] : null;
            string fbx = Dir + "/" + id + ".fbx";
            Copy(Src + "/export/" + id + ".fbx", fbx);
            foreach (var t in new[] { "_base.jpg", "_mask.png", "_normal.jpg" })
                Copy(Src + "/tex/" + id + t, Dir + "/" + id + t);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            TexSettings(Dir + "/" + id + "_base.jpg", false, false);
            TexSettings(Dir + "/" + id + "_mask.png", false, false);
            TexSettings(Dir + "/" + id + "_normal.jpg", true, false);

            var atlas = AtlasMaterial(id);
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
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

            var vetro = Load("Armi_Vetro"); var emit = Load("Armi_Emit"); var emitAmbra = Load("Armi_EmitAmbra");
            Material ret = RetMaterial(id, def);
            Material scope = def != null && def.zoom > 1.4f ? ScopeMaterial(id, def) : null;

            var go = new GameObject(id);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            model.transform.SetParent(go.transform, false);
            go.AddComponent<WeaponRig>();
            int slots = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    if (n.StartsWith("A_")) mats[i] = atlas;
                    else if (n == "W_vetro") mats[i] = r.name == "lente_oculare" && scope != null ? scope : vetro;
                    else if (n == "W_emit") mats[i] = emit;
                    else if (n == "W_emit_ambra") mats[i] = emitAmbra;
                    else if (n == "W_retino") mats[i] = ret;
                    slots++;
                }
                r.sharedMaterials = mats;
            }
            Folder(ResDir);
            PrefabUtility.SaveAsPrefabAsset(go, ResDir + "/" + id + ".prefab");
            Object.DestroyImmediate(go);
            Debug.Log("[Armi] " + id + ": prefab salvato (" + slots + " slot)");
        }

        static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>(ResDir + "/" + name + ".mat");

        static void TexSettings(string path, bool normal, bool srgbMask)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            bool isBase = path.EndsWith("_base.jpg");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = isBase;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.filterMode = FilterMode.Trilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = 2048;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.SaveAndReimport();
        }

        static Material AtlasMaterial(string id)
        {
            string path = MatDir + "/A_" + id + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            var bs = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + id + "_base.jpg");
            var mk = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + id + "_mask.png");
            var nm = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + id + "_normal.jpg");
            m.SetTexture("_BaseMap", bs);
            m.SetColor("_BaseColor", Color.white);
            if (mk != null)
            {
                m.SetTexture("_MetallicGlossMap", mk);
                m.SetTexture("_OcclusionMap", mk);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.EnableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_SmoothnessTextureChannel", 0f);
                m.SetFloat("_Smoothness", 1f);
                m.SetFloat("_Metallic", 1f);
                m.SetFloat("_OcclusionStrength", 1f);
            }
            if (nm != null) { m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Cull", 2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Materiale del reticolo collimato (punto, olografico, anello) di quell'arma.</summary>
        static Material RetMaterial(string id, WeaponDef def)
        {
            string path = ResDir + "/Armi_Retino_" + id + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("PryingEyes/Reticle"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("PryingEyes/Reticle");
            int pat = def != null && (def.sight == "holo" || def.sight == "ring") ? Mathf.Clamp(def.pattern, 0, 2) : 0;
            m.SetColor("_Color", def != null && def.reticle.HasValue ? def.reticle.Value : def != null && def.sight == "holo" ? new Color(1f, 0.045f, 0.02f, 1f) : new Color(1f, 0.05f, 0.03f, 1f));   // intensita' 5,5: un verde > 0,1 satura e il rosso esce giallo
            m.SetFloat("_Intensity", 5.5f);
            m.SetFloat("_Pattern", pat);
            m.SetFloat("_Dot", 0.17f * (def != null ? def.dotSize : 1f));
            m.SetFloat("_Ring", 0.54f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material ScopeMaterial(string id, WeaponDef def)
        {
            string path = ResDir + "/Armi_Scope_" + id + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("PryingEyes/ScopeLens"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("PryingEyes/ScopeLens");
            m.SetFloat("_Zoom", def.zoom);
            m.SetFloat("_Reticle", def.pattern == 3 ? 3f : 4f);
            m.SetFloat("_Lit", def.pattern == 3 ? 1f : 0f);
            m.SetFloat("_Active", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ materiali condivisi e degli effetti
        static Material Lit(string name, Color c, float metallic, float smooth, bool transparent = false, Color? emission = null)
        {
            string path = ResDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
            }
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D SaveTex(string name, Texture2D t)
        {
            Folder(Dir + "/Fx");
            string path = Dir + "/Fx/" + name + ".png";
            File.WriteAllBytes(Path.GetFullPath(path), t.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Unlit(string name, Texture2D tex, bool additive)
        {
            string path = ResDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Unlit");
            Fx.ConfigureUnlit(m, SaveTex(name + "_tex", tex), additive);
            EditorUtility.SetDirty(m);
        }

        static void BuildFxMaterials()
        {
            Folder(ResDir);
            Lit("Armi_Vetro", new Color(0.55f, 0.72f, 0.80f, 0.09f), 0f, 0.96f, true);
            // i colori HDR sono lineari: il verde a 0,55 e' gia' giallo chiaro a schermo (sRGB 195). Per un arancio vero servono ~0,24
            Lit("Armi_Emit", Color.black, 0f, 0.3f, false, new Color(0.05f, 1f, 0.10f) * 1.3f);
            Lit("Armi_EmitAmbra", Color.black, 0f, 0.3f, false, new Color(1f, 0.24f, 0.01f) * 1.3f);
            Lit("Armi_Ottone", new Color(0.78f, 0.55f, 0.20f), 1f, 0.72f);
            Lit("Armi_Cartuccia", new Color(0.55f, 0.05f, 0.04f), 0f, 0.5f);
            Unlit("Fx_Puff", Fx.PuffTex(), false);
            Unlit("Fx_Spark", Fx.SparkTex(), true);
            Unlit("Fx_Tracer", Fx.TracerTex(), true);
            Unlit("Fx_Beam", Fx.BeamTex(), true);
            Unlit("Fx_Hole", Fx.HoleTex(), false);
            Unlit("Fx_Flash", Fx.StarTex(), true);
        }
    }
}

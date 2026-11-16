using System.IO;
using UnityEngine;
using UnityEditor;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa le mani e le maniche del giocatore: materiali URP (guanto di cuoio, giacca di tela, gomma dei rinforzi, con
    /// le texture procedurali di genera_texture.py), rig niente, prefab in Resources (li usa Weapon). Due strati:
    ///  - mani.fbx (modelli/hotel/genera_mani.py): le mani di una volta, Resources/Mani.prefab, ripiego per le armi senza mani su misura;
    ///  - armi/&lt;id&gt;.fbx (modelli/hotel/genera_braccia.py, prima genera_mani2.py): un paio di braccia per arma, mani modellate sulla
    ///    sua impugnatura e sulla sua astina, manica col gomito e giunti per ArmRig; prefab Resources/ManiArmi/&lt;id&gt;.prefab.
    /// Da CLI: Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.ManiImport.Run -quit</summary>
    public static class ManiImport
    {
        const string Dir = "Assets/_PryingEyes/Models/Mani";
        const string MatDir = Dir + "/Materials";
        const string Fbx = Dir + "/mani.fbx";
        const string ArmiDir = Dir + "/armi";
        const string Hotel = "Assets/_PryingEyes/Textures/Hotel/";
        const string ResDir = "Assets/_PryingEyes/Resources";
        const string PrefabPath = ResDir + "/Mani.prefab";
        const string PrefabArmiDir = ResDir + "/ManiArmi";
        const string LiberaFbx = Dir + "/libera/mano_libera.fbx";
        const string ManicaFbx = Dir + "/libera/manica_libera.fbx";
        const string ArmTex = "Assets/_PryingEyes/Textures/Braccia/";

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        /// <summary>Un'arma nuova con le sue mani in una sola apertura di Unity (il coltello): importa l'arma indicata da -armi e
        /// le mani indicate da -mani. Da CLI: -executeMethod PryingEyes.EditorTools.ManiImport.RunWithWeapons -armi id -mani id</summary>
        public static void RunWithWeapons()
        {
            ArmiImport.Run();
            Run();
        }

        public static void Run()
        {
            AssetDatabase.Refresh();
            HotelBuilder.ImportHotelTextures();          // guanto, giacca e rinforzo nuovi: normal map e maschere lineari
            // con -mani id1,id2 si rifanno solo le mani indicate: ogni FBX importato e' una reimportazione e una scrittura su disco
            string only = Arg("-mani");
            // guanto di cuoio scuro, giacca di tela marrone, gomma dei rinforzi. Le mani su misura che restano (il coltello, la presa
            // vecchia) hanno in media lo stesso colore delle texture uniche della mano articolata e della manica neutra
            var glove = Pbr("Mani_Guanto", "guanto", new Color(0.31f, 0.275f, 0.25f), 0.10f, 0.85f);
            var sleeve = Pbr("Mani_Manica", "giacca", new Color(0.58f, 0.555f, 0.515f), 0.09f, 0.6f);
            var guard = Pbr("Mani_Rinforzo", "rinforzo", new Color(0.16f, 0.16f, 0.17f), 0.05f, 0.7f);
            // la mano articolata (guanto e cinturino) e la manica neutra: texture uniche dipinte sulla loro superficie
            // (modelli/hotel/genera_texture_braccia.py); la saia della giacca resta come dettaglio ripetuto sulla manica (9 cm a
            // ripetizione: il giro della manica e' di circa 0,29 m, la lunghezza 0,52 m)
            ImportArmTextures();
            var gloveFree = Atlas("Mani_GuantoLibero", "guanto_libero", null, null, Vector2.one) ?? glove;
            var sleeveFree = Atlas("Mani_ManicaLibera", "manica_libera",
                                   AssetDatabase.LoadAssetAtPath<Texture2D>(ArmTex + "tela_dettaglio_base.png"),
                                   AssetDatabase.LoadAssetAtPath<Texture2D>(Hotel + "giacca_normal.png"), new Vector2(3.2f, 5.8f)) ?? sleeve;

            if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Resources");

            if (File.Exists(Fbx) && only == null)
            {
                ImportFbx(Fbx, glove, sleeve, guard, false);
                SavePrefab(Fbx, PrefabPath, "Mani");
            }

            // una coppia di mani per arma
            if (AssetDatabase.IsValidFolder(ArmiDir))
            {
                if (!AssetDatabase.IsValidFolder(PrefabArmiDir)) AssetDatabase.CreateFolder(ResDir, "ManiArmi");
                int n = 0; long verts = 0;
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ArmiDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string id = Path.GetFileNameWithoutExtension(path);
                    if (only != null && System.Array.IndexOf(only.Split(','), id) < 0) continue;
                    // leggibile: ArmRig ricava dalla manica la mesh con la pelle (pesi sulle ossa del braccio)
                    ImportFbx(path, glove, sleeve, guard, true);
                    int v = SavePrefab(path, $"{PrefabArmiDir}/{id}.prefab", "Mani");
                    verts += v; n++;
                    Debug.Log($"[Mani] {id}: {v} vertici");
                }
                Debug.Log($"[Mani] {n} paia di mani per arma, {verts} vertici in tutto");
            }
            // la mano sinistra libera (genera_braccia.py -- libera): stessi materiali, leggibile (HandRig le da' la pelle a runtime);
            // le prese della sinistra arma per arma stanno in Resources/ManiArmi/presa_sinistra.json (genera_braccia.py -- pose)
            if (File.Exists(LiberaFbx) && (only == null || System.Array.IndexOf(only.Split(','), "libera") >= 0))
            {
                ImportFbx(LiberaFbx, gloveFree, sleeveFree, gloveFree, true);
                int v = SavePrefab(LiberaFbx, ResDir + "/ManoLibera.prefab", "ManoLibera");
                Debug.Log($"[Mani] mano libera: {v} vertici");
            }
            // la manica neutra (genera_braccia.py -- manica): una per tutte le armi, leggibile (ArmRig le da' la pelle a runtime);
            // le prese della destra arma per arma stanno in Resources/ManiArmi/presa_destra.json
            if (File.Exists(ManicaFbx) && (only == null || System.Array.IndexOf(only.Split(','), "manica") >= 0))
            {
                ImportFbx(ManicaFbx, gloveFree, sleeveFree, gloveFree, true);
                int v = SavePrefab(ManicaFbx, ResDir + "/ManicaLibera.prefab", "ManicaLibera");
                Debug.Log($"[Mani] manica neutra: {v} vertici");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Importa e fotografa con una sola apertura di Unity: le foto sono quelle di ArmiShots (hotel_build/armi/&lt;id&gt;_*.png,
        /// con -armi e -viste come li' si sceglie quali).</summary>
        public static void RunAndShots()
        {
            Run();
            ArmiShots.Run();
        }

        /// <summary>Stesse impostazioni per tutti gli FBX: niente rig ne' animazioni, normali importate, materiali del progetto.</summary>
        static void ImportFbx(string path, Material glove, Material sleeve, Material guard, bool readable)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = readable;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "G_guanto"), glove);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "G_manica"), sleeve);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "G_rinforzo"), guard);
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.SaveAndReimport();
        }

        /// <summary>Radice pulita; l'istanza dell'FBX ci sta dentro senza essere toccata (la sua radice porta gli assi di Blender).
        /// Restituisce i vertici del prefab.</summary>
        static int SavePrefab(string fbxPath, string prefabPath, string rootName)
        {
            var go = new GameObject(rootName);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
            inst.transform.SetParent(go.transform, false);
            int verts = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) verts += mf.sharedMesh.vertexCount;
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            Debug.Log("[Mani] prefab salvato: " + prefabPath);
            return verts;
        }

        /// <summary>Impostazioni d'import delle texture uniche delle braccia (Textures/Braccia): normal map come NormalMap, maschere
        /// e dettaglio lineari (la maschera con l'alfa: lucidita'), colori sRGB, fino a 2048 texel, compressione di qualita'.</summary>
        static void ImportArmTextures()
        {
            if (!AssetDatabase.IsValidFolder(ArmTex.TrimEnd('/'))) return;
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArmTex.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool normal = path.EndsWith("_normal.png"), mask = path.EndsWith("_mask.png"), detail = path.Contains("_dettaglio_");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                bool srgb = !normal && !mask && !detail;
                var alpha = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                if (ti.textureType == type && ti.sRGBTexture == srgb && ti.alphaSource == alpha && ti.anisoLevel == 8
                    && ti.maxTextureSize == 2048 && ti.textureCompression == TextureImporterCompression.CompressedHQ) continue;
                ti.textureType = type;
                ti.sRGBTexture = srgb;
                ti.alphaSource = alpha;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
        }

        /// <summary>Lit con un atlante di texture uniche (colore, normali, maschera: metallo in R, occlusione in G, lucidita'
        /// nell'alfa) e, se c'e', un dettaglio ripetuto (colore x2 attorno al grigio 0,5 e normali) con la sua ripetizione sulle UV
        /// dell'atlante. Null se l'atlante manca (genera_texture_braccia.py non lanciato).</summary>
        static Material Atlas(string name, string set, Texture2D detailAlbedo, Texture2D detailNormal, Vector2 detailTiling)
        {
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArmTex + set + "_base.png");
            if (baseTex == null) { Debug.LogWarning("[Mani] texture mancanti: lanciare modelli/hotel/genera_texture_braccia.py (" + set + ")"); return null; }
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", baseTex);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArmTex + set + "_normal.png"));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            var mk = AssetDatabase.LoadAssetAtPath<Texture2D>(ArmTex + set + "_mask.png");
            m.SetTexture("_MetallicGlossMap", mk);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 1f);
            m.SetTexture("_OcclusionMap", mk);
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
            if (detailAlbedo != null || detailNormal != null)
            {
                m.SetTexture("_DetailAlbedoMap", detailAlbedo);
                m.SetTexture("_DetailNormalMap", detailNormal);
                m.SetTextureScale("_DetailAlbedoMap", detailTiling);
                m.SetFloat("_DetailAlbedoMapScale", 1f);
                m.SetFloat("_DetailNormalMapScale", 1f);
                m.EnableKeyword("_DETAIL_MULX2");
            }
            else
            {
                m.SetTexture("_DetailAlbedoMap", null);
                m.SetTexture("_DetailNormalMap", null);
                m.DisableKeyword("_DETAIL_MULX2");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Lit con le tre mappe del set (colore, normali, maschera: lucidita' nell'alfa); tile = metri per ripetizione.</summary>
        static Material Pbr(string name, string set, Color tint, float tile, float smoothness)
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
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Hotel + set + "_base.png"));
            m.SetTextureScale("_BaseMap", Vector2.one / tile);
            m.SetColor("_BaseColor", tint);
            var nm = AssetDatabase.LoadAssetAtPath<Texture2D>(Hotel + set + "_normal.png");
            if (nm != null) { m.SetTexture("_BumpMap", nm); m.EnableKeyword("_NORMALMAP"); }
            var mk = AssetDatabase.LoadAssetAtPath<Texture2D>(Hotel + set + "_mask.png");
            if (mk != null)
            {
                m.SetTexture("_MetallicGlossMap", mk);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_SmoothnessTextureChannel", 0f);
            }
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}

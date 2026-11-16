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
            // guanto di cuoio scuro, giacca di tela marrone (si legge anche al buio: il nero di prima spariva), gomma dei rinforzi
            var glove = Pbr("Mani_Guanto", "guanto", new Color(0.115f, 0.098f, 0.088f), 0.10f, 0.85f);
            var sleeve = Pbr("Mani_Manica", "giacca", new Color(0.36f, 0.31f, 0.24f), 0.09f, 0.6f);
            var guard = Pbr("Mani_Rinforzo", "rinforzo", new Color(0.16f, 0.16f, 0.17f), 0.05f, 0.7f);

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
                ImportFbx(LiberaFbx, glove, sleeve, guard, true);
                int v = SavePrefab(LiberaFbx, ResDir + "/ManoLibera.prefab", "ManoLibera");
                Debug.Log($"[Mani] mano libera: {v} vertici");
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

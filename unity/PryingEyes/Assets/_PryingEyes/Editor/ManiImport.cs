using System.IO;
using UnityEngine;
using UnityEditor;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa le mani e le maniche del giocatore: materiali URP (guanto di cuoio nero, manica di tessuto, con le texture
    /// della hall), rig niente, prefab in Resources (li usa Weapon). Due strati:
    ///  - mani.fbx (modelli/hotel/genera_mani.py): le mani di una volta, Resources/Mani.prefab, ripiego per le armi senza mani su misura;
    ///  - armi/&lt;id&gt;.fbx (modelli/hotel/genera_mani2.py): un paio di mani per arma, modellate sulla sua impugnatura e sulla sua astina,
    ///    prefab Resources/ManiArmi/&lt;id&gt;.prefab.
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

        public static void Run()
        {
            AssetDatabase.Refresh();
            var glove = Pbr("Mani_Guanto", "cuoio", new Color(0.055f, 0.055f, 0.06f), 0.16f, 0.6f);
            var sleeve = Pbr("Mani_Manica", "velluto", new Color(0.075f, 0.08f, 0.062f), 0.2f, 0.3f);

            if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Resources");

            if (File.Exists(Fbx))
            {
                ImportFbx(Fbx, glove, sleeve);
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
                    ImportFbx(path, glove, sleeve);
                    int v = SavePrefab(path, $"{PrefabArmiDir}/{id}.prefab", "Mani");
                    verts += v; n++;
                    Debug.Log($"[Mani] {id}: {v} vertici");
                }
                Debug.Log($"[Mani] {n} paia di mani per arma, {verts} vertici in tutto");
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
        static void ImportFbx(string path, Material glove, Material sleeve)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "G_guanto"), glove);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "G_manica"), sleeve);
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

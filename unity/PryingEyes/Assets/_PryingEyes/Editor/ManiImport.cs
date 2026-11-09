using UnityEngine;
using UnityEditor;

namespace PryingEyes.EditorTools
{
    /// <summary>Importa le mani e le maniche del giocatore (modelli/hotel/genera_mani.py): materiali URP (guanto di cuoio
    /// nero, manica di tessuto, con le texture della hall), rig niente, prefab in Resources (lo usa Weapon).
    /// Da CLI: Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.ManiImport.Run -quit</summary>
    public static class ManiImport
    {
        const string Dir = "Assets/_PryingEyes/Models/Mani";
        const string MatDir = Dir + "/Materials";
        const string Fbx = Dir + "/mani.fbx";
        const string Hotel = "Assets/_PryingEyes/Textures/Hotel/";
        const string PrefabPath = "Assets/_PryingEyes/Resources/Mani.prefab";

        public static void Run()
        {
            AssetDatabase.Refresh();
            var glove = Pbr("Mani_Guanto", "cuoio", new Color(0.055f, 0.055f, 0.06f), 0.16f, 0.6f);
            var sleeve = Pbr("Mani_Manica", "velluto", new Color(0.075f, 0.08f, 0.062f), 0.2f, 0.3f);
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
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "G_guanto"), glove);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "G_manica"), sleeve);
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.SaveAndReimport();

            // radice pulita; l'istanza dell'FBX ci sta dentro senza essere toccata (la sua radice porta gli assi di Blender)
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/Resources")) AssetDatabase.CreateFolder("Assets/_PryingEyes", "Resources");
            var go = new GameObject("Mani");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx));
            inst.transform.SetParent(go.transform, false);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log("[Mani] prefab salvato: " + PrefabPath);
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

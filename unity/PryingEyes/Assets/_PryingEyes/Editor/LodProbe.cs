using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PryingEyes.EditorTools
{
    /// <summary>Prova dei LOD di mesh automatici di Unity sugli umanoidi (monaco, abate, accessori dei temi): li abilita sull'importatore
    /// dell'FBX e stampa quanti livelli escono e quanti indici hanno. Da CLI:
    /// Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.LodProbe.Run -quit</summary>
    public static class LodProbe
    {
        static readonly string[] Paths =
        {
            "Assets/_PryingEyes/Models/Monaco/monaco.fbx",
            "Assets/_PryingEyes/Models/Abate/abate.fbx",
            "Assets/_PryingEyes/Models/Temi/accessori_temi.fbx",
        };

        public static void Run()
        {
            foreach (string path in Paths)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { Debug.LogError("[LodProbe] manca " + path); continue; }
                Debug.Log($"[LodProbe] {path}: generateMeshLods={importer.generateMeshLods} flags={importer.meshLodGenerationFlags} massimo={importer.maximumMeshLod}");
                importer.generateMeshLods = true;
                importer.SaveAndReimport();
                Report(path);
            }
        }

        public static void ReportOnly()
        {
            foreach (string path in Paths) Report(path);
        }

        static void Report(string path)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is Mesh mesh)) continue;
                var sb = new StringBuilder($"[LodProbe] {mesh.name}: vertici={mesh.vertexCount} sottomesh={mesh.subMeshCount} lodCount={mesh.lodCount}");
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var descriptor = mesh.GetSubMesh(sub);
                    sb.Append($" | sub{sub}: indici={descriptor.indexCount}");
                    for (int lod = 1; lod < mesh.lodCount; lod++) sb.Append($" lod{lod}={Describe(mesh, sub, lod)}");
                }
                Debug.Log(sb.ToString());
            }
        }

        static string Describe(Mesh mesh, int sub, int lod)
        {
            var method = typeof(Mesh).GetMethod("GetLod", new[] { typeof(int), typeof(int) });
            if (method == null) return "?";
            object range = method.Invoke(mesh, new object[] { sub, lod });
            var sb = new StringBuilder();
            foreach (var f in range.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) sb.Append(f.Name + ":" + f.GetValue(range) + ",");
            foreach (var p in range.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) sb.Append(p.Name + ":" + p.GetValue(range) + ",");
            return "{" + sb + "}";
        }
    }
}

using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.EditorTools
{
    /// <summary>Estrae dai LOD automatici di monaco.fbx e abate.fbx delle mesh compatte (solo i vertici di quel livello, stessi pesi e pose
    /// di legatura) e le raccoglie in Resources/BodyLods.asset (BodyLodLibrary), che Enemy.Budget usa per alleggerire la pelle dei corpi
    /// lontani. Va rifatta dopo ogni reimportazione dei modelli. Da CLI:
    /// Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.BodyLodBuilder.Run -quit</summary>
    public static class BodyLodBuilder
    {
        const string LibraryPath = "Assets/_PryingEyes/Resources/BodyLods.asset";
        static readonly string[] Models =
        {
            "Assets/_PryingEyes/Models/Monaco/monaco.fbx",
            "Assets/_PryingEyes/Models/Abate/abate.fbx",
        };
        const int TopLevel = 5;

        public static void Run()
        {
            foreach (string path in Models) SetReadable(path, true);
            try { Build(); }
            finally
            {
                foreach (string path in Models) SetReadable(path, false);
                AssetDatabase.SaveAssets();
            }
        }

        static void Build()
        {
            AssetDatabase.DeleteAsset(LibraryPath);
            var library = ScriptableObject.CreateInstance<BodyLodLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
            var report = new StringBuilder();
            int made = 0;
            foreach (string path in Models)
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(asset is Mesh source) || source.lodCount < 2) continue;
                    if (source.blendShapeCount > 0) { Debug.LogWarning($"[BodyLod] {source.name} ha {source.blendShapeCount} blend shape: salto (le copie compatte non le avrebbero)"); continue; }
                    var entry = new BodyLodLibrary.Entry { source = source, vertices = source.vertexCount, bones = source.bindposeCount };
                    var list = new List<Mesh>();
                    int[] allIndices = ReadAllIndices(source);
                    for (int lod = 1; lod <= TopLevel && lod < source.lodCount; lod++)
                    {
                        var compact = Compact(source, lod, allIndices);
                        AssetDatabase.AddObjectToAsset(compact, library);
                        list.Add(compact);
                        report.Append($" L{lod}={compact.vertexCount}");
                        made++;
                    }
                    entry.lods = list.ToArray();
                    library.entries.Add(entry);
                    Debug.Log($"[BodyLod] {System.IO.Path.GetFileName(path)}:{source.name} {source.vertexCount} vertici, {source.bindposeCount} ossa ->{report}");
                    report.Clear();
                }
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BodyLod] fatto: {library.entries.Count} mesh, {made} livelli compatti in {LibraryPath}");
        }

        /// <summary>True se ogni mesh con livelli dei due modelli ha la sua scala nella libreria e il numero di vertici e di ossa torna.</summary>
        public static bool IsCurrent(out string why)
        {
            var library = AssetDatabase.LoadAssetAtPath<BodyLodLibrary>(LibraryPath);
            if (library == null) { why = "manca " + LibraryPath; return false; }
            foreach (string path in Models)
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(asset is Mesh mesh) || mesh.lodCount < 2 || mesh.blendShapeCount > 0) continue;
                    var entry = library.entries.Find(e => e.source == mesh);
                    if (entry == null || entry.vertices != mesh.vertexCount || entry.bones != mesh.bindposeCount)
                    {
                        why = System.IO.Path.GetFileName(path) + ":" + mesh.name + (entry == null ? " non c'e'" : " ha un numero diverso di vertici o di ossa");
                        return false;
                    }
                }
            why = null;
            return true;
        }

        /// <summary>Chiamata da BuildPlayer: se i modelli sono stati rifatti senza rigenerare la scala, i corpi ricadono sui LOD automatici
        /// di Unity (piu' pesanti nelle orde) senza che nessuno se ne accorga.</summary>
        public static void WarnIfStale()
        {
            if (!IsCurrent(out string why))
                Debug.LogWarning("[BodyLod] AVVISO: la scala compatta dei LOD non corrisponde ai modelli (" + why + "): i corpi usano i LOD automatici, piu' pesanti. " +
                                 "Rigenerare con Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.BodyLodBuilder.Run -quit");
        }

        static void SetReadable(string path, bool readable)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) { Debug.LogError("[BodyLod] manca " + path); return; }
            if (importer.isReadable == readable) return;
            importer.isReadable = readable;
            importer.SaveAndReimport();
        }

        /// <summary>Tutto il buffer degli indici, livelli compresi (GetIndices dà solo il livello 0).</summary>
        static int[] ReadAllIndices(Mesh mesh)
        {
            var arrays = Mesh.AcquireReadOnlyMeshData(mesh);
            try
            {
                var data = arrays[0];
                int[] result;
                if (data.indexFormat == IndexFormat.UInt16)
                {
                    var raw = data.GetIndexData<ushort>();
                    result = new int[raw.Length];
                    for (int i = 0; i < result.Length; i++) result[i] = raw[i];
                }
                else
                {
                    var raw = data.GetIndexData<uint>();
                    result = new int[raw.Length];
                    for (int i = 0; i < result.Length; i++) result[i] = (int)raw[i];
                }
                return result;
            }
            finally { arrays.Dispose(); }
        }

        static Mesh Compact(Mesh source, int lod, int[] all)
        {
            int subMeshes = source.subMeshCount;
            var remap = new Dictionary<int, int>();
            var order = new List<int>();
            var triangles = new List<int>[subMeshes];
            for (int s = 0; s < subMeshes; s++)
            {
                var descriptor = source.GetSubMesh(s);
                var range = source.GetLod(s, lod);
                int relative = (int)range.indexStart, count = (int)range.indexCount;
                if (relative + count > descriptor.indexCount || descriptor.indexStart + relative + count > all.Length)
                    throw new System.Exception($"[BodyLod] {source.name} sottomesh {s} livello {lod}: l'intervallo {relative}+{count} esce dagli {descriptor.indexCount} indici della sottomesh ({all.Length} nel buffer)");
                int start = descriptor.indexStart + relative;
                var list = new List<int>(count);
                for (int k = start; k < start + count; k++)
                {
                    int v = all[k] + descriptor.baseVertex;
                    if (!remap.TryGetValue(v, out int nv)) { nv = order.Count; remap[v] = nv; order.Add(v); }
                    list.Add(nv);
                }
                triangles[s] = list;
            }

            var mesh = new Mesh { name = source.name + "_L" + lod };
            if (order.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            var positions = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var newPositions = new Vector3[order.Count];
            var newNormals = normals.Length == positions.Length ? new Vector3[order.Count] : null;
            var newTangents = tangents.Length == positions.Length ? new Vector4[order.Count] : null;
            for (int i = 0; i < order.Count; i++)
            {
                int o = order[i];
                newPositions[i] = positions[o];
                if (newNormals != null) newNormals[i] = normals[o];
                if (newTangents != null) newTangents[i] = tangents[o];
            }
            mesh.vertices = newPositions;
            if (newNormals != null) mesh.normals = newNormals;
            if (newTangents != null) mesh.tangents = newTangents;
            for (int channel = 0; channel < 4; channel++)
            {
                if (!source.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel))) continue;
                var uv = new List<Vector2>();
                source.GetUVs(channel, uv);
                var newUv = new List<Vector2>(order.Count);
                foreach (int o in order) newUv.Add(uv[o]);
                mesh.SetUVs(channel, newUv);
            }
            if (source.HasVertexAttribute(VertexAttribute.Color))
            {
                var colors = source.colors32;
                var newColors = new Color32[order.Count];
                for (int i = 0; i < order.Count; i++) newColors[i] = colors[order[i]];
                mesh.colors32 = newColors;
            }

            // pesi delle ossa: a ogni vertice la sua serie (da uno a quattro), copiata nell'ordine nuovo
            var perVertex = source.GetBonesPerVertex();
            var weights = source.GetAllBoneWeights();
            var first = new int[perVertex.Length];
            int total = 0;
            for (int v = 0; v < perVertex.Length; v++) { first[v] = total; total += perVertex[v]; }
            var newPerVertex = new NativeArray<byte>(order.Count, Allocator.Temp);
            var newWeights = new List<BoneWeight1>(order.Count * 4);
            for (int i = 0; i < order.Count; i++)
            {
                int o = order[i];
                newPerVertex[i] = perVertex[o];
                for (int j = 0; j < perVertex[o]; j++) newWeights.Add(weights[first[o] + j]);
            }
            var newWeightArray = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp);
            mesh.SetBoneWeights(newPerVertex, newWeightArray);
            newPerVertex.Dispose();
            newWeightArray.Dispose();
            mesh.bindposes = source.bindposes;

            mesh.subMeshCount = subMeshes;
            for (int s = 0; s < subMeshes; s++) mesh.SetTriangles(triangles[s], s, false);
            mesh.bounds = source.bounds;
            return mesh;
        }
    }
}

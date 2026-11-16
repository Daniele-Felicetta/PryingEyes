using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Lo specchio di un'istanza (la mano libera e la manica neutra sono modellate sinistre: specchiate diventano destre).
    /// Nel riferimento della radice x diventa -x: le posizioni e le rotazioni locali di tutti i discendenti si coniugano con la
    /// riflessione (la catena intera resta coniugata, nessuna scala negativa) e le mesh si copiano con x negato e i triangoli
    /// rovesciati (le facce restano verso l'esterno). Le copie sono in cache per mesh: cambiare arma non le rifa'.</summary>
    public static class HandMirror
    {
        static readonly Dictionary<Mesh, Mesh> cache = new Dictionary<Mesh, Mesh>();

        public static void Mirror(Transform root)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                var p = t.localPosition;
                t.localPosition = new Vector3(-p.x, p.y, p.z);
                var q = t.localRotation;
                t.localRotation = new Quaternion(q.x, -q.y, -q.z, q.w);
            }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) mf.sharedMesh = Mirrored(mf.sharedMesh);
        }

        /// <summary>La copia specchiata (x negato) di una mesh leggibile; null-safe e in cache.</summary>
        public static Mesh Mirrored(Mesh src)
        {
            if (src == null) return null;
            if (cache.TryGetValue(src, out var m) && m != null) return m;
            if (!src.isReadable) { Debug.LogWarning("[PryingEyes] specchio: mesh non leggibile " + src.name); return src; }
            m = Object.Instantiate(src);
            m.name = src.name + "_specchio";
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i].x = -v[i].x;
            m.vertices = v;
            var n = m.normals;
            if (n != null && n.Length == v.Length) { for (int i = 0; i < n.Length; i++) n[i].x = -n[i].x; m.normals = n; }
            var t = m.tangents;
            if (t != null && t.Length == v.Length) { for (int i = 0; i < t.Length; i++) { t[i].x = -t[i].x; t[i].w = -t[i].w; } m.tangents = t; }
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var tri = m.GetTriangles(s);
                for (int i = 0; i + 2 < tri.Length; i += 3) { int a = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = a; }
                m.SetTriangles(tri, s);
            }
            m.RecalculateBounds();
            cache[src] = m;
            return m;
        }
    }
}

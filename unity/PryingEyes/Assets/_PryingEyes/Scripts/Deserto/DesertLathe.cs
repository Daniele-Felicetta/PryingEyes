using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Mesh tornite a codice per le cose fatte a mano del sagrato (bracieri, lampioni): un profilo (raggio, quota) fatto girare attorno all'asse y, un tubo lungo un cammino (anche chiuso,
    /// un anello) e l'unione di piu' pezzi, ognuno con la sua posizione, in una mesh sola. Le normali sono ricalcolate dai triangoli, con la cucitura mediata.</summary>
    public static class DesertLathe
    {
        /// <summary>Gira un profilo attorno a y. Il profilo si percorre dal fondo in su lungo il fuori e poi si torna dentro (la parete interna guarda l'asse): la superficie guarda dalla parte
        /// destra del cammino, (dy, -dr) girata attorno all'asse, e i triangoli messi al contrario si girano da soli.</summary>
        public static Mesh Turn(string name, Vector2[] prof, int sides)
        {
            int row = sides + 1;
            var v = new List<Vector3>(prof.Length * row); var uv = new List<Vector2>(prof.Length * row); var tris = new List<int>();
            float len = 0f; var along = new float[prof.Length];
            for (int i = 1; i < prof.Length; i++) { len += (prof[i] - prof[i - 1]).magnitude; along[i] = len; }
            for (int i = 0; i < prof.Length; i++)
                for (int j = 0; j <= sides; j++)
                {
                    float ang = j * Mathf.PI * 2f / sides;
                    v.Add(new Vector3(Mathf.Cos(ang) * prof[i].x, prof[i].y, Mathf.Sin(ang) * prof[i].x));
                    uv.Add(new Vector2(j / (float)sides, len > 0f ? along[i] / len : 0f));
                }
            for (int i = 0; i + 1 < prof.Length; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * row + j, b0 = (i + 1) * row + j, c0 = (i + 1) * row + j + 1, d0 = i * row + j + 1;
                    var seg = prof[i + 1] - prof[i];
                    float mid = (j + 0.5f) * Mathf.PI * 2f / sides;
                    var want = new Vector3(Mathf.Cos(mid) * seg.y, -seg.x, Mathf.Sin(mid) * seg.y);          // la normale voluta
                    var got = Vector3.Cross(v[b0] - v[a0], v[c0] - v[a0]);
                    if (Vector3.Dot(got, want) >= 0f) { tris.Add(a0); tris.Add(b0); tris.Add(c0); tris.Add(a0); tris.Add(c0); tris.Add(d0); }
                    else { tris.Add(a0); tris.Add(c0); tris.Add(b0); tris.Add(a0); tris.Add(d0); tris.Add(c0); }
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var nrm = mesh.normals;                                   // la cucitura (il primo e l'ultimo vertice di ogni anello sono lo stesso punto) ha una normale sola
            for (int i = 0; i < prof.Length; i++) { var m = (nrm[i * row] + nrm[i * row + sides]).normalized; nrm[i * row] = m; nrm[i * row + sides] = m; }
            mesh.normals = nrm;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Un tubo lungo un cammino, con il raggio che cambia da un punto all'altro. 'closed' lo chiude su se stesso (un anello piano: il sistema di riferimento trasportato non si torce);
        /// altrimenti lo chiude con due tappi.</summary>
        public static Mesh Tube(string name, Vector3[] path, float[] radius, int sides, bool closed = false)
        {
            int n = path.Length, row = sides + 1;
            var tan = new Vector3[n]; var side = new Vector3[n]; var bin = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var a = path[closed ? (i + n - 1) % n : Mathf.Max(i - 1, 0)];
                var b = path[closed ? (i + 1) % n : Mathf.Min(i + 1, n - 1)];
                tan[i] = (b - a).normalized;
            }
            var up0 = Mathf.Abs(Vector3.Dot(tan[0], Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            side[0] = Vector3.Cross(tan[0], up0).normalized;
            for (int i = 1; i < n; i++)
            {
                var s = side[i - 1] - tan[i] * Vector3.Dot(side[i - 1], tan[i]);          // la normale che gira il meno possibile
                side[i] = s.sqrMagnitude < 1e-8f ? Vector3.Cross(tan[i], up0).normalized : s.normalized;
            }
            for (int i = 0; i < n; i++) bin[i] = Vector3.Cross(tan[i], side[i]);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= sides; j++)
                {
                    float ang = j * Mathf.PI * 2f / sides;
                    v.Add(path[i] + (Mathf.Cos(ang) * side[i] + Mathf.Sin(ang) * bin[i]) * radius[i]);
                    uv.Add(new Vector2(j / (float)sides, i / (float)Mathf.Max(1, n - 1)));
                }
            int rings = closed ? n : n - 1;
            for (int i = 0; i < rings; i++)
            {
                int i1 = (i + 1) % n;
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * row + j, b0 = i1 * row + j, c0 = i1 * row + j + 1, d0 = i * row + j + 1;
                    float mid = (j + 0.5f) * Mathf.PI * 2f / sides;
                    var want = Mathf.Cos(mid) * side[i] + Mathf.Sin(mid) * bin[i];
                    var got = Vector3.Cross(v[b0] - v[a0], v[c0] - v[a0]);
                    if (Vector3.Dot(got, want) >= 0f) { tris.Add(a0); tris.Add(b0); tris.Add(c0); tris.Add(a0); tris.Add(c0); tris.Add(d0); }
                    else { tris.Add(a0); tris.Add(c0); tris.Add(b0); tris.Add(a0); tris.Add(d0); tris.Add(c0); }
                }
            }
            int tubeVerts = v.Count;
            if (!closed)
            {
                // due tappi: un ventaglio con i propri vertici (la normale e' quella del tappo, non del tubo)
                for (int cap = 0; cap < 2; cap++)
                {
                    int ring = cap == 0 ? 0 : n - 1;
                    int c = v.Count; v.Add(path[ring]); uv.Add(new Vector2(0.5f, cap));
                    int first = v.Count;
                    for (int j = 0; j <= sides; j++) { v.Add(v[ring * row + j]); uv.Add(new Vector2(j / (float)sides, cap)); }
                    var outward = cap == 0 ? -tan[0] : tan[n - 1];
                    for (int j = 0; j < sides; j++)
                    {
                        var got = Vector3.Cross(v[first + j] - v[c], v[first + j + 1] - v[c]);
                        if (Vector3.Dot(got, outward) >= 0f) { tris.Add(c); tris.Add(first + j); tris.Add(first + j + 1); }
                        else { tris.Add(c); tris.Add(first + j + 1); tris.Add(first + j); }
                    }
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var nrm = mesh.normals;
            for (int i = 0; i < n; i++) { var m = (nrm[i * row] + nrm[i * row + sides]).normalized; nrm[i * row] = m; nrm[i * row + sides] = m; }
            mesh.normals = nrm;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Unisce i pezzi (ognuno con la sua matrice) in una mesh sola e libera i pezzi (uno stesso pezzo puo' comparire piu' volte, in posti diversi).</summary>
        public static Mesh Join(string name, List<(Mesh mesh, Matrix4x4 at)> parts)
        {
            var ci = new CombineInstance[parts.Count];
            var spent = new HashSet<Mesh>();
            for (int i = 0; i < parts.Count; i++) { ci[i] = new CombineInstance { mesh = parts[i].mesh, transform = parts[i].at }; spent.Add(parts[i].mesh); }
            var m = new Mesh { name = name };
            m.CombineMeshes(ci, true, true);
            foreach (var p in spent) if (p != null) Object.Destroy(p);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Una matrice di posizione e di giro attorno a y (gradi).</summary>
        public static Matrix4x4 At(Vector3 pos, float yawDeg = 0f) => Matrix4x4.TRS(pos, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one);
    }
}

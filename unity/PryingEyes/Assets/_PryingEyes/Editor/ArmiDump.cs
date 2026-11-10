using System.Text;
using UnityEngine;
using UnityEditor;

namespace PryingEyes.EditorTools
{
    /// <summary>Diagnostica: stampa gerarchia, assi, ingombri e materiali di un prefab d'arma (-armi id).</summary>
    public static class ArmiDump
    {

        /// <summary>Colore medio dell'atlante nei punti UV di una zona (vertici con z locale dell'arma sotto una soglia): per capire cosa vede lo shader.</summary>
        public static void Uv()
        {
            string id = "ar9_malleus";
            float zmax = -0.20f;
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) { if (a[i] == "-armi") id = a[i + 1]; if (a[i] == "-zmax") zmax = float.Parse(a[i + 1], System.Globalization.CultureInfo.InvariantCulture); }
            var prefab = Resources.Load<GameObject>("Armi/" + id);
            var corpo = prefab.transform.Find(id + "/corpo");
            if (corpo == null) foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) if (t.name == "corpo") corpo = t;
            var mesh = corpo.GetComponent<MeshFilter>().sharedMesh;
            var verts = mesh.vertices; var uvs = mesh.uv;
            var baseTex = new Texture2D(2, 2); baseTex.LoadImage(System.IO.File.ReadAllBytes("Assets/_PryingEyes/Models/Armi/" + id + "_base.jpg"));
            var maskTex = new Texture2D(2, 2); maskTex.LoadImage(System.IO.File.ReadAllBytes("Assets/_PryingEyes/Models/Armi/" + id + "_mask.png"));
            Color bs = Color.black, mk = Color.black; int n = 0;
            foreach (var kv in new[] { 0 })
            for (int i = 0; i < verts.Length; i++)
            {
                // asse avanti dell'arma = Y locale dell'FBX (vedi WeaponRig)
                if (verts[i].y > zmax) continue;
                bs += baseTex.GetPixelBilinear(uvs[i].x, uvs[i].y); mk += maskTex.GetPixelBilinear(uvs[i].x, uvs[i].y); n++;
            }
            Debug.Log($"[ArmiDump] uv zona y<{zmax}: {n} vertici, base media (sRGB 0..255) {bs * 255f / Mathf.Max(1, n)}, maschera media R metallo {mk.r / Mathf.Max(1, n):F2} G occlusione {mk.g / Mathf.Max(1, n):F2} A levigatezza {mk.a / Mathf.Max(1, n):F2}");
        }

        public static void Run()
        {
            string id = "ar9_malleus";
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-armi") id = a[i + 1];
            var prefab = Resources.Load<GameObject>("Armi/" + id);
            if (prefab == null) { Debug.Log("[ArmiDump] prefab mancante " + id); return; }
            var sb = new StringBuilder("[ArmiDump] " + id + "\n");
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                var mf = t.GetComponent<MeshFilter>();
                var r = t.GetComponent<Renderer>();
                sb.Append($"  {t.name}  pos={t.localPosition:F4} rot={t.localEulerAngles:F1} scale={t.localScale:F3}");
                if (mf != null && mf.sharedMesh != null)
                    sb.Append($"  mesh bounds c={mf.sharedMesh.bounds.center:F4} s={mf.sharedMesh.bounds.size:F4} tri={mf.sharedMesh.triangles.Length / 3}");
                if (r != null)
                {
                    sb.Append("  mat=");
                    foreach (var m in r.sharedMaterials) sb.Append((m != null ? m.name + "(" + m.shader.name + ")" : "null") + ";");
                }
                sb.Append('\n');
            }
            Debug.Log(sb.ToString());
        }
    }
}

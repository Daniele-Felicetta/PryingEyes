using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>La tenda di un banco del mercato del sagrato nel deserto (RoomKits.HubStall): un telo a strisce che pende tra i due pali con un po' di fiocco, scende dal retro verso i clienti e finisce con
    /// una balza a scalloppi. Prima era una lastra piatta di un colore. Il telo e' una mesh sola con due facce (dritta e rovescia: la luce e' giusta anche da sotto) e le strisce sono una texture
    /// minuscola per colore (una striscia scura e una chiara, a passo di 16 cm); i materiali sono suoi e muoiono con lui.</summary>
    public static class DesertAwning
    {
        const float Stripe = 0.16f;
        static readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();

        /// <summary>Costruisce la tenda come figlio di 'stall' (lo spazio del banco: x lungo il bancone da 0 a 'len' nel verso di 'sideSign', z verso i clienti). 'cloth' e' il colore del telo.</summary>
        public static GameObject Build(Transform stall, float len, float sideSign, Color cloth)
        {
            float a0 = -0.15f, a1 = len + 0.15f;
            const float dBack = -0.72f, dFront = 0.80f, yBack = 2.60f, yFront = 2.42f;
            int na = Mathf.Max(6, Mathf.CeilToInt((a1 - a0) / 0.30f)), nd = 4;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            float Sag(float a) => -0.07f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((a - a0) / (a1 - a0)));           // il telo cede un poco a meta' tra i pali
            Vector3 P(float a, float d, float y) => new Vector3(sideSign * a, y, d);
            // la faccia superiore: una griglia a (a, d), piu' alta dietro che davanti
            for (int j = 0; j <= nd; j++)
            {
                float d = Mathf.Lerp(dBack, dFront, j / (float)nd);
                float y0 = Mathf.Lerp(yBack, yFront, j / (float)nd);
                for (int i = 0; i <= na; i++)
                {
                    float a = Mathf.Lerp(a0, a1, i / (float)na);
                    v.Add(P(a, d, y0 + Sag(a) * (0.4f + 0.6f * j / nd)));
                    uv.Add(new Vector2((a - a0) / Stripe / 2f, 0.5f));
                }
            }
            int topVerts = v.Count;
            for (int j = 0; j < nd; j++)
                for (int i = 0; i < na; i++)
                {
                    int p = j * (na + 1) + i, q = p + 1, r = p + na + 1, s = r + 1;
                    Quad(tris, v, p, q, s, r, true);
                }
            // la balza davanti: una fila di scalloppi (mezzi cerchi) appesa al bordo anteriore
            int sc = Mathf.Max(4, Mathf.RoundToInt((a1 - a0) / 0.30f));
            const int seg = 5;
            int valStart = v.Count;
            for (int k = 0; k <= sc * seg; k++)
            {
                float u = k / (float)(sc * seg);
                float a = Mathf.Lerp(a0, a1, u);
                float within = (k % seg) / (float)seg;                       // 0..1 dentro uno scalloppo
                float drop = 0.10f + 0.15f * Mathf.Sqrt(Mathf.Max(0f, 1f - (2f * within - 1f) * (2f * within - 1f)));
                if (k > 0 && k % seg == 0) drop = 0.10f;
                float yTop = yFront + Sag(a) * 1.0f;
                v.Add(P(a, dFront, yTop)); uv.Add(new Vector2((a - a0) / Stripe / 2f, 0.5f));
                v.Add(P(a, dFront + 0.04f, yTop - drop)); uv.Add(new Vector2((a - a0) / Stripe / 2f, 0.5f));
            }
            for (int k = 0; k < sc * seg; k++)
            {
                int p = valStart + k * 2, q = p + 2, r = p + 1, s = p + 3;
                Quad(tris, v, p, q, s, r, false);
            }
            // l'altra faccia di tutto (rovescia): vertici propri con le normali girate dal giro dei triangoli
            int n0 = v.Count, t0 = tris.Count;
            for (int i = 0; i < n0; i++) { v.Add(v[i]); uv.Add(uv[i]); }
            for (int i = 0; i < t0; i += 3) { tris.Add(n0 + tris[i]); tris.Add(n0 + tris[i + 2]); tris.Add(n0 + tris[i + 1]); }
            var mesh = new Mesh { name = "Tenda" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Tenda");
            go.transform.SetParent(stall, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = Material(cloth);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        static Material wood;

        /// <summary>Il legno dei banconi nel deserto: il legno del kit (albedo 0,1, fatto per i corridoi bui) di giorno e' quasi nero, e i banchi erano scatole nere sotto una tenda colorata. Qui e' un bruno caldo.</summary>
        public static Material Wood()
        {
            if (wood != null) return wood;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "BancoLegno", color = new Color(0.43f, 0.29f, 0.16f) };
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.22f);
            return wood = m;
        }

        /// <summary>Un drappo dello stesso telo della tenda davanti al bancone: scende dal bordo del piano per 35-45 cm e finisce a scalloppi, a strisce come la tenda, due facce. Da' al banco il colore
        /// anche sotto il piano (prima era una parete scura) e ne spezza la scatola.</summary>
        public static GameObject Drape(Transform stall, float len, float sideSign, Color cloth)
        {
            const float yTop = 0.98f, d = 0.44f;
            float a0 = -0.03f, a1 = len + 0.03f;
            int sc = Mathf.Max(3, Mathf.RoundToInt((a1 - a0) / 0.32f));
            const int seg = 5;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int k = 0; k <= sc * seg; k++)
            {
                float u = k / (float)(sc * seg), a = Mathf.Lerp(a0, a1, u), within = (k % seg) / (float)seg;
                float drop = 0.35f + 0.10f * Mathf.Sqrt(Mathf.Max(0f, 1f - (2f * within - 1f) * (2f * within - 1f)));
                if (k > 0 && k % seg == 0) drop = 0.35f;
                v.Add(new Vector3(sideSign * a, yTop, d)); uv.Add(new Vector2((a - a0) / Stripe / 2f, 0.5f));
                v.Add(new Vector3(sideSign * a, yTop - drop, d + 0.015f)); uv.Add(new Vector2((a - a0) / Stripe / 2f, 0.5f));
            }
            for (int k = 0; k < sc * seg; k++)
            {
                int p = k * 2, q = p + 2, r = p + 1, s = p + 3;
                Quad(tris, v, p, q, s, r, false);
            }
            int n0 = v.Count, t0 = tris.Count;
            for (int i = 0; i < n0; i++) { v.Add(v[i]); uv.Add(uv[i]); }
            for (int i = 0; i < t0; i += 3) { tris.Add(n0 + tris[i]); tris.Add(n0 + tris[i + 2]); tris.Add(n0 + tris[i + 1]); }
            var mesh = new Mesh { name = "Drappo" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Drappo");
            go.transform.SetParent(stall, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = Material(cloth);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        /// <summary>Un quadrilatero (p, q, s, r in giro) con la faccia verso l'alto (o verso fuori, la balza): il giro si sceglie dalla normale che esce dal telo.</summary>
        static void Quad(List<int> tris, List<Vector3> v, int p, int q, int s, int r, bool up)
        {
            var n = Vector3.Cross(v[q] - v[p], v[s] - v[p]);
            bool flip = up ? n.y < 0f : n.z < 0f;                    // la balza guarda verso +z (i clienti)
            if (!flip) { tris.Add(p); tris.Add(q); tris.Add(s); tris.Add(p); tris.Add(s); tris.Add(r); }
            else { tris.Add(p); tris.Add(s); tris.Add(q); tris.Add(p); tris.Add(r); tris.Add(s); }
        }

        static Material Material(Color cloth)
        {
            int key = Mathf.RoundToInt(cloth.r * 255f) << 16 | Mathf.RoundToInt(cloth.g * 255f) << 8 | Mathf.RoundToInt(cloth.b * 255f);
            if (mats.TryGetValue(key, out var have) && have != null) return have;
            var tex = new Texture2D(32, 4, TextureFormat.RGBA32, true, false) { name = "TendaStrisce", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var dark = cloth * 0.85f; dark.a = 1f;
            var light = Color.Lerp(cloth, new Color(0.86f, 0.78f, 0.62f), 0.72f); light.a = 1f;      // la striscia chiara e' un crema virato sul colore del telo
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 32; x++)
                {
                    float weave = 0.93f + 0.07f * (((x + y) & 1) == 0 ? 1f : 0f);
                    var c = (x < 16 ? dark : light) * weave; c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply(true, true);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Tenda" };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_Metallic", 0f);
            mats[key] = m;
            return m;
        }
    }
}

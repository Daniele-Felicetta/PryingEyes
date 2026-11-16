using UnityEngine;
using UnityEngine.Rendering;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Il grimorio sull'altare del sacrario: un leggio di pietra scura con il piano inclinato e sopra il libro aperto, coperta di
    /// cuoio con gli angoli d'ottone e le due pagine dipinte (GrimoirePage.Page3D: pergamena, sangue, il dodecaedro coi suoi 19 nodi) le
    /// cui rune brillano di rosso. Braci che salgono, la luce rossa che respira. F apre il libro (GrimoireUI).</summary>
    public sealed class GrimoireAltar : MonoBehaviour, IInteract
    {
        public Grimoire.Book book;
        AnnexSite site;
        Transform bookT;
        Light glow;
        Material pageMat;
        float fxT, phase;
        Color glowColor;

        static readonly float[] Pedestal =
        {
            0.000f, 0.000f, 0.34f, 0.000f, 0.34f, 0.08f, 0.28f, 0.11f, 0.26f, 0.16f, 0.17f, 0.22f, 0.13f, 0.32f, 0.12f, 0.70f,
            0.15f, 0.80f, 0.22f, 0.86f, 0.24f, 0.92f, 0.00f, 0.92f,
        };

        public Vector3 Point => bookT != null ? bookT.position + Vector3.up * 0.06f : transform.position + Vector3.up * 1.05f;
        public float Radius => 2.4f;
        public string Title => book == Grimoire.Book.Stregoneria ? "IL GRIMORIO DELLA STREGONERIA" : "IL GRIMORIO DELL'ARSENALE";
        public string Prompt => GrimoireUI.Open ? null : $"{Title} — {Grimoire.Subtitle(book)}: leggi";
        public void Use() => GrimoireUI.Show(book, this);

        public static GrimoireAltar Place(AnnexSite site, Grimoire.Book book)
        {
            ShrineKit.Mats();
            var go = new GameObject("Altare del grimorio");
            go.transform.SetParent(site.props, false);
            go.transform.position = site.rewardPos;
            go.transform.rotation = Quaternion.Euler(0f, site.rewardYaw, 0f);
            var A = go.AddComponent<GrimoireAltar>();
            A.site = site; A.book = book;
            A.glowColor = book == Grimoire.Book.Stregoneria ? new Color(0.95f, 0.10f, 0.12f) : new Color(1f, 0.32f, 0.06f);
            A.phase = Random.value * 6f;
            var t = go.transform;
            ShrineKit.LatheObj(t, "Leggio", ShrineKit.StoneDark != null ? ShrineKit.StoneDark : ShrineKit.Stone, ShrineKit.Lathe("grimLeggio", Pedestal, 28));
            // il piano inclinato verso chi legge: chi legge guarda lungo +z locale (verso il fondo), il bordo lontano del piano sale
            var top = new GameObject("Piano");
            top.transform.SetParent(t, false);
            top.transform.localPosition = new Vector3(0f, 0.97f, 0f);
            top.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            ShrineKit.Prim(top.transform, PrimitiveType.Cube, "Lastra", ShrineKit.StoneDark != null ? ShrineKit.StoneDark : ShrineKit.Stone, new Vector3(0f, -0.03f, 0f), new Vector3(0.78f, 0.06f, 0.56f));
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.5f, 0f);
            col.size = new Vector3(0.78f, 1.0f, 0.6f);
            A.bookT = A.BuildBook(top.transform);
            A.glow = ShrineKit.PointLight(t, "Luce del grimorio", new Vector3(0f, 2.05f, -0.45f), A.glowColor, 0.9f * LightPool.Dim, 5.5f, 0.3f, 1.6f);
            Interact.Add(A);
            return A;
        }

        /// <summary>Il libro aperto: coperta in due ante, dorso, blocchi di pagine e i due fogli dipinti un filo sollevati a V.</summary>
        Transform BuildBook(Transform parent)
        {
            var b = new GameObject("Grimorio aperto").transform;
            b.SetParent(parent, false);
            b.localPosition = new Vector3(0f, 0.012f, 0f);
            var lea = ShrineKit.Leather; var brass = ShrineKit.Brass;
            const float pw = 0.30f, ph = 0.42f, tilt = 7f;
            foreach (float sx in new[] { -1f, 1f })
            {
                var side = new GameObject(sx < 0f ? "Anta sinistra" : "Anta destra").transform;
                side.SetParent(b, false);
                side.localRotation = Quaternion.Euler(0f, 0f, -sx * tilt);
                ShrineKit.Prim(side, PrimitiveType.Cube, "Coperta", lea, new Vector3(sx * (pw * 0.5f + 0.01f), 0f, 0f), new Vector3(pw + 0.03f, 0.012f, ph + 0.03f));
                ShrineKit.Prim(side, PrimitiveType.Cube, "Pagine", ShrineKit.Paper, new Vector3(sx * pw * 0.5f, 0.016f, 0f), new Vector3(pw - 0.004f, 0.022f, ph - 0.006f));
                foreach (float sz in new[] { -1f, 1f })
                    ShrineKit.Prim(side, PrimitiveType.Cube, "Angolo", brass, new Vector3(sx * (pw + 0.004f), 0.004f, sz * (ph * 0.5f + 0.004f)), new Vector3(0.05f, 0.016f, 0.05f));
                // il foglio dipinto: un quad sopra il blocco di pagine con la sua meta' della doppia pagina
                var leaf = new GameObject("Foglio");
                leaf.transform.SetParent(side, false);
                leaf.transform.localPosition = new Vector3(sx * pw * 0.5f, 0.0275f, 0f);
                var mf = leaf.AddComponent<MeshFilter>();
                mf.sharedMesh = PageMesh(sx < 0f, pw - 0.01f, ph - 0.012f);
                var mr = leaf.AddComponent<MeshRenderer>();
                mr.sharedMaterial = PageMaterial();
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            ShrineKit.Prim(b, PrimitiveType.Cylinder, "Dorso", lea, new Vector3(0f, -0.004f, 0f), new Vector3(0.03f, ph * 0.5f + 0.012f, 0.03f), new Vector3(90f, 0f, 0f));
            return b;
        }

        Material PageMaterial()
        {
            if (pageMat != null) return pageMat;
            var tex = GrimoirePage.Page3D(book, out var glowTex);
            pageMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Pagine del grimorio" };
            pageMat.SetTexture("_BaseMap", tex);
            pageMat.SetColor("_BaseColor", new Color(0.72f, 0.68f, 0.64f));
            pageMat.SetFloat("_Smoothness", 0.18f);
            pageMat.SetFloat("_AlphaClip", 1f);
            pageMat.SetFloat("_Cutoff", 0.45f);
            pageMat.EnableKeyword("_ALPHATEST_ON");
            pageMat.SetFloat("_Cull", 0f);
            pageMat.EnableKeyword("_EMISSION");
            pageMat.SetTexture("_EmissionMap", glowTex);
            pageMat.SetColor("_EmissionColor", glowColor * 1.2f);
            pageMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return pageMat;
        }

        /// <summary>Il foglio: una griglia leggermente incurvata (la carta che sale dal dorso), con la meta' giusta della doppia pagina.</summary>
        static Mesh PageMesh(bool left, float w, float h)
        {
            const int nx = 10, ny = 4;
            var r = left ? GrimoirePage.LeftPage : GrimoirePage.RightPage;
            var v = new Vector3[(nx + 1) * (ny + 1)];
            var uv = new Vector2[v.Length];
            var tri = new int[nx * ny * 6];
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float fx = i / (float)nx, fz = j / (float)ny;
                    // dal dorso (fx = 0 per la destra, 1 per la sinistra) il foglio sale e poi si appiattisce
                    float fromSpine = left ? 1f - fx : fx;
                    float lift = Mathf.Sin(Mathf.Clamp01(fromSpine * 1.4f) * Mathf.PI * 0.5f) * 0.012f - fromSpine * 0.004f;
                    v[j * (nx + 1) + i] = new Vector3((fx - 0.5f) * w, lift, (fz - 0.5f) * h);
                    // u da sinistra a destra, v dall'alto: l'alto della pagina e' il bordo lontano da chi legge (+z locale)
                    uv[j * (nx + 1) + i] = new Vector2(Mathf.Lerp(r.xMin, r.xMax, fx), 1f - Mathf.Lerp(r.yMax, r.yMin, fz));
                }
            int k = 0;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    tri[k++] = a; tri[k++] = c; tri[k++] = b; tri[k++] = b; tri[k++] = c; tri[k++] = d;
                }
            var m = new Mesh { name = left ? "Foglio sinistro" : "Foglio destro", vertices = v, uv = uv, triangles = tri };
            m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool reading = GrimoireUI.Open;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.7f + phase);
            if (pageMat != null) pageMat.SetColor("_EmissionColor", glowColor * ((reading ? 1.3f : 0.65f) * pulse));
            if (glow != null) ShrineKit.SetGlow(glow, (reading ? 1.5f : 0.9f) * LightPool.Dim);
            fxT -= dt;
            if (fxT > 0f || bookT == null) return;
            fxT = reading ? 0.05f : 0.12f;
            // braci che salgono dalle pagine
            Vector3 p = bookT.position + bookT.right * Random.Range(-0.28f, 0.28f) + bookT.forward * Random.Range(-0.18f, 0.18f) + Vector3.up * 0.03f;
            Fx.Particle(4, true, p, Vector3.up * Random.Range(0.15f, 0.4f) + Random.insideUnitSphere * 0.05f, Vector3.up * 0.1f, 0.006f, 0.014f, Random.Range(0.8f, 1.6f), Color.white, 0.8f);
        }

        void OnDestroy()
        {
            Interact.Remove(this);
            if (pageMat != null) Destroy(pageMat);
        }
    }
}

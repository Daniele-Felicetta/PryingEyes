using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>The scenery of a race, made from the circuit by code: the ribbon of the road (asphalt, kerbs, paved run-off, gravel traps, grass, a guard rail on both sides), the countryside around it as a
    /// height field that follows the road and rolls away to the horizon, trees in belts along the track, the pit building and grandstand by the start straight, the starting gantry with its five lights, and a
    /// ring of hills. The ground under the bikes is not these meshes: the physics reads the circuit itself (Circuit.SurfaceY), and the ribbon is drawn from the very same function, so what the wheels touch is
    /// what the eye sees.
    ///
    /// The terrain is one function, TerrainY: the nearest section of road decides the height (a shoulder just below the guard rail, a ditch, and a slope that climbs or falls to the rolling country far from the
    /// track), and under the road itself the ground is sunk well below it, so that the ribbon is never hidden by the ground; a skirt hangs from the ribbon's outer edge to close any crack.</summary>
    public sealed partial class PistaWorld : MonoBehaviour
    {
        [System.NonSerialized] public Circuit c;
        [System.NonSerialized] public Transform root;
        int seed;
        float baseY;
        float tx0, tx1, tz0, tz1;
        // the materials
        Material mAsphalt, mKerbRed, mKerbWhite, mApron, mGravel, mGrass, mLine, mArmco, mPost, mConcrete, mRoof, mGlass, mSteel, mSeatRed, mSeatBlue, mSeatYellow, mSeatWhite, mBlack;
        Material mTrunk, mLeaf, mNeedle, mTyre, mSignRed, mSignWhite;
        readonly List<Material> lampMats = new List<Material>();
        readonly List<Object> made = new List<Object>();
        /// <summary>How much of the work is done (0..1), for the loading screen.</summary>
        public float Progress { get; private set; }
        public string Stage { get; private set; } = "";
        public bool Ready { get; private set; }
        /// <summary>The five lights of the starting gantry: the director lights them one by one.</summary>
        public int LampsOn { get; private set; }
        /// <summary>Where the gantry stands (the middle of the line) and the height of its lights.</summary>
        public Vector3 GantryPos { get; private set; }

        public static PistaWorld Create(Circuit circuit, int seed, Transform parent)
        {
            var go = new GameObject("Pista mondo");
            go.transform.SetParent(parent, false);
            var w = go.AddComponent<PistaWorld>();
            w.c = circuit; w.seed = seed; w.root = go.transform;
            return w;
        }

        static float S(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        // ------------------------------------------------------------------ the ground
        float FarY(float x, float z)
        {
            float u = Mathf.Repeat(x / 4096f, 1f), v = Mathf.Repeat(z / 4096f, 1f);
            float n = PistaTex.Fbm(u, v, 14, 4, seed) - 0.5f;
            float dx = Mathf.Min(x - tx0, tx1 - x), dz = Mathf.Min(z - tz0, tz1 - z);
            float e = Mathf.Clamp01(Mathf.Min(dx, dz) / 380f);
            return baseY + 26f * n * (e * e * (3f - 2f * e));
        }

        /// <summary>The height of the countryside at a point (and under the road, well below it).</summary>
        public float TerrainY(float x, float z)
        {
            float far = FarY(x, z);
            Proj p;
            if (!c.ProjectNear(x, z, out p)) return far;
            int side = p.d >= 0f ? 1 : 0;
            float sgn = side == 1 ? 1f : -1f;
            float lim = c.Lerp(c.halfW, p.i, p.t) + c.Lerp(c.run[side], p.i, p.t);
            float a = Mathf.Abs(p.d), e = a - lim;
            float yRef = e <= 0f ? c.SurfaceY(p.i, p.t, p.d) : c.SurfaceY(p.i, p.t, sgn * lim) - 0.02f * e;
            float dep = 1.7f * S(7f, -7f, e);
            float near = yRef - 0.3f - dep;
            return Mathf.Lerp(near, far, S(25f, 190f, e));
        }

        /// <summary>The height the player's eye should stay above: the road corridor if the point is in it, the countryside otherwise.</summary>
        public float GroundY(float x, float z)
        {
            Proj p;
            if (c.ProjectNear(x, z, out p))
            {
                int side = p.d >= 0f ? 1 : 0;
                float lim = c.Lerp(c.halfW, p.i, p.t) + c.Lerp(c.run[side], p.i, p.t);
                if (Mathf.Abs(p.d) <= lim) return c.SurfaceY(p.i, p.t, p.d);
            }
            return TerrainY(x, z) + 0.3f;
        }

        // ------------------------------------------------------------------ materials
        Material Track(string name, Texture2D tex, Color tint, float smooth)
        {
            var m = PistaTex.Lit(name, tint, smooth, 0f, tex);
            made.Add(m); if (tex != null) made.Add(tex);
            return m;
        }

        Material Flat(string name, Color col, float smooth, float metal = 0f)
        {
            var m = PistaTex.Lit(name, col, smooth, metal);
            made.Add(m);
            return m;
        }

        /// <summary>A ground material of the PistaSurface shader: 'kind' picks what the shader draws (0 asphalt, 1 paved run-off, 2 gravel, 3 grass, 4 kerb), the three colours and the four frequencies
        /// (fine grain, mid grain, macro, stones: per metre) tune it. The two detail textures are shared by every material and by every race, so they are not ours to destroy.</summary>
        Material Surface(Shader sh, string name, int kind, Color a, Color b, Color c, float smooth, Vector4 scales)
        {
            var m = new Material(sh) { name = "Pista_" + name };
            m.SetFloat("_Kind", kind);
            m.SetColor("_ColorA", a); m.SetColor("_ColorB", b); m.SetColor("_ColorC", c);
            m.SetVector("_Scales", scales);
            m.SetFloat("_Smooth", smooth);
            m.SetTexture("_Grain", PistaDetail.Grain);
            m.SetTexture("_Stones", PistaDetail.Stones);
            made.Add(m);
            return m;
        }

        /// <summary>A leaf material of the PistaFoliage shader: the card texture is shared by every race (not ours to destroy), 'tint' scales its colour, 'sway' is the reach of the wind in metres.</summary>
        Material Foliage(Shader sh, string name, Texture2D card, Color tint, float smooth, float sway)
        {
            var m = new Material(sh) { name = "Pista_" + name };
            m.SetTexture("_LeafMap", card);
            m.SetColor("_Tint", tint);
            m.SetFloat("_Smooth", smooth);
            m.SetFloat("_Sway", sway);
            made.Add(m);
            return m;
        }

        void MakeMaterials()
        {
            var surf = Shader.Find("PryingEyes/PistaSurface");
            bool custom = PistaDetail.Ready && surf != null && surf.isSupported;
            if (custom)
            {
                var asphalt = new Vector4(0.7f, 0.11f, 0.013f, 0.85f);
                mAsphalt = Surface(surf, "Asfalto", 0, new Color(0.23f, 0.23f, 0.24f), new Color(0.36f, 0.36f, 0.37f), Color.black, 0.18f, asphalt);
                mApron = Surface(surf, "Via di fuga", 1, new Color(0.36f, 0.36f, 0.35f), new Color(0.50f, 0.50f, 0.48f), Color.black, 0.16f, asphalt);
                mGravel = Surface(surf, "Ghiaia", 2, Color.white, Color.white, Color.black, 0.10f, new Vector4(0.7f, 0.11f, 0.013f, 1.2f));
                mGrass = Surface(surf, "Erba", 3, new Color(0.16f, 0.25f, 0.07f), new Color(0.27f, 0.37f, 0.11f), new Color(0.42f, 0.37f, 0.18f), 0.08f, new Vector4(1.1f, 0.19f, 0.021f, 0.85f));
                mKerbRed = Surface(surf, "Cordolo rosso", 4, new Color(0.62f, 0.06f, 0.06f), Color.black, Color.black, 0.30f, asphalt);
                mKerbWhite = Surface(surf, "Cordolo bianco", 4, new Color(0.80f, 0.80f, 0.78f), Color.black, Color.black, 0.30f, asphalt);
            }
            else
            {
                mAsphalt = Track("Asfalto", PistaTex.Make("asfalto", 256, new Color(0.13f, 0.13f, 0.14f), new Color(0.27f, 0.27f, 0.28f), 6, 4, 0.9f, 0.9f, seed + 1), Color.white, 0.22f);
                mApron = Track("Via di fuga", PistaTex.Make("viafuga", 256, new Color(0.30f, 0.31f, 0.31f), new Color(0.42f, 0.43f, 0.42f), 6, 3, 0.9f, 0.7f, seed + 2), Color.white, 0.2f);
                mGravel = Track("Ghiaia", PistaTex.Make("ghiaia", 256, new Color(0.50f, 0.44f, 0.34f), new Color(0.74f, 0.68f, 0.54f), 20, 3, 0.7f, 1.3f, seed + 3), Color.white, 0.08f);
                mGrass = Track("Erba", PistaTex.Make("erba", 256, new Color(0.15f, 0.28f, 0.07f), new Color(0.30f, 0.45f, 0.13f), 10, 4, 1.1f, 0.8f, seed + 4), Color.white, 0.06f);
                mKerbRed = Flat("Cordolo rosso", new Color(0.62f, 0.06f, 0.06f), 0.35f);
                mKerbWhite = Flat("Cordolo bianco", new Color(0.80f, 0.80f, 0.78f), 0.35f);
            }
            mLine = Flat("Linee", new Color(0.88f, 0.88f, 0.86f), 0.25f);
            mArmco = Flat("Guardrail", new Color(0.62f, 0.64f, 0.66f), 0.6f, 0.5f);
            mPost = Flat("Paletti", new Color(0.30f, 0.31f, 0.33f), 0.4f, 0.4f);
            mConcrete = Flat("Cemento", new Color(0.55f, 0.54f, 0.51f), 0.15f);
            mRoof = Flat("Tetto", new Color(0.16f, 0.16f, 0.18f), 0.3f);
            mGlass = Flat("Vetrate", new Color(0.06f, 0.09f, 0.13f), 0.92f, 0.1f);
            mSteel = Flat("Acciaio", new Color(0.24f, 0.26f, 0.29f), 0.5f, 0.6f);
            mBlack = Flat("Nero", new Color(0.04f, 0.04f, 0.045f), 0.3f);
            mSeatRed = Flat("Sedili rossi", new Color(0.65f, 0.09f, 0.09f), 0.3f);
            mSeatBlue = Flat("Sedili blu", new Color(0.10f, 0.20f, 0.58f), 0.3f);
            mSeatYellow = Flat("Sedili gialli", new Color(0.80f, 0.66f, 0.10f), 0.3f);
            mSeatWhite = Flat("Sedili bianchi", new Color(0.78f, 0.78f, 0.76f), 0.3f);
            mSignRed = Flat("Insegna rossa", new Color(0.70f, 0.08f, 0.08f), 0.3f);
            mSignWhite = Flat("Insegna bianca", new Color(0.85f, 0.85f, 0.83f), 0.3f);
            mTyre = Flat("Gomme", new Color(0.03f, 0.03f, 0.035f), 0.2f);
            mTrunk = Flat("Tronchi", new Color(0.20f, 0.13f, 0.08f), 0.08f);
            var fol = Shader.Find("PryingEyes/PistaFoliage");
            if (PistaFoliage.Ready && fol != null && fol.isSupported)
            {
                mLeaf = Foliage(fol, "Foglie", PistaFoliage.Leaf, Color.white, 0.10f, 0.10f);
                mNeedle = Foliage(fol, "Aghi", PistaFoliage.Needle, Color.white, 0.08f, 0.06f);
                if (PistaSigns.Ready)
                {
                    mCrowd = Foliage(fol, "Folla", PistaSigns.Crowd, Color.white, 0.04f, 0.04f);
                    mCrowd.SetFloat("_Trans", 0f);                 // a crowd is not a leaf: no sun through it
                }
            }
            else
            {
                // the cards could not be made (or the shader is not there): flat colours, as before
                mLeaf = Flat("Foglie", new Color(0.16f, 0.33f, 0.08f), 0.1f);
                mNeedle = Flat("Aghi", new Color(0.05f, 0.16f, 0.08f), 0.1f);
            }
            // the advertising boards are an ordinary lit surface with a picture: they need no shader of their own, so they are there with or without the cards (the crowd needs the cut-out shader)
            if (PistaSigns.Ready)
            {
                mBoards = PistaTex.Lit("Cartelloni", Color.white, 0.25f, 0f, PistaSigns.Boards);
                made.Add(mBoards);
            }
            for (int i = 0; i < 5; i++)
            {
                var lamp = PistaTex.Glow("Luce " + i, new Color(0.10f, 0.02f, 0.02f), Color.black, 0.5f);
                lampMats.Add(lamp); made.Add(lamp);
            }
        }

        // ------------------------------------------------------------------ the whole build, a few pieces per frame
        public IEnumerator Build()
        {
            Stage = "Materiali"; Progress = 0.02f;
            PistaDetail.Begin();                                    // the worker thread usually finished long ago (the director starts it when the mode starts)
            while (!PistaDetail.Done) yield return null;
            PistaDetail.Finish();
            PistaFoliage.Begin();
            while (!PistaFoliage.Done) yield return null;
            PistaFoliage.Finish();
            PistaSigns.Begin();
            while (!PistaSigns.Done) yield return null;
            PistaSigns.Finish();
            MakeMaterials();
            // the ground rectangle: the track with a margin of 900 m
            tx0 = c.minX - 900f; tx1 = c.maxX + 900f; tz0 = c.minZ - 900f; tz1 = c.maxZ + 900f;
            float sum = 0f; for (int i = 0; i < c.n; i++) sum += c.py[i];
            baseY = sum / c.n - 1.5f;
            yield return null;
            Stage = "Asfalto"; Progress = 0.05f;
            var road = BuildRoad();
            while (road.MoveNext()) yield return null;
            Stage = "Terreno"; Progress = 0.35f;
            var ground = BuildTerrain();
            while (ground.MoveNext()) yield return null;
            Stage = "Strutture"; Progress = 0.80f;
            BuildProps();
            yield return null;
            Stage = "Alberi"; Progress = 0.86f;
            var trees = BuildTrees();
            while (trees.MoveNext()) yield return null;
            BuildHills();
            Stage = ""; Progress = 1f; Ready = true;
        }

        void OnDestroy()
        {
            foreach (var o in made) if (o != null) Destroy(o);
            made.Clear();
        }

        /// <summary>The lamps of the gantry: n lit (0..5, red), or all dark; 'go' flashes them green for the start.</summary>
        public void SetLamps(int n, bool go = false)
        {
            LampsOn = n;
            for (int i = 0; i < lampMats.Count; i++)
            {
                bool on = i < n || go;
                Color e = go ? new Color(0.2f, 2.2f, 0.4f) : new Color(3.2f, 0.12f, 0.08f);
                lampMats[i].SetColor("_EmissionColor", on ? e : Color.black);
                lampMats[i].SetColor("_BaseColor", on ? (go ? new Color(0.1f, 0.5f, 0.2f) : new Color(0.6f, 0.05f, 0.04f)) : new Color(0.10f, 0.02f, 0.02f));
            }
        }

        // ------------------------------------------------------------------ the terrain
        IEnumerator BuildTerrain()
        {
            float area = (tx1 - tx0) * (tz1 - tz0);
            float cs = Mathf.Clamp(Mathf.Sqrt(area / 130000f), 6f, 14f);
            int nx = Mathf.CeilToInt((tx1 - tx0) / cs), nz = Mathf.CeilToInt((tz1 - tz0) / cs);
            var h = new float[(nx + 1) * (nz + 1)];
            int done = 0;
            for (int j = 0; j <= nz; j++)
            {
                for (int i = 0; i <= nx; i++) h[j * (nx + 1) + i] = TerrainY(tx0 + i * cs, tz0 + j * cs);
                done += nx + 1;
                if (done > 14000) { done = 0; Progress = 0.35f + 0.40f * j / nz; yield return null; }
            }
            const int cell = 40;
            int chunks = 0;
            for (int cz0 = 0; cz0 < nz; cz0 += cell)
                for (int cx0 = 0; cx0 < nx; cx0 += cell)
                {
                    int cx1 = Mathf.Min(cx0 + cell, nx), cz1 = Mathf.Min(cz0 + cell, nz);
                    var b = new MB(1);
                    int w = cx1 - cx0 + 1;
                    for (int j = cz0; j <= cz1; j++)
                        for (int i = cx0; i <= cx1; i++)
                        {
                            float x = tx0 + i * cs, z = tz0 + j * cs, y = h[j * (nx + 1) + i];
                            float hl = h[j * (nx + 1) + Mathf.Max(i - 1, 0)], hr = h[j * (nx + 1) + Mathf.Min(i + 1, nx)];
                            float hd = h[Mathf.Max(j - 1, 0) * (nx + 1) + i], hu = h[Mathf.Min(j + 1, nz) * (nx + 1) + i];
                            var nrm = new Vector3(hl - hr, 2f * cs, hd - hu).normalized;
                            b.Vert(new Vector3(x, y, z), nrm, new Vector2(x / 9f, z / 9f));
                        }
                    for (int j = 0; j < cz1 - cz0; j++)
                        for (int i = 0; i < cx1 - cx0; i++)
                        {
                            int a = j * w + i, bb = a + 1, cc = a + w + 1, d = a + w;
                            b.Tri(0, a, d, cc); b.Tri(0, a, cc, bb);
                        }
                    var mesh = b.ToMesh("Terreno");
                    made.Add(mesh);
                    MB.Spawn("Terreno " + chunks++, root, mesh, new[] { mGrass }, false, true);
                    if (chunks % 3 == 0) yield return null;
                }
            // beyond the terrain: a flat plain to the horizon, level with the edge of the countryside. It is a frame around the terrain, never a sheet under it: a sheet stands above every stretch of road that
            // dips below the average height of the circuit (on a hilly circuit that is a third of the lap) and hides the road, the kerbs and the wheels of the bikes.
            var far = new MB(1);
            const float R = 9000f;
            float cxm = (tx0 + tx1) * 0.5f, czm = (tz0 + tz1) * 0.5f, fy = baseY;
            float ex0 = tx0, ex1 = tx0 + nx * cs, ez0 = tz0, ez1 = tz0 + nz * cs;
            float fx0 = cxm - R, fx1 = cxm + R, fz0 = czm - R, fz1 = czm + R;
            FramePiece(far, fx0, fz0, ex0, fz1, fy);
            FramePiece(far, ex1, fz0, fx1, fz1, fy);
            FramePiece(far, ex0, fz0, ex1, ez0, fy);
            FramePiece(far, ex0, ez1, ex1, fz1, fy);
            for (int k = 0; k < far.uv.Count; k++) far.uv[k] = new Vector2(far.v[k].x / 9f, far.v[k].z / 9f);
            var fm = far.ToMesh("Pianura");
            made.Add(fm);
            MB.Spawn("Pianura", root, fm, new[] { mGrass }, false, false);
        }

        static void FramePiece(MB b, float xa, float za, float xb, float zb, float y)
        {
            b.Face(0, new Vector3(xa, y, za), new Vector3(xa, y, zb), new Vector3(xb, y, zb), new Vector3(xb, y, za), Vector3.up, 1f, 1f);
        }

        // ------------------------------------------------------------------ the hills on the horizon
        // Three ranges of ridges round the countryside, near to far, each a height field on a ring so that the sun shades its slopes and the ranges overlap into depth; the haze does the rest, and the
        // farthest are almost the colour of the sky. A texture of fields and woods wraps round each ring. The crests stay inside the far plane of the race camera (6,000 m).
        void BuildHills()
        {
            float cxm = (tx0 + tx1) * 0.5f, czm = (tz0 + tz1) * 0.5f;
            float rad = Mathf.Max(Mathf.Sqrt((tx1 - tx0) * (tx1 - tx0) + (tz1 - tz0) * (tz1 - tz0)) * 0.5f + 600f, 2400f);
            var tex = PistaTex.Make("colline", 256, new Color(0.13f, 0.22f, 0.10f), new Color(0.36f, 0.42f, 0.24f), 12, 5, 1.2f, 0.9f, seed + 6);
            var mat = Track("Colline", tex, Color.white, 0.04f);
            BuildRange(cxm, czm, rad, 900f, 260f, seed + 77, mat, "Colline vicine");
            BuildRange(cxm, czm, rad + 900f, 1300f, 520f, seed + 78, mat, "Colline medie");
            BuildRange(cxm, czm, rad + 1900f, 1500f, 800f, seed + 79, mat, "Colline lontane");
        }

        /// <summary>One range: a ring from radius r0 to r0 + width, as high as 'top' where the noise is highest and falling back below the plain at both edges; the normals come from the grid, so the slopes are shaded.</summary>
        void BuildRange(float cxm, float czm, float r0, float width, float top, int sd, Material mat, string name)
        {
            const int seg = 360, rings = 8;
            int row = seg + 1;
            var pos = new Vector3[(rings + 1) * row];
            for (int j = 0; j <= rings; j++)
            {
                float rho = j / (float)rings;
                float env = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(rho * Mathf.PI)), 0.8f);                   // 0 at both edges, 1 in the middle (sin(pi) is a hair under zero: a power of that is NaN)
                for (int i = 0; i <= seg; i++)
                {
                    float u = i / (float)seg, a = (i % seg) * Mathf.PI * 2f / seg;
                    float n = PistaTex.Fbm(Mathf.Repeat(u, 1f), Mathf.Repeat(0.13f + rho * 0.45f, 1f), 14, 5, sd);
                    float ridge = Mathf.Pow(Mathf.Clamp01(n * 1.35f - 0.12f), 1.5f);
                    float r = r0 + rho * width + 160f * Mathf.Sin(a * 5f + sd);
                    pos[j * row + i] = new Vector3(cxm + Mathf.Sin(a) * r, baseY - 8f + top * ridge * env, czm + Mathf.Cos(a) * r);
                }
            }
            var b = new MB(1);
            for (int j = 0; j <= rings; j++)
                for (int i = 0; i <= seg; i++)
                {
                    int ia = (i + seg - 1) % seg, ib = (i + 1) % seg;                                     // the neighbours round the ring (the seam wraps)
                    Vector3 dAng = pos[j * row + ib] - pos[j * row + ia];
                    Vector3 dRad = pos[Mathf.Min(j + 1, rings) * row + i] - pos[Mathf.Max(j - 1, 0) * row + i];
                    Vector3 nrm = Vector3.Cross(dRad, dAng).normalized;
                    if (nrm.y < 0f) nrm = -nrm;
                    b.Vert(pos[j * row + i], nrm, new Vector2(i / (float)seg * 14f, j / (float)rings * 3f));
                }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int p00 = j * row + i, p10 = p00 + 1, p01 = p00 + row, p11 = p01 + 1;
                    b.Tri(0, p00, p01, p11); b.Tri(0, p00, p11, p10);
                }
            var mesh = b.ToMesh(name);
            made.Add(mesh);
            MB.Spawn(name, root, mesh, new[] { mat }, false, false);
        }
    }
}

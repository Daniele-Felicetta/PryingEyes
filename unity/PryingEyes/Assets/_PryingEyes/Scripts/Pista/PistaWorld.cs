using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        Material mAsphalt, mKerbRed, mKerbWhite, mApron, mGravel, mGrass, mLine, mArmco, mPost, mConcrete, mRoof, mGlass, mSteel, mSeatRed, mSeatBlue, mSeatYellow, mSeatWhite, mBlack, mHill;
        Material mTrunk, mLeafA, mLeafB, mLeafC, mPine, mTyre, mSignRed, mSignWhite;
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

        void MakeMaterials()
        {
            mAsphalt = Track("Asfalto", PistaTex.Make("asfalto", 256, new Color(0.13f, 0.13f, 0.14f), new Color(0.27f, 0.27f, 0.28f), 6, 4, 0.9f, 0.9f, seed + 1), Color.white, 0.22f);
            mApron = Track("Via di fuga", PistaTex.Make("viafuga", 256, new Color(0.30f, 0.31f, 0.31f), new Color(0.42f, 0.43f, 0.42f), 6, 3, 0.9f, 0.7f, seed + 2), Color.white, 0.2f);
            mGravel = Track("Ghiaia", PistaTex.Make("ghiaia", 256, new Color(0.50f, 0.44f, 0.34f), new Color(0.74f, 0.68f, 0.54f), 20, 3, 0.7f, 1.3f, seed + 3), Color.white, 0.08f);
            mGrass = Track("Erba", PistaTex.Make("erba", 256, new Color(0.15f, 0.28f, 0.07f), new Color(0.30f, 0.45f, 0.13f), 10, 4, 1.1f, 0.8f, seed + 4), Color.white, 0.06f);
            mKerbRed = Flat("Cordolo rosso", new Color(0.62f, 0.06f, 0.06f), 0.35f);
            mKerbWhite = Flat("Cordolo bianco", new Color(0.80f, 0.80f, 0.78f), 0.35f);
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
            mLeafA = Flat("Foglie A", new Color(0.10f, 0.25f, 0.06f), 0.1f);
            mLeafB = Flat("Foglie B", new Color(0.16f, 0.33f, 0.08f), 0.1f);
            mLeafC = Flat("Foglie C", new Color(0.24f, 0.38f, 0.10f), 0.1f);
            mPine = Flat("Pini", new Color(0.05f, 0.16f, 0.08f), 0.1f);
            mHill = Flat("Colline", new Color(0.30f, 0.37f, 0.30f), 0.05f);
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
            // beyond the terrain: a flat plain to the horizon, just under the edge of the countryside
            var far = new MB(1);
            const float R = 9000f;
            float cxm = (tx0 + tx1) * 0.5f, czm = (tz0 + tz1) * 0.5f, fy = baseY - 0.4f;
            far.Face(0, new Vector3(cxm - R, fy, czm - R), new Vector3(cxm - R, fy, czm + R), new Vector3(cxm + R, fy, czm + R), new Vector3(cxm + R, fy, czm - R), Vector3.up, 1f, 1f);
            for (int k = 0; k < far.uv.Count; k++) far.uv[k] = new Vector2(far.v[k].x / 9f, far.v[k].z / 9f);
            var fm = far.ToMesh("Pianura");
            made.Add(fm);
            MB.Spawn("Pianura", root, fm, new[] { mGrass }, false, false);
        }

        // ------------------------------------------------------------------ the hills on the horizon
        void BuildHills()
        {
            float cxm = (tx0 + tx1) * 0.5f, czm = (tz0 + tz1) * 0.5f;
            float rad = Mathf.Max(Mathf.Sqrt((tx1 - tx0) * (tx1 - tx0) + (tz1 - tz0) * (tz1 - tz0)) * 0.5f + 600f, 2400f);
            const int seg = 180;
            var b = new MB(1);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                float u = i / (float)seg;
                float n = PistaTex.Fbm(Mathf.Repeat(u * 1f, 1f), 0.37f, 18, 4, seed + 77);
                float r = rad * (1f + 0.04f * Mathf.Sin(a * 5f));
                float top = baseY + 40f + 190f * Mathf.Pow(n, 1.6f) * 2.2f;
                Vector3 p0 = new Vector3(cxm + Mathf.Sin(a) * r, baseY - 6f, czm + Mathf.Cos(a) * r);
                Vector3 p1 = new Vector3(cxm + Mathf.Sin(a) * (r - 120f), top, czm + Mathf.Cos(a) * (r - 120f));
                Vector3 nrm = new Vector3(-Mathf.Sin(a), 0.4f, -Mathf.Cos(a)).normalized;
                b.Vert(p0, nrm, new Vector2(u * 40f, 0f)); b.Vert(p1, nrm, new Vector2(u * 40f, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                b.Tri(0, a0, a1, b1); b.Tri(0, a0, b1, b0);
            }
            var mesh = b.ToMesh("Colline");
            made.Add(mesh);
            var g = MB.Spawn("Colline", root, mesh, new[] { mHill }, false, false);
            // double sided: the same ring seen from inside (the winding above faces outward)
            var b2 = new MB(1);
            for (int k = 0; k < b.v.Count; k++) b2.Vert(b.v[k], -b.n[k], b.uv[k]);
            for (int i = 0; i < seg; i++)
            {
                int a0 = i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                b2.Tri(0, a0, b1, a1); b2.Tri(0, a0, b0, b1);
            }
            var mesh2 = b2.ToMesh("Colline dentro");
            made.Add(mesh2);
            MB.Spawn("Colline dentro", root, mesh2, new[] { mHill }, false, false);
            g.name = "Colline fuori";
        }
    }
}

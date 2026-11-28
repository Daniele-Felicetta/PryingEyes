using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le mesh delle cose che stanno sulla sabbia (Prop), fatte a mano dal codice una volta sola: poche centinaia di vertici l'una, colore nei vertici (rgb) e parte che si piega al vento
    /// nell'alfa. Sassi di quattro tinte, massi, lastre in piedi a strati, cactus (alto con le braccia, a botte), cespugli secchi, alberi morti, ossa, colonne rotte, stele, mucchi di sassi,
    /// ciuffi d'erba e ciottoli. L'ombreggiatura e' morbida (le normali si mediano) tranne dove ci vuole uno spigolo (le lastre, le stele).</summary>
    public static partial class PropMeshes
    {
        static Mesh[] meshes, lowMeshes;

        public static Mesh Get(Prop p)
        {
            if (meshes == null) BuildAll();
            return meshes[(int)p];
        }

        /// <summary>I sassi, i massi, i cactus e i cespugli hanno due versioni: quella piena (Get) per quelli vicini e questa per quelli lontani, con la stessa sagoma e gli stessi colori
        /// ma meno triangoli (un quinto circa per i sassi). Le altre cose ne hanno una sola e questa restituisce quella.</summary>
        public static Mesh GetLow(Prop p)
        {
            if (!HasLow(p)) return Get(p);
            lowMeshes ??= new Mesh[(int)Prop.Count];
            return lowMeshes[(int)p] ??= IsBiomeTree(p) ? BiomeTree(p, true) :
                p == Prop.Cactus ? Cactus(true) : p == Prop.Bush ? Bush(true) : Stone(p, true);
        }

        /// <summary>Se il genere ha la versione per la distanza (GetLow): le sole cose che pesano (circa 1.670 triangoli la roccia, 3.580 il masso, 2.300 il cactus e 2.000 il cespuglio, contro i
        /// 480, 980, 350 e 520 di prima).</summary>
        public static bool HasLow(Prop p)
        {
            switch (p)
            {
                case Prop.RockTan: case Prop.RockDark: case Prop.RockRed: case Prop.RockWhite:
                case Prop.BoulderTan: case Prop.BoulderDark: case Prop.BoulderRed:
                case Prop.Cactus: case Prop.Bush: case Prop.Broadleaf: case Prop.Fir: case Prop.FrostFir: return true;
                default: return false;
            }
        }

        public static void Clear()
        {
            if (meshes != null) foreach (var m in meshes) if (m != null) UnityEngine.Object.Destroy(m);
            if (lowMeshes != null) foreach (var m in lowMeshes) if (m != null) UnityEngine.Object.Destroy(m);
            if (alternateTrees != null) foreach (var m in alternateTrees) if (m != null) UnityEngine.Object.Destroy(m);
            meshes = null; lowMeshes = null;
            alternateTrees = null;
            ClearExtra();
        }

        /// <summary>Di che materia e' fatta una mesh (UV0.y): il fragment di DesertProp.shader ci mette la grana giusta (pietra: chiazze, strati, polvere sopra; organico: venature lungo l'altezza;
        /// liscio: appena un po' di rumore).</summary>
        enum Surf { Stone = 0, Organic = 1, Plain = 2, GlacialIce = 4 }

        // ------------------------------------------------------------------ il costruttore
        sealed class PM
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Color32> c = new List<Color32>();
            public readonly List<bool> auto = new List<bool>();
            public readonly List<int> t = new List<int>();
            public readonly List<Vector2> uv = new List<Vector2>();
            /// <summary>La luce propria dei vertici che si aggiungono da qui in poi (UV0.x): 0 niente, fino a 0.5 un bagliore fisso, da 0.5 a 1 una fiamma (vedi DesertProp.shader).</summary>
            public float emit;
            readonly float surf;

            public PM(Surf s = Surf.Plain) { surf = (float)s; }

            public int V(Vector3 p, Color col, float sway = 0f)
            {
                v.Add(p); n.Add(Vector3.zero); col.a = sway; c.Add(col); auto.Add(true); uv.Add(new Vector2(emit, surf));
                return v.Count - 1;
            }

            public int Vn(Vector3 p, Vector3 nrm, Color col, float sway = 0f)
            {
                v.Add(p); n.Add(nrm.normalized); col.a = sway; c.Add(col); auto.Add(false); uv.Add(new Vector2(emit, surf));
                return v.Count - 1;
            }

            public void Tri(int a, int b, int d) { t.Add(a); t.Add(b); t.Add(d); }

            /// <summary>Una griglia di anelli x segmenti (l'ultimo segmento si chiude sul primo). Ogni quadrato guarda in fuori rispetto all'asse dell'anello.</summary>
            public void Grid(int rings, int segs, Func<int, int, Vector3> pos, Func<int, int, Color> col, Func<int, Vector3> axis, Func<int, int, float> sway = null)
            {
                int b0 = v.Count;
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < segs; j++) V(pos(i, j), col(i, j), sway != null ? sway(i, j) : 0f);
                for (int i = 0; i < rings - 1; i++)
                    for (int j = 0; j < segs; j++)
                    {
                        int a = b0 + i * segs + j, b = b0 + (i + 1) * segs + j, c2 = b0 + (i + 1) * segs + (j + 1) % segs, d = b0 + i * segs + (j + 1) % segs;
                        var nrm = Vector3.Cross(v[b] - v[a], v[c2] - v[a]);
                        if (nrm.sqrMagnitude < 1e-12f) nrm = Vector3.Cross(v[c2] - v[a], v[d] - v[a]);
                        bool ok = Vector3.Dot(nrm, (v[a] + v[c2]) * 0.5f - axis(i)) >= 0f;
                        if (ok) { Tri(a, b, c2); Tri(a, c2, d); } else { Tri(a, c2, b); Tri(a, d, c2); }
                    }
            }

            /// <summary>Il tappo di un anello (piatto): un ventaglio dal centro, con la faccia dalla parte di 'outward'.</summary>
            public void Cap(int ringStart, int segs, Vector3 center, Vector3 outward, Color col, float sway = 0f)
            {
                int cc = V(center, col, sway);
                for (int j = 0; j < segs; j++)
                {
                    int a = ringStart + j, b = ringStart + (j + 1) % segs;
                    if (Vector3.Dot(Vector3.Cross(v[a] - v[cc], v[b] - v[cc]), outward) > 0f) Tri(cc, a, b); else Tri(cc, b, a);
                }
            }

            public Mesh Done(string name)
            {
                var acc = new Vector3[v.Count];
                for (int i = 0; i < t.Count; i += 3)
                {
                    var fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    acc[t[i]] += fn; acc[t[i + 1]] += fn; acc[t[i + 2]] += fn;
                }
                for (int i = 0; i < v.Count; i++) if (auto[i]) n[i] = acc[i].sqrMagnitude > 1e-14f ? acc[i].normalized : Vector3.up;
                var mesh = new Mesh { name = name };
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetColors(c);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }

        static Color C(float r, float g, float b) => new Color(r, g, b, 0f);
        static float Nz(Vector3 p, int seed) => DesertNoise.Value(p.x * 3.1f + p.y * 1.7f + seed * 13.7f, p.z * 3.1f - p.y * 2.3f - seed * 7.1f, seed);

        // ------------------------------------------------------------------ i pezzi
        /// <summary>I piani che tagliano un sasso: ogni direzione esce dal solido dove incontra il primo piano (un poliedro convesso, con gli spigoli smussati) invece di essere una patata. Sei piani
        /// sugli assi chiudono sempre il solido; quelli a caso gli danno facce e creste vere.</summary>
        static Vector4[] RockPlanes(int seed, int n)
        {
            var rng = new System.Random(seed * 7919 + 13);
            var pl = new Vector4[n + 6];
            for (int i = 0; i < n; i++)
            {
                float y = (float)rng.NextDouble() * 2f - 1f, a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt(1f - y * y);
                pl[i] = new Vector4(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r, 0.74f + (float)rng.NextDouble() * 0.30f);
            }
            pl[n] = new Vector4(1f, 0f, 0f, 1.02f); pl[n + 1] = new Vector4(-1f, 0f, 0f, 1.02f);
            pl[n + 2] = new Vector4(0f, 1f, 0f, 0.96f + (float)rng.NextDouble() * 0.06f); pl[n + 3] = new Vector4(0f, -1f, 0f, 1.02f);
            pl[n + 4] = new Vector4(0f, 0f, 1f, 1.02f); pl[n + 5] = new Vector4(0f, 0f, -1f, 1.02f);
            return pl;
        }

        /// <summary>Quanto e' lontano il solido dei piani lungo una direzione (min morbido delle distanze dai piani) e quale piano e' il piu' vicino (per dare a ogni faccia una tinta sua).</summary>
        static float RockRadius(Vector3 dir, Vector4[] pl, out int which)
        {
            float sum = 0f, best = 99f; which = 0;
            const float s = 20f;
            for (int i = 0; i < pl.Length; i++)
            {
                float d = pl[i].x * dir.x + pl[i].y * dir.y + pl[i].z * dir.z;
                float r = d > 0.05f ? Mathf.Min(pl[i].w / d, 20f) : 20f;
                sum += Mathf.Exp(-s * r);
                if (r < best) { best = r; which = i; }
            }
            return Mathf.Min(-Mathf.Log(Mathf.Max(sum, 1e-30f)) / s, 1.25f);
        }

        /// <summary>Un sasso: una sfera schiacciata (con 'facets' piani a caso: un poliedro smussato; senza: spostata dal rumore), col fondo piatto e il colore che schiarisce in alto e cambia un poco da una
        /// faccia all'altra.</summary>
        static void Blob(PM m, Vector3 c, Vector3 r, int seed, float rough, Color dark, Color light, int segs = 9, int rings = 6, float flat = 0.4f, float sway = 0f, int facets = 0)
        {
            var planes = facets > 0 ? RockPlanes(seed, facets) : null;
            Func<int, int, Vector3> pos = (i, j) =>
            {
                float phi = Mathf.PI * (i / (float)(rings - 1) - 0.5f), th = j * Mathf.PI * 2f / segs;
                float y = Mathf.Max(Mathf.Sin(phi), -flat), rr = Mathf.Cos(phi);
                var dir = new Vector3(Mathf.Cos(th) * rr, y, Mathf.Sin(th) * rr);
                float k;
                if (planes != null) k = RockRadius(dir.normalized, planes, out _) * (1f + rough * ((Nz(dir, seed) - 0.5f) + 0.55f * (Nz(dir * 2.7f, seed + 31) - 0.5f)));      // the second octave chips the faces
                else k = 1f + rough * (Nz(dir, seed) - 0.5f) * 2f;
                return c + Vector3.Scale(dir * k, r);
            };
            Func<int, int, Color> col = (i, j) =>
            {
                var p = pos(i, j);
                float h = Mathf.Clamp01(((p.y - c.y) / Mathf.Max(0.01f, r.y) + 1f) * 0.5f + (Nz(p, seed + 5) - 0.5f) * 0.35f);
                var colr = Color.Lerp(dark, light, h);
                if (planes != null)
                {
                    float phi = Mathf.PI * (i / (float)(rings - 1) - 0.5f), th = j * Mathf.PI * 2f / segs, rr = Mathf.Cos(phi);
                    var dir = new Vector3(Mathf.Cos(th) * rr, Mathf.Max(Mathf.Sin(phi), -flat), Mathf.Sin(th) * rr).normalized;
                    RockRadius(dir, planes, out int which);
                    float tint = 0.90f + 0.20f * (float)((which * 37 + seed * 11) % 17) / 16f;
                    colr.r *= tint; colr.g *= tint; colr.b *= tint;
                }
                float band = Mathf.Sin(p.y * 9.5f + (Nz(p, seed + 9) - 0.5f) * 5f) * 0.5f + 0.5f;      // layered stone: bands that wander with the noise
                float shade = 0.90f + 0.20f * band;
                colr.r *= shade; colr.g *= shade; colr.b *= shade;
                return colr;
            };
            m.Grid(rings, segs, pos, col, i => c, sway > 0f ? (Func<int, int, float>)((i, j) => sway) : null);
        }

        static void Tube(PM m, Vector3[] path, float[] radius, int sides, Func<int, int, Color> col, float swayBase = 0f, float swayTip = 0f, bool capTop = false, Func<float, float> rib = null)
        {
            int rings = path.Length;
            var S = new Vector3[rings]; var U = new Vector3[rings];
            for (int i = 0; i < rings; i++)
            {
                var tg = (path[Mathf.Min(i + 1, rings - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                var up = Mathf.Abs(tg.y) < 0.9f ? Vector3.up : Vector3.right;
                S[i] = Vector3.Cross(up, tg).normalized; U[i] = Vector3.Cross(tg, S[i]);
            }
            int b0 = m.v.Count;
            m.Grid(rings, sides, (i, j) => { float a = j * Mathf.PI * 2f / sides; return path[i] + (S[i] * Mathf.Cos(a) + U[i] * Mathf.Sin(a)) * (radius[i] * (rib != null ? rib(a) : 1f)); }, col, i => path[i],
                   (i, j) => Mathf.Lerp(swayBase, swayTip, i / (float)Mathf.Max(1, rings - 1)));
            if (capTop) m.Cap(b0 + (rings - 1) * sides, sides, path[rings - 1], path[rings - 1] - path[rings - 2], col(rings - 1, 0), swayTip);
        }

        /// <summary>Un parallelepipedo che parte da terra (la base e' sotto 'foot'), inclinabile, con le facce laterali a fasce di colore (gli strati di una lastra, le righe di una stele).</summary>
        static void Slab(PM m, Vector3 foot, Vector3 size, float yaw, float tiltX, float tiltZ, Func<float, Color> colAtY, int bands)
        {
            var q = Quaternion.Euler(tiltX, yaw, tiltZ);
            Vector3 P(float x, float y, float z) => foot + q * new Vector3(x, y, z);
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            var corners = new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
            for (int f = 0; f < 4; f++)
            {
                var a = corners[f]; var b = corners[(f + 1) % 4];
                var nrm = q * new Vector3((a.y - b.y), 0f, (b.x - a.x));
                for (int k = 0; k < bands; k++)
                {
                    float y0 = size.y * k / bands, y1 = size.y * (k + 1) / bands;
                    var col = colAtY((k + 0.5f) / bands);
                    int i0 = m.Vn(P(a.x, y0, a.y), nrm, col), i1 = m.Vn(P(b.x, y0, b.y), nrm, col), i2 = m.Vn(P(b.x, y1, b.y), nrm, col), i3 = m.Vn(P(a.x, y1, a.y), nrm, col);
                    var fn = Vector3.Cross(m.v[i2] - m.v[i0], m.v[i1] - m.v[i0]);
                    if (Vector3.Dot(fn, nrm) > 0f) { m.Tri(i0, i2, i1); m.Tri(i0, i3, i2); } else { m.Tri(i0, i1, i2); m.Tri(i0, i2, i3); }
                }
            }
            var topN = q * Vector3.up; var topC = colAtY(1f);
            int t0 = m.Vn(P(-hx, size.y, -hz), topN, topC), t1 = m.Vn(P(hx, size.y, -hz), topN, topC), t2 = m.Vn(P(hx, size.y, hz), topN, topC), t3 = m.Vn(P(-hx, size.y, hz), topN, topC);
            if (Vector3.Dot(Vector3.Cross(m.v[t2] - m.v[t0], m.v[t1] - m.v[t0]), topN) > 0f) { m.Tri(t0, t2, t1); m.Tri(t0, t3, t2); } else { m.Tri(t0, t1, t2); m.Tri(t0, t2, t3); }
        }

        static void Blade(PM m, Vector3 b, Vector3 lean, float width, float height, Color baseCol, Color tipCol)
        {
            var side = Vector3.Cross(Vector3.up, lean.sqrMagnitude > 1e-6f ? lean : Vector3.forward).normalized * width * 0.5f;
            var mid = b + lean * 0.45f + Vector3.up * height * 0.55f; var tip = b + lean + Vector3.up * height;
            var nrm = (Vector3.up + lean.normalized * 0.4f).normalized;
            var cm = Color.Lerp(baseCol, tipCol, 0.5f);
            int bl = m.Vn(b - side, nrm, baseCol, 0f), br = m.Vn(b + side, nrm, baseCol, 0f);
            int ml = m.Vn(mid - side * 0.6f, nrm, cm, 0.5f), mr = m.Vn(mid + side * 0.6f, nrm, cm, 0.5f), tp = m.Vn(tip, nrm, tipCol, 1f);
            // le due facce
            m.Tri(bl, ml, br); m.Tri(br, ml, mr); m.Tri(ml, tp, mr);
            m.Tri(bl, br, ml); m.Tri(br, mr, ml); m.Tri(ml, mr, tp);
        }

        // ------------------------------------------------------------------ tutte le mesh
        static void BuildAll()
        {
            meshes = new Mesh[(int)Prop.Count];
            meshes[(int)Prop.Tuft] = Tuft();
            meshes[(int)Prop.Pebbles] = Pebbles();
            // le tinte stanno basse (il sole pieno e il cielo le schiariscono molto): la pietra al sole di mezzogiorno non e' bianca, e' beige, ocra, bruna, rossa o grigio fumo
            meshes[(int)Prop.RockTan] = Stone(Prop.RockTan, false);
            meshes[(int)Prop.RockDark] = Stone(Prop.RockDark, false);
            meshes[(int)Prop.RockRed] = Stone(Prop.RockRed, false);
            meshes[(int)Prop.RockWhite] = Stone(Prop.RockWhite, false);
            meshes[(int)Prop.BoulderTan] = Stone(Prop.BoulderTan, false);
            meshes[(int)Prop.BoulderDark] = Stone(Prop.BoulderDark, false);
            meshes[(int)Prop.BoulderRed] = Stone(Prop.BoulderRed, false);
            meshes[(int)Prop.Slab] = Slabs();
            meshes[(int)Prop.Cactus] = Cactus(false);
            meshes[(int)Prop.Barrel] = Barrel();
            meshes[(int)Prop.Bush] = Bush(false);
            meshes[(int)Prop.DeadTree] = DeadTree();
            meshes[(int)Prop.Bones] = Bones();
            meshes[(int)Prop.Column] = Column();
            meshes[(int)Prop.Stele] = Stele();
            meshes[(int)Prop.Cairn] = Cairn();
            meshes[(int)Prop.Tent] = Tent();
            meshes[(int)Prop.FirePit] = FirePit();
            meshes[(int)Prop.Crate] = Crate();
            meshes[(int)Prop.Well] = Well();
            meshes[(int)Prop.Obelisk] = Obelisk();
            meshes[(int)Prop.Windmill] = Windmill();
            meshes[(int)Prop.Wagon] = Wagon();
            meshes[(int)Prop.Broadleaf] = BiomeTree(Prop.Broadleaf, false);
            meshes[(int)Prop.Fir] = BiomeTree(Prop.Fir, false);
            meshes[(int)Prop.FrostFir] = BiomeTree(Prop.FrostFir, false);
            meshes[(int)Prop.MeadowGrass] = MeadowGrass(false);
            meshes[(int)Prop.Wildflowers] = MeadowGrass(true);
            meshes[(int)Prop.GlacierShard] = GlacierShard();
            meshes[(int)Prop.ForestFern] = ForestFern();
        }

        static Mesh Tuft()
        {
            var m = new PM(Surf.Organic); var rng = new System.Random(11);
            for (int i = 0; i < 9; i++)
            {
                float a = (float)rng.NextDouble() * 6.283f, d = 0.03f + (float)rng.NextDouble() * 0.12f;
                var b = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                var lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.10f + (float)rng.NextDouble() * 0.22f);
                Blade(m, b, lean, 0.05f, 0.30f + (float)rng.NextDouble() * 0.28f, C(0.36f, 0.30f, 0.14f), C(0.80f, 0.70f, 0.38f));
            }
            return m.Done("Tuft");
        }

        static Mesh Pebbles()
        {
            var m = new PM(Surf.Stone); var rng = new System.Random(12);
            for (int i = 0; i < 6; i++)
            {
                float a = (float)rng.NextDouble() * 6.283f, d = (float)rng.NextDouble() * 0.5f, r = 0.05f + (float)rng.NextDouble() * 0.09f;
                float tone = 0.36f + (float)rng.NextDouble() * 0.26f;
                Blob(m, new Vector3(Mathf.Cos(a) * d, r * 0.3f, Mathf.Sin(a) * d), new Vector3(r * 1.3f, r * 0.8f, r), 20 + i, 0.18f, C(tone * 0.6f, tone * 0.52f, tone * 0.42f), C(tone * 1.1f, tone, tone * 0.82f), 7, 4, 0.3f, 0f, 7);
            }
            return m.Done("Pebbles");
        }

        /// <summary>Un sasso o un masso: la tinta e il seme di ognuno stanno qui, e 'low' dice se e' la versione per la distanza (GetLow).</summary>
        static Mesh Stone(Prop p, bool low)
        {
            switch (p)
            {
                case Prop.RockTan: return Rock("RockTan", 1, C(0.27f, 0.20f, 0.13f), C(0.52f, 0.41f, 0.28f), low);
                case Prop.RockDark: return Rock("RockDark", 2, C(0.12f, 0.115f, 0.115f), C(0.32f, 0.295f, 0.27f), low);
                case Prop.RockRed: return Rock("RockRed", 3, C(0.31f, 0.14f, 0.095f), C(0.58f, 0.32f, 0.20f), low);
                case Prop.RockWhite: return Rock("RockWhite", 4, C(0.30f, 0.285f, 0.255f), C(0.56f, 0.53f, 0.47f), low);
                case Prop.BoulderTan: return Boulder("BoulderTan", 5, C(0.24f, 0.18f, 0.115f), C(0.50f, 0.39f, 0.26f), low);
                case Prop.BoulderDark: return Boulder("BoulderDark", 6, C(0.105f, 0.10f, 0.10f), C(0.30f, 0.275f, 0.25f), low);
                default: return Boulder("BoulderRed", 7, C(0.28f, 0.125f, 0.085f), C(0.56f, 0.30f, 0.19f), low);
            }
        }

        /// <summary>Un sasso: tre blob (il corpo e due sassolini accanto). La versione 'low' ha gli stessi piani (la stessa sagoma) ma meno anelli e segmenti: il rumore piu' fine non si vede da lontano.</summary>
        static Mesh Rock(string name, int seed, Color dark, Color light, bool low)
        {
            var m = new PM(Surf.Stone);
            Blob(m, new Vector3(0f, 0.30f, 0f), new Vector3(0.62f, 0.46f, 0.55f), seed, 0.20f, dark, light, low ? 15 : 28, low ? 10 : 18, 0.4f, 0f, 12);
            Blob(m, new Vector3(0.50f, 0.13f, 0.16f), new Vector3(0.28f, 0.2f, 0.26f), seed + 1, 0.2f, dark, light, low ? 10 : 18, low ? 7 : 12, 0.4f, 0f, 9);
            Blob(m, new Vector3(-0.36f, 0.10f, -0.34f), new Vector3(0.22f, 0.16f, 0.2f), seed + 2, 0.2f, dark, light, low ? 9 : 16, low ? 6 : 11, 0.4f, 0f, 9);
            return m.Done(low ? name + "Low" : name);
        }

        static Mesh Boulder(string name, int seed, Color dark, Color light, bool low)
        {
            var m = new PM(Surf.Stone);
            Blob(m, new Vector3(0f, 0.85f, 0f), new Vector3(1.25f, 1.0f, 1.1f), seed, 0.22f, dark, light, low ? 20 : 36, low ? 13 : 24, 0.5f, 0f, 15);
            Blob(m, new Vector3(0.95f, 0.38f, 0.45f), new Vector3(0.6f, 0.5f, 0.55f), seed + 1, 0.22f, dark, light, low ? 13 : 24, low ? 8 : 16, 0.5f, 0f, 11);
            Blob(m, new Vector3(-0.7f, 0.42f, -0.65f), new Vector3(0.62f, 0.55f, 0.5f), seed + 2, 0.22f, dark, light, low ? 13 : 24, low ? 8 : 16, 0.5f, 0f, 11);
            Blob(m, new Vector3(0.2f, 1.62f, -0.1f), new Vector3(0.5f, 0.38f, 0.46f), seed + 3, 0.24f, dark, light, low ? 11 : 20, low ? 7 : 13, 0.5f, 0f, 11);
            return m.Done(low ? name + "Low" : name);
        }

        /// <summary>Una lastra di roccia stratificata: un parallelepipedo che parte da terra con le facce ondulate dal rumore (nessuna e' un piano: la roccia si scheggia), gli strati di colore con il bordo
        /// storto, la cima spezzata e un po' inclinata. Le facce hanno vertici propri (gli spigoli restano vivi) e le normali si mediano dentro ogni faccia.</summary>
        static void RoughSlab(PM m, Vector3 foot, Vector3 size, float yaw, float tiltX, float tiltZ, Color[] layers, int seed, float bump = 0.07f)
        {
            var q = Quaternion.Euler(tiltX, yaw, tiltZ);
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            int nx = 7, ny = 12;
            Func<float, float, float, Vector3> corner = (x, y, z) => foot + q * new Vector3(x, y, z);
            Func<float, float, float, Color> colour = (x, y, z) =>
            {
                float yy = Mathf.Clamp01(y / size.y + (Nz(new Vector3(x * 0.8f, y * 0.4f, z * 0.8f), seed + 9) - 0.5f) * 0.28f);
                int k = Mathf.Min(layers.Length - 1, (int)(yy * layers.Length));
                float within = yy * layers.Length - k;
                var cl = layers[k];
                float edge = 0.80f + 0.20f * Mathf.SmoothStep(0f, 0.18f, within) * Mathf.SmoothStep(1f, 0.82f, within);      // un filo piu' scuro tra uno strato e l'altro
                float mott = 0.88f + 0.24f * Nz(new Vector3(x * 2.1f, y * 1.3f, z * 2.1f), seed + 3);
                return new Color(cl.r * edge * mott, cl.g * edge * mott, cl.b * edge * mott, 0f);
            };
            // quattro facce laterali e la cima: (origine, asse u, asse v, normale uscente, passi in u, passi in v)
            var faces = new List<(Vector3 o, Vector3 u, Vector3 v, Vector3 n, int su, int sv)>
            {
                (new Vector3(-hx, 0f, -hz), new Vector3(2f * hx, 0f, 0f), new Vector3(0f, size.y, 0f), new Vector3(0f, 0f, -1f), nx, ny),
                (new Vector3(hx, 0f, -hz), new Vector3(0f, 0f, 2f * hz), new Vector3(0f, size.y, 0f), new Vector3(1f, 0f, 0f), 3, ny),
                (new Vector3(hx, 0f, hz), new Vector3(-2f * hx, 0f, 0f), new Vector3(0f, size.y, 0f), new Vector3(0f, 0f, 1f), nx, ny),
                (new Vector3(-hx, 0f, hz), new Vector3(0f, 0f, -2f * hz), new Vector3(0f, size.y, 0f), new Vector3(-1f, 0f, 0f), 3, ny),
                (new Vector3(-hx, size.y, -hz), new Vector3(2f * hx, 0f, 0f), new Vector3(0f, 0f, 2f * hz), new Vector3(0f, 1f, 0f), nx, 3),
            };
            foreach (var f in faces)
            {
                int[,] id = new int[f.su + 1, f.sv + 1];
                for (int j = 0; j <= f.sv; j++)
                    for (int i = 0; i <= f.su; i++)
                    {
                        Vector3 p = f.o + f.u * (i / (float)f.su) + f.v * (j / (float)f.sv);
                        // lo spostamento dipende solo dalla posizione: i vertici sullo spigolo tra due facce finiscono nello stesso punto e il solido resta chiuso
                        float amp = bump * Mathf.Min(size.x, size.y);
                        var pn = new Vector3(p.x * 1.6f, p.y * 1.3f, p.z * 1.6f);
                        p += new Vector3(Nz(pn, seed) - 0.5f, Nz(pn + new Vector3(5.2f, 1.3f, 9.1f), seed + 1) - 0.5f, Nz(pn + new Vector3(2.7f, 8.3f, 4.4f), seed + 2) - 0.5f) * (2f * amp);
                        // la cima e' scheggiata: scende piu' in basso dove il rumore lo dice
                        p.y += Mathf.SmoothStep(0.72f, 1f, p.y / size.y) * (Nz(new Vector3(p.x * 1.7f, 0f, p.z * 1.7f), seed + 33) - 0.62f) * 0.20f * size.y;
                        id[i, j] = m.V(corner(p.x, p.y, p.z), colour(p.x, p.y, p.z));
                    }
                var nrm = q * f.n;
                for (int j = 0; j < f.sv; j++)
                    for (int i = 0; i < f.su; i++)
                    {
                        int a = id[i, j], b = id[i + 1, j], c2 = id[i + 1, j + 1], d = id[i, j + 1];
                        var fn = Vector3.Cross(m.v[c2] - m.v[a], m.v[b] - m.v[a]);
                        if (Vector3.Dot(fn, nrm) > 0f) { m.Tri(a, c2, b); m.Tri(a, d, c2); } else { m.Tri(a, b, c2); m.Tri(a, c2, d); }
                    }
            }
        }

        /// <summary>Tre lastre storte, a strati di colore (rosso, ocra, bianco sporco): gli affioramenti dei calanchi.</summary>
        static Mesh Slabs()
        {
            var m = new PM(Surf.Stone);
            Color[] layers = { C(0.34f, 0.17f, 0.12f), C(0.52f, 0.33f, 0.20f), C(0.40f, 0.22f, 0.15f), C(0.62f, 0.48f, 0.33f), C(0.45f, 0.25f, 0.16f), C(0.56f, 0.39f, 0.25f) };
            RoughSlab(m, new Vector3(0f, -0.1f, 0f), new Vector3(1.4f, 2.5f, 0.55f), 15f, 4f, -3f, layers, 71);
            RoughSlab(m, new Vector3(1.0f, -0.1f, 0.6f), new Vector3(1.0f, 1.7f, 0.5f), -28f, -6f, 5f, layers, 72);
            RoughSlab(m, new Vector3(-0.9f, -0.1f, 0.5f), new Vector3(0.9f, 1.2f, 0.6f), 40f, 3f, 8f, layers, 73);
            Blob(m, new Vector3(0.1f, 0.2f, 0.4f), new Vector3(1.5f, 0.45f, 1.0f), 31, 0.2f, C(0.30f, 0.17f, 0.115f), C(0.50f, 0.32f, 0.21f), 14, 8, 0.5f, 0f, 9);
            return m.Done("Slab");
        }

        /// <summary>Un cactus saguaro: il fusto e due bracci a coste (otto sul fusto, sei sui bracci: una cresta e una gola ciascuna, e la forma e il colore seguono lo stesso coseno, quattro lati per costa),
        /// percorsi morbidi e punte a cupola; la cima e' un po' piu' gialla. Prima le coste erano due lati e il fusto sembrava liscio, di plastica. La versione 'low' (da lontano) ha meno anelli e meno lati.</summary>
        static Mesh Cactus(bool low)
        {
            var m = new PM(Surf.Organic);
            int rings = low ? 5 : 12, sides = low ? 16 : 32, armSides = low ? 12 : 24;
            Func<float, float> trunkRib = a => 1f + 0.12f * Mathf.Cos(a * 8f), armRib = a => 1f + 0.13f * Mathf.Cos(a * 6f);
            Limb(m, new[] { new Vector3(0f, 0f, 0f), new Vector3(0.02f, 0.7f, 0f), new Vector3(0f, 1.5f, 0.02f), new Vector3(-0.02f, 2.25f, 0f) },
                 new[] { 0.25f, 0.23f, 0.22f, 0.19f }, rings, sides, 8, trunkRib);
            Limb(m, new[] { new Vector3(0.1f, 1.0f, 0f), new Vector3(0.55f, 1.05f, 0f), new Vector3(0.78f, 1.35f, 0f), new Vector3(0.8f, 2.0f, 0f) },
                 new[] { 0.13f, 0.12f, 0.12f, 0.11f }, rings, armSides, 6, armRib);
            Limb(m, new[] { new Vector3(-0.1f, 1.45f, 0.02f), new Vector3(-0.5f, 1.5f, 0.1f), new Vector3(-0.68f, 1.75f, 0.12f), new Vector3(-0.7f, 2.2f, 0.12f) },
                 new[] { 0.12f, 0.11f, 0.11f, 0.1f }, rings, armSides, 6, armRib);
            return m.Done(low ? "CactusLow" : "Cactus");
        }

        /// <summary>Un ramo di cactus: un percorso morbido (Catmull-Rom) per i punti di controllo con il raggio che li segue, tre anelli in piu' che chiudono la punta a cupola (30, 60 e 85 gradi) e il colore a coste
        /// (le creste piu' chiare delle gole, la cima un po' piu' gialla).</summary>
        static void Limb(PM m, Vector3[] ctrl, float[] radius, int rings, int sides, int flutes, Func<float, float> rib)
        {
            int total = rings + 3;
            var path = new Vector3[total]; var rad = new float[total];
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1) * (ctrl.Length - 1);
                int k = Mathf.Min((int)t, ctrl.Length - 2); float u = t - k;
                Vector3 p0 = ctrl[Mathf.Max(k - 1, 0)], p1 = ctrl[k], p2 = ctrl[k + 1], p3 = ctrl[Mathf.Min(k + 2, ctrl.Length - 1)];
                path[i] = 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (3f * p1 - p0 - 3f * p2 + p3) * u * u * u);
                rad[i] = Mathf.Lerp(radius[k], radius[k + 1], u);
            }
            Vector3 up = (path[rings - 1] - path[rings - 2]).normalized; float rTop = rad[rings - 1];
            float[] dome = { 30f, 60f, 85f };
            for (int d = 0; d < 3; d++)
            {
                float a = dome[d] * Mathf.Deg2Rad;
                path[rings + d] = path[rings - 1] + up * (rTop * Mathf.Sin(a));
                rad[rings + d] = rTop * Mathf.Cos(a);
            }
            Func<int, int, Color> col = (i, j) =>
            {
                float a = j * Mathf.PI * 2f / sides, ridge = 0.5f + 0.5f * Mathf.Cos(a * flutes), v = i / (float)(total - 1);
                var c = Color.Lerp(C(0.13f, 0.24f, 0.11f), C(0.30f, 0.43f, 0.22f), ridge);
                return Color.Lerp(c, C(0.46f, 0.52f, 0.27f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.78f, 1f, v)) * 0.55f);
            };
            Tube(m, path, rad, sides, col, 0f, 0f, true, rib);
        }

        static Mesh Barrel()
        {
            var m = new PM(Surf.Organic);
            int segs = 12, rings = 6;
            m.Grid(rings, segs, (i, j) =>
            {
                float phi = Mathf.PI * (i / (float)(rings - 1) - 0.5f), th = j * Mathf.PI * 2f / segs;
                float y = Mathf.Max(Mathf.Sin(phi), -0.3f), rr = Mathf.Cos(phi) * (1f + 0.09f * Mathf.Cos(th * 6f));
                return new Vector3(Mathf.Cos(th) * rr * 0.34f, 0.2f + y * 0.24f, Mathf.Sin(th) * rr * 0.34f);
            }, (i, j) => (j % 2 == 0) ? C(0.22f, 0.36f, 0.18f) : C(0.32f, 0.48f, 0.25f), i => new Vector3(0f, 0.2f, 0f));
            Blob(m, new Vector3(0f, 0.46f, 0f), new Vector3(0.1f, 0.05f, 0.1f), 41, 0.1f, C(0.8f, 0.45f, 0.5f), C(0.95f, 0.75f, 0.3f), 6, 4, 0.5f);
            return m.Done("Barrel");
        }

        /// <summary>Un cespuglio secco: sedici rami con tre ciuffi di foglie ciascuno. La versione 'low' (da lontano) ha lo stesso ingombro con un ciuffo solo, piu' largo, in punta a ogni ramo.</summary>
        static Mesh Bush(bool low)
        {
            var m = new PM(Surf.Organic); var rng = new System.Random(21);
            for (int i = 0; i < 16; i++)
            {
                float a = (float)rng.NextDouble() * 6.283f, lean = 0.25f + (float)rng.NextDouble() * 0.5f, hgt = 0.55f + (float)rng.NextDouble() * 0.45f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p0 = dir * 0.02f; var p1 = dir * lean * 0.4f + Vector3.up * hgt * 0.5f; var p2 = dir * lean + Vector3.up * hgt;
                Tube(m, new[] { p0, p1, p2 }, new[] { 0.022f, 0.016f, 0.008f }, 4, (k, j) => Color.Lerp(C(0.30f, 0.22f, 0.13f), C(0.46f, 0.38f, 0.22f), k * 0.5f), 0f, 0.8f);
                if (low)
                {
                    Blob(m, p2 + Vector3.up * 0.02f, new Vector3(0.22f, 0.13f, 0.22f), 50 + i, 0.3f, C(0.20f, 0.24f, 0.12f), C(0.48f, 0.52f, 0.28f), 6, 4, 0.5f, 1f);
                    continue;
                }
                // three tufts of leaves along each stem (the tip, the middle and one at the side), in a sage green: the old single yellow ball on each stem read as a lollipop
                Blob(m, p2 + Vector3.up * 0.02f, new Vector3(0.15f, 0.09f, 0.15f), 50 + i, 0.3f, C(0.20f, 0.24f, 0.12f), C(0.50f, 0.54f, 0.29f), 6, 4, 0.5f, 1f);
                Blob(m, Vector3.Lerp(p1, p2, 0.55f) + dir * 0.03f, new Vector3(0.11f, 0.07f, 0.11f), 70 + i, 0.3f, C(0.20f, 0.24f, 0.12f), C(0.46f, 0.50f, 0.27f), 6, 4, 0.5f, 0.8f);
                Blob(m, p1 + Quaternion.AngleAxis(90f, Vector3.up) * dir * 0.07f, new Vector3(0.09f, 0.06f, 0.09f), 90 + i, 0.3f, C(0.18f, 0.22f, 0.11f), C(0.42f, 0.47f, 0.25f), 6, 4, 0.5f, 0.5f);
            }
            return m.Done(low ? "BushLow" : "Bush");
        }

        /// <summary>Un ramo che si piega a caso e si assottiglia, con rami figli che nascono a meta' e in punta: gli alberi morti del deserto sono nodosi e ramificati, non pali con degli stuzzicadenti.</summary>
        static void Branch(PM m, System.Random rng, Vector3 p0, Vector3 dir, float len, float r0, int depth, Func<int, int, Color> bark, float rise)
        {
            const int N = 5;
            var pts = new Vector3[N]; var rad = new float[N];
            var d = dir.normalized; var p = p0;
            Func<float, float> R = a => ((float)rng.NextDouble() * 2f - 1f) * a;
            for (int i = 0; i < N; i++)
            {
                pts[i] = p; rad[i] = Mathf.Lerp(r0, r0 * 0.28f, i / (float)(N - 1));
                d = (d + new Vector3(R(0.34f), R(0.14f) + rise, R(0.34f))).normalized;
                p += d * (len / (N - 1));
            }
            Tube(m, pts, rad, 5, bark, 0f, 0f, true);
            if (depth <= 0) return;
            int kids = depth >= 2 ? 3 : 2;
            for (int k = 0; k < kids; k++)
            {
                int at = k == kids - 1 ? N - 1 : 1 + k;                              // l'ultimo parte dalla punta (il ramo si biforca), gli altri dal fianco
                var side = Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.up) * Vector3.right;
                var dd = (d * 0.8f + side * (0.7f + (float)rng.NextDouble() * 0.5f) + Vector3.up * 0.25f).normalized;
                Branch(m, rng, pts[at], dd, len * (0.50f + (float)rng.NextDouble() * 0.2f), rad[at] * 0.72f, depth - 1, bark, rise * 0.7f);
            }
        }

        static Mesh DeadTree()
        {
            var m = new PM(Surf.Organic); var rng = new System.Random(31);
            Func<int, int, Color> bark = (i, j) => (j % 3 == 0) ? C(0.20f, 0.17f, 0.145f) : (j % 3 == 1) ? C(0.34f, 0.30f, 0.25f) : C(0.27f, 0.235f, 0.20f);
            // il tronco: svasato alla base (le radici), storto, spezzato in cima con la punta scheggiata
            var trunk = new[] { new Vector3(0f, -0.2f, 0f), new Vector3(0.03f, 0.25f, 0.02f), new Vector3(0.10f, 0.9f, 0.04f), new Vector3(-0.02f, 1.7f, 0.10f), new Vector3(-0.10f, 2.5f, 0.02f), new Vector3(0.00f, 3.2f, -0.06f), new Vector3(0.10f, 3.5f, -0.02f) };
            Tube(m, trunk, new[] { 0.52f, 0.34f, 0.27f, 0.22f, 0.17f, 0.12f, 0.06f }, 9, bark, 0f, 0f, true);
            // radici che affiorano
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.6f + 0.4f + (float)rng.NextDouble() * 0.5f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Tube(m, new[] { dir * 0.18f + Vector3.up * 0.30f, dir * 0.55f + Vector3.up * 0.10f, dir * 1.0f + Vector3.down * 0.04f }, new[] { 0.14f, 0.09f, 0.03f }, 5, bark, 0f, 0f, true);
            }
            // i rami: pochi, grossi, che escono in alto e si piegano verso l'alto; ognuno si ramifica
            for (int i = 0; i < 5; i++)
            {
                float h = 1.2f + i * 0.55f + (float)rng.NextDouble() * 0.3f, a = i * 2.4f + (float)rng.NextDouble() * 0.8f;
                var dir = new Vector3(Mathf.Cos(a), 0.55f, Mathf.Sin(a));
                float r0 = 0.115f - 0.012f * i;
                var p0 = new Vector3(0f, h, 0f) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.10f;
                Branch(m, rng, p0, dir, 1.15f - 0.08f * i, r0, 2, bark, 0.10f);
            }
            return m.Done("DeadTree");
        }

        static Mesh Bones()
        {
            var m = new PM();
            Func<int, int, Color> bone = (i, j) => Color.Lerp(C(0.62f, 0.58f, 0.48f), C(0.88f, 0.84f, 0.74f), (j % 4) / 3f);
            Tube(m, new[] { new Vector3(-0.55f, 0.04f, 0f), new Vector3(0f, 0.07f, 0f), new Vector3(0.55f, 0.04f, 0f) }, new[] { 0.026f, 0.03f, 0.026f }, 5, bone, 0f, 0f, true);
            for (int i = 0; i < 7; i++)
            {
                float x = -0.42f + i * 0.14f, s = 1f - Mathf.Abs(i - 3) * 0.1f;
                for (int side = -1; side <= 1; side += 2)
                    Tube(m, new[] { new Vector3(x, 0.05f, side * 0.02f), new Vector3(x + 0.01f, 0.16f * s, side * 0.12f * s), new Vector3(x + 0.02f, 0.27f * s, side * 0.25f * s), new Vector3(x + 0.04f, 0.2f * s, side * 0.4f * s), new Vector3(x + 0.05f, 0.05f, side * 0.46f * s) },
                         new[] { 0.02f, 0.019f, 0.017f, 0.014f, 0.009f }, 4, bone, 0f, 0f, true);
            }
            Blob(m, new Vector3(0.7f, 0.1f, 0f), new Vector3(0.14f, 0.1f, 0.1f), 61, 0.1f, C(0.6f, 0.56f, 0.46f), C(0.9f, 0.86f, 0.76f), 8, 5, 0.4f);
            Blob(m, new Vector3(0.82f, 0.05f, 0f), new Vector3(0.09f, 0.04f, 0.07f), 62, 0.1f, C(0.6f, 0.56f, 0.46f), C(0.9f, 0.86f, 0.76f), 6, 4, 0.4f);
            return m.Done("Bones");
        }

        static Mesh Column()
        {
            var m = new PM(Surf.Stone); var rng = new System.Random(41);
            int segs = 10;
            Func<int, int, Color> flute = (i, j) => (j % 2 == 0) ? C(0.45f, 0.42f, 0.36f) : C(0.63f, 0.59f, 0.51f);
            float[] ys = { 0f, 0.5f, 1.0f, 1.6f, 2.1f };
            float[] bump = { 0f, 0f, 0f, 0f, 0f };
            int b0 = m.v.Count;
            m.Grid(ys.Length, segs, (i, j) =>
            {
                float a = j * Mathf.PI * 2f / segs, r = 0.46f - 0.025f * i;
                float jag = i == ys.Length - 1 ? -0.22f * (0.3f + 0.7f * (float)((j * 7 + 3) % 5) / 4f) : 0f;
                return new Vector3(Mathf.Cos(a) * r, ys[i] + jag, Mathf.Sin(a) * r);
            }, flute, i => new Vector3(0f, ys[i], 0f));
            m.Cap(b0 + (ys.Length - 1) * segs, segs, new Vector3(0f, 1.95f, 0f), Vector3.up, C(0.55f, 0.51f, 0.44f));
            Slab(m, new Vector3(0f, -0.05f, 0f), new Vector3(1.1f, 0.3f, 1.1f), 8f, 0f, 0f, y => C(0.40f, 0.37f, 0.32f), 1);
            // un rocchio caduto accanto
            var drum = new[] { new Vector3(0.9f, 0.4f, 0.3f), new Vector3(1.3f, 0.4f, 0.32f), new Vector3(1.7f, 0.41f, 0.34f) };
            Tube(m, drum, new[] { 0.4f, 0.4f, 0.4f }, 10, flute, 0f, 0f, true);
            Tube(m, new[] { drum[0] - new Vector3(0.002f, 0f, 0.002f), drum[0] }, new[] { 0.01f, 0.4f }, 10, flute);
            return m.Done("Column");
        }

        static Mesh Stele()
        {
            var m = new PM(Surf.Stone);
            Func<float, Color> carved = y => (y > 0.35f && y < 0.85f && ((int)(y * 26f)) % 2 == 0) ? C(0.37f, 0.34f, 0.29f) : C(0.62f, 0.58f, 0.50f);
            Slab(m, new Vector3(0f, -0.05f, 0f), new Vector3(0.8f, 2.2f, 0.24f), 0f, 0f, 2f, carved, 24);
            Slab(m, new Vector3(0f, -0.05f, 0f), new Vector3(1.1f, 0.18f, 0.5f), 0f, 0f, 0f, y => C(0.47f, 0.44f, 0.38f), 1);
            // la punta: una piramide bassa
            var top = new Vector3(0f, 2.15f + 0.3f, 0f);
            int a = m.Vn(new Vector3(-0.4f, 2.15f, -0.12f), Vector3.back, C(0.62f, 0.58f, 0.50f)), b = m.Vn(new Vector3(0.4f, 2.15f, -0.12f), Vector3.back, C(0.62f, 0.58f, 0.50f)), c = m.Vn(top + Vector3.back * 0.03f, Vector3.back, C(0.65f, 0.61f, 0.53f));
            m.Tri(a, c, b);
            int d = m.Vn(new Vector3(-0.4f, 2.15f, 0.12f), Vector3.forward, C(0.62f, 0.58f, 0.50f)), e = m.Vn(new Vector3(0.4f, 2.15f, 0.12f), Vector3.forward, C(0.62f, 0.58f, 0.50f)), f = m.Vn(top + Vector3.forward * 0.03f, Vector3.forward, C(0.65f, 0.61f, 0.53f));
            m.Tri(d, e, f);
            return m.Done("Stele");
        }

        static Mesh Cairn()
        {
            var m = new PM(Surf.Stone);
            Color dark = C(0.24f, 0.22f, 0.20f), light = C(0.50f, 0.46f, 0.41f);
            Blob(m, new Vector3(0f, 0.12f, 0f), new Vector3(0.5f, 0.16f, 0.46f), 71, 0.15f, dark, light, 12, 6, 0.5f, 0f, 8);
            Blob(m, new Vector3(0.03f, 0.34f, 0f), new Vector3(0.37f, 0.13f, 0.34f), 72, 0.15f, dark, light, 11, 6, 0.5f, 0f, 8);
            Blob(m, new Vector3(-0.02f, 0.52f, 0.02f), new Vector3(0.26f, 0.12f, 0.24f), 73, 0.15f, dark, light, 10, 5, 0.5f, 0f, 7);
            Blob(m, new Vector3(0f, 0.68f, 0f), new Vector3(0.17f, 0.1f, 0.16f), 74, 0.15f, dark, light, 9, 5, 0.5f, 0f, 7);
            return m.Done("Cairn");
        }
    }
}

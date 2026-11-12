using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Materiali, solidi e punti liberi condivisi dagli oggetti della fase 3: i leggii con i libri (Biblioteca.cs), lo Scrigno
    /// (Scrigno.cs) e l'altare della Consacrazione (Consacrazione.cs). Tutto procedurale e a runtime, come i potenziamenti (DropPickup.cs):
    /// primitive e solidi di rotazione, con i materiali del kit dove ce n'e' uno adatto (pietra, legno, ferro, cera) e ottone, oro,
    /// cuoio e carta fatti a mano.</summary>
    public static class ShrineKit
    {
        public static Material Stone, Wood, Iron, Brass, Gold, Leather, Paper, Dark, Wax;
        static readonly Dictionary<long, Material> em = new Dictionary<long, Material>();

        public static Material Lit(string name, Color c, float metallic, float smooth, bool doubleSided = false)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = c };
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (doubleSided) m.SetFloat("_Cull", 0f);
            return m;
        }

        /// <summary>Materiale che brilla di suo (non riceve luce): 'k' moltiplica l'emissione. Uno per colore e intensita'.</summary>
        public static Material Emit(Color c, float k)
        {
            long key = ((long)Mathf.RoundToInt(c.r * 255f) << 40) | ((long)Mathf.RoundToInt(c.g * 255f) << 32) | ((long)Mathf.RoundToInt(c.b * 255f) << 24) | (long)Mathf.RoundToInt(k * 10f);
            if (em.TryGetValue(key, out var have) && have != null) return have;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Santuario_Luce", color = Color.black };
            m.SetFloat("_Smoothness", 0.2f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * k);
            em[key] = m;
            return m;
        }

        static Material TexOr(string kit, string name, Color c, float metal, float smooth) => ProcKit.HasMat(kit) ? ProcKit.Mat(kit) : Lit(name, c, metal, smooth);

        /// <summary>Una copia del materiale del kit con il colore piu' scuro (k < 1): la pietra chiara del kit sotto la torcia si brucia.</summary>
        static Material Darker(Material m, float k)
        {
            var c = new Material(m) { name = m.name + "_Scura" };
            if (c.HasProperty("_BaseColor")) c.SetColor("_BaseColor", c.GetColor("_BaseColor") * k);
            return c;
        }

        public static void Mats()
        {
            if (Stone != null) return;
            Stone = ProcKit.HasMat("kit_pietra") ? Darker(ProcKit.Mat("kit_pietra"), 0.55f) : Lit("Santuario_Pietra", new Color(0.30f, 0.29f, 0.28f), 0f, 0.38f);
            Wood = TexOr("kit_legno_scuro", "Santuario_Legno", new Color(0.16f, 0.085f, 0.045f), 0f, 0.35f);
            Iron = TexOr("kit_ferro", "Santuario_Ferro", new Color(0.10f, 0.10f, 0.11f), 0.85f, 0.45f);
            Wax = TexOr("kit_cera", "Santuario_Cera", new Color(0.86f, 0.80f, 0.64f), 0f, 0.4f);
            Brass = Lit("Santuario_Ottone", new Color(0.78f, 0.56f, 0.18f), 0.92f, 0.72f, true);
            Gold = Lit("Santuario_Oro", new Color(0.95f, 0.72f, 0.22f), 0.95f, 0.80f, true);
            Leather = Lit("Santuario_Cuoio", new Color(0.20f, 0.06f, 0.05f), 0f, 0.42f);
            Paper = Lit("Santuario_Carta", new Color(0.80f, 0.74f, 0.58f), 0f, 0.25f);
            Dark = Lit("Santuario_Nero", new Color(0.015f, 0.012f, 0.012f), 0f, 0.8f);
        }

        // ------------------------------------------------------------------ forme
        public static GameObject Prim(Transform parent, PrimitiveType t, string name, Material m, Vector3 pos, Vector3 scale, Vector3 euler = default, bool collider = false)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            if (!collider) Util.Kill(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.transform.localScale = scale;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        public static GameObject LatheObj(Transform parent, string name, Material m, Mesh mesh)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        public static Mesh Lathe(string name, float[] prof, int segs = 28) => DropPickup.Lathe(name, prof, segs);

        public static Light PointLight(Transform parent, string name, Vector3 local, Color c, float intensity, float range, float pulseAmp = 0.2f, float pulseSpeed = 2.6f)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = local;
            var l = g.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            LightPool.Register(l, false, true);
            if (pulseAmp > 0f)
            {
                var p = g.AddComponent<LightPulse>();
                p.l = l; p.baseI = intensity; p.amp = pulseAmp; p.speed = pulseSpeed;
            }
            return l;
        }

        // ------------------------------------------------------------------ raggiungibilita'
        /// <summary>Il pavimento di una stanza a celle di 25 cm: occupata dove un corpo largo 84 cm (l'agente della navmesh ha raggio 40 cm)
        /// non entra (arredi alti tra 40 cm e 1,5 m).
        /// Serve a sapere se un ingombro nuovo, messo in un punto, resterebbe raggiungibile a piedi dalle porte (niente tane chiuse tra un
        /// armadio e un letto, dove la navmesh non arriva e l'oggetto non si potrebbe usare).</summary>
        sealed class RoomGrid
        {
            const float Cell = 0.25f, Body = 0.42f, Ring = 1.4f;
            float gx0, gz0;
            int nx, nz;
            bool[] occ;

            public static RoomGrid Build(Room r)
            {
                Physics.SyncTransforms();
                float T = FloorBuilder.TH;
                var g = new RoomGrid();
                g.gx0 = r.x0 * 0.1f + T + Body + 0.01f;
                g.gz0 = r.z0 * 0.1f + T + Body + 0.01f;
                float ex = r.x1 * 0.1f - T - Body, ez = r.z1 * 0.1f - T - Body;
                g.nx = Mathf.Max(0, Mathf.FloorToInt((ex - g.gx0) / Cell) + 1);
                g.nz = Mathf.Max(0, Mathf.FloorToInt((ez - g.gz0) / Cell) + 1);
                g.occ = new bool[g.nx * g.nz];
                var half = new Vector3(Body, 0.55f, Body);
                for (int j = 0; j < g.nz; j++)
                    for (int i = 0; i < g.nx; i++)
                        g.occ[j * g.nx + i] = Physics.CheckBox(new Vector3(g.gx0 + i * Cell, 0.95f, g.gz0 + j * Cell), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                return g;
            }

            /// <summary>Vero se, con un ingombro di mezza misura 'half' centrato in p, dalle porte della stanza si arriva a camminare
            /// intorno ad esso (una fascia di 1,4 m): almeno sei celle e due quinti di quelle libere. Senza porte da cui partire non si sa: vero.</summary>
            public bool Reachable(Plan plan, Room r, Vector3 p, Vector2 half)
            {
                if (nx == 0 || nz == 0) return true;
                float mx = half.x + Body, mz = half.y + Body;
                bool InFoot(float x, float z) => Mathf.Abs(x - p.x) < mx && Mathf.Abs(z - p.z) < mz;
                var seen = new bool[nx * nz];
                var q = new Queue<int>();
                foreach (var o in plan.openings)
                {
                    if ((o.a != r.id && o.b != r.id) || o.sill > 0.5f) continue;
                    float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                    int ci = Mathf.RoundToInt((ox - gx0) / Cell), cj = Mathf.RoundToInt((oz - gz0) / Cell);
                    int span = Mathf.CeilToInt(1.5f / Cell);
                    for (int j = Mathf.Max(0, cj - span); j <= Mathf.Min(nz - 1, cj + span); j++)
                        for (int i = Mathf.Max(0, ci - span); i <= Mathf.Min(nx - 1, ci + span); i++)
                        {
                            float dx = gx0 + i * Cell - ox, dz = gz0 + j * Cell - oz;
                            int k = j * nx + i;
                            if (dx * dx + dz * dz > 1.5f * 1.5f || seen[k] || occ[k] || InFoot(gx0 + i * Cell, gz0 + j * Cell)) continue;
                            seen[k] = true; q.Enqueue(k);
                        }
                }
                if (q.Count == 0) return true;
                while (q.Count > 0)
                {
                    int k = q.Dequeue(), i = k % nx, j = k / nx;
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = i + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                        int nk = nj * nx + ni;
                        if (seen[nk] || occ[nk] || InFoot(gx0 + ni * Cell, gz0 + nj * Cell)) continue;
                        seen[nk] = true; q.Enqueue(nk);
                    }
                }
                int ring = 0, ringSeen = 0;
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float x = gx0 + i * Cell, z = gz0 + j * Cell;
                        if (InFoot(x, z) || Mathf.Abs(x - p.x) > mx + Ring || Mathf.Abs(z - p.z) > mz + Ring) continue;
                        int k = j * nx + i;
                        if (occ[k]) continue;
                        ring++;
                        if (seen[k]) ringSeen++;
                    }
                return ringSeen >= 6 && ringSeen >= ring * 0.4f;
            }
        }

        // ------------------------------------------------------------------ punti liberi
        /// <summary>Un punto libero per un oggetto che occupa mezzo ingombro 'halfXZ' (m): lontano dai muri ('wallGap') e dalle porte
        /// (2,4 m), senza arredi nel mezzo, con il pavimento al livello giusto, raggiungibile a piedi dalle porte (RoomGrid) e distante
        /// 'sep' dai punti gia' presi.</summary>
        public static bool FindSpot(Plan plan, Room r, Rng rng, Vector2 halfXZ, float wallGap, IList<Vector3> taken, float sep, out Vector3 pos)
        {
            pos = default;
            RoomGrid grid = null;
            float T = FloorBuilder.TH;
            float x0 = r.x0 * 0.1f + T + halfXZ.x + wallGap, x1 = r.x1 * 0.1f - T - halfXZ.x - wallGap;
            float z0 = r.z0 * 0.1f + T + halfXZ.y + wallGap, z1 = r.z1 * 0.1f - T - halfXZ.y - wallGap;
            if (x1 <= x0 || z1 <= z0) return false;
            for (int i = 0; i < 120; i++)
            {
                float x = rng.Range(x0, x1), z = rng.Range(z0, z1);
                var p = new Vector3(x, 0f, z);
                if (!Physics.Raycast(p + Vector3.up * 1.6f, Vector3.down, out RaycastHit fh, 2.2f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(fh.point.y) > 0.12f) continue;
                if (Physics.CheckBox(p + Vector3.up * 1.1f, new Vector3(halfXZ.x, 0.95f, halfXZ.y), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                bool bad = false;
                if (taken != null) foreach (var t in taken) if ((t - p).sqrMagnitude < sep * sep) { bad = true; break; }
                if (bad) continue;
                foreach (var o in plan.openings)
                {
                    if ((o.a != r.id && o.b != r.id) || o.sill > 0.5f) continue;
                    float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                    float dx = ox - x, dz = oz - z;
                    if (dx * dx + dz * dz < 2.5f * 2.5f) { bad = true; break; }
                }
                if (bad) continue;
                if (grid == null) grid = RoomGrid.Build(r);
                if (!grid.Reachable(plan, r, p, halfXZ)) continue;
                pos = new Vector3(x, fh.point.y, z);
                return true;
            }
            return false;
        }

        /// <summary>Stanze buone per un santuario: raggiungibili a piedi, non ascensori, non la partenza, non quelle con una candela ne' quelle
        /// di una zona sigillabile (li' si combatte), abbastanza grandi. Ordinate per quanto la loro distanza a piedi dall'arrivo (0..1 del
        /// massimo del piano) si avvicina a 'target'; 'used' sono le stanze gia' prese.</summary>
        public static List<Room> Candidates(Plan plan, List<RoundRegion> regions, HashSet<int> used, float target, float minAreaDm2, Rng rng)
        {
            var dist = plan.WalkDist(plan.start);
            float max = 1f;
            foreach (var d in dist) if (d < float.MaxValue && d > max) max = d;
            var res = new List<(Room r, float score)>();
            foreach (var r in plan.rooms)
            {
                if (r.IsLift || r.id == plan.start || used.Contains(r.id) || plan.objectives.Contains(r.id)) continue;
                if (dist[r.id] == float.MaxValue || (long)r.W * r.D < minAreaDm2) continue;
                if (plan.grid && r.type != RoomType.Guardia && r.type != RoomType.Normale) continue;     // il ripiego e' una stanza di guardia
                bool inRegion = false;
                if (regions != null) foreach (var g in regions) if (g.Has(r.id)) { inRegion = true; break; }
                if (inRegion) continue;
                res.Add((r, Mathf.Abs(dist[r.id] / max - target) + (float)rng.Value() * 0.12f));
            }
            res.Sort((a, b) => a.score.CompareTo(b.score));
            var list = new List<Room>();
            foreach (var e in res) list.Add(e.r);
            return list;
        }
    }
}

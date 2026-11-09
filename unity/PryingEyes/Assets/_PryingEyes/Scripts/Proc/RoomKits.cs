using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Arreda e illumina ogni stanza: ricette per tipo di stanza (albergo qui, chiesa in RoomKitsChiesa), con un
    /// seme per stanza. Gli arredi sono prefab del kit; se manca un modello al suo posto compare una scatola colorata della
    /// stessa misura, cosi' si vede subito dove andrebbe. Le luci sono tutte registrate nel LightPool.</summary>
    public static partial class RoomKits
    {
        // ---------------------------------------------------------------- contesto
        sealed class Ctx
        {
            public FloorInstance fi;
            public Plan plan;
            public Room r;
            public Transform parent;
            public Transform props;
            public Rng rng;
            public float ix0, ix1, iz0, iz1;
            public readonly List<Rect> occ = new List<Rect>();
            public float W => ix1 - ix0;
            public float D => iz1 - iz0;
            public Vector3 Center => new Vector3((ix0 + ix1) * 0.5f, 0f, (iz0 + iz1) * 0.5f);
        }

        /// <summary>Riferimento locale: 'f' lungo la direzione 'fwd', 'r' alla sua destra. L'origine e' l'angolo interno da cui f e r crescono.</summary>
        struct Frame
        {
            public Vector3 o, f, r;
            public float Lf, Lr;
            public Vector3 P(float ff, float rr, float y = 0f) => o + f * ff + r * rr + Vector3.up * y;
        }

        static Frame MakeFrame(Ctx c, Vector3 fwd)
        {
            var fr = new Frame { f = fwd, r = Vector3.Cross(Vector3.up, fwd) };
            if (fwd == Vector3.forward) fr.o = new Vector3(c.ix0, 0, c.iz0);
            else if (fwd == Vector3.right) fr.o = new Vector3(c.ix0, 0, c.iz1);
            else if (fwd == Vector3.back) fr.o = new Vector3(c.ix1, 0, c.iz1);
            else fr.o = new Vector3(c.ix1, 0, c.iz0);
            bool alongX = Mathf.Abs(fwd.x) > 0.5f;
            fr.Lf = alongX ? c.W : c.D;
            fr.Lr = alongX ? c.D : c.W;
            return fr;
        }

        static Vector3 Inward(Side s) => s == Side.S ? Vector3.forward : s == Side.N ? Vector3.back : s == Side.W ? Vector3.right : Vector3.left;

        /// <summary>Lato col passaggio principale: quello con l'apertura piu' larga verso un'altra stanza (con preferenza per 'prefer').</summary>
        static Side MainSide(Ctx c, params RoomKind[] prefer)
        {
            Side best = Side.S; float bw = -1f;
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var o in c.plan.OpeningsOn(c.r, s))
                {
                    if (o.b < 0 || o.oneSided) continue;
                    var other = c.plan.rooms[o.a == c.r.id ? o.b : o.a];
                    float w = o.width;
                    if (other.IsLift) w -= 100f;
                    foreach (var k in prefer) if (other.kind == k) w += 1000f;
                    if (w > bw) { bw = w; best = s; }
                }
            return best;
        }

        /// <summary>Direzione (asse) lungo la quale la chiesa guarda l'altare: dal nartece al presbiterio.</summary>
        static Vector3 ChurchAxis(Ctx c)
        {
            var pres = c.plan.Find(RoomKind.Presbiterio);
            var nar = c.plan.Find(RoomKind.Nartece);
            if (pres == null || nar == null) return Vector3.right;
            Vector3 d = new Vector3(pres.CenterX - nar.CenterX, 0f, pres.CenterZ - nar.CenterZ);
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.z)) return d.x >= 0f ? Vector3.right : Vector3.left;
            return d.z >= 0f ? Vector3.forward : Vector3.back;
        }

        static Vector3 LongAxis(Ctx c) => c.W >= c.D ? Vector3.right : Vector3.forward;

        // ---------------------------------------------------------------- ingresso
        public static void Dress(FloorInstance fi, Room r, Transform parent)
        {
            var c = new Ctx
            {
                fi = fi, plan = fi.plan, r = r, parent = parent, rng = new Rng(r.seed),
                ix0 = r.x0 * 0.1f + FloorBuilder.TH, ix1 = r.x1 * 0.1f - FloorBuilder.TH,
                iz0 = r.z0 * 0.1f + FloorBuilder.TH, iz1 = r.z1 * 0.1f - FloorBuilder.TH,
            };
            var pg = new GameObject("Arredi");
            pg.transform.SetParent(parent, false);
            c.props = pg.transform;
            fi.propRoots.Add(pg.transform);

            ReserveOpenings(c);
            if (r.kind != RoomKind.Corridoio && r.kind != RoomKind.Navata) ReserveSpokes(c);
            switch (r.kind)
            {
                case RoomKind.Ascensore: Lift(c); break;
                case RoomKind.Atrio: Atrio(c); break;
                case RoomKind.Corridoio: Corridoio(c); break;
                case RoomKind.Camera: Camera(c, false); break;
                case RoomKind.Suite: Camera(c, true); break;
                case RoomKind.Servizio: Servizio(c); break;
                case RoomKind.Ristorante: Ristorante(c); break;
                case RoomKind.Cucina: Cucina(c); break;
                case RoomKind.SalaBallo: SalaBallo(c); break;
                case RoomKind.Nartece: Nartece(c); break;
                case RoomKind.Navata: Navata(c); break;
                case RoomKind.Navatella: Navatella(c); break;
                case RoomKind.Cappella: Cappella(c); break;
                case RoomKind.Presbiterio: Presbiterio(c); break;
                case RoomKind.Sacrestia: Sacrestia(c); break;
                case RoomKind.Cripta: Cripta(c); break;
                case RoomKind.Campanile: Campanile(c); break;
            }
            if (!r.IsLift && c.plan.objectives.Contains(r.id)) Objective(c);
            WindowLights(c);
            Fill(c);
            Spawns(c);
        }

        /// <summary>Il candelabro da accendere: in un punto libero della stanza (a caso), spento, con l'alone freddo.</summary>
        static void Objective(Ctx c)
        {
            if (c.W > 2.4f && c.D > 2.4f)
                for (int i = 0; i < 80; i++)
                {
                    var p = new Vector3(c.rng.Range(c.ix0 + 1.0f, c.ix1 - 1.0f), 0f, c.rng.Range(c.iz0 + 1.0f, c.iz1 - 1.0f));
                    var cd = Candle(c, "chi_candelabro", p, Vector3.forward, false, true, true);
                    if (cd != null) return;
                }
            Candle(c, "chi_candelabro", c.Center, Vector3.forward, false, true, false);
        }

        // ---------------------------------------------------------------- occupazione
        static void ReserveOpenings(Ctx c)
        {
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var o in c.plan.OpeningsOn(c.r, s))
                {
                    if (o.b < 0 && o.sill > 0.5f) continue;     // finestre: nessun vincolo
                    float w = o.width * 0.1f + 0.7f, depth = o.style == OpenStyle.Arcata ? 1.1f : 1.7f;
                    Vector3 inw = Inward(s);
                    Vector3 wallPt = Mathf.Abs(inw.z) > 0.5f
                        ? new Vector3(o.center * 0.1f, 0f, inw.z > 0 ? c.iz0 : c.iz1)
                        : new Vector3(inw.x > 0 ? c.ix0 : c.ix1, 0f, o.center * 0.1f);
                    Vector3 mid = wallPt + inw * (depth * 0.5f);
                    bool ax = Mathf.Abs(inw.z) > 0.5f;
                    c.occ.Add(ax ? new Rect(mid.x - w * 0.5f, mid.z - depth * 0.5f, w, depth) : new Rect(mid.x - depth * 0.5f, mid.z - w * 0.5f, depth, w));
                }
        }

        /// <summary>Stanze con piu' di un passaggio (atrio, navate, sacrestia...): tiene libero un tracciato a L da davanti a ogni porta fino al
        /// centro, cosi' gli arredi non possono chiudere una tasca attorno a una porta o all'ascensore.</summary>
        static void ReserveSpokes(Ctx c)
        {
            var fronts = new List<Vector3>();
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var o in c.plan.OpeningsOn(c.r, s))
                {
                    if (o.b < 0 || o.oneSided) continue;
                    Vector3 inw = Inward(s);
                    Vector3 wallPt = Mathf.Abs(inw.z) > 0.5f
                        ? new Vector3(o.center * 0.1f, 0f, inw.z > 0 ? c.iz0 : c.iz1)
                        : new Vector3(inw.x > 0 ? c.ix0 : c.ix1, 0f, o.center * 0.1f);
                    fronts.Add(wallPt + inw * 1.0f);
                }
            if (fronts.Count < 2) return;
            Vector3 ctr = c.Center;
            const float w = 1.6f;
            c.occ.Add(new Rect(ctr.x - w * 0.5f, ctr.z - w * 0.5f, w, w));
            foreach (var f in fronts)
            {
                // prima lungo x fino al centro, poi lungo z
                c.occ.Add(new Rect(Mathf.Min(f.x, ctr.x) - w * 0.5f, f.z - w * 0.5f, Mathf.Abs(f.x - ctr.x) + w, w));
                c.occ.Add(new Rect(ctr.x - w * 0.5f, Mathf.Min(f.z, ctr.z) - w * 0.5f, w, Mathf.Abs(f.z - ctr.z) + w));
            }
        }

        static bool Free(Ctx c, Vector3 pos, float w, float d, float yaw, float margin = 0.04f)
        {
            float s = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)), co = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad));
            float ex = (w * co + d * s) * 0.5f + margin, ez = (w * s + d * co) * 0.5f + margin;
            if (pos.x - ex < c.ix0 - 0.02f || pos.x + ex > c.ix1 + 0.02f || pos.z - ez < c.iz0 - 0.02f || pos.z + ez > c.iz1 + 0.02f) return false;
            var rc = new Rect(pos.x - ex, pos.z - ez, ex * 2f, ez * 2f);
            foreach (var o in c.occ) if (o.Overlaps(rc)) return false;
            return true;
        }

        static void Occupy(Ctx c, Vector3 pos, float w, float d, float yaw)
        {
            float s = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)), co = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad));
            float ex = (w * co + d * s) * 0.5f, ez = (w * s + d * co) * 0.5f;
            c.occ.Add(new Rect(pos.x - ex, pos.z - ez, ex * 2f, ez * 2f));
        }

        static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // ---------------------------------------------------------------- arredi
        /// <summary>Mette un arredo con il davanti verso 'face'. Se manca il modello, una scatola della stessa misura.
        /// 'sx' scala la larghezza (panche che devono stare in uno spazio piu' stretto). Falso se non c'e' posto.</summary>
        static GameObject Prop(Ctx c, string name, Vector3 pos, Vector3 face, float sx = 1f, bool check = true, float yOff = 0f, float yawJitter = 0f)
        {
            var def = PropDefs.Get(name);
            float yaw = YawOf(face) + yawJitter;
            if (check && !Free(c, pos, def.size.x * sx, def.size.z, yaw)) return null;
            var go = new GameObject(name);
            go.transform.SetParent(c.props, false);
            go.transform.position = pos + Vector3.up * yOff;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(sx, 1f, 1f);
            var pf = ProcKit.Prefab(name);
            if (pf != null)
            {
                var inst = UnityEngine.Object.Instantiate(pf, go.transform);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
            }
            else
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Util.Kill(cube.GetComponent<Collider>());
                cube.name = "Segnaposto";
                cube.transform.SetParent(go.transform, false);
                cube.transform.localPosition = def.center + (def.center.y < 0.001f ? Vector3.zero : Vector3.zero);
                cube.transform.localScale = def.size;
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.color = name.StartsWith("chi_") ? new Color(0.35f, 0.33f, 0.31f) : new Color(0.32f, 0.2f, 0.12f);
                cube.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            if (def.solid)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = def.center;
                bc.size = def.size;
            }
            if (check) Occupy(c, pos, def.size.x * sx, def.size.z, yaw);
            return go;
        }

        /// <summary>Appoggia un arredo contro un muro: 'side' e' il muro, 't' la distanza lungo il muro dal suo inizio.</summary>
        static GameObject OnWall(Ctx c, string name, Side side, float t, float gap = 0.04f, float sx = 1f, bool check = true, float yOff = 0f)
        {
            var def = PropDefs.Get(name);
            Vector3 inw = Inward(side);
            float depth = def.size.z * 0.5f + gap;
            Vector3 pos;
            switch (side)
            {
                case Side.S: pos = new Vector3(c.ix0 + t, 0f, c.iz0 + depth); break;
                case Side.N: pos = new Vector3(c.ix0 + t, 0f, c.iz1 - depth); break;
                case Side.W: pos = new Vector3(c.ix0 + depth, 0f, c.iz0 + t); break;
                default: pos = new Vector3(c.ix1 - depth, 0f, c.iz0 + t); break;
            }
            return Prop(c, name, pos, inw, sx, check, yOff);
        }

        static float WallLen(Ctx c, Side s) => Plan.AlongX(s) ? c.W : c.D;

        // ---------------------------------------------------------------- luci
        static PoolLight AddLight(Ctx c, Vector3 pos, Color col, float intensity, float range, bool shadow, bool wanted = true, Transform parent = null, LightType type = LightType.Point)
        {
            var g = new GameObject("Luce");
            g.transform.SetParent(parent != null ? parent : c.parent, false);
            g.transform.position = pos;
            var l = g.AddComponent<Light>();
            l.type = type;
            l.color = col;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            c.fi.lights.Add(l);
            return LightPool.Register(l, shadow, wanted);
        }

        static Material EmissiveMat(string name) => ProcKit.Mat(name);

        static GameObject Bulb(Ctx c, Vector3 pos, float size, string mat, Transform parent = null)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Util.Kill(s.GetComponent<Collider>());
            s.name = "Lampadina";
            s.transform.SetParent(parent != null ? parent : c.props, false);
            s.transform.position = pos;
            s.transform.localScale = Vector3.one * size;
            var mr = s.GetComponent<MeshRenderer>();
            mr.sharedMaterial = ProcKit.Mat(mat);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return s;
        }

        /// <summary>Applique a muro: placca d'ottone, lampadina e luce calda; alcune sono rotte (lampadina spenta, niente luce).</summary>
        static void Sconce(Ctx c, Vector3 pt, Vector3 inward, bool on, float intensity = 3.4f, float range = 8.5f)
        {
            var g = new GameObject(on ? "Applique" : "AppliqueRotta");
            g.transform.SetParent(c.props, false);
            g.transform.position = pt;
            g.transform.rotation = Quaternion.LookRotation(inward);
            var brass = ProcKit.Mat("ottone");
            void Part(PrimitiveType t, Vector3 sc, Vector3 lp, Material m)
            {
                var p = GameObject.CreatePrimitive(t);
                Util.Kill(p.GetComponent<Collider>());
                p.transform.SetParent(g.transform, false);
                p.transform.localPosition = lp; p.transform.localScale = sc;
                p.GetComponent<MeshRenderer>().sharedMaterial = m;
                p.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            Part(PrimitiveType.Cube, new Vector3(0.16f, 0.32f, 0.05f), new Vector3(0f, 0f, 0.02f), brass);
            Part(PrimitiveType.Cube, new Vector3(0.03f, 0.03f, 0.16f), new Vector3(0f, 0.05f, 0.1f), brass);
            Part(PrimitiveType.Sphere, Vector3.one * 0.11f, new Vector3(0f, 0.1f, 0.19f), ProcKit.Mat(on ? "lampadina" : "lampadina_spenta"));
            if (!on) return;
            var pl = AddLight(c, pt + inward * 0.55f + Vector3.up * 0.12f, new Color(1f, 0.74f, 0.48f), intensity, range, true, true, g.transform);
            if (c.rng.Chance(0.4)) g.AddComponent<LampFlicker>().Init(pl);
        }

        /// <summary>Lampadario: la corona di ferro con le candele (stesso modello della chiesa), appesa al soffitto. 'pos' e' il centro
        /// della corona; i parametri di luce restano per compatibilita' con le ricette d'albergo.</summary>
        static void Chandelier(Ctx c, Vector3 pos, float radius, bool on, float intensity = 14f, float range = 14f, float drop = 0f)
        {
            Crown(c, new Vector3(pos.x, c.r.wallH - 0.05f, pos.z), on);
        }

        /// <summary>Luce del chiaro di luna che passa dalle finestre: faro freddo davanti al vetro, in giu' e verso l'interno.</summary>
        static void WindowLights(Ctx c)
        {
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var o in c.plan.OpeningsOn(c.r, s))
                {
                    if (o.b >= 0 || o.style == OpenStyle.Feritoia) continue;
                    Vector3 inw = Inward(s);
                    bool ax = Mathf.Abs(inw.z) > 0.5f;
                    float wallCoord = ax ? (inw.z > 0 ? c.iz0 : c.iz1) : (inw.x > 0 ? c.ix0 : c.ix1);
                    float y = Mathf.Lerp(o.sill, o.top, 0.7f);
                    Vector3 pt = ax ? new Vector3(o.center * 0.1f, y, wallCoord) : new Vector3(wallCoord, y, o.center * 0.1f);
                    bool church = o.style == OpenStyle.Lancetta;
                    Color col = church ? GlassColor(o.variant) : new Color(0.62f, 0.74f, 1f);
                    float inten = church ? (o.oneSided ? 12f : 17f) : 6f;
                    var pl = AddLight(c, pt + inw * 0.25f, col, inten, church ? 13f : 8.5f, church, true, c.parent, LightType.Spot);
                    var l = pl.l;
                    l.spotAngle = church ? 75f : 90f;
                    l.innerSpotAngle = 20f;
                    Vector3 dir = (inw + Vector3.down * (o.oneSided ? 1.5f : 0.7f)).normalized;
                    l.transform.rotation = Quaternion.LookRotation(dir);
                    if (church && ProcKit.HasMat("cookie_vetrata" + (o.variant & 3))) { }
                }
        }

        public static Color GlassColor(int variant)
        {
            switch (variant & 3)
            {
                case 0: return new Color(1f, 0.38f, 0.30f);
                case 1: return new Color(0.35f, 0.55f, 1f);
                case 2: return new Color(0.45f, 0.95f, 0.55f);
                default: return new Color(1f, 0.78f, 0.35f);
            }
        }

        /// <summary>Luce di riempimento senza ombre: al posto dei rimbalzi della luce cotta, che qui non ci sono. Tenue e di tinta
        /// diversa per le due ali (fredda in chiesa, polverosa in albergo), cosi' ogni stanza si legge anche con le lampade spente.</summary>
        static void Fill(Ctx c)
        {
            var r = c.r;
            if (r.IsLift) return;
            float size = Mathf.Max(c.W, c.D);
            Color col = r.church ? new Color(0.55f, 0.62f, 0.9f) : new Color(0.95f, 0.78f, 0.6f);
            // sopra il soffitto nelle stanze basse (niente macchia calda sull'intonaco: la faccia del soffitto guarda in basso e non la prende),
            // a mezza altezza in quelle alte
            float y = r.wallH < 5.2f ? r.wallH + 1.2f : 5.0f;
            float inten = (r.church ? 3.0f : 2.6f) * (y / 3.6f) * (y / 3.6f);
            float range = Mathf.Min(size * 0.55f + y + 1.5f, 22f);
            int n = size > 14f ? Mathf.RoundToInt(size / 12f) : 1;
            Vector3 a = LongAxis(c);
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0f : ((i + 0.5f) / n - 0.5f) * (a == Vector3.right ? c.W : c.D);
                AddLight(c, c.Center + a * t + Vector3.up * y, col, inten, range, false);
            }
        }

        // ---------------------------------------------------------------- nascite dei nemici
        static void Spawns(Ctx c)
        {
            if (c.r.IsLift) return;
            var r = c.r;
            Vector3 ctr = c.Center;
            bool corridor = r.kind == RoomKind.Corridoio;
            if (corridor)
            {
                Vector3 a = LongAxis(c);
                float len = a == Vector3.right ? c.W : c.D;
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 9f));
                for (int i = 0; i < n; i++)
                {
                    float t = (i + 0.5f) / n - 0.5f;
                    c.fi.groundSpawns.Add(ctr + a * (t * len));
                }
            }
            else
            {
                int n = r.W * r.D > 3000 ? 3 : r.W * r.D > 1400 ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = ctr + new Vector3(c.rng.Range(-0.3f, 0.3f) * c.W * (n > 1 ? 1f : 0f), 0f, c.rng.Range(-0.3f, 0.3f) * c.D * (n > 1 ? 1f : 0f));
                    c.fi.groundSpawns.Add(p);
                }
            }
            c.fi.airSpawns.Add(new Vector3(ctr.x, Mathf.Min(2.4f, r.wallH - 0.6f), ctr.z));
        }

        // ---------------------------------------------------------------- ascensore
        static void Lift(Ctx c)
        {
            var o = c.plan.openings.Find(q => q.style == OpenStyle.Ascensore && (q.a == c.r.id || q.b == c.r.id));
            if (o == null) return;
            var host = c.plan.rooms[o.a == c.r.id ? o.b : o.a];
            bool isExit = c.plan.exit == c.r.id;
            var lift = ProcLift.Build(c.fi, c.r, host, o, c.parent, isExit);
            if (isExit) c.fi.exit = lift; else c.fi.arrival = lift;
        }
    }
}

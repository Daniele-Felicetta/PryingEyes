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
            var pres = c.plan.FindIn(RoomKind.Presbiterio, c.r.unit);
            var nar = c.plan.FindIn(RoomKind.Nartece, c.r.unit);
            if (pres == null || nar == null) return Vector3.right;
            Vector3 d = new Vector3(pres.CenterX - nar.CenterX, 0f, pres.CenterZ - nar.CenterZ);
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.z)) return d.x >= 0f ? Vector3.right : Vector3.left;
            return d.z >= 0f ? Vector3.forward : Vector3.back;
        }

        static Vector3 LongAxis(Ctx c) => c.W >= c.D ? Vector3.right : Vector3.forward;

        // ---------------------------------------------------------------- ingresso
        public static Light Dress(FloorInstance fi, Room r, Transform parent)
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
            // teatro e sanatorio: il candelabro dell'obiettivo sceglie il posto PRIMA degli arredi. Con gli arredi gia' messi, nelle stanze piccole
            // (depositi, camerini, palchi) non restava un posto libero e il ripiego lo metteva al centro, l'incrocio dei tracciati riservati,
            // chiudendo la stanza. I corridoi riservano la loro corsia nella ricetta, quindi restano nell'ordine di prima.
            bool objectiveFirst = c.plan.theme != Theme.Albergo && !r.church && !r.IsLift && r.kind != RoomKind.Corridoio && c.plan.objectives.Contains(r.id);
            if (objectiveFirst) Objective(c, true);
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
                // teatro
                case RoomKind.Platea: Platea(c); break;
                case RoomKind.Palco: Palco(c); break;
                case RoomKind.Foyer: Foyer(c); break;
                case RoomKind.Palcoscenico: Palcoscenico(c); break;
                case RoomKind.Camerino: Camerino(c); break;
                case RoomKind.Deposito: Deposito(c); break;
                case RoomKind.Ridotto: Ridotto(c); break;
                // sanatorio
                case RoomKind.Accettazione: Accettazione(c); break;
                case RoomKind.Degenza: Degenza(c, false); break;
                case RoomKind.Corsia: Degenza(c, true); break;
                case RoomKind.Cella: Cella(c); break;
                case RoomKind.Refettorio: Refettorio(c); break;
                case RoomKind.SalaOperatoria: SalaOperatoria(c); break;
                case RoomKind.Lavanderia: Lavanderia(c); break;
                case RoomKind.Archivio: Archivio(c); break;
                case RoomKind.Obitorio: Obitorio(c); break;
                case RoomKind.SalaMedici: SalaMedici(c); break;
            }
            if (!objectiveFirst && !r.IsLift && c.plan.objectives.Contains(r.id)) Objective(c);
            if (r.church) PortalCandles(c);
            var window = WindowLights(c);
            Fill(c, window != null);
            Spawns(c);
            return window;
        }

        /// <summary>Il candelabro da accendere: in un punto libero della stanza (a caso), spento, con l'alone freddo. Con 'nearWall' (i temi
        /// nuovi, dove lo si piazza prima degli arredi) sta a 0,6-1,5 m da un muro e si riserva un metro libero tutt'attorno: in mezzo a
        /// un blocco di sedie o di scaffali gli arredi messi dopo lo chiudevano in una tasca.</summary>
        static void Objective(Ctx c, bool nearWall = false)
        {
            if (c.W > 2.4f && c.D > 2.4f)
                for (int i = 0; i < 80; i++)
                {
                    Vector3 p;
                    if (nearWall)
                    {
                        float d = c.rng.Range(0.6f, 1.5f);
                        switch (c.rng.Range(0, 4))
                        {
                            case 0: p = new Vector3(c.rng.Range(c.ix0 + 0.8f, c.ix1 - 0.8f), 0f, c.iz0 + d); break;
                            case 1: p = new Vector3(c.rng.Range(c.ix0 + 0.8f, c.ix1 - 0.8f), 0f, c.iz1 - d); break;
                            case 2: p = new Vector3(c.ix0 + d, 0f, c.rng.Range(c.iz0 + 0.8f, c.iz1 - 0.8f)); break;
                            default: p = new Vector3(c.ix1 - d, 0f, c.rng.Range(c.iz0 + 0.8f, c.iz1 - 0.8f)); break;
                        }
                    }
                    else p = new Vector3(c.rng.Range(c.ix0 + 1.0f, c.ix1 - 1.0f), 0f, c.rng.Range(c.iz0 + 1.0f, c.iz1 - 1.0f));
                    var cd = Candle(c, "chi_candelabro", p, Vector3.forward, false, true, true);
                    if (cd == null) continue;
                    if (nearWall) c.occ.Add(new Rect(p.x - 1.0f, p.z - 1.0f, 2.0f, 2.0f));
                    return;
                }
            // nessun posto libero: in un angolo, il piu' lontano dalle porte. Il centro e' l'incrocio dei tracciati riservati: un candelabro
            // li' chiude le stanze piccole (un deposito di 6,8 x 4,4 m a due porte lo spezzava in due e l'ala dietro restava isolata)
            Vector3 corner = c.Center; float far = -1f;
            var fronts = new List<Vector3>();
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var o in c.plan.OpeningsOn(c.r, s))
                {
                    if (o.b < 0 || o.oneSided) continue;
                    fronts.Add(OpeningPoint(o) + Inward(s) * 1.0f);
                }
            foreach (float cx in new[] { c.ix0 + 0.55f, c.ix1 - 0.55f })
                foreach (float cz in new[] { c.iz0 + 0.55f, c.iz1 - 0.55f })
                {
                    var p = new Vector3(cx, 0f, cz);
                    float dmin = float.MaxValue;
                    foreach (var f in fronts) dmin = Mathf.Min(dmin, (f - p).sqrMagnitude);
                    if (dmin > far) { far = dmin; corner = p; }
                }
            Candle(c, "chi_candelabro", corner, Vector3.forward, false, true, false);
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

        /// <summary>Mezza larghezza della corsia libera che i corridoi dei temi nuovi tengono sull'asse (1,3 m in tutto: la navmesh chiude i
        /// varchi sotto ~1,2 m). Gli arredi stanno nella fascia tra il muro e la corsia, larga (larghezza del corridoio / 2 - questo).</summary>
        const float ThemeLaneHalf = 0.65f;

        /// <summary>Un arredo addossato a un muro lungo del corridoio con la fronte verso l'asse, nella fascia tra muro e corsia. La posizione
        /// si ricava dall'ingombro vero (compresa l'inclinazione 'jitter' in gradi): messo a 0,5 m dal muro e ruotato a caso, come prima,
        /// Free lo scartava quasi sempre perche' sconfinava nella corsia riservata e i corridoi restavano vuoti. Nullo se non ci sta.</summary>
        static GameObject HugWall(Ctx c, Frame fr, string name, float f, bool far, float jitter = 0f)
        {
            var def = PropDefs.Get(name);
            float j = jitter > 0f ? c.rng.Range(-jitter, jitter) : 0f;
            float half = (def.size.x * Mathf.Abs(Mathf.Sin(j * Mathf.Deg2Rad)) + def.size.z * Mathf.Cos(j * Mathf.Deg2Rad)) * 0.5f;
            float d = half + 0.045f;
            return Prop(c, name, fr.P(f, far ? fr.Lr - d : d), far ? -fr.r : fr.r, 1f, true, 0f, j);
        }

        /// <summary>HugWall con fino a 'tries' tentativi (lato e punto a caso lungo il corridoio): ogni porta riserva davanti a se' mezza parete,
        /// quindi con un tentativo solo ne riusciva uno ogni dieci.</summary>
        static GameObject HugWallAny(Ctx c, Frame fr, string name, float jitter = 0f, int tries = 8)
        {
            for (int i = 0; i < tries; i++)
            {
                var go = HugWall(c, fr, name, fr.Lf * c.rng.Range(0.1f, 0.9f), c.rng.Chance(0.5), jitter);
                if (go != null) return go;
            }
            return null;
        }

        static float WallLen(Ctx c, Side s) => Plan.AlongX(s) ? c.W : c.D;

        // ---------------------------------------------------------------- luci
        static PoolLight AddLight(Ctx c, Vector3 pos, Color col, float intensity, float range, bool shadow, bool wanted = true, Transform parent = null, LightType type = LightType.Point, bool dim = true)
        {
            var g = new GameObject("Luce");
            g.transform.SetParent(parent != null ? parent : c.parent, false);
            g.transform.position = pos;
            var l = g.AddComponent<Light>();
            l.type = type;
            l.color = col;
            l.intensity = dim ? intensity * LightPool.Dim : intensity;
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
        static void Sconce(Ctx c, Vector3 pt, Vector3 inward, bool on, float intensity = 3.4f, float range = 8.5f, bool candle = false)
        {
            var g = new GameObject(on ? "Applique" : "AppliqueRotta");
            g.transform.SetParent(c.props, false);
            g.transform.position = pt;
            g.transform.rotation = Quaternion.LookRotation(inward);
            var metal = ProcKit.Mat(candle ? "ferro" : "ottone");
            void Part(PrimitiveType t, Vector3 sc, Vector3 lp, Material m)
            {
                var p = GameObject.CreatePrimitive(t);
                var collider = p.GetComponent<Collider>();
                collider.enabled = false;
                Util.Kill(collider);
                p.transform.SetParent(g.transform, false);
                p.transform.localPosition = lp; p.transform.localScale = sc;
                p.GetComponent<MeshRenderer>().sharedMaterial = m;
                p.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            if (candle)
            {
                Part(PrimitiveType.Sphere, new Vector3(0.094f, 0.24f, 0.029f), new Vector3(0f, -0.022f, 0.02f), metal);
                var bracket = new MeshBuilder();
                foreach (int side in new[] { -1, 1 })
                {
                    float x = side * 0.124f;
                    Vector3 a = new Vector3(side * 0.018f, -0.105f, 0.032f);
                    Vector3 b = new Vector3(side * 0.087f, -0.085f, 0.112f);
                    Vector3 d = new Vector3(x, 0.023f, 0.223f);
                    bracket.Beam(metal, a, b, 0.021f, 0.024f, Vector3.up);
                    bracket.Beam(metal, b, d, 0.018f, 0.021f, Vector3.up);
                    float h = side < 0 ? 0.109f : 0.143f;
                    Part(PrimitiveType.Cylinder, new Vector3(0.092f, 0.008f, 0.092f), d, metal);
                    Part(PrimitiveType.Cylinder, new Vector3(0.038f, h * 0.5f, 0.038f), d + Vector3.up * (h * 0.5f), ProcKit.Mat("cera"));
                    Part(PrimitiveType.Cylinder, new Vector3(0.006f, 0.006f, 0.006f), d + Vector3.up * (h + 0.004f), metal);
                    Part(PrimitiveType.Sphere, new Vector3(0.016f, 0.044f, 0.016f), d + Vector3.up * (h + 0.023f), ProcKit.Mat("fiamma"));
                }
                g.AddComponent<MeshFilter>().sharedMesh = bracket.ToMesh("Ferro dell'applique", out var materials);
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            else
            {
                Part(PrimitiveType.Cube, new Vector3(0.16f, 0.32f, 0.05f), new Vector3(0f, 0f, 0.02f), metal);
                Part(PrimitiveType.Cube, new Vector3(0.03f, 0.03f, 0.16f), new Vector3(0f, 0.05f, 0.1f), metal);
                Part(PrimitiveType.Sphere, Vector3.one * 0.11f, new Vector3(0f, 0.1f, 0.19f), ProcKit.Mat(on ? "lampadina" : "lampadina_spenta"));
            }
            if (!on) return;
            var pl = AddLight(c, pt + inward * (candle ? 0.33f : 0.55f) + Vector3.up * (candle ? 0.18f : 0.12f),
                              candle ? CandleCol : new Color(1f, 0.74f, 0.48f), intensity, range, true, true, g.transform);
            if (candle)
            {
                pl.l.shadowStrength = 0.68f;
                var pulse = g.AddComponent<LightPulse>();
                pulse.l = pl.l; pulse.baseI = pl.l.intensity; pulse.amp = 0.11f; pulse.speed = 7f;
            }
            else if (c.rng.Chance(0.4)) g.AddComponent<LampFlicker>().Init(pl);
        }

        /// <summary>Lampadario: la corona di ferro con le candele (stesso modello della chiesa), appesa al soffitto. 'pos' e' il centro
        /// della corona; i parametri di luce restano per compatibilita' con le ricette d'albergo.</summary>
        static void Chandelier(Ctx c, Vector3 pos, float radius, bool on, float intensity = 14f, float range = 14f, float drop = 0f)
        {
            Crown(c, new Vector3(pos.x, c.r.wallH - 0.05f, pos.z), on);
        }

        static Vector3 WindowOrigin(Opening opening, Vector3 inward)
        {
            float y = Mathf.Lerp(opening.sill, opening.top, 0.72f);
            var onWall = opening.alongX ? new Vector3(opening.center * 0.1f, y, opening.line * 0.1f)
                : new Vector3(opening.line * 0.1f, y, opening.center * 0.1f);
            return onWall + inward * (FloorBuilder.TH + 0.25f);
        }

        static Opening EntranceKey(Ctx c, out Vector3 target)
        {
            target = default;
            if (c.r.kind != RoomKind.Nartece) return null;
            var nave = c.plan.FindIn(RoomKind.Navata, c.r.unit);
            if (nave == null) return null;
            var portal = c.plan.openings.Find(o => o.a == c.r.id && o.b == nave.id || o.a == nave.id && o.b == c.r.id);
            if (portal == null) return null;
            Vector3 forward = ChurchAxis(c);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            var center = portal.alongX ? new Vector3(portal.center * 0.1f, 1.8f, portal.line * 0.1f)
                : new Vector3(portal.line * 0.1f, 1.8f, portal.center * 0.1f);
            target = center - forward * 0.55f - right * (portal.width * 0.05f + 0.25f);
            Opening best = null;
            float bestScore = 0f;
            foreach (Side side in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var window in c.plan.OpeningsOn(c.r, side))
                {
                    if (window.b >= 0 || window.style != OpenStyle.Lancetta) continue;
                    Vector3 toward = target - WindowOrigin(window, Inward(side));
                    float incidence = Vector3.Dot(toward.normalized, Inward(side));
                    if (incidence < 0.38f) continue;
                    float score = incidence / (1f + toward.sqrMagnitude);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = window;
                }
            return best;
        }

        /// <summary>Chiarore dall'esterno, con una luce radente dal vetro del nartece verso i conci del portale.</summary>
        static Light WindowLights(Ctx c)
        {
            var keyOpening = EntranceKey(c, out var keyTarget);
            Light first = null, second = null, keyLight = null;
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var o in c.plan.OpeningsOn(c.r, s))
                {
                    if (o.b >= 0 || o.style == OpenStyle.Feritoia) continue;
                    Vector3 inw = Inward(s);
                    var origin = WindowOrigin(o, inw);
                    bool church = o.style == OpenStyle.Lancetta;
                    bool key = o == keyOpening;
                    Color col = church ? Color.Lerp(new Color(0.73f, 0.82f, 1f), GlassColor(o.variant), 0.22f)
                        : c.plan.theme == Theme.Sanatorio ? new Color(0.62f, 0.84f, 0.80f) : new Color(0.62f, 0.74f, 1f);
                    float inten = church ? (o.oneSided ? 21f : 31f) : 6f;
                    float range = church ? 17f : 8.5f;
                    var direction = inw + Vector3.down * (o.oneSided ? 1.5f : 0.7f);
                    if (key)
                    {
                        direction = keyTarget - origin;
                        col = new Color(0.80f, 0.87f, 1f);
                        inten = Mathf.Clamp(30f + direction.sqrMagnitude * 1.65f, 42f, 135f);
                        range = Mathf.Clamp(direction.magnitude + 8f, 13f, 22f);
                    }
                    else if (c.r.kind == RoomKind.Nartece && church)
                    {
                        col = new Color(0.74f, 0.84f, 1f);
                        inten = 18f;
                    }
                    var pl = AddLight(c, origin, col, inten, range, church, true, c.parent, LightType.Spot);
                    var l = pl.l;
                    l.spotAngle = key ? 75f : church ? 86f : 90f;
                    l.innerSpotAngle = key ? 41f : church ? 47f : 20f;
                    l.transform.rotation = Quaternion.LookRotation(direction);
                    if (!church) continue;
                    l.name = key ? "Chiarore del nartece" : "Luce della vetrata";
                    l.cookie = StainedWindowCookie();
                    l.shadowStrength = 0.82f;
                    if (key) keyLight = l;
                    if (first == null) first = l;
                    else if (c.r.kind == RoomKind.Navata && second == null && (l.transform.position - first.transform.position).sqrMagnitude > 40f)
                        second = l;
                }
            first = keyLight != null ? keyLight : first;
            if (first != null) c.fi.windowBounceSources.Add(first);
            if (second != null) c.fi.windowBounceSources.Add(second);
            return first;
        }

        static Texture2D stainedWindowCookie;

        static Texture2D StainedWindowCookie()
        {
            if (stainedWindowCookie != null) return stainedWindowCookie;
            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Proiezione della vetrata",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float arch = 0.86f - Mathf.Pow(Mathf.Abs(u), 1.5f) * 0.85f;
                    float aperture = (1f - Stealth.Smooth(0.66f, 0.96f, Mathf.Abs(u)))
                                   * Stealth.Smooth(-0.97f, -0.65f, v) * (1f - Stealth.Smooth(arch - 0.24f, arch + 0.11f, v));
                    float lead = Mathf.Min(Mathf.Abs(u), Mathf.Min(Mathf.Abs(v + 0.31f), Mathf.Abs(v - 0.22f)));
                    float transmission = aperture * Mathf.Lerp(0.76f, 1f, Stealth.Smooth(0.009f, 0.045f, lead));
                    byte value = (byte)Mathf.RoundToInt(transmission * 255f);
                    pixels[y * size + x] = new Color32(value, value, value, value);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return stainedWindowCookie = texture;
        }

        internal static void PlaceWindowBounce(FloorInstance floor)
        {
            if (floor.windowBounceSources.Count == 0) return;
            Physics.SyncTransforms();
            foreach (var source in floor.windowBounceSources)
            {
                var direction = source.transform.forward;
                var parent = source.transform.parent;
                if (!Physics.Raycast(source.transform.position, direction, out var hit, source.range, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)
                    || !hit.transform.IsChildOf(parent) || hit.distance < 0.65f) continue;

                var go = new GameObject("Rimbalzo della vetrata");
                go.transform.SetParent(parent, false);
                go.transform.position = hit.point + hit.normal * 0.23f;
                Vector3 reflection = Vector3.Reflect(direction, hit.normal);
                go.transform.rotation = Quaternion.LookRotation(Vector3.Slerp(hit.normal, reflection, 0.32f));
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 147f;
                light.innerSpotAngle = 72f;
                light.color = Color.Lerp(source.color, new Color(0.80f, 0.75f, 0.66f), 0.53f);
                light.range = Mathf.Clamp(hit.distance + 3.4f, 5.2f, 8.8f);
                float incidence = Mathf.Clamp01(Vector3.Dot(-direction, hit.normal));
                light.intensity = Mathf.Min(3.7f * LightPool.Dim, source.intensity * incidence * 6f / (1f + hit.distance * hit.distance));
                floor.lights.Add(light);
                LightPool.Register(light, false);
            }
            floor.windowBounceSources.Clear();
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
        static void Fill(Ctx c, bool hasWindow)
        {
            var r = c.r;
            if (r.IsLift) return;
            float size = Mathf.Max(c.W, c.D);
            Color col = r.church ? new Color(0.65f, 0.74f, 0.83f) : new Color(0.95f, 0.78f, 0.6f);
            float base_ = r.church ? (hasWindow ? 3.65f : 5.6f) : 2.6f;
            // teatro: un fondo rossastro e fioco (la sala e' quasi al buio); sanatorio: verdastro e freddo, come i neon
            if (!r.church && c.plan.theme == Theme.Teatro) { col = new Color(0.95f, 0.64f, 0.50f); base_ = r.wallH > 5.2f ? 11f : r.kind == RoomKind.Palco ? 9f : 4.6f; }
            else if (!r.church && c.plan.theme == Theme.Sanatorio) { col = new Color(0.72f, 0.88f, 0.82f); base_ = 2.7f; }
            // sopra il soffitto nelle stanze basse (niente macchia calda sull'intonaco: la faccia del soffitto guarda in basso e non la prende),
            // a mezza altezza in quelle alte
            float y = r.wallH < 5.2f ? r.wallH + 1.2f : 5.0f;
            float inten = base_ * (y / 3.6f) * (y / 3.6f);
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

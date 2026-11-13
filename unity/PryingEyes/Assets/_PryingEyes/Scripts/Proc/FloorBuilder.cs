using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Un piano costruito: radice, mappe delle stanze e punti utili a chi gioca (nascite dei nemici, ascensori).</summary>
    public sealed class FloorInstance
    {
        public Plan plan;
        public GameObject root;
        public Bounds bounds;
        public GameObject[] roomGo;
        public readonly List<Renderer> ceilings = new List<Renderer>();
        public readonly List<Vector3> groundSpawns = new List<Vector3>();
        public readonly List<Vector3> airSpawns = new List<Vector3>();
        public ProcLift arrival, exit;
        public readonly List<Candelabro> candles = new List<Candelabro>();
        public readonly List<Light> lights = new List<Light>();
        public readonly List<GalleryRoute> galleries = new List<GalleryRoute>();
        internal readonly List<Light> windowBounceSources = new List<Light>();
        /// <summary>Radici degli arredi: la navmesh li tratta da ostacoli non camminabili (la loro cima non e' un'isola).</summary>
        public readonly List<Transform> propRoots = new List<Transform>();

        public Room RoomAt(Vector3 p)
        {
            foreach (var r in plan.rooms)
                if (r.Contains(p.x, p.z)) return r;
            return null;
        }

        /// <summary>Altezza del soffitto sopra il punto (per chi vola).</summary>
        public float CeilingAt(Vector3 p)
        {
            var r = RoomAt(p);
            if (r == null) return 3f;
            return FloorBuilder.CeilHeight(r, p.x, p.z);
        }
    }

    /// <summary>Costruisce un piano dalla pianta: pavimenti, muri con porte, arcate e finestre, soffitti (piani, a travi,
    /// capriate, volte a botte), travi e nervature. Tutto in metri, una mesh per stanza e materiale. I muri sono sottili
    /// 30 cm: ogni stanza disegna la sua faccia, 15 cm dentro la linea, e tra le due facce resta il pieno.</summary>
    public static partial class FloorBuilder
    {
        public const float TH = 0.15f;          // mezzo spessore dei muri
        public const float Bay = 4.5f;          // passo delle campate

        static float M(int dm) => dm * 0.1f;

        public struct Style
        {
            public Material floor, wall, dado, ceil, trim, outer, beam;
            public float dadoH;
        }

        public static FloorInstance Build(Plan plan, Transform parent = null)
        {
            var fi = new FloorInstance { plan = plan };
            fi.root = new GameObject(plan.name);
            if (parent != null) fi.root.transform.SetParent(parent, false);
            fi.roomGo = new GameObject[plan.rooms.Count];
            foreach (var r in plan.rooms) BuildRoom(fi, r);
            RoomKits.PlaceWindowBounce(fi);
            plan.ComputeBounds();
            var b = new Bounds(new Vector3(M(plan.minX + plan.maxX) * 0.5f, 6f, M(plan.minZ + plan.maxZ) * 0.5f),
                               new Vector3(M(plan.maxX - plan.minX) + 8f, 16f, M(plan.maxZ - plan.minZ) + 8f));
            fi.bounds = b;
            return fi;
        }

        /// <summary>Superficie del pavimento per i passi: 0 pietra/marmo/piastrelle, 1 moquette, 2 legno.</summary>
        public static int Surface(Room r, Theme theme = Theme.Albergo)
        {
            if (!r.church && theme == Theme.Teatro)
                switch (r.kind)
                {
                    case RoomKind.Platea: case RoomKind.Palco: return 1;                    // moquette
                    case RoomKind.Palcoscenico: case RoomKind.Camerino: case RoomKind.Ridotto: return 2;   // assi e parquet
                    default: return 0;                                                       // marmo, cemento
                }
            if (!r.church && theme == Theme.Sanatorio) return r.kind == RoomKind.Archivio || r.kind == RoomKind.SalaMedici ? 2 : 0;
            switch (r.kind)
            {
                case RoomKind.Corridoio: case RoomKind.Galleria: return 1;
                case RoomKind.Camera:
                case RoomKind.Suite: return r.variant % 3 == 0 ? 1 : 2;
                case RoomKind.Ristorante:
                case RoomKind.Sacrestia: return 2;
                default: return 0;
            }
        }

        // ------------------------------------------------------------------ stili
        /// <summary>Materiali di una stanza. Il tema conta solo per i corridoi (servono a tutti e tre) e per l'esterno dei muri:
        /// le stanze del teatro e del sanatorio hanno il loro tipo.</summary>
        public static Style StyleOf(Room r, Theme theme = Theme.Albergo)
        {
            var s = new Style();
            int v = r.variant;
            string[] damask = { "mur_damasco", "mur_damasco_verde", "mur_damasco_ocra" };
            switch (r.kind)
            {
                case RoomKind.Atrio:
                case RoomKind.Rotonda:
                case RoomKind.GiardinoInverno:
                    s.floor = Mat("pav_marmo"); s.wall = Mat(damask[0]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.3f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); s.beam = Mat("trave"); break;
                case RoomKind.Corridoio:
                case RoomKind.Galleria:
                    if (theme == Theme.Sanatorio)       // corsia dell'istituto: linoleum e vernice verde
                    {
                        s.floor = Mat("pav_linoleo"); s.wall = Mat("mur_intonaco_freddo"); s.dado = Mat("mur_vernice_verde"); s.dadoH = 1.4f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco_freddo");
                    }
                    else if (theme == Theme.Teatro)     // corridoio delle quinte: cemento e intonaco nero
                    {
                        s.floor = Mat("pav_servizio"); s.wall = Mat("mur_intonaco_nero"); s.ceil = Mat("sof_scuro"); s.trim = Mat("mur_intonaco_nero");
                    }
                    else { s.floor = Mat("pav_moquette"); s.wall = Mat(damask[v % 3]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.15f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); }
                    break;
                case RoomKind.Camera:
                case RoomKind.Suite:
                    s.floor = Mat(v % 3 == 0 ? "pav_moquette" : "pav_parquet"); s.wall = Mat(damask[(v / 3) % 3]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.0f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); break;
                case RoomKind.Ristorante:
                case RoomKind.SalaBallo:
                    s.floor = Mat(r.kind == RoomKind.SalaBallo ? "pav_marmo" : "pav_parquet"); s.wall = Mat(damask[(v / 3) % 3]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.3f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); s.beam = Mat("trave"); break;
                case RoomKind.Cucina:
                    s.floor = Mat("pav_piastrelle"); s.wall = Mat("mur_intonaco"); s.dado = Mat("mur_piastrelle"); s.dadoH = 1.9f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco"); break;
                case RoomKind.Servizio:
                    s.floor = Mat("pav_servizio"); s.wall = Mat("mur_intonaco"); s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco"); break;
                case RoomKind.Nartece:
                case RoomKind.Navatella:
                case RoomKind.Campanile:
                    s.floor = Mat("pav_lastre"); s.wall = Mat("mur_pietra"); s.ceil = Mat("sof_legno"); s.trim = Mat("cornice_pietra"); s.beam = Mat("trave"); break;
                case RoomKind.Navata:
                    s.floor = Mat("pav_lastre"); s.wall = Mat("mur_pietra"); s.ceil = Mat("sof_legno"); s.trim = Mat("cornice_pietra"); s.beam = Mat("trave"); break;
                case RoomKind.Cappella:
                case RoomKind.Presbiterio:
                    s.floor = Mat("pav_lastre"); s.wall = Mat("mur_pietra"); s.ceil = Mat("sof_volta"); s.trim = Mat("cornice_pietra"); s.beam = Mat("cornice_pietra"); break;
                case RoomKind.Sacrestia:
                    s.floor = Mat("pav_parquet"); s.wall = Mat("mur_intonaco"); s.dado = Mat("mur_boiserie"); s.dadoH = 1.4f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); s.beam = Mat("trave"); break;
                case RoomKind.Cripta:
                    s.floor = Mat("pav_cripta"); s.wall = Mat("mur_cripta"); s.ceil = Mat("sof_volta"); s.trim = Mat("cornice_pietra"); s.beam = Mat("cornice_pietra"); break;
                // teatro: velluto rosso e legno scuro in sala, nero e polvere dietro il sipario
                case RoomKind.Platea:
                    s.floor = Mat("pav_moquetta"); s.wall = Mat("mur_velluto"); s.dado = Mat("mur_boiserie"); s.dadoH = 1.5f; s.ceil = Mat("sof_scuro"); s.trim = Mat("cornice_legno"); s.beam = Mat("trave"); break;
                case RoomKind.Palco:
                    s.floor = Mat("pav_moquetta"); s.wall = Mat("mur_velluto"); s.dado = Mat("mur_boiserie"); s.dadoH = 1.0f; s.ceil = Mat("sof_scuro"); s.trim = Mat("cornice_legno"); break;
                case RoomKind.Foyer:
                    s.floor = Mat("pav_marmo"); s.wall = Mat(damask[0]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.3f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); s.beam = Mat("trave"); break;
                case RoomKind.Palcoscenico:
                    s.floor = Mat("pav_palco"); s.wall = Mat("mur_intonaco_nero"); s.ceil = Mat("sof_scuro"); s.trim = Mat("mur_intonaco_nero"); s.beam = Mat("trave"); break;
                case RoomKind.Camerino:
                    s.floor = Mat("pav_parquet"); s.wall = Mat(damask[1]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.0f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); break;
                case RoomKind.Ridotto:
                    s.floor = Mat("pav_parquet"); s.wall = Mat(damask[2]); s.dado = Mat("mur_boiserie"); s.dadoH = 1.1f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); break;
                case RoomKind.Deposito:
                    s.floor = Mat("pav_servizio"); s.wall = Mat("mur_intonaco_nero"); s.ceil = Mat("sof_scuro"); s.trim = Mat("mur_intonaco_nero"); break;
                // sanatorio: linoleum, vernice verde e intonaco freddo; piastrelle bianche dove si opera e si lava
                case RoomKind.Accettazione:
                    s.floor = Mat("pav_linoleo"); s.wall = Mat("mur_intonaco_freddo"); s.dado = Mat("mur_vernice_verde"); s.dadoH = 1.5f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco_freddo"); s.beam = Mat("trave"); break;
                case RoomKind.Degenza:
                    s.floor = Mat("pav_linoleo"); s.wall = Mat("mur_intonaco_freddo"); s.dado = Mat("mur_vernice_verde"); s.dadoH = 1.4f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco_freddo"); break;
                case RoomKind.Corsia:
                    s.floor = Mat("pav_linoleo"); s.wall = Mat("mur_intonaco_freddo"); s.dado = Mat("mur_vernice_verde"); s.dadoH = 1.5f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco_freddo"); break;
                case RoomKind.Cella:
                    s.floor = Mat("pav_servizio"); s.wall = Mat("mur_vernice_verde"); s.ceil = Mat("sof_scuro"); s.trim = Mat("mur_vernice_verde"); break;
                case RoomKind.Obitorio:
                    s.floor = Mat("pav_piastrelle"); s.wall = Mat("mur_intonaco_freddo"); s.dado = Mat("mur_piastrelle"); s.dadoH = 2.1f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco_freddo"); break;
                case RoomKind.Refettorio:
                    s.floor = Mat("pav_linoleo"); s.wall = Mat("mur_intonaco_freddo"); s.dado = Mat("mur_vernice_verde"); s.dadoH = 1.3f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco_freddo"); s.beam = Mat("trave"); break;
                case RoomKind.SalaOperatoria:
                    s.floor = Mat("pav_piastrelle"); s.wall = Mat("mur_piastrelle"); s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco_freddo"); break;
                case RoomKind.Lavanderia:
                    s.floor = Mat("pav_piastrelle"); s.wall = Mat("mur_intonaco_freddo"); s.dado = Mat("mur_piastrelle"); s.dadoH = 1.9f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("mur_intonaco_freddo"); break;
                case RoomKind.Archivio:
                case RoomKind.SalaMedici:
                    s.floor = Mat("pav_parquet"); s.wall = Mat("mur_intonaco_freddo"); s.dado = Mat("mur_boiserie"); s.dadoH = 1.3f; s.ceil = Mat("sof_intonaco"); s.trim = Mat("cornice_legno"); break;
                case RoomKind.Ascensore:
                    s.floor = Mat("pav_servizio"); s.wall = Mat("mur_boiserie"); s.ceil = Mat("mur_boiserie"); s.trim = Mat("ottone"); break;
            }
            s.outer = r.church ? Mat("mur_pietra") : theme == Theme.Sanatorio ? Mat("mur_intonaco_freddo") : theme == Theme.Teatro ? Mat("mur_intonaco_nero") : Mat("mur_intonaco");
            return s;
        }

        static Material Mat(string n) => ProcKit.Mat(n);

        // ------------------------------------------------------------------ una stanza
        static void BuildRoom(FloorInstance fi, Room r)
        {
            var plan = fi.plan;
            var st = StyleOf(r, plan.theme);
            var go = new GameObject(string.Format("{0:00}_{1}", r.id, r.kind));
            go.transform.SetParent(fi.root.transform, false);
            fi.roomGo[r.id] = go;

            float x0 = M(r.x0), x1 = M(r.x1), z0 = M(r.z0), z1 = M(r.z1);
            float ix0 = x0 + TH, ix1 = x1 - TH, iz0 = z0 + TH, iz1 = z1 - TH;
            var shell = new MeshBuilder();
            var roof = new MeshBuilder();
            var archTrim = new MeshBuilder();
            var doors = new MeshBuilder();
            var frames = new MeshBuilder();

            // pavimento: sotto i muri fino alla linea di mezzo, cosi' alle soglie non resta nessun buco
            shell.Quad(st.floor, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0),
                       new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0), Vector3.up);

            Ceiling(roof, r, st, ix0, ix1, iz0, iz1);

            // muri
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E }) WallSide(shell, plan, r, st, s, ix0, ix1, iz0, iz1);
            // riquadri delle aperture (spalle, davanzale, architrave) e vetri
            foreach (var o in plan.openings)
            {
                if (o.a != r.id) continue;
                OpeningGeometry(shell, plan, r, o, st);
                if (o.style == OpenStyle.Lancetta) LancetFrame(archTrim, r, o, st.trim);
                if (r.church && o.b >= 0 && plan.rooms[o.b].church && o.Pointed)
                {
                    StoneArch(archTrim, r, o, st.trim);
                    if (r.kind == RoomKind.Nartece && plan.rooms[o.b].kind == RoomKind.Navata)
                        OpenChurchDoor(doors, fi, go.transform, r, o);
                }
            }

            // pianta a griglia: ogni porta ha la cornice (e la soglia) del colore della stanza che sta dall'altra parte, come le porte di Isaac
            if (plan.grid)
                foreach (var o in plan.openings)
                {
                    if ((o.a != r.id && o.b != r.id) || o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore) continue;
                    var other = plan.rooms[o.a == r.id ? o.b : o.a];
                    var fm = FrameMat(other.type);
                    if (fm == null) continue;
                    DoorFrame(frames, r, o, fm, !(r.church && other.church));
                }

            Flush(go, "Guscio", shell, true, null);
            Flush(go, "Soffitto", roof, true, fi.ceilings);
            Flush(go, "Cornici in pietra", archTrim, false, null);
            Flush(go, "Portale aperto", doors, true, null);
            Flush(go, "Cornici di tipo", frames, false, null);

            // il collider del pavimento e' un blocco: niente caduta tra i triangoli
            var floorGo = new GameObject("Pavimento");
            floorGo.transform.SetParent(go.transform, false);
            var bc = floorGo.AddComponent<BoxCollider>();
            bc.center = new Vector3((x0 + x1) * 0.5f, -0.25f, (z0 + z1) * 0.5f);
            bc.size = new Vector3(x1 - x0, 0.5f, z1 - z0);

            if (r.upperGallery) BuildGallery(fi, r, go);
            if (r.cornerRadius > TH) BuildRoundedCorners(r, st, go);
            var window = RoomKits.Dress(fi, r, go.transform);
            if (r.church && ProcKit.HasMat("foschia_chiesa")) ChurchHaze(go.transform, x0, x1, z0, z1, r.wallH, window);
        }

        // ------------------------------------------------------------------ cornici delle porte per tipo di stanza
        static readonly Dictionary<RoomType, Material> frameMats = new Dictionary<RoomType, Material>();

        /// <summary>Colore della cornice di una porta che porta in una stanza di questo tipo (nullo: nessuna cornice). La stessa tavolozza
        /// delle icone della minimappa.</summary>
        public static Color FrameColor(RoomType t)
        {
            switch (t)
            {
                case RoomType.Candela: return new Color(1.00f, 0.42f, 0.10f);       // fuoco: qui c'e' una candela e un round
                case RoomType.Negozio: return new Color(1.00f, 0.78f, 0.22f);       // oro: armi e scrigno
                case RoomType.Biblioteca: return new Color(0.62f, 0.40f, 1.00f);    // viola: i libri dei perk
                case RoomType.Boss: return new Color(0.90f, 0.10f, 0.14f);          // cremisi: il guardiano dell'uscita
                case RoomType.Segreta: return new Color(0.30f, 0.95f, 0.85f);       // turchese: il passaggio basso
                case RoomType.Partenza: return new Color(0.35f, 1.00f, 0.55f);      // verde: dove sei arrivato
                case RoomType.Guardia: return new Color(0.55f, 0.66f, 0.80f);       // acciaio: pattuglie
                default: return new Color(0f, 0f, 0f, 0f);
            }
        }

        static Material FrameMat(RoomType t)
        {
            var c = FrameColor(t);
            if (c.a <= 0f) return null;
            if (frameMats.TryGetValue(t, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "cornice_" + t };
            m.SetColor("_BaseColor", c * 0.22f);
            m.SetFloat("_Smoothness", 0.25f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * (t == RoomType.Guardia ? 0.55f : 1.35f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            frameMats[t] = m;
            return m;
        }

        /// <summary>La cornice di una porta vista dalla stanza 'room': due montanti e un architrave (o un arco acuto) sulla faccia del muro,
        /// e una striscia di soglia sul pavimento. Senza collisione. 'wall' falso: sui conci delle arcate di pietra non si mette la cornice
        /// (ci sarebbe dentro), resta la soglia.</summary>
        static void DoorFrame(MeshBuilder mb, Room room, Opening o, Material mat, bool wall)
        {
            bool ax = o.alongX;
            Vector3 along = ax ? Vector3.right : Vector3.forward;
            Vector3 across = ax ? Vector3.forward : Vector3.right;
            Vector3 inside = across * (((ax ? room.z1 : room.x1) == o.line) ? -1f : 1f);
            float hw = o.width * 0.05f;
            Vector3 origin = along * M(o.center) + across * M(o.line);
            Vector3 P(float x, float y, float depth) => origin + along * x + Vector3.up * y + inside * depth;
            // soglia: una lastra sottile sul pavimento, dalla linea del muro fin dentro la stanza (ogni stanza disegna la sua meta': non si sovrappongono)
            const float strip = 0.012f;
            mb.Quad(mat, P(-hw, strip, 0f), P(hw, strip, 0f), P(hw, strip, TH + 0.22f), P(-hw, strip, TH + 0.22f),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), Vector3.up);
            if (!wall) return;
            const float t = 0.10f, dep = 0.05f, d = TH + dep * 0.5f + 0.004f;
            float spring = o.Pointed ? o.Head(hw, hw) : o.top;
            foreach (int side in new[] { -1, 1 })
                mb.Beam(mat, P(side * (hw + t * 0.5f), 0f, d), P(side * (hw + t * 0.5f), spring, d), t, dep, inside);
            if (!o.Pointed)
                mb.Beam(mat, P(-hw - t, o.top + t * 0.5f, d), P(hw + t, o.top + t * 0.5f, d), t, dep, inside);
            else
            {
                const int segs = 12;
                for (int i = 0; i < segs; i++)
                {
                    float x0 = Mathf.Lerp(-hw, hw, i / (float)segs), x1 = Mathf.Lerp(-hw, hw, (i + 1f) / segs);
                    mb.Beam(mat, P(x0, o.Head(x0, hw) + t * 0.5f, d), P(x1, o.Head(x1, hw) + t * 0.5f, d), t, dep, inside);
                }
            }
        }

        static void ChurchHaze(Transform room, float x0, float x1, float z0, float z1, float height, Light window)
        {
            var volume = GameObject.CreatePrimitive(PrimitiveType.Cube);
            volume.name = "Foschia";
            var collider = volume.GetComponent<Collider>();
            collider.enabled = false;
            Util.Kill(collider);
            volume.transform.SetParent(room, false);
            volume.transform.localPosition = new Vector3((x0 + x1) * 0.5f, height * 0.5f, (z0 + z1) * 0.5f);
            volume.transform.localScale = new Vector3(x1 - x0, height, z1 - z0);
            var renderer = volume.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ProcKit.Mat("foschia_chiesa");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (window != null)
            {
                var properties = new MaterialPropertyBlock();
                var position = window.transform.position;
                var direction = window.transform.forward;
                properties.SetVector("_WindowPosition", new Vector4(position.x, position.y, position.z, window.intensity * 0.16f));
                properties.SetVector("_WindowDirection", new Vector4(direction.x, direction.y, direction.z, Mathf.Cos(window.spotAngle * 0.5f * Mathf.Deg2Rad)));
                properties.SetColor("_WindowTint", window.color);
                renderer.SetPropertyBlock(properties);
            }
        }

        static void Flush(GameObject parent, string name, MeshBuilder mb, bool collide, List<Renderer> track)
        {
            if (mb.Empty) return;
            var mesh = mb.ToMesh(parent.name + "_" + name, out var mats);
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            if (collide) g.AddComponent<MeshCollider>().sharedMesh = mesh;
            track?.Add(mr);
        }

        // ------------------------------------------------------------------ altezza e soffitti
        public static float CeilHeight(Room r, float x, float z)
        {
            float x0 = M(r.x0) + TH, x1 = M(r.x1) - TH, z0 = M(r.z0) + TH, z1 = M(r.z1) - TH;
            if (r.ceil == CeilKind.Gable)
            {
                float a = r.ridgeAlongX ? (z1 - z0) * 0.5f : (x1 - x0) * 0.5f;
                float d = r.ridgeAlongX ? Mathf.Abs(z - (z0 + z1) * 0.5f) : Mathf.Abs(x - (x0 + x1) * 0.5f);
                return r.wallH + (r.ridgeH - r.wallH) * Mathf.Clamp01(1f - d / a);
            }
            if (r.ceil == CeilKind.BarrelRound || r.ceil == CeilKind.BarrelPointed)
            {
                float a = r.ridgeAlongX ? (z1 - z0) * 0.5f : (x1 - x0) * 0.5f;
                float d = r.ridgeAlongX ? z - (z0 + z1) * 0.5f : x - (x0 + x1) * 0.5f;
                return r.wallH + Arch.Y(d, a, r.ridgeH - r.wallH, r.ceil == CeilKind.BarrelPointed);
            }
            return r.wallH;
        }

        static void Ceiling(MeshBuilder mb, Room r, Style st, float ix0, float ix1, float iz0, float iz1)
        {
            float H = r.wallH;
            switch (r.ceil)
            {
                case CeilKind.Flat:
                case CeilKind.Beams:
                    mb.Quad(st.ceil, new Vector3(ix0, H, iz0), new Vector3(ix1, H, iz0), new Vector3(ix1, H, iz1), new Vector3(ix0, H, iz1),
                            new Vector2(ix0, iz0), new Vector2(ix1, iz0), new Vector2(ix1, iz1), new Vector2(ix0, iz1), Vector3.down);
                    if (r.ceil == CeilKind.Beams && st.beam != null) FlatBeams(mb, st, ix0, ix1, iz0, iz1, H);
                    break;
                case CeilKind.Gable:
                    GableCeiling(mb, r, st, ix0, ix1, iz0, iz1);
                    break;
                case CeilKind.BarrelRound:
                case CeilKind.BarrelPointed:
                    BarrelCeiling(mb, r, st, ix0, ix1, iz0, iz1);
                    break;
            }
        }

        static void FlatBeams(MeshBuilder mb, Style st, float ix0, float ix1, float iz0, float iz1, float H)
        {
            bool alongZ = (iz1 - iz0) <= (ix1 - ix0);   // le travi attraversano il lato corto
            float len = alongZ ? ix1 - ix0 : iz1 - iz0;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.4f));
            for (int k = 0; k < n; k++)
            {
                float p = (k + 0.5f) / n * len;
                if (alongZ) mb.Beam(st.beam, new Vector3(ix0 + p, H - 0.15f, iz0), new Vector3(ix0 + p, H - 0.15f, iz1), 0.24f, 0.30f, Vector3.up);
                else mb.Beam(st.beam, new Vector3(ix0, H - 0.15f, iz0 + p), new Vector3(ix1, H - 0.15f, iz0 + p), 0.24f, 0.30f, Vector3.up);
            }
        }

        static void GableCeiling(MeshBuilder mb, Room r, Style st, float ix0, float ix1, float iz0, float iz1)
        {
            float H = r.wallH, R = r.ridgeH;
            if (r.ridgeAlongX)
            {
                float zc = (iz0 + iz1) * 0.5f;
                float sl = Mathf.Sqrt((zc - iz0) * (zc - iz0) + (R - H) * (R - H));
                mb.Quad(st.ceil, new Vector3(ix0, H, iz0), new Vector3(ix1, H, iz0), new Vector3(ix1, R, zc), new Vector3(ix0, R, zc),
                        new Vector2(ix0, 0), new Vector2(ix1, 0), new Vector2(ix1, sl), new Vector2(ix0, sl), Vector3.down);
                mb.Quad(st.ceil, new Vector3(ix0, H, iz1), new Vector3(ix1, H, iz1), new Vector3(ix1, R, zc), new Vector3(ix0, R, zc),
                        new Vector2(ix0, 0), new Vector2(ix1, 0), new Vector2(ix1, sl), new Vector2(ix0, sl), Vector3.down);
                if (st.beam == null) return;
                // capriate a ogni campata: catena, puntoni, monaco e due saette; correnti lungo il tetto
                int bays = Mathf.Max(1, Mathf.RoundToInt((ix1 - ix0) / Bay));
                for (int k = 1; k < bays; k++)
                {
                    float x = ix0 + (ix1 - ix0) * k / bays;
                    Truss(mb, st.beam, new Vector3(x, 0, iz0), new Vector3(x, 0, iz1), new Vector3(x, 0, zc), H, R);
                }
                mb.Beam(st.beam, new Vector3(ix0, R - 0.14f, zc), new Vector3(ix1, R - 0.14f, zc), 0.30f, 0.32f, Vector3.up);
                foreach (float f in new[] { 0.5f })
                    foreach (float zz in new[] { Mathf.Lerp(zc, iz0, f), Mathf.Lerp(zc, iz1, f) })
                        mb.Beam(st.beam, new Vector3(ix0, Mathf.Lerp(R, H, f) - 0.12f, zz), new Vector3(ix1, Mathf.Lerp(R, H, f) - 0.12f, zz), 0.2f, 0.22f, Vector3.up);
            }
            else
            {
                float xc = (ix0 + ix1) * 0.5f;
                float sl = Mathf.Sqrt((xc - ix0) * (xc - ix0) + (R - H) * (R - H));
                mb.Quad(st.ceil, new Vector3(ix0, H, iz0), new Vector3(ix0, H, iz1), new Vector3(xc, R, iz1), new Vector3(xc, R, iz0),
                        new Vector2(iz0, 0), new Vector2(iz1, 0), new Vector2(iz1, sl), new Vector2(iz0, sl), Vector3.down);
                mb.Quad(st.ceil, new Vector3(ix1, H, iz0), new Vector3(ix1, H, iz1), new Vector3(xc, R, iz1), new Vector3(xc, R, iz0),
                        new Vector2(iz0, 0), new Vector2(iz1, 0), new Vector2(iz1, sl), new Vector2(iz0, sl), Vector3.down);
                if (st.beam == null) return;
                int bays = Mathf.Max(1, Mathf.RoundToInt((iz1 - iz0) / Bay));
                for (int k = 1; k < bays; k++)
                {
                    float z = iz0 + (iz1 - iz0) * k / bays;
                    Truss(mb, st.beam, new Vector3(ix0, 0, z), new Vector3(ix1, 0, z), new Vector3(xc, 0, z), H, R);
                }
                mb.Beam(st.beam, new Vector3(xc, R - 0.14f, iz0), new Vector3(xc, R - 0.14f, iz1), 0.30f, 0.32f, Vector3.up);
            }
        }

        // capriata: a = appoggio 1, b = appoggio 2, c = colmo (ordinate in y ignorate: H = quota d'imposta, R = colmo)
        static void Truss(MeshBuilder mb, Material m, Vector3 a, Vector3 b, Vector3 c, float H, float R)
        {
            Vector3 A = new Vector3(a.x, H, a.z), B = new Vector3(b.x, H, b.z), C = new Vector3(c.x, R, c.z);
            Vector3 T0 = new Vector3(a.x, H - 0.18f, a.z), T1 = new Vector3(b.x, H - 0.18f, b.z);
            mb.Beam(m, T0, T1, 0.32f, 0.36f, Vector3.up);                                   // catena
            mb.Beam(m, A + Vector3.down * 0.16f, C + Vector3.down * 0.16f, 0.28f, 0.30f, Vector3.up);   // puntoni
            mb.Beam(m, B + Vector3.down * 0.16f, C + Vector3.down * 0.16f, 0.28f, 0.30f, Vector3.up);
            Vector3 mid = new Vector3(c.x, H - 0.18f, c.z);
            mb.Beam(m, mid, C + Vector3.down * 0.16f, 0.26f, 0.26f, Vector3.right);          // monaco
            Vector3 q1 = Vector3.Lerp(T0, T1, 0.25f), q2 = Vector3.Lerp(T0, T1, 0.75f);
            Vector3 r1 = Vector3.Lerp(A, C, 0.55f) + Vector3.down * 0.16f, r2 = Vector3.Lerp(B, C, 0.55f) + Vector3.down * 0.16f;
            mb.Beam(m, q1, r1, 0.2f, 0.2f, Vector3.right);                                  // saette
            mb.Beam(m, q2, r2, 0.2f, 0.2f, Vector3.right);
        }

        static void BarrelCeiling(MeshBuilder mb, Room r, Style st, float ix0, float ix1, float iz0, float iz1)
        {
            float H = r.wallH, rise = r.ridgeH - r.wallH;
            bool pointed = r.ceil == CeilKind.BarrelPointed;
            bool alongX = r.ridgeAlongX;
            float a = alongX ? (iz1 - iz0) * 0.5f : (ix1 - ix0) * 0.5f;
            float c = alongX ? (iz0 + iz1) * 0.5f : (ix0 + ix1) * 0.5f;
            float a0 = alongX ? ix0 : iz0, a1 = alongX ? ix1 : iz1;      // estremi lungo l'asse della volta
            int n = 18;
            // profilo in sezione: (distanza dall'asse, quota)
            var px = new float[n + 1]; var py = new float[n + 1]; var nrm = new Vector2[n + 1]; var arc = new float[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = -a + 2f * a * i / n;
                px[i] = t; py[i] = H + Arch.Y(t, a, rise, pointed); nrm[i] = Arch.InNormal(t, a, rise, pointed);
                arc[i] = i == 0 ? 0f : arc[i - 1] + Mathf.Sqrt((px[i] - px[i - 1]) * (px[i] - px[i - 1]) + (py[i] - py[i - 1]) * (py[i] - py[i - 1]));
            }
            Vector3 Pt(float along, int i, float offs = 0f)
            {
                float dd = px[i] + nrm[i].x * offs, yy = py[i] + nrm[i].y * offs;
                return alongX ? new Vector3(along, yy, c + dd) : new Vector3(c + dd, yy, along);
            }
            Vector3 Nrm(int i) => alongX ? new Vector3(0f, nrm[i].y, nrm[i].x) : new Vector3(nrm[i].x, nrm[i].y, 0f);
            for (int i = 0; i < n; i++)
                mb.QuadSmooth(st.ceil, Pt(a0, i), Pt(a1, i), Pt(a1, i + 1), Pt(a0, i + 1), Nrm(i), Nrm(i), Nrm(i + 1), Nrm(i + 1),
                              new Vector2(a0, arc[i]), new Vector2(a1, arc[i]), new Vector2(a1, arc[i + 1]), new Vector2(a0, arc[i + 1]));
            // nervature ogni ~3 m
            if (st.beam == null) return;
            float len = a1 - a0;
            int ribs = Mathf.Max(1, Mathf.RoundToInt(len / 3.2f));
            const float w = 0.16f, dep = 0.16f;
            for (int k = 0; k <= ribs; k++)
            {
                float p = a0 + len * k / ribs;
                for (int i = 0; i < n; i++)
                {
                    // fascia sotto: tra i due profili spostati verso l'interno
                    mb.QuadSmooth(st.beam, Pt(p - w, i, dep), Pt(p + w, i, dep), Pt(p + w, i + 1, dep), Pt(p - w, i + 1, dep), Nrm(i), Nrm(i), Nrm(i + 1), Nrm(i + 1),
                                  new Vector2(0, arc[i]), new Vector2(0.32f, arc[i]), new Vector2(0.32f, arc[i + 1]), new Vector2(0, arc[i + 1]));
                    // fianchi della nervatura
                    foreach (float sg in new[] { -1f, 1f })
                    {
                        Vector3 s0 = Pt(p + sg * w, i, 0f), s1 = Pt(p + sg * w, i + 1, 0f), d0 = Pt(p + sg * w, i, dep), d1 = Pt(p + sg * w, i + 1, dep);
                        Vector3 axis = alongX ? new Vector3(sg, 0f, 0f) : new Vector3(0f, 0f, sg);
                        mb.Quad(st.beam, s0, s1, d1, d0, new Vector2(0, arc[i]), new Vector2(0, arc[i + 1]), new Vector2(dep, arc[i + 1]), new Vector2(dep, arc[i]), axis);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ muri
        static void WallSide(MeshBuilder mb, Plan plan, Room r, Style st, Side s, float ix0, float ix1, float iz0, float iz1)
        {
            bool ax = Plan.AlongX(s);
            Vector3 origin, u, facing;
            float len;
            switch (s)
            {
                case Side.S: origin = new Vector3(ix0, 0, iz0); u = Vector3.right; facing = Vector3.forward; len = ix1 - ix0; break;
                case Side.N: origin = new Vector3(ix0, 0, iz1); u = Vector3.right; facing = Vector3.back; len = ix1 - ix0; break;
                case Side.W: origin = new Vector3(ix0, 0, iz0); u = Vector3.forward; facing = Vector3.right; len = iz1 - iz0; break;
                default: origin = new Vector3(ix1, 0, iz0); u = Vector3.forward; facing = Vector3.left; len = iz1 - iz0; break;
            }
            float faceStart = ax ? ix0 : iz0;
            TopFunc(r, ax, len, out var top, out var breaks);

            var holes = new List<Hole>();
            foreach (var o in plan.OpeningsOn(r, s))
                holes.Add(new Hole { c = M(o.center) - faceStart, hw = o.width * 0.05f, o = o });

            DrawFace(mb, st.wall, st.dado, st.dadoH, origin, u, facing, len, top, breaks, holes, faceStart);

            // pelle esterna lungo i tratti che danno sul vuoto
            Vector3 outward = -facing;
            foreach (var seg in plan.Segments(r, s))
            {
                if (seg.other >= 0) continue;
                float sa = M(seg.a0), sb = M(seg.a1);
                Vector3 o2 = ax ? new Vector3(sa, 0, Plan.Line(r, s) * 0.1f) : new Vector3(Plan.Line(r, s) * 0.1f, 0, sa);
                o2 += outward * TH;
                var hs = new List<Hole>();
                foreach (var h in holes)
                {
                    float c = h.c + faceStart - sa;
                    if (h.o.b < 0 && c - h.hw >= -0.001f && c + h.hw <= sb - sa + 0.001f && !h.o.oneSided) hs.Add(new Hole { c = c, hw = h.hw, o = h.o });
                }
                float shift = sa - faceStart;
                TopFunc(r, ax, len, out var top0, out var br0);
                Func<float, float> topShifted = x => top0(x + shift);
                DrawFace(mb, st.outer, null, 0f, o2, u, outward, sb - sa, topShifted, null, hs, sa);
            }
        }

        static void DrawFace(MeshBuilder mb, Material wall, Material dado, float dadoH, Vector3 origin, Vector3 u, Vector3 facing, float len,
                             Func<float, float> top, float[] breaks, List<Hole> holes, float uOff)
        {
            if (len < 0.01f) return;
            if (dado != null)
            {
                mb.WallFace(dado, origin, u, facing, len, 0f, x => Mathf.Min(dadoH, top(x)), holes, uOff, breaks);
                mb.WallFace(wall, origin, u, facing, len, dadoH, top, holes, uOff, breaks);
            }
            else mb.WallFace(wall, origin, u, facing, len, 0f, top, holes, uOff, breaks);
        }

        /// <summary>Profilo alto del muro lungo una faccia: costante, oppure triangolo/arco sulle testate delle capriate e delle volte.</summary>
        static void TopFunc(Room r, bool faceAlongX, float len, out Func<float, float> top, out float[] breaks)
        {
            float H = r.wallH;
            breaks = null;
            top = x => H;
            bool parallel = r.ridgeAlongX == faceAlongX;
            if (parallel) return;
            if (r.ceil == CeilKind.Gable)
            {
                float R = r.ridgeH - H, half = len * 0.5f;
                top = x => H + R * Mathf.Clamp01(1f - Mathf.Abs(x - half) / half);
                breaks = new[] { half };
            }
            else if (r.ceil == CeilKind.BarrelRound || r.ceil == CeilKind.BarrelPointed)
            {
                float rise = r.ridgeH - H, a = len * 0.5f;
                bool pt = r.ceil == CeilKind.BarrelPointed;
                top = x => H + Arch.Y(x - a, a, rise, pt);
                int n = 28;
                breaks = new float[n - 1];
                for (int i = 1; i < n; i++) breaks[i - 1] = len * i / n;
            }
        }

        // ------------------------------------------------------------------ aperture
        static void OpeningGeometry(MeshBuilder mb, Plan plan, Room A, Opening o, Style st)
        {
            bool ax = o.alongX;
            Vector3 e = ax ? Vector3.right : Vector3.forward;
            Vector3 nOut = ax ? (A.z1 == o.line ? Vector3.forward : Vector3.back) : (A.x1 == o.line ? Vector3.right : Vector3.left);
            float line = M(o.line);
            float sLo = M(o.Lo), sHi = M(o.Hi), hw = (sHi - sLo) * 0.5f, c = (sLo + sHi) * 0.5f;
            float dA = -TH, dB = TH;
            Vector3 P(float s, float y, float d) => ax ? new Vector3(s, y, line + d * nOut.z) : new Vector3(line + d * nOut.x, y, s);
            var mat = st.trim != null ? st.trim : st.wall;
            float edgeTop = o.Pointed ? o.top - o.RiseFactor * hw : o.top;
            float sill = o.sill;
            // spalle
            mb.Quad(mat, P(sLo, sill, dA), P(sLo, edgeTop, dA), P(sLo, edgeTop, dB), P(sLo, sill, dB),
                    new Vector2(0, sill), new Vector2(0, edgeTop), new Vector2(0.3f, edgeTop), new Vector2(0.3f, sill), e);
            mb.Quad(mat, P(sHi, sill, dA), P(sHi, edgeTop, dA), P(sHi, edgeTop, dB), P(sHi, sill, dB),
                    new Vector2(0, sill), new Vector2(0, edgeTop), new Vector2(0.3f, edgeTop), new Vector2(0.3f, sill), -e);
            if (sill > 0.01f)
                mb.Quad(mat, P(sLo, sill, dA), P(sHi, sill, dA), P(sHi, sill, dB), P(sLo, sill, dB),
                        new Vector2(sLo, 0), new Vector2(sHi, 0), new Vector2(sHi, 0.3f), new Vector2(sLo, 0.3f), Vector3.up);
            if (!o.Pointed)
                mb.Quad(mat, P(sLo, o.top, dA), P(sHi, o.top, dA), P(sHi, o.top, dB), P(sLo, o.top, dB),
                        new Vector2(sLo, 0), new Vector2(sHi, 0), new Vector2(sHi, 0.3f), new Vector2(sLo, 0.3f), Vector3.down);
            else
            {
                int n = Mathf.Max(8, Mathf.CeilToInt(2f * hw / 0.12f));
                float rise = o.RiseFactor * hw;
                float acc = 0f;
                float prevY = 0f;
                for (int i = 0; i < n; i++)
                {
                    float s0 = sLo + 2f * hw * i / n, s1 = sLo + 2f * hw * (i + 1) / n;
                    float y0 = o.Head(s0 - c, hw), y1 = o.Head(s1 - c, hw);
                    var n0 = ArchNormal(o, s0 - c, hw, rise, e); var n1 = ArchNormal(o, s1 - c, hw, rise, e);
                    float seg = Mathf.Sqrt((s1 - s0) * (s1 - s0) + (y1 - y0) * (y1 - y0));
                    mb.QuadSmooth(mat, P(s0, y0, dA), P(s1, y1, dA), P(s1, y1, dB), P(s0, y0, dB), n0, n1, n1, n0,
                                  new Vector2(acc, 0), new Vector2(acc + seg, 0), new Vector2(acc + seg, 0.3f), new Vector2(acc, 0.3f));
                    acc += seg; prevY = y1;
                }
            }
            // vetro al centro dello spessore (solo le finestre sul vuoto)
            if (o.b < 0) Glass(mb, o, A, P, nOut);
        }

        static void LancetFrame(MeshBuilder mesh, Room room, Opening opening, Material stone)
        {
            Vector3 along = opening.alongX ? Vector3.right : Vector3.forward;
            Vector3 across = opening.alongX ? Vector3.forward : Vector3.right;
            Vector3 inside = across * ((opening.alongX ? room.z1 : room.x1) == opening.line ? -1f : 1f);
            float center = M(opening.Lo + opening.Hi) * 0.5f;
            float half = M(opening.Hi - opening.Lo) * 0.5f;
            Vector3 origin = along * center + across * M(opening.line);
            Vector3 Point(float x, float y, float depth) => origin + along * x + Vector3.up * y + inside * depth;

            // Mostra lo spessore della battuta dentro al vano e un davanzale sporgente.
            float rim = half - 0.035f;
            float yBase = opening.sill + 0.025f;
            Vector3 Head(float x) => Point(x, opening.Head(x, half) - 0.034f, 0.053f);
            mesh.Beam(stone, Point(-half - 0.085f, opening.sill - 0.028f, TH - 0.07f),
                Point(half + 0.085f, opening.sill - 0.028f, TH - 0.07f), 0.31f, 0.095f, Vector3.up);
            mesh.Beam(stone, Point(-rim, yBase, 0.053f), Head(-rim), 0.065f, 0.07f, inside);
            mesh.Beam(stone, Point(rim, yBase, 0.053f), Head(rim), 0.065f, 0.07f, inside);
            const int segments = 14;
            for (int i = 0; i < segments; i++)
                mesh.Beam(stone, Head(Mathf.Lerp(-rim, rim, i / (float)segments)),
                    Head(Mathf.Lerp(-rim, rim, (i + 1f) / segments)), 0.065f, 0.07f, inside);

            var iron = Mat("ferro");
            float spring = opening.Head(half, half);
            foreach (float joint in new[] { 0.25f, 0.48f })
            {
                float y = Mathf.Lerp(opening.sill, opening.top, joint);
                if (y < spring - 0.02f)
                    mesh.Beam(iron, Point(-rim, y, 0.027f), Point(rim, y, 0.027f), 0.044f, 0.024f, Vector3.up);
            }
        }

        /// <summary>Conci separati e cornice a due risalti sopra il muro, fuori dalla luce utile del passaggio.</summary>
        static void StoneArch(MeshBuilder mesh, Room room, Opening opening, Material stone)
        {
            if (stone == null) return;
            bool alongX = opening.alongX;
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            Vector3 outward = alongX ? (room.z1 == opening.line ? Vector3.forward : Vector3.back)
                : (room.x1 == opening.line ? Vector3.right : Vector3.left);
            Vector3 origin = along * M(opening.Lo + opening.Hi) * 0.5f + (alongX ? Vector3.forward : Vector3.right) * M(opening.line);
            float half = M(opening.Hi - opening.Lo) * 0.5f;
            float spring = opening.Head(half, half);
            float width = Mathf.Clamp(half * 0.18f, 0.13f, 0.25f);
            Vector3 Size(float span, float height, float depth) => alongX ? new Vector3(span, height, depth) : new Vector3(depth, height, span);
            var wear = new Rng(unchecked(room.seed + opening.center * 1181 + opening.line * 877));

            foreach (int side in new[] { -1, 1 })
            {
                Vector3 facing = outward * side;
                Vector3 wall = origin + facing * TH;
                Vector3 Point(float t, float offset, float depth)
                {
                    float x = Mathf.Lerp(-half, half, t);
                    float y = opening.Head(x, half) + offset * (1f - Mathf.Abs(x) / half);
                    return wall + along * (x * (1f + offset / half)) + Vector3.up * y + facing * depth;
                }

                void Course(float from, float to, float depth, int stones)
                {
                    float start = 0f;
                    for (int i = 0; i < stones; i++)
                    {
                        float end = i == stones - 1 ? 1f : (i + 1f + wear.Range(-0.24f, 0.24f)) / stones;
                        float gap = wear.Range(0.018f, 0.042f) / stones;
                        float t0 = start + gap, t1 = end - gap;
                        float outer = to + wear.Range(-0.018f, 0.012f);
                        CarvedStone(mesh, stone, Point(t0, from, 0f), Point(t1, from, 0f),
                                    Point(t1, outer, 0f), Point(t0, outer, 0f), facing,
                                    depth + wear.Range(-0.012f, 0.011f), wear.Range(0.009f, 0.022f));
                        start = end;
                    }
                }

                int count = Mathf.Clamp(Mathf.CeilToInt(half * 12f), 9, 30);
                Course(width * 0.95f, width * 1.55f, 0.055f, count);
                Course(0.015f, width, 0.13f, count);
                foreach (int end in new[] { -1, 1 })
                {
                    Vector3 jamb = wall + along * end * (half + width * 0.52f);
                    int blocks = Mathf.Max(1, Mathf.CeilToInt(spring / 0.42f));
                    float height = spring / blocks;
                    float bottom = 0f;
                    for (int i = 0; i < blocks; i++)
                    {
                        float top = i == blocks - 1 ? spring : (i + 1) * height + wear.Range(-0.055f, 0.055f);
                        Vector3 a = jamb + Vector3.up * (bottom + 0.003f) - along * (width * 0.5f);
                        Vector3 d = jamb + Vector3.up * (top - 0.003f) - along * (width * 0.5f);
                        Vector3 w = along * width;
                        CarvedStone(mesh, stone, a, a + w, d + w, d, facing, wear.Range(0.11f, 0.137f), wear.Range(0.012f, 0.022f));
                        bottom = top;
                    }
                    mesh.Box(stone, jamb + Vector3.up * (spring - 0.04f) + facing * 0.082f, Size(width * 1.30f, 0.11f, 0.164f));
                    mesh.Box(stone, jamb + Vector3.up * 0.08f + facing * 0.079f, Size(width * 1.27f, 0.16f, 0.158f));
                    if (room.kind == RoomKind.Nartece && side < 0 && opening.width >= 28 && opening.style == OpenStyle.Arco)
                        PortalColumn(mesh, stone, wall + along * end * (half + width + 0.15f) - facing * 0.02f, spring);
                }
            }
        }

        static void PortalColumn(MeshBuilder mesh, Material stone, Vector3 foot, float spring)
        {
            var profile = new (float y, float radius)[]
            {
                (0f, 0.202f), (0.11f, 0.202f), (0.14f, 0.178f), (0.24f, 0.157f),
                (0.27f, 0.161f), (0.30f, 0.136f), (spring - 0.29f, 0.122f),
                (spring - 0.25f, 0.154f), (spring - 0.19f, 0.149f), (spring - 0.09f, 0.182f),
                (spring - 0.05f, 0.197f), (spring + 0.01f, 0.197f)
            };
            const int faces = 16;
            Vector3 Radial(int i)
            {
                float angle = i * (2f * Mathf.PI / faces);
                return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            Vector3 P((float y, float radius) ring, Vector3 radial) => foot + Vector3.up * ring.y + radial * ring.radius;
            for (int r = 1; r < profile.Length; r++)
                for (int i = 0; i < faces; i++)
                {
                    Vector3 a = Radial(i), b = Radial(i + 1);
                    var low = profile[r - 1];
                    var high = profile[r];
                    Vector3 n0 = (a * (high.y - low.y) + Vector3.up * (low.radius - high.radius)).normalized;
                    Vector3 n1 = (b * (high.y - low.y) + Vector3.up * (low.radius - high.radius)).normalized;
                    float u0 = i * (0.95f / faces), u1 = (i + 1) * (0.95f / faces);
                    mesh.QuadSmooth(stone, P(low, a), P(low, b), P(high, b), P(high, a), n0, n1, n1, n0,
                                    new Vector2(u0, low.y), new Vector2(u1, low.y), new Vector2(u1, high.y), new Vector2(u0, high.y));
                }
        }

        // Il gradino del bordo è geometria: intercetta le candele anche quando il concio è visto di taglio.
        static void CarvedStone(MeshBuilder mesh, Material stone, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                                Vector3 facing, float depth, float bevel)
        {
            Vector3 center = (a + b + c + d) * 0.25f;
            Vector3 rim = facing * (depth - bevel);
            Vector3 Top(Vector3 p) => Vector3.MoveTowards(p, center, bevel * 1.4f) + facing * depth;
            void Edge(Vector3 p, Vector3 q)
            {
                mesh.QuadW(stone, p, q, q + rim, p + rim, (p + q) * 0.5f - center);
                mesh.QuadW(stone, p + rim, q + rim, Top(q), Top(p), facing);
            }
            Edge(a, b); Edge(b, c); Edge(c, d); Edge(d, a);
            mesh.QuadW(stone, Top(a), Top(b), Top(c), Top(d), facing);
        }

        static void OpenChurchDoor(MeshBuilder mesh, FloorInstance floor, Transform roomRoot, Room room, Opening opening)
        {
            bool alongX = opening.alongX;
            Vector3 forward = alongX ? (room.z1 == opening.line ? Vector3.forward : Vector3.back)
                : (room.x1 == opening.line ? Vector3.right : Vector3.left);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 span = alongX ? Vector3.right : Vector3.forward;
            Vector3 origin = span * M(opening.center) + (alongX ? Vector3.forward : Vector3.right) * M(opening.line);
            float half = M(opening.Hi - opening.Lo) * 0.5f;
            float width = Mathf.Min(1.39f, half);
            float Top(float x) => opening.Head(half - x, half) - 0.08f;
            var hinge = origin + right * (half - 0.035f) - forward * (TH + 0.18f);
            var leaf = (right * 0.96f - forward * 0.28f).normalized;
            var normal = Vector3.Cross(leaf, Vector3.up);
            float yaw = Quaternion.LookRotation(normal).eulerAngles.y;
            var oak = Mat("quercia_portale");
            var iron = Mat("ferro");
            Vector3 P(float x, float y, float z) => hinge + leaf * x + Vector3.up * y + normal * z;
            void Detail(Material m, float x, float y, float z, Vector3 size) => mesh.Box(m, P(x, y, z), size, yaw);

            const int boards = 10;
            for (int i = 0; i < boards; i++)
            {
                float x0 = width * i / boards, x1 = width * (i + 1) / boards;
                float y0 = Top(x0), y1 = Top(x1);
                mesh.QuadW(oak, P(x0, y0, -0.042f), P(x1, y1, -0.042f), P(x1, y1, 0.042f), P(x0, y0, 0.042f), Vector3.up);
                mesh.QuadW(oak, P(x0, 0.035f, -0.042f), P(x1, 0.035f, -0.042f), P(x1, 0.035f, 0.042f), P(x0, 0.035f, 0.042f), Vector3.down);
                foreach (float z in new[] { -0.042f, 0.042f })
                    mesh.QuadW(oak, P(x0, 0.035f, z), P(x1, 0.035f, z), P(x1, y1, z), P(x0, y0, z), normal * Mathf.Sign(z));
            }
            foreach (float x in new[] { 0f, width })
                mesh.QuadW(oak, P(x, 0.035f, -0.042f), P(x, Top(x), -0.042f), P(x, Top(x), 0.042f), P(x, 0.035f, 0.042f), x == 0f ? -leaf : leaf);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < boards; i++)
                {
                    float x0 = width * i / boards, x1 = width * (i + 1) / boards;
                    float y0 = Top(x0), y1 = Top(x1);
                    float z = side * (i % 3 == 1 ? 0.046f : 0.053f);
                    const float bottom = 0.035f;
                    float offset = (i * 0.47f) % 1.35f;
                    Vector2 U(float x, float y) => new Vector2(x + offset, y + offset * 0.44f);
                    mesh.Quad(oak, P(x0 + 0.001f, bottom, z), P(x1 - 0.001f, bottom, z), P(x1 - 0.001f, y1, z), P(x0 + 0.001f, y0, z),
                              U(x0, bottom), U(x1, bottom), U(x1, y1), U(x0, y0), normal * side);
                    mesh.Beam(oak, P(x0, y0 - 0.055f, side * 0.061f), P(x1, y1 - 0.055f, side * 0.061f), 0.115f, 0.032f, normal);
                }
                foreach (float x in new[] { 0.051f, width - 0.051f })
                {
                    float h = Top(x) - 0.065f;
                    Detail(oak, x, h * 0.5f + 0.035f, side * 0.065f, new Vector3(0.105f, h, 0.045f));
                }
                foreach (float y in new[] { 0.13f, 1.14f })
                    Detail(oak, width * 0.5f, y, side * 0.066f, new Vector3(width, 0.13f, 0.045f));
                foreach (float y in new[] { 0.36f, Top(0f) - 0.23f })
                {
                    Detail(iron, width * 0.24f, y, side * 0.085f, new Vector3(width * 0.48f, 0.077f, 0.024f));
                    for (int i = 1; i <= 4; i++)
                        Detail(iron, width * i * 0.10f, y, side * 0.104f, new Vector3(0.031f, 0.031f, 0.02f));
                    for (int curl = -1; curl <= 1; curl += 2)
                    {
                        Vector3 last = P(width * 0.33f, y, side * 0.092f);
                        for (int step = 1; step <= 14; step++)
                        {
                            float t = step / 14f;
                            float angle = t * 5.3f;
                            float r = Mathf.Lerp(0.125f, 0.026f, t);
                            Vector3 point = P(width * 0.33f + 0.125f - Mathf.Cos(angle) * r,
                                              y + curl * Mathf.Sin(angle) * r, side * 0.092f);
                            mesh.Beam(iron, last, point, 0.020f, 0.019f, normal);
                            last = point;
                        }
                    }
                }
                Detail(iron, width - 0.16f, 1.08f, side * 0.075f, new Vector3(0.12f, 0.23f, 0.035f));
                Detail(iron, width - 0.16f, 1.10f, side * 0.115f, new Vector3(0.18f, 0.042f, 0.074f));
            }

            // Lanterna incernierata sulla faccia che guarda il nartece. La fiamma ha una luce propria a corto raggio.
            float lx = width * 0.70f, ly = 1.78f, lz = -0.216f;
            Detail(iron, lx, ly + 0.185f, -0.150f, new Vector3(0.028f, 0.038f, 0.17f));
            Detail(iron, lx, ly - 0.115f, lz, new Vector3(0.175f, 0.038f, 0.147f));
            Detail(iron, lx, ly + 0.126f, lz, new Vector3(0.17f, 0.045f, 0.148f));
            Detail(iron, lx, ly + 0.162f, lz, new Vector3(0.108f, 0.035f, 0.091f));
            foreach (float dx in new[] { -0.068f, 0.068f })
                foreach (float dz in new[] { -0.055f, 0.055f })
                    Detail(iron, lx + dx, ly + 0.01f, lz + dz, new Vector3(0.012f, 0.229f, 0.012f));
            Detail(Mat("cera"), lx, ly - 0.052f, lz, new Vector3(0.036f, 0.087f, 0.036f));
            Detail(Mat("fiamma"), lx, ly + 0.016f, lz, new Vector3(0.019f, 0.044f, 0.019f));

            var lamp = new GameObject("Lume del portale");
            lamp.transform.SetParent(roomRoot, false);
            lamp.transform.position = P(lx, ly + 0.042f, lz - 0.18f);
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.69f, 0.37f);
            light.range = 4.2f;
            light.intensity = 0.90f * LightPool.Dim;
            floor.lights.Add(light);
            LightPool.Register(light, false);
            var pulse = lamp.AddComponent<LightPulse>();
            pulse.l = light; pulse.baseI = light.intensity; pulse.amp = 0.12f; pulse.speed = 6.1f;
        }

        /// <summary>Normale verso l'interno dell'apertura (in basso e verso l'asse): ogni meta' dell'arco acuto ha il centro
        /// sull'altro lato dell'asse, a distanza d, e la superficie guarda il suo centro.</summary>
        static Vector3 ArchNormal(Opening o, float dx, float hw, float rise, Vector3 e)
        {
            float d = (rise * rise - hw * hw) / (2f * hw);
            float R = hw + d;
            float xx = Mathf.Abs(dx) + d;
            float yy = Mathf.Sqrt(Mathf.Max(0.0001f, R * R - xx * xx));
            float sgn = dx >= 0f ? 1f : -1f;
            return (e * (-sgn * xx / R) + Vector3.up * (-yy / R)).normalized;
        }

        static void Glass(MeshBuilder mb, Opening o, Room A, Func<float, float, float, Vector3> P, Vector3 nOut)
        {
            var mat = o.style == OpenStyle.Lancetta
                ? Mat(A.kind == RoomKind.Nartece ? "vetrata_chiara" : "vetrata" + (o.variant & 3))
                : Mat(o.style == OpenStyle.Feritoia ? "vetro_buio" : "vetro_albergo");
            Vector3 facing = -nOut;
            float sLo = M(o.Lo), sHi = M(o.Hi), hw = (sHi - sLo) * 0.5f, c = (sLo + sHi) * 0.5f;
            float w = sHi - sLo, hgt = o.top - o.sill;
            if (!o.Pointed)
            {
                mb.Quad(mat, P(sLo, o.sill, 0f), P(sHi, o.sill, 0f), P(sHi, o.top, 0f), P(sLo, o.top, 0f),
                        new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), facing);
                return;
            }
            int n = Mathf.Max(8, Mathf.CeilToInt(2f * hw / 0.1f));
            for (int i = 0; i < n; i++)
            {
                float s0 = sLo + w * i / n, s1 = sLo + w * (i + 1) / n;
                float y0 = o.Head(s0 - c, hw), y1 = o.Head(s1 - c, hw);
                mb.Quad(mat, P(s0, o.sill, 0f), P(s1, o.sill, 0f), P(s1, y1, 0f), P(s0, y0, 0f),
                        new Vector2((s0 - sLo) / w, 0), new Vector2((s1 - sLo) / w, 0), new Vector2((s1 - sLo) / w, (y1 - o.sill) / hgt), new Vector2((s0 - sLo) / w, (y0 - o.sill) / hgt), facing);
            }
        }
    }
}

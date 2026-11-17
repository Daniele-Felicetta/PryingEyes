using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc.MapArt;

namespace PryingEyes.Proc
{
    /// <summary>La mappa di un piano: cosa si sa (stanze visitate, armi e candele viste, santuari, uscita, sangue) e come lo dipinge il pittore
    /// (Proc/Map). Una sola per piano e per tutti: nello schermo condiviso e in cooperativa quello che si scopre e' di tutti, mentre ogni
    /// giocatore la guarda dal suo HUD (MapView). Il gioco vero la riempie qui; planlab ne riempie una finta (MapLab) per le anteprime.
    /// Refresh() ricostruisce la scena quando e' cambiato qualcosa e ridipinge solo le parti che ne dipendono: ogni stanza, le porte
    /// sigillate, i punti d'interesse e il sangue hanno una versione che la vista confronta per sapere cosa rifare.</summary>
    public sealed class FloorMap
    {
        public static FloorMap I { get; private set; }

        public readonly FloorInstance floor;
        public readonly Plan plan;

        // ---- quello che si e' scoperto
        public readonly HashSet<int> explored = new HashSet<int>();
        public readonly HashSet<WallBuy> foundWeapons = new HashSet<WallBuy>();
        public readonly HashSet<Candelabro> foundCandles = new HashSet<Candelabro>();
        public readonly HashSet<int> foundShrines = new HashSet<int>();      // 0 Biblioteca, 1 Scrigno, 2 altare
        public bool foundExit, foundPanel;

        // ---- quello che si disegna
        public MapScene scene { get; private set; }
        public MapPainter painter { get; private set; }
        public Art[] rooms { get; private set; }
        public Art doors { get; private set; }
        public Art pois { get; private set; }
        public Art blood { get; private set; }
        /// <summary>Cresce a ogni ridipinta: chi guarda lo confronta con l'ultima che ha visto.</summary>
        public int version { get; private set; }
        public readonly int[] roomVersion;
        public int doorsVersion { get; private set; }
        public int poisVersion { get; private set; }
        public int bloodVersion { get; private set; }
        /// <summary>Quando (Time.unscaledTime) ogni stanza e' comparsa o e' diventata visitata: la vista la fa affiorare piano piano.</summary>
        public readonly float[] shownAt;

        readonly List<MapProp> props = new List<MapProp>();
        readonly List<MapGallery> galleries = new List<MapGallery>();
        readonly List<MapBlood> bloodList = new List<MapBlood>();
        readonly List<int>[] neighbours;
        readonly int[] roomSig;
        readonly RoomSeen[] lastSeen;
        int doorsSig, poisSig, bloodSig;
        int inputHash = int.MinValue;
        public bool exitOpen;

        // ------------------------------------------------------------------ vita
        public static FloorMap Create(FloorInstance fi)
        {
            var m = new FloorMap(fi);
            I = m;
            m.Refresh(true);
            return m;
        }

        public static void Clear() { I = null; }

        FloorMap(FloorInstance fi)
        {
            floor = fi; plan = fi.plan;
            int n = plan.rooms.Count;
            rooms = new Art[n]; roomVersion = new int[n]; roomSig = new int[n]; shownAt = new float[n]; lastSeen = new RoomSeen[n];
            neighbours = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                rooms[i] = new Art(); shownAt[i] = -100f; roomSig[i] = int.MinValue;
                neighbours[i] = new List<int>();
            }
            foreach (var r in plan.rooms)
                foreach (var side in new[] { Side.S, Side.N, Side.W, Side.E })
                    foreach (var seg in plan.Segments(r, side))
                        if (seg.other >= 0 && !neighbours[r.id].Contains(seg.other)) neighbours[r.id].Add(seg.other);
            CollectProps();
            CollectGalleries();
        }

        // ------------------------------------------------------------------ cosa si e' scoperto
        /// <summary>Segna una stanza come visitata. Chi sta nella cabina dell'ascensore vede anche la stanza di fronte.</summary>
        public bool Explore(int roomId)
        {
            if (roomId < 0 || roomId >= plan.rooms.Count) return false;
            bool isNew = explored.Add(roomId);
            if (isNew && plan.rooms[roomId].IsLift)
                foreach (var o in plan.openings)
                {
                    if (o.style != OpenStyle.Ascensore) continue;
                    if (o.a == roomId && o.b >= 0) explored.Add(o.b);
                    else if (o.b == roomId) explored.Add(o.a);
                }
            return isNew;
        }

        public RoomSeen StateOf(int roomId) => scene != null && roomId >= 0 && roomId < scene.seen.Length ? scene.seen[roomId] : RoomSeen.Hidden;

        /// <summary>Quante stanze si mostrano come un punto interrogativo.</summary>
        public int RumorCount { get { int n = 0; if (scene != null) foreach (var s in scene.seen) if (s == RoomSeen.Rumor) n++; return n; } }
        public int SeenCount { get { int n = 0; if (scene != null) foreach (var s in scene.seen) if (s == RoomSeen.Seen) n++; return n; } }
        /// <summary>Le stanze vere del piano (senza le cabine degli ascensori).</summary>
        public int RoomTotal { get { int n = 0; foreach (var r in plan.rooms) if (!r.IsLift) n++; return n; } }

        /// <summary>Un nemico e' morto qui: sul pavimento resta una macchia (si vede sulla mappa, nelle stanze visitate).</summary>
        public static void NoteKill(Vector3 pos, float size = -1f) { I?.AddBlood(pos, size); }

        void AddBlood(Vector3 pos, float size)
        {
            var room = floor.RoomAt(pos);
            if (room == null || room.IsLift) return;
            if (size < 0f) size = 0.25f + 0.6f * Random.value;
            bloodList.Add(new MapBlood { x = pos.x, z = pos.z, size = Mathf.Clamp01(size), room = room.id });
            if (bloodList.Count > 90) bloodList.RemoveAt(0);
        }

        // ------------------------------------------------------------------ cosa c'e' sul piano (una volta sola)
        static HashSet<string> propNames;

        void CollectProps()
        {
            if (propNames == null) propNames = new HashSet<string>(PropDefs.Names);
            var done = new HashSet<Transform>();
            foreach (var root in floor.propRoots)
            {
                if (root == null) continue;
                TakeProp(root, done);
                for (int i = 0; i < root.childCount; i++) TakeProp(root.GetChild(i), done);
            }
        }

        void TakeProp(Transform t, HashSet<Transform> done)
        {
            string name = t.name;
            bool column = name == "Colonna";
            if (!column && !propNames.Contains(name)) return;
            if (!done.Add(t)) return;
            float w, d;
            PropClass cls;
            if (column)
            {
                var cc = t.GetComponent<CapsuleCollider>();
                w = d = (cc != null ? cc.radius : 0.4f) * 2f;
                cls = PropClass.Column;
            }
            else
            {
                var def = PropDefs.Get(name);
                if (!def.solid) return;
                w = def.size.x * Mathf.Abs(t.localScale.x); d = def.size.z;
                cls = ClassOf(name);
                if (cls == (PropClass)255) return;
            }
            var room = floor.RoomAt(t.position);
            if (room == null || room.IsLift) return;
            // l'arredo ha il davanti verso +Z locale e la larghezza lungo X; sulla mappa (x, z) una rotazione di yaw gradi attorno a Y
            // porta la larghezza lungo (cos, -sin): l'angolo dall'asse x e' l'opposto
            props.Add(new MapProp { x = t.position.x, z = t.position.z, w = w, d = d, yaw = -t.eulerAngles.y * Mathf.Deg2Rad, cls = cls, room = room.id });
        }

        /// <summary>Che segno fare per un arredo; 255 = non si disegna (cartelli, lumi, cose minuscole).</summary>
        static PropClass ClassOf(string name)
        {
            switch (name)
            {
                case "arr_chiavi": case "chi_pala": case "chi_ceri": case "san_flebo": case "arr_lampada": return (PropClass)255;
                case "arr_letto": case "san_letto": return PropClass.Bed;
                case "arr_tavolo": case "arr_tavolino": case "arr_scrivania": case "arr_tavolo_acciaio": case "tea_toeletta": case "gu_tavolo_sarto": return PropClass.Table;
                case "arr_poltrona": case "arr_sedia": case "arr_comodino": case "san_sedia_ruote": case "chi_inginocchiatoio": return PropClass.Chair;
                case "chi_panca": case "chi_stallo": case "tea_fila": case "tea_fila_b": case "tea_fila_c": return PropClass.Pew;
                case "arr_scaffale": case "san_paravento": case "gu_stoffe": case "gu_specchio": return PropClass.Shelf;
                case "chi_sarcofago": case "gu_bara": case "gu_bara_chiusa": return PropClass.Sarcophagus;
                case "chi_altare": return PropClass.Altar;
                case "chi_statua": case "chi_candelabro": case "chi_pulpito": case "chi_fonte": case "arr_palma": case "tea_manichino":
                case "gu_forbici": case "gu_attaccapanni": case "gu_gargoyle": case "gu_urna": return PropClass.Statue;
                case "arr_armadio": case "arr_orologio": case "chi_confessionale": case "gu_armadio": case "gu_armadio_aperto": return PropClass.Cabinet;
                case "arr_pianoforte": return PropClass.Piano;
                case "arr_fornello": case "chi_organo": case "tea_faro": case "gu_macchina_cucire": case "gu_braciere": case "gu_incudine": case "gu_crogiolo": return PropClass.Machine;
                case "arr_vasca": return PropClass.Tub;
                case "gu_albero_secco": case "gu_lampione": return PropClass.Column;
                default: return PropClass.Generic;
            }
        }

        void CollectGalleries()
        {
            foreach (var g in floor.galleries)
            {
                if (g == null || g.room == null || g.points == null || g.points.Length < 2) continue;
                var pts = new V2[g.points.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = new V2(g.points[i].x, g.points[i].z);
                galleries.Add(new MapGallery { room = g.room.id, points = pts, height = FloorBuilder.GalleryHeight });
            }
        }

        // ------------------------------------------------------------------ la scena
        int InputHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + explored.Count; h = h * 31 + foundWeapons.Count; h = h * 31 + foundCandles.Count;
                foreach (var c in foundCandles) if (c != null && c.lit) h = h * 31 + 7;
                h = h * 31 + foundShrines.Count; h = h * 31 + bloodList.Count;
                h = h * 31 + (foundExit ? 1 : 0) + (foundPanel ? 2 : 0) + (Power.On ? 4 : 0) + (exitOpen ? 8 : 0);
                var acc = RoomAccessDoors.I != null ? RoomAccessDoors.I.Access : null;
                if (acc != null) h = h * 31 + acc.OpenRooms.Count;
                return h;
            }
        }

        /// <summary>Rifa la scena e ridipinge cio' che e' cambiato. Falso se non c'era niente di nuovo.</summary>
        public bool Refresh(bool force = false)
        {
            int ih = InputHash();
            if (!force && ih == inputHash) return false;
            inputHash = ih;
            var s = new MapScene(plan) { seed = plan.seed };
            ComputeSeen(s);
            ComputeDoors(s);
            s.powerOn = Power.On;
            s.props.AddRange(props);
            s.galleries.AddRange(galleries);
            BuildPois(s);
            foreach (var b in bloodList) if (s.IsSeen(b.room)) s.blood.Add(b);
            scene = s;
            painter = new MapPainter(s);
            bool any = false;
            for (int i = 0; i < plan.rooms.Count; i++)
            {
                int sig = RoomSignature(i, s);
                if (sig == roomSig[i]) continue;
                roomSig[i] = sig;
                rooms[i] = painter.Room(i);
                roomVersion[i]++;
                if (s.seen[i] != RoomSeen.Hidden && s.seen[i] != lastSeen[i]) shownAt[i] = Time.unscaledTime;
                lastSeen[i] = s.seen[i];
                any = true;
            }
            int ds = DoorsSignature(s);
            if (doors == null || ds != doorsSig) { doors = painter.Doors(); doorsSig = ds; doorsVersion++; any = true; }
            int ps = PoisSignature(s);
            if (pois == null || ps != poisSig) { pois = painter.Pois(); poisSig = ps; poisVersion++; any = true; }
            int bs = s.blood.Count * 7 + bloodList.Count;
            if (blood == null || bs != bloodSig) { blood = painter.Blood(); bloodSig = bs; bloodVersion++; any = true; }
            if (any) version++;
            return any;
        }

        void ComputeSeen(MapScene s)
        {
            var known = new HashSet<int>();
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || o.style == OpenStyle.Passaggio) continue;
                if (explored.Contains(o.a)) known.Add(o.b);
                if (explored.Contains(o.b)) known.Add(o.a);
            }
            foreach (var r in plan.rooms)
            {
                if (r.IsLift) continue;                  // la cabina e' un segno sulla mappa, non una stanza
                if (explored.Contains(r.id)) s.seen[r.id] = RoomSeen.Seen;
                else if (known.Contains(r.id) && r.type != RoomType.Segreta) s.seen[r.id] = RoomSeen.Rumor;     // la segreta compare solo dopo
            }
        }

        void ComputeDoors(MapScene s)
        {
            var acc = RoomAccessDoors.I != null ? RoomAccessDoors.I.Access : null;
            s.paidDoors = SurvivalMode.Enabled && acc != null;
            if (!s.paidDoors) return;
            for (int i = 0; i < plan.rooms.Count; i++) { s.open[i] = acc.IsOpen(i); s.price[i] = acc.Price(i); }
            for (int i = 0; i < plan.openings.Count; i++)
            {
                var o = plan.openings[i];
                if (o.b < 0 || o.oneSided) { s.door[i] = DoorState.Open; continue; }
                bool oa = acc.IsOpen(o.a), ob = acc.IsOpen(o.b);
                s.door[i] = oa && ob ? DoorState.Open : oa != ob ? DoorState.Sealed : DoorState.SealedFar;
                s.doorPrice[i] = oa && !ob ? acc.Price(o.b) : !oa && ob ? acc.Price(o.a) : 0;
            }
        }

        int RoomIdAt(Vector3 p) { var r = floor.RoomAt(p); return r != null ? r.id : -1; }

        void BuildPois(MapScene s)
        {
            foreach (var w in foundWeapons)
            {
                if (w == null) continue;
                var p = w.transform.position;
                s.pois.Add(new MapPoi { kind = PoiKind.Weapon, x = p.x, z = p.z, label = w.displayName, price = w.price, room = RoomIdAt(p) });
            }
            foreach (var c in foundCandles)
            {
                if (c == null) continue;
                var p = c.transform.position;
                s.pois.Add(new MapPoi { kind = PoiKind.Candela, x = p.x, z = p.z, state = (byte)(c.lit ? 1 : 0), label = c.lit ? "Candela accesa" : "Candela", room = RoomIdAt(p) });
            }
            foreach (int k in foundShrines)
            {
                var sp = ShrinePos(k);
                if (!sp.HasValue) continue;
                var p = sp.Value;
                s.pois.Add(new MapPoi
                {
                    kind = k == 0 ? PoiKind.Biblioteca : k == 1 ? PoiKind.Scrigno : PoiKind.Altare, x = p.x, z = p.z,
                    label = k == 0 ? "Biblioteca" : k == 1 ? "Scrigno" : "Altare", price = k == 1 ? Scrigno.Price : k == 2 ? Altare.Price : 0, room = RoomIdAt(p),
                });
            }
            if (foundPanel && Power.Panel != null)
            {
                var p = Power.Panel.transform.position;
                s.pois.Add(new MapPoi { kind = PoiKind.Quadro, x = p.x, z = p.z, state = (byte)(Power.On ? 1 : 0), label = "Quadro elettrico", room = Power.Panel.RoomId });
            }
            // gli ascensori: quello d'arrivo si conosce da subito, quello d'uscita solo dopo averlo visto
            if (plan.start >= 0 && plan.start < plan.rooms.Count && explored.Contains(plan.start))
            {
                var lift = plan.rooms[plan.start];
                s.pois.Add(new MapPoi { kind = PoiKind.ArrivalLift, x = lift.CenterX, z = lift.CenterZ, room = HostOf(lift) });
            }
            if (foundExit && plan.exit >= 0 && plan.exit < plan.rooms.Count)
            {
                var lift = plan.rooms[plan.exit];
                s.pois.Add(new MapPoi { kind = PoiKind.ExitLift, x = lift.CenterX, z = lift.CenterZ, state = (byte)(exitOpen ? 1 : 0), label = exitOpen ? "Uscita" : "", room = HostOf(lift) });
            }
        }

        /// <summary>La stanza a cui e' attaccata una cabina d'ascensore (la sua porta da' li').</summary>
        int HostOf(Room lift)
        {
            foreach (var o in plan.openings)
            {
                if (o.style != OpenStyle.Ascensore) continue;
                if (o.a == lift.id) return o.b;
                if (o.b == lift.id) return o.a;
            }
            return -1;
        }

        /// <summary>Dove sta il santuario k (0 Biblioteca, 1 Scrigno, 2 altare), se c'e' in questo piano.</summary>
        public static Vector3? ShrinePos(int k)
        {
            if (k == 0) return Biblioteca.Current != null && Biblioteca.Current.stands.Count > 0 && Biblioteca.Current.root != null ? Biblioteca.Current.root.transform.position : (Vector3?)null;
            if (k == 1) return Scrigno.Current != null ? Scrigno.Current.transform.position : (Vector3?)null;
            return Altare.Current != null ? Altare.Current.transform.position : (Vector3?)null;
        }

        // ------------------------------------------------------------------ firme: cosa ridipingere
        int RoomSignature(int id, MapScene s)
        {
            unchecked
            {
                int h = (int)s.seen[id] + 1;
                foreach (int n in neighbours[id]) h = h * 7 + (int)s.seen[n];
                for (int k = 0; k < plan.openings.Count; k++)
                {
                    var o = plan.openings[k];
                    if (o.a == id || o.b == id) h = h * 5 + (int)s.door[k];
                }
                return h * 31 + (s.paidDoors ? 1 : 0);
            }
        }

        int DoorsSignature(MapScene s)
        {
            unchecked
            {
                int h = s.paidDoors ? 3 : 1;
                for (int k = 0; k < plan.openings.Count; k++)
                {
                    var o = plan.openings[k];
                    h = h * 5 + (int)s.door[k] * 3 + (o.b >= 0 && (s.IsSeen(o.a) || s.IsSeen(o.b)) ? 1 : 0) + s.doorPrice[k];
                }
                return h;
            }
        }

        static int PoisSignature(MapScene s)
        {
            unchecked
            {
                int h = s.pois.Count + 1;
                foreach (var p in s.pois)
                    h = h * 31 + (int)p.kind * 7 + p.state + p.price + Mathf.RoundToInt(p.x * 4f) + Mathf.RoundToInt(p.z * 4f) * 3 + (p.label != null ? p.label.Length : 0);
                return h;
            }
        }
    }
}

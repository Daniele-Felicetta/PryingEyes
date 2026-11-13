using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Porte delle aree a pagamento: collider reali e maschera delle soglie, senza ricostruire la navmesh.</summary>
    public sealed class RoomAccessDoors : MonoBehaviour
    {
        public static RoomAccessDoors I;
        static readonly Dictionary<Opening, int> areas = new Dictionary<Opening, int>();
        readonly List<PaidRoomDoor> doors = new List<PaidRoomDoor>();
        readonly List<int> closedAreas = new List<int>();
        FloorInstance floor;
        public RoomAccessState Access { get; private set; }
        public IReadOnlyList<PaidRoomDoor> Doors => doors;
        public FloorInstance Floor => floor;
        /// <summary>Tutte le aree del piano sono aperte (ascensori compresi).</summary>
        public bool AllOpen => Access != null && Access.OpenRooms.Count >= floor.plan.rooms.Count;

        /// <summary>Quante aree della navmesh servono a questo piano (una per coppia di stanze collegate). Per le prove.</summary>
        public static int AreasNeeded(Plan plan)
        {
            var pairs = new HashSet<long>();
            foreach (var opening in plan.openings)
                if (RoundRegions.IsLink(opening) && opening.style != OpenStyle.Ascensore) pairs.Add(PairKey(opening));
            return pairs.Count;
        }

        static long PairKey(Opening o) => System.Math.Min(o.a, o.b) * 1000L + System.Math.Max(o.a, o.b);

        /// <summary>Prima di costruire la navmesh: una fascia marcata sotto ogni porta. Due porte fra le stesse stanze si aprono insieme
        /// (si compra la stanza, non la porta): dividono l'area, cosi' le 29 aree bastano a ogni piano.</summary>
        public static void PrepareNav(Plan plan)
        {
            areas.Clear();
            var pairArea = new Dictionary<long, int>();
            int next = HotelNav.FirstDoorArea;
            foreach (var opening in plan.openings)
            {
                if (!RoundRegions.IsLink(opening) || opening.style == OpenStyle.Ascensore) continue;
                long key = PairKey(opening);
                if (!pairArea.TryGetValue(key, out int area))
                {
                    if (next > HotelNav.LastDoorArea) Debug.LogWarning("[PryingEyes] troppe coppie di stanze per le aree della navmesh: l'ultima area e' condivisa");
                    area = System.Math.Min(next, HotelNav.LastDoorArea);
                    next++;
                    pairArea[key] = area;
                }
                areas.Add(opening, area);
                HotelNav.DoorBoxes.Add(RoundDirector.Band(opening, area));
            }
        }

        public static RoomAccessDoors Build(FloorInstance floor)
        {
            var controller = floor.root.AddComponent<RoomAccessDoors>(); I = controller;
            controller.floor = floor; controller.Access = new RoomAccessState(floor.plan);
            foreach (var item in areas)
            {
                var shutter = Shutter.Make(floor.root.transform, item.Key, 1);
                shutter.SnapDown();
                var door = shutter.gameObject.AddComponent<PaidRoomDoor>();
                door.owner = controller; door.opening = item.Key; door.shutter = shutter; door.area = item.Value;
                controller.doors.Add(door); Interact.Add(door);
            }
            controller.Update();
            return controller;
        }

        /// <summary>La stanza da comprare dietro la porta (-1 se le due parti sono aperte o chiuse insieme).</summary>
        public int Target(PaidRoomDoor door)
        {
            bool a = Access.IsOpen(door.opening.a), b = Access.IsOpen(door.opening.b);
            return a && !b ? door.opening.b : b && !a ? door.opening.a : -1;
        }

        /// <summary>La stanza gia' aperta di una porta da comprare, cioe' da dove si compra (-1 se non serve piu').</summary>
        public int OpenSide(PaidRoomDoor door)
        {
            bool a = Access.IsOpen(door.opening.a), b = Access.IsOpen(door.opening.b);
            return a && !b ? door.opening.a : b && !a ? door.opening.b : -1;
        }

        public bool TryBuy(ITarget buyer, int room, int points, out int charged)
        {
            charged = 0;
            if (buyer == null || !buyer.Alive) return false;
            var from = floor.RoomAt(buyer.Feet);
            if (from == null || !Access.CanBuy(from.id, room)) return false;
            bool local = ReferenceEquals(buyer, Targets.Local);
            foreach (var door in doors)
            {
                if (Target(door) != room) continue;
                Vector3 eye = buyer.LeanedPoint(Mathf.Min(1.6f, buyer.BodyHeight * 0.92f));
                Vector3 to = door.Point - eye;
                // Chi gioca qui deve vedere la porta e guardarla. Per un compagno l'ospite si fida della selezione fatta sul suo schermo
                // (la sua posizione e il suo sguardo arrivano in ritardo) e controlla solo la vicinanza.
                if (to.magnitude > door.Radius + (local ? 0f : 1.5f)) continue;
                if (local && (buyer.LookAt == null || Vector3.Dot(buyer.LookAt.forward, to.normalized) < 0.6f || !Interact.Visible(eye, door.Point, door.transform))) continue;
                if (!Access.TryBuy(from.id, room, points, out charged)) return false;
                Update(); FloorDirector.I?.RefreshObjectiveNow();
                return true;
            }
            return false;
        }

        public void ApplySnapshot(IEnumerable<int> rooms, bool instant)
        {
            Access.ApplySnapshot(rooms);
            foreach (var door in doors)
                if (Access.IsOpen(door.opening.a) && Access.IsOpen(door.opening.b) && instant)
                { door.shutter.SnapUp(); door.released = true; }
            Update();
        }

        void Update()
        {
            if (Access == null) return;
            closedAreas.Clear();
            foreach (var door in doors)
            {
                if (Access.IsOpen(door.opening.a) && Access.IsOpen(door.opening.b) && !door.released)
                { door.released = true; door.shutter.Release(); }
                if (!door.shutter.IsUp) closedAreas.Add(door.area);
            }
            HotelNav.SealAreas(closedAreas);
        }

        void OnDestroy() { if (I == this) { I = null; HotelNav.Unseal(); } }
    }

    public sealed class PaidRoomDoor : MonoBehaviour, IInteract
    {
        internal RoomAccessDoors owner;
        internal Opening opening;
        internal Shutter shutter;
        internal int area;
        internal bool released;
        /// <summary>Il punto che si guarda per comprare: a 1,3 m, ma sempre dentro l'apertura. Il passaggio basso (a carponi: architrave a 1,3 m)
        /// aveva il punto proprio sull'architrave e la linea di vista sbatteva nel muro, quindi la stanza Segreta non si poteva comprare.</summary>
        public Vector3 Point => shutter.center + Vector3.up * Mathf.Min(1.3f, opening.top - 0.35f);
        public float Radius => 3f;
        public int TargetRoom => owner.Target(this);
        public string Prompt => TargetRoom < 0 ? null : $"APRI LA PORTA — {owner.Access.Price(TargetRoom)} PUNTI";
        public Opening Opening => opening;
        public Shutter Shutter => shutter;

        /// <summary>Un punto a terra a 1,5 m dalla porta, dalla parte della stanza 'roomId': dove sta chi la compra. Lo usano il bot e le prove.</summary>
        public Vector3 Front(int roomId)
        {
            Vector3 axis = shutter.alongX ? Vector3.forward : Vector3.right;      // attraversa il muro
            Vector3 plus = shutter.center + axis * 1.5f;
            var room = owner.Floor.RoomAt(plus);
            return room != null && room.id == roomId ? plus : shutter.center - axis * 1.5f;
        }

        public void Use()
        {
            if (Coop.IsClient) { Coop.Game?.RequestRoom(TargetRoom); return; }
            var gm = GameManager.I;
            if (owner.TryBuy(Targets.Local, TargetRoom, gm.score, out int cost))
            { gm.AddPoints(-cost); Coop.Game?.RoomsChanged(); Sfx.UI(SoundBank.Doors, 0.6f); }
            else { gm.hud?.Toast("Punti insufficienti o porta non raggiungibile."); Sfx.UI(SoundBank.BookNo, 0.5f); }
        }

        void OnDestroy() => Interact.Remove(this);
    }
}

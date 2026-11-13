using System;
using System.Collections.Generic;

namespace PryingEyes.Proc
{
    /// <summary>Aree possedute e acquisti tra stanze adiacenti. Modello senza Unity, condiviso da porte, spawn e rete.</summary>
    public sealed class RoomAccessState
    {
        readonly Plan plan;
        readonly List<int>[] links;
        readonly int[] depth;
        readonly HashSet<int> open = new HashSet<int>();
        public int StartRoom { get; }
        public IReadOnlyCollection<int> OpenRooms => open;

        public RoomAccessState(Plan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            var starts = plan.OfType(RoomType.Partenza);
            if (starts.Count != 1) throw new ArgumentException("Serve una sola area iniziale");
            StartRoom = starts[0].id;
            links = new List<int>[plan.rooms.Count]; depth = new int[plan.rooms.Count];
            for (int i = 0; i < links.Length; i++) { links[i] = new List<int>(); depth[i] = -1; }
            foreach (var door in plan.openings)
            {
                if (!RoundRegions.IsLink(door)) continue;
                if (!links[door.a].Contains(door.b)) links[door.a].Add(door.b);
                if (!links[door.b].Contains(door.a)) links[door.b].Add(door.a);
            }
            var queue = new Queue<int>(); queue.Enqueue(StartRoom); depth[StartRoom] = 0;
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                foreach (int next in links[room]) if (depth[next] < 0) { depth[next] = depth[room] + 1; queue.Enqueue(next); }
            }
            foreach (var room in plan.rooms) if (depth[room.id] < 0) throw new ArgumentException("Area non collegata: " + room.id);
            Open(StartRoom);
        }

        public bool IsOpen(int room) => open.Contains(room);
        public int Price(int room) => room < 0 || room >= links.Length || plan.rooms[room].IsLift || IsOpen(room) ? 0 :
            500 + Math.Min(4, Math.Max(0, depth[room] - 1)) * 250;

        public bool CanBuy(int from, int room) => from >= 0 && from < links.Length && room >= 0 && room < links.Length &&
            IsOpen(from) && !IsOpen(room) && !plan.rooms[room].IsLift && links[from].Contains(room);

        public bool TryBuy(int from, int room, int points, out int charged)
        {
            charged = 0;
            if (!CanBuy(from, room)) return false;
            int cost = Price(room);
            if (points < cost) return false;
            Open(room); charged = cost;
            return true;
        }

        void Open(int room)
        {
            open.Add(room);
            foreach (int next in links[room]) if (plan.rooms[next].IsLift) open.Add(next);
        }

        public void ApplySnapshot(IEnumerable<int> rooms)
        {
            var validated = new List<int>(rooms);
            foreach (int room in validated) if (room < 0 || room >= links.Length) throw new ArgumentException("Area di rete non valida");
            open.Clear(); Open(StartRoom);
            foreach (int room in validated) Open(room);
        }
    }
}

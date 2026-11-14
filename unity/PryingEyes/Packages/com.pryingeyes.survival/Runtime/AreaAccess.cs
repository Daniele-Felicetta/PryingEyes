using System;
using System.Collections.Generic;

namespace Production.Survival
{
    /// <summary>Paid access over a declared graph. The caller owns currency and validates physical interaction.
    /// A successful purchase opens the destination exactly once and returns its charge; failures change nothing.</summary>
    public sealed class AreaAccess
    {
        public readonly struct Area
        {
            public readonly string Id;
            public readonly int Price;
            public Area(string id, int price) { Id = id; Price = price; }
        }
        public readonly struct Link
        {
            public readonly string A, B;
            public Link(string a, string b) { A = a; B = b; }
        }
        readonly Dictionary<string, int> prices = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, HashSet<string>> links = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        readonly HashSet<string> open = new HashSet<string>(StringComparer.Ordinal);

        public AreaAccess(IEnumerable<Area> areas, IEnumerable<Link> connections, IEnumerable<string> initiallyOpen)
        {
            if (areas == null || connections == null || initiallyOpen == null) throw new ArgumentNullException();
            foreach (var area in areas)
            {
                if (string.IsNullOrWhiteSpace(area.Id) || area.Price < 0 || prices.ContainsKey(area.Id)) throw new ArgumentException("ACCESS_AREA_INVALID");
                prices.Add(area.Id, area.Price); links.Add(area.Id, new HashSet<string>(StringComparer.Ordinal));
            }
            foreach (var edge in connections)
            {
                if (edge.A == null || edge.B == null || edge.A == edge.B || !links.ContainsKey(edge.A) || !links.ContainsKey(edge.B)) throw new ArgumentException("ACCESS_LINK_INVALID");
                links[edge.A].Add(edge.B); links[edge.B].Add(edge.A);
            }
            foreach (var id in initiallyOpen)
            {
                if (id == null || !prices.ContainsKey(id)) throw new ArgumentException("ACCESS_START_INVALID");
                open.Add(id);
            }
            if (open.Count == 0) throw new ArgumentException("ACCESS_START_MISSING");
            var reachable = new HashSet<string>(open); var pending = new Queue<string>(open);
            while (pending.Count > 0)
                foreach (var next in links[pending.Dequeue()]) if (reachable.Add(next)) pending.Enqueue(next);
            if (reachable.Count != prices.Count) throw new ArgumentException("ACCESS_AREA_UNREACHABLE");
        }

        public bool IsOpen(string id) => id != null && open.Contains(id);
        public int Price(string id) => id != null && prices.TryGetValue(id, out int price) ? (IsOpen(id) ? 0 : price) : throw new ArgumentException("ACCESS_AREA_UNKNOWN");
        public bool CanBuy(string from, string destination) => IsOpen(from) && destination != null && !IsOpen(destination) && links[from].Contains(destination);
        public bool TryBuy(string from, string destination, int balance, out int charged)
        {
            charged = 0;
            if (balance < 0 || !CanBuy(from, destination) || balance < prices[destination]) return false;
            charged = prices[destination]; open.Add(destination); return true;
        }
    }
}

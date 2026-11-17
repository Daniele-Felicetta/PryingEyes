using System;
using System.Collections.Generic;

namespace Production.Economy
{
    /// <summary>One-off constructions with a cost and prerequisites (a tech tree, a base to rebuild). The tree owns what is built; the
    /// caller owns the wallet, what each construction does and how it looks. Building pays the whole cost or nothing.</summary>
    public sealed class WorkTree
    {
        public readonly struct Work
        {
            public readonly string Id;
            public readonly Bundle Cost;
            public readonly string[] Requires;
            public Work(string id, Bundle cost, params string[] requires)
            {
                Id = id; Cost = cost; Requires = requires ?? new string[0];
            }
        }

        public enum Check { Ok, Unknown, AlreadyBuilt, Locked, Unaffordable }

        readonly List<Work> order = new List<Work>();
        readonly Dictionary<string, Work> works = new Dictionary<string, Work>(StringComparer.Ordinal);
        readonly HashSet<string> built = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Unique non-blank ids, a cost for each, prerequisites that exist and no cycle among them (ECONOMY_WORK_* otherwise).</summary>
        public WorkTree(IEnumerable<Work> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (var w in definitions)
            {
                if (string.IsNullOrWhiteSpace(w.Id) || w.Cost == null || works.ContainsKey(w.Id)) throw new ArgumentException("ECONOMY_WORK_INVALID");
                var copy = new Work(w.Id, w.Cost, (string[])w.Requires.Clone());
                works.Add(w.Id, copy); order.Add(copy);
            }
            foreach (var w in order)
                foreach (var r in w.Requires)
                    if (r == null || r == w.Id || !works.ContainsKey(r)) throw new ArgumentException("ECONOMY_WORK_REQUIREMENT_INVALID");
            // depth-first search for a cycle: 0 unseen, 1 on the stack, 2 done
            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var w in order) if (Cycle(w.Id, state)) throw new ArgumentException("ECONOMY_WORK_CYCLE");
        }

        bool Cycle(string id, Dictionary<string, int> state)
        {
            state.TryGetValue(id, out int s);
            if (s == 1) return true;
            if (s == 2) return false;
            state[id] = 1;
            foreach (var r in works[id].Requires) if (Cycle(r, state)) return true;
            state[id] = 2;
            return false;
        }

        public IReadOnlyList<Work> All => order;
        public int BuiltCount => built.Count;
        public bool Exists(string id) => id != null && works.ContainsKey(id);
        public Work Get(string id) => id != null && works.TryGetValue(id, out var w) ? w : throw new ArgumentException("ECONOMY_WORK_UNKNOWN");
        public bool IsBuilt(string id) => id != null && built.Contains(id);

        /// <summary>Every prerequisite is built (a construction can be offered).</summary>
        public bool Unlocked(string id)
        {
            foreach (var r in Get(id).Requires) if (!built.Contains(r)) return false;
            return true;
        }

        public Check CanBuild(string id, Ledger wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (!Exists(id)) return Check.Unknown;
            if (built.Contains(id)) return Check.AlreadyBuilt;
            if (!Unlocked(id)) return Check.Locked;
            return wallet.Has(works[id].Cost) ? Check.Ok : Check.Unaffordable;
        }

        /// <summary>Pays the cost and marks the work built. Any other answer than Ok changes nothing.</summary>
        public Check TryBuild(string id, Ledger wallet)
        {
            var check = CanBuild(id, wallet);
            if (check != Check.Ok) return check;
            if (!wallet.TryPay(works[id].Cost)) return Check.Unaffordable;
            built.Add(id);
            return Check.Ok;
        }

        /// <summary>Marks works built without paying (loading a save). Unknown ids are skipped and counted, so a save written by a newer
        /// catalog still loads; prerequisites are not re-checked, the save is the authority on what stands.</summary>
        public int Restore(IEnumerable<string> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            built.Clear();
            int skipped = 0;
            foreach (var id in ids) { if (Exists(id)) built.Add(id); else skipped++; }
            return skipped;
        }

        /// <summary>Built ids in declaration order (stable for saves and signatures).</summary>
        public List<string> BuiltIds()
        {
            var list = new List<string>();
            foreach (var w in order) if (built.Contains(w.Id)) list.Add(w.Id);
            return list;
        }
    }
}

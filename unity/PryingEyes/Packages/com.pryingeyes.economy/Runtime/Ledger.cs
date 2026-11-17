using System;
using System.Collections.Generic;

namespace Production.Economy
{
    /// <summary>Mutable balances of any number of resources, never negative. Payments are atomic: a cost the ledger cannot cover changes
    /// nothing. Deposits that would pass int.MaxValue throw before any balance moves. Single simulation thread.</summary>
    public sealed class Ledger
    {
        readonly Dictionary<string, int> balance = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Raised after every change with the resources that moved (positive ids only, the new balances are read with Get).</summary>
        public event Action<Bundle> Changed;

        public Ledger() { }
        public Ledger(Bundle initial) { Deposit(initial); }

        public int Get(string id) => id != null && balance.TryGetValue(id, out int n) ? n : 0;

        public bool Has(Bundle cost)
        {
            if (cost == null) throw new ArgumentNullException(nameof(cost));
            foreach (var c in cost) if (Get(c.Key) < c.Value) return false;
            return true;
        }

        /// <summary>What is still lacking to pay 'cost' (empty when it can be paid).</summary>
        public Bundle Missing(Bundle cost)
        {
            if (cost == null) throw new ArgumentNullException(nameof(cost));
            var lack = new List<KeyValuePair<string, int>>();
            foreach (var c in cost) { int gap = c.Value - Get(c.Key); if (gap > 0) lack.Add(new KeyValuePair<string, int>(c.Key, gap)); }
            return new Bundle(lack);
        }

        public void Deposit(Bundle goods)
        {
            if (goods == null) throw new ArgumentNullException(nameof(goods));
            if (goods.IsEmpty) return;
            foreach (var g in goods) checked { _ = Get(g.Key) + g.Value; }        // every sum first: an overflow leaves the ledger untouched
            foreach (var g in goods) balance[g.Key] = Get(g.Key) + g.Value;
            Changed?.Invoke(goods);
        }

        public void Deposit(string id, int amount) => Deposit(Bundle.One(id, amount));

        public bool TryPay(Bundle cost)
        {
            if (cost == null) throw new ArgumentNullException(nameof(cost));
            if (!Has(cost)) return false;
            if (cost.IsEmpty) return true;
            foreach (var c in cost)
            {
                int left = Get(c.Key) - c.Value;
                if (left == 0) balance.Remove(c.Key); else balance[c.Key] = left;
            }
            Changed?.Invoke(cost);
            return true;
        }

        public bool TryWithdraw(string id, int amount) => TryPay(Bundle.One(id, amount));

        /// <summary>Every nonzero balance.</summary>
        public Bundle Snapshot()
        {
            var all = new List<KeyValuePair<string, int>>(balance);
            return new Bundle(all);
        }

        /// <summary>Empties the ledger and returns what it held.</summary>
        public Bundle TakeAll()
        {
            var all = Snapshot();
            if (all.IsEmpty) return all;
            balance.Clear();
            Changed?.Invoke(all);
            return all;
        }

        /// <summary>Replaces every balance (loading a save). Same validation as a bundle.</summary>
        public void Restore(Bundle state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var before = Snapshot();
            balance.Clear();
            foreach (var s in state) balance[s.Key] = s.Value;
            Changed?.Invoke(before.Plus(state));
        }
    }
}

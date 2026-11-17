using System;
using System.Collections.Generic;

namespace Production.Economy
{
    /// <summary>Weighted drops. Each roll succeeds with 'chance'; a success picks one entry by weight and an amount between its Min and Max.
    /// The random source is the caller's (a function returning values in [0, 1)), so the same sequence gives the same loot: tests and
    /// networked peers can reproduce a drop. No clock, no global random.</summary>
    public sealed class LootTable
    {
        public readonly struct Entry
        {
            public readonly string Id;
            public readonly double Weight;
            public readonly int Min, Max;
            public Entry(string id, double weight, int min = 1, int max = 1) { Id = id; Weight = weight; Min = min; Max = max; }
        }

        readonly Entry[] entries;
        readonly double totalWeight;
        public readonly double Chance;
        public readonly int Rolls;

        /// <summary>'chance' in [0, 1], at least one roll, entries with finite positive weights and 1 &lt;= Min &lt;= Max; at least one entry
        /// unless the chance is zero (ECONOMY_LOOT_INVALID otherwise).</summary>
        public LootTable(double chance, int rolls, IEnumerable<Entry> table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (double.IsNaN(chance) || chance < 0 || chance > 1 || rolls < 1) throw new ArgumentException("ECONOMY_LOOT_INVALID");
            var list = new List<Entry>();
            foreach (var e in table)
            {
                if (string.IsNullOrWhiteSpace(e.Id) || double.IsNaN(e.Weight) || double.IsInfinity(e.Weight) || e.Weight <= 0 || e.Min < 1 || e.Max < e.Min)
                    throw new ArgumentException("ECONOMY_LOOT_INVALID");
                list.Add(e); totalWeight += e.Weight;
            }
            if (list.Count == 0 && chance > 0) throw new ArgumentException("ECONOMY_LOOT_INVALID");
            entries = list.ToArray(); Chance = chance; Rolls = rolls;
        }

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Rolls the table. 'chanceMul' scales the chance (clamped to 1) and 'amountMul' the amounts (rounded down, never below the
        /// entry's Min), both finite and at least zero.</summary>
        public Bundle Roll(Func<double> uniform, double chanceMul = 1, double amountMul = 1)
        {
            if (uniform == null) throw new ArgumentNullException(nameof(uniform));
            if (!Finite(chanceMul) || chanceMul < 0 || !Finite(amountMul) || amountMul < 0) throw new ArgumentException("ECONOMY_LOOT_FACTOR_INVALID");
            double p = Math.Min(1, Chance * chanceMul);
            if (p <= 0 || entries.Length == 0) return Bundle.Empty;
            var won = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < Rolls; i++)
            {
                if (Draw(uniform) >= p) continue;
                double pick = Draw(uniform) * totalWeight;
                var e = entries[entries.Length - 1];
                foreach (var candidate in entries) { pick -= candidate.Weight; if (pick < 0) { e = candidate; break; } }
                int amount = e.Min + (int)Math.Floor(Draw(uniform) * (e.Max - e.Min + 1));
                amount = Math.Min(e.Max, amount);
                double scaled = Math.Floor(amount * amountMul);
                amount = scaled >= int.MaxValue ? int.MaxValue : Math.Max(e.Min, (int)scaled);
                won.Add(new KeyValuePair<string, int>(e.Id, amount));
            }
            return new Bundle(won);
        }

        /// <summary>The mean amount of one resource per Roll call (chance, weight share and mean amount, before the multipliers).</summary>
        public double Expected(string id)
        {
            if (entries.Length == 0) return 0;
            double mean = 0;
            foreach (var e in entries) if (e.Id == id) mean += e.Weight / totalWeight * (e.Min + e.Max) * 0.5;
            return mean * Chance * Rolls;
        }

        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        static double Draw(Func<double> uniform)
        {
            double v = uniform();
            if (double.IsNaN(v) || v < 0 || v >= 1) throw new ArgumentException("ECONOMY_RANDOM_OUT_OF_RANGE");
            return v;
        }
    }
}

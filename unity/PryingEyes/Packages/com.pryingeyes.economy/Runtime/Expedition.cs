using System;
using System.Collections.Generic;

namespace Production.Economy
{
    /// <summary>The loot of one trip away from a safe place (a dungeon run, a raid): what is carried is at risk until the trip ends.
    /// Extraction moves everything carried into the bank; a failed trip keeps only what a policy allows and reports the rest as lost
    /// (for a corpse run, a penalty screen). Secured loot goes straight to the bank (a boss trophy, an alms box). One trip at a time.</summary>
    public sealed class Expedition
    {
        public Ledger Bank { get; }
        public Ledger Carried { get; } = new Ledger();
        public bool Active { get; private set; }
        /// <summary>Everything secured or extracted during the current (or last) trip.</summary>
        public Bundle Banked { get; private set; } = Bundle.Empty;

        public Expedition(Ledger bank) { Bank = bank ?? throw new ArgumentNullException(nameof(bank)); }

        /// <summary>Starts a trip with nothing carried (ECONOMY_EXPEDITION_ACTIVE if one is already running).</summary>
        public void Begin()
        {
            if (Active) throw new InvalidOperationException("ECONOMY_EXPEDITION_ACTIVE");
            Carried.TakeAll();
            Banked = Bundle.Empty;
            Active = true;
        }

        /// <summary>Loot picked up during the trip: at risk.</summary>
        public void Pick(Bundle loot)
        {
            if (loot == null) throw new ArgumentNullException(nameof(loot));
            if (!Active) throw new InvalidOperationException("ECONOMY_EXPEDITION_INACTIVE");
            Carried.Deposit(loot);
        }

        /// <summary>Loot that is safe at once, whatever happens next.</summary>
        public void Secure(Bundle loot)
        {
            if (loot == null) throw new ArgumentNullException(nameof(loot));
            if (!Active) throw new InvalidOperationException("ECONOMY_EXPEDITION_INACTIVE");
            Bank.Deposit(loot);
            Banked = Banked.Plus(loot);
        }

        /// <summary>Back safe: everything carried is banked. Returns what moved.</summary>
        public Bundle Extract()
        {
            if (!Active) throw new InvalidOperationException("ECONOMY_EXPEDITION_INACTIVE");
            var moved = Carried.TakeAll();
            Bank.Deposit(moved);
            Banked = Banked.Plus(moved);
            Active = false;
            return moved;
        }

        /// <summary>The trip failed: for each carried resource 'keep' gives the fraction (0..1) that is banked anyway, rounded down;
        /// the rest is returned in 'lost'. Returns what was kept.</summary>
        public Bundle Fail(Func<string, double> keep, out Bundle lost)
        {
            if (keep == null) throw new ArgumentNullException(nameof(keep));
            if (!Active) throw new InvalidOperationException("ECONOMY_EXPEDITION_INACTIVE");
            var carried = Carried.Snapshot();
            var kept = new List<KeyValuePair<string, int>>();
            foreach (var c in carried)
            {
                double f = keep(c.Key);
                if (double.IsNaN(f) || f < 0 || f > 1) throw new ArgumentException("ECONOMY_KEEP_FRACTION_INVALID");
                kept.Add(new KeyValuePair<string, int>(c.Key, (int)Math.Floor(c.Value * f)));
            }
            var keptBundle = new Bundle(kept);
            lost = carried.Minus(keptBundle);
            Carried.TakeAll();
            Bank.Deposit(keptBundle);
            Banked = Banked.Plus(keptBundle);
            Active = false;
            return keptBundle;
        }
    }
}

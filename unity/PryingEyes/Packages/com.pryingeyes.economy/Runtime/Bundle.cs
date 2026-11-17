using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Production.Economy
{
    /// <summary>An immutable multiset of resources: ordinal string ids with positive amounts, sorted by id. Duplicates are merged and
    /// zero amounts are dropped, so two bundles with the same contents compare equal whatever order they were written in.</summary>
    public sealed class Bundle : IEnumerable<KeyValuePair<string, int>>, IEquatable<Bundle>
    {
        public static readonly Bundle Empty = new Bundle(new SortedDictionary<string, int>(StringComparer.Ordinal));
        readonly SortedDictionary<string, int> items;

        Bundle(SortedDictionary<string, int> owned) { items = owned; }

        /// <summary>Builds a bundle. Rejects null or blank ids and negative amounts (ECONOMY_BUNDLE_INVALID) and sums above
        /// int.MaxValue (OverflowException) before anything is kept.</summary>
        public Bundle(IEnumerable<KeyValuePair<string, int>> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            items = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 0) throw new ArgumentException("ECONOMY_BUNDLE_INVALID");
                if (entry.Value == 0) continue;
                items.TryGetValue(entry.Key, out int had);
                items[entry.Key] = checked(had + entry.Value);
            }
        }

        public static Bundle Of(params (string id, int amount)[] entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var list = new List<KeyValuePair<string, int>>(entries.Length);
            foreach (var (id, amount) in entries) list.Add(new KeyValuePair<string, int>(id, amount));
            return new Bundle(list);
        }

        public static Bundle One(string id, int amount) => Of((id, amount));

        /// <summary>The amount of one resource (zero when absent).</summary>
        public int this[string id] => id != null && items.TryGetValue(id, out int n) ? n : 0;
        public int Count => items.Count;
        public bool IsEmpty => items.Count == 0;
        public IEnumerable<string> Ids => items.Keys;
        public long Total { get { long t = 0; foreach (var n in items.Values) t += n; return t; } }

        public Bundle Plus(Bundle other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var sum = new List<KeyValuePair<string, int>>(items);
            sum.AddRange(other.items);
            return new Bundle(sum);
        }

        /// <summary>Each amount multiplied by 'factor' (finite, at least zero) and rounded down: what a loss policy keeps.</summary>
        public Bundle Scaled(double factor)
        {
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 0) throw new ArgumentException("ECONOMY_FACTOR_INVALID");
            var scaled = new List<KeyValuePair<string, int>>(items.Count);
            foreach (var entry in items)
            {
                double v = Math.Floor(entry.Value * factor);
                if (v > int.MaxValue) throw new OverflowException("ECONOMY_FACTOR_OVERFLOW");
                scaled.Add(new KeyValuePair<string, int>(entry.Key, (int)v));
            }
            return new Bundle(scaled);
        }

        /// <summary>This bundle without what 'other' holds, never below zero per id.</summary>
        public Bundle Minus(Bundle other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var rest = new List<KeyValuePair<string, int>>(items.Count);
            foreach (var entry in items) rest.Add(new KeyValuePair<string, int>(entry.Key, Math.Max(0, entry.Value - other[entry.Key])));
            return new Bundle(rest);
        }

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(Bundle other)
        {
            if (other is null || other.items.Count != items.Count) return false;
            foreach (var entry in items) if (other[entry.Key] != entry.Value) return false;
            return true;
        }
        public override bool Equals(object obj) => obj is Bundle b && Equals(b);
        public override int GetHashCode()
        {
            unchecked { int h = 17; foreach (var e in items) h = h * 31 + StringComparer.Ordinal.GetHashCode(e.Key) * 7 + e.Value; return h; }
        }

        /// <summary>"id:amount" pairs in id order, comma separated ("" when empty).</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var e in items) { if (sb.Length > 0) sb.Append(", "); sb.Append(e.Key).Append(':').Append(e.Value); }
            return sb.ToString();
        }
    }
}

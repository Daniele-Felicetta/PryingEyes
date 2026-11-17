using System;
using System.Collections.Generic;

namespace Production.Economy
{
    /// <summary>Exchanges at fixed prices: pay a bundle, receive a bundle. Each offer has a stock that the caller refills with Restock
    /// (after a day, a trip, a round); -1 is unlimited. Selling is an offer whose price is goods and whose goods are coins. A purchase
    /// pays, delivers and counts the stock together, or does nothing.</summary>
    public sealed class Market
    {
        public readonly struct Offer
        {
            public readonly string Id;
            public readonly Bundle Price, Goods;
            public readonly int Stock;
            public Offer(string id, Bundle price, Bundle goods, int stock = -1) { Id = id; Price = price; Goods = goods; Stock = stock; }
        }

        public enum Check { Ok, Unknown, SoldOut, Unaffordable }

        readonly List<Offer> order = new List<Offer>();
        readonly Dictionary<string, Offer> offers = new Dictionary<string, Offer>(StringComparer.Ordinal);
        readonly Dictionary<string, int> left = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Unique non-blank ids, price and goods not both empty, stock -1 or more (ECONOMY_OFFER_INVALID otherwise).</summary>
        public Market(IEnumerable<Offer> catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            foreach (var o in catalog)
            {
                if (string.IsNullOrWhiteSpace(o.Id) || o.Price == null || o.Goods == null || (o.Price.IsEmpty && o.Goods.IsEmpty) ||
                    o.Stock < -1 || offers.ContainsKey(o.Id)) throw new ArgumentException("ECONOMY_OFFER_INVALID");
                offers.Add(o.Id, o); order.Add(o); left.Add(o.Id, o.Stock);
            }
        }

        public IReadOnlyList<Offer> All => order;
        public bool Exists(string id) => id != null && offers.ContainsKey(id);
        public Offer Get(string id) => id != null && offers.TryGetValue(id, out var o) ? o : throw new ArgumentException("ECONOMY_OFFER_UNKNOWN");
        /// <summary>Units still on sale until the next restock (-1 unlimited).</summary>
        public int Left(string id) => Exists(id) ? left[id] : throw new ArgumentException("ECONOMY_OFFER_UNKNOWN");

        public Check CanBuy(string id, Ledger wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (!Exists(id)) return Check.Unknown;
            if (left[id] == 0) return Check.SoldOut;
            return wallet.Has(offers[id].Price) ? Check.Ok : Check.Unaffordable;
        }

        /// <summary>Pays the price from 'wallet' and deposits the goods into 'into' (the same ledger when null).</summary>
        public Check TryBuy(string id, Ledger wallet, Ledger into = null)
        {
            var check = CanBuy(id, wallet);
            if (check != Check.Ok) return check;
            var target = into ?? wallet;
            var o = offers[id];
            foreach (var g in o.Goods) checked { _ = target.Get(g.Key) + g.Value; }        // an overflow must not take the payment
            if (!wallet.TryPay(o.Price)) return Check.Unaffordable;
            target.Deposit(o.Goods);
            if (left[id] > 0) left[id]--;
            return Check.Ok;
        }

        public void Restock() { foreach (var o in order) left[o.Id] = o.Stock; }

        /// <summary>Sets the remaining stock of one offer (loading a save): clamped to the declared stock, ignored for unlimited offers.</summary>
        public void RestoreLeft(string id, int units)
        {
            var o = Get(id);
            if (o.Stock < 0) return;
            left[id] = Math.Max(0, Math.Min(o.Stock, units));
        }
    }
}

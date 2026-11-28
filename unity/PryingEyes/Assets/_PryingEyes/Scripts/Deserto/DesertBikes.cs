using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le moto del deserto: quelle del garage di ogni posto (da soli e in cooperativa il proprio, nello schermo condiviso uno per ogni posto), una per piazzola, a destra della scalinata della
    /// cattedrale con il muso verso il deserto (DesertGarageSite e' il luogo, DesertGarage dice quali moto ha ognuno). La moto di casa e' sempre la prima; le altre sono quelle comprate a "Bozzo Moto Store"
    /// e nascono nella loro piazzola appena si comprano. Le crea DesertDirector.Create e vivono sotto di lui (spariscono quando il deserto si chiude). Le moto dei compagni in cooperativa non passano
    /// di qui: le fa RemotePlayer quando il compagno guida.</summary>
    public static class DesertBikes
    {
        static Transform host;
        // l'ultima moto guidata da ogni slot: e' quella che la minimappa segna e che "Own" restituisce se nessuno e' in sella
        static readonly Dictionary<int, DesertBike> lastRidden = new Dictionary<int, DesertBike>();

        /// <summary>La fila del garage di un posto: la sua nello schermo condiviso, la prima negli altri casi (ogni macchina in cooperativa ha solo le sue moto).</summary>
        static int RowOf(int slot) => Seats.Split ? Mathf.Clamp(slot, 0, Seats.MaxSeats - 1) : 0;

        /// <summary>Dove nasce la moto di casa di uno slot: la prima piazzola della sua fila.</summary>
        public static Vector3 HomeFor(int slot) => DesertGarage.BayPos(RowOf(slot), 0);

        /// <summary>Dove sta la piazzola di una moto nel garage di quel posto (null se non e' sua).</summary>
        public static Vector3? BayFor(int seat, DesertBikeDef d)
        {
            int bay = DesertGarage.BayOf(seat, d);
            return bay < 0 ? (Vector3?)null : DesertGarage.ParkingPos(RowOf(seat), bay, d);
        }

        public static void Create(Transform parent)
        {
            Clear();
            host = parent;
            lastRidden.Clear();
            DesertGarage.Reload();
            int n = Seats.Split ? Seats.All.Count : 1;
            if (DesertGarageSite.I != null) Object.Destroy(DesertGarageSite.I.gameObject);          // rifatto (le prove lo rifanno): il vecchio tetto non resta accanto al nuovo
            DesertGarageSite.Build(parent, n);
            for (int i = 0; i < n; i++)
            {
                int slot = Seats.Split ? i : Mathf.Clamp(Coop.Slot, 0, Seats.MaxSeats - 1);
                foreach (var d in DesertGarage.Owned(i)) Spawn(parent, i, slot, d, false);
            }
            DesertGarage.Bought -= OnBought;
            DesertGarage.Bought += OnBought;
        }

        /// <summary>Rifa il garage da quello che i personaggi hanno (per le prove: dopo aver svuotato o riempito un garage). Non fa niente se il deserto non e' aperto.</summary>
        public static void Rebuild()
        {
            if (host != null) Create(host);
        }

        static DesertBike Spawn(Transform parent, int seat, int slot, DesertBikeDef d, bool effect)
        {
            int bay = DesertGarage.BayOf(seat, d);
            if (bay < 0) return null;
            Vector3 at = DesertGarage.ParkingPos(RowOf(slot), bay, d);
            var b = DesertBike.Create(parent, at, DesertGarage.BayHeading, d.Starter ? "Moto " + (slot + 1) : d.name + " " + (slot + 1), d);
            b.slot = slot;
            if (effect) DesertGarageSite.Delivered(at);
            return b;
        }

        /// <summary>Una moto e' stata comprata: nasce nella sua piazzola del garage di chi l'ha comprata (se il deserto e' aperto).</summary>
        static void OnBought(int seat, DesertBikeDef d)
        {
            if (host == null || DesertDirector.I == null) return;
            int slot = Seats.Split ? seat : Mathf.Clamp(Coop.Slot, 0, Seats.MaxSeats - 1);
            // le moto di quel posto con una piazzola dopo la nuova scalano di una (la nuova puo' stare in mezzo: l'ordine e' quello del negozio)
            foreach (var b in new List<DesertBike>(DesertBike.All))
            {
                if (b == null || b.puppet || b.slot != slot || b.Def == d) continue;
                var at = BayFor(seat, b.Def);
                if (!at.HasValue || Flat(b.home, at.Value) < 0.05f) continue;
                bool parkedAtHome = b.rider == null && Flat(b.transform.position, b.home) < 1f;
                b.SetHome(at.Value, DesertGarage.BayHeading);
                if (parkedAtHome) b.Park(at.Value, DesertGarage.BayHeading);
            }
            Spawn(host, seat, slot, d, true);
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>Un giocatore e' rinato alla scalinata: le sue moto che nessuno guida e non sono gia' a casa tornano nella loro piazzola (lasciate lontano non gli servirebbero piu', e tornare a piedi a
        /// prenderle dopo una morte non e' quello che si voleva da una moto). Quelle degli altri posti restano dove sono.</summary>
        public static void ReturnHome(PlayerController p)
        {
            if (p == null || DesertBike.All.Count == 0) return;
            int slot = SlotOf(p);
            foreach (var b in DesertBike.All)
            {
                if (b == null || b.puppet || b.rider != null || b.slot != slot) continue;
                Vector3 d = b.transform.position - b.home; d.y = 0f;
                if (d.sqrMagnitude > 1f) b.Park(b.home, b.homeHeading);
            }
        }

        /// <summary>Lo slot di un giocatore: il suo posto nello schermo condiviso, lo slot della cooperativa, 0 da soli (-1 se e' un giocatore che non siede a nessun posto).</summary>
        public static int SlotOf(PlayerController p)
        {
            if (!Seats.Split) return Mathf.Clamp(Coop.Slot, 0, Seats.MaxSeats - 1);
            foreach (var s in Seats.All) if (s.player == p) return s.index;
            return -1;
        }

        /// <summary>Il posto (indice nel garage) di un giocatore: il suo nello schermo condiviso, 0 negli altri casi.</summary>
        public static int SeatOf(PlayerController p)
        {
            if (!Seats.Split) return 0;
            foreach (var s in Seats.All) if (s.player == p) return s.index;
            return 0;
        }

        /// <summary>Una moto e' stata presa in sella: e' l'ultima del suo slot.</summary>
        public static void NoteRide(DesertBike b)
        {
            if (b != null && !b.puppet) lastRidden[b.slot] = b;
        }

        /// <summary>La moto di un giocatore: quella in cui e' in sella, se no l'ultima che ha guidato, se no la moto di casa del suo garage (mai quella di un compagno in cooperativa, che e' un pupazzo).
        /// Null se non ne ha. Ci sia o no qualcuno in sella.</summary>
        public static DesertBike Own(PlayerController p)
        {
            int slot = SlotOf(p);
            if (slot < 0) return null;
            if (p != null && p.Bike != null && !p.Bike.puppet && p.Bike.slot == slot) return p.Bike;
            if (lastRidden.TryGetValue(slot, out var last) && last != null && !last.puppet) return last;
            DesertBike first = null;
            foreach (var b in DesertBike.All)
            {
                if (b == null || b.puppet || b.slot != slot) continue;
                if (b.Def.Starter) return b;
                if (first == null) first = b;
            }
            return first;
        }

        /// <summary>Le moto di un posto: tutte quelle del suo garage che ci sono nel mondo.</summary>
        public static List<DesertBike> Of(int slot)
        {
            var l = new List<DesertBike>();
            foreach (var b in DesertBike.All) if (b != null && !b.puppet && b.slot == slot) l.Add(b);
            return l;
        }

        /// <summary>Toglie le moto dei posti (non quelle dei compagni, che appartengono al loro pupazzo).</summary>
        public static void Clear()
        {
            for (int i = DesertBike.All.Count - 1; i >= 0; i--)
            {
                var b = DesertBike.All[i];
                if (b == null) { DesertBike.All.RemoveAt(i); continue; }
                if (!b.puppet) Object.Destroy(b.gameObject);
            }
            lastRidden.Clear();
        }

        /// <summary>La moto parcheggiata piu' vicina al punto, entro 'within' metri (null se non ce n'e').</summary>
        public static DesertBike Nearest(Vector3 p, float within = 1e9f)
        {
            DesertBike best = null;
            float bd = within;
            foreach (var b in DesertBike.All)
            {
                if (b == null || b.puppet || b.rider != null) continue;
                float d = Vector3.Distance(b.transform.position, p);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }
    }
}

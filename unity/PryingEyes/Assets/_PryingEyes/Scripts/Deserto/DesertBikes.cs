using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le moto del deserto: una per posto (da soli una, nello schermo condiviso una per ogni posto, in cooperativa quella del proprio slot), parcheggiate ai piedi della scalinata della
    /// cattedrale con il muso verso il deserto. Le crea DesertDirector.Create e vivono sotto di lui (spariscono quando il deserto si chiude). Le moto dei compagni in cooperativa non passano
    /// di qui: le fa RemotePlayer quando il compagno guida.</summary>
    public static class DesertBikes
    {
        /// <summary>Dove nasce la moto di uno slot: a destra della scalinata (che va da x 11 a 25), tre metri e mezzo oltre il suo piede (z -5,8), una accanto all'altra.</summary>
        public static Vector3 HomeFor(int slot)
        {
            float x = 21.5f + 1.8f * Mathf.Clamp(slot, 0, 3), z = -9.5f;
            return new Vector3(x, DesertWorld.Height(x, z), z);
        }

        public static void Create(Transform host)
        {
            Clear();
            int n = Seats.Split ? Seats.All.Count : 1;
            for (int i = 0; i < n; i++)
            {
                int slot = Seats.Split ? i : Mathf.Clamp(Coop.Slot, 0, Seats.MaxSeats - 1);
                DesertBike.Create(host, HomeFor(slot), Mathf.PI, "Moto " + (slot + 1)).slot = slot;
            }
        }

        /// <summary>Un giocatore e' rinato alla scalinata: la sua moto, se nessuno la guida e non e' gia' a casa, torna dov'era nata (lasciata lontano non gli servirebbe piu', e tornare a piedi a prenderla
        /// dopo una morte non e' quello che si voleva da una moto). Quelle degli altri posti restano dove sono.</summary>
        public static void ReturnHome(PlayerController p)
        {
            if (p == null || DesertBike.All.Count == 0) return;
            int slot = Mathf.Clamp(Coop.Slot, 0, Seats.MaxSeats - 1);
            if (Seats.Split)
            {
                slot = -1;
                foreach (var s in Seats.All) if (s.player == p) { slot = s.index; break; }
            }
            foreach (var b in DesertBike.All)
            {
                if (b == null || b.puppet || b.rider != null || b.slot != slot) continue;
                Vector3 d = b.transform.position - b.home; d.y = 0f;
                if (d.sqrMagnitude > 1f) b.Park(b.home, b.homeHeading);
            }
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

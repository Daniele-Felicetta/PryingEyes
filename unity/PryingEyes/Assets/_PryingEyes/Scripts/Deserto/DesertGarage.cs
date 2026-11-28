using System.Collections.Generic;
using PryingEyes.Characters;
using Production.Saves;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>I garage dei giocatori: quali moto ha ognuno. La moto di casa (<see cref="DesertBikeDefs.StarterId"/>) e' di tutti e non si salva; le altre si comprano a "Bozzo Moto Store" (il menu che
    /// si apre con Select) coi punti di chi compra, e da quel momento sono sue per sempre: restano nel personaggio (CharacterRecord.extras, la chiave <see cref="Key"/>, una lista di id) e a ogni partita nel
    /// deserto aspettano nel garage all'aperto accanto alla scalinata (DesertGarageSite, DesertBikes). Il garage e' per posto: nello schermo condiviso ogni posto ha il suo personaggio e le sue moto, e in
    /// cooperativa ognuno ha le sue sulla propria macchina. Niente di questo cambia il formato dei salvataggi: l'elenco sta in un campo nuovo dentro 'extras', che le versioni vecchie ignorano e tengono.</summary>
    public static class DesertGarage
    {
        /// <summary>La chiave dentro CharacterRecord.extras: un oggetto { "owned": ["argo1000", ...] }.</summary>
        public const string Key = "desertGarage";

        // ---- il luogo: il garage sta a destra della scalinata (che va da x 11 a 25), sulla sabbia piana del sagrato. Le piazzole sono in fila lungo x, una moto ogni 2 m, il muso verso sud (verso il
        // deserto: lo stesso verso in cui nascevano le moto); nello schermo condiviso ogni posto ha la sua fila, 3,4 m piu' a sud
        public const float X0 = 30f, Pitch = 2.0f, RowZ0 = -9.0f, RowPitch = 3.4f;
        public const int Bays = 10;
        public const float BayHeading = Mathf.PI;

        /// <summary>Dove sta la moto della piazzola 'bay' (0..Bays-1) della fila 'row' (il posto: 0 da soli e in cooperativa).</summary>
        public static Vector3 BayPos(int row, int bay)
        {
            float x = X0 + Pitch * Mathf.Clamp(bay, 0, Bays - 1), z = RowZ0 - RowPitch * Mathf.Clamp(row, 0, Seats.MaxSeats - 1);
            return new Vector3(x, DesertWorld.Height(x, z), z);
        }

        public static Vector3 ParkingPos(int row, int bay, DesertBikeDef vehicle)
        {
            if (!vehicle.IsPlane) return BayPos(row, bay);
            float x = 56f + 9f * Mathf.Clamp(row, 0, Seats.MaxSeats - 1), z = -10f;
            return new Vector3(x, DesertWorld.Height(x, z), z);
        }

        sealed class Book { public readonly List<string> owned = new List<string>(); }

        static readonly Dictionary<int, Book> books = new Dictionary<int, Book>();

        /// <summary>Qualcuno ha comprato una moto (posto, moto): il garage la mette nella sua piazzola, il menu la segna comprata.</summary>
        public static event System.Action<int, DesertBikeDef> Bought;

        static Seat SeatAt(int i) => i >= 0 && i < Seats.All.Count ? Seats.All[i] : null;

        /// <summary>Il personaggio di quel posto (il giocatore singolo e la cooperativa hanno il personaggio locale). Null nelle prove senza personaggi.</summary>
        static CharacterRecord RecordOf(int seat)
        {
            var s = SeatAt(seat);
            return s != null && s.character != null ? s.character : Roster.Local;
        }

        static Book BookOf(int seat)
        {
            if (books.TryGetValue(seat, out var b)) return b;
            b = new Book();
            var rec = RecordOf(seat);
            var o = rec != null && rec.extras != null ? rec.extras.Get(Key) : null;
            var list = o != null && o.IsObject ? o.Get("owned") : null;
            if (list != null && list.IsArray)
                foreach (var it in list.Items)
                {
                    string id = it.AsString();
                    var d = DesertBikeDefs.ById(id);
                    if (d != null && !d.Free && !b.owned.Contains(d.id)) b.owned.Add(d.id);          // un id che questa versione non conosce non compra niente (e si perde alla prossima scrittura)
                }
            books[seat] = b;
            return b;
        }

        /// <summary>Dimentica quello che ha letto (si rilegge dal personaggio alla prossima domanda): all'inizio di ogni partita nel deserto, perche' i posti possono avere altri personaggi.</summary>
        public static void Reload() => books.Clear();

        public static bool Owns(int seat, DesertBikeDef d) => d != null && (d.Free || BookOf(seat).owned.Contains(d.id));

        /// <summary>Le moto di quel posto, nell'ordine del negozio (la moto di casa per prima): e' anche l'ordine delle piazzole del garage.</summary>
        public static List<DesertBikeDef> Owned(int seat)
        {
            var l = new List<DesertBikeDef>();
            foreach (var d in DesertBikeDefs.All) if (Owns(seat, d)) l.Add(d);
            return l;
        }

        /// <summary>La piazzola di una moto nel garage di quel posto (0 e' la prima), -1 se non ce l'ha.</summary>
        public static int BayOf(int seat, DesertBikeDef d)
        {
            if (d != null && d.IsPlane) return Owns(seat, d) ? Bays : -1;
            int i = 0;
            foreach (var o in Owned(seat))
            {
                if (o.IsPlane) continue;
                if (o == d) return i;
                i++;
            }
            return -1;
        }

        public enum Result { Bought, AlreadyOwned, NotEnoughPoints, NoGame }

        /// <summary>Compra una moto coi punti di chi gira adesso (il chiamante e' dentro Seats.Enter del posto: i punti sono GameManager.I.score). 'missing' e' quanto manca se non bastano.</summary>
        public static Result TryBuy(int seat, DesertBikeDef d, out int missing)
        {
            missing = 0;
            var gm = GameManager.I;
            if (d == null || gm == null) return Result.NoGame;
            if (Owns(seat, d)) return Result.AlreadyOwned;
            if (gm.score < d.price) { missing = d.price - gm.score; return Result.NotEnoughPoints; }
            gm.AddPoints(-d.price);
            BookOf(seat).owned.Add(d.id);
            Save(seat);
            Bought?.Invoke(seat, d);
            return Result.Bought;
        }

        /// <summary>Per le prove e per chi regala una moto: mette la moto nel garage senza pagare.</summary>
        public static void Grant(int seat, DesertBikeDef d)
        {
            if (d == null || d.Free || Owns(seat, d)) return;
            BookOf(seat).owned.Add(d.id);
            Save(seat);
            Bought?.Invoke(seat, d);
        }

        /// <summary>Per le prove: svuota il garage di quel posto (resta la moto di casa).</summary>
        public static void Clear(int seat)
        {
            BookOf(seat).owned.Clear();
            Save(seat);
        }

        static void Save(int seat)
        {
            var rec = RecordOf(seat);
            if (rec == null) return;
            if (rec.extras == null || !rec.extras.IsObject) rec.extras = JsonItem.Object();
            var arr = JsonItem.Array();
            foreach (var id in BookOf(seat).owned) arr.Add(id);
            rec.extras.Set(Key, JsonItem.Object().Set("owned", arr));
            Roster.MarkDirty(rec);
        }
    }
}

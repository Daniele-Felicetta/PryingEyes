using UnityEngine;

namespace PryingEyes
{
    /// <summary>Medaglie (2026-11-15): imprese istantanee premiate con un riquadro dorato in alto, un rintocco e qualche punto, contate
    /// nel tabellone (Tab). Complementari alle Prove dell'ondata. Si contano sulle uccisioni accreditate a questo giocatore
    /// (GameManager.CreditKill: in cooperativa e nello split screen ognuno le sue):
    ///  - a catena (uccisioni a meno di 0,75 s l'una dall'altra): Doppietta +20, Tripletta +50, Quaterna +80, Strage (5 o piu') +120;
    ///  - Mira divina: cinque uccisioni di fila alla testa +100 (Occhio di Dio a dieci, +250);
    ///  - Intoccabile: venticinque uccisioni senza essere ferito +150;
    ///  - Ultimo respiro: un'uccisione con la vita sotto il 15% +50 (al piu' ogni 20 s);
    ///  - Fuoco purificatore: tre uccisioni o piu' nello stesso scoppio (stesso fotogramma, esplosione) +75;
    ///  - Lama santa: tre uccisioni di coltello in 5 s +60.</summary>
    public static class Medals
    {
        public const float ChainGap = 0.75f;

        /// <summary>Lo stato di UN giocatore: nello split screen ogni posto (Seat) ne ha uno e Seats.Enter mette al suo posto quello di chi gira.</summary>
        public sealed class State
        {
            public int Count, Points;
            public string Last;
            public readonly System.Collections.Generic.List<string> Awarded = new System.Collections.Generic.List<string>();
            public float lastKill = -99f, lastBreath = -99f, knifeStart = -99f;
            public int chain, headRow, cleanRow, blastFrame = -1, blastKills, knifeKills, chainShown;
        }
        static State st = new State();
        public static State Current => st;
        public static void Use(State s) { st = s ?? new State(); }

        public static int Count => st.Count;
        /// <summary>I punti pagati dalle medaglie dall'ultimo azzeramento (le prove li tolgono dal conto del punteggio).</summary>
        public static int Points => st.Points;
        public static string Last => st.Last;
        /// <summary>Le medaglie prese dall'ultimo azzeramento, in ordine (il tabellone e le prove le leggono).</summary>
        public static System.Collections.Generic.List<string> Awarded => st.Awarded;

        /// <summary>Partita nuova (o prova): azzera serie e conteggi (del giocatore che gira: Seats.ResetRun li azzera per tutti).</summary>
        public static void Reset() { Reset(st); }
        public static void Reset(State s)
        {
            s.Count = s.Points = 0; s.Last = null; s.Awarded.Clear();
            s.lastKill = s.lastBreath = s.knifeStart = -99f;
            s.chain = s.headRow = s.cleanRow = s.blastKills = s.knifeKills = s.chainShown = 0;
            s.blastFrame = -1;
        }

        public static void OnKill(DamageSource src, bool headshot)
        {
            var s = st;
            float now = Time.time;
            // a catena
            s.chain = now - s.lastKill <= ChainGap ? s.chain + 1 : 1;
            s.lastKill = now;
            if (s.chain >= 2 && s.chain > s.chainShown)
            {
                s.chainShown = s.chain;
                if (s.chain == 2) Award("Doppietta", 20);
                else if (s.chain == 3) Award("Tripletta", 50);
                else if (s.chain == 4) Award("Quaterna", 80);
                else if (s.chain == 5) Award("Strage", 120);
            }
            if (s.chain == 1) s.chainShown = 1;
            // alla testa, di fila
            s.headRow = headshot ? s.headRow + 1 : 0;
            if (s.headRow == 5) Award("Mira divina", 100);
            else if (s.headRow == 10) Award("Occhio di Dio", 250);
            // senza essere feriti
            s.cleanRow++;
            if (s.cleanRow == 25) Award("Intoccabile", 150);
            // all'ultimo respiro
            var p = GameManager.I != null ? GameManager.I.player : null;
            if (p != null && p.alive && p.hp > 0f && p.hp <= p.MaxHp * 0.15f && now - s.lastBreath > 20f) { s.lastBreath = now; Award("Ultimo respiro", 50); }
            // tre nello stesso scoppio
            if (src == DamageSource.Esplosione)
            {
                if (Time.frameCount != s.blastFrame) { s.blastFrame = Time.frameCount; s.blastKills = 0; }
                s.blastKills++;
                if (s.blastKills == 3) Award("Fuoco purificatore", 75);
            }
            // di coltello
            if (src == DamageSource.Knife || src == DamageSource.Takedown)
            {
                if (now - s.knifeStart > 5f) { s.knifeStart = now; s.knifeKills = 0; }
                s.knifeKills++;
                if (s.knifeKills == 3) Award("Lama santa", 60);
            }
        }

        public static void OnHurt() { st.cleanRow = 0; }

        static void Award(string name, int points)
        {
            var s = st;
            s.Count++;
            s.Last = name;
            s.Awarded.Add(name);
            var gm = GameManager.I;
            if (gm == null) return;
            s.Points += points;
            gm.AddPoints(points);
            gm.hud?.ShowMedal(name, points);
            Sfx.UI(SoundBank.Medal, 0.7f, 1f + Mathf.Min(0.25f, s.chain * 0.03f));
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] medaglia: {name} +{points}");
        }
    }
}

using UnityEngine;

namespace PryingEyes
{
    /// <summary>Medaglie (2026-11-15): imprese istantanee premiate con un riquadro dorato in alto, un rintocco e qualche punto, contate
    /// nel tabellone (Tab). Complementari alle Prove dell'ondata. Si contano sulle uccisioni accreditate a questo giocatore
    /// (GameManager.CreditKill: in cooperativa ognuno le sue):
    ///  - a catena (uccisioni a meno di 0,75 s l'una dall'altra): Doppietta +20, Tripletta +50, Quaterna +80, Strage (5 o piu') +120;
    ///  - Mira divina: cinque uccisioni di fila alla testa +100 (Occhio di Dio a dieci, +250);
    ///  - Intoccabile: venticinque uccisioni senza essere ferito +150;
    ///  - Ultimo respiro: un'uccisione con la vita sotto il 15% +50 (al piu' ogni 20 s);
    ///  - Fuoco purificatore: tre uccisioni o piu' nello stesso scoppio (stesso fotogramma, esplosione) +75;
    ///  - Lama santa: tre uccisioni di coltello in 5 s +60.</summary>
    public static class Medals
    {
        public const float ChainGap = 0.75f;
        public static int Count { get; private set; }
        public static string Last { get; private set; }
        /// <summary>Le medaglie prese dall'ultimo azzeramento, in ordine (il tabellone e le prove le leggono).</summary>
        public static readonly System.Collections.Generic.List<string> Awarded = new System.Collections.Generic.List<string>();
        static float lastKill = -99f, lastBreath = -99f;
        static int chain, headRow, cleanRow, blastFrame = -1, blastKills, knifeKills;
        static float knifeStart = -99f;
        static int chainShown;

        /// <summary>Partita nuova (o prova): azzera serie e conteggi.</summary>
        public static void Reset()
        {
            Count = 0; Last = null; Awarded.Clear();
            lastKill = lastBreath = knifeStart = -99f;
            chain = headRow = cleanRow = blastKills = knifeKills = chainShown = 0;
            blastFrame = -1;
        }

        public static void OnKill(DamageSource src, bool headshot)
        {
            float now = Time.time;
            // a catena
            chain = now - lastKill <= ChainGap ? chain + 1 : 1;
            lastKill = now;
            if (chain >= 2 && chain > chainShown)
            {
                chainShown = chain;
                if (chain == 2) Award("Doppietta", 20);
                else if (chain == 3) Award("Tripletta", 50);
                else if (chain == 4) Award("Quaterna", 80);
                else if (chain == 5) Award("Strage", 120);
            }
            if (chain == 1) chainShown = 1;
            // alla testa, di fila
            headRow = headshot ? headRow + 1 : 0;
            if (headRow == 5) Award("Mira divina", 100);
            else if (headRow == 10) Award("Occhio di Dio", 250);
            // senza essere feriti
            cleanRow++;
            if (cleanRow == 25) Award("Intoccabile", 150);
            // all'ultimo respiro
            var p = GameManager.I != null ? GameManager.I.player : null;
            if (p != null && p.alive && p.hp > 0f && p.hp <= p.MaxHp * 0.15f && now - lastBreath > 20f) { lastBreath = now; Award("Ultimo respiro", 50); }
            // tre nello stesso scoppio
            if (src == DamageSource.Esplosione)
            {
                if (Time.frameCount != blastFrame) { blastFrame = Time.frameCount; blastKills = 0; }
                blastKills++;
                if (blastKills == 3) Award("Fuoco purificatore", 75);
            }
            // di coltello
            if (src == DamageSource.Knife || src == DamageSource.Takedown)
            {
                if (now - knifeStart > 5f) { knifeStart = now; knifeKills = 0; }
                knifeKills++;
                if (knifeKills == 3) Award("Lama santa", 60);
            }
        }

        public static void OnHurt() { cleanRow = 0; }

        static void Award(string name, int points)
        {
            Count++;
            Last = name;
            Awarded.Add(name);
            var gm = GameManager.I;
            if (gm == null) return;
            gm.AddPoints(points);
            gm.hud?.ShowMedal(name, points);
            Sfx.UI(SoundBank.Medal, 0.7f, 1f + Mathf.Min(0.25f, chain * 0.03f));
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] medaglia: {name} +{points}");
        }
    }
}

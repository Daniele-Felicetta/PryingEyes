using System;
using UnityEngine;

namespace PryingEyes.Characters
{
    /// <summary>L'esperienza e i conti dei personaggi: chi gira (nello schermo condiviso il suo posto) guadagna esperienza per le uccisioni e
    /// per cio' che la Fabbrica racconta (un boss, un livello sceso, una risalita, una morte). I valori sono in <see cref="Leveling"/>.
    /// Chi sale di livello lo vede sul proprio HUD.</summary>
    public static class Progress
    {
        /// <summary>Un personaggio e' salito di livello (dopo che il livello e' cambiato).</summary>
        public static event Action<CharacterRecord> LevelledUp;

        /// <summary>Il personaggio di chi gira: quello del suo posto nello schermo condiviso, altrimenti quello locale.</summary>
        public static CharacterRecord Mine
        {
            get
            {
                var cur = Seats.Current;
                return cur != null && cur.character != null ? cur.character : Roster.Local;
            }
        }

        /// <summary>Un'uccisione a nome di chi gira (GameManager.CreditKill).</summary>
        public static void Kill(bool headshot, bool knife)
        {
            var c = Mine;
            if (c == null) return;
            c.stats.kills++;
            if (headshot) c.stats.headshots++;
            Award(c, Leveling.KillAward(headshot, knife));
        }

        /// <summary>Esperienza a un personaggio. Se sale di livello lo dice sull'HUD del suo posto.</summary>
        public static void Award(CharacterRecord c, long xp)
        {
            if (c == null || xp <= 0) return;
            int gained = c.AddXp(xp);
            Roster.MarkDirty(c);
            if (gained > 0) OnLevelUp(c);
        }

        static void OnLevelUp(CharacterRecord c)
        {
            Debug.Log($"[PryingEyes] personaggi: {c.name} e' salito al livello {c.Level} ({c.xp} xp)");
            LevelledUp?.Invoke(c);
            var hud = HudOf(c);
            if (hud == null) return;
            string bonus = Roster.IsTest ? "" : $"+{Leveling.StartPointsBonus(c.Level)} punti all'inizio di una discesa · +{Leveling.HpBonus(c.Level):0} di vita";
            hud.Banner($"{c.name}: livello {c.Level}", bonus);
        }

        static HUD HudOf(CharacterRecord c)
        {
            foreach (var s in Seats.All) if (s.character == c && s.hud != null) return s.hud;
            var gm = GameManager.I;
            return gm != null && c == Roster.Local ? gm.hud : null;
        }

        // ------------------------------------------------------------------ tempo di gioco
        static float carry;
        const float CommitEvery = 30f;      // il tempo di gioco si scrive ogni mezzo minuto (e quando il gioco si ferma): non vale una scrittura al secondo

        /// <summary>Il tempo di gioco di chi sta giocando (non in menu, non in pausa): ogni personaggio in campo, uno per posto.</summary>
        public static void Tick(float dt)
        {
            if (GameMenu.InMenu || GameMenu.Paused || GameManager.I == null) return;
            carry += dt;
            if (carry >= CommitEvery) CommitTime();
        }

        /// <summary>Mette sul conto dei personaggi in campo il tempo contato finora (si chiama prima di scrivere i file).</summary>
        public static void CommitTime()
        {
            long secs = (long)carry;
            if (secs <= 0) return;
            carry -= secs;
            if (Seats.Split) { foreach (var s in Seats.All) if (s.character != null) { s.character.stats.playSeconds += secs; Roster.MarkDirty(s.character); } }
            else { var c = Roster.Local; if (c != null) { c.stats.playSeconds += secs; Roster.MarkDirty(c); } }
        }

        /// <summary>Un conto per un'ora e minuti: "3 h 12 min", "47 min", "meno di un minuto".</summary>
        public static string Duration(long seconds)
        {
            long m = seconds / 60;
            if (m < 1) return "meno di un minuto";
            return m < 60 ? m + " min" : m / 60 + " h " + m % 60 + " min";
        }
    }
}

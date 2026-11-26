using System;

namespace PryingEyes.Characters
{
    /// <summary>I livelli di un personaggio: la curva, quanta esperienza vale ogni cosa e quanto danno i livelli. Tutto qui e' una tabella da
    /// ritoccare: il personaggio salva solo l'esperienza totale e il livello si ricava SEMPRE da quella, quindi cambiare la curva in una
    /// versione nuova sistema da solo anche i personaggi vecchi.</summary>
    public static class Leveling
    {
        public const int MaxLevel = 99;

        // ---- quanta esperienza vale cosa
        public const int KillXp = 5, HeadshotXp = 8, KnifeXp = 10;
        public const int BossXp = 150;               // il boss di un ramo
        public const int DescentXp = 40;             // ogni livello delle catacombe raggiunto per la prima volta nella spedizione
        public const int ReturnXp = 100;             // risalire vivi con lo zaino
        public const int DeathXp = 25;               // morire dopo essere sceso: la spedizione conta lo stesso un poco

        // ---- cosa danno i livelli (a partire dal secondo): piccolo di proposito, il livello e' soprattutto il tuo racconto
        public const int StartPointsPerLevel = 10, MaxStartPointsBonus = 250;       // punti di partenza di una spedizione
        public const float HpPerLevel = 1f, MaxHpBonus = 25f;                       // vita massima (come una vetrata)

        public static int KillAward(bool headshot, bool knife) => knife ? KnifeXp : headshot ? HeadshotXp : KillXp;

        /// <summary>L'esperienza totale che serve per ARRIVARE a quel livello (il primo costa zero).</summary>
        public static long XpToReach(int level)
        {
            if (level <= 1) return 0;
            if (level > MaxLevel) level = MaxLevel;
            return 50L * (level - 1) * level;        // 100, 300, 600, 1000, 1500 ... 4500 al decimo
        }

        public static int LevelFor(long xp)
        {
            if (xp <= 0) return 1;
            int level = (int)Math.Min(MaxLevel, Math.Floor((1.0 + Math.Sqrt(1.0 + xp / 12.5)) / 2.0));
            while (level < MaxLevel && XpToReach(level + 1) <= xp) level++;
            while (level > 1 && XpToReach(level) > xp) level--;
            return level;
        }

        /// <summary>Quanta esperienza serve ancora per il livello dopo (0 al massimo).</summary>
        public static long ToNext(long xp)
        {
            int level = LevelFor(xp);
            return level >= MaxLevel ? 0 : XpToReach(level + 1) - Math.Max(0, xp);
        }

        /// <summary>A che punto sei del livello che hai, da 0 a 1 (1 al massimo).</summary>
        public static float Progress(long xp)
        {
            int level = LevelFor(xp);
            if (level >= MaxLevel) return 1f;
            long from = XpToReach(level), span = XpToReach(level + 1) - from;
            return Math.Max(0f, Math.Min(1f, (float)((Math.Max(0, xp) - from) / (double)span)));
        }

        public static int StartPointsBonus(int level) => Math.Min(MaxStartPointsBonus, StartPointsPerLevel * Math.Max(0, level - 1));
        public static float HpBonus(int level) => Math.Min(MaxHpBonus, HpPerLevel * Math.Max(0, level - 1));
    }
}

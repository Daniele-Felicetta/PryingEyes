using System;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>The workshop of the career: a bike the player owns can be taken in three times, and every level makes it better in four ways at once: a stronger engine (+4 % torque, so +4 % power), grippier tyres (+2 % friction),
    /// a lighter bike (2 % of its own weight less, so a 125 does not lose a tenth of itself) and a slipperier fairing (3 % less drag). Tourist Trophy sells the parts one by one; here a level is a package, so that the screen of the
    /// garage stays one button. What a rider feels most is the grip: round a lap a full tune is worth about 3 % when the rider uses it (the power, the weight and the drag together are worth about 1 %), which is a third of the
    /// spread of the computer riders. Pure code and no random numbers: a level gives the same bike every time, and the bikes of the roster (Bikes.Get) are never touched: a tuned bike is a copy with its own torque curve and a key
    /// that says the level ("argo1000+2"), so that a record set with it is not mixed up with one set on the bike as it comes (and BikeCurves, which keeps what a bike can do by its key, measures each level once).</summary>
    public static class Tuning
    {
        /// <summary>The top level.</summary>
        public const int MaxLevel = 3;

        /// <summary>What a level adds, as a fraction of the torque and of the friction, and takes away, as a fraction of the drag and of the weight of the bike (the rider is not tuned).</summary>
        public const float TorqueGain = 0.04f, GripGain = 0.02f, DragCut = 0.03f, MassCut = 0.02f;

        /// <summary>The bike of the roster at a level of tuning (the bike itself at level 0, a copy above that).</summary>
        public static BikeSpec Apply(BikeSpec stock, int level)
        {
            level = RacingMath.Clamp(level, 0, MaxLevel);
            if (level == 0) return stock;
            var s = stock.Clone();
            var torque = new float[stock.torqueCurve.Length];
            float k = 1f + TorqueGain * level;
            for (int i = 0; i < torque.Length; i++) torque[i] = stock.torqueCurve[i] * k;
            s.torqueCurve = torque;
            float grip = 1f + GripGain * level;
            s.muFront = stock.muFront * grip;
            s.muRear = stock.muRear * grip;
            float drag = 1f - DragCut * level;
            s.cdA = stock.cdA * drag;
            s.cdATuck = stock.cdATuck * drag;
            s.bikeMass = stock.bikeMass * (1f - MassCut * level);
            s.id = stock.id + "+" + level;
            s.tune = level;
            return s;
        }

        /// <summary>The bike of the roster at a level (the same as Apply(Bikes.Get(index), level)).</summary>
        public static BikeSpec Of(int bikeIndex, int level) { return Apply(Bikes.Get(bikeIndex), level); }

        /// <summary>How a level is written on the screen: "livello 2" (nothing for the bike as it comes).</summary>
        public static string Label(int level) { return level <= 0 ? "" : "livello " + Math.Min(level, MaxLevel); }

        /// <summary>What a level adds up to, for the garage: "+8 % potenza   +4 % aderenza   -4 % peso   -6 % aria" (nothing for the bike as it comes).</summary>
        public static string Describe(int level)
        {
            level = RacingMath.Clamp(level, 0, MaxLevel);
            if (level == 0) return "";
            return "+" + Percent(TorqueGain, level) + " % potenza   +" + Percent(GripGain, level) + " % aderenza   -" + Percent(MassCut, level) + " % peso   -" + Percent(DragCut, level) + " % aria";
        }

        static int Percent(float perLevel, int level) { return (int)Math.Round(perLevel * level * 100f); }
    }
}

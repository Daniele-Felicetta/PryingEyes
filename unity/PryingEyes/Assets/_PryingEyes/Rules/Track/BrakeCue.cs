using System;

namespace PryingEyes.Pista
{
    /// <summary>What the braking sign on the dashboard says: nothing, the brakes are coming (a countdown in metres to the red of the line on the road) or the brakes are due now (the bike is on the red).</summary>
    public enum CueKind : byte { None = 0, Approach = 1, Brake = 2 }

    /// <summary>The state of the braking sign: what it says, the metres it shows, how full its bar is and the speed the braking is for.</summary>
    public readonly struct BrakeCue
    {
        public readonly CueKind kind;
        /// <summary>Approach: metres to where the red begins. Brake: metres of red left, to the bottom of the zone or to the far end of what is seen.</summary>
        public readonly float metres;
        /// <summary>The bar of the sign, 0 to 1: it fills as the red comes nearer (Approach) and empties as the red is used up (Brake).</summary>
        public readonly float fill;
        /// <summary>The speed (m/s) the braking is for, the lowest the plan reaches in the first dip of the zone; 0 when the plan only keeps falling and the speed of the corner is not known.</summary>
        public readonly float target;

        public BrakeCue(CueKind kind, float metres, float fill, float target)
        {
            this.kind = kind; this.metres = metres; this.fill = fill; this.target = target;
        }

        public static readonly BrakeCue None = new BrakeCue(CueKind.None, 0f, 0f, 0f);
    }

    /// <summary>The braking sign of the dashboard: the red of the driving line on the road said another way, for the rider who looks at the speedometer and not at the ribbon (from the far camera or the helmet the ribbon is a thin
    /// strip, and from behind the bike the rider hides it where it runs straight under him). It is made from the same advice as the ribbon (DrivingLine.FirstBraking on the stretch the rider sees), so it never says what the road does
    /// not: where the red is still ahead it counts the metres down from five seconds away (never under 60 m: a slow bike still needs the room), when the bike is on the red it says brake and shows how much red is left, and both
    /// say the speed the braking is for, the lowest the plan reaches in that zone, in steps of five km/h. Green and yellow have no sign: the sign is about the brakes. The aids level decides whether there is one, like the red of
    /// the ribbon (DrivingLine.Shows: Completi and Sportivi have it, Nessuno has none). Pure code and no random numbers.</summary>
    public static class BrakeCues
    {
        /// <summary>The countdown starts this many seconds before the red (at the speed of the bike), and never closer than this many metres.</summary>
        public const float LeadSeconds = 5f, MinLeadMetres = 60f;

        /// <summary>How far ahead (m) a red is counted down for a bike at this speed (m/s): five seconds of road, between 60 m and the 300 m of the ribbon.</summary>
        public static float ApproachWindow(float speed)
        {
            return PM.Clamp(speed * LeadSeconds, MinLeadMetres, DrivingLine.AheadMetres);
        }

        /// <summary>The sign for an aids level, the first red of the stretch the rider sees (DrivingLine.FirstBraking) and the speed of the bike (m/s).</summary>
        public static BrakeCue Make(int aids, DrivingLine.Braking b, float speed)
        {
            if (!DrivingLine.Shows(aids, Advice.Brake) || b.ahead < 0f || speed < 1f) return BrakeCue.None;
            float window = ApproachWindow(speed);
            float target = b.bottom ? b.lowest : 0f;
            if (b.ahead <= 0f) return new BrakeCue(CueKind.Brake, b.length, PM.Clamp01(b.length / window), target);
            if (b.ahead <= window) return new BrakeCue(CueKind.Approach, b.ahead, 1f - PM.Clamp01(b.ahead / window), target);
            return BrakeCue.None;
        }

        /// <summary>The distance as the sign writes it: to the nearest 10 m from 100 m, to the nearest 5 m under that and never under 5.</summary>
        public static int RoundMetres(float m)
        {
            if (m >= 100f) return (int)Math.Round(m / 10f, MidpointRounding.AwayFromZero) * 10;
            return Math.Max(5, (int)Math.Round(m / 5f, MidpointRounding.AwayFromZero) * 5);
        }

        /// <summary>A speed (m/s) in km/h to the nearest 5.</summary>
        public static int TargetKmh(float metresPerSecond)
        {
            return (int)Math.Round(metresPerSecond * 3.6f / 5f, MidpointRounding.AwayFromZero) * 5;
        }
    }
}

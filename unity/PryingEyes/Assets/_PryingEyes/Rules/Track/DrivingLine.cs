using System;
using System.Collections.Generic;

namespace PryingEyes.Pista
{
    /// <summary>What the driving line tells the rider at one place of the lap: Go (the plan is accelerating or already at the top speed of the bike: full throttle), Lift (the plan holds a speed the grip allows: ease off or
    /// stay neutral through the corner) and Brake (the plan is slowing the bike hard). It is the speed plan of the racing line the computer riders follow (RacingLine.vplan), read the way Gran Turismo reads it for a
    /// human: green, yellow and red on the road ahead.</summary>
    public enum Advice : byte { Go = 0, Lift = 1, Brake = 2 }

    /// <summary>The coloured driving line: from a racing line to one piece of advice per sample (Classify: what the pedals of a rider on the plan do there), the advice as it shows to a bike at its speed (Advise: the red
    /// comes where the bike is faster than the plan, so a bike that is slow, as at a standing start, is not told to brake for a corner it is a long way from, and the red shrinks as the bike brakes), and what the aids level
    /// of the player lets him see of it. Pure code and no random numbers: the same line always gives the same advice, and a bike with other brakes or another top speed gets a line of its own (the plan is made for the bike).</summary>
    public static class DrivingLine
    {
        /// <summary>The plan slows the bike by more than this (m/s2, a little over a third of a g) and it is braking: below it the bike is only easing off (the wind alone slows a big bike by 2 to 4 m/s2 at its top speed). The plans of
        /// the nine bikes slow them by less than 1.5 m/s2 on seven tenths of a lap and by more than 7 on a tenth to a seventh, so the line between easing off and braking is not a fine one.</summary>
        public const float BrakeDecel = 3.5f;
        /// <summary>The plan speeds the bike up by more than this (m/s2) and it is on the throttle.</summary>
        public const float GoAccel = 0.4f;
        /// <summary>A speed above this fraction of the fastest speed of the plan is full throttle even when it does not rise any more.</summary>
        public const float TopFraction = 0.97f;
        /// <summary>A braking zone is shown this far (m) before the plan starts to slow: a rider who reacts to a red line needs the room.</summary>
        public const float LeadMetres = 12f;
        /// <summary>No piece of advice is shorter than this (m): shorter ones are folded into their longer neighbour, so that the line does not flicker.</summary>
        public const float MinZoneMetres = 10f;
        /// <summary>The plan speed at a place is under the first figure times the speed of the bike: brake (red); over the second figure times the speed of the bike: accelerate (green). Between the two the bike is at the plan speed there and does what the plan does there.</summary>
        public const float BrakeBelow = 0.97f, GoAbove = 1.05f;
        /// <summary>A stretch of red that never gets deeper than this fraction of the speed of the bike asks for an easing off and not for the brakes (a fast kink of the plan, which a rider takes without touching the brakes,
        /// is not a braking zone): it is shown yellow. A stretch of red that runs to the end of what is seen stays red, because what comes after it is not known.</summary>
        public const float DeepBelow = 0.90f;
        /// <summary>How much of the road the ribbon shows, ahead of the bike and behind it (m).</summary>
        public const float AheadMetres = 300f, BehindMetres = 8f;

        /// <summary>The advice for every sample of the lap (the same indices as the racing line and the circuit).</summary>
        public static Advice[] Classify(RacingLine line)
        {
            int n = line.c.n;
            float top = 0f;
            for (int i = 0; i < n; i++) if (line.vplan[i] > top) top = line.vplan[i];
            float ds = PM.Max(0.5f, line.c.spacing);
            var a = new Advice[n];
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;
                float v0 = line.vplan[i], v1 = line.vplan[j];
                float accel = (v1 * v1 - v0 * v0) / (2f * ds);          // positive: the plan speeds up; negative: it slows down
                if (accel < -BrakeDecel) a[i] = Advice.Brake;
                else if (accel > GoAccel || v0 >= TopFraction * top) a[i] = Advice.Go;
                else a[i] = Advice.Lift;
            }
            int minZone = Math.Max(2, (int)Math.Round(MinZoneMetres / line.c.spacing));
            Merge(a, minZone);
            Lead(a, (int)Math.Round(LeadMetres / line.c.spacing));
            Merge(a, minZone);
            return a;
        }

        /// <summary>Fold the shortest run below 'min' samples into the longer of its two neighbours, again and again until none is left (the lap is a ring: the first run follows the last).</summary>
        static void Merge(Advice[] a, int min)
        {
            int n = a.Length;
            var begins = new List<int>(); var lens = new List<int>();
            for (int guard = 0; guard < 4 * n; guard++)
            {
                int start = -1;
                for (int i = 0; i < n; i++) if (a[i] != a[i == 0 ? n - 1 : i - 1]) { start = i; break; }
                if (start < 0) return;                                  // one advice all the way round
                begins.Clear(); lens.Clear();
                int k = 0;
                while (k < n)
                {
                    int s = (start + k) % n, len = 1;
                    while (k + len < n && a[(start + k + len) % n] == a[s]) len++;
                    begins.Add(s); lens.Add(len); k += len;
                }
                int r = -1;
                for (int q = 0; q < lens.Count; q++) if (lens[q] < min && (r < 0 || lens[q] < lens[r])) r = q;
                if (r < 0 || lens.Count < 2) return;
                int prev = (r + lens.Count - 1) % lens.Count, next = (r + 1) % lens.Count;
                Advice into = lens[prev] >= lens[next] ? a[begins[prev]] : a[begins[next]];
                for (int m = 0; m < lens[r]; m++) a[(begins[r] + m) % n] = into;
            }
        }

        /// <summary>Start every braking zone 'lead' samples earlier.</summary>
        static void Lead(Advice[] a, int lead)
        {
            int n = a.Length;
            var was = (Advice[])a.Clone();
            for (int i = 0; i < n; i++)
            {
                if (was[i] != Advice.Brake || was[(i + n - 1) % n] == Advice.Brake) continue;
                for (int m = 1; m <= lead; m++)
                {
                    int q = (i + n - m) % n;
                    if (was[q] == Advice.Brake) break;
                    a[q] = Advice.Brake;
                }
            }
        }

        /// <summary>The advice at a place of the road for a bike with this speed (m/s): 'planSpeed' is what the plan has there and 'plan' what a rider on the plan does there (Classify). Red where the bike is faster than the plan
        /// (it must brake: the red begins where the plan speed falls under the speed of the bike, which is the braking point of a rider at that speed), green where the plan has room over the bike (and for a bike that stands still
        /// all of it), and where the bike is at the plan speed what the plan does there; a braking zone the bike is not up to yet is a place to hold the speed.</summary>
        public static Advice Advise(float planSpeed, float bikeSpeed, Advice plan)
        {
            if (bikeSpeed < 1f) return Advice.Go;
            float r = planSpeed / bikeSpeed;
            if (r < BrakeBelow) return Advice.Brake;
            if (r > GoAbove) return Advice.Go;
            return plan == Advice.Brake ? Advice.Lift : plan;
        }

        /// <summary>The advice for a stretch of road for a bike with this speed, as the player sees it: the sample i0 + k, for k from -behind to into.Length - behind - 1, goes to into[k + behind] (the lap is a ring). Advise at
        /// every sample; a red that does not get deeper than DeepBelow of the speed is yellow; the red that is left starts LeadMetres earlier; and a piece of the advice that is shorter than MinZoneMetres in the middle of the
        /// stretch is folded into the longer of its neighbours (Tidy), so that a blip of the plan, a few metres where its curve changes shape, does not show on the road as a speck. The pieces at the two ends of the stretch
        /// are kept as they are, because the stretch cuts them.</summary>
        public static void Window(RacingLine line, Advice[] plan, int i0, float speed, int behind, Advice[] into)
        {
            int n = line.c.n, m = into.Length;
            for (int q = 0; q < m; q++)
            {
                int i = (i0 + q - behind) % n; if (i < 0) i += n;
                into[q] = Advise(line.vplan[i], speed, plan[i]);
            }
            if (speed >= 1f)
            {
                // a red that does not get deep enough (DeepBelow) is an easing off: yellow
                for (int q = 0; q < m;)
                {
                    if (into[q] != Advice.Brake) { q++; continue; }
                    int r = q; float low = float.MaxValue;
                    while (r < m && into[r] == Advice.Brake) { int i = (i0 + r - behind) % n; if (i < 0) i += n; if (line.vplan[i] < low) low = line.vplan[i]; r++; }
                    if (r < m && low > DeepBelow * speed) for (int k = q; k < r; k++) into[k] = Advice.Lift;
                    q = r;
                }
                // the red is shown a little before the plan reaches the speed of the bike: a rider who reacts to it needs the room (the sweep goes from the far end back, and goes on from before the start it has just moved)
                int lead = (int)Math.Round(LeadMetres / line.c.spacing);
                for (int q = m - 1; q >= 1; q--)
                {
                    if (into[q] != Advice.Brake || into[q - 1] == Advice.Brake) continue;
                    int k = 1;
                    while (k <= lead && q - k >= 0 && into[q - k] != Advice.Brake) { into[q - k] = Advice.Brake; k++; }
                    q -= k - 1;
                }
            }
            Tidy(into, Math.Max(2, (int)Math.Round(MinZoneMetres / line.c.spacing)));
        }

        /// <summary>Fold the pieces of a stretch of advice that are shorter than 'min' samples into the longer of their two neighbours, the shortest first, again and again until none is left; the first and the last piece are
        /// never folded (the stretch cuts them: their length is not known).</summary>
        public static void Tidy(Advice[] a, int min)
        {
            int m = a.Length;
            for (int guard = 0; guard < m; guard++)
            {
                int at = -1, atLen = int.MaxValue;
                for (int i = 0; i < m;)
                {
                    int j = i + 1; while (j < m && a[j] == a[i]) j++;
                    if (i > 0 && j < m && j - i < min && j - i < atLen) { at = i; atLen = j - i; }
                    i = j;
                }
                if (at < 0) return;
                int pl = 1; while (at - 1 - pl >= 0 && a[at - 1 - pl] == a[at - 1]) pl++;
                int nl = 1; while (at + atLen + nl < m && a[at + atLen + nl] == a[at + atLen]) nl++;
                Advice into = pl >= nl ? a[at - 1] : a[at + atLen];
                for (int q = 0; q < atLen; q++) a[at + q] = into;
            }
        }

        /// <summary>How far down the plan is read for the bottom of a dip (m).</summary>
        public const float ScanMetres = 800f;

        /// <summary>What a stretch of the line holds for the brakes, as the player sees it (FirstBraking).</summary>
        public struct Braking
        {
            /// <summary>Metres from the bike to where the first red begins (0: the bike is on it), or -1 when the stretch has no red.</summary>
            public float ahead;
            /// <summary>Metres of that red, from where it begins (or from the bike, when the bike is on it) to where it ends or to the far end of the stretch.</summary>
            public float length;
            /// <summary>The red runs to the far end of the stretch: how long it really is is not known yet.</summary>
            public bool open;
            /// <summary>The lowest speed of the plan (m/s) in the first dip from where the red begins: the speed of the corner the braking is for. It is read on the plan itself, down to ScanMetres from the start of the red, and is not cut by the stretch the player sees.</summary>
            public float lowest;
            /// <summary>The plan comes back up by 5 % from its lowest speed within ScanMetres: 'lowest' is the bottom of a dip. When it does not, the plan only keeps falling and the speed of the corner is not known.</summary>
            public bool bottom;
        }

        /// <summary>The first red of a stretch of advice made by Window (the sample i0 + k goes to win[k + behind]), from the bike on: where it begins, how long it is and how low the plan goes in its first dip. 'ahead' is -1 when there is none.</summary>
        public static Braking FirstBraking(RacingLine line, Advice[] win, int i0, int behind)
        {
            var b = new Braking { ahead = -1f };
            int m = win.Length, n = line.c.n, q = behind;
            while (q < m && win[q] != Advice.Brake) q++;
            if (q >= m) return b;
            int r = q;
            while (r < m && win[r] == Advice.Brake) r++;
            b.ahead = (q - behind) * line.c.spacing; b.length = (r - q) * line.c.spacing; b.open = r >= m;
            // the speed the braking is for: the bottom of the first dip of the plan from the start of the red (the plan falls to its lowest speed, and the dip ends where it has climbed 5 % above it)
            int from = (i0 + q - behind) % n; if (from < 0) from += n;
            int scan = (int)Math.Round(ScanMetres / line.c.spacing);
            float low = float.MaxValue; bool risen = false;
            for (int k = 0; k < scan && !risen; k++)
            {
                float vv = line.vplan[(from + k) % n];
                if (vv < low) low = vv; else if (vv > low * 1.05f) risen = true;
            }
            b.lowest = low; b.bottom = risen;
            return b;
        }

        /// <summary>How many separate stretches of this advice the lap has (the ring is closed: a stretch that crosses the start line is one).</summary>
        public static int Zones(Advice[] a, Advice kind)
        {
            int n = a.Length, zones = 0, all = 0;
            for (int i = 0; i < n; i++)
            {
                if (a[i] != kind) continue;
                all++;
                if (a[(i + n - 1) % n] != kind) zones++;
            }
            return all == n && n > 0 ? 1 : zones;
        }

        /// <summary>The fraction of the lap with this advice.</summary>
        public static float Share(Advice[] a, Advice kind)
        {
            int k = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] == kind) k++;
            return a.Length == 0 ? 0f : k / (float)a.Length;
        }

        /// <summary>The length (m) of the shortest stretch of this advice, or 0 when there is none.</summary>
        public static float ShortestZone(Advice[] a, Advice kind, float spacing)
        {
            int n = a.Length, best = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (a[i] != kind || a[(i + n - 1) % n] == kind) continue;
                int len = 1;
                while (len < n && a[(i + len) % n] == kind) len++;
                if (len < best) best = len;
            }
            return best == int.MaxValue ? 0f : best * spacing;
        }

        /// <summary>What the aids level of the rider lets him see: 0 (Completi) the whole line in colour, 1 (Sportivi) only the red of the braking zones, 2 (Nessuno) nothing.</summary>
        public static bool Shows(int aids, Advice what)
        {
            if (aids <= 0) return true;
            if (aids == 1) return what == Advice.Brake;
            return false;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Production.Racing
{
    /// <summary>The cameras of a replay. Chase, Far and Onboard are the three views of the race camera, on any rider; the others are the cameras of a television director: Pan (a long lens beside the track that
    /// follows the rider past), Fixed (a low wide camera on the verge that looks up the road while the rider thunders by), Heli (a helicopter that trails the rider), Grid (the start seen from past the line)
    /// and Finish (the line seen from past it).</summary>
    public enum ShotKind : byte { Chase = 0, Far = 1, Onboard = 2, Pan = 3, Fixed = 4, Heli = 5, Grid = 6, Finish = 7 }

    /// <summary>Why the director cut to a shot.</summary>
    public enum ShotWhy : byte { Ambient = 0, Start = 1, Crash = 2, Overtake = 3, Lap = 4, Finish = 5 }

    /// <summary>One shot of the edit: from t0 to t1 seconds into the race, on this rider, with this camera. A camera that stays where it is (Pan, Fixed, Grid, Finish) has its place in (ax, ay, az) and 'pass'
    /// is the moment the rider is level with it; Fixed also looks at (bx, by, bz); 'param' is how many metres tall the picture is at the rider (Pan, Grid, Finish) or the angle round the rider a helicopter
    /// starts at (Heli).</summary>
    public struct Shot
    {
        public float t0, t1;
        public int subject;
        public ShotKind kind;
        public ShotWhy why;
        public float ax, ay, az, bx, by, bz, param, pass;

        public float Length { get { return t1 - t0; } }
        /// <summary>The camera stays where it is: it was put on the verge when the edit was made.</summary>
        public bool Anchored { get { return kind == ShotKind.Pan || kind == ShotKind.Fixed || kind == ShotKind.Grid || kind == ShotKind.Finish; } }
        /// <summary>The race camera of the game makes these shots (it needs the bike's own model, for the helmet).</summary>
        public bool GameCamera { get { return kind == ShotKind.Chase || kind == ShotKind.Far || kind == ShotKind.Onboard; } }
    }

    /// <summary>Where a camera is, what it looks at, and its field of view in degrees.</summary>
    public struct CamPose { public float x, y, z, lx, ly, lz, fov; }

    /// <summary>A race, recorded: for every rider a tape of poses (20 a second, from the lights going out) and the distance along the race at every frame, as the rules of the race saw it. From it the replay shows
    /// any rider from any camera, and the director finds what is worth showing: the falls, the overtakes, the battles, the finish. Pure code: a test can record races and measure the edits.</summary>
    public sealed class RaceRecording
    {
        public readonly int count, laps;
        public readonly float length;
        public readonly string[] names;
        public readonly PoseTape[] tapes;
        /// <summary>Which rider is the player's own (the first subject of the replay).</summary>
        public int me;
        /// <summary>Seconds into the recording at which each rider crossed the line to finish, -1 if they did not.</summary>
        public readonly float[] finish;
        public readonly List<Moment> crashes = new List<Moment>(), overtakes = new List<Moment>();
        readonly List<float>[] prog;

        /// <summary>Something that happened: when, to whom (and who was passed), where, how far along the race.</summary>
        public struct Moment { public float t; public int rider, other; public float x, z, progress; }

        public RaceRecording(int count, string[] names, int me, float length, int laps)
        {
            this.count = count; this.names = names; this.me = me; this.length = length; this.laps = laps;
            tapes = new PoseTape[count]; prog = new List<float>[count]; finish = new float[count];
            for (int i = 0; i < count; i++) { tapes[i] = new PoseTape(); prog[i] = new List<float>(); finish[i] = -1f; }
        }

        /// <summary>Add the next frame of a rider (call for every rider once every 1/20 s); false when the tape is full.</summary>
        public bool Add(int rider, in PoseFrame f, float progress)
        {
            if (rider < 0 || rider >= count) return false;
            if (!tapes[rider].Add(f)) return false;
            prog[rider].Add(progress);
            return true;
        }

        public int Frames { get { int n = 0; for (int i = 0; i < count; i++) n = Math.Max(n, tapes[i].Count); return n; } }
        public float Duration { get { int n = Frames; return n < 2 ? 0f : (n - 1) * PoseTape.FrameTime; } }

        public PoseFrame PoseAt(int rider, float t) { return tapes[rider].At(t); }

        /// <summary>Metres along the race (negative on the grid, one lap is 'length') at this time.</summary>
        public float ProgressAt(int rider, float t)
        {
            var p = prog[rider];
            if (p.Count == 0) return 0f;
            float f = t * PoseTape.Rate;
            if (!(f > 0f)) return p[0];
            int i = (int)f;
            if (i >= p.Count - 1) return p[p.Count - 1];
            return p[i] + (p[i + 1] - p[i]) * (f - i);
        }

        float ProgressFrame(int r, int f) { var p = prog[r]; return p.Count == 0 ? -1e9f : p[Math.Min(f, p.Count - 1)]; }

        bool CrashedFrame(int r, int f) { var t = tapes[r]; return t.Count > 0 && t[Math.Min(f, t.Count - 1)].Has(PoseFrame.Crashed); }

        int FrameOf(float t) { return Math.Max(0, (int)(t * PoseTape.Rate + 0.5f)); }

        /// <summary>1 for the leader: the place of a rider by distance along the race at this time.</summary>
        public int PositionAt(int rider, float t)
        {
            int f = FrameOf(t), better = 0; float pr = ProgressFrame(rider, f);
            for (int o = 0; o < count; o++)
            {
                if (o == rider) continue;
                float po = ProgressFrame(o, f);
                if (po > pr || (po == pr && o < rider)) better++;
            }
            return better + 1;
        }

        public int LeaderAt(float t)
        {
            int f = FrameOf(t), best = 0; float bp = float.MinValue;
            for (int r = 0; r < count; r++) { float p = ProgressFrame(r, f); if (p > bp) { bp = p; best = r; } }
            return best;
        }

        /// <summary>The rider (not 'exclude', not fallen) who is closest to a neighbour on the road, if they are within 'within' metres: the heart of a battle. -1 if the field is strung out.</summary>
        public int BattleRider(float t, int exclude, float within)
        {
            int f = FrameOf(t), best = -1; float bestGap = within;
            for (int r = 0; r < count; r++)
            {
                if (r == exclude || CrashedFrame(r, f)) continue;
                float pr = ProgressFrame(r, f);
                for (int o = 0; o < count; o++)
                {
                    if (o == r || CrashedFrame(o, f)) continue;
                    float g = Math.Abs(ProgressFrame(o, f) - pr);
                    if (g < bestGap) { bestGap = g; best = r; }
                }
            }
            return best;
        }

        /// <summary>When a rider reached this distance along the race (the first time), -1 if never.</summary>
        public float TimeOfProgress(int rider, float distance)
        {
            var p = prog[rider];
            for (int i = 1; i < p.Count; i++)
                if (p[i] >= distance && p[i - 1] < distance) return (i - 1 + (distance - p[i - 1]) / Math.Max(1e-6f, p[i] - p[i - 1])) * PoseTape.FrameTime;
            return -1f;
        }

        /// <summary>Find the falls and the overtakes in what was recorded. A fall is the flag coming up (at most one in four seconds for a rider). An overtake is a rider who gets ahead of one who was ahead, when both
        /// are on their bikes and within thirty metres, after the first six seconds (the start is a scramble the first shot covers).</summary>
        public void Analyse()
        {
            crashes.Clear(); overtakes.Clear();
            int F = Frames;
            if (F < 3) return;
            for (int r = 0; r < count; r++)
            {
                var tp = tapes[r]; bool was = false; float last = -99f;
                for (int f = 0; f < tp.Count; f++)
                {
                    bool now = tp[f].Has(PoseFrame.Crashed);
                    float t = f * PoseTape.FrameTime;
                    if (now && !was && t - last > 4f) { crashes.Add(new Moment { t = t, rider = r, other = -1, x = tp[f].x, z = tp[f].z, progress = ProgressFrame(r, f) }); last = t; }
                    was = now;
                }
            }
            crashes.Sort((a, b) => a.t.CompareTo(b.t));
            var rank = new int[count]; var prev = new int[count]; bool have = false;
            var lastGain = new float[count]; for (int i = 0; i < count; i++) lastGain[i] = -99f;
            for (int f = 5; f < F; f += 5)
            {
                float t = f * PoseTape.FrameTime;
                for (int r = 0; r < count; r++)
                {
                    int better = 0; float pr = ProgressFrame(r, f);
                    for (int o = 0; o < count; o++) { if (o == r) continue; float po = ProgressFrame(o, f); if (po > pr || (po == pr && o < r)) better++; }
                    rank[r] = better;
                }
                if (have && t >= 6f)
                {
                    for (int r = 0; r < count; r++)
                    {
                        if (tapes[r].Count == 0 || rank[r] >= prev[r] || t - lastGain[r] < 1.5f || CrashedFrame(r, f)) continue;
                        int passed = -1; float gap = 30f;
                        for (int o = 0; o < count; o++)
                        {
                            if (o == r || prev[o] >= prev[r] || rank[o] <= rank[r] || CrashedFrame(o, f)) continue;
                            float g = Math.Abs(ProgressFrame(o, f) - ProgressFrame(r, f));
                            if (g < gap) { gap = g; passed = o; }
                        }
                        if (passed >= 0) { overtakes.Add(new Moment { t = t, rider = r, other = passed, x = tapes[r][Math.Min(f, tapes[r].Count - 1)].x, z = tapes[r][Math.Min(f, tapes[r].Count - 1)].z, progress = ProgressFrame(r, f) }); lastGain[r] = t; }
                    }
                }
                Array.Copy(rank, prev, count); have = true;
            }
        }
    }

    /// <summary>The television director: from a recorded race it cuts a list of shots that covers the whole of it, in the way a broadcast would. The start from past the line, then a mixture of the player, the
    /// leader and the riders in a battle seen from every camera, never the same camera or the same rider twice running, with a cut to every fall (the camera is waiting where it happens) and to every
    /// overtake and every lap of the player, and the end of the race at the line, on the winner and on the player. The same race and seed always give the same edit.</summary>
    public static class ReplayEdit
    {
        public const float MinShot = 2.6f, MaxShot = 9.5f, StartShot = 5.5f;

        struct Event { public float t, lead, tail, weight; public int rider; public ShotWhy why; }

        /// <summary>What the plan keeps while it is being made.</summary>
        sealed class Cutter
        {
            public List<Shot> shots = new List<Shot>();
            public List<Event> events;
            public Rng rng;
            public ShotKind prev = ShotKind.Grid, prev2 = ShotKind.Grid;
            public int prevSubject;
            public float lastOvertake = -99f;
        }

        public static List<Shot> Plan(RaceRecording rec, Circuit c, int seed)
        {
            var cut = new Cutter { rng = new Rng(seed * 7919 + 17), prevSubject = rec.me };
            float T = rec.Duration;
            if (rec.count == 0 || T <= 0.5f) return cut.shots;
            int me = RacingMath.Clamp(rec.me, 0, rec.count - 1);
            if (T < 2f * MinShot + 1f) { cut.shots.Add(Build(rec, c, ref cut.rng, ShotKind.Chase, ShotWhy.Ambient, me, 0f, T, 0f)); return cut.shots; }

            // ---- the start, seen from past the line
            float tStart = Math.Min(StartShot, T * 0.34f);
            cut.shots.Add(Build(rec, c, ref cut.rng, ShotKind.Grid, ShotWhy.Start, me, 0f, tStart, 0f));

            // ---- the end: the winner crossing the line, then the player if somebody else won (the finish shots wait for their turn, the race between them is cut like the rest)
            var finishes = new List<Shot>();
            int winner = -1; float tWin = -1f;
            for (int r = 0; r < rec.count; r++) if (rec.finish[r] >= 0f && (winner < 0 || rec.finish[r] < tWin)) { winner = r; tWin = rec.finish[r]; }
            if (winner >= 0 && tWin > tStart + MinShot + 1f)
            {
                float a = Math.Max(tStart + MinShot, tWin - 3.4f), b = Math.Min(T, tWin + 2.8f);
                if (b - a >= MinShot)
                {
                    finishes.Add(Build(rec, c, ref cut.rng, ShotKind.Finish, ShotWhy.Finish, winner, a, b, tWin));
                    float tMe = rec.finish[me];
                    if (me != winner && tMe > b + 0.5f)
                    {
                        float a2 = Math.Max(b, tMe - 3.0f), b2 = Math.Min(T, tMe + 2.8f);
                        if (b2 - a2 >= MinShot) finishes.Add(Build(rec, c, ref cut.rng, ShotKind.Finish, ShotWhy.Finish, me, a2, b2, tMe));
                    }
                }
            }

            // ---- what is worth a cut
            cut.events = new List<Event>();
            foreach (var m in rec.crashes) cut.events.Add(new Event { t = m.t, lead = 2.0f, tail = 3.6f, weight = 3f + (m.rider == me ? 1.5f : 0f), rider = m.rider, why = ShotWhy.Crash });
            foreach (var m in rec.overtakes)
            {
                // a broadcast shows the passes that matter: the player's, and those for the first places
                bool mine = m.rider == me || m.other == me;
                if (!mine && rec.PositionAt(m.rider, m.t) > 3) continue;
                cut.events.Add(new Event { t = m.t, lead = 1.6f, tail = 2.4f, weight = 1.4f + (mine ? 1.6f : 0f), rider = m.rider, why = ShotWhy.Overtake });
            }
            for (int j = 1; j < rec.laps; j++)
            {
                float tl = rec.TimeOfProgress(me, j * rec.length);
                if (tl > tStart + 2f) cut.events.Add(new Event { t = tl, lead = 1.8f, tail = 0.9f, weight = 1.2f, rider = me, why = ShotWhy.Lap });
            }
            cut.events.Sort((x, y) => x.t.CompareTo(y.t));

            // ---- from the start shot to the first finish shot, then round the finish shots to the end
            float from = tStart;
            foreach (var f in finishes)
            {
                Fill(cut, rec, c, from, f.t0);
                cut.shots.Add(f);
                from = f.t1;
            }
            Fill(cut, rec, c, from, T);
            return cut.shots;
        }

        /// <summary>Cut the race between two moments, shot by shot. A fall is never missed: when one is about to happen the camera is put where it will, whatever else is going on.</summary>
        static void Fill(Cutter cut, RaceRecording rec, Circuit c, float from, float to)
        {
            var shots = cut.shots;
            if (to - from < MinShot)
            {
                // not enough room for a shot: the one before simply lasts a little longer
                if (to > from && shots.Count > 0) { var l = shots[shots.Count - 1]; l.t1 = to; shots[shots.Count - 1] = l; }
                return;
            }
            float t = from;
            int guard = 0;
            while (t < to - 0.01f && guard++ < 2000)
            {
                int best = -1;
                for (int e = 0; e < cut.events.Count; e++)
                {
                    var ev = cut.events[e];
                    if (ev.t < t - 0.3f || ev.t - ev.lead > t + MinShot || ev.t > to - 0.5f) continue;
                    if (ev.why == ShotWhy.Overtake && t - cut.lastOvertake < 6f) continue;       // not one pass after another
                    bool better = best < 0 || (ev.why == ShotWhy.Crash && cut.events[best].why != ShotWhy.Crash) || (ev.why == cut.events[best].why && ev.weight > cut.events[best].weight)
                        || (cut.events[best].why != ShotWhy.Crash && ev.why != ShotWhy.Crash && ev.weight > cut.events[best].weight);
                    if (better) best = e;
                }
                Shot s;
                if (best >= 0)
                {
                    var ev = cut.events[best];
                    float t1 = Math.Min(to, Math.Max(t + MinShot, ev.t + ev.tail));
                    // never run over the lead-in of a fall that comes next
                    for (int e = 0; e < cut.events.Count; e++)
                        if (e != best && cut.events[e].why == ShotWhy.Crash && cut.events[e].t > ev.t && cut.events[e].t - cut.events[e].lead >= t + MinShot) t1 = Math.Min(t1, cut.events[e].t - cut.events[e].lead);
                    if (to - t1 < MinShot) t1 = to;
                    if (ev.why == ShotWhy.Lap && t1 - t < MinShot) t1 = Math.Min(to, t + MinShot);
                    ShotKind kind = ev.why == ShotWhy.Crash ? ShotKind.Pan
                        : ev.why == ShotWhy.Lap ? (t1 - t > 4.2f ? ShotKind.Pan : ShotKind.Fixed)       // the whoosh at the line, or a long lens if the shot is long
                        : cut.prev != ShotKind.Pan ? ShotKind.Pan : ShotKind.Chase;
                    float pass = ev.why == ShotWhy.Lap ? ev.t : Math.Max(t, Math.Min(t1, ev.t));
                    s = Build(rec, c, ref cut.rng, kind, ev.why, ev.rider, t, t1, pass);
                    if (ev.why == ShotWhy.Overtake) cut.lastOvertake = t1;
                    float tt = t;
                    cut.events.RemoveAll(x => x.t <= t1 + 0.2f && (x.why != ShotWhy.Crash || x.t >= tt - 0.3f));
                }
                else
                {
                    float limit = to;
                    for (int e = 0; e < cut.events.Count; e++)
                    {
                        var ev = cut.events[e];
                        if (ev.t >= t && ev.t <= to - 0.5f && !(ev.why == ShotWhy.Overtake && t - cut.lastOvertake < 6f)) limit = Math.Min(limit, ev.t - ev.lead);
                    }
                    s = Ambient(rec, c, ref cut.rng, t, limit, to, cut.prevSubject, cut.prev, cut.prev2);
                }
                shots.Add(s);
                cut.prev2 = cut.prev; cut.prev = s.kind; cut.prevSubject = s.subject;
                t = s.t1;
            }
        }

        /// <summary>A shot picked by the viewer: this camera on this rider from this moment, for as long as such a shot lasts (a camera that stays where it is has its place made for the next few seconds, and when
        /// they are over the replay asks for another).</summary>
        public static Shot Manual(RaceRecording rec, Circuit c, ShotKind kind, int subject, float t, int seed)
        {
            var rng = new Rng(seed * 31 + subject * 977 + (int)(t * 10f));
            float len = kind == ShotKind.Fixed ? 3.5f : kind == ShotKind.Pan ? 6.5f : 1e6f;
            float pass = kind == ShotKind.Fixed ? t + 1.6f : t + 0.55f * Math.Min(len, 8f);
            return Build(rec, c, ref rng, kind, ShotWhy.Ambient, subject, t, t + len, pass);
        }

        /// <summary>A shot with no event behind it: a rider (the player, the leader, somebody in a battle, or anyone), a camera that is not the one just used, and a length that suits the camera.</summary>
        static Shot Ambient(RaceRecording rec, Circuit c, ref Rng rng, float t0, float limit, float to, int prevSubject, ShotKind prev, ShotKind prev2)
        {
            int me = rec.me;
            float probe = t0 + 3f;
            int leader = rec.LeaderAt(probe), battle = rec.BattleRider(probe, me, 22f);
            var w = new[] { 2.0f, leader != me ? 1.2f : 0f, battle >= 0 && battle != me ? 1.7f : 0f, 0.4f };
            if (prevSubject == me) w[0] *= 0.5f;
            int pick = Pick(ref rng, w);
            int subject = pick == 0 ? me : pick == 1 ? leader : pick == 2 ? battle : rng.Int(rec.count);
            if (subject == prevSubject && pick != 0) subject = me == prevSubject ? leader : me;
            var kinds = new[] { ShotKind.Chase, ShotKind.Pan, ShotKind.Heli, ShotKind.Fixed, ShotKind.Far, ShotKind.Onboard };
            var kw = new[] { 1.0f, 1.7f, 0.9f, 0.9f, 0.6f, 0.9f };
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] == prev) kw[i] = 0f;
                else if (kinds[i] == prev2) kw[i] *= 0.5f;
            }
            ShotKind kind = kinds[Pick(ref rng, kw)];
            float len = kind == ShotKind.Fixed ? rng.Range(3.0f, 4.0f) : kind == ShotKind.Pan ? rng.Range(4.5f, 7.5f) : kind == ShotKind.Heli ? rng.Range(6f, 9f)
                : kind == ShotKind.Onboard ? rng.Range(5f, 8f) : rng.Range(4.5f, 7f);
            float t1 = Math.Min(t0 + len, limit);
            if (to - t1 < MinShot) t1 = to;
            if (t1 - t0 < MinShot) t1 = Math.Min(to, t0 + MinShot);
            float pass = t0 + 0.55f * (t1 - t0);
            return Build(rec, c, ref rng, kind, ShotWhy.Ambient, subject, t0, t1, pass);
        }

        static int Pick(ref Rng rng, float[] w)
        {
            float sum = 0f; for (int i = 0; i < w.Length; i++) sum += w[i];
            float x = rng.Value() * sum;
            for (int i = 0; i < w.Length; i++) { if (x < w[i]) return i; x -= w[i]; }
            return w.Length - 1;
        }

        /// <summary>Make the shot and, for a camera that stays where it is, put it beside the road near the place the rider is at the moment of 'pass'.</summary>
        static Shot Build(RaceRecording rec, Circuit c, ref Rng rng, ShotKind kind, ShotWhy why, int subject, float t0, float t1, float pass)
        {
            var s = new Shot { t0 = t0, t1 = t1, subject = subject, kind = kind, why = why, pass = pass };
            if (kind == ShotKind.Heli) { s.param = rng.Range(0f, RacingMath.TwoPi); return s; }
            if (!s.Anchored) return s;
            float sT = c.WrapS(rec.ProgressAt(subject, pass));
            switch (kind)
            {
                case ShotKind.Pan:
                    s.param = rng.Range(5.5f, 8.5f);
                    Place(c, sT, Outside(c, sT, ref rng), rng.Range(18f, 34f), rng.Range(3.8f, 8.5f), 6f, ref s);
                    break;
                case ShotKind.Fixed:
                    {
                        Place(c, sT, Nearest(c, sT), rng.Range(1.0f, 2.0f), rng.Range(0.7f, 1.4f), 1.5f, ref s);
                        // it looks up the road at the place the rider will come from, a moment before the pass
                        var from = rec.PoseAt(subject, Math.Max(t0, pass - 0.7f));
                        s.bx = from.x; s.by = from.y + 0.9f; s.bz = from.z;
                        break;
                    }
                case ShotKind.Grid:
                    s.param = 26f; s.pass = 0f;
                    Place(c, 14f, 1, rng.Range(4f, 7f), rng.Range(1.8f, 2.8f), 2.0f, ref s);
                    break;
                case ShotKind.Finish:
                    s.param = 14f;
                    Place(c, 8f, rng.Chance(0.5f) ? 1 : -1, rng.Range(9f, 14f), rng.Range(1.8f, 3f), 2.0f, ref s);
                    break;
            }
            return s;
        }

        /// <summary>Put the camera of a shot beside the road, and not on a stretch of road that happens to run close by (the other leg of a hairpin): try the side asked for, the other side, and nearer and farther
        /// from the barrier, and keep the first place that is at least 'clear' metres beyond the barrier of the nearest stretch of road.</summary>
        static void Place(Circuit c, float sT, int side, float beyond, float height, float clear, ref Shot s)
        {
            float bestX = 0f, bestY = 0f, bestZ = 0f, bestOff = -1e9f;
            int hint = -1;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int sd = attempt % 2 == 0 ? side : -side;
                float bey = beyond * (attempt < 2 ? 1f : attempt < 4 ? 0.7f : attempt < 6 ? 1.4f : 1.9f);
                float x, y, z;
                Spot(c, sT, sd, bey, height, out x, out y, out z);
                Proj pr; c.Project(x, z, ref hint, out pr);
                int ps = pr.d >= 0f ? 1 : 0;
                float off = Math.Abs(pr.d) - (c.HalfWidthAt(pr) + c.Lerp(c.run[ps], pr.i, pr.t));
                if (off > bestOff) { bestOff = off; bestX = x; bestY = y; bestZ = z; }
                if (off >= clear) break;
            }
            s.ax = bestX; s.ay = bestY; s.az = bestZ;
        }

        /// <summary>The side of the road a camera is best put on at this distance along the lap: outside of the corner, so that the road curves towards it (+1 right, -1 left); on a straight, either.</summary>
        static int Outside(Circuit c, float s, ref Rng rng)
        {
            float cx, cz, cy, h0, h1;
            c.At(s - 30f, out cx, out cz, out cy, out h0);
            c.At(s + 30f, out cx, out cz, out cy, out h1);
            float turn = RacingMath.WrapPi(h1 - h0);
            if (turn > 0.10f) return -1;
            if (turn < -0.10f) return 1;
            return rng.Chance(0.5f) ? 1 : -1;
        }

        /// <summary>The side of the road whose barrier is the nearer at this distance along the lap (+1 right, -1 left): a low camera wants the riders close, not across a gravel trap.</summary>
        static int Nearest(Circuit c, float s)
        {
            float f = c.WrapS(s) / c.spacing; int i = c.Wrap((int)f); float t = f - (int)f;
            return c.Lerp(c.run[1], i, t) <= c.Lerp(c.run[0], i, t) ? 1 : -1;
        }

        /// <summary>A place beside the road: at distance 's' along the lap, on a side, 'beyond' metres past the barrier, 'height' above the ground there.</summary>
        public static void Spot(Circuit c, float s, int side, float beyond, float height, out float x, out float y, out float z)
        {
            float f = c.WrapS(s) / c.spacing; int i = c.Wrap((int)f); float t = f - (int)f;
            int sd = side > 0 ? 1 : 0;
            float off = c.Lerp(c.halfW, i, t) + c.Lerp(c.run[sd], i, t) + beyond;
            float nx = c.Lerp(c.nx, i, t), nz = c.Lerp(c.nz, i, t);
            float nl = RacingMath.Sqrt(nx * nx + nz * nz);
            if (nl > 1e-6f) { nx /= nl; nz /= nl; }
            float d = (side > 0 ? 1f : -1f) * off;
            x = c.Lerp(c.px, i, t) + nx * d; z = c.Lerp(c.pz, i, t) + nz * d;
            y = c.SurfaceY(i, t, d) + height;
        }
    }

    /// <summary>Where the cameras of the edit are, frame by frame. Chase, Far and Onboard are not here: the race camera of the game makes those.</summary>
    public static class ShotCamera
    {
        /// <summary>The pose of the camera of this shot at this time; false for the shots the race camera makes.</summary>
        public static bool Pose(in Shot s, RaceRecording rec, float t, out CamPose p)
        {
            p = default(CamPose);
            var f = rec.PoseAt(s.subject, t);
            switch (s.kind)
            {
                case ShotKind.Pan:
                case ShotKind.Grid:
                case ShotKind.Finish:
                    {
                        p.x = s.ax; p.y = s.ay; p.z = s.az;
                        p.lx = f.x; p.ly = f.y + 0.9f; p.lz = f.z;
                        float dx = p.lx - p.x, dy = p.ly - p.y, dz = p.lz - p.z;
                        float dist = RacingMath.Sqrt(dx * dx + dy * dy + dz * dz);
                        p.fov = RacingMath.Clamp(2f * RacingMath.Atan(0.5f * s.param / Math.Max(dist, 4f)) * RacingMath.Rad2Deg, 9f, 46f);
                        return true;
                    }
                case ShotKind.Fixed:
                    p.x = s.ax; p.y = s.ay; p.z = s.az; p.lx = s.bx; p.ly = s.by; p.lz = s.bz; p.fov = 50f;
                    return true;
                case ShotKind.Heli:
                    {
                        var behind = rec.PoseAt(s.subject, t - 0.8f);
                        float ang = s.param + 0.12f * (t - s.t0);
                        p.x = behind.x + 30f * RacingMath.Sin(ang); p.z = behind.z + 30f * RacingMath.Cos(ang); p.y = Math.Max(behind.y, f.y) + 21f;
                        p.lx = f.x; p.ly = f.y + 0.8f; p.lz = f.z; p.fov = 40f;
                        return true;
                    }
            }
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using PryingEyes.Pista;

/// <summary>Timing, ghosts and the named circuits: a recording must come back from a file exactly as it went in (and a damaged file must be refused, not crash the game), the clock of a lap against the
/// distance must tell how far ahead or behind another lap a bike is, a ghost must fit only the circuit and bike it was made on, and the catalogue must keep giving the same circuits.</summary>
static partial class Program
{
    static PoseFrame SampleFrame(int i)
    {
        float t = i * PoseTape.FrameTime;
        return new PoseFrame
        {
            x = 100f + 35f * t, y = 2f + (float)Math.Sin(t * 0.3f), z = -40f + 12f * t, heading = (float)Math.Sin(t * 0.2f) * 3.0f, lean = (float)Math.Sin(t) * 0.7f, pitch = 0.01f * i, steer = -0.1f * (float)Math.Sin(t),
            hang = (float)Math.Cos(t), tuck = 0.5f, body = -0.25f, speed = 30f + 0.1f * i, rpm = 9000f + 7f * i, spinR = 90f + i, spinF = 91f + i, forkC = 0.02f, shockC = 0.01f, throttle = 0.8f, slipF = 0.01f, slipR = -0.02f,
            gear = (byte)(1 + i % 6), crashKind = (byte)(i % 3), surface = (byte)(i % 2), flags = (ushort)(i % 7 == 0 ? PoseFrame.Crashed | PoseFrame.Braking : PoseFrame.AbsOn)
        };
    }

    static bool SameFrame(PoseFrame a, PoseFrame b)
    {
        return a.x == b.x && a.y == b.y && a.z == b.z && a.heading == b.heading && a.lean == b.lean && a.pitch == b.pitch && a.steer == b.steer && a.hang == b.hang && a.tuck == b.tuck && a.body == b.body
            && a.speed == b.speed && a.rpm == b.rpm && a.spinR == b.spinR && a.spinF == b.spinF && a.forkC == b.forkC && a.shockC == b.shockC && a.throttle == b.throttle && a.slipF == b.slipF && a.slipR == b.slipR
            && a.gear == b.gear && a.crashKind == b.crashKind && a.surface == b.surface && a.flags == b.flags;
    }

    /// <summary>Ride 'laps' laps alone at a pace with the recorder the game uses (a lap trace per lap, a tape per lap) and hand back what it wrote.</summary>
    static void RecordLaps(Circuit c, float pace, int laps, List<LapTrace> traces, List<PoseTape> tapes)
    {
        var race = new Race(c, laps, 3);
        var r = race.AddAi("solo", 0, 0, 5, pace, 0f);
        race.PlaceAll(new List<Racer> { r });
        var trace = new LapTrace(c.length); var tape = new PoseTape();
        int steps = 0;
        race.OnLap = rr => { trace.Seal(rr.lastLap); traces.Add(trace); tapes.Add(tape); trace = new LapTrace(c.length); tape = new PoseTape(); steps = 0; };
        race.StartCountdown();
        float t = 0f;
        while (race.phase != RacePhase.Over && t < 700f)
        {
            race.Step(RaceDt); t += RaceDt;
            if (race.phase != RacePhase.Racing || r.finished) continue;
            trace.Feed(r.progress - r.lap * c.length, race.time - r.lapStart);
            if (steps++ % 3 == 0) tape.Add(new PoseFrame { x = r.x, y = r.y, z = r.z, heading = r.heading, lean = r.lean, speed = r.speed });
        }
    }

    static void TimingChecks()
    {
        // ---- a tape comes back from the file exactly as it went in, in any length, and bad files are refused
        {
            var tape = new PoseTape();
            for (int i = 0; i < 400; i++) tape.Add(SampleFrame(i));
            var trace = new LapTrace(2732.4f);
            for (float d = 0f; d < 2732f; d += 3.1f) trace.Feed(d, d / 38f);
            trace.Seal(75.31f);
            var g = new Ghost { seed = 4242, bikeId = "argo1000", kind = 1, lapTime = 75.31f, circuitLength = 2732.4f, trace = trace, tape = tape };
            var bytes = g.ToBytes();
            Ghost back;
            bool read = Ghost.TryFromBytes(bytes, out back);
            bool same = read && back.seed == 4242 && back.bikeId == "argo1000" && back.kind == 1 && back.lapTime == 75.31f && back.circuitLength == 2732.4f && back.tape.Count == 400 && back.trace.Marks == trace.Marks;
            for (int i = 0; same && i < 400; i++) same = SameFrame(tape[i], back.tape[i]);
            for (int i = 0; same && i < trace.Marks; i++) same = trace.t[i] == back.trace.t[i];
            Check("TIMING_GHOST_FILE_ROUNDTRIP", same, read ? (same ? "400 frames and " + trace.Marks + " marks in " + bytes.Length + " bytes, back bit for bit" : "came back different") : "could not be read",
                "every field comes back exactly");
            int accepted = 0, tried = 0;
            for (int cut = 0; cut < bytes.Length; cut += Math.Max(1, bytes.Length / 211)) { tried++; Ghost x; if (Ghost.TryFromBytes(Slice(bytes, cut), out x)) accepted++; }
            var wrongMagic = (byte[])bytes.Clone(); wrongMagic[0] ^= 0x55;
            var wrongVersion = (byte[])bytes.Clone(); wrongVersion[4] = 9;
            var nan = (byte[])bytes.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(nan, bytes.Length - 81 * 5);
            Ghost y1, y2, y3, y4;
            bool refused = !Ghost.TryFromBytes(wrongMagic, out y1) && !Ghost.TryFromBytes(wrongVersion, out y2) && !Ghost.TryFromBytes(null, out y3) && !Ghost.TryFromBytes(new byte[0], out y4);
            Check("TIMING_GHOST_REFUSES_BAD_FILES", accepted == 0 && refused, tried + " truncated files, " + accepted + " accepted; wrong magic, wrong version, empty and missing refused: " + refused, "nothing damaged is accepted, nothing throws");
            Ghost z;
            bool nanOut = Ghost.TryFromBytes(nan, out z);
            Check("TIMING_GHOST_REFUSES_NONSENSE_FRAMES", !nanOut, nanOut ? "a frame with a position that is not a number was accepted" : "a frame at 'not a number' is refused", "refused");
        }

        // ---- the tape interpolates the short way round and is safe at its ends
        {
            var tape = new PoseTape();
            tape.Add(new PoseFrame { heading = 3.0f, x = 0f, gear = 2 }); tape.Add(new PoseFrame { heading = -3.0f, x = 10f, gear = 3 }); tape.Add(new PoseFrame { heading = -3.0f, x = 20f, gear = 3 });
            var mid = tape.At(PoseTape.FrameTime * 0.5f);
            float seam = PM.WrapPi(mid.heading - (float)Math.PI);
            bool ends = tape.At(-1f).x == 0f && tape.At(99f).x == 20f && new PoseTape().At(1f).x == 0f && tape.At(PoseTape.FrameTime).x == 10f;
            Check("TIMING_TAPE_INTERPOLATES", Math.Abs(mid.x - 5f) < 1e-4f && Math.Abs(seam) < 0.2f && ends && tape.Duration == 2 * PoseTape.FrameTime,
                "half way between 3.0 and -3.0 rad is " + mid.heading.ToString("F2") + " (the short way is through pi), x " + mid.x.ToString("F1") + ", ends safe " + ends, "short way round, clamped at both ends");
            var big = new PoseTape(); int added = 0;
            for (int i = 0; i < PoseTape.MaxFrames + 50; i++) if (big.Add(default(PoseFrame))) added++;
            Check("TIMING_TAPE_HAS_A_CEILING", added == PoseTape.MaxFrames && big.Full, added + " frames kept of " + (PoseTape.MaxFrames + 50) + " offered", "stops at " + PoseTape.MaxFrames);
        }

        // ---- the clock against the distance: a slower lap is behind everywhere, by the right amount at the end, and a lap is level with itself
        {
            var c = CircuitGen.Generate(4242);
            var fast = new List<LapTrace>(); var slow = new List<LapTrace>(); var tf = new List<PoseTape>(); var ts = new List<PoseTape>();
            RecordLaps(c, 0.97f, 3, fast, tf); RecordLaps(c, 0.90f, 3, slow, ts);
            var a = fast[2]; var b = slow[2];
            bool complete = fast.Count == 3 && slow.Count == 3 && a.Complete && b.Complete;
            float worstSelf = 0f, behindFrom = 0f; int n = 0, behind = 0; float endDelta = 0f;
            if (complete)
                for (float d = 5f; d < c.length - 5f; d += 17f)
                {
                    float delta;
                    if (LapTrace.Delta(a, d, a.TimeAt(d), out delta)) worstSelf = Math.Max(worstSelf, Math.Abs(delta));
                    float slowDelta;
                    if (LapTrace.Delta(a, d, b.TimeAt(d), out slowDelta)) { n++; if (slowDelta > -0.01f) behind++; behindFrom = slowDelta; }
                }
            if (complete) LapTrace.Delta(a, c.length - 1f, b.TimeAt(c.length - 1f), out endDelta);
            float lapDiff = complete ? b.lapTime - a.lapTime : 0f;
            Check("TIMING_TRACE_TELLS_THE_DELTA", complete && worstSelf < 1e-3f && behind == n && n > 100 && Math.Abs(endDelta - lapDiff) < 0.15f && lapDiff > 3f,
                "laps " + a.lapTime.ToString("F2") + " s and " + b.lapTime.ToString("F2") + " s; against itself worst " + worstSelf.ToString("F4") + " s; the slower lap was behind at " + behind + " of " + n + " points and " + endDelta.ToString("F2") + " s at the end (laps differ by " + lapDiff.ToString("F2") + " s)",
                "level with itself, behind everywhere, the lap difference at the line");
            float noRef;
            Check("TIMING_NO_REFERENCE_NO_DELTA", !LapTrace.Delta(null, 100f, 3f, out noRef) && !LapTrace.Delta(new LapTrace(c.length), 100f, 3f, out noRef), "no delta against nothing or against a lap that is not over", "false");
            // the tape of a lap has a frame every 1/20 s and the trace has a mark every 10 m
            var tape = tf[2];
            Check("TIMING_TAPE_LENGTH_MATCHES_THE_LAP", complete && Math.Abs(tape.Duration - a.lapTime) < 0.2f && a.Marks == (int)Math.Ceiling(c.length / LapTrace.Bin) + 1,
                "the tape lasts " + tape.Duration.ToString("F2") + " s for a lap of " + a.lapTime.ToString("F2") + " s, " + a.Marks + " marks for " + c.length.ToString("F0") + " m", "within a fifth of a second");
        }

        // ---- a ghost fits only its own circuit and bike
        {
            var c = CircuitGen.Generate(4242);
            var tr = new LapTrace(c.length); tr.Seal(80f);
            var tape = new PoseTape(); for (int i = 0; i < 20; i++) tape.Add(SampleFrame(i));
            var g = new Ghost { seed = 4242, bikeId = "argo1000", kind = 1, lapTime = 80f, circuitLength = c.length, trace = tr, tape = tape };
            Check("TIMING_GHOST_FITS_ONLY_ITS_CIRCUIT", g.Fits(4242, "argo1000", c.length) && !g.Fits(4243, "argo1000", c.length) && !g.Fits(4242, "argo600", c.length) && !g.Fits(4242, "argo1000", c.length + 1f),
                "same seed, bike and length fits; another seed, another bike or a circuit a metre longer does not", "fits only the same circuit and bike");
        }

        // ---- the named circuits
        {
            var seen = new HashSet<int>(); var names = new HashSet<string>();
            bool unique = true, pinned = true, ranged = true; string off = "";
            float lo = 1e9f, hi = 0f, hillLo = 1e9f, hillHi = 0f; int fast = 0, technical = 0, hilly = 0;
            for (int i = 0; i < Catalogue.Count; i++)
            {
                var e = Catalogue.All[i];
                var c = CircuitGen.Generate(e.seed);
                var ch = Describe(c);
                unique &= seen.Add(e.seed) && names.Add(c.name);
                if (Math.Abs(c.length - e.length) > 1.0f) { pinned = false; off += " [" + e.seed + ": " + c.length.ToString("F0") + " m, listed " + e.length + "]"; }
                ranged &= c.length > 2400f && c.length < 4300f && !string.IsNullOrEmpty(e.tag) && Catalogue.IndexOf(e.seed) == i;
                lo = Math.Min(lo, c.length); hi = Math.Max(hi, c.length); hillLo = Math.Min(hillLo, ch.hills); hillHi = Math.Max(hillHi, ch.hills);
                if (ch.longestStraight >= 640f) fast++;
                if (ch.tightCorners >= 15 || ch.hairpins >= 10) technical++;
                if (ch.hills >= 25f) hilly++;
            }
            Check("CATALOGUE_CIRCUITS_ARE_STILL_THE_SAME", pinned, pinned ? Catalogue.Count + " circuits, every one of the length listed" : "the generator moved:" + off, "every listed length within a metre (a record belongs to a circuit that does not change)");
            Check("CATALOGUE_IS_WELL_FORMED", unique && ranged && Catalogue.Count >= 10 && !Catalogue.Named(1), Catalogue.Count + " circuits, seeds and names all different: " + unique + ", lengths " + lo.ToString("F0") + "-" + hi.ToString("F0") + " m", "at least ten, all different, all a plausible length");
            Check("CATALOGUE_HAS_CHARACTER", hi - lo > 1300f && hillHi - hillLo > 15f && fast >= 3 && technical >= 3 && hilly >= 3,
                "lengths span " + (hi - lo).ToString("F0") + " m, hills " + hillLo.ToString("F0") + "-" + hillHi.ToString("F0") + " m, " + fast + " fast, " + technical + " technical, " + hilly + " hilly", "short and long, flat and hilly, at least three of each of fast, technical and hilly");
        }
    }

    static byte[] Slice(byte[] b, int len)
    {
        var o = new byte[len];
        Array.Copy(b, o, len);
        return o;
    }
}

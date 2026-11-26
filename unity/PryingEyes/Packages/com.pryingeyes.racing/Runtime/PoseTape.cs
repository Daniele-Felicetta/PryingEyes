using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Production.Racing
{
    /// <summary>One moment of one bike, everything the picture and the sound need (the same things the view of a bike holds, without an engine): where it is, how it leans, how fast its engine turns, which
    /// of its lights and effects are on.</summary>
    public struct PoseFrame
    {
        public float x, y, z, heading, lean, pitch, steer, hang, tuck, body, speed, rpm, spinR, spinF, forkC, shockC, throttle, slipF, slipR;
        public byte gear, crashKind, surface;
        public ushort flags;

        public const ushort Crashed = 1, Braking = 2, SlidingF = 4, SlidingR = 8, Scraping = 16, AbsOn = 32, TcsOn = 64, Shifting = 128, Limiter = 256;
        /// <summary>How many floats a frame has when it is written out.</summary>
        public const int Floats = 19;

        public bool Has(ushort flag) { return (flags & flag) != 0; }

        /// <summary>Between two frames: positions and angles are blended (the heading the short way round); the gear, the surface and the flags are those of the nearer frame.</summary>
        public static PoseFrame Lerp(in PoseFrame a, in PoseFrame b, float t)
        {
            var o = t < 0.5f ? a : b;
            o.x = a.x + (b.x - a.x) * t; o.y = a.y + (b.y - a.y) * t; o.z = a.z + (b.z - a.z) * t;
            o.heading = a.heading + RacingMath.WrapPi(b.heading - a.heading) * t;
            o.lean = a.lean + (b.lean - a.lean) * t; o.pitch = a.pitch + (b.pitch - a.pitch) * t; o.steer = a.steer + (b.steer - a.steer) * t;
            o.hang = a.hang + (b.hang - a.hang) * t; o.tuck = a.tuck + (b.tuck - a.tuck) * t; o.body = a.body + (b.body - a.body) * t;
            o.speed = a.speed + (b.speed - a.speed) * t; o.rpm = a.rpm + (b.rpm - a.rpm) * t;
            o.spinR = a.spinR + (b.spinR - a.spinR) * t; o.spinF = a.spinF + (b.spinF - a.spinF) * t;
            o.forkC = a.forkC + (b.forkC - a.forkC) * t; o.shockC = a.shockC + (b.shockC - a.shockC) * t;
            o.throttle = a.throttle + (b.throttle - a.throttle) * t; o.slipF = a.slipF + (b.slipF - a.slipF) * t; o.slipR = a.slipR + (b.slipR - a.slipR) * t;
            return o;
        }

        internal void Write(BinaryWriter w)
        {
            w.Write(x); w.Write(y); w.Write(z); w.Write(heading); w.Write(lean); w.Write(pitch); w.Write(steer); w.Write(hang); w.Write(tuck); w.Write(body);
            w.Write(speed); w.Write(rpm); w.Write(spinR); w.Write(spinF); w.Write(forkC); w.Write(shockC); w.Write(throttle); w.Write(slipF); w.Write(slipR);
            w.Write(gear); w.Write(crashKind); w.Write(surface); w.Write(flags);
        }

        internal static PoseFrame Read(BinaryReader r)
        {
            var f = new PoseFrame();
            f.x = r.ReadSingle(); f.y = r.ReadSingle(); f.z = r.ReadSingle(); f.heading = r.ReadSingle(); f.lean = r.ReadSingle(); f.pitch = r.ReadSingle(); f.steer = r.ReadSingle();
            f.hang = r.ReadSingle(); f.tuck = r.ReadSingle(); f.body = r.ReadSingle(); f.speed = r.ReadSingle(); f.rpm = r.ReadSingle(); f.spinR = r.ReadSingle(); f.spinF = r.ReadSingle();
            f.forkC = r.ReadSingle(); f.shockC = r.ReadSingle(); f.throttle = r.ReadSingle(); f.slipF = r.ReadSingle(); f.slipR = r.ReadSingle();
            f.gear = r.ReadByte(); f.crashKind = r.ReadByte(); f.surface = r.ReadByte(); f.flags = r.ReadUInt16();
            return f;
        }

        public bool Finite()
        {
            return !(float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) || float.IsNaN(z) || float.IsInfinity(z) || float.IsNaN(heading) || float.IsInfinity(heading));
        }
    }

    /// <summary>A recording of one bike: a frame every 1/20 s, kept in memory for the replay of a race and, for one lap, written to a file as a ghost. Frame k is the bike at k/20 s from the start of the tape.</summary>
    public sealed class PoseTape
    {
        public const float Rate = 20f, FrameTime = 1f / Rate;
        /// <summary>A tape stops growing here (15 minutes): a very long race replays its first quarter of an hour.</summary>
        public const int MaxFrames = 18000;

        readonly List<PoseFrame> frames = new List<PoseFrame>();

        public int Count { get { return frames.Count; } }
        public float Duration { get { return frames.Count < 2 ? 0f : (frames.Count - 1) * FrameTime; } }
        public PoseFrame this[int i] { get { return frames[i]; } }
        public bool Full { get { return frames.Count >= MaxFrames; } }

        public void Clear() { frames.Clear(); }

        public bool Add(in PoseFrame f)
        {
            if (frames.Count >= MaxFrames) return false;
            frames.Add(f);
            return true;
        }

        /// <summary>The bike 't' seconds into the tape (before the start: the first frame; after the end: the last).</summary>
        public PoseFrame At(float t)
        {
            if (frames.Count == 0) return default(PoseFrame);
            if (!(t > 0f)) return frames[0];
            float k = t * Rate;
            int i = (int)k;
            if (i >= frames.Count - 1) return frames[frames.Count - 1];
            return PoseFrame.Lerp(frames[i], frames[i + 1], k - i);
        }

        /// <summary>A copy of the frames from the one at 'from' (inclusive) to the one at 'to' (exclusive).</summary>
        public PoseTape Slice(int from, int to)
        {
            var t = new PoseTape();
            from = Math.Max(0, from); to = Math.Min(frames.Count, to);
            for (int i = from; i < to; i++) t.frames.Add(frames[i]);
            return t;
        }

        // ------------------------------------------------------------------ the file
        public void Write(BinaryWriter w)
        {
            w.Write(frames.Count);
            for (int i = 0; i < frames.Count; i++) frames[i].Write(w);
        }

        public static PoseTape Read(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > MaxFrames) throw new InvalidDataException("tape length");
            var t = new PoseTape();
            for (int i = 0; i < n; i++)
            {
                var f = PoseFrame.Read(r);
                if (!f.Finite()) throw new InvalidDataException("tape frame");
                t.frames.Add(f);
            }
            return t;
        }
    }

    /// <summary>How long a lap took to get to each ten metres of the track: the clock of a lap against the distance, so that the lap being ridden can be compared with a lap ridden before at every point
    /// ("two tenths up"). Fed with the distance and the time of every step, it fills the ten-metre marks it passes by interpolating.</summary>
    public sealed class LapTrace
    {
        public const float Bin = 10f;
        public readonly float length;
        /// <summary>The time (s from the start of the lap) at each mark; negative where the lap has not been yet.</summary>
        public readonly float[] t;
        float lastD, lastT;
        public bool Complete { get; private set; }
        public float lapTime;

        public LapTrace(float length)
        {
            this.length = length;
            int n = (int)Math.Ceiling(length / Bin) + 1;
            t = new float[n];
            for (int i = 0; i < n; i++) t[i] = -1f;
            t[0] = 0f;
        }

        public int Marks { get { return t.Length; } }

        /// <summary>The bike crosses the line at 'time' on the clock of this lap (0 for a flying lap; for the first lap of a race, the seconds since the lights went out).</summary>
        public void Start(float time) { t[0] = time; lastT = time; lastD = 0f; }

        /// <summary>The bike is 'dist' metres into the lap at 'time' seconds into the lap.</summary>
        public void Feed(float dist, float time)
        {
            if (Complete || !(dist > lastD) || !(time >= lastT)) return;
            int k0 = (int)(lastD / Bin) + 1, k1 = (int)(Math.Min(dist, length) / Bin);
            for (int k = k0; k <= k1 && k < t.Length; k++)
            {
                float d = k * Bin;
                float f = (d - lastD) / (dist - lastD);
                t[k] = lastT + (time - lastT) * f;
            }
            lastD = dist; lastT = time;
        }

        /// <summary>The lap is over after 'time' seconds: the last mark is the line.</summary>
        public void Seal(float time)
        {
            lapTime = time; Complete = true;
            // marks never reached (a gap of a step across a mark can leave none; a lap with a wrong way in it can) take the nearest known time before them
            float prev = 0f;
            for (int k = 0; k < t.Length; k++) { if (t[k] < 0f) t[k] = k == t.Length - 1 ? time : prev; prev = t[k]; }
            t[t.Length - 1] = time;
        }

        /// <summary>The time at 'dist' metres (blending the two marks around it); negative when the lap never got there.</summary>
        public float TimeAt(float dist)
        {
            if (dist <= 0f) return 0f;
            float k = Math.Min(dist, length) / Bin;
            int i = (int)k;
            if (i >= t.Length - 1) return t[t.Length - 1];
            if (t[i] < 0f || t[i + 1] < 0f) return -1f;
            return t[i] + (t[i + 1] - t[i]) * (k - i);
        }

        /// <summary>How far ahead of (negative) or behind (positive) the reference lap the bike is: the time now minus the reference's time at this distance. False when the reference has no time there.</summary>
        public static bool Delta(LapTrace reference, float dist, float time, out float delta)
        {
            delta = 0f;
            if (reference == null || !reference.Complete) return false;
            float r = reference.TimeAt(dist);
            if (r < 0f) return false;
            delta = time - r;
            return true;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(length); w.Write(lapTime); w.Write(t.Length);
            for (int i = 0; i < t.Length; i++) w.Write(t[i]);
        }

        public static LapTrace Read(BinaryReader r)
        {
            float length = r.ReadSingle(), lapTime = r.ReadSingle();
            int n = r.ReadInt32();
            if (!(length > 100f && length < 20000f) || n != (int)Math.Ceiling(length / Bin) + 1) throw new InvalidDataException("trace length");
            var tr = new LapTrace(length);
            for (int i = 0; i < n; i++) tr.t[i] = r.ReadSingle();
            tr.lapTime = lapTime; tr.Complete = true;
            return tr;
        }
    }

    /// <summary>The best lap of a bike on a circuit, kept as a ghost: how long it took (the standing-start lap and a flying lap are different things and are kept apart), the clock against the distance,
    /// and the recording of the bike. A ghost is only good on the circuit it was made on, so it carries the seed and the length of that circuit and is refused on any other.</summary>
    public sealed class Ghost
    {
        public const int Version = 1;
        public int seed;
        public string bikeId = "";
        /// <summary>0: the lap with the standing start (the first of a race), 1: a flying lap.</summary>
        public int kind;
        public float lapTime;
        public float circuitLength;
        public LapTrace trace;
        public PoseTape tape;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(0x48474550);              // the signature of a ghost file: it must not change, or the ghosts that earlier versions saved would not load
                w.Write(Version);
                w.Write(seed); w.Write(bikeId ?? ""); w.Write((byte)kind); w.Write(lapTime); w.Write(circuitLength);
                trace.Write(w); tape.Write(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>A ghost from its bytes; false for anything that is not a good ghost of this version (a damaged file must never stop the game).</summary>
        public static bool TryFromBytes(byte[] bytes, out Ghost g)
        {
            g = null;
            if (bytes == null || bytes.Length < 40) return false;
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (r.ReadInt32() != 0x48474550 || r.ReadInt32() != Version) return false;
                    var o = new Ghost { seed = r.ReadInt32(), bikeId = r.ReadString(), kind = r.ReadByte(), lapTime = r.ReadSingle(), circuitLength = r.ReadSingle() };
                    o.trace = LapTrace.Read(r); o.tape = PoseTape.Read(r);
                    if (!(o.lapTime > 5f && o.lapTime < 1200f) || o.tape.Count < 10 || o.kind < 0 || o.kind > 1) return false;
                    g = o;
                    return true;
                }
            }
            catch (Exception) { g = null; return false; }
        }

        /// <summary>Is this ghost of this circuit and bike (same seed, a length that matches to a centimetre)?</summary>
        public bool Fits(int seed, string bikeId, float circuitLength)
        {
            return this.seed == seed && this.bikeId == bikeId && Math.Abs(this.circuitLength - circuitLength) < 0.01f && trace != null && Math.Abs(trace.length - circuitLength) < 0.01f;
        }
    }
}

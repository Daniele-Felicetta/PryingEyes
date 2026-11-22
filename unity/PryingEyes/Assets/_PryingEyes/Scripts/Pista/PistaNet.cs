using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>How a race goes over the network (cooperative game): the shape of a bike's pose, the roster of the riders and the results, written with the NetWriter/NetReader of the cooperative game.
    /// A pose is about 35 bytes (position as it is, the angles in 16 bits, the flags in two bytes), so every rider of a full grid fits in one unreliable packet (19 x 35 = 665 bytes, the limit is 900).
    /// The wheel speed is not sent: the receiver works it out from the ground speed. Whatever arrives is checked by NetReader (a short or broken message reads as zeros and says it is not Ok).</summary>
    public static class PistaNet
    {
        const float TwoPi = Mathf.PI * 2f;
        const float WheelRadius = 0.31f;

        static void S16(NetWriter w, float v, float scale) { w.U16(Mathf.Clamp(Mathf.RoundToInt(v * scale), -32767, 32767)); }
        static float S16(ref NetReader r, float scale) { return (short)r.U16() / scale; }

        /// <summary>One rider's pose: the racer's id, then the bike.</summary>
        public static void WritePose(NetWriter w, int id, in BikeView v)
        {
            w.U8(id);
            w.F32(v.x).F32(v.y).F32(v.z);
            w.U16(Mathf.RoundToInt(Mathf.Repeat(v.heading, TwoPi) / TwoPi * 65535f));
            S16(w, v.lean, 10000f); S16(w, v.pitch, 10000f); S16(w, v.steer, 10000f); S16(w, v.hang, 10000f);
            S16(w, v.speed, 100f);
            w.U16(Mathf.Clamp(Mathf.RoundToInt(v.rpm), 0, 65535));
            w.U8(Mathf.Clamp(Mathf.RoundToInt(v.tuck * 255f), 0, 255)).U8(Mathf.Clamp(Mathf.RoundToInt(v.throttle * 255f), 0, 255)).U8(Mathf.Clamp(v.gear, 0, 15));
            int flags = (v.crashed ? 1 : 0) | (v.braking ? 2 : 0) | (v.slidingF ? 4 : 0) | (v.slidingR ? 8 : 0) | (v.scraping ? 16 : 0) | (v.absOn ? 32 : 0) | (v.tcsOn ? 64 : 0) | (v.shifting ? 128 : 0) | (v.limiter ? 256 : 0);
            w.U16(flags);
            w.U8(((int)v.crashKind & 15) | (((int)v.surface & 15) << 4));
            S16(w, v.forkC, 1000f);
        }

        /// <summary>Read one rider's pose. False when the message was short.</summary>
        public static bool ReadPose(ref NetReader r, out int id, out BikeView v)
        {
            v = new BikeView();
            id = r.U8();
            v.x = r.F32(); v.y = r.F32(); v.z = r.F32();
            v.heading = r.U16() / 65535f * TwoPi;
            v.lean = S16(ref r, 10000f); v.pitch = S16(ref r, 10000f); v.steer = S16(ref r, 10000f); v.hang = S16(ref r, 10000f);
            v.speed = S16(ref r, 100f);
            v.rpm = r.U16();
            v.tuck = r.U8() / 255f; v.throttle = r.U8() / 255f; v.gear = r.U8();
            int flags = r.U16();
            v.crashed = (flags & 1) != 0; v.braking = (flags & 2) != 0; v.slidingF = (flags & 4) != 0; v.slidingR = (flags & 8) != 0; v.scraping = (flags & 16) != 0;
            v.absOn = (flags & 32) != 0; v.tcsOn = (flags & 64) != 0; v.shifting = (flags & 128) != 0; v.limiter = (flags & 256) != 0;
            int kinds = r.U8();
            v.crashKind = (CrashKind)(kinds & 15); v.surface = (SurfaceKind)(kinds >> 4);
            v.forkC = S16(ref r, 1000f);
            v.spinR = v.spinF = Mathf.Abs(v.speed) / WheelRadius;
            if (!r.Ok) return false;
            // a pose that cannot be a bike on a circuit (a broken or hostile message) is thrown away
            return Mathf.Abs(v.x) < 20000f && Mathf.Abs(v.z) < 20000f && Mathf.Abs(v.y) < 5000f;
        }

        // ------------------------------------------------------------------ the roster
        public struct Entry
        {
            public int id, kind, netSlot, bike, skin, grid, flags;
            public string name;
            public bool Dnf => (flags & 1) != 0;
        }

        /// <summary>The rules of the race (laps, bike, aids, hour of the day, weather) and who rides it. 'kind' 0 is a person (netSlot says which), 1 a computer rider; 'flags' bit 0 is a friend who has left.</summary>
        public static void WriteRoster(NetWriter w, int epoch, int seed, int laps, int bike, int aids, int hour, int weather, List<Racer> racers)
        {
            w.U16(epoch).I32(seed).U8(laps).U8(bike).U8(aids).U8(hour).U8(weather).U8(racers.Count);
            for (int i = 0; i < racers.Count; i++)
            {
                var r = racers[i];
                w.U8(r.id).U8(r.cpu ? 1 : 0).U8(r.netSlot < 0 ? 255 : r.netSlot).U8(r.bikeIndex).U8(r.skin).U8(r.slot).U8(r.dnf ? 1 : 0).Str(r.name);
            }
        }

        public static bool ReadRoster(ref NetReader r, out int epoch, out int seed, out int laps, out int bike, out int aids, out int hour, out int weather, List<Entry> into)
        {
            into.Clear();
            epoch = r.U16(); seed = r.I32(); laps = r.U8(); bike = r.U8(); aids = r.U8(); hour = r.U8(); weather = r.U8();
            int n = r.U8();
            if (n > 32) return false;
            for (int i = 0; i < n; i++)
            {
                var e = new Entry { id = r.U8(), kind = r.U8(), netSlot = r.U8(), bike = r.U8(), skin = r.U8(), grid = r.U8(), flags = r.U8(), name = r.Str() };
                if (e.netSlot == 255) e.netSlot = -1;
                into.Add(e);
            }
            if (!r.Ok) return false;
            // ids are the places in the list (the race hands them out in order): a roster out of order would put the wrong pose on the wrong bike
            for (int i = 0; i < into.Count; i++) if (into[i].id != i) return false;
            return laps >= 1 && laps <= 20;
        }

        // ------------------------------------------------------------------ the results
        public static void WriteResults(NetWriter w, int epoch, List<Race.Result> list)
        {
            w.U16(epoch).U8(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                w.U8(e.id).U8((e.finished ? 1 : 0) | (e.dnf ? 2 : 0)).U8(e.finishOrder).U8(e.crashes).U8(e.place).F32(e.finishTime).F32(e.bestLap);
            }
        }

        public static bool ReadResults(ref NetReader r, out int epoch, List<Race.Result> into)
        {
            into.Clear();
            epoch = r.U16();
            int n = r.U8();
            if (n > 32) return false;
            for (int i = 0; i < n; i++)
            {
                int id = r.U8(), flags = r.U8(), order = r.U8(), crashes = r.U8(), place = r.U8();
                float ft = r.F32(), bl = r.F32();
                into.Add(new Race.Result { id = id, finished = (flags & 1) != 0, dnf = (flags & 2) != 0, finishOrder = order, crashes = crashes, place = place, finishTime = ft, bestLap = bl });
            }
            return r.Ok;
        }
    }
}

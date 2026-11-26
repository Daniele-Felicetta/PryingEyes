using System;
using System.Collections.Generic;
using System.Linq;

namespace PryingEyes.Multiplayer
{
    /// <summary>Host admission with monotonic seconds supplied by the caller. Each challenge gets exactly one reply.</summary>
    public sealed class AdmissionGate
    {
        readonly string revision;
        readonly double timeout;
        readonly Dictionary<int, double> pending = new Dictionary<int, double>();
        public SessionRoster Roster { get; }

        public AdmissionGate(int capacity, string revision, double timeoutSeconds)
        {
            SessionAdmission.Create(1, revision);
            if (!Finite(timeoutSeconds) || timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            Roster = new SessionRoster(capacity);
            this.revision = revision; timeout = timeoutSeconds;
        }

        public bool Begin(int peer, double now)
        {
            CheckTime(now);
            if (peer < 0) throw new ArgumentOutOfRangeException(nameof(peer));
            if (pending.ContainsKey(peer) || Roster.Owns(peer, new PlayerSeat(peer, 0))) return false;
            double deadline = now + timeout;
            if (!Finite(deadline)) throw new ArgumentOutOfRangeException(nameof(now));
            pending.Add(peer, deadline);
            return true;
        }

        public bool TryAdmit(int peer, byte[] token, double now)
        {
            CheckTime(now);
            if (!pending.TryGetValue(peer, out double deadline)) return false;
            pending.Remove(peer);
            // Validate here, not just in a periodic expiry sweep: a delayed frame must not extend the deadline.
            return now < deadline && SessionAdmission.TryRead(token, revision, out int seats) && Roster.TryJoin(peer, seats);
        }

        public int[] Expire(double now)
        {
            CheckTime(now);
            if (pending.Count == 0) return Array.Empty<int>();
            var expired = pending.Where(item => item.Value <= now).Select(item => item.Key).ToArray();
            foreach (int peer in expired) pending.Remove(peer);
            return expired;
        }

        public void Leave(int peer) { pending.Remove(peer); Roster.Leave(peer); }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static void CheckTime(double now)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        }
    }
}

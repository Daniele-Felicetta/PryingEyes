using System;
using System.Collections.Generic;
using System.Linq;

namespace PryingEyes.Multiplayer
{
    /// <summary>A peer is a connection; a seat is a human. Seat indices never depend on total roster size.</summary>
    public readonly struct PlayerSeat : IEquatable<PlayerSeat>
    {
        public readonly int Peer;
        public readonly int LocalIndex;
        public PlayerSeat(int peer, int localIndex)
        {
            if (peer < 0) throw new ArgumentOutOfRangeException(nameof(peer));
            if (localIndex < 0 || localIndex >= SessionRoster.MaxLocalPlayers)
                throw new ArgumentOutOfRangeException(nameof(localIndex));
            Peer = peer;
            LocalIndex = localIndex;
        }
        public bool Equals(PlayerSeat other) => Peer == other.Peer && LocalIndex == other.LocalIndex;
        public override bool Equals(object obj) => obj is PlayerSeat other && Equals(other);
        public override int GetHashCode() => unchecked(Peer * 397 ^ LocalIndex);
        public override string ToString() => Peer + ":" + LocalIndex;
    }

    /// <summary>Host-only admission and floor barrier. Not a transport or a source of client identity.</summary>
    public sealed class SessionRoster
    {
        public const int MaxLocalPlayers = 4;
        readonly int capacity;
        readonly Dictionary<int, int> peers = new Dictionary<int, int>();
        readonly HashSet<int> ready = new HashSet<int>();
        public int Epoch { get; private set; }
        public string FloorDigest { get; private set; }
        public int PlayerCount => peers.Values.Sum();
        public bool AllReady => FloorDigest != null && peers.Count > 0 && ready.Count == peers.Count;
        public int[] WaitingPeers => peers.Keys.Where(peer => !ready.Contains(peer)).ToArray();
        public PlayerSeat[] Seats => peers.OrderBy(p => p.Key)
            .SelectMany(p => Enumerable.Range(0, p.Value).Select(i => new PlayerSeat(p.Key, i))).ToArray();

        public SessionRoster(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }

        public bool TryJoin(int peer, int seats)
        {
            if (peer < 0 || seats < 1 || seats > MaxLocalPlayers) return false;
            if (peers.TryGetValue(peer, out int existing)) return existing == seats;
            if (seats > capacity - PlayerCount) return false;
            peers.Add(peer, seats);
            // Late joins close the barrier until their current geometry is acknowledged.
            ready.Remove(peer);
            return true;
        }

        public void Leave(int peer) { peers.Remove(peer); ready.Remove(peer); }
        public bool Owns(int authenticatedPeer, PlayerSeat seat) => authenticatedPeer == seat.Peer &&
            peers.TryGetValue(authenticatedPeer, out int count) && seat.LocalIndex < count;

        public int BeginFloor(string digest)
        {
            if (string.IsNullOrWhiteSpace(digest)) throw new ArgumentException("Missing geometry digest.", nameof(digest));
            Epoch = checked(Epoch + 1);
            FloorDigest = digest;
            ready.Clear();
            return Epoch;
        }

        public bool Acknowledge(int authenticatedPeer, int epoch, string digest)
        {
            if (!peers.ContainsKey(authenticatedPeer) || epoch != Epoch || FloorDigest == null ||
                !string.Equals(digest, FloorDigest, StringComparison.Ordinal)) return false;
            ready.Add(authenticatedPeer);
            return true;
        }
    }
}

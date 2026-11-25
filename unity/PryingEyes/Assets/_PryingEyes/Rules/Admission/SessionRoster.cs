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

    /// <summary>Host-only admission. Not a transport or a source of client identity.</summary>
    public sealed class SessionRoster
    {
        public const int MaxLocalPlayers = 4;
        readonly int capacity;
        readonly Dictionary<int, int> peers = new Dictionary<int, int>();
        public int PlayerCount => peers.Values.Sum();
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
            return true;
        }

        public void Leave(int peer) { peers.Remove(peer); }
        public bool Owns(int authenticatedPeer, PlayerSeat seat) => authenticatedPeer == seat.Peer &&
            peers.TryGetValue(authenticatedPeer, out int count) && seat.LocalIndex < count;
    }
}

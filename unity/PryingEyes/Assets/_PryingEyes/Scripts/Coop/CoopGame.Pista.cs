using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>The cooperative race (moto su pista, Scripts/Pista): the director of the race (PistaDirector.Coop.cs) talks to the network only through these few calls, and what travels is in PistaNet.
    /// The host rides the computer riders and gives the go; every friend rides their own bike here and sends its pose; the host relays the poses of everybody to everybody. The epoch is the number of
    /// the race in the session: it goes up at every race (the same circuit again or a new one) and a message of another epoch is thrown away.</summary>
    public sealed partial class CoopGame
    {
        /// <summary>The epoch of this race, on 16 bits (as FloorReady and the roster carry it).</summary>
        public int PistaEpoch => Epoch & 0xFFFF;

        /// <summary>A message of the race to the host (from a friend's machine).</summary>
        internal void PistaSendHost(CoopKind kind, byte[] data, bool reliable)
        {
            if (net != null && !host) net.SendToHost(kind, data, reliable);
        }

        /// <summary>A message of the race from the host to one friend.</summary>
        internal void PistaSendSlot(int slot, CoopKind kind, byte[] data, bool reliable)
        {
            if (net != null && host) net.SendToSlot(slot, kind, data, reliable);
        }

        /// <summary>The host: the friends of this session who have been welcomed (ready or not to race).</summary>
        internal void PistaFriends(List<int> into)
        {
            into.Clear();
            if (net == null || !host) return;
            foreach (int s in net.RemoteSlots) if (names.ContainsKey(s) && !pendingWelcome.Contains(s)) into.Add(s);
        }

        /// <summary>The host: this friend has built the scene of the current race (and is waiting for the roster).</summary>
        internal bool PistaReady(int slot) { return host && IsReady(slot); }

        /// <summary>The host starts another race, the same circuit again or a new one: a new epoch, and the friends follow (FloorChange carries the circuit).</summary>
        public void PistaNewRace(int seed)
        {
            builtLocal = false; readySent = false;
            if (!host) return;
            var fd = FloorDirector.I;
            if (fd != null) fd.runSeed = seed;
            AnnounceFloor(0);
        }

        /// <summary>The host: a friend who has been welcomed but is not in the race any more is told about it by the director; here the welcome list is kept clean.</summary>
        internal bool PistaKnows(int slot) { return names.ContainsKey(slot); }
    }
}

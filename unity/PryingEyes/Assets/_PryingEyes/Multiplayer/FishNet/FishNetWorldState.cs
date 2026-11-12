#if PRYINGEYES_FISHNET
using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend
{
    public struct FloorDescriptor
    {
        public int Epoch, RunSeed, Floor;
        public bool UseGrid;
        public string Digest;
    }

    /// <summary>Persistent snapshot for late join; acknowledgements use the authenticated RPC sender.</summary>
    public sealed class FishNetWorldState : NetworkBehaviour
    {
        readonly SyncVar<FloorDescriptor> descriptor = new SyncVar<FloorDescriptor>();
        readonly SyncVar<bool> movementEnabled = new SyncVar<bool>();
        public FloorDescriptor Descriptor => descriptor.Value;
        public bool MovementEnabled => movementEnabled.Value;
        public bool LocalReady { get; private set; }
        public string Failure { get; private set; }
        public event Action<Plan, int, string> BuildRequested;
        /// <summary>Validated, fresh intent on the authoritative tick. Gameplay modules subscribe without owning transport.</summary>
        public event Action<FishNetSeatAvatar, SeatCommand, float> ServerInput;
        // Exactly one scene builder attaches this callback. It reserves and places an authoritative avatar.
        public Func<FishNetSeatAvatar, bool> PlaceAvatar;
        public bool useGrid = true;
        internal void DispatchInput(FishNetSeatAvatar avatar, SeatCommand input, float dt)
        {
            if (!IsServerInitialized || !LocalReady || !session.Roster.AllReady || input.Epoch != Descriptor.Epoch) return;
            ServerInput?.Invoke(avatar, input, dt);
        }
        FishNetSessionLauncher session;
        int observedEpoch;
        bool acknowledgementSent;
        string generatedDigest;
        [Min(1)] public float floorTimeoutSeconds = 45;
        readonly Dictionary<int, double> deadlines = new Dictionary<int, double>();

        public override void OnStartNetwork()
        {
            session = NetworkManager.GetComponent<FishNetSessionLauncher>();
            session.World = this;
        }
        public override void OnStopNetwork()
        {
            if (session != null && session.World == this) session.World = null;
        }

        public void BeginFloor(int seed, int floor)
        {
            if (!IsServerInitialized) throw new InvalidOperationException("Only host can change the floor.");
            string digest = NetworkFloorPlan.Digest(NetworkFloorPlan.Generate(seed, floor, useGrid));
            int epoch = session.Roster.BeginFloor(digest);
            deadlines.Clear();
            LocalReady = false; movementEnabled.Value = false;
            descriptor.Value = new FloorDescriptor { Epoch = epoch, RunSeed = seed, Floor = floor, UseGrid = useGrid, Digest = digest };
        }

        void Update()
        {
            if (!IsServerInitialized && !IsClientInitialized) return;
            var floor = Descriptor;
            if (floor.Epoch > 0 && floor.Epoch != observedEpoch && BuildRequested != null)
            {
                observedEpoch = floor.Epoch;
                LocalReady = false; acknowledgementSent = false; Failure = null;
                try
                {
                    var plan = NetworkFloorPlan.Generate(floor.RunSeed, floor.Floor, floor.UseGrid);
                    generatedDigest = NetworkFloorPlan.Digest(plan);
                    if (generatedDigest != floor.Digest) throw new InvalidOperationException("Procedural plan mismatch.");
                    BuildRequested.Invoke(plan, floor.Epoch, generatedDigest);
                }
                catch (Exception error) { Failure = error.Message; Debug.LogException(error, this); }
            }
            if (LocalReady && !acknowledgementSent && IsClientInitialized && NetworkManager.ClientManager.Connection.IsAuthenticated)
            {
                if (IsServerInitialized)
                    acknowledgementSent = AcknowledgePeer(NetworkManager.ClientManager.Connection.ClientId, floor.Epoch, generatedDigest);
                else { FloorReadyServerRpc(floor.Epoch, generatedDigest); acknowledgementSent = true; }
            }
            if (IsServerInitialized)
            {
                CheckReadinessTimeouts();
                movementEnabled.Value = LocalReady && Failure == null && session.Roster.AllReady;
            }
        }

        void CheckReadinessTimeouts()
        {
            if (Descriptor.Epoch < 1) return;
            var pending = new HashSet<int>(session.Roster.WaitingPeers);
            foreach (int previous in new List<int>(deadlines.Keys))
                if (!pending.Contains(previous)) deadlines.Remove(previous);
            double now = Time.realtimeSinceStartupAsDouble;
            float timeout = float.IsNaN(floorTimeoutSeconds) || float.IsInfinity(floorTimeoutSeconds)
                ? 45 : Mathf.Max(1, floorTimeoutSeconds);
            foreach (int peer in pending)
            {
                if (!deadlines.TryGetValue(peer, out double deadline)) { deadlines.Add(peer, now + timeout); continue; }
                if (now < deadline) continue;
                var local = NetworkManager.ClientManager.Connection;
                if (local.IsAuthenticated && local.ClientId == peer)
                    ReportBuildFailure("Host did not finish the procedural floor before the readiness timeout.");
                else if (NetworkManager.ServerManager.Clients.TryGetValue(peer, out var remote)) remote.Disconnect(true);
            }
        }

        public void NotifyBuilt(int epoch, string digest)
        {
            if (Failure != null || epoch != observedEpoch || epoch != Descriptor.Epoch || digest != generatedDigest)
                throw new InvalidOperationException("Cannot acknowledge a stale or unbuilt floor.");
            LocalReady = true;
        }
        public void InvalidateLocalBuild()
        {
            LocalReady = false; acknowledgementSent = false; observedEpoch = 0;
        }
        public void ReportBuildFailure(string message) { LocalReady = false; Failure = message; }

        bool AcknowledgePeer(int peer, int epoch, string digest)
        {
            // RPC dispatch can precede this MonoBehaviour's Update. A late acknowledgement cannot revive an expired peer.
            if (deadlines.TryGetValue(peer, out double deadline) && Time.realtimeSinceStartupAsDouble >= deadline) return false;
            return session.Roster.Acknowledge(peer, epoch, digest);
        }

        [ServerRpc(RequireOwnership = false)]
        void FloorReadyServerRpc(int epoch, string digest, NetworkConnection sender = null)
        {
            if (sender != null) AcknowledgePeer(sender.ClientId, epoch, digest);
        }
    }
}
#endif

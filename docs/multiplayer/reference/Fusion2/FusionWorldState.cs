#if PRYINGEYES_FUSION2
using System;
using Fusion;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Multiplayer.Fusion2
{
    /// <summary>Persistent floor descriptor supports late joins; RPCs only acknowledge a specific built floor.</summary>
    public sealed class FusionWorldState : NetworkBehaviour
    {
        [Networked] public int RunSeed { get; private set; }
        [Networked] public int Floor { get; private set; }
        [Networked] public int Epoch { get; private set; }
        [Networked] public NetworkString<_64> GeometryDigest { get; private set; }
        [Networked] public NetworkBool MovementEnabled { get; private set; }
        public event Action<Plan, int, string> BuildRequested;
        public string LocalFailure { get; private set; }
        FusionSessionLauncher session;
        int observedEpoch;
        string generatedDigest;

        public override void Spawned()
        {
            session = Runner.GetComponent<FusionSessionLauncher>();
            session.World = this;
        }

        // Host gameplay adapter calls once per transition. Must pause enemies/interactions as well as movement.
        public void BeginFloor(int seed, int floor)
        {
            if (!HasStateAuthority) throw new InvalidOperationException("Only host can change floors.");
            string digest = NetworkFloorPlan.Digest(NetworkFloorPlan.Generate(seed, floor));
            RunSeed = seed; Floor = floor; GeometryDigest = digest;
            Epoch = session.Roster.BeginFloor(digest);
            MovementEnabled = false;
        }

        public override void FixedUpdateNetwork()
        {
            if (HasStateAuthority) MovementEnabled = session.Roster.AllReady;
        }

        public override void Render()
        {
            if (Epoch < 1 || observedEpoch == Epoch || BuildRequested == null) return;
            observedEpoch = Epoch;
            LocalFailure = null;
            try
            {
                var plan = NetworkFloorPlan.Generate(RunSeed, Floor);
                generatedDigest = NetworkFloorPlan.Digest(plan);
                if (generatedDigest != GeometryDigest.ToString())
                    throw new InvalidOperationException("Procedural geometry mismatch; session remains blocked.");
                BuildRequested.Invoke(plan, observedEpoch, generatedDigest);
            }
            catch (Exception error)
            {
                LocalFailure = error.Message;
                Debug.LogException(error, this);
            }
        }

        // Build subscriber calls ONLY after geometry/colliders and local spawn positions are ready.
        // For async builds retain the epoch and digest passed to BuildRequested; stale completion is ignored.
        public void NotifyBuilt(int epoch, string digest)
        {
            if (LocalFailure != null || epoch != observedEpoch || epoch != Epoch || digest != generatedDigest)
                throw new InvalidOperationException("Cannot acknowledge an unbuilt or stale floor.");
            if (HasStateAuthority)
                session.Roster.Acknowledge(Runner.LocalPlayer.RawEncoded, epoch, digest);
            else RPC_FloorReady(epoch, digest);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        void RPC_FloorReady(int epoch, string digest, RpcInfo info = default)
        {
            // Source is transport-authenticated; no caller-provided peer ID.
            session.Roster.Acknowledge(info.Source.RawEncoded, epoch, digest);
        }
    }
}
#endif

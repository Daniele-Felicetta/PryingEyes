#if PRYINGEYES_FUSION2
using System;
using Fusion;
using UnityEngine;

namespace PryingEyes.Multiplayer.Fusion2
{
    /// <summary>Isolated movement probe, not a replacement for PlayerController. Add NetworkTransform to prefab.</summary>
    [RequireComponent(typeof(CharacterController), typeof(LocalSeatView))]
    public sealed class FusionSeatAvatar : NetworkBehaviour
    {
        [Networked] public int SeatIndex { get; set; }
        [Networked] public int PlacedEpoch { get; private set; }
        public float speed = 4f, turnSpeed = 120f;
        public event Action<PlayerSeat> FireHeld;
        CharacterController body;
        FusionSessionLauncher session;

        public override void Spawned()
        {
            body = GetComponent<CharacterController>();
            session = Runner.GetComponent<FusionSessionLauncher>();
            GetComponent<LocalSeatView>().Bind(Object.HasInputAuthority, SeatIndex, session.localPlayers);
            // Remote collision/movement is supplied by NetworkTransform; only the host simulates this probe.
            body.enabled = HasStateAuthority;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || session.World == null) return;
            var floor = session.World.GetComponent<FusionProceduralFloor>();
            if (floor == null || !floor.isActiveAndEnabled || floor.Failure != null || floor.BuiltEpoch != session.World.Epoch) return;
            if (PlacedEpoch != session.World.Epoch && !floor.TryPlace(this)) return;
            if (!session.Roster.AllReady) return;
            var seat = new PlayerSeat(Object.InputAuthority.RawEncoded, SeatIndex);
            if (!session.Roster.Owns(Object.InputAuthority.RawEncoded, seat)) return;
            if (!GetInput<FusionSeatInput>(out var input)) return; // missing input = neutral, no stale fire
            var command = input.Get(SeatIndex);
            transform.Rotate(0, command.Turn * turnSpeed * Runner.DeltaTime, 0);
            var move = transform.right * command.Move.x + transform.forward * command.Move.y;
            body.Move((move * speed + Vector3.down * 9.81f) * Runner.DeltaTime);
            // Gameplay adapter must validate cooldown, ammo, range and hits on host.
            if (command.Fire) FireHeld?.Invoke(seat);
        }

        public void PlaceAt(Vector3 feet, int epoch)
        {
            if (!HasStateAuthority) throw new InvalidOperationException("Only host places avatars.");
            body.enabled = false;
            try
            {
                var position = feet + Vector3.up * (body.height * .5f - body.center.y);
                GetComponent<NetworkTransform>().Teleport(position, Quaternion.identity);
                PlacedEpoch = epoch;
            }
            finally { body.enabled = true; }
        }
    }
}
#endif

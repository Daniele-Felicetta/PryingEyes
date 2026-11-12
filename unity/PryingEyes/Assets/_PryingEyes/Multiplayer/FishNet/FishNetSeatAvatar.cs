#if PRYINGEYES_FISHNET
using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend
{
    public struct AvatarSnapshot
    {
        public Vector3 Position;
        public float Yaw;
        public int Epoch;
    }

    /// <summary>Server-simulated movement probe. Pose uses server-only SyncVar; no client-authoritative transform.</summary>
    [RequireComponent(typeof(CharacterController), typeof(LocalSeatView))]
    public sealed class FishNetSeatAvatar : NetworkBehaviour
    {
        readonly SyncVar<int> seatIndex = new SyncVar<int>();
        readonly SyncVar<AvatarSnapshot> pose = new SyncVar<AvatarSnapshot>(new SyncTypeSettings(.05f));
        public int SeatIndex => seatIndex.Value;
        public int PlacedEpoch => pose.Value.Epoch;
        public float speed = 4f, turnSpeed = 120f;
        public event Action<PlayerSeat> FireHeld;
        FishNetSessionLauncher session;
        CharacterController body;
        SeatCommand command;
        uint sequence;
        bool receivedCommand;
        double receivedAt;
        int displayedEpoch;

        void Awake() => body = GetComponent<CharacterController>();
        public void InitializeSeat(int index)
        {
            if (index < 0 || index >= SessionRoster.MaxLocalPlayers) throw new ArgumentOutOfRangeException(nameof(index));
            seatIndex.Value = index;
        }
        public override void OnStartNetwork()
        {
            session = NetworkManager.GetComponent<FishNetSessionLauncher>();
            session.Avatars.Add(this);
            receivedCommand = false; sequence = 0; command = default; displayedEpoch = 0;
            TimeManager.OnTick += Tick;
        }
        public override void OnStartServer() => body.enabled = true;
        public override void OnStartClient()
        {
            body.enabled = IsServerInitialized;
            GetComponent<LocalSeatView>().Bind(IsOwner, SeatIndex, session.LocalPlayers);
        }
        public override void OnStopNetwork()
        {
            TimeManager.OnTick -= Tick;
            if (session != null) session.Avatars.Remove(this);
        }

        void Tick()
        {
            var world = session.World;
            if (world == null) return;
            if (IsOwner)
            {
                var source = session.InputFor(SeatIndex);
                var input = new SeatCommand { Sequence = ++sequence, Epoch = world.Descriptor.Epoch };
                if (source != null && source.isActiveAndEnabled)
                { input.MoveX = source.Move.x; input.MoveZ = source.Move.y; input.Turn = source.Turn; input.Fire = source.Fire; input.Use = source.Use; }
                if (IsServerInitialized) AcceptInput(input); else InputServerRpc(input);
            }
            if (!IsServerInitialized || !world.LocalReady || world.Failure != null) return;
            if (PlacedEpoch != world.Descriptor.Epoch && (world.PlaceAvatar == null || !world.PlaceAvatar(this))) return;
            if (!session.Roster.AllReady) return;
            float dt = (float)TimeManager.TickDelta;
            var intent = receivedCommand && Time.realtimeSinceStartupAsDouble - receivedAt <= .25 &&
                command.Epoch == world.Descriptor.Epoch ? command : default;
            // Neutral ticks must still release held actions and advance authoritative gameplay timers.
            intent.Epoch = world.Descriptor.Epoch;
            transform.Rotate(0, intent.Turn * turnSpeed * dt, 0);
            body.Move(((transform.right * intent.MoveX + transform.forward * intent.MoveZ) * speed + Vector3.down * 9.81f) * dt);
            pose.Value = new AvatarSnapshot { Position = transform.position, Yaw = transform.eulerAngles.y, Epoch = PlacedEpoch };
            if (intent.Fire) FireHeld?.Invoke(new PlayerSeat(Owner.ClientId, SeatIndex));
            world.DispatchInput(this, intent, dt);
        }

        [ServerRpc]
        void InputServerRpc(SeatCommand input) => AcceptInput(input);
        void AcceptInput(SeatCommand input)
        {
            var world = session.World;
            if (world == null || input.Epoch != world.Descriptor.Epoch ||
                !session.Roster.Owns(Owner.ClientId, new PlayerSeat(Owner.ClientId, SeatIndex)) ||
                (receivedCommand && !SeatCommand.IsNewer(input.Sequence, command.Sequence))) return;
            command = input.Sanitized(); receivedAt = Time.realtimeSinceStartupAsDouble; receivedCommand = true;
        }

        public void PlaceAt(Vector3 feet, int epoch, float yaw = 0)
        {
            if (!IsServerInitialized) throw new InvalidOperationException("Only host places avatars.");
            body.enabled = false;
            try
            {
                transform.SetPositionAndRotation(feet + Vector3.up * (body.height * .5f - body.center.y), Quaternion.Euler(0, yaw, 0));
                pose.Value = new AvatarSnapshot { Position = transform.position, Yaw = yaw, Epoch = epoch };
            }
            finally { body.enabled = true; }
        }

        void Update()
        {
            if (!IsClientInitialized || IsServerInitialized || pose.Value.Epoch < 1) return;
            var state = pose.Value;
            if (displayedEpoch != state.Epoch)
            { displayedEpoch = state.Epoch; transform.SetPositionAndRotation(state.Position, Quaternion.Euler(0, state.Yaw, 0)); return; }
            float blend = 1 - Mathf.Exp(-20 * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, state.Position, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, state.Yaw, 0), blend);
        }
    }
}
#endif

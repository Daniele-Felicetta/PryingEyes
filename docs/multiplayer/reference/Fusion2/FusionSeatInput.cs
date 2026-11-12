#if PRYINGEYES_FUSION2
using Fusion;
using UnityEngine;

namespace PryingEyes.Multiplayer.Fusion2
{
    public struct SeatCommand : INetworkStruct
    {
        public Vector2 Move;
        public float Turn;
        public NetworkBool Fire;
        public SeatCommand Sanitized()
        {
            var value = this;
            value.Move = float.IsNaN(Move.x) || float.IsInfinity(Move.x) || float.IsNaN(Move.y) || float.IsInfinity(Move.y)
                ? Vector2.zero : Vector2.ClampMagnitude(Move, 1);
            value.Turn = float.IsNaN(Turn) || float.IsInfinity(Turn) ? 0 : Mathf.Clamp(Turn, -1, 1);
            return value;
        }
    }

    // Fusion has one input payload per peer/tick, even when that peer owns four avatars.
    public struct FusionSeatInput : INetworkInput
    {
        public SeatCommand Seat0, Seat1, Seat2, Seat3;
        public SeatCommand Get(int index)
        {
            switch (index)
            {
                case 0: return Seat0.Sanitized();
                case 1: return Seat1.Sanitized();
                case 2: return Seat2.Sanitized();
                case 3: return Seat3.Sanitized();
                default: return default;
            }
        }
        public void Set(int index, SeatCommand command)
        {
            switch (index)
            {
                case 0: Seat0 = command; break;
                case 1: Seat1 = command; break;
                case 2: Seat2 = command; break;
                case 3: Seat3 = command; break;
            }
        }
    }
}
#endif

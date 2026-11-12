using System;

namespace PryingEyes.Multiplayer
{
    /// <summary>Intent only: host chooses time step, position, damage and rate of actions.</summary>
    public struct SeatCommand
    {
        public uint Sequence;
        public int Epoch;
        public float MoveX, MoveZ, Turn;
        public bool Fire, Use;

        public SeatCommand Sanitized()
        {
            var result = this;
            if (!Finite(MoveX) || !Finite(MoveZ)) result.MoveX = result.MoveZ = 0;
            else
            {
                double length = Math.Sqrt((double)MoveX * MoveX + (double)MoveZ * MoveZ);
                if (length > 1) { result.MoveX = (float)(MoveX / length); result.MoveZ = (float)(MoveZ / length); }
            }
            result.Turn = Finite(Turn) ? Math.Max(-1, Math.Min(1, Turn)) : 0;
            return result;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        // Sequence rollover is valid; duplicates and older packets cannot overwrite newer intent.
        public static bool IsNewer(uint candidate, uint previous) => unchecked((int)(candidate - previous)) > 0;
    }
}

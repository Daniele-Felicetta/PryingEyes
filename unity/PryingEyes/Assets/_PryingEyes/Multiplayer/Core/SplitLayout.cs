using System;

namespace PryingEyes.Multiplayer
{
    public readonly struct Viewport
    {
        public readonly float X, Y, Width, Height;
        public Viewport(float x, float y, float width, float height)
        { X = x; Y = y; Width = width; Height = height; }
    }

    public static class SplitLayout
    {
        // Unity normalized viewport coordinates, origin at bottom left. Remote avatars consume no viewport.
        public static Viewport ForSeat(int localIndex, int localCount)
        {
            if (localCount < 1 || localCount > SessionRoster.MaxLocalPlayers)
                throw new ArgumentOutOfRangeException(nameof(localCount));
            if (localIndex < 0 || localIndex >= localCount) throw new ArgumentOutOfRangeException(nameof(localIndex));
            if (localCount == 1) return new Viewport(0, 0, 1, 1);
            if (localCount == 2) return new Viewport(0, localIndex == 0 ? .5f : 0, 1, .5f);
            return new Viewport(localIndex % 2 * .5f, localIndex < 2 ? .5f : 0, .5f, .5f);
        }
    }
}

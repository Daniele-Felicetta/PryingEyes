using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>What the braking sign of the dashboard says, in the words of this game. BrakeCues works out the state of the sign (what it says, the metres, the speed the braking is for); the words are the game's.</summary>
    public static class BrakeCueWords
    {
        /// <summary>The big line of the sign: the countdown, or the order to brake (empty when the sign has nothing to say).</summary>
        public static string Title(BrakeCue c)
        {
            if (c.kind == CueKind.Brake) return "FRENA";
            if (c.kind == CueKind.Approach) return "FRENA TRA " + BrakeCues.RoundMetres(c.metres) + " m";
            return "";
        }

        /// <summary>The small line under it: the speed the braking is for (empty when the sign has nothing to say, or the bottom of the zone is not seen yet).</summary>
        public static string Sub(BrakeCue c)
        {
            if (c.kind == CueKind.None || c.target <= 0f) return "";
            return "fino a " + BrakeCues.TargetKmh(c.target) + " km/h";
        }
    }
}

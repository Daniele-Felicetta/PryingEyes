namespace PryingEyes.Pista
{
    /// <summary>The riders of a race by the number of their bike in the roster of this game (Bikes). The package's Race takes the bike itself (a BikeSpec); the game says "the third bike of the roster", and these calls
    /// are how it says it. A bike that is not the roster's as it comes (a tuned one) arrives as 'spec' and is the one that rides.</summary>
    public static class RaceRoster
    {
        /// <summary>The racing line of the bike at this number of the roster.</summary>
        public static RacingLine LineFor(this Race race, int bikeIndex) { return race.LineFor(Bikes.Get(bikeIndex)); }

        /// <summary>A rider with a bike on this machine; 'spec' is the bike as the garage made it (a tuned one), the roster's own when it is null.</summary>
        public static Racer AddHuman(this Race race, string name, int bikeIndex, int skin, int seed, BikeSpec spec = null)
        {
            return race.AddHuman(name, spec ?? Bikes.Get(bikeIndex), skin, seed, bikeIndex);
        }

        /// <summary>A computer rider; 'spec' is a bike that is not the roster's as it comes (a tuned one), which then gets a line of its own.</summary>
        public static Racer AddAi(this Race race, string name, int bikeIndex, int skin, int seed, float pace, float mistakes, BikeSpec spec = null)
        {
            return race.AddAi(name, spec ?? Bikes.Get(bikeIndex), skin, seed, pace, mistakes, bikeIndex);
        }

        /// <summary>Somebody on another machine: only poses arrive, and the roster says which bike they ride.</summary>
        public static Racer AddRemote(this Race race, string name, int bikeIndex, int skin, bool friend = true)
        {
            return race.AddRemote(name, Bikes.Get(bikeIndex), skin, friend, bikeIndex);
        }
    }
}

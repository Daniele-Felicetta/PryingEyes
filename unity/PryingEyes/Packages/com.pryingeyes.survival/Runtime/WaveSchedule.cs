using System;

namespace Production.Survival
{
    /// <summary>One wave's unspent spawn budget and clock. Supply the current living population each tick.
    /// Advance reports eligibility; CommitSpawn spends exactly one slot only after a successful creation.
    /// A full population cannot bank overdue spawns. Completion includes pending and living enemies.</summary>
    public sealed class WaveSchedule
    {
        public int Pending { get; private set; }
        public int Spawned { get; private set; }
        public int PopulationLimit { get; }
        readonly float interval;
        float remaining;
        bool eligible;

        public WaveSchedule(int total, int populationLimit, float spawnInterval)
        {
            if (total <= 0 || populationLimit <= 0) throw new ArgumentOutOfRangeException(nameof(total));
            if (float.IsNaN(spawnInterval) || float.IsInfinity(spawnInterval) || spawnInterval <= 0)
                throw new ArgumentOutOfRangeException(nameof(spawnInterval));
            Pending = total; PopulationLimit = populationLimit; interval = spawnInterval;
        }

        public bool Advance(float seconds, int living)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            ValidatePopulation(living);
            remaining = Math.Max(0, remaining - seconds);
            eligible = Pending > 0 && living < PopulationLimit && remaining <= 0;
            return eligible;
        }

        public void CommitSpawn()
        {
            if (!eligible) throw new InvalidOperationException("WAVE_SPAWN_NOT_READY");
            eligible = false; Pending--; Spawned++; remaining = interval;
        }

        public bool Complete(int living)
        {
            ValidatePopulation(living);
            return Pending == 0 && living == 0;
        }

        static void ValidatePopulation(int living)
        {
            if (living < 0) throw new ArgumentOutOfRangeException(nameof(living));
        }
    }
}

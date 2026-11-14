using System;
using System.Collections;
using Production.Survival;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>City balance is game data; the reusable scheduler knows nothing about it.</summary>
    [Serializable]
    public sealed class CityWaveProfile
    {
        public int firstCount = 12, increment = 4, maximum = 96, aliveLimit = 24;
        public float interval = .65f, intermission = 10f, healthPerWave = .12f;
        public float speed = 1.45f, speedPerWave = .05f, maximumSpeed = 2f;

        public void Validate()
        {
            foreach (float value in new[] { interval, intermission, healthPerWave, speed, speedPerWave, maximumSpeed })
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new InvalidOperationException("CITY_WAVE_PROFILE_NONFINITE_OR_NEGATIVE");
            if (firstCount < 1 || increment < 0 || maximum < firstCount || maximum > 512 || aliveLimit < 1 || aliveLimit > 64 || interval <= 0 || speed <= 0 || maximumSpeed < speed)
                throw new InvalidOperationException("CITY_WAVE_PROFILE_INVALID");
        }

        public int Count(int wave) => (int)Math.Min(maximum, firstCount + Math.Max(0L, (long)wave - 1) * increment);
        public float Speed(int wave) => Mathf.Min(maximumSpeed, speed + Mathf.Max(0, wave - 1) * speedPerWave);
        public float Health(int wave) => 1f + Mathf.Min(100, Mathf.Max(0, wave - 1)) * healthPerWave;
    }

    public sealed partial class CityDirector
    {
        public CityWaveProfile survival = new CityWaveProfile();

        public IEnumerator RunWave(GameManager game, int number)
        {
            survival.Validate();
            var schedule = new WaveSchedule(survival.Count(number), survival.aliveLimit, survival.interval);
            while (!schedule.Complete(Enemy.All.Count))
            {
                if (Time.deltaTime > 0 && schedule.Advance(Time.deltaTime, Enemy.All.Count))
                {
                    var type = schedule.Spawned > 0 && UnityEngine.Random.value < Mathf.Min(.30f, .12f + number * .025f)
                        ? Enemy.EnemyType.Vigile : Enemy.EnemyType.Spazzino;
                    var enemy = Enemy.Spawn(type, game.PickSpawn(type));
                    if (enemy != null)
                    {
                        enemy.maxHp *= survival.Health(number); enemy.hp = enemy.maxHp;
                        enemy.speed *= survival.Speed(number);
                        enemy.MakeRelentless();
                        schedule.CommitSpawn();
                    }
                }
                game.SetWaveRemaining(schedule.Pending + Enemy.All.Count);
                yield return null;
            }
            game.CompleteWave(survival.intermission);
        }
    }
}

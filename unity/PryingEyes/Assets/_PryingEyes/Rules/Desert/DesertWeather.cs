using System;

namespace PryingEyes.Deserto
{
    /// <summary>What the air does at one moment: the wind and the sandstorm, and what they do to the light. Pure (no UnityEngine): tools/desertlab prints it.</summary>
    public struct WeatherState
    {
        /// <summary>0..1: how hard the wind blows (it bends the bushes, rolls the tumbleweeds, loads the air with sand and is heard).</summary>
        public float wind;
        /// <summary>0..1: the gust inside the wind (a puff every few seconds).</summary>
        public float gust;
        /// <summary>0..1: a sandstorm (zero most of the time: about one day in two has one, for a minute or two of play).</summary>
        public float storm;
        /// <summary>The way the wind blows, a unit vector on the ground (it turns slowly through the day, always within the same quarter of the compass).</summary>
        public float dirX, dirZ;
        /// <summary>The wind and the gust without the storm (WithStorm builds the rest from them).</summary>
        public float calmWind, calmGust;
        /// <summary>What the air does to the light: the fog density is multiplied by fogMul, the sun by sunMul; sand is how loaded the air is (0..1), which tints the fog.</summary>
        public float fogMul, sunMul, sand;
    }

    /// <summary>The desert's weather: a steady wind with gusts all day, and sandstorms, a pure function of the world seed and of the clock (day and hour), so that
    /// it is the same whoever asks, in whatever order. A storm starts between 08:00 and 22:00 of a day, lasts one and a half to three game hours (75 to 160 s of
    /// play), grows in 25 seconds and fades in 25; the first morning is always clear. DesertSky applies it (fog, sun, sand colour, the wind of the shaders),
    /// DesertDust draws it, the life of the sand follows the wind.</summary>
    public static class DesertWeather
    {
        /// <summary>The chance that a day has a sandstorm.</summary>
        public const float StormChance = 0.55f;
        /// <summary>Game hours a storm takes to grow, and to fade.</summary>
        public const float Ramp = 0.5f;
        /// <summary>No storm before this hour of the first day (the player's first minutes are clear).</summary>
        public const float FirstClear = 12f;

        /// <summary>The storm of one day, if it has one: when it starts (hours since the first midnight), how long it lasts (hours) and how strong it gets (0.7..1).</summary>
        public static bool Storm(int seed, int day, out float start, out float length, out float strength)
        {
            start = day * 24f + 8f + DesertNoise.Hash01(day, 5, seed + 4002) * 14f;
            length = 1.6f + DesertNoise.Hash01(day, 7, seed + 4003) * 1.6f;
            strength = 0.7f + DesertNoise.Hash01(day, 9, seed + 4004) * 0.3f;
            if (day == 0) start = Math.Max(start, FirstClear);
            return DesertNoise.Hash01(day, 3, seed + 4001) < StormChance;
        }

        /// <summary>The weather at the hour (0..24) of a day; 'daylight' (0..1, from the clock) tells how much of the light there is to take away.</summary>
        public static WeatherState Evaluate(int seed, int day, float hour, float daylight)
        {
            float t = day * 24f + hour;
            float storm = 0f;
            for (int d = Math.Max(0, day - 1); d <= day; d++)       // a storm can run past midnight into the next day; nothing happened before the first day
            {
                if (!Storm(seed, d, out float s0, out float len, out float str)) continue;
                float a = DesertNoise.Smooth(s0, s0 + Ramp, t), b = 1f - DesertNoise.Smooth(s0 + len - Ramp, s0 + len, t);
                storm = Math.Max(storm, str * a * b);
            }
            var w = new WeatherState();
            float slow = DesertNoise.Value(t * 0.32f, 11.7f, seed + 4010);
            float gust = DesertNoise.Smooth(0.52f, 0.90f, DesertNoise.Value(t * 9f, 3.3f, seed + 4011)) * (0.35f + 0.65f * slow);
            w.calmGust = gust;
            w.calmWind = 0.12f + 0.30f * slow + gust * 0.35f;
            float ang = 6.2832f * (0.15f + 0.30f * DesertNoise.Value(t * 0.06f, 5.9f, seed + 4012));
            w.dirX = (float)Math.Cos(ang); w.dirZ = (float)Math.Sin(ang);
            return WithStorm(w, storm, daylight);
        }

        /// <summary>The same weather with another storm (0..1): the wind rises, the fog thickens, the sun dims, the air fills with sand. The tests use it to have a storm on demand.</summary>
        public static WeatherState WithStorm(WeatherState w, float storm, float daylight)
        {
            storm = DesertNoise.Clamp01(storm);
            w.storm = storm;
            w.gust = DesertNoise.Clamp01(w.calmGust + storm * 0.5f);
            w.wind = DesertNoise.Clamp01(w.calmWind + storm * 0.9f);
            w.fogMul = 1f + storm * DesertNoise.Lerp(0.8f, 2.8f, daylight);
            w.sunMul = 1f - 0.55f * storm;
            w.sand = storm;
            return w;
        }
    }
}

using System;

namespace PryingEyes.Deserto
{
    /// <summary>A colour as three floats (the clock is pure: no UnityEngine.Color). Values are sRGB-like 0..1; the sky applies them as they are.</summary>
    public struct Rgb
    {
        public float r, g, b;
        public Rgb(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }
        public static Rgb Lerp(Rgb a, Rgb b, float t) => new Rgb(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);
        public static Rgb operator *(Rgb a, float k) => new Rgb(a.r * k, a.g * k, a.b * k);
    }

    /// <summary>Everything the sky shows and the light does at one hour of the day: where the sun and the moon are, how bright they are, the colour of
    /// the horizon and of the zenith, the fog and the ambient light, how many stars. Pure: tools/desertlab prints a table of it.</summary>
    public struct SkyState
    {
        public float hour, sunHeight, daylight, twilight, night;
        public float sunX, sunY, sunZ, moonX, moonY, moonZ;
        public float sunIntensity, moonIntensity, stars, fogDensity, exposure;
        public Rgb sunColor, moonColor, horizon, zenith, fog, ambientSky, ambientEquator, ambientGround;
    }

    /// <summary>The desert's day: a full day lasts <see cref="DaySeconds"/> of real time, the game starts at <see cref="StartHour"/> (early morning:
    /// "inizia il giorno"). The sun rises at 6, is highest at noon and sets at 18; the moon is always the opposite of the sun.</summary>
    public sealed class DesertClock
    {
        public const float DaySeconds = 1200f;                 // 20 minutes: about 8 of night, 12 of day
        public const float StartHour = 7.0f;
        public const float Sunrise = 6f, Sunset = 18f;

        public float hour = StartHour;
        public int day;
        /// <summary>1 = the clock runs at its normal speed; tests set it to freeze or skip time.</summary>
        public float speed = 1f;

        public void Advance(float dt)
        {
            hour += dt * speed * 24f / DaySeconds;
            while (hour >= 24f) { hour -= 24f; day++; }
            while (hour < 0f) { hour += 24f; day--; }
        }

        public bool IsNight => Evaluate(hour).daylight < 0.12f;

        static readonly Rgb NoonHorizon = new Rgb(0.76f, 0.73f, 0.64f), NoonZenith = new Rgb(0.13f, 0.34f, 0.84f);
        // l'orizzonte al tramonto e' pesca polverosa, non arancio pieno (0,95 / 0,50 / 0,26 faceva tutto lontano, la nebbia compresa, una sola macchia arancione): l'arancio lo mette il sole,
        // dalla sua parte (l'alone della nebbia e della cupola prende il colore del sole)
        static readonly Rgb DuskHorizon = new Rgb(0.90f, 0.60f, 0.44f), DuskZenith = new Rgb(0.20f, 0.24f, 0.46f);
        static readonly Rgb NightHorizon = new Rgb(0.050f, 0.060f, 0.100f), NightZenith = new Rgb(0.008f, 0.012f, 0.035f);

        /// <summary>The sky at an hour (0..24). Sun height is in -1..1 (sine of the elevation); the horizontal path goes from the east at sunrise
        /// through the south at noon to the west at sunset.</summary>
        public static SkyState Evaluate(float hour)
        {
            var s = new SkyState { hour = hour };
            float ph = (hour - Sunrise) / 24f * 2f * (float)Math.PI;          // 0 at sunrise, pi/2 at noon, pi at sunset
            float sinEl = (float)Math.Sin(ph) * 0.94f;
            float el = (float)Math.Asin(sinEl), cosEl = (float)Math.Cos(el);
            float hx = (float)Math.Cos(ph), hz = -0.32f * (float)Math.Sin(ph);
            float hl = (float)Math.Sqrt(hx * hx + hz * hz);
            hx /= hl; hz /= hl;
            s.sunHeight = sinEl;
            s.sunX = hx * cosEl; s.sunY = sinEl; s.sunZ = hz * cosEl;
            s.moonX = -s.sunX; s.moonY = -s.sunY; s.moonZ = -s.sunZ;

            s.daylight = DesertNoise.Smooth(-0.06f, 0.30f, sinEl);                       // 0 in the night, 1 in full day
            s.twilight = DesertNoise.Clamp01(1f - Math.Abs(sinEl - 0.02f) / 0.26f);      // peaks with the sun on the horizon
            s.night = DesertNoise.Clamp01(1f - DesertNoise.Smooth(-0.20f, 0.06f, sinEl));              // 1 in the dead of night

            Rgb horizon = Rgb.Lerp(NightHorizon, NoonHorizon, s.daylight);
            horizon = Rgb.Lerp(horizon, DuskHorizon, s.twilight * 0.85f);
            Rgb zenith = Rgb.Lerp(NightZenith, NoonZenith, s.daylight);
            zenith = Rgb.Lerp(zenith, DuskZenith, s.twilight * 0.55f);
            s.horizon = horizon; s.zenith = zenith;
            s.fog = horizon * 0.92f;

            // sun: orange when low, white when high; weak below the horizon
            s.sunColor = Rgb.Lerp(new Rgb(1f, 0.46f, 0.20f), new Rgb(1f, 0.95f, 0.84f), DesertNoise.Smooth(0.04f, 0.5f, sinEl));
            s.sunIntensity = DesertNoise.Smooth(-0.03f, 0.22f, sinEl) * DesertNoise.Lerp(0.55f, 1f, DesertNoise.Smooth(0.05f, 0.6f, sinEl));
            s.moonColor = new Rgb(0.55f, 0.66f, 1f);
            s.moonIntensity = DesertNoise.Smooth(0.02f, 0.30f, -sinEl) * 0.16f;
            s.stars = DesertNoise.Clamp01(1f - s.daylight * 2.2f - s.twilight * 0.25f);

            // la notte non e' nera: la luna e la volta del cielo riempiono le ombre di un blu cupo (un cassone di legno all'ombra si vede ancora)
            s.ambientSky = Rgb.Lerp(new Rgb(0.050f, 0.066f, 0.120f), new Rgb(0.46f, 0.58f, 0.82f), s.daylight);
            s.ambientEquator = Rgb.Lerp(new Rgb(0.038f, 0.048f, 0.088f), new Rgb(0.60f, 0.54f, 0.43f), s.daylight);          // di giorno i muri all'ombra prendono il rimbalzo della sabbia al sole (caldo, e un quinto piu' chiaro di prima)
            s.ambientGround = Rgb.Lerp(new Rgb(0.020f, 0.022f, 0.038f), new Rgb(0.34f, 0.27f, 0.19f), s.daylight);
            s.ambientSky = Rgb.Lerp(s.ambientSky, new Rgb(0.34f, 0.32f, 0.50f), s.twilight * 0.45f);          // il cielo che riempie le ombre al tramonto e' violetto, non rosa: ombre fredde contro la sabbia calda

            s.fogDensity = DesertNoise.Lerp(0.0090f, 0.0016f, s.daylight);                 // the night closes in, the day opens up (0.0016 leaves 31 % of the sand at the edge of the terrain, 740 m, and 9 % at 1500 m: the horizon blends into the sky and the far places show in colour)
            s.exposure = DesertNoise.Lerp(0.15f, -0.90f, s.daylight);                      // EV on top of the game's base: eyes adapt to the dark
            return s;
        }

        public SkyState Now => Evaluate(hour);

        /// <summary>"07:30" style text for the HUD.</summary>
        public static string Format(float hour)
        {
            int h = (int)hour % 24, m = (int)((hour - (int)hour) * 60f);
            return h.ToString("00") + ":" + m.ToString("00");
        }
    }
}

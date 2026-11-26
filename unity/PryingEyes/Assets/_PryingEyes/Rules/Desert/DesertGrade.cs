using System;

namespace PryingEyes.Deserto
{
    /// <summary>One look of the screen's colour grading: what the post-processing volume is told (saturation and contrast of ColorAdjustments, its colour filter, the two tints of the
    /// split toning and where they meet) and how much bloom, vignette and grain the picture gets before the player's own sliders scale them. Pure, like the clock.</summary>
    public struct Grade
    {
        public float saturation, contrast;              // -100..100 as in ColorAdjustments
        public Rgb filter;                              // multiplies the picture (white = nothing)
        public Rgb shadows, highlights;                 // split toning
        public float balance;                           // -100..100: where the two tints meet
        public float bloom, vignette, grain;            // base intensities (the sliders in the settings scale them)
        public float bloomThreshold;                    // what the bloom picks up (HDR, before the exposure): low in the rooms and at night, high in the sun, where the sand is above 1 almost everywhere

        public static Grade Lerp(Grade a, Grade b, float t)
        {
            t = DesertNoise.Clamp01(t);
            return new Grade
            {
                saturation = a.saturation + (b.saturation - a.saturation) * t,
                contrast = a.contrast + (b.contrast - a.contrast) * t,
                filter = Rgb.Lerp(a.filter, b.filter, t),
                shadows = Rgb.Lerp(a.shadows, b.shadows, t),
                highlights = Rgb.Lerp(a.highlights, b.highlights, t),
                balance = a.balance + (b.balance - a.balance) * t,
                bloom = a.bloom + (b.bloom - a.bloom) * t,
                bloomThreshold = a.bloomThreshold + (b.bloomThreshold - a.bloomThreshold) * t,
                vignette = a.vignette + (b.vignette - a.vignette) * t,
                grain = a.grain + (b.grain - a.grain) * t,
            };
        }
    }

    /// <summary>The desert's grading. The rooms of the game are graded cold and grey on purpose (saturation -26, blue shadows: stone, glass, damp), and the open sand inherited that look: it
    /// came out pale and flat. Outdoors the picture is graded by the hour and the weather instead: a clean day pushes the sand and the blue a little (saturation up, a firm contrast, warm
    /// highlights over blue shadows), the golden hour goes much further (orange lights, violet shadows, a lot of bloom for the sun), the night is cool (blue shadows, pale blue lights, less colour,
    /// more grain) and a sandstorm washes everything to dust. Indoors DesertSky blends back to the grade the room asked for.</summary>
    public static class DesertGrade
    {
        static readonly Grade Day = new Grade
        {
            saturation = 14f, contrast = 17f, filter = new Rgb(1.00f, 0.985f, 0.95f),
            shadows = new Rgb(0.34f, 0.46f, 0.70f), highlights = new Rgb(0.56f, 0.52f, 0.45f), balance = 6f,       // le luci appena calde: il 'soft light' di URP con (0,98 0,80 0,58) alzava il rosso di tutto quello che e' chiaro, e il cielo blu diventava grigio-lilla
            bloom = 0.70f, bloomThreshold = 2.6f, vignette = 0.22f, grain = 0.12f,
        };
        static readonly Grade Golden = new Grade
        {
            saturation = 17f, contrast = 15f, filter = new Rgb(1.00f, 0.93f, 0.83f),          // contrasto 19 schiacciava l'ombra della sabbia rossa in un cremisi quasi nero
            shadows = new Rgb(0.36f, 0.34f, 0.68f), highlights = new Rgb(1.00f, 0.56f, 0.30f), balance = 10f,
            bloom = 0.85f, bloomThreshold = 1.7f, vignette = 0.28f, grain = 0.14f,
        };
        static readonly Grade Night = new Grade
        {
            saturation = -6f, contrast = 10f, filter = new Rgb(0.91f, 0.96f, 1.05f),
            shadows = new Rgb(0.20f, 0.32f, 0.68f), highlights = new Rgb(0.60f, 0.72f, 0.95f), balance = -8f,
            bloom = 0.75f, bloomThreshold = 1.0f, vignette = 0.32f, grain = 0.22f,
        };
        static readonly Grade Storm = new Grade
        {
            saturation = -14f, contrast = 4f, filter = new Rgb(1.00f, 0.87f, 0.72f),
            shadows = new Rgb(0.46f, 0.38f, 0.33f), highlights = new Rgb(0.95f, 0.72f, 0.48f), balance = 0f,
            bloom = 0.40f, bloomThreshold = 1.8f, vignette = 0.38f, grain = 0.20f,
        };

        /// <summary>Only the look sweep of the test sets this (`-desertonly look`): it changes the grade after the tables, to compare several tunings of the same view. Null in the game.</summary>
        public static Func<Grade, Grade> Tweak;

        /// <summary>The grade of the open air at a moment of the day (<see cref="SkyState"/>) and in some weather.</summary>
        public static Grade Outdoors(SkyState s, WeatherState wx)
        {
            var g = Grade.Lerp(Night, Day, s.daylight);
            g = Grade.Lerp(g, Golden, s.twilight * 0.9f);
            g = Grade.Lerp(g, Storm, wx.sand * 0.8f);
            return Tweak != null ? Tweak(g) : g;
        }
    }
}

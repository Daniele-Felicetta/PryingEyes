using System.Globalization;
using PryingEyes.Deserto;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>How the sky of the desert is tuned for a circuit. The sky, the haze and the exposure belong to the desert mode (DesertSky, shared); a circuit wants its own values (a clear day over green
    /// country and dark asphalt, not the white haze that suits sand), so the mode sets them when a race starts and puts the desert's own back when it ends. Every number can be changed from the command
    /// line (-pistafog 0.25, -pistaskygain, -pistazenith, -pistacloud, -pistaexposure, -pistasun, -pistadaylight, -pistaglow) so that a look can be tried on a photograph without building again.</summary>
    public static class PistaLook
    {
        /// <summary>The circuit's values: how thick the haze is (1 = the desert's), how much brighter than the clock's palette the sky and the haze are by day (and the zenith and the clouds), the exposure offset (EV),
        /// the sun at noon, how much of the sun and the ambient is left at noon, and the glow around the sun.</summary>
        public static float Fog = 0.22f, SkyGain = 1.15f, ZenithGain = 0.9f, CloudGain = 1.2f, Exposure = 0.3f, Sun = 1.5f, Daylight = 0.58f, Glow = 1f;

        /// <summary>The blue of the haze and of the horizon by day (multipliers of the desert's sand-coloured palette; -pistahorizon r,g,b and -pistahazetint r,g,b). They fade toward dawn and dusk.</summary>
        public static Rgb HorizonTint = new Rgb(0.93f, 1.07f, 1.30f), HazeTint = new Rgb(0.93f, 1.08f, 1.32f);

        /// <summary>The same numbers for the rain: a thick grey haze, a low dull sky under heavy cloud (the cover is the share of the sky the clouds take), the sun mostly gone and no glow round it. Every
        /// flag above (-pistafog and the rest) changes both looks, -pistacover the cloud cover of the rain.</summary>
        public static float RainFog = 0.62f, RainSkyGain = 0.78f, RainZenithGain = 0.62f, RainCloudGain = 0.55f, RainExposure = 0.55f, RainSun = 0.5f, RainDaylight = 0.5f, RainGlow = 0f, RainCover = 0.93f;
        public static Rgb RainHorizonTint = new Rgb(0.86f, 0.94f, 1.04f), RainHazeTint = new Rgb(0.86f, 0.94f, 1.06f);

        static bool saved;
        static float sFog, sSky, sZen, sCloud, sExp, sSun, sDay, sGlow, sCover;
        static Rgb sHorizonTint, sFogTint;

        /// <summary>The value after a command-line flag, or null.</summary>
        public static string StrArg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        /// <summary>The number after a flag of the command line (the photographs try values with them), or the fallback.</summary>
        public static float Arg(string name, float fallback)
        {
            string v = StrArg(name);
            float f;
            return v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : fallback;
        }

        /// <summary>Three numbers after a flag ("0.9,1.1,1.3"), or the fallback.</summary>
        static Rgb Arg3(string name, Rgb fallback)
        {
            string v = StrArg(name);
            if (v == null) return fallback;
            var p = v.Split(',');
            float r, g, b;
            if (p.Length == 3 && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out r) && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out g)
                && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out b)) return new Rgb(r, g, b);
            return fallback;
        }

        /// <summary>Set the circuit's look, clear or in the rain (the desert's values are kept the first time, to give back).</summary>
        public static void Apply(bool rain = false)
        {
            if (!saved)
            {
                saved = true;
                sFog = DesertSky.FogScale; sSky = DesertSky.DaySkyGain; sZen = DesertSky.DayZenithGain; sCloud = DesertSky.DayCloudGain;
                sExp = DesertSky.ExposureOffset; sSun = DesertSky.SunMax; sDay = DesertSky.DayLight; sGlow = DesertSky.SunGlow;
                sHorizonTint = DesertSky.HorizonTint; sFogTint = DesertSky.FogTint; sCover = DesertSky.CloudOverride;
            }
            DesertSky.FogScale = Arg("-pistafog", rain ? RainFog : Fog);
            DesertSky.DaySkyGain = Arg("-pistaskygain", rain ? RainSkyGain : SkyGain);
            DesertSky.DayZenithGain = Arg("-pistazenith", rain ? RainZenithGain : ZenithGain);
            DesertSky.DayCloudGain = Arg("-pistacloud", rain ? RainCloudGain : CloudGain);
            DesertSky.ExposureOffset = Arg("-pistaexposure", rain ? RainExposure : Exposure);
            DesertSky.SunMax = Arg("-pistasun", rain ? RainSun : Sun);
            DesertSky.DayLight = Arg("-pistadaylight", rain ? RainDaylight : Daylight);
            DesertSky.SunGlow = Arg("-pistaglow", rain ? RainGlow : Glow);
            DesertSky.HorizonTint = Arg3("-pistahorizon", rain ? RainHorizonTint : HorizonTint);
            DesertSky.FogTint = Arg3("-pistahazetint", rain ? RainHazeTint : HazeTint);
            DesertSky.CloudOverride = rain ? Arg("-pistacover", RainCover) : sCover;
        }

        /// <summary>Give the desert its values back (the mode is over).</summary>
        public static void Restore()
        {
            if (!saved) return;
            saved = false;
            DesertSky.FogScale = sFog; DesertSky.DaySkyGain = sSky; DesertSky.DayZenithGain = sZen; DesertSky.DayCloudGain = sCloud;
            DesertSky.ExposureOffset = sExp; DesertSky.SunMax = sSun; DesertSky.DayLight = sDay; DesertSky.SunGlow = sGlow;
            DesertSky.HorizonTint = sHorizonTint; DesertSky.FogTint = sFogTint; DesertSky.CloudOverride = sCover;
        }
    }
}

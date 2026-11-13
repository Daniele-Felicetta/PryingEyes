using System;
using System.Globalization;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>L'aspetto di ogni ambiente: nebbia, luce ambiente e grading. Un look per tema (albergo, chiesa, teatro, sanatorio),
    /// scelto da ApplyLook all'inizio del piano. Il progetto e' in spazio colore Linear con grading HDR: a parita' di valori la scena
    /// viene circa meta' piu' scura di com'era in Gamma (la curva gamma non schiarisce piu' le ombre di nascosto), quindi luci,
    /// esposizione, ambiente e nebbia sono stati ritarati sulle foto a seme fisso della sonda (-perfshots, flag -look* per provare
    /// varianti senza ricostruire; vedi LookTune e la diciottesima passata di docs/quality-audit.md).</summary>
    public partial class FloorDirector
    {
        /// <summary>I colori sono sRGB (Unity li converte in lineare). density e ambient moltiplicano i valori base di ApplyLook,
        /// exposure e' in EV sopra l'esposizione del gioco, contrast e saturation sono quelli di ColorAdjustments.</summary>
        struct ThemeLook
        {
            public Color fog, sky, equator, ground, shadows, highlights;
            public float density, ambient, exposure, contrast, saturation, balance, bloom, vignette, vignetteSmooth, grain;
        }

        /// <summary>Il look del tema del piano: l'albergo e' il riferimento, gli altri ne cambiano il colore e poco altro.
        /// La nebbia non e' illuminata da niente e l'esposizione la schiarisce come tutto il resto: con il colore di prima (in sRGB
        /// 0,17-0,21) era 10-20 volte piu' chiara delle pareti lit solo dall'ambiente e l'immagine diventava un latte piatto, per
        /// questo qui vale la meta' (taratura con -lookfogc). L'esposizione di teatro e sanatorio e' piu' bassa perche' hanno
        /// superfici piu' chiare (marmo e piastrelle) che con quella dell'albergo bruciavano le pozze di luce.</summary>
        static ThemeLook LookOf(Plan plan)
        {
            var look = new ThemeLook
            {
                fog = new Color(0.095f, 0.08f, 0.085f),
                sky = new Color(0.13f, 0.15f, 0.21f), equator = new Color(0.095f, 0.095f, 0.11f), ground = new Color(0.05f, 0.046f, 0.05f),
                shadows = new Color(0.36f, 0.45f, 0.58f), highlights = new Color(0.63f, 0.53f, 0.41f), balance = -12f,
                density = 1.3f, ambient = 0.83f, exposure = -0.1f,
                contrast = 8f, saturation = -24f, bloom = 0.55f, vignette = 0.26f, vignetteSmooth = 0.55f, grain = 0.30f,
            };
            if (plan.theme == Theme.Teatro)             // polvere rossastra di velluto e legno
            {
                look.fog = new Color(0.105f, 0.055f, 0.055f); look.exposure = -0.5f;
                look.sky = new Color(0.16f, 0.11f, 0.13f); look.equator = new Color(0.115f, 0.082f, 0.088f); look.ground = new Color(0.06f, 0.04f, 0.045f);
                look.shadows = new Color(0.46f, 0.36f, 0.52f); look.highlights = new Color(0.72f, 0.52f, 0.36f); look.balance = -6f; look.saturation = -14f;
            }
            else if (plan.theme == Theme.Sanatorio)     // aria verdastra di vernice e disinfettante
            {
                look.fog = new Color(0.06f, 0.0875f, 0.0775f); look.exposure = -0.3f;
                look.sky = new Color(0.105f, 0.17f, 0.17f); look.equator = new Color(0.082f, 0.11f, 0.105f); look.ground = new Color(0.045f, 0.052f, 0.05f);
                look.shadows = new Color(0.30f, 0.46f, 0.46f); look.highlights = new Color(0.60f, 0.62f, 0.46f); look.balance = -10f; look.saturation = -30f;
            }
            else if (plan.startInChurch)                // pietra e vetro freddi
            {
                look.fog = new Color(0.085f, 0.095f, 0.115f);
                look.shadows = new Color(0.30f, 0.40f, 0.62f); look.highlights = new Color(0.66f, 0.60f, 0.48f); look.saturation = -26f;
            }
            return look;
        }

        /// <summary>Quanto valgono le luci del piano (LightPool.Dim). In Linear i lampadari non si "alzano" piu' da soli con la gamma:
        /// servono ~2,4 volte i valori di prima perche' le pozze di luce calda stacchino dal blu dell'ambiente (foto a seme fisso).</summary>
        public static float LightDim(int floor) => Mathf.Lerp(1.20f, 0.77f, Mathf.Clamp01((floor - 1) / 7f)) * LookTune.Dim;

        /// <summary>Atmosfera condivisa tra il piano di gioco e le foto di taratura: nebbia, luce ambiente e grading del tema.</summary>
        public static void ApplyLook(Plan plan, int floor)
        {
            var rnd = new Rng(plan.seed + 77);
            var look = LookOf(plan);
            float deep = Mathf.Clamp01((floor - 1) / 7f);
            // La foschia fonde lo sfondo lasciando leggibili gli oggetti vicini.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = rnd.Range(0.027f, 0.033f) * Mathf.Lerp(1f, 1.22f, deep) * look.density * LookTune.Fog;
            var fogColor = look.fog * LookTune.FogColor;
            fogColor.a = 1f;
            RenderSettings.fogColor = fogColor;
            float amb = Mathf.Lerp(0.48f, 0.31f, deep) * look.ambient * LookTune.Ambient;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.sky * amb;
            RenderSettings.ambientEquatorColor = look.equator * amb;
            RenderSettings.ambientGroundColor = look.ground * amb;
            RenderSettings.reflectionIntensity = 0.30f;     // il cubemap della hall illuminata, al buio, farebbe brillare i metalli
            GameSettings.SetThemeExposure(look.exposure + LookTune.Exposure);
            // grading del tema (profilo copiato in istanza: l'asset non cambia)
            var vol = FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            if (vol == null || vol.profile == null) return;
            var prof = vol.profile;
            if (prof.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments ca))
            { ca.saturation.Override(look.saturation + LookTune.Saturation); ca.contrast.Override(look.contrast + LookTune.Contrast); }
            if (prof.TryGet(out UnityEngine.Rendering.Universal.SplitToning st))
            { st.shadows.Override(look.shadows); st.highlights.Override(look.highlights); st.balance.Override(look.balance); }
            if (prof.TryGet(out UnityEngine.Rendering.Universal.Bloom bl)) bl.intensity.Override(look.bloom * LookTune.Bloom);
            if (prof.TryGet(out UnityEngine.Rendering.Universal.Vignette vg)) { vg.intensity.Override(look.vignette); vg.smoothness.Override(look.vignetteSmooth); }
            if (prof.TryGet(out UnityEngine.Rendering.Universal.FilmGrain fg)) fg.intensity.Override(look.grain);
        }
    }

    /// <summary>Ritocchi del look da riga di comando, per tarare con la build gia' fatta senza ricostruire (EV, moltiplicatori o
    /// differenze): -lookexp EV, -lookamb x, -lookfog x (densita'), -lookfogc x (luminosita' del colore della nebbia), -lookdim x
    /// (le luci del piano), -looksat d, -lookcon d, -lookbloom x. Senza argomenti non cambia niente.</summary>
    public static class LookTune
    {
        public static readonly float Exposure = Arg("-lookexp", 0f), Ambient = Arg("-lookamb", 1f), Fog = Arg("-lookfog", 1f), Dim = Arg("-lookdim", 1f),
            FogColor = Arg("-lookfogc", 1f), Saturation = Arg("-looksat", 0f), Contrast = Arg("-lookcon", 0f), Bloom = Arg("-lookbloom", 1f);

        static float Arg(string name, float fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
            return fallback;
        }
    }
}

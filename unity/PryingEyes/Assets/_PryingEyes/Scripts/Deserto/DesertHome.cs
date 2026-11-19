using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>La strada di casa (0.9.1): nel deserto, lontano dalla cattedrale, la freccia in alto al centro (la stessa dell'ascensore nei piani, HUD.SetGuideArrow)
    /// punta alla cattedrale e dice quanto manca. Si accende a <see cref="ShowFrom"/> metri dalla cattedrale (nel sagrato e dentro non serve) e si spegne a terra, in
    /// morte e durante i passaggi. La direzione e' quella in linea d'aria rispetto a dove si guarda: sulla sabbia non ci sono strade da seguire, e sotto terra (le
    /// catacombe) la cattedrale resta dove sta. La minimappa (DesertMap) mostra la stessa direzione sul bordo, quando la cattedrale e' fuori dalla finestra.</summary>
    public static class DesertHome
    {
        /// <summary>Da che distanza dalla cattedrale (m, dal suo perimetro) la freccia si accende.</summary>
        public const float ShowFrom = 90f;

        /// <summary>L'oro dei bracieri della cattedrale: la freccia dell'ascensore e' verde, questa e' di casa.</summary>
        public static readonly Color Gold = new Color(1f, 0.80f, 0.42f);

        /// <summary>Una distanza a parole: a decine di metri sotto i 300, a cinquanta fino al chilometro, poi in chilometri con una cifra ("1,4 km"). Lo stesso modo dell'obiettivo.</summary>
        public static string Distance(float m)
        {
            if (m >= 1000f) return (Mathf.Round(m / 100f) / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " km";
            return (m < 300f ? Mathf.RoundToInt(m / 10f) * 10 : Mathf.RoundToInt(m / 50f) * 50) + " m";
        }

        /// <summary>Distanza (m dal perimetro) e angolo (gradi, positivo a destra di dove guarda) della cattedrale visti da chi sta in 'p' e guarda lungo 'look'.</summary>
        public static void Bearing(Vector3 p, Vector3 look, out float distance, out float relDegrees)
        {
            distance = DesertWorld.DistToCathedral(p.x, p.z);
            DesertWorld.CathedralCentre(out float cx, out float cz);
            var to = new Vector3(cx - p.x, 0f, cz - p.z);
            look.y = 0f;
            relDegrees = look.sqrMagnitude > 0.001f && to.sqrMagnitude > 0.01f ? Vector3.SignedAngle(look.normalized, to.normalized, Vector3.up) : 0f;
        }

        /// <summary>Ogni fotogramma di chi gioca (lo chiama Guide.TickArrow al posto della freccia dell'ascensore): accende, orienta o spegne la freccia.</summary>
        public static void Tick(PlayerController pl, HUD hud)
        {
            var fd = Proc.FloorDirector.I;
            Vector3 p = pl.transform.position;
            Bearing(p, pl.cam != null ? pl.cam.forward : Vector3.forward, out float d, out float rel);
            bool show = pl.alive && !pl.Downed && d >= ShowFrom && (fd == null || !fd.transitioning);
            if (!show) { hud.SetGuideArrow(false, 0f, d, "", Gold); return; }
            hud.SetGuideArrow(true, rel, d, "cattedrale  ·  " + Distance(d), Gold);
        }
    }
}

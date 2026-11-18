using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    /// <summary>Il cielo del deserto e tutto cio' che segue l'orologio: la cupola (sole, luna, stelle, nuvole: DesertSky.shader), la luce direzionale
    /// (sole di giorno, luna di notte, con le ombre), la nebbia, l'ambiente e l'esposizione. Al chiuso (sotto un tetto, nelle catacombe) il giocatore
    /// non vede il cielo: nebbia, ambiente ed esposizione tornano a quelli delle stanze del gioco (un fattore 0..1 che sfuma in tre quarti di secondo),
    /// cosi' le lampade contano e il sole non illumina le catacombe.</summary>
    public sealed class DesertSky : MonoBehaviour
    {
        public static DesertSky I { get; private set; }

        /// <summary>L'intensita' del sole a mezzogiorno (il progetto e' Linear con grading HDR; vedi i fotogrammi di -desertotest per la taratura).</summary>
        public static float SunMax = 1.5f;
        /// <summary>Di giorno il cielo e la nebbia (insieme, perche' l'orizzonte non si spezzi) valgono fino a tanto piu' della tavolozza dell'orologio: la sabbia
        /// al sole sta sotto l'1 dopo l'esposizione e il cielo, a parita' di esposizione, deve starle alla pari.</summary>
        public static float DaySkyGain = 1.45f;
        /// <summary>Il look delle stanze del gioco (FloorDirector.ApplyLook per l'albergo), a cui si torna al chiuso.</summary>
        static readonly Color InFog = new Color(0.095f, 0.08f, 0.085f);
        static readonly Color InSky = new Color(0.065f, 0.075f, 0.105f), InEquator = new Color(0.047f, 0.047f, 0.055f), InGround = new Color(0.025f, 0.023f, 0.025f);
        const float InFogDensity = 0.030f, InExposure = -0.1f;

        public SkyState State { get; private set; }
        /// <summary>0 all'aperto, 1 al chiuso (sfumato).</summary>
        public float Indoor => indoor;
        public bool Frozen;                 // le prove fermano il tempo e fanno vedere un'ora precisa

        Light sun;
        Material skyMat;
        Transform dome;
        Camera cam;
        float indoor, exposureSent = 99f, exposureT, coveredT;
        bool covered;
        float savedFar; CameraClearFlags savedFlags; Color savedBack; bool camSaved;
        bool savedFog; FogMode savedFogMode; float savedDensity; Color savedFogColor; AmbientMode savedAmbient;

        public static DesertSky Create(Camera camera)
        {
            var go = new GameObject("DesertSky");
            var s = go.AddComponent<DesertSky>();
            s.Attach(camera);
            return s;
        }

        /// <summary>La camera del giocatore: sfondo a colore pieno (senza, il cielo predefinito della scena si disegna sopra la cupola) e piano lontano a
        /// 1500 m. Va fatto dopo AddComponent, perche' Awake gira dentro AddComponent, prima che la camera sia nota. Le impostazioni di prima si salvano
        /// qui e si rimettono in OnDestroy (solo se sono state salvate).</summary>
        void Attach(Camera camera)
        {
            cam = camera;
            if (cam == null) return;
            savedFar = cam.farClipPlane; savedFlags = cam.clearFlags; savedBack = cam.backgroundColor; camSaved = true;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ToColor(State.fog);
        }

        void Awake()
        {
            I = this;
            savedFog = RenderSettings.fog; savedFogMode = RenderSettings.fogMode; savedDensity = RenderSettings.fogDensity; savedFogColor = RenderSettings.fogColor;
            savedAmbient = RenderSettings.ambientMode;
            // il sole
            var lg = new GameObject("Sole e luna");
            lg.transform.SetParent(transform, false);
            sun = lg.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.4f;
            sun.renderMode = LightRenderMode.ForcePixel;
            RenderSettings.sun = sun;
            // la cupola
            var shader = Shader.Find("PryingEyes/DesertSky");
            if (shader != null) skyMat = new Material(shader) { name = "DesertSky" };
            dome = new GameObject("Cupola").transform;
            dome.SetParent(transform, false);
            var mf = dome.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = DomeMesh();
            var mr = dome.gameObject.AddComponent<MeshRenderer>();
            if (skyMat != null) mr.sharedMaterial = skyMat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            Apply(0f, true);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (cam != null && camSaved) { cam.farClipPlane = savedFar; cam.clearFlags = savedFlags; cam.backgroundColor = savedBack; }
            RenderSettings.fog = savedFog; RenderSettings.fogMode = savedFogMode; RenderSettings.fogDensity = savedDensity; RenderSettings.fogColor = savedFogColor;
            RenderSettings.ambientMode = savedAmbient;
            if (RenderSettings.sun == sun) RenderSettings.sun = null;
            if (skyMat != null) Destroy(skyMat);
        }

        /// <summary>Una sfera a 32 x 16 fasce, raggio 1: lo shader usa solo la direzione. I limiti sono enormi, perche' la cupola non sparisca mai.</summary>
        static Mesh DomeMesh()
        {
            const int seg = 32, rings = 16;
            var v = new Vector3[(seg + 1) * (rings + 1)];
            var t = new int[seg * rings * 6];
            for (int r = 0; r <= rings; r++)
            {
                float a = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float b = 2f * Mathf.PI * s / seg;
                    v[r * (seg + 1) + s] = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)) * 900f;
                }
            }
            int k = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int i0 = r * (seg + 1) + s, i1 = i0 + 1, i2 = i0 + seg + 1, i3 = i2 + 1;
                    t[k++] = i0; t[k++] = i2; t[k++] = i1; t[k++] = i1; t[k++] = i2; t[k++] = i3;
                }
            var m = new Mesh { name = "CupolaDeserto", vertices = v, triangles = t };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return m;
        }

        void LateUpdate()
        {
            if (!Frozen) DesertMode.Clock.Advance(Time.deltaTime);
            UpdateCovered();
            Apply(Time.deltaTime, false);
            if (cam != null) dome.position = cam.transform.position;
        }

        /// <summary>Si sta al chiuso se sopra la testa c'e' qualcosa entro 12 m (un tetto, la volta di una cripta, la sabbia sopra le catacombe).</summary>
        void UpdateCovered()
        {
            coveredT -= Time.deltaTime;
            if (coveredT > 0f || cam == null) return;
            coveredT = 0.12f;
            covered = Physics.Raycast(cam.transform.position, Vector3.up, 12f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
        }

        void Apply(float dt, bool immediate)
        {
            var s = DesertClock.Evaluate(DesertMode.Clock.hour);
            State = s;
            indoor = immediate ? (covered ? 1f : 0f) : Mathf.MoveTowards(indoor, covered ? 1f : 0f, dt / 0.75f);
            float k = indoor * indoor * (3f - 2f * indoor);

            // luce direzionale: il sole se e' piu' forte della luna, altrimenti la luna
            bool sunUp = s.sunIntensity >= s.moonIntensity;
            float inten = sunUp ? s.sunIntensity * SunMax : s.moonIntensity * 3.2f;
            Rgb col = sunUp ? s.sunColor : s.moonColor;
            Vector3 dir = sunUp ? new Vector3(s.sunX, s.sunY, s.sunZ) : new Vector3(s.moonX, s.moonY, s.moonZ);
            sun.color = new Color(col.r, col.g, col.b);
            sun.intensity = inten;
            sun.enabled = inten > 0.002f;
            sun.transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);
            sun.shadowStrength = sunUp ? 0.95f : 0.6f;

            // atmosfera: fuori quella dell'orologio, al chiuso quella delle stanze
            float gain = Mathf.Lerp(1f, DaySkyGain, s.daylight);
            var fog = Color.Lerp(Scaled(s.fog, gain), InFog, k); fog.a = 1f;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = Mathf.Lerp(s.fogDensity, InFogDensity, k);
            RenderSettings.ambientSkyColor = Color.Lerp(ToColor(s.ambientSky), InSky, k);
            RenderSettings.ambientEquatorColor = Color.Lerp(ToColor(s.ambientEquator), InEquator, k);
            RenderSettings.ambientGroundColor = Color.Lerp(ToColor(s.ambientGround), InGround, k);
            if (cam != null) cam.backgroundColor = fog;

            // esposizione (SetThemeExposure riscrive il profilo del volume: a quattro scatti al secondo basta)
            exposureT -= dt;
            float ev = Mathf.Lerp(s.exposure, InExposure, k);
            if (immediate || (exposureT <= 0f && Mathf.Abs(ev - exposureSent) > 0.01f))
            {
                exposureT = 0.25f; exposureSent = ev;
                GameSettings.SetThemeExposure(ev);
            }

            // la cupola
            if (skyMat != null)
            {
                Shader.SetGlobalVector("_DSHorizon", ToVec(s.horizon, gain));
                Shader.SetGlobalVector("_DSZenith", ToVec(s.zenith, gain));
                Shader.SetGlobalVector("_DSFog", ToVec(s.fog, gain));
                Shader.SetGlobalVector("_DSSunDir", new Vector4(s.sunX, s.sunY, s.sunZ, Mathf.Clamp01(s.sunHeight * 6f + 0.35f)));
                Shader.SetGlobalVector("_DSSunColor", new Vector4(s.sunColor.r, s.sunColor.g, s.sunColor.b, 1f));
                Shader.SetGlobalVector("_DSMoonDir", new Vector4(s.moonX, s.moonY, s.moonZ, Mathf.Clamp01(-s.sunHeight * 6f + 0.35f)));
                float cloudy = 0.20f + 0.10f * Mathf.Sin(DesertMode.Clock.day * 1.7f + 0.6f);      // un cielo di deserto: per lo piu' sereno, qualche velo
                var cloud = Rgb.Lerp(new Rgb(0.10f, 0.11f, 0.16f), new Rgb(0.95f, 0.93f, 0.90f), s.daylight);
                cloud = Rgb.Lerp(cloud, new Rgb(1.0f, 0.62f, 0.45f), s.twilight * 0.7f);
                Shader.SetGlobalVector("_DSCloud", new Vector4(cloud.r * gain, cloud.g * gain, cloud.b * gain, cloudy));
                Shader.SetGlobalVector("_DSParams", new Vector4(s.stars, Time.time, s.twilight, 0f));
            }
        }

        static Color ToColor(Rgb c) => new Color(c.r, c.g, c.b);
        static Color Scaled(Rgb c, float g) => new Color(c.r * g, c.g * g, c.b * g);
        static Vector4 ToVec(Rgb c, float g = 1f) => new Vector4(c.r * g, c.g * g, c.b * g, 1f);

        /// <summary>Qualcuno ha riscritto l'esposizione (ApplyLook di un piano): al prossimo giro il cielo rimette la sua.</summary>
        public void Resync() { exposureSent = 99f; exposureT = 0f; }

        /// <summary>Per le prove: l'ora del giorno, subito, e (se 'freeze') ferma il tempo.</summary>
        public void SetHour(float hour, bool freeze)
        {
            DesertMode.Clock.hour = Mathf.Repeat(hour, 24f);
            Frozen = freeze;
            Apply(0f, true);
        }
    }
}

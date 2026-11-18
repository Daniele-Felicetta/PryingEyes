using PryingEyes.Proc;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        /// <summary>Quanta della luce del sole e dell'ambiente resta a mezzogiorno (1 = tutta). Il cielo no, e l'esposizione neanche: vedi Apply.</summary>
        public static float DayLight = 0.58f;
        /// <summary>Di giorno il cielo e la nebbia (insieme, perche' l'orizzonte non si spezzi) valgono fino a tanto piu' della tavolozza dell'orologio: la sabbia
        /// al sole sta sotto l'1 dopo l'esposizione e il cielo, a parita' di esposizione, deve starle alla pari.</summary>
        public static float DaySkyGain = 1.45f;
        /// <summary>Lo stesso guadagno per lo zenit e per le nuvole (l'orizzonte e la nebbia usano DaySkyGain): il cielo piu' scuro e blu in alto e chiaro e pallido sull'orizzonte, come un cielo vero; con il guadagno dell'orizzonte anche allo zenit il tone mapping lo faceva diventare lattiginoso.</summary>
        public static float DayZenithGain = 1.0f, DayCloudGain = 1.35f;
        /// <summary>Di quanto un'ombra di nuvola scurisce la sabbia al sole pieno (0..1): la luce diretta e' circa i due terzi di quella che arriva.</summary>
        public static float CloudShadow = 0.30f;
        /// <summary>Quanto e' forte il bagliore attorno al sole nella cupola (e nella nebbia verso il sole): 1 e' il massimo, meno e' piu' discreto e il disco si stacca meglio dal cielo.</summary>
        public static float SunGlow = 1f;
        /// <summary>Il look delle stanze del gioco (FloorDirector.ApplyLook per l'albergo), a cui si torna al chiuso.</summary>
        /// <summary>Solo per le prove: la copertura delle nuvole (0..1) al posto di quella del giorno; -1 = quella normale (fase nuvole).</summary>
        public static float CloudOverride = -1f;
        static readonly Color InFog = new Color(0.095f, 0.08f, 0.085f);
        static readonly Color InSky = new Color(0.065f, 0.075f, 0.105f), InEquator = new Color(0.047f, 0.047f, 0.055f), InGround = new Color(0.025f, 0.023f, 0.025f);
        const float InFogDensity = 0.030f, InExposure = -0.1f;
        /// <summary>All'aperto le ombre del sole arrivano tre volte piu' lontano dei 70 m dell'asset (i fianchi delle dune e le torri hanno ombra a 200 m, e il taglio a 70 non si vede piu').</summary>
        public const float OutdoorShadowRange = 3f;

        public SkyState State { get; private set; }
        /// <summary>Il vento e la tempesta di sabbia di questo momento (DesertWeather): nebbia, sole, colore della sabbia nell'aria e vento degli shader li prendono da qui.</summary>
        public WeatherState Weather { get; private set; }
        /// <summary>0 di giorno, 1 di notte (sfumato al crepuscolo): quando si accendono le finestre delle facciate e i fuochi dei campi.</summary>
        public float Night { get; private set; }
        /// <summary>0 all'aperto, 1 al chiuso (sfumato).</summary>
        public float Indoor => indoor;
        /// <summary>Quanta luce c'e' ora (0 al buio, 1 a mezzogiorno): le cose senza luce propria (le particelle di polvere, che sono Unlit) si moltiplicano per questo, altrimenti all'esposizione
        /// del tramonto o della notte il loro colore pieno e' bianco.</summary>
        public float LightLevel { get; private set; } = 1f;
        public bool Frozen;                 // le prove fermano il tempo e fanno vedere un'ora precisa
        float stormOverride = -1f;          // le prove: una tempesta a comando (negativo = quella vera)

        /// <summary>Per le prove: una tempesta di quella forza (0..1) finche' non si richiama con un valore negativo.</summary>
        public void ForceStorm(float amount) { stormOverride = amount; }

        Light sun;
        Material skyMat;
        Transform dome;
        Camera cam;
        float indoor, exposureSent = 99f, exposureT, coveredT;
        bool covered;
        // il grading dello schermo: il volume del gioco, le due voci che il deserto riscrive, e il grading della stanza (quello scritto da ApplyLook) a cui si torna al chiuso
        Volume gradeVol; ColorAdjustments gradeCa; SplitToning gradeSt; Bloom gradeBl; Tonemapping gradeTm;
        /// <summary>Solo per le prove (`-desertonly look`): sposta l'esposizione del deserto (EV) e sceglie il tone mapping (-1 non lo tocca, 0 ACES, 1 Neutral, 2 nessuno).</summary>
        public static float ExposureOffset = 0f;
        public static int TestTonemap = -1;
        /// <summary>Quanto (EV) l'immagine si scurisce guardando dritto verso il sole.</summary>
        public static float SunAdaptEV = 0.9f;
        float sunAdapt;
        Grade indoorLook; bool indoorCaptured; float gradeT;
        /// <summary>L'ultimo grading mandato al volume (all'aperto o sfumato col chiuso): le prove lo guardano.</summary>
        public Grade CurrentGrade { get; private set; }
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
            GameSettings.SetShadowRange(1f);
            // il grading del deserto non deve restare ai piani: si rimette quello che ApplyLook aveva scritto
            if (indoorCaptured) WriteGrade(indoorLook);
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
            var wx = DesertWeather.Evaluate(DesertWorld.Seed, DesertMode.Clock.day, DesertMode.Clock.hour, s.daylight);
            if (stormOverride >= 0f) wx = DesertWeather.WithStorm(wx, stormOverride, s.daylight);
            Weather = wx;
            Night = 1f - Stealth.Smooth(0.05f, 0.45f, s.daylight);
            indoor = immediate ? (covered ? 1f : 0f) : Mathf.MoveTowards(indoor, covered ? 1f : 0f, dt / 0.75f);
            float k = indoor * indoor * (3f - 2f * indoor);

            // luce direzionale: il sole se e' piu' forte della luna, altrimenti la luna (la sabbia nell'aria li oscura)
            bool sunUp = s.sunIntensity >= s.moonIntensity;
            // di giorno la luce (sole e ambiente) e' piu' bassa di prima, e l'esposizione e il cielo no: la sabbia al sole stava sulla spalla del tone mapping, tutta chiara e senza variazioni (la gamma
            // tra l'ombra di una duna e il suo dorso era di venti livelli su 255); un'ora piu' bassa la porta a meta' curva, dorata e con tutto lo spazio per le increspature, e il cielo resta com'e'
            // (solo col sole alto: al mattino e alla sera il sole e' radente, la luce sul terreno e' gia' poca e la sabbia non brucia, quindi li' resta com'era)
            float dayLight = Mathf.Lerp(1f, DayLight, Stealth.Smooth(0.30f, 0.85f, s.sunHeight) * s.daylight);
            float inten = sunUp ? s.sunIntensity * SunMax * wx.sunMul * dayLight : s.moonIntensity * 3.2f * (0.5f + 0.5f * wx.sunMul);
            Rgb col = sunUp ? s.sunColor : s.moonColor;
            Vector3 dir = sunUp ? new Vector3(s.sunX, s.sunY, s.sunZ) : new Vector3(s.moonX, s.moonY, s.moonZ);
            sun.color = new Color(col.r, col.g, col.b);
            sun.intensity = inten;
            sun.enabled = inten > 0.002f;
            sun.transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);
            sun.shadowStrength = sunUp ? 0.95f : 0.6f;

            // atmosfera: fuori quella dell'orologio, al chiuso quella delle stanze
            float gain = Mathf.Lerp(1f, DaySkyGain, s.daylight);
            float zenithGain = Mathf.Lerp(1f, DayZenithGain, s.daylight), cloudGain = Mathf.Lerp(1f, DayCloudGain, s.daylight);
            // in una tempesta l'aria e' sabbia: la nebbia, l'orizzonte e il cielo prendono il suo colore (scuro di notte), e la nebbia si infittisce
            var dust = new Rgb(0.80f, 0.58f, 0.36f) * (0.12f + 0.88f * s.daylight);
            var horizon = Rgb.Lerp(s.horizon, dust, wx.sand * 0.8f);
            var fogRgb = Rgb.Lerp(s.fog, dust * 0.92f, wx.sand * 0.8f);
            var zenith = Rgb.Lerp(s.zenith, dust * 0.8f, wx.sand * 0.55f);
            var fog = Color.Lerp(Scaled(fogRgb, gain), InFog, k); fog.a = 1f;
            // la nebbia prende il colore del cielo nella direzione in cui si guarda: sull'orizzonte dalla parte del sole il cielo ha il suo bagliore (lo stesso della cupola, DesertSky.shader), e se la
            // nebbia non lo avesse la sabbia lontana si staccherebbe dal cielo, scura contro il sole. Vale per tutte le cose, anche per le facciate, che hanno la nebbia di URP.
            if (cam != null)
            {
                var fwd = cam.transform.forward; float hl = Mathf.Sqrt(fwd.x * fwd.x + fwd.z * fwd.z);
                if (hl > 0.05f)
                {
                    float sd = Mathf.Clamp01((fwd.x * s.sunX + fwd.z * s.sunZ) / hl), clear = 1f - wx.sand;
                    // la stessa formula della cupola (DesertSky.shader): l'alone si mescola al colore del sole, poi sull'orizzonte del tramonto si somma il chiarore
                    float hiSun = Mathf.Clamp01(s.sunHeight * 2.2f);
                    float haloW = (Mathf.Pow(sd, Mathf.Lerp(5f, 9f, hiSun)) * 0.50f + Mathf.Pow(sd, 36f) * 0.40f) * Mathf.Clamp01(s.sunHeight * 6f + 0.35f) * (0.35f + 0.65f * clear) * (1f - k) * SunGlow;
                    haloW = Mathf.Clamp01(haloW);
                    fog.r = Mathf.Lerp(fog.r, s.sunColor.r, haloW); fog.g = Mathf.Lerp(fog.g, s.sunColor.g, haloW); fog.b = Mathf.Lerp(fog.b, s.sunColor.b, haloW);
                    float rim = sd * sd * s.twilight * 0.45f * clear * (1f - k) * SunGlow;
                    fog.r += s.sunColor.r * rim; fog.g += s.sunColor.g * rim; fog.b += s.sunColor.b * rim;
                }
            }
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = Mathf.Lerp(s.fogDensity * wx.fogMul, InFogDensity, k);
            RenderSettings.ambientSkyColor = Color.Lerp(ToColor(s.ambientSky) * dayLight, InSky, k);
            RenderSettings.ambientEquatorColor = Color.Lerp(ToColor(s.ambientEquator) * dayLight, InEquator, k);
            RenderSettings.ambientGroundColor = Color.Lerp(ToColor(s.ambientGround) * dayLight, InGround, k);
            LightLevel = Mathf.Clamp01(RenderSettings.ambientEquatorColor.grayscale * 1.7f + (sun.enabled ? sun.intensity : 0f) * 0.3f);
            if (cam != null) cam.backgroundColor = fog;

            // l'occhio si abitua: guardando verso il sole (alto o basso, in aria pulita) l'immagine si scurisce di un po' e il cielo attorno al disco resta colorato invece di sbiancare; guardando
            // altrove l'occhio si riapre piano. Col tempo fermo (le prove, le foto) e' gia' abituato.
            float facing = 0f;
            if (cam != null && k < 0.5f)
            {
                float toward = cam.transform.forward.x * s.sunX + cam.transform.forward.y * s.sunY + cam.transform.forward.z * s.sunZ;
                facing = Stealth.Smooth(0.70f, 0.995f, toward) * Mathf.Clamp01(s.sunHeight * 6f + 0.35f) * (1f - wx.sand) * Mathf.Clamp01(s.sunIntensity * 3f);
            }
            sunAdapt = immediate || Frozen ? facing : Mathf.MoveTowards(sunAdapt, facing, dt * (facing > sunAdapt ? 1.6f : 0.55f));
            // esposizione (SetThemeExposure riscrive il profilo del volume: a quattro scatti al secondo basta; mentre l'occhio si adatta, venticinque)
            exposureT -= dt;
            float ev = Mathf.Lerp(s.exposure + ExposureOffset - SunAdaptEV * sunAdapt, InExposure, k);
            if (immediate || (exposureT <= 0f && Mathf.Abs(ev - exposureSent) > 0.01f))
            {
                exposureT = Mathf.Abs(sunAdapt - facing) > 0.002f ? 0.04f : 0.25f; exposureSent = ev;
                GameSettings.SetThemeExposure(ev);
            }

            // grading dello schermo (a dieci scatti al secondo) e distanza delle ombre: all'aperto quelli del deserto, al chiuso quelli della stanza
            gradeT -= dt;
            if (immediate || gradeT <= 0f) { gradeT = 0.1f; ApplyGrade(k, s, wx); }
            GameSettings.SetShadowRange(k > 0.5f ? 1f : OutdoorShadowRange);

            FacadeKit.SetNight(Night);      // le finestre accese delle facciate si vedono al buio
            Shader.SetGlobalFloat("_DSNight", Night);       // e le fiamme dei fuochi
            Shader.SetGlobalFloat("_DSWind", wx.wind);      // e il vento che piega i cespugli

            // la cupola
            if (skyMat != null)
            {
                Shader.SetGlobalVector("_DSHorizon", ToVec(horizon, gain));
                Shader.SetGlobalVector("_DSZenith", ToVec(zenith, zenithGain));
                Shader.SetGlobalVector("_DSFog", ToVec(fogRgb, gain));
                Shader.SetGlobalVector("_DSSunDir", new Vector4(s.sunX, s.sunY, s.sunZ, Mathf.Clamp01(s.sunHeight * 6f + 0.35f)));
                Shader.SetGlobalVector("_DSSunColor", new Vector4(s.sunColor.r, s.sunColor.g, s.sunColor.b, 1f));
                Shader.SetGlobalFloat("_DSSunGlow", SunGlow);
                Shader.SetGlobalVector("_DSMoonDir", new Vector4(s.moonX, s.moonY, s.moonZ, Mathf.Clamp01(-s.sunHeight * 6f + 0.35f)));
                float cloudy = CloudOverride >= 0f ? CloudOverride : 0.27f + 0.08f * Mathf.Sin(DesertMode.Clock.day * 1.7f + 0.6f);      // un cielo di deserto: per lo piu' sereno e profondo, ma con le sue nuvole (la copertura 0,1 e' circa il 6% del cielo, 0,2 il 22%): il primo giorno ne ha un po', il terzo e' quasi limpido
                var cloud = Rgb.Lerp(new Rgb(0.10f, 0.11f, 0.16f), new Rgb(0.95f, 0.93f, 0.90f), s.daylight);
                cloud = Rgb.Lerp(cloud, new Rgb(1.0f, 0.62f, 0.45f), s.twilight * 0.7f);
                Shader.SetGlobalVector("_DSCloud", new Vector4(cloud.r * cloudGain, cloud.g * cloudGain, cloud.b * cloudGain, cloudy));
                // le ombre delle nuvole sulla sabbia e sulle cose (z: quanto scuriscono, w: la copertura): macchie larghe che scorrono col vento, solo di giorno e con l'aria pulita
                Shader.SetGlobalVector("_DSCloudShadow", new Vector4(0f, 0f, CloudShadow * s.daylight * (1f - wx.sand) * (1f - k), cloudy));
                Shader.SetGlobalVector("_DSParams", new Vector4(s.stars * (1f - wx.sand), Time.time, s.twilight, 1f - wx.sand));
            }
        }

        // ------------------------------------------------------------------ il grading dello schermo
        bool FindGradeVolume()
        {
            if (gradeVol != null && gradeCa != null && gradeSt != null) return true;
            gradeVol = FindFirstObjectByType<Volume>();
            gradeCa = null; gradeSt = null; gradeBl = null; gradeTm = null;
            if (gradeVol == null || gradeVol.profile == null) return false;
            gradeVol.profile.TryGet(out gradeBl);
            gradeVol.profile.TryGet(out gradeTm);
            return gradeVol.profile.TryGet(out gradeCa) && gradeVol.profile.TryGet(out gradeSt);
        }

        /// <summary>Quello che il volume ha adesso e' il grading della stanza (ApplyLook l'ha appena scritto): lo si tiene per tornarci al chiuso. Il filtro colore non lo scrive nessuno, resta neutro.</summary>
        void CaptureIndoor()
        {
            if (!FindGradeVolume()) return;
            indoorLook = new Grade
            {
                saturation = gradeCa.saturation.value, contrast = gradeCa.contrast.value, filter = new Rgb(1f, 1f, 1f),
                shadows = FromColor(gradeSt.shadows.value), highlights = FromColor(gradeSt.highlights.value), balance = gradeSt.balance.value,
            };
            GameSettings.ThemeEffects(out indoorLook.grain, out indoorLook.vignette, out indoorLook.bloom);
            indoorLook.bloomThreshold = gradeBl != null ? gradeBl.threshold.value : 1f;
            indoorCaptured = true;
        }

        void ApplyGrade(float indoorK, SkyState s, WeatherState wx)
        {
            if (!indoorCaptured) CaptureIndoor();
            if (!indoorCaptured) return;
            var g = Grade.Lerp(DesertGrade.Outdoors(s, wx), indoorLook, indoorK);
            CurrentGrade = g;
            WriteGrade(g);
        }

        void WriteGrade(Grade g)
        {
            if (!FindGradeVolume()) return;
            gradeCa.saturation.Override(g.saturation); gradeCa.contrast.Override(g.contrast); gradeCa.colorFilter.Override(ToColor(g.filter));
            gradeSt.shadows.Override(ToColor(g.shadows)); gradeSt.highlights.Override(ToColor(g.highlights)); gradeSt.balance.Override(g.balance);
            if (gradeBl != null) gradeBl.threshold.Override(g.bloomThreshold);
            if (gradeTm != null && TestTonemap >= 0) gradeTm.mode.Override(TestTonemap == 2 ? TonemappingMode.None : TestTonemap == 1 ? TonemappingMode.Neutral : TonemappingMode.ACES);
            GameSettings.SetThemeEffects(gradeVol, g.grain, g.vignette, g.bloom);
        }

        static Rgb FromColor(Color c) => new Rgb(c.r, c.g, c.b);
        static Color ToColor(Rgb c) => new Color(c.r, c.g, c.b);
        static Color Scaled(Rgb c, float g) => new Color(c.r * g, c.g * g, c.b * g);
        static Vector4 ToVec(Rgb c, float g = 1f) => new Vector4(c.r * g, c.g * g, c.b * g, 1f);

        /// <summary>Qualcuno ha riscritto l'esposizione (ApplyLook di un piano): al prossimo giro il cielo rimette la sua.</summary>
        public void Resync() { exposureSent = 99f; exposureT = 0f; CaptureIndoor(); }

        /// <summary>Per le prove: l'ora del giorno, subito, e (se 'freeze') ferma il tempo.</summary>
        public void SetHour(float hour, bool freeze)
        {
            DesertMode.Clock.hour = Mathf.Repeat(hour, 24f);
            Frozen = freeze;
            Apply(0f, true);
        }
    }
}

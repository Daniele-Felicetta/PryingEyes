using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes
{
    /// <summary>Le altre impostazioni del menu (0.8.0), salvate in PlayerPrefs come quelle di GameSettings.cs. Ogni valore ha il suo default (il gioco
    /// com'era prima), il suo limite, il suo effetto: audio (ambiente, tema del menu, suoni dell'interfaccia), immagine (scala di rendering,
    /// anti-aliasing, qualita' delle ombre, grana, vignettatura, bagliore), comandi (sensibilita' del controller, asse Y invertito, mira, corsa e
    /// abbassarsi "tieni premuto" o "a interruttore"), gioco (mirino, contatore dei fotogrammi, pausa quando la finestra perde il fuoco).
    /// L'immagine tocca l'asset della pipeline solo nel gioco vero (non nell'editor ne' nelle prove automatiche: un asset modificato resterebbe tale).</summary>
    public static partial class GameSettings
    {
        const string KeyAmbience = "vol_ambience", KeyMenuMusic = "vol_menumusic", KeyUiSounds = "ui_sounds";
        const string KeyRenderScale = "gfx_renderscale", KeyMsaa = "gfx_msaa", KeyShadows = "gfx_shadows";
        const string KeyGrain = "gfx_grain", KeyVignette = "gfx_vignette", KeyBloom = "gfx_bloom";
        const string KeyPadSens = "input_pad_sensitivity", KeyInvertY = "input_invert_y", KeyInvertPadY = "input_invert_pad_y";
        const string KeyAimMode = "input_aim_mode", KeyCrouchMode = "input_crouch_mode", KeySprintMode = "input_sprint_mode";
        const string KeyCrosshair = "ui_crosshair", KeyFps = "ui_fps", KeyPauseFocus = "pause_on_focus_loss";

        /// <summary>Scala di rendering scelte nel menu (percento della risoluzione dello schermo) e i campioni dell'anti-aliasing (1 = spento).</summary>
        public static readonly int[] RenderScales = { 50, 60, 70, 80, 90, 100 };
        public static readonly int[] MsaaSamples = { 1, 2, 4 };
        /// <summary>Mirino: classico (quattro trattini e punto), solo il punto, nessuno.</summary>
        public const int CrosshairClassic = 0, CrosshairDot = 1, CrosshairNone = 2;
        /// <summary>Come si comanda mira, corsa e abbassarsi con tastiera e mouse: tenendo premuto o con un tocco che accende e spegne.</summary>
        public const int ModeHold = 0, ModeToggle = 1;
        public const float PadSensitivityMin = 0.25f, PadSensitivityMax = 3f;

        // valori correnti: i default sono il gioco com'era prima di queste impostazioni
        static float ambience = 1f, menuMusic = 1f, grain = 1f, vignette = 1f, bloom = 1f, padSensitivity = 1f;
        static int renderScalePct = 100, msaa = 4, shadows = 2, aimMode, crouchMode, sprintMode, crosshair;
        static bool uiSounds = true, invertY, invertPadY, showFps, pauseOnFocusLoss = true;

        // ---- audio
        /// <summary>Ambiente: il ronzio delle stanze, il vento, il brusio, gli scricchiolii e i rintocchi lontani (prima seguivano gli Effetti).</summary>
        public static float Ambience { get { Load(); return ambience; } }
        /// <summary>Il tema musicale del menu principale (MenuMusic): sta sotto il volume Musica.</summary>
        public static float MenuMusic { get { Load(); return menuMusic; } }
        /// <summary>I suoni dei pulsanti e dei cursori del menu (MenuSound).</summary>
        public static bool UiSounds { get { Load(); return uiSounds; } }

        // ---- immagine
        public static int RenderScalePercent { get { Load(); return renderScalePct; } }
        public static int Msaa { get { Load(); return msaa; } }
        /// <summary>Qualita' delle ombre: 0 bassa, 1 media, 2 alta (com'era).</summary>
        public static int ShadowQuality { get { Load(); return shadows; } }
        /// <summary>Quanto della grana, della vignettatura e del bagliore pensati per il tema si vede (0..1; 1 = com'erano).</summary>
        public static float GrainAmount { get { Load(); return grain; } }
        public static float VignetteAmount { get { Load(); return vignette; } }
        public static float BloomAmount { get { Load(); return bloom; } }

        // ---- comandi
        /// <summary>Sensibilita' dello stick destro (moltiplicatore, 0,25..3). Il mouse e il touch seguono Sensitivity.</summary>
        public static float PadSensitivity { get { Load(); return padSensitivity; } }
        public static bool InvertMouseY { get { Load(); return invertY; } }
        public static bool InvertPadY { get { Load(); return invertPadY; } }
        public static int AimMode { get { Load(); return aimMode; } }
        public static int CrouchMode { get { Load(); return crouchMode; } }
        public static int SprintMode { get { Load(); return sprintMode; } }
        public static bool AimToggle => AimMode == ModeToggle;
        public static bool CrouchToggle => CrouchMode == ModeToggle;
        public static bool SprintToggle => SprintMode == ModeToggle;

        // ---- gioco
        public static int Crosshair { get { Load(); return crosshair; } }
        public static bool ShowFps { get { Load(); return showFps; } }
        /// <summary>Il gioco si mette in pausa quando la finestra perde il fuoco (in cooperativa la pausa blocca solo i comandi).</summary>
        public static bool PauseOnFocusLoss { get { Load(); return pauseOnFocusLoss; } }

        static int Index(int[] values, int v, int fallback)
        {
            int i = System.Array.IndexOf(values, v);
            return i >= 0 ? values[i] : fallback;
        }

        static bool Flag(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) != 0;
        static void SaveFlag(string key, bool v) => PlayerPrefs.SetInt(key, v ? 1 : 0);

        /// <summary>Legge le altre impostazioni (lo chiama Load): valori non validi tornano al default.</summary>
        static void LoadMore()
        {
            ambience = Clamp01Or(PlayerPrefs.GetFloat(KeyAmbience, 1f), 1f);
            menuMusic = Clamp01Or(PlayerPrefs.GetFloat(KeyMenuMusic, 1f), 1f);
            uiSounds = Flag(KeyUiSounds, true);
            renderScalePct = Index(RenderScales, PlayerPrefs.GetInt(KeyRenderScale, 100), 100);
            msaa = Index(MsaaSamples, PlayerPrefs.GetInt(KeyMsaa, 4), 4);
            shadows = Mathf.Clamp(PlayerPrefs.GetInt(KeyShadows, 2), 0, 2);
            grain = Clamp01Or(PlayerPrefs.GetFloat(KeyGrain, 1f), 1f);
            vignette = Clamp01Or(PlayerPrefs.GetFloat(KeyVignette, 1f), 1f);
            bloom = Clamp01Or(PlayerPrefs.GetFloat(KeyBloom, 1f), 1f);
            padSensitivity = Valid(PlayerPrefs.GetFloat(KeyPadSens, 1f), PadSensitivityMin, PadSensitivityMax, 1f);
            invertY = Flag(KeyInvertY, false);
            invertPadY = Flag(KeyInvertPadY, false);
            aimMode = Mathf.Clamp(PlayerPrefs.GetInt(KeyAimMode, 0), 0, 1);
            crouchMode = Mathf.Clamp(PlayerPrefs.GetInt(KeyCrouchMode, 0), 0, 1);
            sprintMode = Mathf.Clamp(PlayerPrefs.GetInt(KeySprintMode, 0), 0, 1);
            crosshair = Mathf.Clamp(PlayerPrefs.GetInt(KeyCrosshair, 0), 0, 2);
            showFps = Flag(KeyFps, false);
            pauseOnFocusLoss = Flag(KeyPauseFocus, true);
        }

        static float Clamp01Or(float v, float fallback) => Valid(v, 0f, 1f, fallback);

        // ---- setter
        public static void SetAmbience(float v) { Load(); ambience = Clamp01Or(v, 1f); PlayerPrefs.SetFloat(KeyAmbience, ambience); }
        public static void SetMenuMusic(float v) { Load(); menuMusic = Clamp01Or(v, 1f); PlayerPrefs.SetFloat(KeyMenuMusic, menuMusic); }
        public static void SetUiSounds(bool on) { Load(); uiSounds = on; SaveFlag(KeyUiSounds, on); }
        public static void SetRenderScale(int percent) { Load(); renderScalePct = Index(RenderScales, percent, 100); PlayerPrefs.SetInt(KeyRenderScale, renderScalePct); ApplyGraphics(); }
        public static void SetMsaa(int samples) { Load(); msaa = Index(MsaaSamples, samples, 4); PlayerPrefs.SetInt(KeyMsaa, msaa); ApplyGraphics(); }
        public static void SetShadowQuality(int level) { Load(); shadows = Mathf.Clamp(level, 0, 2); PlayerPrefs.SetInt(KeyShadows, shadows); ApplyGraphics(); }
        public static void SetGrain(float v) { Load(); grain = Clamp01Or(v, 1f); PlayerPrefs.SetFloat(KeyGrain, grain); ApplyEffects(); }
        public static void SetVignette(float v) { Load(); vignette = Clamp01Or(v, 1f); PlayerPrefs.SetFloat(KeyVignette, vignette); ApplyEffects(); }
        public static void SetBloom(float v) { Load(); bloom = Clamp01Or(v, 1f); PlayerPrefs.SetFloat(KeyBloom, bloom); ApplyEffects(); }
        public static void SetPadSensitivity(float v) { Load(); padSensitivity = Valid(v, PadSensitivityMin, PadSensitivityMax, 1f); PlayerPrefs.SetFloat(KeyPadSens, padSensitivity); }
        public static void SetInvertMouseY(bool on) { Load(); invertY = on; SaveFlag(KeyInvertY, on); }
        public static void SetInvertPadY(bool on) { Load(); invertPadY = on; SaveFlag(KeyInvertPadY, on); }
        public static void SetAimMode(int mode) { Load(); aimMode = Mathf.Clamp(mode, 0, 1); PlayerPrefs.SetInt(KeyAimMode, aimMode); }
        public static void SetCrouchMode(int mode) { Load(); crouchMode = Mathf.Clamp(mode, 0, 1); PlayerPrefs.SetInt(KeyCrouchMode, crouchMode); }
        public static void SetSprintMode(int mode) { Load(); sprintMode = Mathf.Clamp(mode, 0, 1); PlayerPrefs.SetInt(KeySprintMode, sprintMode); }
        public static void SetCrosshair(int style) { Load(); crosshair = Mathf.Clamp(style, 0, 2); PlayerPrefs.SetInt(KeyCrosshair, crosshair); }
        public static void SetShowFps(bool on) { Load(); showFps = on; SaveFlag(KeyFps, on); }
        public static void SetPauseOnFocusLoss(bool on) { Load(); pauseOnFocusLoss = on; SaveFlag(KeyPauseFocus, on); }

        // ---- ripristino: ogni scheda del menu riporta i suoi valori ai default (luminosita' e simili restano di GameSettings.cs)
        public static void ResetImage()
        {
            SetBrightness(0.5f); SetFov(75f);
            SetRenderScale(100); SetMsaa(4); SetShadowQuality(2);
            SetGrain(1f); SetVignette(1f); SetBloom(1f);
        }

        public static void ResetAudio()
        {
            SetMaster(1f); SetMusic(1f); SetMenuMusic(1f); SetEffects(1f); SetAmbience(1f); SetUiSounds(true);
        }

        public static void ResetControls()
        {
            SetSensitivity(1f); SetPadSensitivity(1f); SetInvertMouseY(false); SetInvertPadY(false);
            SetAimMode(ModeHold); SetCrouchMode(ModeHold); SetSprintMode(ModeHold);
        }

        public static void ResetGame()
        {
            SetThirdPerson(false); SetCrosshair(CrosshairClassic); SetMotion(1f); SetCoopHudScale(1f); SetShowFps(false); SetPauseOnFocusLoss(true);
        }

        // ---- qualita' dell'immagine: l'asset della pipeline URP (si cambia in memoria, nel gioco vero)
        static bool gfxCaptured;
        static int baseMainRes, baseLightRes, baseCascades;
        static float baseDistance;

        /// <summary>Per le prove: le impostazioni di immagine toccano l'asset solo nel gioco vero.</summary>
        public static bool GraphicsLive => !Application.isEditor && !silentRun;

        static int quiet = -1;
        /// <summary>Una prova automatica (-batchmode, -padtest, -uitest...) o -nomusic: niente tema del menu ne' toni dell'interfaccia. Un agente che
        /// prova il gioco non deve riempire di suoni il PC di chi lavora.</summary>
        public static bool QuietRun
        {
            get
            {
                if (quiet < 0)
                    quiet = silentRun || System.Array.Exists(System.Environment.GetCommandLineArgs(),
                        a => a == "-nomusic" || (a.Length > 5 && a[0] == '-' && a.EndsWith("test", System.StringComparison.OrdinalIgnoreCase))) ? 1 : 0;
                return quiet == 1;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InitializeGraphics() => ApplyGraphics();

        static void ApplyGraphics()
        {
            if (!GraphicsLive) return;
            Load();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            if (!gfxCaptured)
            {
                gfxCaptured = true;
                baseMainRes = urp.mainLightShadowmapResolution; baseLightRes = urp.additionalLightsShadowmapResolution;
                baseCascades = urp.shadowCascadeCount; baseDistance = urp.shadowDistance;
            }
            urp.renderScale = renderScalePct / 100f;
            urp.msaaSampleCount = msaa;
            // 2 = i valori dell'asset (com'era); 1 e 0 li abbassano: meno nitidezza e meno lontano, molto meno lavoro per la scheda video
            int[] res = { 1024, 2048, 4096 };
            int[] cascades = { 2, 3, 4 };
            float[] distance = { 28f, 48f, 70f };
            urp.mainLightShadowmapResolution = shadows == 2 ? baseMainRes : Mathf.Min(baseMainRes, res[shadows]);
            urp.additionalLightsShadowmapResolution = shadows == 2 ? baseLightRes : Mathf.Min(baseLightRes, res[shadows]);
            urp.shadowCascadeCount = shadows == 2 ? baseCascades : Mathf.Min(baseCascades, cascades[shadows]);
            urp.shadowDistance = (shadows == 2 ? baseDistance : Mathf.Min(baseDistance, distance[shadows])) * shadowRange;
        }

        static float shadowRange = 1f, rangeBase = -1f;
        public static float ShadowRange => shadowRange;

        /// <summary>Il deserto vuole le ombre del sole lontane (le dune, le torri: oltre i 70 m dell'asset non c'e' ombra e il taglio si vede, soprattutto col sole basso): un fattore sulla distanza
        /// scelta dalla qualita' dell'immagine. Vale anche nelle prove (in memoria, nel giocatore; nell'editor l'asset non si tocca). Si rimette a 1 uscendo dal deserto.</summary>
        public static void SetShadowRange(float scale)
        {
            scale = Mathf.Clamp(scale, 1f, 6f);
            if (Mathf.Abs(scale - shadowRange) < 0.01f) return;
            shadowRange = scale;
            if (GraphicsLive) { ApplyGraphics(); return; }
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null || Application.isEditor) return;
            if (rangeBase < 0f) rangeBase = urp.shadowDistance;        // il primo valore dell'asset, prima del nostro
            urp.shadowDistance = rangeBase * shadowRange;
        }

        // ---- grana, vignettatura e bagliore: il tema decide quanto, la scelta del giocatore lo scala
        static Volume fxVolume;
        static bool themeFx;
        static float themeGrain, themeVignette, themeBloom;

        /// <summary>Il look del tema in corso (FloorDirector.ApplyLook) fissa i valori di riferimento di grana, vignettatura e bagliore;
        /// quello che si vede e' quel valore per la scelta delle impostazioni (a 100% com'era sempre stato).</summary>
        public static void SetThemeEffects(Volume volume, float grainBase, float vignetteBase, float bloomBase)
        {
            fxVolume = volume; themeFx = true;
            themeGrain = grainBase; themeVignette = vignetteBase; themeBloom = bloomBase;
            ApplyEffects();
        }

        /// <summary>Grana, vignettatura e bagliore di riferimento del look in corso (prima della scelta del giocatore): il deserto li prende per sfumare tra l'aria aperta e le stanze.</summary>
        public static void ThemeEffects(out float grain, out float vignette, out float bloom) { grain = themeGrain; vignette = themeVignette; bloom = themeBloom; }

        static void ApplyEffects()
        {
            if (!themeFx || fxVolume == null) return;
            var profile = fxVolume.profile;
            if (profile == null) return;
            if (profile.TryGet(out FilmGrain fg)) fg.intensity.Override(themeGrain * GrainAmount);
            if (profile.TryGet(out UnityEngine.Rendering.Universal.Vignette vg)) vg.intensity.Override(themeVignette * VignetteAmount);
            if (profile.TryGet(out UnityEngine.Rendering.Universal.Bloom bl)) bl.intensity.Override(themeBloom * BloomAmount);
        }
    }
}

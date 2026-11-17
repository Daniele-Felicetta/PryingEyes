using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes
{
    /// <summary>Volumi del gioco (0..1), salvati in PlayerPrefs. Generale agisce su tutto (AudioListener);
    /// Effetti su spari, passi, nemici e ambiente; Musica sul grammofono che suona il valzer.
    /// I loop gia' in riproduzione si aggiornano subito quando si cambia un volume.</summary>
    public static partial class GameSettings
    {
        const string KeyMaster = "vol_master", KeyEffects = "vol_effects", KeyMusic = "vol_music";
        const string KeyBrightness = "video_brightness";
        const string KeySensitivity = "input_sensitivity", KeyFov = "video_fov", KeyMotion = "video_motion";
        const string KeyCoopHud = "hud_coop_scale", KeyThirdPerson = "video_thirdperson";
        static bool thirdPerson;
        static float master = 1f, effects = 1f, music = 1f;
        static float sensitivity = 1f, fov = 75f, motion = 1f, coopHud = 1f;
        /// <summary>Dimensione del pannello dei compagni (HUD.Coop.cs): da 60% a 180%.</summary>
        public const float CoopHudMin = 0.6f, CoopHudMax = 1.8f;
        static float brightness, baseExposure;
        static ColorAdjustments grading;
        static bool loaded;

        static readonly bool silentRun = System.Array.Exists(System.Environment.GetCommandLineArgs(),
            arg => arg == "-autotest" || arg == "-guntest" || arg == "-uitest" || arg == "-displaytest" || arg == "-batchmode" || arg == "-nographics" || arg == "-nosound");

        // Mute before any scene AudioSource starts; never persist the test volume in PlayerPrefs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeAudio() => AudioListener.volume = silentRun ? 0f : Master;

        struct Loop { public AudioSource src; public float vol; public bool music; }
        static readonly List<Loop> loops = new List<Loop>();

        public static float Master { get { Load(); return master; } }
        public static float Effects { get { Load(); return effects; } }
        /// <summary>Nello split screen la musica la fa solo la finestra del giocatore 1: le altre non la suonano (sarebbero N brani sovrapposti).</summary>
        public static float Music { get { Load(); return music; } }
        public static float Brightness { get { Load(); return brightness; } }
        public static float Sensitivity { get { Load(); return sensitivity; } }
        public static float Fov { get { Load(); return fov; } }
        public static float Motion { get { Load(); return motion; } }
        public static float CoopHudScale { get { Load(); return coopHud; } }
        /// <summary>Visuale in terza persona (0.8.0): la camera sta dietro la spalla del proprio personaggio. Predefinita: prima persona.</summary>
        public static bool ThirdPerson { get { if (thirdPersonOverride >= 0) return thirdPersonOverride == 1; Load(); return thirdPerson; } }
        /// <summary>Solo per le prove: se non e' negativo vale al posto dell'impostazione salvata (una prova non tocca le preferenze del giocatore).</summary>
        internal static int thirdPersonOverride = -1;

        static float Valid(float v, float min, float max, float fallback) =>
            float.IsNaN(v) || float.IsInfinity(v) ? fallback : Mathf.Clamp(v, min, max);

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            master = Clamp01(PlayerPrefs.GetFloat(KeyMaster, 1f));
            effects = Clamp01(PlayerPrefs.GetFloat(KeyEffects, 1f));
            music = Clamp01(PlayerPrefs.GetFloat(KeyMusic, 1f));
            brightness = Clamp01(PlayerPrefs.GetFloat(KeyBrightness, 0.5f));
            sensitivity = Valid(PlayerPrefs.GetFloat(KeySensitivity, 1f), 0.25f, 3f, 1f);
            fov = Valid(PlayerPrefs.GetFloat(KeyFov, 75f), 60f, 105f, 75f);
            motion = Clamp01(PlayerPrefs.GetFloat(KeyMotion, 1f));
            coopHud = Valid(PlayerPrefs.GetFloat(KeyCoopHud, 1f), CoopHudMin, CoopHudMax, 1f);
            thirdPerson = PlayerPrefs.GetInt(KeyThirdPerson, 0) == 1;
            LoadMore();            // le altre impostazioni: GameSettings.More.cs
            Apply();
        }

        public static void SetMaster(float v) { Load(); master = Clamp01(v); PlayerPrefs.SetFloat(KeyMaster, master); Apply(); }
        public static void SetEffects(float v) { Load(); effects = Clamp01(v); PlayerPrefs.SetFloat(KeyEffects, effects); Apply(); }
        public static void SetMusic(float v) { Load(); music = Clamp01(v); PlayerPrefs.SetFloat(KeyMusic, music); Apply(); }
        public static void SetSensitivity(float v) { Load(); sensitivity = Valid(v, 0.25f, 3f, 1f); PlayerPrefs.SetFloat(KeySensitivity, sensitivity); }
        public static void SetFov(float v) { Load(); fov = Valid(v, 60f, 105f, 75f); PlayerPrefs.SetFloat(KeyFov, fov); }
        public static void SetMotion(float v) { Load(); motion = Clamp01(v); PlayerPrefs.SetFloat(KeyMotion, motion); }
        public static void SetThirdPerson(bool on) { Load(); thirdPerson = on; PlayerPrefs.SetInt(KeyThirdPerson, on ? 1 : 0); }
        public static void SetCoopHudScale(float v) { Load(); coopHud = Valid(v, CoopHudMin, CoopHudMax, 1f); PlayerPrefs.SetFloat(KeyCoopHud, coopHud); }

        /// <summary>EV in piu' sul profilo nella partita a piani. Era +0,60 in spazio colore Gamma; in Linear (HDR + ACES) la stessa
        /// luminosita' media chiede +0,70: la gamma non schiarisce piu' le ombre di nascosto, il contrasto ora lo fanno luci e ambiente
        /// (FloorDirector.Look.cs). Tarata sulle foto a seme fisso di albergo, teatro e sanatorio.</summary>
        const float ProcExposure = 0.70f;
        static float themeExposure;

        public static void BindBrightness(Volume volume)
        {
            grading = null;
            if (volume == null || !volume.profile.TryGet(out grading)) return;
            baseExposure = grading.postExposure.value + (GameManager.proc ? ProcExposure : 0f);
            ApplyBrightness(Brightness);
        }

        /// <summary>L'esposizione propria del tema in corso (FloorDirector.ApplyLook), in EV, sopra il valore base e sotto la luminosita' scelta.</summary>
        public static void SetThemeExposure(float ev)
        {
            themeExposure = ev;
            ApplyBrightness(Brightness);
        }

        public static void SetBrightness(float v)
        {
            Load();
            brightness = Clamp01(v);
            PlayerPrefs.SetFloat(KeyBrightness, brightness);
            ApplyBrightness(brightness);
        }

        static void ApplyBrightness(float value)
        {
            if (grading != null) grading.postExposure.Override(baseExposure + themeExposure + Mathf.Lerp(-1.3f, 1.3f, value));
        }

        static float Clamp01(float v) => Valid(v, 0f, 1f, 0.5f);

        /// <summary>Registra un loop e restituisce il volume da dargli, gia' scalato per la sua categoria.</summary>
        public static float Track(AudioSource src, float vol, bool isMusic)
        {
            Load();
            loops.Add(new Loop { src = src, vol = vol, music = isMusic });
            return vol * (isMusic ? Music : effects);
        }

        static void Apply()
        {
            AudioListener.volume = silentRun ? 0f : master;
            loops.RemoveAll(l => l.src == null);
            foreach (var l in loops) l.src.volume = l.vol * (l.music ? Music : effects);
        }
    }
}

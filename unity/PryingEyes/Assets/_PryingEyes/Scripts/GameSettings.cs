using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes
{
    /// <summary>Volumi del gioco (0..1), salvati in PlayerPrefs. Generale agisce su tutto (AudioListener);
    /// Effetti su spari, passi, nemici e ambiente; Musica sul grammofono che suona il valzer.
    /// I loop gia' in riproduzione si aggiornano subito quando si cambia un volume.</summary>
    public static class GameSettings
    {
        const string KeyMaster = "vol_master", KeyEffects = "vol_effects", KeyMusic = "vol_music";
        const string KeyBrightness = "video_brightness";
        const string KeySensitivity = "input_sensitivity", KeyFov = "video_fov", KeyMotion = "video_motion";
        static float master = 1f, effects = 1f, music = 1f;
        static float sensitivity = 1f, fov = 75f, motion = 1f;
        static float brightness, baseExposure;
        static ColorAdjustments grading;
        static bool loaded;

        struct Loop { public AudioSource src; public float vol; public bool music; }
        static readonly List<Loop> loops = new List<Loop>();

        public static float Master { get { Load(); return master; } }
        public static float Effects { get { Load(); return effects; } }
        public static float Music { get { Load(); return music; } }
        public static float Brightness { get { Load(); return brightness; } }
        public static float Sensitivity { get { Load(); return sensitivity; } }
        public static float Fov { get { Load(); return fov; } }
        public static float Motion { get { Load(); return motion; } }

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
            Apply();
        }

        public static void SetMaster(float v) { Load(); master = Clamp01(v); PlayerPrefs.SetFloat(KeyMaster, master); Apply(); }
        public static void SetEffects(float v) { Load(); effects = Clamp01(v); PlayerPrefs.SetFloat(KeyEffects, effects); Apply(); }
        public static void SetMusic(float v) { Load(); music = Clamp01(v); PlayerPrefs.SetFloat(KeyMusic, music); Apply(); }
        public static void SetSensitivity(float v) { Load(); sensitivity = Valid(v, 0.25f, 3f, 1f); PlayerPrefs.SetFloat(KeySensitivity, sensitivity); }
        public static void SetFov(float v) { Load(); fov = Valid(v, 60f, 105f, 75f); PlayerPrefs.SetFloat(KeyFov, fov); }
        public static void SetMotion(float v) { Load(); motion = Clamp01(v); PlayerPrefs.SetFloat(KeyMotion, motion); }

        public static void BindBrightness(Volume volume)
        {
            grading = null;
            if (volume == null || !volume.profile.TryGet(out grading)) return;
            baseExposure = grading.postExposure.value + (GameManager.proc ? 0.60f : 0f);
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
            if (grading != null) grading.postExposure.Override(baseExposure + Mathf.Lerp(-1.3f, 1.3f, value));
        }

        static float Clamp01(float v) => Valid(v, 0f, 1f, 0.5f);

        /// <summary>Registra un loop e restituisce il volume da dargli, gia' scalato per la sua categoria.</summary>
        public static float Track(AudioSource src, float vol, bool isMusic)
        {
            Load();
            loops.Add(new Loop { src = src, vol = vol, music = isMusic });
            return vol * (isMusic ? music : effects);
        }

        static void Apply()
        {
            AudioListener.volume = master;
            loops.RemoveAll(l => l.src == null);
            foreach (var l in loops) l.src.volume = l.vol * (l.music ? music : effects);
        }
    }
}

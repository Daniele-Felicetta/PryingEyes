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
        static float master = 1f, effects = 1f, music = 1f;
        static float brightness, baseExposure;
        static ColorAdjustments grading;
        static bool loaded;

        struct Loop { public AudioSource src; public float vol; public bool music; }
        static readonly List<Loop> loops = new List<Loop>();

        public static float Master { get { Load(); return master; } }
        public static float Effects { get { Load(); return effects; } }
        public static float Music { get { Load(); return music; } }
        public static float Brightness { get { Load(); return brightness; } }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            master = PlayerPrefs.GetFloat(KeyMaster, 1f);
            effects = PlayerPrefs.GetFloat(KeyEffects, 1f);
            music = PlayerPrefs.GetFloat(KeyMusic, 1f);
            brightness = Clamp01(PlayerPrefs.GetFloat(KeyBrightness, 0.5f));
            Apply();
        }

        public static void SetMaster(float v) { master = Clamp01(v); PlayerPrefs.SetFloat(KeyMaster, master); Apply(); }
        public static void SetEffects(float v) { effects = Clamp01(v); PlayerPrefs.SetFloat(KeyEffects, effects); Apply(); }
        public static void SetMusic(float v) { music = Clamp01(v); PlayerPrefs.SetFloat(KeyMusic, music); Apply(); }

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

        static float Clamp01(float v) => Mathf.Clamp01(v);

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

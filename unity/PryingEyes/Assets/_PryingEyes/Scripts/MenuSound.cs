using UnityEngine;

namespace PryingEyes
{
    /// <summary>I toni del menu: il tic di una voce che si sceglie, il passo di una scelta o di un cursore, la conferma, l'indietro e l'anteprima dei
    /// volumi (muovendo Effetti, Ambiente o Generale si sente cosa si sta regolando). Una sorgente sola, secca (niente eco della stanza: non e'
    /// nel mondo), col volume degli Effetti; si spegne con "Suoni dell'interfaccia". Non suonano nelle prove automatiche (GameSettings.QuietRun),
    /// ma le richieste si contano lo stesso (Requests), cosi' una prova puo' controllare che un cursore o un pulsante "parli".</summary>
    public static class MenuSound
    {
        public const float Level = 0.55f;
        static AudioSource source;
        static AudioClip tick, step, confirm, back;
        static float lastTick = -9f, lastStep = -9f, lastSlide = -9f, lastPreview = -9f;

        /// <summary>Quanti toni sono stati chiesti (e sarebbero partiti con i suoni dell'interfaccia accesi e senza prove in corso).</summary>
        public static int Requests { get; private set; }

        public enum Preview { Master, Effects, Ambience }

        static bool Muted => !GameSettings.UiSounds;

        static AudioSource Source()
        {
            if (source != null) return source;
            var go = new GameObject("MenuSound");
            Object.DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0f; source.priority = 24;
            source.bypassListenerEffects = true; source.bypassReverbZones = true; source.dopplerLevel = 0f;
            return source;
        }

        static AudioClip Get(ref AudioClip c, string name, System.Func<float[]> make) => c != null ? c : (c = SoundBank.Clip(name, make()));

        /// <summary>Conta la richiesta e, fuori dalle prove, suona il clip a quel volume e a quell'altezza.</summary>
        static void Play(System.Func<AudioClip> clip, float level, float pitch)
        {
            Requests++;
            if (GameSettings.QuietRun) return;
            var c = clip();
            if (c == null) return;
            var s = Source();
            s.pitch = pitch;
            s.PlayOneShot(c, Mathf.Clamp01(level));
        }

        /// <summary>Una voce si accende o si sceglie.</summary>
        public static void Tick()
        {
            if (Muted || Time.unscaledTime - lastTick < 0.045f) return;
            lastTick = Time.unscaledTime;
            Play(() => Get(ref tick, "menu_tic", SoundGen.UiTick), Level * GameSettings.Effects, 1f);
        }

        /// <summary>Una scelta cambia valore.</summary>
        public static void Step()
        {
            if (Muted || Time.unscaledTime - lastStep < 0.03f) return;
            lastStep = Time.unscaledTime;
            Play(() => Get(ref step, "menu_passo", SoundGen.UiStep), Level * GameSettings.Effects, 1f);
        }

        /// <summary>Un cursore si muove: piu' acuto verso il massimo (0..1).</summary>
        public static void Slide(float value01)
        {
            if (Muted || Time.unscaledTime - lastSlide < 0.06f) return;
            lastSlide = Time.unscaledTime;
            Play(() => Get(ref step, "menu_passo", SoundGen.UiStep), Level * 0.8f * GameSettings.Effects, 0.8f + 0.5f * Mathf.Clamp01(value01));
        }

        public static void Confirm()
        {
            if (Muted) return;
            Play(() => Get(ref confirm, "menu_conferma", SoundGen.UiConfirm), Level * GameSettings.Effects, 1f);
        }

        public static void Back()
        {
            if (Muted) return;
            Play(() => Get(ref back, "menu_indietro", SoundGen.UiBack), Level * GameSettings.Effects, 1f);
        }

        /// <summary>Muovendo un volume si sente un campione di quello che si regola (non piu' di uno ogni 0,35 s).</summary>
        public static void PreviewVolume(Preview what)
        {
            if (Muted || Time.unscaledTime - lastPreview < 0.35f) return;
            lastPreview = Time.unscaledTime;
            switch (what)
            {
                case Preview.Effects: Play(() => SoundBank.Bell, 0.7f * GameSettings.Effects, 1f); break;
                case Preview.Ambience: Play(() => SoundBank.Creak, 0.7f * GameSettings.Ambience, 1f); break;
                default: Play(() => SoundBank.Bell, 0.5f, 1f); break;
            }
        }
    }
}

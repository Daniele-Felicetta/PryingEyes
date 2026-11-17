using System.Threading.Tasks;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Il tema del menu principale (0.8.0): il valzer per carillon e organo di SoundGen.MenuTheme (96 s, stereo, richiusura senza scatti).
    /// Entra con una dissolvenza di due secondi e mezzo appena il menu e' aperto e il campione e' pronto (si calcola in un thread alla creazione
    /// del menu, circa due secondi), nelle schermate che si aprono da li' (impostazioni, guida, cooperativa) continua, esce in un secondo e
    /// mezzo quando la partita comincia, e ricomincia da capo quando si torna al menu. Nella pausa non c'e': li' decide la musica dell'ondata.
    /// Segue "Tema del menu" e "Musica" delle impostazioni (si sente subito: muovendo il cursore la musica e' gia' li'), non passa per l'eco della
    /// stanza e abbassa l'organo d'ambiente (Duck, letto da ProcAmbience). -nomusic la spegne.</summary>
    public sealed class MenuMusic : MonoBehaviour
    {
        public static MenuMusic I;

        /// <summary>Quanto il tema copre l'organo e i toni d'ambiente, 0..1.</summary>
        public static float Duck { get; private set; }

        /// <summary>Volume del tema con "Tema del menu", "Musica" e "Volume generale" al massimo, e i tempi delle dissolvenze (s). Il campione
        /// e' normalizzato a un picco di 0,85 e misura -7,8 LUFS (pyloudnorm, 96 s): 0,16 lo porta a -23,7 LUFS, tra l'ambiente (-28,8) e la
        /// musica delle ondate (da -25 a -22 col suo bus di 0,65). Vedi docs/game/menu-music.md.</summary>
        public const float Level = 0.16f, FadeIn = 2.5f, FadeOut = 1.5f;

        AudioSource source;
        float fade;                 // 0..1, la dissolvenza (al quadrato: parte piano, arriva morbida)
        bool playing;

        // per le prove: il menu forzato aperto o chiuso invece di leggerlo dal gioco
        public static bool debugOverride, debugWant;

        public float Fade => fade;
        public bool Playing => playing;
        public bool Ready => source != null && source.clip != null;
        public AudioSource Source => source;

        /// <summary>Il volume vero della sorgente quando suona (dissolvenza, Tema del menu, Musica, livello).</summary>
        public static float TargetVolume(float fade, float menu, float music) => fade * fade * Level * menu * music;

        /// <summary>Lo crea il menu (uno per scena) e fa partire il calcolo del campione. Non c'e' nelle prove automatiche e con -nomusic.</summary>
        public static void Create()
        {
            if (I != null || GameSettings.QuietRun) return;
            SoundBank.WarmMenuMusic();
            I = new GameObject("MenuMusic").AddComponent<MenuMusic>();
        }

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.loop = true; source.playOnAwake = false; source.spatialBlend = 0f; source.volume = 0f;
            source.priority = 16;
            source.bypassListenerEffects = true; source.bypassReverbZones = true; source.dopplerLevel = 0f;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (source.clip == null) source.clip = SoundBank.MenuTheme;
            bool want = debugOverride ? debugWant : GameMenu.InMenu && !GameMenu.Paused;
            bool ready = source.clip != null;
            fade = Mathf.MoveTowards(fade, want && ready ? 1f : 0f, dt / (want ? FadeIn : FadeOut));
            if (fade > 0.001f && !playing && ready) { source.timeSamples = 0; source.Play(); playing = true; }
            else if (fade <= 0.001f && playing) { source.Stop(); playing = false; }
            source.volume = TargetVolume(fade, GameSettings.MenuMusic, GameSettings.Music);
            Duck = playing ? Mathf.Clamp01(fade * (GameSettings.MenuMusic > 0.02f && GameSettings.Music > 0.02f ? 1f : 0f)) : 0f;
        }

        void OnDestroy()
        {
            if (I != this) return;
            I = null; Duck = 0f;
        }
    }

    public static partial class SoundBank
    {
        static Task<float[]> menuThemeTask;
        static AudioClip menuTheme;
        static bool menuThemeStarted, menuThemeFaultLogged;

        /// <summary>Fa partire in un thread il calcolo del tema del menu.</summary>
        public static void WarmMenuMusic()
        {
            if (menuThemeStarted) return;
            menuThemeStarted = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            menuThemeTask = Task.FromResult(SoundGen.MenuTheme());
#else
            menuThemeTask = Task.Run(() => SoundGen.MenuTheme());
#endif
        }

        /// <summary>Il campione e' stato calcolato (o la clip e' gia' fatta).</summary>
        public static bool MenuThemeReady => menuTheme != null || (menuThemeTask != null && menuThemeTask.IsCompleted && !menuThemeTask.IsFaulted);

        /// <summary>La clip stereo del tema, appena il thread ha finito (poi i campioni restano solo nella clip).</summary>
        public static AudioClip MenuTheme
        {
            get
            {
                if (menuTheme != null || menuThemeTask == null || !menuThemeTask.IsCompleted) return menuTheme;
                if (menuThemeTask.IsFaulted)
                {
                    if (!menuThemeFaultLogged) { menuThemeFaultLogged = true; Debug.LogException(menuThemeTask.Exception); }
                    return null;
                }
                var samples = menuThemeTask.Result;
                menuTheme = AudioClip.Create("musica_menu", samples.Length / 2, 2, SoundGen.MenuRate, false);
                menuTheme.SetData(samples, 0);
                menuThemeTask = null;
                return menuTheme;
            }
        }
    }
}

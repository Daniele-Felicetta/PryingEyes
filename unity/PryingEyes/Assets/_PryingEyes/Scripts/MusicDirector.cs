using System;
using System.Threading.Tasks;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Musica adattiva delle ondate (2026-11-15), procedurale come tutti i suoni del gioco. Tra un'ondata e l'altra resta solo
    /// l'ambiente; quando l'ondata combatte entrano tre strati in sincrono, loop di 24 s (8 battute a 80 battiti al minuto):
    /// i tamburi (taiko grave sull'uno e sul tre, tamburo a cornice sul due e sul quattro, ticchettio sugli ottavi, rullo alla quarta e
    /// all'ottava battuta, un colpo di metallo in testa alle frasi), piu' forti quanti piu' nemici sono vivi e piu' radi quando restano gli
    /// ultimi; l'ostinato grave (re in ottavi che scende a si bemolle e la, sopra un bordone di re e la); gli archi tesi (re, la e la
    /// seconda stonata mi bemolle, col tremolo) quando i nemici sono tanti o c'e' il Cardinale. A ondata superata un accordo d'organo di re
    /// minore che si apre in re maggiore. La musica abbassa l'organo e i toni d'ambiente (Duck, letto da ProcAmbience), cade in fretta
    /// quando si muore, si abbassa in pausa, segue il volume "Musica" delle impostazioni e non passa per l'eco della stanza. I campioni si
    /// calcolano in un thread all'avvio della scena. -nomusic la spegne. In cooperativa ognuno la sua, dallo stato dell'ondata che gli arriva.</summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        public static MusicDirector I;
        static bool disabled;
        static int cleared;          // ondate superate col premio (RoundDirector): a ognuna l'accordo d'organo

        /// <summary>Quanto la musica copre l'organo e i toni d'ambiente, 0..1 (ProcAmbience li abbassa fino al 60%).</summary>
        public static float Duck { get; private set; }

        /// <summary>Livelli degli strati (ampiezza prima del volume Musica e del bus): da pochi nemici a FullHeat.</summary>
        public const float DrumsLow = 0.40f, DrumsHigh = 0.80f, BassLow = 0.34f, BassHigh = 0.44f, TensionLevel = 0.42f, TensionBoss = 0.60f;
        /// <summary>Volume complessivo della musica sotto gli effetti, accordo di fine ondata, quota in pausa, tamburi con gli ultimi nemici.</summary>
        public const float Bus = 0.65f, StingLevel = 0.75f, PauseLevel = 0.35f, StragglerDrums = 0.4f;
        /// <summary>Nemici vivi a cui i tamburi sono al massimo; da quanti entrano gli archi; da quanti rimasti i tamburi si ritirano.</summary>
        public const int FullHeat = 14, TensionFrom = 10, Stragglers = 3;

        AudioSource drums, bass, tension, sting;
        float vDrums, vBass, vTension, countT;
        int alive, clearedSeen;
        bool playing;

        // per le prove: lo stato dell'ondata forzato invece che letto dal gioco
        public static bool debugOverride, debugFighting, debugBoss, debugDown;
        public static int debugAlive, debugRemaining = -1;

        public float DrumsVolume => vDrums;
        public float BassVolume => vBass;
        public float TensionVolume => vTension;
        public bool Ready => drums != null && drums.clip != null && bass.clip != null && tension.clip != null;
        public bool Playing => playing;
        public int Stings { get; private set; }
        public AudioSource DrumsSource => drums;
        public AudioSource BassSource => bass;
        public AudioSource TensionSource => tension;
        public AudioSource StingSource => sting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadArgs() { disabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-nomusic") >= 0; }

        /// <summary>Un'ondata (o un round a candele) e' stata superata col premio, anche sugli ospitati: suona l'accordo d'organo.</summary>
        public static void WaveCleared() { cleared++; }

        /// <summary>Partita procedurale: crea il direttore (uno per scena) e fa partire il calcolo dei campioni.</summary>
        public static void Create()
        {
            if (I != null || disabled) return;
            SoundBank.WarmMusic();
            I = new GameObject("MusicDirector").AddComponent<MusicDirector>();
        }

        void Awake()
        {
            clearedSeen = cleared;
            drums = MakeSource(true); bass = MakeSource(true); tension = MakeSource(true); sting = MakeSource(false);
        }

        AudioSource MakeSource(bool loop)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = loop ? 0f : 1f;
            src.priority = 16;                     // la musica non perde la voce quando gli spari sono tanti
            src.bypassListenerEffects = true;      // niente eco della stanza: la musica non e' nel mondo
            src.bypassReverbZones = true;
            src.dopplerLevel = 0f;
            return src;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!Ready) Attach();
            bool fighting, boss, down; int aliveNow, remaining;
            if (debugOverride)
            {
                fighting = debugFighting; boss = debugBoss; down = debugDown; aliveNow = debugAlive; remaining = debugRemaining;
            }
            else
            {
                var rd = RoundDirector.I;
                fighting = rd != null && rd.Fighting && !GameMenu.InMenu;
                if ((countT -= dt) <= 0f) { countT = 0.25f; alive = CountAlive(); }
                aliveNow = alive;
                boss = Enemy.Boss != null && !Enemy.Boss.IsDead;
                // i primi secondi no: sugli ospitati il conto dei rimasti arriva con il primo stato dell'ondata
                remaining = fighting && rd.Clock > 2f ? rd.Remaining : -1;
                var gm = GameManager.I;
                down = gm != null && gm.player != null && !gm.player.alive;
            }
            float heat = Mathf.Clamp01(aliveNow / (float)FullHeat);
            bool stragglers = remaining >= 0 && remaining <= Stragglers;      // gli ultimi: i tamburi si ritirano, resta l'ostinato
            float tDrums = 0f, tBass = 0f, tTension = 0f;
            if (fighting && !down)
            {
                tDrums = Mathf.Lerp(DrumsLow, DrumsHigh, heat) * (stragglers ? StragglerDrums : 1f);
                tBass = Mathf.Lerp(BassLow, BassHigh, heat);
                if (boss) tTension = TensionBoss;
                else if (aliveNow >= TensionFrom && !stragglers) tTension = TensionLevel;
            }
            float fall = down ? 0.8f : 3.5f;
            vDrums = Fade(vDrums, tDrums, dt, 1.5f, fall);
            vBass = Fade(vBass, tBass, dt, 2.0f, fall);
            vTension = Fade(vTension, tTension, dt, 3.0f, down ? 0.8f : 4.0f);
            bool any = vDrums + vBass + vTension > 0.0005f;
            if (any && !playing && Ready) StartLoops();
            else if (!any && playing) StopLoops();
            float music = GameSettings.Music * Bus * (GameMenu.Paused ? PauseLevel : 1f);
            drums.volume = vDrums * music; bass.volume = vBass * music; tension.volume = vTension * music;
            Duck = Mathf.Clamp01(Mathf.Max(vBass / BassHigh, vDrums / DrumsHigh));
            if (cleared != clearedSeen)
            {
                clearedSeen = cleared;
                var clip = SoundBank.MusicCleared;
                if (clip != null) { sting.PlayOneShot(clip, StingLevel * GameSettings.Music * Bus); Stings++; }
            }
        }

        /// <summary>Sale verso il bersaglio in 'up' secondi ogni mezzo punto di volume, scende in 'down'.</summary>
        static float Fade(float v, float target, float dt, float up, float down) =>
            Mathf.MoveTowards(v, target, dt * 0.5f / (target > v ? up : down));

        void Attach()
        {
            if (drums.clip == null) drums.clip = SoundBank.MusicDrums;
            if (bass.clip == null) bass.clip = SoundBank.MusicBass;
            if (tension.clip == null) tension.clip = SoundBank.MusicTension;
        }

        /// <summary>I tre strati partono insieme dall'inizio del giro, allo stesso istante del mixer: restano allineati.</summary>
        void StartLoops()
        {
            double at = AudioSettings.dspTime + 0.05;
            foreach (var s in new[] { drums, bass, tension }) { s.timeSamples = 0; s.PlayScheduled(at); }
            playing = true;
        }

        void StopLoops()
        {
            drums.Stop(); bass.Stop(); tension.Stop();
            playing = false;
        }

        static int CountAlive()
        {
            int n = 0;
            foreach (var e in Enemy.All) if (e != null && !e.IsDead) n++;
            return n;
        }

        void OnDestroy()
        {
            if (I != this) return;
            I = null; Duck = 0f;
        }
    }

    public static partial class SoundGen
    {
        /// <summary>Il giro della musica: 8 battute in 4/4 a 80 battiti al minuto, 24 s esatti.</summary>
        public const float MusicLoop = 24f, MusicBeat = 0.75f;
        /// <summary>Campionamento: i tamburi hanno i ticchettii a 7 kHz, ostinato e archi stanno sotto i 4 kHz.</summary>
        public const int MusicRateDrums = 32000, MusicRateLow = 22050, MusicRateSting = 32000;

        /// <summary>La frequenza piu' vicina che fa un numero intero di cicli nel giro: il loop non scatta.</summary>
        static double LoopFreq(double f) => Math.Round(f * MusicLoop) / MusicLoop;

        static double MidiHz(int m) => 440.0 * Math.Pow(2.0, (m - 69) / 12.0);

        /// <summary>Tamburi del combattimento, loop di 24 s a 32 kHz.</summary>
        public static float[] MusicDrums()
        {
            const int R = MusicRateDrums;
            const float b = MusicBeat;
            var r = new System.Random(73);
            int N = (int)(MusicLoop * R);
            var d = new float[N + 4 * R];                         // le code oltre il giro ricadono all'inizio
            for (int bar = 0; bar < 8; bar++)
            {
                float b0 = bar * 4 * b;
                bool fill = bar == 3 || bar == 7;
                // taiko grave sull'uno e sul tre, un rimbalzo sul tre-e delle battute pari
                Taiko(d, b0, 1.0f, r, R);
                Taiko(d, b0 + 2 * b, 0.82f, r, R);
                if (bar % 2 == 1) Taiko(d, b0 + 2.5f * b, 0.5f, r, R);
                // tamburo a cornice sul due e sul quattro (il quattro delle battute di rullo lo prende il rullo)
                FrameDrum(d, b0 + b, 0.45f, r, R);
                if (!fill) FrameDrum(d, b0 + 3 * b, 0.5f, r, R);
                // ticchettio sugli ottavi: forte sul battere, un filo in ritardo e piano sul levare
                for (int e = 0; e < 8; e++)
                {
                    float t8 = b0 + e * b * 0.5f + (e % 2 == 1 ? b * 0.06f : 0f);
                    Burst(d, t8, 7500f, 1.6f, 0.012f, e % 2 == 0 ? 0.30f : 0.18f, r, R);
                }
                if (fill)
                {
                    // rullo di quattro tom che scendono sull'ultimo battito
                    float[] tf = { 176f, 152f, 130f, 112f };
                    for (int k = 0; k < 4; k++)
                    {
                        float at = b0 + 3 * b + k * b * 0.25f;
                        Thump(d, at, tf[k], tf[k] * 0.7f, 0.03f, 0.13f, 0.45f + 0.08f * k, R);
                        Burst(d, at, tf[k] * 5f, 1.0f, 0.025f, 0.25f, r, R);
                    }
                }
                // un colpo di metallo in testa a ogni frase di quattro battute (si bemolle, dentro re minore)
                if (bar == 0 || bar == 4)
                    Chime(d, b0, 233.08f, 0.45f, new[] { 1f, 2.76f, 5.40f, 8.93f }, new[] { 1f, 0.55f, 0.3f, 0.15f }, 0.22f, R);
            }
            var s = new float[N];
            for (int i = 0; i < N; i++) s[i] = d[i] + (i + N < d.Length ? d[i + N] : 0f);
            HallVerb(s, R, 0.20f, 0.86f, true);
            LoopHighPass(s, 35f, R);
            SoftClip(s, 1.2f);
            Normalize(s, 0.9f);
            return s;
        }

        static void Taiko(float[] d, float at, float vel, System.Random r, int rate)
        {
            Thump(d, at, 78f, 50f, 0.08f, 0.22f, vel, rate);
            Thump(d, at, 156f, 100f, 0.04f, 0.06f, vel * 0.35f, rate);    // il corpo del fusto
            Burst(d, at, 420f, 0.8f, 0.03f, vel * 0.5f, r, rate);           // la pelle
            Burst(d, at, 2600f, 1.0f, 0.004f, vel * 0.25f, r, rate);        // la mazza
        }

        static void FrameDrum(float[] d, float at, float vel, System.Random r, int rate)
        {
            Thump(d, at, 170f, 120f, 0.03f, 0.09f, vel, rate);
            Burst(d, at, 1600f, 1.0f, 0.035f, vel * 1.1f, r, rate);
        }

        /// <summary>Passa-alto su un giro trattato come periodico (due passate, si tiene la seconda): toglie il rombo che le casse piccole
        /// non rendono, senza lasciare all'inizio del loop il transitorio del filtro.</summary>
        static void LoopHighPass(float[] s, float f, int rate)
        {
            var hp = Biquad.HighPass(f, 0.7f, rate);
            for (int i = 0; i < s.Length; i++) hp.Run(s[i]);
            for (int i = 0; i < s.Length; i++) s[i] = hp.Run(s[i]);
        }

        /// <summary>Ostinato grave e bordone, loop di 24 s a 22 kHz: re in ottavi che alla quarta battuta scende a si bemolle e la (e
        /// all'ottava passa per il do e torna al re), raddoppiato all'ottava sotto, sopra un bordone di re e la che respira.</summary>
        public static float[] MusicBass()
        {
            const int R = MusicRateLow;
            int N = (int)(MusicLoop * R);
            var d = new float[N + R];
            int[][] bars =
            {
                new[] { 38, 38, 38, 38, 38, 38, 38, 38 },
                new[] { 38, 38, 38, 38, 38, 38, 38, 38 },
                new[] { 38, 38, 38, 38, 39, 39, 38, 38 },
                new[] { 34, 34, 34, 34, 33, 33, 33, 33 },
                new[] { 38, 38, 38, 38, 38, 38, 38, 38 },
                new[] { 38, 38, 38, 38, 38, 38, 38, 38 },
                new[] { 38, 38, 41, 41, 39, 39, 38, 38 },
                new[] { 34, 34, 36, 36, 33, 33, 33, 33 },
            };
            for (int bar = 0; bar < 8; bar++)
                for (int e = 0; e < 8; e++)
                {
                    float at = (bar * 8 + e) * MusicBeat * 0.5f;
                    float vel = e == 0 ? 1f : e % 2 == 0 ? 0.82f : 0.6f;
                    double f = MidiHz(bars[bar][e]);
                    BowNote(d, at, f, 0.30f, vel, R);
                    BowNote(d, at, f * 0.5, 0.30f, vel * 0.35f, R);
                }
            var s = new float[N];
            for (int i = 0; i < N; i++) s[i] = d[i] + (i + N < d.Length ? d[i + N] : 0f);
            // bordone: re e la gravi e un re appena crescente che batte lento; armoniche fino a 400 Hz, tremolo di 6 s (4 nel giro)
            double[] df = { LoopFreq(36.708), LoopFreq(55.0), LoopFreq(36.708 * 1.004) };
            double[] da = { 0.16, 0.12, 0.09 };
            var dw = new double[df.Length][];
            for (int k = 0; k < df.Length; k++) dw[k] = Saw((int)(400.0 / df[k]), 1.0);
            for (int i = 0; i < N; i++)
            {
                double t = (double)i / R, v = 0;
                for (int k = 0; k < df.Length; k++) v += Harmonics(TwoPi * df[k] * t, dw[k]) * da[k];
                s[i] += (float)(v * (0.75 + 0.25 * Math.Sin(TwoPi * t / 6.0)));
            }
            HallVerb(s, R, 0.12f, 0.84f, true);
            LoopHighPass(s, 35f, R);
            SoftClip(s, 1.2f);
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Archi tesi, loop di 24 s a 22 kHz: re, la, re acuto, mi bemolle (la seconda stonata contro il re) e un la in alto, ognuno a
        /// due leggii appena scordati, con vibrato, tremolo misurato sulle voci alte e un gonfiarsi lento che si alterna tra gravi e acute.</summary>
        public static float[] MusicTension()
        {
            const int R = MusicRateLow;
            int N = (int)(MusicLoop * R);
            var s = new float[N];
            double[] notes = { 293.66, 440.0, 587.33, 622.25, 880.0 };
            double[] amps = { 0.24, 0.18, 0.26, 0.22, 0.09 };
            int V = notes.Length * 2;
            var f = new double[V]; var a = new double[V]; var vibF = new double[V]; var vibP = new double[V];
            var w = new double[V][];
            for (int k = 0; k < V; k++)
            {
                int n = k / 2;
                f[k] = LoopFreq(notes[n] * (k % 2 == 0 ? 0.9975 : 1.0025));
                a[k] = amps[n];
                vibF[k] = 5.0 + 0.5 * (k % 3);                    // 5, 5,5 e 6 Hz: numeri interi di cicli nel giro
                vibP[k] = k * 1.3;
                w[k] = Saw(Math.Max(1, Math.Min(14, (int)(4000.0 / f[k]))), 1.15);
            }
            const double depth = 0.006;                           // circa dieci cents
            for (int i = 0; i < N; i++)
            {
                double t = (double)i / R, v = 0;
                double swLow = 0.55 + 0.45 * Math.Sin(TwoPi * t / 12.0 - Math.PI / 2), swHigh = 1.1 - swLow;
                double tr0 = 0.72 + 0.28 * Math.Abs(Math.Sin(TwoPi * 6.0 * t)), tr1 = 0.72 + 0.28 * Math.Abs(Math.Cos(TwoPi * 6.0 * t));
                for (int k = 0; k < V; k++)
                {
                    int n = k / 2;
                    // fase col vibrato: l'integrale chiuso di f*(1 + depth*sin), che a fine giro torna uguale
                    double th = TwoPi * f[k] * t - f[k] * depth / vibF[k] * Math.Cos(TwoPi * vibF[k] * t + vibP[k]);
                    double tr = n >= 2 ? (k % 2 == 0 ? tr0 : tr1) : 1.0;
                    v += Harmonics(th, w[k]) * a[k] * tr * (n >= 2 ? swHigh : swLow);
                }
                s[i] = (float)v;
            }
            HallVerb(s, R, 0.35f, 0.88f, true);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Ondata superata: accordo d'organo di re minore che a 1,2 s si apre in re maggiore (il fa lascia il posto al fa diesis),
        /// col pedale grave e un rintocco leggero sul cambio; 4,5 s con la coda.</summary>
        public static float[] MusicCleared()
        {
            const int R = MusicRateSting;
            var s = new float[(int)(4.5f * R)];
            double[] notes = { 73.42, 146.83, 220.0, 293.66, 174.61, 185.0 };      // gli ultimi due: la terza minore e quella maggiore
            double[] gain = { 0.6, 1.0, 0.8, 0.7, 0.8, 0.8 };
            var pipe = new[] { 1.0, 0.55, 0.35, 0.22, 0.0, 0.12, 0.0, 0.07 };    // canne: fondamentale, ottava, dodicesima, quindicesima...
            var ph = new double[notes.Length];
            for (int i = 0; i < s.Length; i++)
            {
                double t = (double)i / R;
                double x = Math.Min(1.0, Math.Max(0.0, (t - 1.2) / 0.12));
                double env = Math.Min(1.0, t / 0.18) * Math.Min(1.0, Math.Max(0.0, (3.4 - t) / 1.4));
                if (env <= 0.0) continue;
                double v = 0;
                for (int n = 0; n < notes.Length; n++)
                {
                    double gate = n == 4 ? 1.0 - x : n == 5 ? x : 1.0;
                    ph[n] += TwoPi * notes[n] * (1.0 + 0.0012 * Math.Sin(TwoPi * 5.2 * t + n)) / R;
                    if (gate > 0.0) v += Harmonics(ph[n], pipe) * gain[n] * gate;
                }
                s[i] = (float)(v * env);
            }
            Chime(s, 1.2f, 587.33f, 0.9f, new[] { 1f, 2.0f, 3.01f, 4.2f }, new[] { 1f, 0.4f, 0.2f, 0.1f }, 0.5f, R);
            HallVerb(s, R, 0.45f, 0.88f, false);
            Normalize(s, 0.75f);
            return s;
        }

        /// <summary>Pesi delle armoniche 1..H di un dente di sega a banda limitata (1/h^tilt).</summary>
        static double[] Saw(int H, double tilt)
        {
            var w = new double[Math.Max(1, H)];
            for (int h = 0; h < w.Length; h++) w[h] = Math.Pow(h + 1, -tilt);
            return w;
        }

        /// <summary>Somma delle armoniche di una fase coi pesi dati, per ricorrenza di Chebyshev: un seno e un coseno per campione invece di
        /// uno per armonica.</summary>
        static double Harmonics(double theta, double[] w)
        {
            double c2 = 2.0 * Math.Cos(theta), prev = 0.0, cur = Math.Sin(theta), v = 0.0;
            for (int h = 0; h < w.Length; h++)
            {
                v += cur * w[h];
                double next = c2 * cur - prev; prev = cur; cur = next;
            }
            return v;
        }

        /// <summary>Nota d'arco staccata: armoniche intere fino a 2,6 kHz che si spengono prima le acute (il colpo d'arco), attacco di 6 ms e
        /// chiusura di 30 ms.</summary>
        static void BowNote(float[] s, float at, double f, float dur, float gain, int rate)
        {
            int i0 = (int)(at * rate), n = (int)(dur * rate);
            int H = Math.Max(1, Math.Min(40, (int)(2600.0 / f)));
            var env = new double[H]; var q = new double[H];
            for (int h = 0; h < H; h++) { env[h] = Math.Pow(h + 1, -0.95); q[h] = Math.Exp(-(0.6 + 0.7 * (h + 1)) / rate); }
            double w = TwoPi * f / rate;
            for (int k = 0; k < n && i0 + k < s.Length; k++)
            {
                double th = w * k;
                double c2 = 2.0 * Math.Cos(th), prev = 0.0, cur = Math.Sin(th), v = 0.0;
                for (int h = 0; h < H; h++)
                {
                    v += cur * env[h];
                    env[h] *= q[h];
                    double next = c2 * cur - prev; prev = cur; cur = next;
                }
                float t = (float)k / rate;
                float a = Mathf.Clamp01(t / 0.006f) * Mathf.Clamp01((dur - t) / 0.03f);
                s[i0 + k] += (float)v * a * gain;
            }
        }

        /// <summary>Eco di sala (quattro pettini smorzati e due passa-tutto, alla Schroeder). Con loop il suono e' trattato come periodico:
        /// gira due volte e si tiene il secondo giro, cosi' la coda della fine ricade sull'inizio e il loop resta senza scatti.</summary>
        static void HallVerb(float[] s, int rate, float mix, float feedback, bool loop)
        {
            int N = s.Length;
            double k = rate / 44100.0;
            int[] cl = { (int)(1557 * k), (int)(1617 * k), (int)(1491 * k), (int)(1422 * k) };
            int[] al = { (int)(556 * k), (int)(441 * k) };
            var cb = new float[4][]; var ci = new int[4]; var cf = new float[4];
            for (int c = 0; c < 4; c++) cb[c] = new float[cl[c]];
            var ab = new float[2][]; var ai = new int[2];
            for (int p = 0; p < 2; p++) ab[p] = new float[al[p]];
            const float damp = 0.3f;
            var wet = new float[N];
            int total = loop ? 2 * N : N;
            for (int n = 0; n < total; n++)
            {
                float x = s[n % N] * 0.25f, y = 0f;
                for (int c = 0; c < 4; c++)
                {
                    float o = cb[c][ci[c]];
                    cf[c] = o * (1f - damp) + cf[c] * damp;
                    cb[c][ci[c]] = x + cf[c] * feedback;
                    if (++ci[c] >= cl[c]) ci[c] = 0;
                    y += o;
                }
                for (int p = 0; p < 2; p++)
                {
                    float bo = ab[p][ai[p]];
                    float o = -y + bo;
                    ab[p][ai[p]] = y + bo * 0.5f;
                    if (++ai[p] >= al[p]) ai[p] = 0;
                    y = o;
                }
                if (!loop || n >= N) wet[n % N] = y;
            }
            for (int i = 0; i < N; i++) s[i] += wet[i] * mix;
        }
    }

    public static partial class SoundBank
    {
        static Task<float[]> drumsTask, bassTask, tensionTask, clearedTask;
        static AudioClip musicDrums, musicBass, musicTension, musicCleared;
        static bool musicStarted, musicFaultLogged;

        /// <summary>Fa partire in un thread il calcolo della musica (un secondo di lavoro circa): gli strati si usano quando sono pronti.</summary>
        public static void WarmMusic()
        {
            if (musicStarted) return;
            musicStarted = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Web non ha i thread C#: si calcola durante il caricamento
            drumsTask = Task.FromResult(SoundGen.MusicDrums());
            bassTask = Task.FromResult(SoundGen.MusicBass());
            tensionTask = Task.FromResult(SoundGen.MusicTension());
            clearedTask = Task.FromResult(SoundGen.MusicCleared());
#else
            drumsTask = Task.Run(() => SoundGen.MusicDrums());
            bassTask = Task.Run(() => SoundGen.MusicBass());
            tensionTask = Task.Run(() => SoundGen.MusicTension());
            clearedTask = Task.Run(() => SoundGen.MusicCleared());
#endif
        }

        /// <summary>La clip di uno strato appena il thread ha finito (poi il thread si dimentica: i campioni restano solo nella clip).</summary>
        static AudioClip MusicClip(ref AudioClip c, ref Task<float[]> task, string name, int rate)
        {
            if (c != null || task == null || !task.IsCompleted) return c;
            if (task.IsFaulted)
            {
                if (!musicFaultLogged) { musicFaultLogged = true; Debug.LogException(task.Exception); }
                return null;
            }
            c = Clip(name, task.Result, rate);
            task = null;
            return c;
        }

        /// <summary>Tutti gli strati e l'accordo sono pronti.</summary>
        public static bool MusicReady => MusicDrums != null && MusicBass != null && MusicTension != null && MusicCleared != null;
        public static AudioClip MusicDrums => MusicClip(ref musicDrums, ref drumsTask, "musica_tamburi", SoundGen.MusicRateDrums);
        public static AudioClip MusicBass => MusicClip(ref musicBass, ref bassTask, "musica_ostinato", SoundGen.MusicRateLow);
        public static AudioClip MusicTension => MusicClip(ref musicTension, ref tensionTask, "musica_archi", SoundGen.MusicRateLow);
        public static AudioClip MusicCleared => MusicClip(ref musicCleared, ref clearedTask, "musica_ondata_superata", SoundGen.MusicRateSting);
    }
}

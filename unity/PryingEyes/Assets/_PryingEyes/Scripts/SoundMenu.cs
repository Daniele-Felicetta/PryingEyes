using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>I suoni del menu (0.8.0), procedurali come tutti: i toni dei pulsanti e il tema musicale del menu principale. Il tema e' un valzer
    /// per carillon e organo in re minore, 3/4 a 60 battiti, 32 battute (96 s) che si richiudono senza scatti, in stereo: il carillon (pettine
    /// d'acciaio, parziali di trave a sbalzo, piu' acute a destra), l'organo che tiene gli accordi con una coppia di voci scordate una a
    /// sinistra e una a destra, il pedale, una campana grave all'inizio dei due giri del tema e una sala di pietra attorno a tutto.
    /// Forma: A (il tema, secco), B (il contrasto in fa maggiore, con un'eco di cristallo all'ottava), A' (il tema che si stona: note
    /// scordate a caso, un mi bemolle contro il re) e C (la chiusa, che finisce sul do diesis: la sensibile che riporta al re del giro dopo).
    /// Le note suonano con risonatori a due poli (nessun seno per campione): un giro si calcola in meno di due secondi, in un thread.</summary>
    public static partial class SoundGen
    {
        // ---------- toni dei pulsanti ----------

        /// <summary>Il tic di una voce che si accende o si sceglie: legno e ottone, 70 ms.</summary>
        public static float[] UiTick()
        {
            var r = new System.Random(501);
            var s = Buf(0.09f);
            Thump(s, 0f, 1500f, 900f, 0.01f, 0.012f, 0.7f);
            Burst(s, 0f, 3200f, 2f, 0.004f, 0.6f, r);
            Normalize(s, 0.5f);
            return s;
        }

        /// <summary>Il passo di una scelta o di un cursore: piu' basso e morbido del tic.</summary>
        public static float[] UiStep()
        {
            var r = new System.Random(502);
            var s = Buf(0.08f);
            Thump(s, 0f, 760f, 520f, 0.012f, 0.016f, 0.7f);
            Burst(s, 0f, 2400f, 2f, 0.003f, 0.35f, r);
            Normalize(s, 0.45f);
            return s;
        }

        /// <summary>Conferma: un rintocco di campanello in re e un colpo sordo sotto.</summary>
        public static float[] UiConfirm()
        {
            var s = Buf(0.7f);
            Chime(s, 0f, 587.33f, 0.28f, new[] { 1f, 2.0f, 3.01f, 4.2f }, new[] { 1f, 0.4f, 0.2f, 0.1f }, 0.5f);
            Thump(s, 0f, 150f, 92f, 0.03f, 0.08f, 0.55f);
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Indietro: un la piu' grave, piu' corto, con un colpo piu' sordo.</summary>
        public static float[] UiBack()
        {
            var s = Buf(0.5f);
            Chime(s, 0f, 440f, 0.2f, new[] { 1f, 2.0f, 3.01f }, new[] { 1f, 0.35f, 0.15f }, 0.45f);
            Thump(s, 0f, 120f, 70f, 0.03f, 0.07f, 0.5f);
            Normalize(s, 0.55f);
            return s;
        }

        // ---------- il tema del menu ----------

        public const int MenuRate = 22050;
        public const int MenuBars = 32;
        public const float MenuBeat = 1f;                         // 60 battiti al minuto
        public const float MenuLoop = MenuBars * 3 * MenuBeat;    // 96 s

        // armonia: un accordo a battuta (A, B, A', C)
        static readonly string[] MenuProg =
        {
            "Dm", "Dm", "Bb", "A7",   "Dm", "Gm", "A7", "Dm",
            "F",  "F",  "C",  "C",    "Bb", "Gm", "A7", "A7",
            "Dm", "Dm", "Bb", "A7",   "Dm", "Gm", "A7", "Dm",
            "Dm", "Bb", "Gm", "A7",   "Dm", "Gm", "A7", "A7",
        };

        // melodia: una stringa per battuta, "nota:battiti" (tre battiti a battuta); la pausa e' "R:battiti"
        static readonly string[] MenuMelody =
        {
            // A: il tema (arpeggi su e giu', un respiro a meta', a casa sul re)
            "D5:1 F5:1 A5:1", "A5:2 G5:1", "F5:1 D5:1 Bb4:1", "C#5:1 E5:1 G5:1",
            "A5:2 F5:1", "G5:1 Bb5:1 A5:1", "G5:1 E5:1 C#5:1", "D5:3",
            // B: il contrasto, in fa maggiore
            "C6:1.5 A5:0.5 F5:1", "G5:1 A5:1 C6:1", "G5:2 E5:1", "D5:1 E5:1 G5:1",
            "D6:2 Bb5:1", "Bb5:1 A5:1 G5:1", "C#6:1.5 A5:0.5 E5:1", "D5:1.5 C#5:1.5",
            // A': il tema che si stona (un mi bemolle al posto del mi, un re che inciampa)
            "D5:1 F5:1 A5:1", "A5:2 G5:1", "F5:1 D5:1 Bb4:1", "C#5:1 Eb5:1 G5:1",
            "A5:2 F5:1", "G5:1 Bb5:1 A5:1", "G5:1 E5:1 C#5:1", "D5:2 Eb5:0.5 D5:0.5",
            // C: la chiusa, sulla sensibile
            "F5:2 A5:1", "D6:2 C6:1", "Bb5:1 G5:1 D5:1", "E5:2 C#5:1",
            "D5:1 F5:1 A5:1", "G5:2 Bb5:1", "A5:1 G5:1 E5:1", "C#5:3",
        };

        /// <summary>Un accordo: nota del pedale, voci dell'organo e le tre note che il carillon pizzica sul secondo e sul terzo battito.</summary>
        struct MenuChord { public int bass; public int[] pad; public int[] box; }

        static MenuChord ChordOf(string name)
        {
            switch (name)
            {
                case "Dm": return new MenuChord { bass = 38, pad = Notes("D3 A3 D4 F4"), box = Notes("A3 D4 F4") };
                case "Bb": return new MenuChord { bass = 34, pad = Notes("F3 Bb3 D4 F4"), box = Notes("Bb3 D4 F4") };
                case "Gm": return new MenuChord { bass = 43, pad = Notes("D3 G3 Bb3 D4"), box = Notes("Bb3 D4 G4") };
                case "A7": return new MenuChord { bass = 33, pad = Notes("E3 G3 A3 C#4"), box = Notes("C#4 E4 G4") };
                case "F": return new MenuChord { bass = 41, pad = Notes("C3 F3 A3 C4"), box = Notes("A3 C4 F4") };
                case "C": return new MenuChord { bass = 36, pad = Notes("C3 G3 C4 E4"), box = Notes("G3 C4 E4") };
                default: throw new ArgumentException("accordo sconosciuto: " + name);
            }
        }

        static int[] Notes(string names)
        {
            var parts = names.Split(' ');
            var list = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) list[i] = MidiOf(parts[i]);
            return list;
        }

        /// <summary>Nome di nota ("D5", "C#5", "Bb4") in numero MIDI (C4 = 60).</summary>
        public static int MidiOf(string name)
        {
            int semi;
            switch (name[0])
            {
                case 'C': semi = 0; break;
                case 'D': semi = 2; break;
                case 'E': semi = 4; break;
                case 'F': semi = 5; break;
                case 'G': semi = 7; break;
                case 'A': semi = 9; break;
                case 'B': semi = 11; break;
                default: throw new ArgumentException("nota sconosciuta: " + name);
            }
            int i = 1;
            if (name[i] == '#') { semi++; i++; }
            else if (name[i] == 'b') { semi--; i++; }
            return 12 * (int.Parse(name.Substring(i)) + 1) + semi;
        }

        static double HzOf(double midi) => 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0);

        /// <summary>Le note della melodia come (battuta, battito, MIDI, durata): per le prove e per l'analisi del brano.</summary>
        public static List<(int bar, float beat, int midi, float len)> MenuMelodyNotes()
        {
            var notes = new List<(int, float, int, float)>();
            for (int bar = 0; bar < MenuMelody.Length; bar++)
            {
                float at = 0f;
                foreach (string token in MenuMelody[bar].Split(' '))
                {
                    int colon = token.IndexOf(':');
                    float len = float.Parse(token.Substring(colon + 1), System.Globalization.CultureInfo.InvariantCulture);
                    if (token[0] != 'R') notes.Add((bar, at, MidiOf(token.Substring(0, colon)), len));
                    at += len;
                }
            }
            return notes;
        }

        /// <summary>L'accordo della battuta (0..31) come nome e note del pedale e dell'organo.</summary>
        public static (string name, int bass, int[] pad) MenuChordAt(int bar)
        {
            var c = ChordOf(MenuProg[bar]);
            return (MenuProg[bar], c.bass, c.pad);
        }

        /// <summary>Parziale smorzato per ricorrenza a due poli: y[k] = a r^k sin(w k), senza un seno per campione. Scrive nei due canali con i
        /// loro guadagni; si ferma quando e' sceso sotto un millesimo.</summary>
        static void Ring(float[] l, float[] r, int rate, double at, double f, double a, double tau, double gl, double gr)
        {
            if (f <= 0.0 || f >= rate * 0.45) return;
            int i0 = (int)Math.Round(at * rate);
            if (i0 >= l.Length) return;
            double w = 2.0 * Math.PI * f / rate, rad = Math.Exp(-1.0 / (tau * rate));
            double c = 2.0 * rad * Math.Cos(w), r2 = rad * rad;
            double y2 = 0.0, y1 = a * rad * Math.Sin(w);
            int n = Math.Min(l.Length - i0, (int)(tau * rate * 6.9));
            for (int k = 1; k < n; k++)
            {
                l[i0 + k] += (float)(y1 * gl); r[i0 + k] += (float)(y1 * gr);
                double y = c * y1 - r2 * y2;
                y2 = y1; y1 = y;
            }
        }

        /// <summary>Una nota di carillon: il pettine d'acciaio. Fondamentale con una copia appena scordata che batte piano, e i modi di una trave a
        /// sbalzo (6,27 e 17,55 volte la fondamentale) che fanno il "tin" dell'attacco e muoiono subito. Piu' acuta, piu' corta.</summary>
        static void BoxNote(float[] l, float[] r, int rate, double at, double midi, double vel, double len, System.Random rng, double cents)
        {
            double f = HzOf(midi + cents / 100.0);
            double tau = Math.Min(2.2, Math.Max(0.45, 1.5 * Math.Pow(440.0 / f, 0.4)));
            if (len < 0.9) tau *= 0.75;
            double pan = Math.Max(-0.62, Math.Min(0.62, (midi - 66.0) / 22.0));       // gravi a sinistra, acute a destra, come le lame
            double ang = (pan + 1.0) * Math.PI * 0.25, gl = Math.Cos(ang), gr = Math.Sin(ang);
            Ring(l, r, rate, at, f, vel, tau, gl, gr);
            Ring(l, r, rate, at, f * 1.0035, vel * 0.32, tau, gl, gr);
            Ring(l, r, rate, at, f * 6.267, vel * 0.20, 0.10, gl, gr);
            Ring(l, r, rate, at, f * 17.55, vel * 0.06, 0.03, gl, gr);
            // il colpetto della punta sul dente
            int i0 = (int)Math.Round(at * rate);
            var bp = Biquad.BandPass(3600f, 1.5f, rate);
            for (int k = 0; k < 90 && i0 + k < l.Length; k++)
            {
                float v = bp.Run((float)(rng.NextDouble() * 2.0 - 1.0)) * (float)Math.Exp(-k / 22.0) * (float)(vel * 0.05);
                l[i0 + k] += v * (float)gl; r[i0 + k] += v * (float)gr;
            }
        }

        /// <summary>Una voce dell'organo che tiene l'accordo: dente di sega a banda limitata (poche armoniche, suono di archi lontani), una
        /// copia a sinistra e una a destra appena scordate, vibrato lento. Attacco di 1 s, rilascio che si sovrappone alla voce dopo.</summary>
        static void PadVoice(float[] l, float[] r, int rate, double at, double dur, int midi, double gain, int seed)
        {
            double f0 = HzOf(midi);
            int H = Math.Max(2, Math.Min(11, (int)(1500.0 / f0)));
            var w = Saw(H, 1.15);
            double rel = 1.3;
            int i0 = (int)Math.Round(at * rate), n = (int)((dur + rel * 4.0) * rate);
            double loop = MenuLoop;
            // frequenze del vibrato con cicli interi nel giro: la voce si richiude senza salto
            double vibF = Math.Max(1.0, Math.Round(0.30 * loop + seed % 3)) / loop;
            double depth = 0.0030, ph0 = seed * 1.7;
            for (int side = 0; side < 2; side++)
            {
                double f = f0 * (side == 0 ? 0.9976 : 1.0024);
                double gl = side == 0 ? 0.86 : 0.50, gr = side == 0 ? 0.50 : 0.86;
                for (int k = 0; k < n; k++)
                {
                    int i = i0 + k; if (i >= l.Length) break;
                    double t = (double)k / rate, tg = (double)i / rate;
                    double env = Math.Min(1.0, t / 1.0); env = env * env * (3.0 - 2.0 * env);
                    if (t > dur) env *= Math.Exp(-(t - dur) / rel);
                    if (env < 1e-4) { if (t > dur) break; continue; }
                    double th = 2.0 * Math.PI * f * tg - f * depth / vibF * Math.Cos(2.0 * Math.PI * vibF * tg + ph0);
                    double v = Harmonics(th, w) * env * gain;
                    l[i] += (float)(v * gl); r[i] += (float)(v * gr);
                }
            }
        }

        /// <summary>Pedale: la nota grave dell'organo, seno con la seconda e la terza armonica, che respira per tutta la battuta.</summary>
        static void PedalNote(float[] l, float[] r, int rate, double at, double dur, int midi, double gain)
        {
            double f = HzOf(midi);
            double w = 2.0 * Math.PI * f / rate;
            int i0 = (int)Math.Round(at * rate), n = (int)((dur + 0.6) * rate);
            for (int k = 0; k < n; k++)
            {
                int i = i0 + k; if (i >= l.Length) break;
                double t = (double)k / rate;
                double env = Math.Min(1.0, t / 0.12) * (t < dur ? 1.0 : Math.Exp(-(t - dur) / 0.2));
                double v = (Math.Sin(w * i) + 0.45 * Math.Sin(2.0 * w * i) + 0.2 * Math.Sin(3.0 * w * i)) * env * gain;
                l[i] += (float)v; r[i] += (float)v;
            }
        }

        /// <summary>Campana grave e lontana in re: i parziali del bronzo con la terza minore in testa, coda di molti secondi.</summary>
        static void BellD(float[] l, float[] r, int rate, double at, double gain)
        {
            double f = 146.83;
            double[] ratio = { 0.5, 1.0, 1.19, 1.5, 2.0, 2.51, 3.0, 4.07 };
            double[] amp = { 0.6, 1.0, 0.7, 0.5, 0.4, 0.25, 0.18, 0.1 };
            for (int p = 0; p < ratio.Length; p++)
                Ring(l, r, rate, at, f * ratio[p], amp[p] * gain, 4.2 / (1.0 + 0.5 * p), 0.8, 0.8);
        }

        /// <summary>Eco di pietra di una sala: pettini e passa-tutto alla Schroeder, trattati come periodici (due giri, si tiene il secondo): la
        /// coda della fine ricade sull'inizio. Due varianti di lunghezze per i due canali: l'eco non e' la stessa a destra e a sinistra.</summary>
        static float[] Hall(float[] s, int rate, float feedback, int variant)
        {
            int n = s.Length;
            double k = rate / 44100.0;
            int[] cl = variant == 0 ? new[] { (int)(1557 * k), (int)(1617 * k), (int)(1491 * k), (int)(1422 * k) }
                                    : new[] { (int)(1613 * k), (int)(1553 * k), (int)(1459 * k), (int)(1381 * k) };
            int[] al = variant == 0 ? new[] { (int)(556 * k), (int)(441 * k) } : new[] { (int)(577 * k), (int)(463 * k) };
            var cb = new float[4][]; var ci = new int[4]; var cf = new float[4];
            for (int c = 0; c < 4; c++) cb[c] = new float[cl[c]];
            var ab = new float[2][]; var ai = new int[2];
            for (int p = 0; p < 2; p++) ab[p] = new float[al[p]];
            var wet = new float[n];
            const float damp = 0.32f;
            for (int i = 0; i < 2 * n; i++)
            {
                float x = s[i % n] * 0.25f, y = 0f;
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
                if (i >= n) wet[i - n] = y;
            }
            return wet;
        }

        /// <summary>Il tema del menu, stereo interlacciato (sinistro, destro, sinistro...), 22,05 kHz, 96 s esatti. Lento (circa due secondi): va in un thread.</summary>
        public static float[] MenuTheme()
        {
            const int R = MenuRate;
            int N = (int)(MenuLoop * R);
            int tail = 9 * R;                                    // le code oltre il giro ricadono all'inizio
            var dl = new float[N + tail]; var dr = new float[N + tail];     // carillon e campana (passano per la sala)
            var pl = new float[N + tail]; var pr = new float[N + tail];     // organo (ha il suo corpo: poca sala)
            var rng = new System.Random(2026);
            double bar = 3.0 * MenuBeat;

            // ---- organo e pedale: un accordo a battuta, le battute uguali di seguito si tengono insieme
            int b = 0;
            while (b < MenuBars)
            {
                int run = 1;
                while (b + run < MenuBars && MenuProg[b + run] == MenuProg[b] && (b + run) % 8 != 0) run++;
                var chord = ChordOf(MenuProg[b]);
                int section = b / 8;
                double g = section == 0 ? 0.060 : section == 1 ? 0.074 : section == 2 ? 0.066 : 0.052;
                double dur = run * bar + 0.4;
                foreach (int m in chord.pad) PadVoice(pl, pr, R, b * bar, dur, m, g, b * 5 + m);
                b += run;
            }
            for (int i = 0; i < MenuBars; i++)
            {
                var chord = ChordOf(MenuProg[i]);
                PedalNote(pl, pr, R, i * bar, 2.4, chord.bass, 0.10);
            }
            // un mi bemolle che trema sotto il tema che si stona (la seconda minore del re): battute 19, 20 e 23
            foreach (int eb in new[] { 18, 19, 22 })
            {
                double t0 = eb * bar, w = 2.0 * Math.PI * HzOf(63) / R;
                int i0 = (int)(t0 * R), n = (int)(bar * R);
                for (int k = 0; k < n; k++)
                {
                    double t = (double)k / n;
                    double env = Math.Sin(Math.PI * t); env *= env;
                    double v = Math.Sin(w * (i0 + k)) * env * (0.55 + 0.45 * Math.Sin(2.0 * Math.PI * 5.0 * k / R)) * 0.020;
                    pl[i0 + k] += (float)(v * 0.7); pr[i0 + k] += (float)(v * 0.7);
                }
            }

            // ---- carillon: accompagnamento (basso sul primo battito, due note sul secondo e sul terzo) e melodia
            for (int i = 0; i < MenuBars; i++)
            {
                var chord = ChordOf(MenuProg[i]);
                int section = i / 8;
                double warp = section == 2 ? Math.Min(1.0, (i - 16 + 1) / 6.0) : 0.0;     // il carillon si stona nelle battute 17-24
                double t0 = i * bar;
                double Cents() => warp > 0.0 ? (rng.NextDouble() * 2.0 - 1.0) * (5.0 + 19.0 * warp) : 0.0;
                double soft = section == 3 ? 0.8 : 1.0;
                BoxNote(dl, dr, R, t0, chord.bass + 12, 0.30 * soft, 2.0, rng, Cents());
                int[] t = chord.box;
                BoxNote(dl, dr, R, t0 + MenuBeat, t[0], 0.16 * soft, 1.0, rng, Cents());
                BoxNote(dl, dr, R, t0 + MenuBeat, t[1], 0.13 * soft, 1.0, rng, Cents());
                BoxNote(dl, dr, R, t0 + 2 * MenuBeat, t[1], 0.15 * soft, 1.0, rng, Cents());
                BoxNote(dl, dr, R, t0 + 2 * MenuBeat, t[2], 0.12 * soft, 1.0, rng, Cents());
            }
            foreach (var note in MenuMelodyNotes())
            {
                int section = note.bar / 8;
                double warp = section == 2 ? Math.Min(1.0, (note.bar - 16 + 1) / 6.0) : 0.0;
                double cents = warp > 0.0 ? (rng.NextDouble() * 2.0 - 1.0) * (6.0 + 22.0 * warp) : 0.0;
                double at = note.bar * bar + note.beat * MenuBeat;
                double vel = section == 3 ? 0.50 : 0.62;
                if (note.beat == 0f) vel *= 1.12;                    // il primo battito appena piu' forte
                BoxNote(dl, dr, R, at, note.midi, vel, note.len, rng, cents);
                if (section == 1) BoxNote(dl, dr, R, at + 0.5 * MenuBeat, note.midi + 12, 0.20, note.len, rng, 0.0);      // l'eco di cristallo all'ottava, in B
            }

            // ---- campana grave: in testa ai due giri del tema
            BellD(dl, dr, R, 0.0, 0.13);
            BellD(dl, dr, R, 16 * bar, 0.11);

            // ---- tutto ricade in 96 s esatti: le code oltre il giro si ripiegano sull'inizio
            Fold(dl, N); Fold(dr, N); Fold(pl, N); Fold(pr, N);
            var cl = new float[N]; var cr = new float[N];
            Array.Copy(dl, cl, N); Array.Copy(dr, cr, N);

            // ---- la sala: eco lunga attorno al carillon, appena un'ombra attorno all'organo
            var midBox = new float[N];
            for (int i = 0; i < N; i++) midBox[i] = (cl[i] + cr[i]) * 0.5f;
            var wetL = Hall(midBox, R, 0.88f, 0);
            var wetR = Hall(midBox, R, 0.88f, 1);
            var midPad = new float[N];
            for (int i = 0; i < N; i++) midPad[i] = (pl[i] + pr[i]) * 0.5f;
            var padWetL = Hall(midPad, R, 0.84f, 0);
            var padWetR = Hall(midPad, R, 0.84f, 1);
            var outL = new float[N]; var outR = new float[N];
            for (int i = 0; i < N; i++)
            {
                outL[i] = cl[i] + pl[i] + wetL[i] * 0.46f + padWetL[i] * 0.22f;
                outR[i] = cr[i] + pr[i] + wetR[i] * 0.46f + padWetR[i] * 0.22f;
            }
            LoopHighPass(outL, 38f, R); LoopHighPass(outR, 38f, R);
            // un velo scuro sulle note piu' alte: sala vecchia, niente scintille oltre gli 8 kHz
            var lpL = Biquad.LowPass(7600f, 0.6f, R); var lpR = Biquad.LowPass(7600f, 0.6f, R);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < N; i++)
                {
                    float a = lpL.Run(outL[i]), c = lpR.Run(outR[i]);
                    if (pass == 1) { outL[i] = a; outR[i] = c; }
                }
            var s = new float[2 * N];
            for (int i = 0; i < N; i++) { s[2 * i] = outL[i]; s[2 * i + 1] = outR[i]; }
            SoftClip(s, 1.15f);
            Normalize(s, 0.85f);
            return s;
        }

        /// <summary>Le code di un suono scritto oltre il giro si sommano all'inizio (il giro e' periodico).</summary>
        static void Fold(float[] d, int n)
        {
            for (int i = n; i < d.Length; i++) d[i - n] += d[i];
        }
    }
}

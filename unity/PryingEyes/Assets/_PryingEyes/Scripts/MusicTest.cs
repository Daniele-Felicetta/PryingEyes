#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -musictest: la musica adattiva (MusicDirector.cs). I campioni arrivano dal thread; i giri durano
    /// 24 s, si richiudono senza scatti e non hanno silenzi ne' valori rotti; i livelli stanno tra l'ambiente e gli spari. Con lo stato
    /// dell'ondata forzato: muta tra le ondate, tamburi e ostinato che entrano col combattimento e crescono coi nemici, archi con tanti
    /// nemici e piu' forti col Cardinale, tamburi che si ritirano con gli ultimi, tutto giu' in fretta da morti, l'accordo d'organo a ondata
    /// superata e non a ondata saltata, l'organo d'ambiente che si abbassa sotto la musica, il volume Musica rispettato e i tre strati
    /// allineati. Infine un'ondata vera accende la musica da sola.</summary>
    public sealed class MusicTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        readonly Dictionary<string, float> rms = new Dictionary<string, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-musictest") >= 0) new GameObject("MusicTest").AddComponent<MusicTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] musictest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            MusicDirector.debugOverride = false;
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] musictest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        static bool Near(float a, float b, float tol = 0.02f) => Mathf.Abs(a - b) <= tol;
        static float Db(float x) => 20f * Mathf.Log10(Mathf.Max(x, 1e-6f));
        static float Sq(float x) => x * x;

        static float[] Samples(AudioClip clip)
        {
            var s = new float[clip.samples * clip.channels];
            clip.GetData(s, 0);
            return s;
        }

        /// <summary>Durata, valori validi, livello e giunzione del giro (o coda nel silenzio) di una clip della musica.</summary>
        void CheckClip(string name, AudioClip clip, float seconds, bool loop)
        {
            if (clip == null) { Check(name + ": pronta", false); return; }
            var s = Samples(clip);
            double sum = 0; float peak = 0; bool bad = false;
            foreach (var v in s) { if (float.IsNaN(v) || float.IsInfinity(v)) bad = true; sum += v * v; peak = Mathf.Max(peak, Mathf.Abs(v)); }
            float r = (float)Math.Sqrt(sum / s.Length);
            rms[name] = r;
            Check($"{name}: {clip.length:F2} s a {clip.frequency} Hz", Mathf.Abs(clip.length - seconds) < 0.01f);
            Check($"{name}: valori validi, picco {peak:F2}, rms {r:F3} ({Db(r):F1} dBFS)", !bad && peak <= 1f && r > 0.02f);
            int N = s.Length;
            if (loop)
            {
                float local = 0f;
                for (int i = 0; i < 48; i++) local = Mathf.Max(local, Mathf.Max(Mathf.Abs(s[i + 1] - s[i]), Mathf.Abs(s[N - 1 - i] - s[N - 2 - i])));
                float jump = Mathf.Abs(s[0] - s[N - 1]);
                Check($"{name}: il giro si richiude senza scatto (salto {jump:F4}, vicini {local:F4})", jump <= 2f * local + 1e-4f);
            }
            else
            {
                float tail = 0f;
                for (int i = N - clip.frequency / 20; i < N; i++) tail = Mathf.Max(tail, Mathf.Abs(s[i]));
                Check($"{name}: finisce nel silenzio (ultimi 50 ms {tail:F3})", tail < 0.06f);
            }
        }

        /// <summary>Con -musicwav: la clip in unity_logs/music come WAV a 16 bit, per ascoltarla e misurarla fuori dal gioco.</summary>
        static void Export(string name, AudioClip clip)
        {
            if (clip == null || Array.IndexOf(Environment.GetCommandLineArgs(), "-musicwav") < 0) return;
            var s = Samples(clip);
            System.IO.Directory.CreateDirectory("unity_logs/music");
            using (var f = new System.IO.BinaryWriter(System.IO.File.Create($"unity_logs/music/{name}.wav")))
            {
                var ascii = System.Text.Encoding.ASCII;
                int bytes = s.Length * 2;
                f.Write(ascii.GetBytes("RIFF")); f.Write(36 + bytes); f.Write(ascii.GetBytes("WAVEfmt "));
                f.Write(16); f.Write((short)1); f.Write((short)1); f.Write(clip.frequency); f.Write(clip.frequency * 2); f.Write((short)2); f.Write((short)16);
                f.Write(ascii.GetBytes("data")); f.Write(bytes);
                foreach (var v in s) f.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
        }

        static float WindowRms(AudioClip clip, float seconds)
        {
            if (clip == null) return 0f;
            var s = Samples(clip);
            int n = Mathf.Min(s.Length, Mathf.RoundToInt(seconds * clip.frequency));
            double sum = 0; for (int i = 0; i < n; i++) sum += s[i] * s[i];
            return n > 0 ? (float)Math.Sqrt(sum / n) : 0f;
        }

        static AudioSource FindOrgan()
        {
            var pa = GameObject.Find("ProcAmbience");
            if (pa == null) return null;
            foreach (var s in pa.GetComponents<AudioSource>()) if (s.clip != null && s.clip.name == "organo") return s;
            return null;
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll();
            var gm = GameManager.I; var player = gm.player;
            player.invulnerable = true;
            var md = MusicDirector.I;
            Check("il direttore della musica c'e'", md != null);
            if (md == null) yield break;

            // i campioni
            float t0 = Time.realtimeSinceStartup;
            while (!SoundBank.MusicReady && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            Check($"i campioni arrivano dal thread ({Time.realtimeSinceStartup - t0:F1} s dopo il piano)", SoundBank.MusicReady);
            yield return null;
            Check("le clip sono sugli strati", md.Ready);
            CheckClip("tamburi", SoundBank.MusicDrums, SoundGen.MusicLoop, true);
            CheckClip("ostinato", SoundBank.MusicBass, SoundGen.MusicLoop, true);
            CheckClip("archi", SoundBank.MusicTension, SoundGen.MusicLoop, true);
            CheckClip("accordo", SoundBank.MusicCleared, 4.5f, false);
            Export("tamburi", SoundBank.MusicDrums); Export("ostinato", SoundBank.MusicBass); Export("archi", SoundBank.MusicTension);
            Export("accordo", SoundBank.MusicCleared); Export("organo", SoundBank.Organ); Export("sparo", SoundBank.Gun("rifle"));

            // livelli (col volume Musica al massimo, per non dipendere dalle impostazioni di chi gioca)
            if (rms.ContainsKey("tamburi") && rms.ContainsKey("ostinato") && rms.ContainsKey("archi"))
            {
                float rD = rms["tamburi"], rB = rms["ostinato"], rT = rms["archi"], bus = MusicDirector.Bus;
                float few = Mathf.Sqrt(Sq(rD * MusicDirector.DrumsLow) + Sq(rB * MusicDirector.BassLow)) * bus;
                float full = Mathf.Sqrt(Sq(rD * MusicDirector.DrumsHigh) + Sq(rB * MusicDirector.BassHigh) + Sq(rT * MusicDirector.TensionBoss)) * bus;
                float organ = WindowRms(SoundBank.Organ, 14f) * 0.36f;
                float shot = WindowRms(SoundBank.Gun("rifle"), 0.1f);
                Check($"livelli: pochi nemici {Db(few):F1} dBFS, al massimo {Db(full):F1} dBFS (organo in chiesa {Db(organ):F1}, primo decimo di uno sparo {Db(shot):F1})",
                      Db(full) < Db(shot) - 6f && Db(few) > Db(organ) - 3f && Db(full) < -12f);
            }

            // tra le ondate
            MusicDirector.debugOverride = true;
            MusicDirector.debugFighting = false; MusicDirector.debugBoss = false; MusicDirector.debugDown = false;
            MusicDirector.debugAlive = 0; MusicDirector.debugRemaining = -1;
            yield return new WaitForSeconds(0.5f);
            Check("tra le ondate e' muta", md.DrumsVolume == 0f && md.BassVolume == 0f && md.TensionVolume == 0f && !md.Playing && MusicDirector.Duck == 0f);

            // combattimento, pochi nemici
            MusicDirector.debugFighting = true; MusicDirector.debugAlive = 4; MusicDirector.debugRemaining = 20;
            yield return new WaitForSeconds(3f);
            float heat = 4f / MusicDirector.FullHeat;
            Check($"col combattimento entrano tamburi e ostinato ({md.DrumsVolume:F2}, {md.BassVolume:F2})",
                  md.Playing && Near(md.DrumsVolume, Mathf.Lerp(MusicDirector.DrumsLow, MusicDirector.DrumsHigh, heat)) && Near(md.BassVolume, Mathf.Lerp(MusicDirector.BassLow, MusicDirector.BassHigh, heat)));
            Check("pochi nemici: niente archi", md.TensionVolume == 0f);
            Check($"la musica copre l'organo d'ambiente (Duck {MusicDirector.Duck:F2})", MusicDirector.Duck > 0.5f);
            float music = GameSettings.Music * MusicDirector.Bus;
            Check($"il volume Musica e' rispettato ({md.DrumsSource.volume:F3} = {md.DrumsVolume:F2} x {music:F2})", Near(md.DrumsSource.volume, md.DrumsVolume * music, 0.002f));

            // tanti nemici, poi il Cardinale
            MusicDirector.debugAlive = 14;
            yield return new WaitForSeconds(3f);
            Check($"tanti nemici: tamburi al massimo ({md.DrumsVolume:F2}) e archi tesi ({md.TensionVolume:F2})",
                  Near(md.DrumsVolume, MusicDirector.DrumsHigh) && Near(md.TensionVolume, MusicDirector.TensionLevel));
            MusicDirector.debugBoss = true;
            yield return new WaitForSeconds(1.5f);
            Check($"col Cardinale gli archi salgono ({md.TensionVolume:F2})", Near(md.TensionVolume, MusicDirector.TensionBoss));
            AudioSource dS = md.DrumsSource, bS = md.BassSource, tS = md.TensionSource;
            if (dS.isPlaying && bS.isPlaying && tS.isPlaying)
                Check($"i tre strati sono allineati ({dS.time:F3} / {bS.time:F3} / {tS.time:F3} s)", Mathf.Abs(dS.time - bS.time) < 0.03f && Mathf.Abs(dS.time - tS.time) < 0.03f);
            else Debug.Log("[PryingEyes] musictest: le sorgenti non risultano in riproduzione (nessuna uscita audio?): allineamento non misurato");

            // restano gli ultimi
            MusicDirector.debugBoss = false; MusicDirector.debugAlive = 2; MusicDirector.debugRemaining = 2;
            yield return new WaitForSeconds(Mathf.Max(MusicDirector.DrumsHigh * 7f, MusicDirector.TensionBoss * 8f) + 0.6f);   // le discese: 3,5 e 4 s ogni mezzo punto
            float expect = Mathf.Lerp(MusicDirector.DrumsLow, MusicDirector.DrumsHigh, 2f / MusicDirector.FullHeat) * MusicDirector.StragglerDrums;
            Check($"con gli ultimi i tamburi si ritirano ({md.DrumsVolume:F2} ~ {expect:F2}) e gli archi tacciono ({md.TensionVolume:F2})",
                  Near(md.DrumsVolume, expect) && md.TensionVolume < 0.01f && md.BassVolume > 0.3f);

            // da morti
            MusicDirector.debugAlive = 8; MusicDirector.debugRemaining = 30;
            yield return new WaitForSeconds(2f);
            MusicDirector.debugDown = true;
            yield return new WaitForSeconds(1.2f);
            Check($"da morti la musica cade in fretta ({md.DrumsVolume:F2}, {md.BassVolume:F2})", md.DrumsVolume < 0.01f && md.BassVolume < 0.01f);
            MusicDirector.debugDown = false;
            yield return new WaitForSeconds(2.5f);
            Check($"rinati, la musica torna ({md.BassVolume:F2})", md.Playing && md.BassVolume > 0.3f);
            var organSrc = FindOrgan();
            float organFight = organSrc != null ? organSrc.volume : -1f;

            // ondata superata
            int stings = md.Stings;
            MusicDirector.debugFighting = false;
            MusicDirector.WaveCleared();
            yield return new WaitForSeconds(0.3f);
            Check($"ondata superata: l'accordo d'organo ({md.Stings - stings})", md.Stings == stings + 1);
            yield return new WaitForSeconds(4.5f);
            Check("dopo l'ondata la musica sfuma e si ferma", !md.Playing && md.DrumsVolume == 0f && md.BassVolume == 0f && md.TensionVolume == 0f);
            yield return new WaitForSeconds(2f);
            float organIdle = organSrc != null ? organSrc.volume : -1f;
            Check($"l'organo d'ambiente sta sotto la musica ({organFight:F3} in combattimento, {organIdle:F3} dopo)", organSrc != null && organIdle > 0.001f && organFight < organIdle * 0.7f);

            // ondata saltata: niente accordo
            MusicDirector.debugFighting = true; MusicDirector.debugAlive = 6; MusicDirector.debugRemaining = 20;
            yield return new WaitForSeconds(1.5f);
            stings = md.Stings;
            MusicDirector.debugFighting = false;
            yield return new WaitForSeconds(1f);
            Check("ondata saltata: nessun accordo", md.Stings == stings);

            // un'ondata vera
            MusicDirector.debugOverride = false;
            yield return new WaitForSeconds(4f);
            var rd = RoundDirector.I;
            Check("c'e' il direttore delle ondate", rd != null);
            if (rd != null)
            {
                SurvivalMode.Hold(false);
                rd.SurvivalNextWaveIn(0.2f);
                float w0 = Time.realtimeSinceStartup;
                while (!rd.Fighting && Time.realtimeSinceStartup - w0 < 6f) yield return null;
                yield return new WaitForSeconds(2.5f);
                Check($"un'ondata vera accende la musica da sola (ondata {RoundDirector.Round}, tamburi {md.DrumsVolume:F2})", rd.Fighting && md.Playing && md.DrumsVolume > 0.2f);
                SurvivalMode.Hold(true);
                Enemy.ClearAll();
            }
            player.invulnerable = false;
        }
    }
}
#endif

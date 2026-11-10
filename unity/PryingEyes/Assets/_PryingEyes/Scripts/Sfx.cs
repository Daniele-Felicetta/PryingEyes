using System.Threading.Tasks;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Riproduzione: una riserva di sorgenti per i suoni una tantum (3D nel punto giusto, o 2D per arma e
    /// giocatore) e sorgenti in loop attaccate agli oggetti (litania dei monaci, ronzio dei droni, vento).</summary>
    public static class Sfx
    {
        static AudioSource[] pool;
        static int next;
        static GameObject host;

        static void Ensure()
        {
            if (host != null) return;
            host = new GameObject("Sfx");
            Object.DontDestroyOnLoad(host);
            pool = new AudioSource[28];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("Voce" + i);
                go.transform.SetParent(host.transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                pool[i] = s;
            }
        }

        /// <summary>Suono una tantum in un punto (spatial 1 = 3D, 0 = 2D).</summary>
        public static AudioSource At(AudioClip clip, Vector3 pos, float vol = 1f, float pitch = 1f,
                                     float minDist = 2f, float maxDist = 35f, float spatial = 1f)
        {
            if (clip == null) return null;
            Ensure();
            AudioSource src = null;
            for (int k = 0; k < pool.Length; k++)
            {
                var s = pool[(next + k) % pool.Length];
                if (!s.isPlaying) { src = s; next = (next + k + 1) % pool.Length; break; }
            }
            if (src == null) { src = pool[next]; next = (next + 1) % pool.Length; }
            src.transform.position = pos;
            src.clip = clip;
            src.volume = vol * GameSettings.Effects;
            src.pitch = pitch;
            src.spatialBlend = spatial;
            src.minDistance = minDist;
            src.maxDistance = maxDist;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0f;
            src.loop = false;
            src.Play();
            return src;
        }

        /// <summary>Suono 2D (arma, passi, colpi subiti): passa comunque per l'eco della hall.</summary>
        public static AudioSource UI(AudioClip clip, float vol = 1f, float pitch = 1f) =>
            At(clip, Vector3.zero, vol, pitch, 1f, 500f, 0f);

        /// <summary>Loop attaccato a un oggetto, partenza da un punto a caso (piu' monaci non cantano all'unisono).</summary>
        public static AudioSource Loop(GameObject owner, AudioClip clip, float vol, float minDist, float maxDist,
                                       float pitch = 1f, float spatial = 1f, float doppler = 0f, bool music = false)
        {
            if (clip == null) return null;
            var src = owner.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = GameSettings.Track(src, vol, music);
            src.pitch = pitch;
            src.spatialBlend = spatial;
            src.minDistance = minDist;
            src.maxDistance = maxDist;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = doppler;
            src.playOnAwake = false;
            src.time = Random.value * clip.length * 0.95f;
            src.Play();
            return src;
        }
    }

    /// <summary>I suoni del gioco, generati una volta da SoundGen e tenuti in memoria. Il valzer (lento da calcolare)
    /// parte in un thread all'avvio e si usa quando e' pronto.</summary>
    public static partial class SoundBank
    {
        static AudioClip[] shots, steps0, steps1, steps2, taps, creaks, chants, whispers;
        static AudioClip hit, kill, hurt, reload, bell, ding, doors, strike, monkDeath, eyeHum, eyeStare, eyeBurst, wind, room, waltz;
        static Task<float[]> waltzTask;
        static int waltzRate;

        public static AudioClip Clip(string name, float[] s, int rate = SoundGen.Rate)
        {
            var c = AudioClip.Create(name, s.Length, 1, rate, false);
            c.SetData(s, 0);
            return c;
        }

        static AudioClip One(ref AudioClip c, string name, System.Func<float[]> gen) => c != null ? c : (c = Clip(name, gen()));

        static AudioClip Pick(ref AudioClip[] set, int count, string name, System.Func<int, float[]> gen)
        {
            if (set == null)
            {
                set = new AudioClip[count];
                for (int i = 0; i < count; i++) set[i] = Clip(name + i, gen(i));
            }
            return set[Random.Range(0, count)];
        }

        public static AudioClip Shot => Pick(ref shots, 3, "sparo", i => SoundGen.Gunshot(100 + i));

        // spari e rumori meccanici delle armi: generati alla prima richiesta (Warm li prepara tutti)
        static readonly System.Collections.Generic.Dictionary<string, AudioClip[]> gunSets = new System.Collections.Generic.Dictionary<string, AudioClip[]>();
        static readonly System.Collections.Generic.Dictionary<string, AudioClip[]> mechSets = new System.Collections.Generic.Dictionary<string, AudioClip[]>();
        public static readonly string[] GunKinds = { "pistol", "pistol45", "revolver", "magnum", "smg", "smg45", "rifle", "ak", "br", "lever", "sniper", "amr", "shotgun", "lmg", "flame", "beam", "spine" };
        /// <summary>Campioni di uno sparo: i set speciali (fiamme, raggio, spine) hanno un generatore a parte.</summary>
        public static float[] GunSamples(string set, int seed) =>
            set == "flame" || set == "beam" || set == "spine" ? SoundGen.Special(set, seed) : SoundGen.Gunshot(set, seed);
        public static readonly string[] MechKinds = { "magout", "magin", "boltback", "boltfwd", "boltup", "slidefwd", "pumpback", "pumpfwd", "leverdown", "leverup",
            "shellin", "shellopen", "cylopen", "cylshells", "cylclose", "breakclose", "dry", "click", "holster", "draw" };

        public static AudioClip Gun(string set)
        {
            if (!gunSets.TryGetValue(set, out var arr))
            {
                arr = new AudioClip[3];
                for (int i = 0; i < 3; i++) arr[i] = Clip("sparo_" + set + i, GunSamples(set, 200 + i));
                gunSets[set] = arr;
            }
            return arr[Random.Range(0, arr.Length)];
        }

        public static AudioClip Mech(string name)
        {
            if (!mechSets.TryGetValue(name, out var arr))
            {
                arr = new AudioClip[2];
                for (int i = 0; i < 2; i++) arr[i] = Clip("mecc_" + name + i, SoundGen.Mech(name, 300 + i));
                mechSets[name] = arr;
            }
            return arr[Random.Range(0, arr.Length)];
        }

        static readonly System.Collections.Generic.Dictionary<string, AudioClip[]> casingSets = new System.Collections.Generic.Dictionary<string, AudioClip[]>();
        public static AudioClip Casing(string kind)
        {
            if (!casingSets.TryGetValue(kind, out var arr))
            {
                arr = new AudioClip[3];
                for (int i = 0; i < 3; i++) arr[i] = Clip("bossolo_" + kind + i, SoundGen.CasingDrop(kind, 400 + i));
                casingSets[kind] = arr;
            }
            return arr[Random.Range(0, arr.Length)];
        }
        public static AudioClip HitTick => One(ref hit, "colpito", SoundGen.HitTick);
        public static AudioClip Kill => One(ref kill, "ucciso", SoundGen.KillThunk);
        public static AudioClip Hurt => One(ref hurt, "ferito", SoundGen.Hurt);
        public static AudioClip Reload => One(ref reload, "ricarica", SoundGen.Reload);
        public static AudioClip Step(int surface) =>
            surface == 1 ? Pick(ref steps1, 4, "passoMoquette", i => SoundGen.Step(1, i))
            : surface == 2 ? Pick(ref steps2, 4, "passoLegno", i => SoundGen.Step(2, i))
            : Pick(ref steps0, 4, "passoMarmo", i => SoundGen.Step(0, i));
        public static AudioClip Bell => One(ref bell, "campanello", SoundGen.ReceptionBell);
        public static AudioClip Ding => One(ref ding, "dingAscensore", SoundGen.ElevatorDing);
        public static AudioClip Doors => One(ref doors, "porteAscensore", SoundGen.ElevatorDoors);
        public static AudioClip Creak => Pick(ref creaks, 5, "scricchiolio", i => SoundGen.Creak(200 + i));
        public static AudioClip Whisper => Pick(ref whispers, 3, "sussurro", i => SoundGen.Whisper(300 + i));
        public static AudioClip Wind => One(ref wind, "vento", () => SoundGen.Wind(7));
        public static AudioClip RoomTone => One(ref room, "ronzioStanza", SoundGen.RoomTone);
        public static AudioClip Tap => Pick(ref taps, 3, "bastone", i => SoundGen.StaffTap(400 + i));
        public static AudioClip Strike => One(ref strike, "bastoneATerra", SoundGen.StaffStrike);
        public static AudioClip MonkDeath => One(ref monkDeath, "monacoCade", SoundGen.MonkDeath);
        public static AudioClip EyeHum => One(ref eyeHum, "ronzioOcchio", SoundGen.EyeHum);
        public static AudioClip EyeStare => One(ref eyeStare, "occhioFissa", SoundGen.EyeStare);
        public static AudioClip EyeBurst => One(ref eyeBurst, "occhioMuore", SoundGen.EyeBurst);
        static AudioClip ignite, toll, liftRide, organ;
        public static AudioClip Ignite => One(ref ignite, "accendiCandela", SoundGen.Ignite);
        public static AudioClip Toll => One(ref toll, "rintocco", SoundGen.BellToll);
        public static AudioClip LiftRide => One(ref liftRide, "discesaAscensore", SoundGen.LiftRide);
        public static AudioClip Organ => organ != null ? organ : (organ = Clip("organo", SoundGen.OrganDrone(out int rt), rt));

        static Task<float[]>[] chantTasks;
        static int chantRate;

        /// <summary>Due varianti della litania (le altre differenze le fa il pitch di ogni monaco). Calcolate in un
        /// thread: finche' non sono pronte vale null e il monaco la attacca appena puo' (Enemy.Update).</summary>
        public static AudioClip Chant
        {
            get
            {
                if (chants == null && chantTasks != null && chantTasks[0].IsCompleted && chantTasks[1].IsCompleted
                    && !chantTasks[0].IsFaulted && !chantTasks[1].IsFaulted)
                {
                    chants = new AudioClip[2];
                    for (int i = 0; i < 2; i++) chants[i] = Clip("litania" + i, chantTasks[i].Result, chantRate);
                }
                return chants != null ? chants[Random.Range(0, 2)] : null;
            }
        }

        /// <summary>Genera all'avvio tutto quello che e' veloce, cosi' il primo sparo non fa scattare il gioco;
        /// litanie e valzer (i piu' lunghi) partono in un thread.</summary>
        public static void Warm()
        {
            foreach (var k in GunKinds) _ = Gun(k);
            foreach (var k in MechKinds) _ = Mech(k);
            _ = Casing("pistol"); _ = Casing("rifle"); _ = Casing("shell"); _ = Casing("big");
            _ = Shot; _ = HitTick; _ = Kill; _ = Hurt; _ = Reload; _ = Step(0); _ = Step(1); _ = Step(2);
            _ = Bell; _ = Ding; _ = Doors; _ = Creak; _ = Whisper; _ = Wind; _ = RoomTone;
            _ = Tap; _ = Strike; _ = MonkDeath; _ = EyeHum; _ = EyeStare; _ = EyeBurst;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Web non supporta i thread C#; genera questi suoni durante il caricamento.
            if (chants == null)
            {
                var first = SoundGen.MonkChant(600, out chantRate);
                var second = SoundGen.MonkChant(601, out int secondRate);
                chants = new[] { Clip("litania0", first, chantRate), Clip("litania1", second, secondRate) };
            }
            if (waltz == null) waltz = Clip("valzer", SoundGen.Waltz(out waltzRate), waltzRate);
#else
            if (chantTasks == null)
                chantTasks = new[]
                {
                    Task.Run(() => SoundGen.MonkChant(600, out chantRate)),
                    Task.Run(() => SoundGen.MonkChant(601, out int _)),
                };
            if (waltzTask == null) waltzTask = Task.Run(() => SoundGen.Waltz(out waltzRate));
#endif
        }

        public static bool WaltzReady => waltz != null || (waltzTask != null && waltzTask.IsCompleted);

        public static AudioClip Waltz
        {
            get
            {
                if (waltz == null && waltzTask != null && waltzTask.IsCompleted && !waltzTask.IsFaulted)
                    waltz = Clip("valzer", waltzTask.Result, waltzRate);
                return waltz;
            }
        }
    }
}

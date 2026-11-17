using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>I campioni dei suoni si calcolano in anticipo in uno o due thread a priorita' bassa, non sul thread principale.
    ///
    /// Prima l'avvio calcolava tutto di fila nel primo fotogramma (armi, meccanismi, impatti, suoni dei temi, ambiente: quasi tre secondi su un
    /// PC veloce, sei con due processori): la finestra restava nera e Windows la dava per "non risponde" prima che comparisse il menu.
    /// Adesso <see cref="PrefetchAll"/> mette in coda i calcoli e torna subito; i getter di SoundBank (One, Pick, Gun, Mech, Casing, Organ,
    /// Impact) usano i campioni gia' pronti quando creano la clip (pochi microsecondi), e se il calcolo non e' ancora partito lo fanno loro
    /// stessi, come prima. Il risultato e' lo stesso di prima: i generatori di SoundGen usano System.Random con il seme fisso e solo matematica.
    ///
    /// Come funziona: mentre <c>prefetching</c> e' vero (dentro <see cref="InPrefetch"/>) i getter, invece di generare, accodano il calcolo con
    /// il nome della clip e restituiscono null. Quindi i metodi Warm* di prima (che chiamano i getter) servono cosi' come sono per accodare.
    /// Solo il thread principale tocca la tabella dei lavori; i thread toccano solo il lavoro che hanno preso.</summary>
    public static partial class SoundBank
    {
        sealed class Job
        {
            public string name;
            public Func<float[]> gen;
            public float[] samples;
            public int state;          // 0 in coda, 1 in calcolo (nel thread o nel principale), 2 finito, 3 fallito
        }

        static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();       // solo dal thread principale
        static readonly ConcurrentQueue<Job> queue = new ConcurrentQueue<Job>();
        static int workersRunning, workersMax;
        static bool prefetching, prefetchStarted, prefetchFaultLogged;

        /// <summary>Quanti campioni sono ancora da calcolare o da consumare (per le prove e per il log).</summary>
        public static int PrefetchPending => jobs.Count;

        /// <summary>Accoda il calcolo di tutti i suoni dei primi minuti di gioco e torna subito: l'ambiente per primo, poi armi, meccanismi,
        /// impatti, temi, furtivita', coltello, perk e reliquie. Una volta sola. Lo chiama il GameManager all'avvio della scena.</summary>
        public static void PrefetchAll()
        {
            if (prefetchStarted) return;
            prefetchStarted = true;
            InPrefetch(() =>
            {
                _ = Organ; _ = RoomTone; _ = Wind; _ = TheatreTone; _ = Murmur; _ = ClinicBuzz;      // ProcAmbience li aspetta (AmbienceReady)
                Warm();
                WarmThemes();
                WarmStealth();
                WarmKnife();
                WarmPerks();
                WarmRelics();
            });
        }

        /// <summary>Tutti i calcoli messi in coda sono finiti (anche quelli falliti): per le prove.</summary>
        public static bool PrefetchDone
        {
            get
            {
                foreach (var j in jobs.Values) if (Volatile.Read(ref j.state) < 2) return false;
                return true;
            }
        }

        /// <summary>Per le prove: ricalcola sul thread principale ogni campione preparato da un thread e lo confronta con quello del thread (stessi
        /// valori, bit per bit: i generatori usano System.Random con seme fisso e solo matematica). Restituisce quanti sono diversi; 'compared' e'
        /// quanti ne ha confrontati e 'first' il nome del primo diverso. Costa quanto il vecchio avvio (alcuni secondi): solo per le prove.</summary>
        public static int VerifyPrefetched(out int compared, out string first)
        {
            int bad = 0, n = 0; string firstBad = "";
            void Check(Job j)
            {
                if (Volatile.Read(ref j.state) != 2 || j.samples == null) return;
                var again = j.gen();
                n++;
                bool same = again.Length == j.samples.Length;
                for (int i = 0; same && i < again.Length; i++) same = again[i] == j.samples[i];
                if (!same) { bad++; if (firstBad.Length == 0) firstBad = j.name; }
            }
            foreach (var j in jobs.Values) Check(j);
            if (consumed != null) foreach (var j in consumed) Check(j);
            compared = n; first = firstBad;
            return bad;
        }

        static List<Job> consumed;

        /// <summary>Per le prove: tiene i campioni dei calcoli gia' usati per creare una clip, cosi' VerifyPrefetched li confronta tutti (anche i sei
        /// dell'ambiente, che nascono al primo fotogramma). Va acceso prima del primo fotogramma.</summary>
        public static void KeepConsumedForTest() { if (consumed == null) consumed = new List<Job>(); }

        /// <summary>I sei suoni dell'ambiente dei piani (ProcAmbience) sono calcolati: le sue sorgenti si possono creare senza aspettare.
        /// I nomi sono quelli delle clip (organo, ronzioStanza, vento, tonoTeatro, brusio, ronzioNeon).</summary>
        public static bool AmbienceReady =>
            Ready("organo") && Ready("ronzioStanza") && Ready("vento") && Ready("tonoTeatro") && Ready("brusio") && Ready("ronzioNeon");

        /// <summary>Il campione 'name' non ha piu' niente da aspettare: e' stato calcolato, o la clip e' gia' fatta, o nessuno l'ha messo in coda
        /// (in quel caso il getter lo calcola al volo).</summary>
        static bool Ready(string name) => !jobs.TryGetValue(name, out var j) || Volatile.Read(ref j.state) >= 2;

        /// <summary>Esegue 'batch' (chiamate ai getter, tipicamente i Warm*) in modo che accodino i calcoli invece di farli, poi avvia i thread.</summary>
        static void InPrefetch(Action batch)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            batch();        // niente thread: si calcola subito, durante il caricamento
#else
            bool was = prefetching;
            prefetching = true;
            try { batch(); }
            finally { prefetching = was; }
            StartWorkers();
#endif
        }

        /// <summary>Dei getter, mentre si accoda: il calcolo del campione 'name' parte in un thread (una volta sola per nome).</summary>
        static void Prefetch(string name, Func<float[]> gen)
        {
            if (jobs.ContainsKey(name)) return;
            var j = new Job { name = name, gen = gen };
            jobs[name] = j;
            queue.Enqueue(j);
        }

        static void PrefetchSet(string name, int count, Func<int, float[]> gen)
        {
            for (int i = 0; i < count; i++) { int k = i; Prefetch(name + i, () => gen(k)); }
        }

        /// <summary>I campioni di 'name' per creare la clip: quelli del thread se ha finito, altrimenti si calcolano qui (con 'gen').
        /// Se il thread ci sta lavorando proprio adesso si aspetta che finisca (al piu' la durata di quel calcolo, come farlo qui).</summary>
        static float[] Computed(string name, Func<float[]> gen)
        {
            if (jobs.TryGetValue(name, out var j))
            {
                jobs.Remove(name);
                if (Interlocked.CompareExchange(ref j.state, 1, 0) != 0)
                {
                    while (Volatile.Read(ref j.state) == 1) Thread.Sleep(1);
                    if (Volatile.Read(ref j.state) == 2)
                    {
                        var s = j.samples;
                        if (consumed != null) consumed.Add(j);       // la prova li vuole ancora (VerifyPrefetched)
                        else j.samples = null;
                        return s;
                    }
                }
                // il calcolo non era ancora partito (il thread lo salta: lo stato non e' piu' 0) o e' fallito nel thread: lo si fa qui
            }
            return gen();
        }

        static AudioClip[] MakeSet(string name, int count, Func<int, float[]> gen)
        {
            var set = new AudioClip[count];
            for (int i = 0; i < count; i++) { int k = i; set[i] = Clip(name + i, Computed(name + i, () => gen(k))); }
            return set;
        }

        static void StartWorkers()
        {
            if (workersMax == 0) workersMax = Mathf.Clamp(SystemInfo.processorCount / 2, 1, 3);
            while (true)
            {
                int n = Volatile.Read(ref workersRunning);
                if (n >= workersMax || queue.IsEmpty) return;
                if (Interlocked.CompareExchange(ref workersRunning, n + 1, n) != n) continue;
                BackgroundWork.StartThread(Work, "PryingEyes suoni", afterFirstFrame: true);
            }
        }

        static void Work()
        {
            try
            {
                while (queue.TryDequeue(out var j))
                {
                    if (Interlocked.CompareExchange(ref j.state, 1, 0) != 0) continue;       // l'ha preso il thread principale
                    try
                    {
                        j.samples = j.gen();
                        Volatile.Write(ref j.state, 2);
                    }
                    catch (Exception e)
                    {
                        Volatile.Write(ref j.state, 3);           // il thread principale lo ricalcola quando serve
                        if (!prefetchFaultLogged) { prefetchFaultLogged = true; Debug.LogWarning("[PryingEyes] suono non calcolabile in un thread (" + j.name + "): " + e.Message); }
                    }
                }
            }
            finally { Interlocked.Decrement(ref workersRunning); }
            if (!queue.IsEmpty) StartWorkers();          // un lavoro arrivato mentre il thread usciva
        }
    }
}

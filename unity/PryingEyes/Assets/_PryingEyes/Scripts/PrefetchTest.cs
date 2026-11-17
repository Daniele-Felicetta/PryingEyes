#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -prefetchtest: i suoni calcolati in anticipo dai thread (SoundPrefetch.cs). All'avvio della scena si
    /// mettono in coda piu' di cento calcoli e il thread principale non li aspetta (i fotogrammi continuano); quando i thread finiscono, ogni
    /// campione e' uguale, bit per bit, a quello che il thread principale ricalcola da solo (nessun generatore dipende da dove gira); le clip
    /// nascono dai campioni pronti e l'ambiente dei piani ha le sue sei sorgenti in loop.</summary>
    public sealed class PrefetchTest : MonoBehaviour
    {
        int checks, failed, exceptions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-prefetchtest") < 0) return;
            SoundBank.KeepConsumedForTest();         // prima del primo fotogramma: le sorgenti dell'ambiente consumano i loro campioni li'
            new GameObject("PrefetchTest").AddComponent<PrefetchTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] prefetchtest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception || (type == LogType.Warning && message.Contains("non calcolabile in un thread"))) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            Check("nessuna eccezione e nessun thread fallito", exceptions == 0);
            Debug.Log($"[PryingEyes] prefetchtest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        IEnumerator Run()
        {
            int queued = SoundBank.PrefetchPending;
            Check($"all'avvio della scena i calcoli sono in coda ({queued})", queued >= 100);
            float t0 = Time.realtimeSinceStartup;
            int frames = 0;
            while (!SoundBank.PrefetchDone && Time.realtimeSinceStartup - t0 < 90f) { frames++; yield return null; }
            float took = Time.realtimeSinceStartup - t0;
            Check($"i thread finiscono ({took:F1} s)", SoundBank.PrefetchDone);
            Debug.Log($"[PryingEyes] prefetchtest: {frames} fotogrammi mentre i thread calcolavano ({took:F1} s)");
            Check($"il thread principale non aspetta: i fotogrammi continuano ({frames})", took < 1f || frames >= 5);
            Check("l'ambiente dei piani e' pronto (SoundBank.AmbienceReady)", SoundBank.AmbienceReady);

            var amb = GameObject.Find("ProcAmbience");
            int sources = 0, withClip = 0;
            if (amb != null) foreach (var s in amb.GetComponents<AudioSource>()) { sources++; if (s.clip != null && s.clip.length > 1f) withClip++; }
            Check($"ProcAmbience ha le sei sorgenti in loop, ognuna con la sua clip ({sources} sorgenti, {withClip} con la clip)", sources == 6 && withClip == 6);

            int bad = SoundBank.VerifyPrefetched(out int compared, out string first);
            Check($"ogni campione dei thread e' uguale al ricalcolo sul thread principale ({compared} confrontati{(bad > 0 ? ", primo diverso: " + first + ", " + bad + " in tutto" : "")})", bad == 0 && compared >= 100);

            // le clip nascono dai campioni pronti (la prima richiesta dopo il calcolo non ricalcola niente: pochi millisecondi in tutto)
            float t1 = Time.realtimeSinceStartup;
            int made = 0;
            foreach (var k in SoundBank.GunKinds) { if (SoundBank.Gun(k) != null) made++; }
            foreach (var k in SoundBank.MechKinds) { if (SoundBank.Mech(k) != null) made++; }
            if (SoundBank.HitTick != null) made++;
            if (SoundBank.Step(0) != null) made++;
            if (SoundBank.Organ != null && SoundBank.Organ.frequency > 0) made++;
            float ms = (Time.realtimeSinceStartup - t1) * 1000f;
            int expected = SoundBank.GunKinds.Length + SoundBank.MechKinds.Length + 3;
            Check($"le clip delle armi, dei meccanismi e dei passi si creano dai campioni pronti ({made}/{expected} in {ms:F0} ms)", made == expected && ms < 500f);
            Check("a prova finita non resta niente in coda che qualcuno debba ancora aspettare", SoundBank.PrefetchDone);
        }
    }
}
#endif

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PryingEyes
{
    /// <summary>Lavori lunghi in un thread a parte, a priorita' bassa: calcolare i campioni dei suoni, la musica e le trame non deve togliere
    /// tempo al thread principale, che costruisce il menu e risponde a Windows. Con Task.Run (thread del pool, priorita' normale) su un PC con
    /// pochi processori questi lavori si dividevano la CPU alla pari col thread principale: la finestra restava nera e Windows la dava per
    /// "non risponde". Un thread per lavoro: sono pochi e vivono pochi secondi. Nel Web (niente thread C#) il lavoro si fa subito, in chi chiama.
    ///
    /// Due accorgimenti per i PC con pochi processori (misurati con il gioco limitato a due processori logici):
    /// <list type="bullet">
    /// <item>Thread.Start di Mono aspetta che il nuovo thread sia partito, e su una CPU piena puo' voler dire decine di millisecondi a thread, tutti
    /// sul thread principale: a creare e avviare i thread e' un lavoro del pool, e chi chiama torna subito.</item>
    /// <item>Il cancello dell'avvio: i lavori "dopo il primo fotogramma" (suoni, musica) non partono finche' la scena non ha fatto qualche
    /// fotogramma (StartupTrace lo apre), cosi' il thread principale costruisce il menu e avvia il giocatore senza dividere la CPU con loro.
    /// Fuori dal gioco in esecuzione (strumenti dell'editor, prove senza scena) il cancello non e' armato e si parte subito. Un lavoro che
    /// il thread principale puo' aspettare di colpo (.Result) NON deve essere "dopo il primo fotogramma": aspetterebbe un cancello
    /// che puo' aprire solo lui.</item></list></summary>
    public static class BackgroundWork
    {
        static bool gateArmed, gateOpen;
        static readonly List<Action> waiting = new List<Action>();      // solo dal thread principale

        /// <summary>Arma il cancello (all'avvio del gioco, prima della scena): i lavori "dopo il primo fotogramma" aspettano <see cref="OpenGate"/>.</summary>
        public static void ArmGate() { gateArmed = true; gateOpen = false; waiting.Clear(); }

        /// <summary>Apre il cancello: partono tutti i lavori che aspettavano, e da qui in poi partono subito.</summary>
        public static void OpenGate()
        {
            gateOpen = true;
            var start = waiting.ToArray();
            waiting.Clear();
            foreach (var a in start) a();
        }

        static bool MustWait(bool afterFirstFrame) => afterFirstFrame && gateArmed && !gateOpen;

        public static Task<T> Run<T>(Func<T> work, bool afterFirstFrame = false)
        {
            var done = new TaskCompletionSource<T>();
            StartThread(() =>
            {
                try { done.SetResult(work()); }
                catch (Exception e) { done.SetException(e); }
            }, "PryingEyes lavoro", afterFirstFrame);
            return done.Task;
        }

        /// <summary>Avvia 'body' in un thread nuovo a priorita' bassa senza far aspettare chi chiama.</summary>
        public static void StartThread(Action body, string name, bool afterFirstFrame = false)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            body();         // il Web non ha thread C#: si lavora subito, durante il caricamento
            return;
#else
            Action start = () => ThreadPool.QueueUserWorkItem(_ =>
                new Thread(() => body()) { IsBackground = true, Name = name, Priority = System.Threading.ThreadPriority.BelowNormal }.Start());
            if (MustWait(afterFirstFrame)) waiting.Add(start);
            else start();
#endif
        }
    }
}

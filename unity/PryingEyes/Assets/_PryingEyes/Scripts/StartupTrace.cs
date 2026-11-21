using System.Globalization;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>I tempi dell'avvio nel log del giocatore (<c>[PryingEyes] avvio: ...</c>): quanto passa dal processo al primo codice del gioco,
    /// alla scena, al menu e al primo fotogramma. Costa una riga di log per tappa. Serve a capire, dal Player.log di un PC lento, in
    /// quale tappa il gioco resta fermo e senza finestra.
    ///
    /// Apre anche il cancello dell'avvio di <see cref="BackgroundWork"/>: i lavori di sottofondo (suoni, musica) partono dopo qualche
    /// fotogramma della scena, non mentre il thread principale costruisce il menu.</summary>
    public static class StartupTrace
    {
        static float last;
        static bool firstFrameDone;

        /// <summary>Scrive la tappa: secondi dall'avvio del motore e dalla tappa prima.</summary>
        public static void Mark(string what)
        {
            float t = Time.realtimeSinceStartup;
            Debug.Log("[PryingEyes] avvio: " + what + " a " + t.ToString("F2", CultureInfo.InvariantCulture)
                + " s (+" + (t - last).ToString("F2", CultureInfo.InvariantCulture) + ")");
            last = t;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Begin()
        {
            string lead = "";
#if !UNITY_WEBGL || UNITY_EDITOR
            // dal processo al primo codice: comprende il controllo dell'antivirus sull'eseguibile e le DLL
            try
            {
                double s = (System.DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds;
                lead = " (processo avviato " + s.ToString("F2", CultureInfo.InvariantCulture) + " s fa)";
            }
            catch (System.Exception) { }
#endif
            Mark("primo codice del gioco" + lead);
            Application.onBeforeRender += FirstFrame;
            BackgroundWork.ArmGate();
            var gate = new GameObject("StartupGate") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(gate);
            gate.AddComponent<Gate>();
        }

        static void FirstFrame()
        {
            if (firstFrameDone) return;
            firstFrameDone = true;
            Application.onBeforeRender -= FirstFrame;
            Mark("primo fotogramma disegnato");
        }

        /// <summary>Apre il cancello dei lavori di sottofondo al terzo fotogramma: il primo ha disegnato il menu, e le prime richieste alla
        /// scheda video (le sue compilazioni) non dividono la CPU coi calcoli dei suoni.</summary>
        sealed class Gate : MonoBehaviour
        {
            int frames;

            void Update()
            {
                // il tempo fra un Update e il successivo e' il costo del fotogramma in mezzo: il primo comprende le prime richieste alla scheda
                // video (su un PC senza la cache dei shader del driver puo' essere lungo)
                if (++frames < 3) { Mark("Update " + frames); return; }
                BackgroundWork.OpenGate();
                Mark("Update 3: lavori di sottofondo avviati (suoni e musica)");
                Destroy(gameObject);
            }
        }
    }
}

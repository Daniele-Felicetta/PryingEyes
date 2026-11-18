using UnityEngine;

namespace PryingEyes.Characters
{
    /// <summary>Scrive i personaggi con qualcosa in sospeso ogni due secondi, e subito quando il gioco si ferma, perde il fuoco o si chiude, e
    /// conta il tempo di gioco di chi sta giocando. Nasce con l'elenco dei personaggi (Roster.Load) e resta fra una scena e l'altra.</summary>
    public sealed class CharacterAutosave : MonoBehaviour
    {
        const float Every = 2f;
        static CharacterAutosave instance;
        float next;

        public static void Ensure()
        {
            if (instance != null || !Application.isPlaying) return;
            var go = new GameObject("CharacterAutosave") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CharacterAutosave>();
        }

        void Awake() { instance = this; }

        void Update()
        {
            Progress.Tick(Time.unscaledDeltaTime);
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Every;
            Roster.Flush();
        }

        static void WriteEverything() { Progress.CommitTime(); Roster.Flush(); }

        void OnApplicationPause(bool paused) { if (paused) WriteEverything(); }
        void OnApplicationFocus(bool focused) { if (!focused) WriteEverything(); }
        void OnApplicationQuit() { WriteEverything(); }
        void OnDestroy() { if (instance == this) { WriteEverything(); instance = null; } }
    }
}

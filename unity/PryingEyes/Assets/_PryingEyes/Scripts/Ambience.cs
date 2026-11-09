using UnityEngine;

namespace PryingEyes
{
    /// <summary>L'aria dell'hotel: eco della hall, vento dal lucernario rotto, ronzio della stanza vuota,
    /// scricchiolii sparsi e un grammofono che suona un valzer in una stanza al piano di sopra.</summary>
    public class Ambience : MonoBehaviour
    {
        public static Ambience I;
        static readonly Vector3 Skylight = new Vector3(0f, 8.6f, -6f);
        static readonly Vector3 GramophoneRoom = new Vector3(20f, 5.6f, -22.4f);   // dietro la porta est del soppalco
        AudioSource gramophone;
        float creakT;

        public static void Create()
        {
            if (I != null) return;
            var go = new GameObject("Ambience");
            go.transform.position = new Vector3(0f, 4f, 0f);
            I = go.AddComponent<Ambience>();
        }

        void Start()
        {
            SoundBank.Warm();
            // eco di una hall di marmo da 70x46x10 m: coda lunga e scura
            var rz = gameObject.AddComponent<AudioReverbZone>();
            rz.minDistance = 48f;
            rz.maxDistance = 60f;
            rz.reverbPreset = AudioReverbPreset.User;
            rz.room = -1000;
            rz.roomHF = -700;
            rz.decayTime = 2.7f;
            rz.decayHFRatio = 0.55f;
            rz.reflections = -1300;
            rz.reflectionsDelay = 0.025f;
            rz.reverb = -350;
            rz.reverbDelay = 0.04f;
            rz.diffusion = 100f;
            rz.density = 100f;

            var w = new GameObject("Vento");
            w.transform.position = Skylight;
            Sfx.Loop(w, SoundBank.Wind, 0.45f, 6f, 45f);
            Sfx.Loop(gameObject, SoundBank.RoomTone, 0.22f, 1f, 500f, 1f, 0f);
            creakT = Random.Range(4f, 9f);
        }

        void Update()
        {
            if (gramophone == null && SoundBank.WaltzReady && SoundBank.Waltz != null)
            {
                var g = new GameObject("Grammofono");
                g.transform.position = GramophoneRoom;
                gramophone = Sfx.Loop(g, SoundBank.Waltz, 0.4f, 3f, 55f, music: true);
                if (GameManager.autoTest) Debug.Log($"[PryingEyes] grammofono: valzer di {SoundBank.Waltz.length:F1} s");
            }
            creakT -= Time.deltaTime;
            if (creakT <= 0f)
            {
                creakT = Random.Range(6f, 16f);
                var p = new Vector3(Random.Range(-33f, 33f), Random.Range(2f, 9f), Random.Range(-22f, 22f));
                Sfx.At(SoundBank.Creak, p, Random.Range(0.25f, 0.5f), Random.Range(0.85f, 1.15f), 4f, 40f);
            }
        }
    }
}

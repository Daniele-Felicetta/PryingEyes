using UnityEngine;

namespace PryingEyes
{
    /// <summary>Monta la componente furtiva sul giocatore (torcia, ping e freccia, cuore) e la tiene viva: luce e minaccia a ogni
    /// fotogramma, il battito del cuore che sale quando un prete e' vicino. Si installa una volta, all'inizio della partita a piani.</summary>
    public class StealthRig : MonoBehaviour
    {
        public static StealthRig I;
        public Torch torch;
        public Guide guide;
        public Knife knife;
        AudioSource beat;

        public static void Install()
        {
            if (I != null) return;
            var gm = GameManager.I;
            if (gm == null || gm.player == null || gm.player.cam == null) return;
            var go = new GameObject("Furtivita");
            Seats.Adopt(go);                    // split screen: appartiene al posto che gira
            bool primarySeat = Seats.Current == null || Seats.Current == Seats.Primary;
            I = go.AddComponent<StealthRig>();
            I.primary = primarySeat;
            I.torch = go.AddComponent<Torch>();
            I.torch.Init(gm.player.cam);
            I.torch.NewRun();
            I.guide = go.AddComponent<Guide>();
            SoundBank.WarmStealth();
            SoundBank.WarmKnife();
            SoundBank.WarmPerks();
            I.knife = go.AddComponent<Knife>();
            I.knife.Init(gm.player.cam);
            var clip = SoundBank.Heartbeat;
            if (clip != null && primarySeat)         // un solo cuore per tutti: in split screen batte per chiunque sia in pericolo (lo comanda il posto 0)
            {
                I.beat = go.AddComponent<AudioSource>();
                I.beat.clip = clip;
                I.beat.loop = true;
                I.beat.spatialBlend = 0f;
                I.beat.volume = 0f;
                I.beat.playOnAwake = false;
                I.beat.Play();
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-stealthtest") >= 0) go.AddComponent<StealthTest>();
        }

        /// <summary>Il rig del primo giocatore (o del giocatore singolo): fa girare i potenziamenti del mondo e il cuore, che sono di tutti.</summary>
        [System.NonSerialized] public bool primary = true;

        void Update() { using (Seats.Enter(this)) UpdateBody(); }

        void UpdateBody()
        {
            Stealth.Tick(Time.deltaTime);
            Proc.FloorDirector.I?.TickSeat(Time.deltaTime);      // la mappa, le scoperte e le cadute di questo giocatore
            if (!primary) return;
            Drops.Tick(Time.deltaTime);
            if (beat == null) return;
            float lv = Seats.MaxThreat();
            float v = lv > 0.05f && !GameMenu.Blocked ? Mathf.Pow(lv, 1.2f) * 0.5f : 0f;
            beat.volume = Mathf.MoveTowards(beat.volume, v * GameSettings.Effects, Time.deltaTime * 0.8f);
            beat.pitch = Mathf.Lerp(0.8f, 1.6f, lv);
        }
    }
}

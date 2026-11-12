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
            I = go.AddComponent<StealthRig>();
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
            if (clip != null)
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

        void Update()
        {
            Stealth.Tick(Time.deltaTime);
            Drops.Tick(Time.deltaTime);
            if (beat == null) return;
            float lv = Stealth.Level;
            float v = lv > 0.05f && !GameMenu.Blocked ? Mathf.Pow(lv, 1.2f) * 0.5f : 0f;
            beat.volume = Mathf.MoveTowards(beat.volume, v * GameSettings.Effects, Time.deltaTime * 0.8f);
            beat.pitch = Mathf.Lerp(0.8f, 1.6f, lv);
        }
    }
}

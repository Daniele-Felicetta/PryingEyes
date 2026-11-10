using System;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Suoni della componente furtiva: torcia, pile, ping, cuore, allerta dei preti. Stesse regole di SoundGen:
    /// solo System.Random e matematica, nessun file audio.</summary>
    public static partial class SoundGen
    {
        /// <summary>Interruttore della torcia: clic secco di plastica con un rimbalzo di molla.</summary>
        public static float[] TorchClick(bool on, int seed)
        {
            var r = new System.Random(seed);
            var s = Buf(0.18f);
            Burst(s, 0f, on ? 3100f : 2300f, 3f, 0.005f, 1.4f, r);
            Thump(s, 0.002f, on ? 520f : 360f, 220f, 0.015f, 0.018f, 0.55f);
            Burst(s, on ? 0.050f : 0.040f, on ? 1700f : 2900f, 4f, 0.008f, 0.65f, r);
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>Pila raccolta: la plastica che sbatte e un tintinnio di metallo.</summary>
        public static float[] BatteryPick()
        {
            var r = new System.Random(41);
            var s = Buf(0.42f);
            Burst(s, 0f, 1900f, 2.5f, 0.012f, 1.2f, r);
            Thump(s, 0f, 190f, 110f, 0.03f, 0.05f, 0.6f);
            Chime(s, 0.012f, 3300f, 0.07f, new[] { 1f, 2.74f }, new[] { 0.35f, 0.15f }, 1f);
            Burst(s, 0.11f, 2400f, 3f, 0.008f, 0.5f, r);
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>Cambio pila in 1,5 s: il tappo che salta, la pila vecchia che cade e rotola, la nuova che entra, il tappo
        /// che si avvita e il clic dell'interruttore.</summary>
        public static float[] BatterySwap()
        {
            var r = new System.Random(42);
            var s = Buf(1.6f);
            Burst(s, 0.02f, 1500f, 2f, 0.02f, 1.1f, r);
            Thump(s, 0.02f, 150f, 90f, 0.04f, 0.05f, 0.7f);
            for (int k = 0; k < 7; k++)
                Burst(s, 0.22f + k * 0.045f + (float)r.NextDouble() * 0.01f, U(r, 2200f, 4200f), 3f, 0.006f, 0.45f * Mathf.Exp(-k * 0.28f), r);
            Thump(s, 0.78f, 170f, 120f, 0.03f, 0.04f, 0.6f);
            Burst(s, 0.78f, 2100f, 3f, 0.01f, 0.9f, r);
            var bp = Biquad.BandPass(2800f, 2f);
            int a = (int)(0.98f * Rate), b = (int)(1.22f * Rate);
            for (int i = a; i < b && i < s.Length; i++)
            {
                float t = (float)(i - a) / Rate;
                s[i] += bp.Run(N(r)) * 0.28f * (0.5f + 0.5f * Mathf.Sin(TwoPi * 38f * t));
            }
            Burst(s, 1.32f, 3100f, 3f, 0.005f, 1.3f, r);
            Thump(s, 1.32f, 520f, 220f, 0.015f, 0.018f, 0.5f);
            Normalize(s, 0.75f);
            return s;
        }

        /// <summary>Filamento che sfarfalla: ronzio a 100 Hz a scatti, secco, con il clic del contatto.</summary>
        public static float[] TorchBuzz()
        {
            var r = new System.Random(43);
            var s = Buf(0.28f);
            var lp = Biquad.LowPass(1800f, 0.7f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float gate = (Mathf.Sin(TwoPi * 100f * t) > 0f ? 1f : 0.2f) * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(TwoPi * 23f * t)));
                s[i] = lp.Run(N(r) * 0.6f + Mathf.Sin(TwoPi * 100f * t) * 0.4f) * gate * Mathf.Exp(-t / 0.12f);
            }
            Burst(s, 0f, 3500f, 3f, 0.004f, 1.2f, r);
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Ping: un battito grave (l'impulso) e un tono acuto che scende un poco, con tre echi sempre piu' lontani e scuri.</summary>
        public static float[] PingOut()
        {
            var s = Buf(2.4f);
            Thump(s, 0f, 95f, 48f, 0.09f, 0.13f, 0.8f);
            float[] at = { 0f, 0.21f, 0.47f, 0.80f };
            float[] g = { 1f, 0.42f, 0.2f, 0.09f };
            for (int e = 0; e < at.Length; e++)
            {
                int i0 = (int)(at[e] * Rate);
                var lp = Biquad.LowPass(Mathf.Lerp(7000f, 1500f, e / 3f), 0.7f);
                double ph = 0;
                int n = (int)(0.9f * Rate);
                for (int k = 0; k < n && i0 + k < s.Length; k++)
                {
                    float t = (float)k / Rate;
                    float f = Mathf.Lerp(1320f, 1180f, Mathf.Clamp01(t / 0.12f));
                    ph += TwoPi * f / Rate;
                    float env = Mathf.Clamp01(t / 0.004f) * Mathf.Exp(-t / 0.16f);
                    s[i0 + k] += lp.Run((float)Math.Sin(ph) + 0.25f * (float)Math.Sin(ph * 2.0)) * env * g[e] * 0.55f;
                }
            }
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>Eco di una candela al ping: una campanella fredda e lontana.</summary>
        public static float[] PingEcho()
        {
            var r = new System.Random(44);
            var s = Buf(2.6f);
            Chime(s, 0f, 932f, 1.2f, new[] { 1f, 2.41f, 4.12f, 0.5f }, new[] { 1f, 0.35f, 0.14f, 0.2f }, 1f);
            var hp = Biquad.HighPass(3500f);
            for (int i = 0; i < 120; i++) s[i] += hp.Run(N(r)) * Mathf.Exp(-i / 50f) * 0.3f;
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Battito del cuore da loop: "lub-dub" in 0,86 s (con il pitch si accelera).</summary>
        public static float[] Heartbeat()
        {
            var r = new System.Random(45);
            int n = (int)(0.86f * Rate);
            var s = new float[n];
            Thump(s, 0f, 62f, 34f, 0.07f, 0.075f, 1f);
            Thump(s, 0f, 120f, 70f, 0.05f, 0.03f, 0.35f);
            Thump(s, 0.27f, 56f, 32f, 0.07f, 0.07f, 0.72f);
            Burst(s, 0f, 140f, 1.2f, 0.03f, 0.5f, r);
            Burst(s, 0.27f, 120f, 1.2f, 0.03f, 0.35f, r);
            Normalize(s, 0.9f);
            int fade = n / 10;
            for (int i = 0; i < fade; i++) s[n - 1 - i] *= (float)i / fade;
            return s;
        }

        /// <summary>Un prete ti ha visto: un lamento corale che sale di una quinta, formanti a "a", e un colpo grave.</summary>
        public static float[] AlertSting()
        {
            var r = new System.Random(46);
            var s = Buf(1.35f);
            var f1 = Biquad.BandPass(720f, 3f);
            var f2 = Biquad.BandPass(1150f, 4f);
            double p1 = 0, p2 = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float k = Mathf.Clamp01(t / 0.55f);
                float f = Mathf.Lerp(170f, 262f, k * k * (3f - 2f * k)) * (1f + 0.018f * Mathf.Sin(TwoPi * 6.2f * t));
                p1 += TwoPi * f / Rate; p2 += TwoPi * f * 1.006f / Rate;
                float saw = (float)((p1 / Math.PI) % 2.0 - 1.0) * 0.6f + (float)((p2 / Math.PI) % 2.0 - 1.0) * 0.6f;
                float env = Mathf.Clamp01(t / 0.05f) * (t < 0.8f ? 1f : Mathf.Exp(-(t - 0.8f) / 0.2f));
                s[i] = (f1.Run(saw) * 1.2f + f2.Run(saw) * 0.8f + f1.Run(N(r)) * 0.15f) * env;
            }
            Thump(s, 0.02f, 70f, 40f, 0.1f, 0.14f, 0.9f);
            Burst(s, 0f, 1300f, 1f, 0.06f, 1f, r);
            Normalize(s, 0.85f);
            return s;
        }

        /// <summary>Un prete ha sentito qualcosa: un "hm?" breve e cupo che sale appena.</summary>
        public static float[] Suspect()
        {
            var r = new System.Random(47);
            var s = Buf(0.5f);
            var f1 = Biquad.BandPass(520f, 3f);
            var f2 = Biquad.BandPass(930f, 4f);
            double p = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float f = Mathf.Lerp(150f, 188f, Mathf.Clamp01(t / 0.35f));
                p += TwoPi * f / Rate;
                float saw = (float)((p / Math.PI) % 2.0 - 1.0);
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.45f));
                s[i] = (f1.Run(saw) + 0.7f * f2.Run(saw) + f1.Run(N(r)) * 0.12f) * env;
            }
            Normalize(s, 0.6f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip torchOn, torchOff, batPick, batSwap, torchBuzz, pingOut, pingEcho, heartbeat, alertSting, suspect;
        public static AudioClip TorchOn => One(ref torchOn, "torciaOn", () => SoundGen.TorchClick(true, 31));
        public static AudioClip TorchOff => One(ref torchOff, "torciaOff", () => SoundGen.TorchClick(false, 32));
        public static AudioClip BatteryPick => One(ref batPick, "pilaRaccolta", SoundGen.BatteryPick);
        public static AudioClip BatterySwap => One(ref batSwap, "pilaCambio", SoundGen.BatterySwap);
        public static AudioClip TorchBuzz => One(ref torchBuzz, "torciaSfarfalla", SoundGen.TorchBuzz);
        public static AudioClip PingOut => One(ref pingOut, "ping", SoundGen.PingOut);
        public static AudioClip PingEcho => One(ref pingEcho, "pingEco", SoundGen.PingEcho);
        public static AudioClip Heartbeat => One(ref heartbeat, "cuore", SoundGen.Heartbeat);
        public static AudioClip AlertSting => One(ref alertSting, "allerta", SoundGen.AlertSting);
        public static AudioClip Suspect => One(ref suspect, "sospetto", SoundGen.Suspect);

        /// <summary>Genera subito i suoni della componente furtiva: il primo clic della torcia non deve far scattare il gioco.</summary>
        public static void WarmStealth()
        {
            _ = TorchOn; _ = TorchOff; _ = BatteryPick; _ = BatterySwap; _ = TorchBuzz;
            _ = PingOut; _ = PingEcho; _ = Heartbeat; _ = AlertSting; _ = Suspect;
        }
    }
}

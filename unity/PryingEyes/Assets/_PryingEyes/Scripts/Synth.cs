using UnityEngine;

namespace PryingEyes
{
    /// <summary>Procedural SFX (no assets): clips generated once, played via a persistent source.</summary>
    public static class Synth
    {
        static AudioSource src;
        static AudioClip cShot, cHit, cKill, cWave, cReload, cHurt;
        const int RATE = 44100;

        static void Ensure()
        {
            if (src != null) return;
            var host = new GameObject("Synth");
            Object.DontDestroyOnLoad(host);
            src = host.AddComponent<AudioSource>();
            src.volume = 0.45f;
        }

        static AudioClip Clip(float[] s)
        {
            var c = AudioClip.Create("synth", s.Length, 1, RATE, false);
            c.SetData(s, 0);
            return c;
        }

        static float[] Noise(float dur, float decay, float toneHz, float toneAmt)
        {
            int n = (int)(dur * RATE);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float env = Mathf.Exp(-t * decay);
                phase += 2f * Mathf.PI * toneHz / RATE;
                s[i] = (Random.Range(-1f, 1f) * (1f - toneAmt) + Mathf.Sin(phase) * toneAmt) * env;
            }
            return s;
        }

        static float[] Sweep(float dur, float f0, float f1, float decay, bool square)
        {
            int n = (int)(dur * RATE);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float f = Mathf.Lerp(f0, f1, t / dur);
                phase += 2f * Mathf.PI * f / RATE;
                float v = square ? Mathf.Sign(Mathf.Sin(phase)) * 0.6f : Mathf.Sin(phase);
                s[i] = v * Mathf.Exp(-t * decay);
            }
            return s;
        }

        public static void Shot() { Ensure(); if (cShot == null) cShot = Clip(Noise(0.14f, 28f, 110f, 0.5f)); src.PlayOneShot(cShot); }
        public static void Hit() { Ensure(); if (cHit == null) cHit = Clip(Sweep(0.06f, 1400f, 900f, 40f, false)); src.PlayOneShot(cHit); }
        public static void Kill() { Ensure(); if (cKill == null) cKill = Clip(Sweep(0.28f, 320f, 70f, 9f, true)); src.PlayOneShot(cKill); }
        public static void Wave() { Ensure(); if (cWave == null) cWave = Clip(Sweep(0.5f, 200f, 800f, 4f, false)); src.PlayOneShot(cWave, 0.7f); }
        public static void Reload() { Ensure(); if (cReload == null) cReload = Clip(Sweep(0.05f, 2000f, 1200f, 60f, true)); src.PlayOneShot(cReload); }
        public static void Hurt() { Ensure(); if (cHurt == null) cHurt = Clip(Sweep(0.2f, 110f, 60f, 18f, false)); src.PlayOneShot(cHurt); }
    }
}

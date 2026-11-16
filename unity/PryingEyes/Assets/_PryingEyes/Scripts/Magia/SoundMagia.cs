using UnityEngine;

namespace PryingEyes
{
    /// <summary>Suoni della mano sinistra e della stanza segreta del libro, procedurali come tutti gli altri (nessun file audio): fuoco,
    /// fulmine, gelo, sangue, ombra, onda, essenza, i sigilli sull'arma, la parete che si apre e il grimorio.</summary>
    public static partial class SoundGen
    {
        static float Edge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Rumore filtrato con inviluppo e banda che scivola: soffi, fiamme, sibili.</summary>
        static void Whoosh(float[] s, float at, float dur, float f0, float f1, float q, float gain, System.Random r)
        {
            var bp = new Biquad();
            int i0 = (int)(at * Rate), n = (int)(dur * Rate);
            for (int k = 0; k < n && i0 + k < s.Length; k++)
            {
                float t = (float)k / n;
                if ((k & 31) == 0) bp.SetBandPass(Mathf.Lerp(f0, f1, t), q, Rate);
                float env = Edge(0f, 0.18f, t) * (1f - Edge(0.55f, 1f, t));
                s[i0 + k] += bp.Run(N(r)) * env * gain;
            }
        }

        /// <summary>Crepitio: schiocchi brevi casuali (fuoco, scintille).</summary>
        static void Crackle(float[] s, float at, float dur, float rate, float freq, float gain, System.Random r)
        {
            int n = Mathf.RoundToInt(dur * rate);
            for (int k = 0; k < n; k++) Burst(s, at + (float)r.NextDouble() * dur, freq * U(r, 0.7f, 1.4f), 2.2f, U(r, 0.002f, 0.006f), gain * U(r, 0.4f, 1f), r);
        }

        public static float[] MagiaFuoco()
        {
            var r = new System.Random(301); var s = Buf(1.1f);
            Whoosh(s, 0f, 1.0f, 300f, 1400f, 0.7f, 0.9f, r);
            Crackle(s, 0.05f, 0.9f, 40f, 2600f, 0.5f, r);
            Thump(s, 0f, 90f, 60f, 0.1f, 0.12f, 0.5f);
            Normalize(s, 0.7f); return s;
        }

        public static float[] MagiaBoom()
        {
            var r = new System.Random(302); var s = Buf(2.2f);
            Thump(s, 0f, 70f, 34f, 0.25f, 0.45f, 1.2f);
            Whoosh(s, 0f, 1.6f, 1800f, 200f, 0.5f, 1f, r);
            Crackle(s, 0.08f, 1.6f, 30f, 2200f, 0.55f, r);
            SoftClip(s, 1.8f);
            Normalize(s, 0.85f); return s;
        }

        public static float[] MagiaScintille()
        {
            var r = new System.Random(303); var s = Buf(0.9f);
            Crackle(s, 0f, 0.85f, 90f, 5200f, 0.7f, r);
            for (int i = 0; i < s.Length; i++) { float t = (float)i / Rate; s[i] += Mathf.Sin(TwoPi * 120f * t) * Mathf.Sin(TwoPi * 7f * t) * 0.08f * (1f - t / 0.9f); }
            Normalize(s, 0.6f); return s;
        }

        public static float[] MagiaFulmine()
        {
            var r = new System.Random(304); var s = Buf(2.4f);
            Burst(s, 0f, 3800f, 0.6f, 0.012f, 1.4f, r);
            Burst(s, 0.004f, 1800f, 0.8f, 0.03f, 1f, r);
            Crackle(s, 0f, 0.35f, 140f, 4200f, 0.8f, r);
            var lp = new Biquad(); lp.SetLowPass(160f, 0.7f, Rate);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Edge(0.05f, 0.25f, t) * (1f - Edge(0.5f, 2.4f, t));
                s[i] += lp.Run(N(r)) * env * 2.4f;
            }
            SoftClip(s, 1.6f);
            Normalize(s, 0.85f); return s;
        }

        public static float[] MagiaGelo()
        {
            var r = new System.Random(305); var s = Buf(1.3f);
            Whoosh(s, 0f, 1.2f, 6500f, 3800f, 1.2f, 0.7f, r);
            for (int k = 0; k < 14; k++) Chime(s, U(r, 0.05f, 0.9f), U(r, 2400f, 5200f), 0.12f, new[] { 1f, 2.7f }, new[] { 1f, 0.3f }, U(r, 0.08f, 0.2f));
            Normalize(s, 0.6f); return s;
        }

        public static float[] MagiaSangue()
        {
            var r = new System.Random(306); var s = Buf(1.6f);
            Thump(s, 0f, 62f, 42f, 0.06f, 0.14f, 1f);
            Thump(s, 0.28f, 56f, 40f, 0.06f, 0.16f, 0.8f);
            var bp = new Biquad();
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                if ((i & 63) == 0) bp.SetBandPass(420f + 260f * Mathf.Sin(t * 23f) + 150f * Mathf.Sin(t * 41f), 3f, Rate);
                s[i] += bp.Run(N(r)) * 0.5f * Edge(0.1f, 0.3f, t) * (1f - Edge(1.0f, 1.6f, t));
            }
            Normalize(s, 0.65f); return s;
        }

        public static float[] MagiaOmbra()
        {
            var r = new System.Random(307); var s = Buf(0.9f);
            Burst(s, 0f, 2600f, 1.5f, 0.004f, 1.2f, r);           // lo schiocco delle dita
            Whoosh(s, 0.02f, 0.6f, 1600f, 180f, 0.6f, 0.9f, r);
            for (int i = 0; i < s.Length; i++) { float t = (float)i / Rate; s[i] += Mathf.Sin(TwoPi * Mathf.Lerp(220f, 55f, t / 0.9f) * t) * 0.25f * Edge(0.02f, 0.1f, t) * (1f - Edge(0.3f, 0.9f, t)); }
            Normalize(s, 0.7f); return s;
        }

        public static float[] MagiaOnda()
        {
            var r = new System.Random(308); var s = Buf(1.5f);
            Thump(s, 0f, 95f, 38f, 0.2f, 0.35f, 1.3f);
            Whoosh(s, 0f, 0.9f, 900f, 2400f, 0.6f, 0.9f, r);
            float[] ratios = { 1f, 1.5f, 2.01f }, amps = { 1f, 0.5f, 0.25f };
            Chime(s, 0.02f, 146.8f, 0.9f, ratios, amps, 0.35f);
            SoftClip(s, 1.4f);
            Normalize(s, 0.85f); return s;
        }

        public static float[] MagiaEssenza()
        {
            var r = new System.Random(309); var s = Buf(2.6f);
            float[] ratios = { 1f, 2f, 3.01f, 4.02f }, amps = { 1f, 0.5f, 0.25f, 0.12f };
            float[] chord = { 110f, 116.5f, 164.8f, 233.1f };               // il tritono e la seconda minore: un accordo che non chiude
            for (int k = 0; k < chord.Length; k++) Chime(s, 0.05f * k, chord[k], 2.0f, ratios, amps, 0.4f);
            Whoosh(s, 0.2f, 2.2f, 200f, 900f, 0.5f, 0.6f, r);
            Thump(s, 1.0f, 60f, 30f, 0.3f, 0.6f, 1f);
            Normalize(s, 0.8f); return s;
        }

        public static float[] MagiaGesto()
        {
            var r = new System.Random(310); var s = Buf(0.35f);
            Whoosh(s, 0f, 0.3f, 700f, 2200f, 0.8f, 0.6f, r);
            Normalize(s, 0.4f); return s;
        }

        public static float[] MagiaScelta()
        {
            var s = Buf(0.5f);
            Chime(s, 0f, 659.3f, 0.25f, new[] { 1f, 2.76f }, new[] { 1f, 0.3f }, 0.5f);
            Chime(s, 0.06f, 987.8f, 0.22f, new[] { 1f, 2.76f }, new[] { 1f, 0.3f }, 0.35f);
            Normalize(s, 0.45f); return s;
        }

        public static float[] MagiaTraccia()
        {
            var s = Buf(1.0f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Edge(0f, 0.08f, t) * (1f - Edge(0.6f, 1f, t));
                s[i] = (Mathf.Sin(TwoPi * 880f * t + Mathf.Sin(TwoPi * 6f * t) * 2f) * 0.5f + Mathf.Sin(TwoPi * 1320f * t) * 0.2f) * env;
            }
            Normalize(s, 0.35f); return s;
        }

        public static float[] MagiaTocco()
        {
            var r = new System.Random(311); var s = Buf(0.4f);
            Burst(s, 0f, 4200f, 2f, 0.004f, 1f, r);
            Chime(s, 0.002f, 1760f, 0.18f, new[] { 1f, 2.76f, 5.4f }, new[] { 1f, 0.4f, 0.12f }, 0.5f);
            Normalize(s, 0.5f); return s;
        }

        public static float[] MagiaPresa()
        {
            var r = new System.Random(312); var s = Buf(0.5f);
            Burst(s, 0f, 900f, 1.2f, 0.02f, 1f, r);
            Thump(s, 0f, 140f, 90f, 0.05f, 0.08f, 0.6f);
            Normalize(s, 0.5f); return s;
        }

        public static float[] MagiaIcore()
        {
            var r = new System.Random(313); var s = Buf(1.2f);
            for (int k = 0; k < 22; k++)
            {
                float at = U(r, 0f, 1.05f), f = U(r, 300f, 900f);
                int i0 = (int)(at * Rate), n = (int)(0.05f * Rate);
                for (int i = 0; i < n && i0 + i < s.Length; i++) { float t = (float)i / Rate; s[i0 + i] += Mathf.Sin(TwoPi * (f + 4000f * t) * t) * Mathf.Exp(-t / 0.012f) * 0.4f; }
            }
            Normalize(s, 0.5f); return s;
        }

        public static float[] MagiaSchiaffo()
        {
            var r = new System.Random(314); var s = Buf(0.9f);
            Burst(s, 0f, 1500f, 0.9f, 0.012f, 1.3f, r);
            Thump(s, 0f, 180f, 110f, 0.04f, 0.06f, 0.7f);
            Crackle(s, 0.01f, 0.5f, 120f, 5000f, 0.6f, r);
            Normalize(s, 0.75f); return s;
        }

        public static float[] MagiaRune()
        {
            var r = new System.Random(315); var s = Buf(1.8f);
            var lp = new Biquad(); lp.SetLowPass(240f, 0.9f, Rate);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Edge(0f, 0.3f, t) * (1f - Edge(1.2f, 1.8f, t));
                s[i] = (lp.Run(N(r)) * 2.2f + Mathf.Sin(TwoPi * 49f * t) * 0.3f + Mathf.Sin(TwoPi * 73.4f * t) * 0.2f) * env;
            }
            Crackle(s, 0.1f, 1.4f, 30f, 1800f, 0.4f, r);
            SoftClip(s, 2f);
            Normalize(s, 0.8f); return s;
        }

        public static float[] MagiaSigillo()
        {
            var s = Buf(1.4f);
            float[] ratios = { 1f, 2f, 3.01f }, amps = { 1f, 0.4f, 0.16f };
            float[] notes = { 293.7f, 440f, 587.3f };
            for (int k = 0; k < notes.Length; k++) Chime(s, k * 0.07f, notes[k], 0.9f, ratios, amps, 0.45f);
            Normalize(s, 0.6f); return s;
        }

        /// <summary>La parete segreta che si apre: pietra che gratta, un tonfo, polvere.</summary>
        public static float[] SegretaApre()
        {
            var r = new System.Random(316); var s = Buf(2.4f);
            var bp = new Biquad();
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                if ((i & 63) == 0) bp.SetBandPass(180f + 90f * Mathf.Sin(t * 13f) + 60f * (float)r.NextDouble(), 1.4f, Rate);
                float env = Edge(0.05f, 0.25f, t) * (1f - Edge(1.6f, 2.0f, t));
                s[i] = bp.Run(N(r)) * env * 1.4f;
            }
            Thump(s, 1.95f, 70f, 45f, 0.1f, 0.25f, 1.2f);
            Crackle(s, 1.95f, 0.4f, 60f, 3000f, 0.3f, r);
            Normalize(s, 0.75f); return s;
        }

        /// <summary>Un nodo del grimorio comprato: carta che brucia, una nota grave e il sigillo che si accende.</summary>
        public static float[] GrimorioNodo()
        {
            var r = new System.Random(317); var s = Buf(1.8f);
            Whoosh(s, 0f, 0.7f, 1200f, 3800f, 0.7f, 0.5f, r);
            Thump(s, 0.05f, 82f, 50f, 0.1f, 0.3f, 1f);
            float[] ratios = { 1f, 2f, 3.01f, 4.02f }, amps = { 1f, 0.45f, 0.2f, 0.08f };
            float[] chord = { 146.8f, 220f, 311.1f };
            for (int k = 0; k < chord.Length; k++) Chime(s, 0.1f + k * 0.08f, chord[k], 1.2f, ratios, amps, 0.4f);
            Normalize(s, 0.75f); return s;
        }

        /// <summary>Il grimorio che si apre: pelle, pagine, un respiro di voci.</summary>
        public static float[] GrimorioApre()
        {
            var r = new System.Random(318); var s = Buf(1.6f);
            Thump(s, 0f, 110f, 60f, 0.06f, 0.14f, 0.9f);
            float[] at = { 0.05f, 0.1f, 0.16f, 0.2f, 0.26f };
            foreach (float a in at) Burst(s, a, U(r, 3600f, 5200f), 1.0f, 0.014f, U(r, 0.35f, 0.7f), r);
            Whoosh(s, 0.2f, 1.3f, 300f, 700f, 2.5f, 0.5f, r);
            Normalize(s, 0.6f); return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip mFuoco, mBoom, mScintille, mFulmine, mGelo, mSangue, mOmbra, mOnda, mEssenza, mGesto, mScelta, mTraccia, mTocco, mPresa,
            mIcore, mSchiaffo, mRune, mSigillo, segretaApre, grimNodo, grimApre;
        public static AudioClip MagiaFuoco => One(ref mFuoco, "magiaFuoco", SoundGen.MagiaFuoco);
        public static AudioClip MagiaBoom => One(ref mBoom, "magiaBoom", SoundGen.MagiaBoom);
        public static AudioClip MagiaScintille => One(ref mScintille, "magiaScintille", SoundGen.MagiaScintille);
        public static AudioClip MagiaFulmine => One(ref mFulmine, "magiaFulmine", SoundGen.MagiaFulmine);
        public static AudioClip MagiaGelo => One(ref mGelo, "magiaGelo", SoundGen.MagiaGelo);
        public static AudioClip MagiaSangue => One(ref mSangue, "magiaSangue", SoundGen.MagiaSangue);
        public static AudioClip MagiaOmbra => One(ref mOmbra, "magiaOmbra", SoundGen.MagiaOmbra);
        public static AudioClip MagiaOnda => One(ref mOnda, "magiaOnda", SoundGen.MagiaOnda);
        public static AudioClip MagiaEssenza => One(ref mEssenza, "magiaEssenza", SoundGen.MagiaEssenza);
        public static AudioClip MagiaGesto => One(ref mGesto, "magiaGesto", SoundGen.MagiaGesto);
        public static AudioClip MagiaScelta => One(ref mScelta, "magiaScelta", SoundGen.MagiaScelta);
        public static AudioClip MagiaTraccia => One(ref mTraccia, "magiaTraccia", SoundGen.MagiaTraccia);
        public static AudioClip MagiaTocco => One(ref mTocco, "magiaTocco", SoundGen.MagiaTocco);
        public static AudioClip MagiaPresa => One(ref mPresa, "magiaPresa", SoundGen.MagiaPresa);
        public static AudioClip MagiaIcore => One(ref mIcore, "magiaIcore", SoundGen.MagiaIcore);
        public static AudioClip MagiaSchiaffo => One(ref mSchiaffo, "magiaSchiaffo", SoundGen.MagiaSchiaffo);
        public static AudioClip MagiaRune => One(ref mRune, "magiaRune", SoundGen.MagiaRune);
        public static AudioClip MagiaSigillo => One(ref mSigillo, "magiaSigillo", SoundGen.MagiaSigillo);
        public static AudioClip SegretaApre => One(ref segretaApre, "segretaApre", SoundGen.SegretaApre);
        public static AudioClip GrimorioNodo => One(ref grimNodo, "grimorioNodo", SoundGen.GrimorioNodo);
        public static AudioClip GrimorioApre => One(ref grimApre, "grimorioApre", SoundGen.GrimorioApre);
    }
}

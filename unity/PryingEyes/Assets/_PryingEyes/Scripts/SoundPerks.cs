using UnityEngine;

namespace PryingEyes
{
    /// <summary>Suoni dei perk, dei libri, dello Scrigno e della Consacrazione (fase 3), procedurali come tutti gli altri: nessun file audio.</summary>
    public static partial class SoundGen
    {
        /// <summary>Il Velo: un drappo che si chiude intorno, rumore filtrato che scende e due note basse. 1,4 s.</summary>
        public static float[] PerkVelo()
        {
            var r = new System.Random(71);
            var s = Buf(1.4f);
            var bp = new Biquad();
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                if ((i & 31) == 0) bp.SetBandPass(Mathf.Lerp(2400f, 380f, SmoothEdge(0f, 1.1f, t)), 0.9f, Rate);
                float env = SmoothEdge(0f, 0.22f, t) * (1f - SmoothEdge(0.45f, 1.4f, t));
                s[i] = bp.Run(N(r)) * env * 0.9f;
            }
            float[] ratios = { 1f, 2.01f, 3.02f }, amps = { 0.6f, 0.25f, 0.1f };
            Chime(s, 0.05f, 220f, 0.9f, ratios, amps, 0.5f);
            Chime(s, 0.05f, 329.6f, 0.9f, ratios, amps, 0.32f);
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>La Rinascita: due battiti di cuore e un accordo di voci che si apre, con un lampo acuto sopra. 3 s.</summary>
        public static float[] PerkRevive()
        {
            var r = new System.Random(72);
            var s = Buf(3.2f);
            Thump(s, 0f, 78f, 40f, 0.06f, 0.13f, 1.1f);
            Thump(s, 0.30f, 70f, 38f, 0.06f, 0.16f, 0.9f);
            float[] ratios = { 1f, 2f, 3.01f, 4.02f }, amps = { 1f, 0.45f, 0.22f, 0.1f };
            float[] notes = { 196f, 246.9f, 293.7f, 392f };
            for (int k = 0; k < notes.Length; k++) Chime(s, 0.75f + k * 0.06f, notes[k], 1.9f, ratios, amps, 0.5f);
            Chime(s, 1.15f, 1568f, 1.1f, new[] { 1f, 2.76f, 5.4f }, new[] { 1f, 0.4f, 0.15f }, 0.4f);
            Burst(s, 0.7f, 5200f, 1.2f, 0.25f, 0.18f, r);
            Normalize(s, 0.8f);
            return s;
        }

        /// <summary>Un libro che si legge (perk comprato): pagine che sfogliano veloci, un tonfo, un accordo che sale e scintille. 2,6 s.</summary>
        public static float[] BookOpen()
        {
            var r = new System.Random(73);
            var s = Buf(2.7f);
            float[] at = { 0f, 0.05f, 0.10f, 0.13f, 0.19f, 0.22f, 0.27f };
            foreach (float a in at) Burst(s, a, U(r, 3600f, 5200f), 1.0f, 0.014f, U(r, 0.35f, 0.7f), r);
            Thump(s, 0.30f, 118f, 56f, 0.07f, 0.16f, 1.0f);
            float[] ratios = { 1f, 2f, 3.01f }, amps = { 1f, 0.4f, 0.16f };
            float[] chord = { 261.6f, 329.6f, 392f, 523.25f, 659.25f };
            for (int k = 0; k < chord.Length; k++) Chime(s, 0.32f + k * 0.11f, chord[k], 1.5f, ratios, amps, 0.5f);
            for (int k = 0; k < 9; k++) Chime(s, 0.75f + k * 0.13f + U(r, 0f, 0.05f), U(r, 2000f, 4200f), 0.35f, new[] { 1f, 2.76f }, new[] { 1f, 0.3f }, U(r, 0.12f, 0.26f));
            Normalize(s, 0.75f);
            return s;
        }

        /// <summary>Gli altri due libri si richiudono: due tonfi di pelle e un clic di fermaglio. 0,9 s.</summary>
        public static float[] BookSeal()
        {
            var r = new System.Random(74);
            var s = Buf(0.9f);
            Thump(s, 0f, 150f, 70f, 0.04f, 0.09f, 1f);
            Burst(s, 0f, 700f, 0.9f, 0.02f, 0.8f, r);
            Thump(s, 0.13f, 135f, 62f, 0.04f, 0.09f, 0.85f);
            Burst(s, 0.13f, 650f, 0.9f, 0.02f, 0.7f, r);
            Burst(s, 0.26f, 2700f, 2.4f, 0.004f, 0.8f, r);
            Normalize(s, 0.6f);
            return s;
        }

        /// <summary>Non puoi (punti che mancano, posti pieni): un colpo sordo e una nota bassa. 0,5 s.</summary>
        public static float[] BookNo()
        {
            var s = Buf(0.5f);
            Thump(s, 0f, 210f, 130f, 0.05f, 0.12f, 0.9f);
            Chime(s, 0f, 138f, 0.18f, new[] { 1f, 1.5f }, new[] { 0.7f, 0.25f }, 0.5f);
            Normalize(s, 0.45f);
            return s;
        }

        /// <summary>Lo Scrigno si apre: scatto della serratura, il legno che geme sui cardini, il coperchio che batte e due note di vetro. 2,5 s.</summary>
        public static float[] ChestOpen()
        {
            var r = new System.Random(75);
            var s = Buf(2.5f);
            Burst(s, 0f, 1800f, 2.5f, 0.004f, 1.0f, r);
            Burst(s, 0.035f, 2900f, 2.5f, 0.004f, 0.8f, r);
            var bp = new Biquad();
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate - 0.18f;
                if (t < 0f || t > 1.1f) continue;
                if ((i & 15) == 0) bp.SetBandPass(Mathf.Lerp(170f, 430f, SmoothEdge(0f, 0.6f, t)) * (1f + 0.07f * Mathf.Sin(t * 31f)), 5f, Rate);
                float stick = Mathf.Repeat(t * 36f, 1f);                      // il gemito del legno: un dente di sega, attacco e scatto
                float env = SmoothEdge(0f, 0.12f, t) * (1f - SmoothEdge(0.85f, 1.1f, t)) * (0.35f + 0.65f * stick);
                s[i] += bp.Run(N(r)) * env * 1.6f;
            }
            Thump(s, 1.32f, 122f, 52f, 0.05f, 0.13f, 0.95f);
            Burst(s, 1.32f, 900f, 0.9f, 0.02f, 0.7f, r);
            float[] ratios = { 1f, 2.76f, 5.4f }, amps = { 1f, 0.38f, 0.14f };
            Chime(s, 1.45f, 1046.5f, 0.9f, ratios, amps, 0.5f);
            Chime(s, 1.58f, 1568f, 1.0f, ratios, amps, 0.42f);
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>Lo Scrigno si richiude: il legno geme appena mentre il coperchio cala, batte sul bordo a 0,62 s (la durata della caduta),
        /// rimbalza e il fermaglio scatta due volte. 1,2 s.</summary>
        public static float[] ChestClose()
        {
            var r = new System.Random(78);
            var s = Buf(1.2f);
            var bp = new Biquad();
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                if (t > 0.6f) break;
                if ((i & 15) == 0) bp.SetBandPass(Mathf.Lerp(430f, 240f, t / 0.6f), 5f, Rate);
                float env = SmoothEdge(0f, 0.08f, t) * (1f - SmoothEdge(0.35f, 0.6f, t)) * 0.45f;
                s[i] += bp.Run(N(r)) * env * 1.2f;
            }
            Thump(s, 0.62f, 125f, 48f, 0.05f, 0.12f, 1.0f);
            Burst(s, 0.62f, 950f, 0.9f, 0.02f, 0.75f, r);
            Thump(s, 0.763f, 92f, 55f, 0.04f, 0.07f, 0.35f);
            Burst(s, 0.80f, 1900f, 2.5f, 0.004f, 0.8f, r);
            Burst(s, 0.835f, 2800f, 2.5f, 0.004f, 0.6f, r);
            Normalize(s, 0.65f);
            return s;
        }

        /// <summary>L'arma estratta dallo Scrigno: arpeggio che sale e scintille. 1,8 s.</summary>
        public static float[] ChestReveal()
        {
            var r = new System.Random(76);
            var s = Buf(1.9f);
            float[] ratios = { 1f, 2f, 3.01f }, amps = { 1f, 0.4f, 0.15f };
            float[] notes = { 523.25f, 659.25f, 784f, 1046.5f, 1318.5f };
            for (int k = 0; k < notes.Length; k++) Chime(s, k * 0.075f, notes[k], 1.0f, ratios, amps, 0.5f);
            for (int k = 0; k < 7; k++) Chime(s, 0.2f + k * 0.1f + U(r, 0f, 0.04f), U(r, 2400f, 4600f), 0.3f, new[] { 1f, 2.76f }, new[] { 1f, 0.3f }, U(r, 0.12f, 0.22f));
            Normalize(s, 0.65f);
            return s;
        }

        /// <summary>La Consacrazione: tre colpi di martello sul metallo che suona, poi un coro che sale con uno sfregamento d'aria. 4 s.</summary>
        public static float[] Consecrate()
        {
            var r = new System.Random(77);
            var s = Buf(4.2f);
            float[] strikes = { 0f, 0.46f, 0.92f };
            for (int k = 0; k < strikes.Length; k++)
            {
                float a = strikes[k];
                Thump(s, a, 210f, 70f, 0.03f, 0.16f, 1.0f);
                Burst(s, a, 3300f, 1.6f, 0.012f, 0.9f, r);
                Chime(s, a, 660f * (1f + 0.06f * k), 1.0f, new[] { 1f, 2.4f, 3.9f, 5.6f }, new[] { 1f, 0.6f, 0.35f, 0.18f }, 0.7f);
            }
            var bp = new Biquad();
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate - 1.2f;
                if (t < 0f || t > 1.4f) continue;
                if ((i & 31) == 0) bp.SetBandPass(Mathf.Lerp(300f, 3200f, SmoothEdge(0f, 1.3f, t)), 1.2f, Rate);
                float env = SmoothEdge(0f, 0.5f, t) * (1f - SmoothEdge(1.0f, 1.4f, t));
                s[i] += bp.Run(N(r)) * env * 0.5f;
            }
            float[] ratios = { 1f, 2f, 3.01f, 4.02f }, amps = { 1f, 0.5f, 0.24f, 0.1f };
            float[] chord = { 261.6f, 392f, 523.25f, 659.25f, 784f };
            for (int k = 0; k < chord.Length; k++) Chime(s, 1.7f + k * 0.05f, chord[k], 2.3f, ratios, amps, 0.48f);
            Chime(s, 1.9f, 2093f, 1.5f, new[] { 1f, 2.76f, 5.4f }, new[] { 1f, 0.4f, 0.15f }, 0.35f);
            Normalize(s, 0.85f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip perkVelo, perkRevive, bookOpen, bookSeal, bookNo, chestOpen, chestClose, chestReveal, consecrate;

        public static AudioClip PerkVelo => One(ref perkVelo, "perkVelo", SoundGen.PerkVelo);
        public static AudioClip PerkRevive => One(ref perkRevive, "perkRinascita", SoundGen.PerkRevive);
        public static AudioClip BookOpen => One(ref bookOpen, "libroAperto", SoundGen.BookOpen);
        public static AudioClip BookSeal => One(ref bookSeal, "libroChiuso", SoundGen.BookSeal);
        public static AudioClip BookNo => One(ref bookNo, "libroNo", SoundGen.BookNo);
        public static AudioClip ChestOpen => One(ref chestOpen, "scrignoApre", SoundGen.ChestOpen);
        public static AudioClip ChestClose => One(ref chestClose, "scrignoChiude", SoundGen.ChestClose);
        public static AudioClip ChestReveal => One(ref chestReveal, "scrignoArma", SoundGen.ChestReveal);
        public static AudioClip Consecrate => One(ref consecrate, "consacrazione", SoundGen.Consecrate);

        /// <summary>Prepara i suoni dei perk all'avvio (come WarmKnife): il primo acquisto non deve far scattare il gioco.</summary>
        public static void WarmPerks()
        {
            _ = PerkVelo; _ = PerkRevive; _ = BookOpen; _ = BookSeal; _ = BookNo; _ = ChestOpen; _ = ChestClose; _ = ChestReveal; _ = Consecrate;
        }
    }
}

using System;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Suoni del coltello e dei potenziamenti (fase 2), tutti procedurali come gli altri: nessun file audio.</summary>
    public static partial class SoundGen
    {
        /// <summary>Il fendente nell'aria: rumore filtrato con la banda che sale, picco a un terzo. 0,3 s.</summary>
        public static float[] KnifeSwish()
        {
            var r = new System.Random(61);
            var s = Buf(0.32f);
            var bp = new Biquad();
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate, k = t / 0.32f;
                if ((i & 31) == 0) bp.SetBandPass(Mathf.Lerp(520f, 3400f, k * k), 1.3f, Rate);
                float env = SmoothEdge(0f, 0.07f, t) * (1f - SmoothEdge(0.13f, 0.32f, t));
                s[i] = bp.Run(N(r)) * env;
            }
            Normalize(s, 0.55f);
            return s;
        }

        /// <summary>La lama che entra: tonfo umido, scoppio di rumore medio, un filo metallico acuto. 0,5 s.</summary>
        public static float[] KnifeStab()
        {
            var r = new System.Random(62);
            var s = Buf(0.55f);
            Thump(s, 0f, 170f, 52f, 0.05f, 0.075f, 0.95f);
            Burst(s, 0f, 820f, 0.9f, 0.014f, 1.0f, r);
            Burst(s, 0.012f, 3100f, 1.4f, 0.006f, 0.38f, r);
            Chime(s, 0.004f, 2350f, 0.10f, new[] { 1f, 2.71f }, new[] { 0.26f, 0.10f }, 0.2f);
            var lp = Biquad.LowPass(5200f, 0.7f);
            for (int i = 0; i < s.Length; i++) s[i] = lp.Run(s[i]);
            Normalize(s, 0.85f);
            return s;
        }

        /// <summary>Il drop compare: due note di campanello, la seconda una quinta sopra. 1,2 s.</summary>
        public static float[] DropAppear()
        {
            var s = Buf(1.3f);
            float[] ratios = { 1f, 2.76f, 5.4f }, amps = { 1f, 0.4f, 0.16f };
            Chime(s, 0.00f, 880f, 0.42f, ratios, amps, 0.8f);
            Chime(s, 0.16f, 1318f, 0.55f, ratios, amps, 0.7f);
            Normalize(s, 0.5f);
            return s;
        }

        /// <summary>Reliquiario (munizioni): due scatti di metallo e un arpeggio che sale. 1,3 s.</summary>
        public static float[] PickAmmo()
        {
            var r = new System.Random(63);
            var s = Buf(1.4f);
            Burst(s, 0f, 2200f, 1.6f, 0.004f, 0.9f, r);
            Burst(s, 0.075f, 1700f, 1.6f, 0.004f, 0.9f, r);
            Thump(s, 0.075f, 190f, 90f, 0.03f, 0.05f, 0.5f);
            float[] ratios = { 1f, 2.76f, 5.4f }, amps = { 1f, 0.35f, 0.14f };
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            for (int k = 0; k < notes.Length; k++) Chime(s, 0.18f + k * 0.085f, notes[k], 0.45f, ratios, amps, 0.55f);
            Normalize(s, 0.7f);
            return s;
        }

        /// <summary>Sguardo (uccisioni istantanee): un occhio che si apre: respiro che sale, tonfo profondo, due note dissonanti. 1,8 s.</summary>
        public static float[] PickEye()
        {
            var r = new System.Random(64);
            var s = Buf(1.9f);
            var bp = new Biquad();
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                if ((i & 31) == 0) bp.SetBandPass(Mathf.Lerp(300f, 2200f, SmoothEdge(0f, 0.8f, t)), 1.1f, Rate);
                float env = SmoothEdge(0f, 0.7f, t) * (1f - SmoothEdge(0.8f, 1.6f, t));
                s[i] = bp.Run(N(r)) * env * 0.9f;
            }
            Thump(s, 0.62f, 90f, 38f, 0.2f, 0.28f, 1.1f);
            float[] ratios = { 1f, 1.5f, 2.01f, 3.02f }, amps = { 1f, 0.5f, 0.3f, 0.12f };
            Chime(s, 0.64f, 392f, 0.8f, ratios, amps, 0.5f);
            Chime(s, 0.64f, 415.3f, 0.8f, ratios, amps, 0.4f);        // un semitono sopra: non si risolve
            Normalize(s, 0.75f);
            return s;
        }

        /// <summary>Offerta (punti doppi): monete d'oro in una ciotola e un campanello acuto. 1,2 s.</summary>
        public static float[] PickCoins()
        {
            var r = new System.Random(65);
            var s = Buf(1.3f);
            float[] ratios = { 1f, 2.32f, 4.1f }, amps = { 1f, 0.5f, 0.22f };
            for (int k = 0; k < 7; k++)
            {
                float at = k * 0.055f + U(r, 0f, 0.012f);
                Chime(s, at, U(r, 2000f, 3300f), 0.11f, ratios, amps, U(r, 0.35f, 0.6f));
                Burst(s, at, 5200f, 2f, 0.002f, 0.25f, r);
            }
            Chime(s, 0.42f, 1568f, 0.7f, new[] { 1f, 2.76f, 5.4f }, new[] { 1f, 0.4f, 0.15f }, 0.6f);
            Normalize(s, 0.65f);
            return s;
        }

        /// <summary>La Campana: un rintocco enorme e basso, partiali inarmonici che si spengono lenti e un colpo sordo. 7 s.</summary>
        public static float[] NukeToll()
        {
            var r = new System.Random(66);
            var s = Buf(7.5f);
            Thump(s, 0f, 120f, 42f, 0.25f, 0.5f, 1.2f);
            Burst(s, 0f, 600f, 0.7f, 0.03f, 0.8f, r);
            Chime(s, 0f, 98f, 3.2f, new[] { 1f, 2f, 2.4f, 3f, 4.07f, 5.43f, 6.8f }, new[] { 1f, 0.8f, 0.6f, 0.5f, 0.32f, 0.2f, 0.1f }, 0.7f);
            var lp = Biquad.LowPass(3600f, 0.7f);
            for (int i = 0; i < s.Length; i++) s[i] = lp.Run(s[i]);
            Normalize(s, 0.9f);
            return s;
        }

        /// <summary>Tic di legno per gli ultimi secondi di un potenziamento. 0,12 s.</summary>
        public static float[] DropTick()
        {
            var r = new System.Random(67);
            var s = Buf(0.14f);
            Burst(s, 0f, 1500f, 2.2f, 0.006f, 1f, r);
            Thump(s, 0f, 420f, 300f, 0.02f, 0.02f, 0.4f);
            Normalize(s, 0.5f);
            return s;
        }

        /// <summary>Il potenziamento finisce: una nota che scende e si spegne. 0,8 s.</summary>
        public static float[] DropEnd()
        {
            var s = Buf(0.9f);
            Thump(s, 0f, 520f, 190f, 0.4f, 0.22f, 0.7f);
            Chime(s, 0f, 330f, 0.3f, new[] { 1f, 2.01f }, new[] { 0.7f, 0.2f }, 0.5f);
            Normalize(s, 0.5f);
            return s;
        }

        /// <summary>Processione dei Lumi: un respiro nel tessuto e quattro rintocchi ascendenti. 3,2 s.</summary>
        public static float[] LumiProcession()
        {
            var breath = VigilBreath();
            var s = Buf(3.2f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / Rate;
                s[i] = breath[i] * (1f - SmoothEdge(2.6f, 3.2f, t));
            }
            for (int i = 0; i < 4; i++)
                Chime(s, 0.15f + i * 0.48f, 293.66f * Mathf.Pow(2f, i / 12f), 0.9f,
                    new[] { 1f, 2.01f, 2.76f }, new[] { 0.24f, 0.08f, 0.03f }, 0.5f);
            Normalize(s, 0.55f);
            return s;
        }
    }

    public static partial class SoundBank
    {
        static AudioClip knifeSwish, knifeStab, dropAppear, pickAmmo, pickEye, pickCoins, nukeToll, dropTick, dropEnd, mothSwarm;

        public static AudioClip KnifeSwish => One(ref knifeSwish, "coltelloAria", SoundGen.KnifeSwish);
        public static AudioClip KnifeStab => One(ref knifeStab, "coltelloColpo", SoundGen.KnifeStab);
        public static AudioClip DropAppear => One(ref dropAppear, "dropCompare", SoundGen.DropAppear);
        public static AudioClip PickAmmo => One(ref pickAmmo, "dropMunizioni", SoundGen.PickAmmo);
        public static AudioClip PickEye => One(ref pickEye, "dropSguardo", SoundGen.PickEye);
        public static AudioClip PickCoins => One(ref pickCoins, "dropOfferta", SoundGen.PickCoins);
        public static AudioClip NukeToll => One(ref nukeToll, "dropCampana", SoundGen.NukeToll);
        public static AudioClip DropTick => One(ref dropTick, "dropTic", SoundGen.DropTick);
        public static AudioClip DropEnd => One(ref dropEnd, "dropFine", SoundGen.DropEnd);
        public static AudioClip LumiProcession => One(ref mothSwarm, "processioneLumi", SoundGen.LumiProcession);

        /// <summary>Prepara i suoni del coltello e dei potenziamenti all'avvio della partita: la Campana e' lunga da calcolare e il primo drop non deve far scattare il gioco.</summary>
        public static void WarmKnife()
        {
            _ = KnifeSwish; _ = KnifeStab; _ = DropAppear; _ = PickAmmo; _ = PickEye; _ = PickCoins;
            _ = NukeToll; _ = DropTick; _ = DropEnd; _ = LumiProcession;
        }
    }
}

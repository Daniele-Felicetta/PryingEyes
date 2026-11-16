using UnityEngine;

namespace PryingEyes
{
    /// <summary>Una posa della mano in angoli anatomici (gradi). Dita (0 indice, 1 medio, 2 anulare, 3 mignolo): apertura alla nocca
    /// (positivo verso il pollice, misurata rispetto all'asse del palmo), flessione della nocca metacarpo-falangea (MCP), della
    /// interfalangea prossimale (PIP) e distale (DIP); positivo = verso il palmo. Pollice: scarti dalla posa neutra del modello
    /// (abduzione palmare, opposizione verso il mignolo, flessione di MCP e IP). HandRig limita tutto ai campi articolari veri.</summary>
    [System.Serializable]
    public struct HandPose
    {
        public float s0, s1, s2, s3;                       // apertura di ogni dito
        public float m0, m1, m2, m3;                       // MCP
        public float p0, p1, p2, p3;                       // PIP
        public float d0, d1, d2, d3;                       // DIP
        public float tAbd, tOpp, tMcp, tIp;                // pollice

        public float Spread(int f) => f == 0 ? s0 : f == 1 ? s1 : f == 2 ? s2 : s3;
        public float Mcp(int f) => f == 0 ? m0 : f == 1 ? m1 : f == 2 ? m2 : m3;
        public float Pip(int f) => f == 0 ? p0 : f == 1 ? p1 : f == 2 ? p2 : p3;
        public float Dip(int f) => f == 0 ? d0 : f == 1 ? d1 : f == 2 ? d2 : d3;

        public void SetFinger(int f, float spread, float mcp, float pip, float dip)
        {
            switch (f)
            {
                case 0: s0 = spread; m0 = mcp; p0 = pip; d0 = dip; break;
                case 1: s1 = spread; m1 = mcp; p1 = pip; d1 = dip; break;
                case 2: s2 = spread; m2 = mcp; p2 = pip; d2 = dip; break;
                default: s3 = spread; m3 = mcp; p3 = pip; d3 = dip; break;
            }
        }

        /// <summary>Le quattro dita uguali (apertura a ventaglio naturale) e la DIP accoppiata alla PIP come nella mano vera (circa 2/3).</summary>
        public static HandPose Fingers(float mcp, float pip, float fan = 1f, float dipK = 0.67f)
        {
            var h = new HandPose();
            for (int f = 0; f < 4; f++) h.SetFinger(f, Fan[f] * fan, mcp + f * 2.5f, pip + f * 2f, (pip + f * 2f) * dipK);
            return h;
        }

        /// <summary>Apertura a ventaglio di una mano rilassata (gradi, positivo verso il pollice).</summary>
        public static readonly float[] Fan = { 7f, 1.5f, -5f, -12f };

        public HandPose Thumb(float abd, float opp, float mcp, float ip) { tAbd = abd; tOpp = opp; tMcp = mcp; tIp = ip; return this; }
        public HandPose Finger(int f, float spread, float mcp, float pip, float dip) { SetFinger(f, spread, mcp, pip, dip); return this; }

        public static HandPose Lerp(in HandPose a, in HandPose b, float t)
        {
            var r = new HandPose();
            r.s0 = Mathf.LerpUnclamped(a.s0, b.s0, t); r.s1 = Mathf.LerpUnclamped(a.s1, b.s1, t); r.s2 = Mathf.LerpUnclamped(a.s2, b.s2, t); r.s3 = Mathf.LerpUnclamped(a.s3, b.s3, t);
            r.m0 = Mathf.LerpUnclamped(a.m0, b.m0, t); r.m1 = Mathf.LerpUnclamped(a.m1, b.m1, t); r.m2 = Mathf.LerpUnclamped(a.m2, b.m2, t); r.m3 = Mathf.LerpUnclamped(a.m3, b.m3, t);
            r.p0 = Mathf.LerpUnclamped(a.p0, b.p0, t); r.p1 = Mathf.LerpUnclamped(a.p1, b.p1, t); r.p2 = Mathf.LerpUnclamped(a.p2, b.p2, t); r.p3 = Mathf.LerpUnclamped(a.p3, b.p3, t);
            r.d0 = Mathf.LerpUnclamped(a.d0, b.d0, t); r.d1 = Mathf.LerpUnclamped(a.d1, b.d1, t); r.d2 = Mathf.LerpUnclamped(a.d2, b.d2, t); r.d3 = Mathf.LerpUnclamped(a.d3, b.d3, t);
            r.tAbd = Mathf.LerpUnclamped(a.tAbd, b.tAbd, t); r.tOpp = Mathf.LerpUnclamped(a.tOpp, b.tOpp, t);
            r.tMcp = Mathf.LerpUnclamped(a.tMcp, b.tMcp, t); r.tIp = Mathf.LerpUnclamped(a.tIp, b.tIp, t);
            return r;
        }

        /// <summary>Distanza massima fra due pose (gradi): per sapere se una transizione e' finita.</summary>
        public static float MaxDelta(in HandPose a, in HandPose b)
        {
            float m = 0f;
            for (int f = 0; f < 4; f++)
            {
                m = Mathf.Max(m, Mathf.Abs(a.Spread(f) - b.Spread(f)), Mathf.Abs(a.Mcp(f) - b.Mcp(f)));
                m = Mathf.Max(m, Mathf.Abs(a.Pip(f) - b.Pip(f)), Mathf.Abs(a.Dip(f) - b.Dip(f)));
            }
            m = Mathf.Max(m, Mathf.Abs(a.tAbd - b.tAbd), Mathf.Abs(a.tOpp - b.tOpp));
            return Mathf.Max(m, Mathf.Abs(a.tMcp - b.tMcp), Mathf.Abs(a.tIp - b.tIp));
        }
    }

    /// <summary>Le pose con un nome: quelle dei gesti delle magie, dei sigilli sull'arma e dei lanci. Gli angoli rispettano la mano vera
    /// (la DIP non si piega senza la PIP, il mignolo si chiude piu' degli altri, il pollice si oppone ruotando).</summary>
    public static class HandPoses
    {
        /// <summary>Mano abbandonata: tutte le articolazioni appena flesse, ventaglio naturale.</summary>
        public static readonly HandPose Relaxed = HandPose.Fingers(16f, 24f).Thumb(4f, 6f, 8f, 10f);
        /// <summary>Aperta e tesa: dita distese e larghe (si spinge, si para).</summary>
        public static readonly HandPose Open = HandPose.Fingers(2f, 3f, 1.9f, 0.4f).Thumb(-14f, -10f, 0f, 0f);
        /// <summary>Piatta: dita distese e unite (si poggia il palmo).</summary>
        public static readonly HandPose Flat = HandPose.Fingers(0f, 2f, 0.15f, 0.3f).Thumb(-20f, -6f, 0f, 2f);
        /// <summary>Pugno chiuso, pollice sopra medio e indice.</summary>
        public static readonly HandPose Fist = HandPose.Fingers(84f, 100f, 0.1f, 0.62f).Thumb(14f, 52f, 34f, 38f);
        /// <summary>Artiglio: nocche distese, falangi piegate, dita larghe (la tensione del fulmine).</summary>
        public static readonly HandPose Claw = new HandPose()
            .Finger(0, 15f, 18f, 72f, 55f).Finger(1, 3f, 20f, 76f, 58f).Finger(2, -8f, 22f, 78f, 60f).Finger(3, -20f, 26f, 80f, 60f)
            .Thumb(-6f, 18f, 30f, 42f);
        /// <summary>Coppa: la mano raccoglie (la fiamma nel palmo, l'icore).</summary>
        public static readonly HandPose Cup = HandPose.Fingers(36f, 44f, 0.4f, 0.6f).Thumb(10f, 28f, 16f, 18f);
        /// <summary>Indice teso, le altre chiuse (si traccia un sigillo).</summary>
        public static readonly HandPose Point = new HandPose()
            .Finger(0, 4f, 2f, 4f, 2f).Finger(1, 0f, 82f, 100f, 62f).Finger(2, -2f, 86f, 100f, 62f).Finger(3, -4f, 90f, 98f, 60f)
            .Thumb(12f, 44f, 30f, 30f);
        /// <summary>Le corna: indice e mignolo tesi, medio e anulare trattenuti dal pollice (il segno del maleficio).</summary>
        public static readonly HandPose Horns = new HandPose()
            .Finger(0, 10f, 4f, 6f, 3f).Finger(1, 0f, 78f, 102f, 64f).Finger(2, -3f, 80f, 102f, 64f).Finger(3, -16f, 4f, 6f, 3f)
            .Thumb(18f, 58f, 36f, 40f);
        /// <summary>Pronto allo schiocco: polpastrello del medio premuto sul pollice, indice morbido.</summary>
        public static readonly HandPose SnapReady = new HandPose()
            .Finger(0, 6f, 26f, 30f, 18f).Finger(1, 0f, 40f, 50f, 22f).Finger(2, -6f, 64f, 86f, 52f).Finger(3, -12f, 70f, 88f, 54f)
            .Thumb(30f, 34f, 18f, 4f);
        /// <summary>Lo schiocco: il medio e' sceso sul palmo, l'indice si e' disteso.</summary>
        public static readonly HandPose Snapped = new HandPose()
            .Finger(0, 8f, 10f, 12f, 6f).Finger(1, 1f, 86f, 98f, 60f).Finger(2, -5f, 70f, 90f, 56f).Finger(3, -12f, 74f, 90f, 56f)
            .Thumb(16f, 18f, 4f, 0f);
        /// <summary>Pizzico: indice e pollice si toccano (si prende un bossolo, una scintilla).</summary>
        public static readonly HandPose Pinch = new HandPose()
            .Finger(0, 2f, 38f, 46f, 22f).Finger(1, 0f, 34f, 48f, 30f).Finger(2, -5f, 40f, 56f, 36f).Finger(3, -11f, 46f, 60f, 40f)
            .Thumb(26f, 38f, 16f, 10f);
        /// <summary>Presa su un cilindro di circa 3 cm (la canna, il caricatore): le dita lo avvolgono.</summary>
        public static readonly HandPose Grip = HandPose.Fingers(56f, 72f, 0.2f, 0.55f).Thumb(22f, 30f, 22f, 16f);
        /// <summary>Benedizione rovesciata: indice e medio tesi e uniti, gli altri chiusi.</summary>
        public static readonly HandPose Blessing = new HandPose()
            .Finger(0, 1f, 2f, 4f, 2f).Finger(1, 1f, 2f, 4f, 2f).Finger(2, -3f, 84f, 100f, 62f).Finger(3, -6f, 88f, 98f, 60f)
            .Thumb(16f, 50f, 32f, 34f);
        /// <summary>Spinta: palmo in avanti, dita aperte all'indietro (iperestese alla nocca).</summary>
        public static readonly HandPose Thrust = HandPose.Fingers(-18f, 0f, 1.5f, 0.2f).Thumb(-18f, -12f, -4f, -6f);
    }
}

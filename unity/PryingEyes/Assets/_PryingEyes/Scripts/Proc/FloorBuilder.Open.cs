using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Le stanze all'aperto (CeilKind.Open: la terrazza del Guardaroba). Niente soffitto; i muri sul vuoto sono un parapetto con i merli,
    /// abbastanza alto da non poterlo scavalcare (Mantle arriva a 1,35 m), quelli verso le altre stanze salgono fin sopra il loro tetto.</summary>
    public static partial class FloorBuilder
    {
        /// <summary>Altezza del parapetto: piu' del massimo che la presa del giocatore scavalca, cosi' dalla terrazza non si cade nel vuoto.</summary>
        public const float Parapet = 1.55f;
        const float Eps = 0.0015f;

        /// <summary>Il profilo alto di una faccia di muro di una stanza all'aperto: parapetto dove si da' sul vuoto, tetto del vicino (piu' un
        /// risalto) dove c'e' un'altra stanza, tre metri verso un'altra terrazza. Costante a tratti: ai bordi c'e' un gradino di tre millimetri
        /// (WallFace scarta le fette sotto i due millimetri e campiona gli estremi, quindi servono due punti di rottura vicini).</summary>
        static void OpenTop(Plan plan, Room r, Side s, float faceStart, out Func<float, float> top, out float[] breaks)
        {
            var ends = new List<float>(); var hs = new List<float>();
            foreach (var seg in plan.Segments(r, s))
            {
                float h = Parapet;
                if (seg.other >= 0)
                {
                    var nb = plan.rooms[seg.other];
                    h = nb.Open ? 3f : Mathf.Max(nb.wallH, nb.ridgeH) + 0.3f;
                }
                ends.Add(M(seg.a1) - faceStart); hs.Add(h);
            }
            var br = new List<float>();
            for (int i = 0; i + 1 < ends.Count; i++)
            {
                if (Mathf.Abs(hs[i] - hs[i + 1]) < 0.01f) continue;
                br.Add(ends[i] - Eps); br.Add(ends[i] + Eps);
            }
            breaks = br.ToArray();
            top = x =>
            {
                for (int i = 0; i < ends.Count; i++)
                    if (x <= ends[i] - Eps) return hs[i];
                return hs[hs.Count - 1];
            };
        }

        /// <summary>Il coronamento di un tratto di parapetto sul vuoto: un cordolo di pietra che copre lo spessore del muro e i merli (denti di pietra
        /// larghi 55 cm, alti 45, uno ogni 1,3 m) sopra. Il Guscio ha il collider di mesh: i merli fermano i colpi come il muro.</summary>
        static void OpenCrown(MeshBuilder mb, Style st, Room r, bool alongX, float line, float a, float b)
        {
            Material stone = st.trim != null ? st.trim : st.outer;
            Vector3 P(float s, float y) => alongX ? new Vector3(s, y, line) : new Vector3(line, y, s);
            // il cordolo: largo quanto il muro piu' un risalto
            mb.Beam(stone, P(a, Parapet + 0.05f), P(b, Parapet + 0.05f), TH * 2f + 0.14f, 0.10f, Vector3.up);
            int n = Mathf.Max(1, Mathf.FloorToInt((b - a) / 1.3f));
            float step = (b - a) / n;
            for (int i = 0; i < n; i++)
            {
                float c = a + step * (i + 0.5f);
                Vector3 size = alongX ? new Vector3(0.55f, 0.45f, TH * 2f + 0.06f) : new Vector3(TH * 2f + 0.06f, 0.45f, 0.55f);
                mb.Box(stone, P(c, Parapet + 0.10f + 0.225f), size);
            }
        }
    }
}

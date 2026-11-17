using PryingEyes.Cattedrale;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Lo zaino nell'HUD, a destra sotto la minimappa: le risorse che porti (nelle catacombe) o quelle del magazzino (nella
    /// cattedrale), una riga per risorsa con il suo colore; sopra, l'ultima raccolta ("+ 2 Ossa") che sfuma. Nella cattedrale il numero
    /// in basso a sinistra sono gli oboli invece dei punti.</summary>
    public partial class HUD
    {
        const int SackRows = 11;
        const float SackTop = -236f, SackRowH = 17f;
        Text sackTitle, sackFeed;
        Text[] sackRows;
        Image[] sackDots;
        float sackFeedT, sackRefreshT, sackPopT;
        Text scoreLabelText;
        bool sackBuilt;

        void BuildRisorseUI()
        {
            sackTitle = MakeText("ZainoTitolo", new Vector2(1, 1), new Vector2(-28, SackTop), new Vector2(240, 18), 12, TextAnchor.UpperRight, "", Dim, FontStyle.Bold);
            sackFeed = MakeText("ZainoRaccolta", new Vector2(1, 1), new Vector2(-28, SackTop + 20f), new Vector2(420, 20), 15, TextAnchor.UpperRight, "", Ink, FontStyle.Bold);
            sackRows = new Text[SackRows];
            sackDots = new Image[SackRows];
            for (int i = 0; i < SackRows; i++)
            {
                sackRows[i] = MakeText("ZainoRiga" + i, new Vector2(1, 1), new Vector2(-44, SackTop - 20f - i * SackRowH), new Vector2(260, 18), 14, TextAnchor.UpperRight, "", Ink, FontStyle.Normal);
                var dot = MakeImage("ZainoColore" + i, Color.white);
                var rt = dot.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-28, SackTop - 23f - i * SackRowH); rt.sizeDelta = new Vector2(10, 10);
                sackDots[i] = dot;
                sackRows[i].gameObject.SetActive(false); dot.gameObject.SetActive(false);
            }
            Fabbrica.Cambiata += OnSackChanged;
            sackBuilt = true;
        }

        void OnDestroy() { Fabbrica.Cambiata -= OnSackChanged; }
        void OnSackChanged() { sackRefreshT = 0f; }

        /// <summary>L'ultima raccolta, sopra lo zaino ("+ 2 Ossa", "Le tue spoglie: ...").</summary>
        public void RisorsaFeed(string text, Color c)
        {
            if (sackFeed == null) return;
            sackFeed.text = text;
            sackFeed.color = Color.Lerp(c, Ink, 0.25f);
            sackFeedT = 2.6f;
            sackPopT = 0.35f;
        }

        /// <summary>Nella cattedrale il numero in basso a sinistra sono gli oboli.</summary>
        public void SetScoreLabel(string s)
        {
            if (scoreLabelText == null) { var label = transform.Find("ScoreLabel"); if (label != null) scoreLabelText = label.GetComponent<Text>(); }
            if (scoreLabelText != null) scoreLabelText.text = s;
        }

        void UpdateRisorseUI(float dt)
        {
            if (!sackBuilt) return;
            if (sackFeedT > 0f)
            {
                sackFeedT -= dt;
                var c = sackFeed.color; c.a = Mathf.Clamp01(sackFeedT / 0.6f); sackFeed.color = c;
                if (sackFeedT <= 0f) sackFeed.text = "";
            }
            if (sackPopT > 0f) { sackPopT -= dt; float k = 1f + 0.18f * Mathf.Clamp01(sackPopT / 0.35f); sackTitle.rectTransform.localScale = new Vector3(k, k, 1f); }
            sackRefreshT -= dt;
            if (sackRefreshT > 0f) return;
            sackRefreshT = 0.5f;
            var fd = Proc.FloorDirector.I;
            bool on = Catacombe.Attive && GameManager.proc && fd != null && (fd.InHub || fd.floor >= 1);
            if (!on) { sackTitle.text = ""; for (int i = 0; i < SackRows; i++) { sackRows[i].gameObject.SetActive(false); sackDots[i].gameObject.SetActive(false); } return; }
            bool hub = fd.InHub;
            var carried = Fabbrica.Spedizione.Active && !Fabbrica.Diretta ? Fabbrica.Spedizione.Carried : null;
            sackTitle.text = hub ? "MAGAZZINO DELLA CATTEDRALE" : carried != null ? "ZAINO" : "MAGAZZINO";
            int row = 0;
            foreach (var r in Risorse.Tutte)
            {
                if (row >= SackRows) break;
                int n = hub || carried == null ? Fabbrica.Banca.Get(r.id) : carried.Get(r.id);
                if (r.grado == Grado.Moneta && !hub) n = carried != null ? carried.Get(r.id) : 0;
                if (n <= 0) continue;
                sackRows[row].text = r.nome + "  " + n;
                sackRows[row].color = r.grado >= Grado.Rara ? Color.Lerp(new Color(r.r, r.g, r.b), Ink, 0.35f) : Ink;
                sackDots[row].color = new Color(r.r, r.g, r.b, 0.95f);
                sackRows[row].gameObject.SetActive(true); sackDots[row].gameObject.SetActive(true);
                row++;
            }
            if (row == 0 && !hub)
            {
                sackRows[0].text = "vuoto";
                sackRows[0].color = Dim;
                sackRows[0].gameObject.SetActive(true); sackDots[0].gameObject.SetActive(false);
                row = 1;
            }
            for (int i = row; i < SackRows; i++) { sackRows[i].gameObject.SetActive(false); sackDots[i].gameObject.SetActive(false); }
        }
    }
}

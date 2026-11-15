using UnityEngine;
using UnityEngine.UI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Tabellone (tenendo premuto Tab): la partita in cifre, al centro dello schermo su un pannello scuro dal filo d'oro.
    /// Ondata e piano, punti, uccisioni e quante alla testa, precisione, prove superate, morti, tempo, equipaggiamento e, in
    /// cooperativa, quanti siete. Si aggiorna mentre lo guardi.</summary>
    public partial class HUD
    {
        Image scoreBack, scoreFrame;
        Text scoreTitle, scoreLeft, scoreRight;
        float scoreA;

        void BuildScoreUI()
        {
            scoreFrame = MakeImage("TabelloneFilo", new Color(0.96f, 0.78f, 0.30f, 0f));
            PlaceCenter(scoreFrame.rectTransform, new Vector2(0, 10), new Vector2(624, 334));
            scoreBack = MakeImage("Tabellone", new Color(0.03f, 0.025f, 0.02f, 0f));
            PlaceCenter(scoreBack.rectTransform, new Vector2(0, 10), new Vector2(620, 330));
            scoreTitle = MakeText("TabelloneTitolo", new Vector2(0.5f, 0.5f), new Vector2(0, 140), new Vector2(600, 40), 28, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            scoreTitle.font = Serif();
            scoreLeft = MakeText("TabelloneSinistra", new Vector2(0.5f, 0.5f), new Vector2(-140, -10), new Vector2(260, 250), 16, TextAnchor.UpperRight, "", Dim, FontStyle.Normal);
            scoreRight = MakeText("TabelloneDestra", new Vector2(0.5f, 0.5f), new Vector2(140, -10), new Vector2(260, 250), 16, TextAnchor.UpperLeft, "", Ink, FontStyle.Bold);
            scoreLeft.lineSpacing = 1.35f; scoreRight.lineSpacing = 1.35f;
            SetScoreAlpha(0f);
        }

        static void PlaceCenter(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        void SetScoreAlpha(float a)
        {
            if (scoreBack == null) return;
            scoreFrame.color = new Color(0.96f, 0.78f, 0.30f, 0.55f * a);
            scoreBack.color = new Color(0.03f, 0.025f, 0.02f, 0.88f * a);
            var c = Ink; c.a = a; scoreTitle.color = c; scoreRight.color = c;
            var d = Dim; d.a = a * Dim.a; scoreLeft.color = d;
        }

        /// <summary>Il tabellone e' aperto (per le prove).</summary>
        public bool ScoreboardOpen => scoreA > 0.5f;
        /// <summary>Le prove lo aprono senza tastiera.</summary>
        public static bool forceScoreboard;

        void UpdateScoreUI(float dt)
        {
            if (scoreBack == null) return;
            bool want = forceScoreboard || (!GameMenu.Blocked && Input.GetKey(KeyCode.Tab));
            scoreA = Mathf.MoveTowards(scoreA, want ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            SetScoreAlpha(scoreA);
            if (scoreA <= 0f) return;
            var gm = GameManager.I;
            if (gm == null) return;
            int round = RoundDirector.Round, floor = FloorDirector.I != null ? FloorDirector.I.floor : 0;
            scoreTitle.text = round > 0 ? $"Ondata {Roman(round)} · Piano {floor}" : $"Piano {floor}";
            int fired = GameManager.shotsFired, hit = GameManager.shotsHit, kills = gm.kills, heads = GameManager.headshots;
            float acc = fired > 0 ? 100f * hit / fired : 0f;
            int secs = Mathf.FloorToInt(Time.timeSinceLevelLoad);
            var eq = Equipment.I;
            var he = HiddenEyes.I;
            scoreLeft.text = "Punti\nUccisioni\nAlla testa\nPrecisione\nProve superate\nMedaglie\nMorti\nTempo\nEquipaggiamento\nOcchi di vetro" + (Coop.Active ? "\nGiocatori" : "");
            scoreRight.text = $"{gm.score.ToString("N0", It)}\n{kills}\n{heads} ({(kills > 0 ? 100 * heads / kills : 0)}%)\n{acc:F0}%  ({hit}/{fired})\n" +
                              $"{Prove.completed}\n{Medals.Count}\n{GameManager.deaths}\n{secs / 60}:{secs % 60:00}\n" +
                              (eq != null ? $"acquasanta {eq.grenades}, carillon {eq.lures}, mine {eq.mines}" : "-") + "\n" + (he != null ? $"{he.Found}/{he.Total}" : "-") +
                              (Coop.Active ? $"\n{Targets.Count}" : "");
        }
    }
}

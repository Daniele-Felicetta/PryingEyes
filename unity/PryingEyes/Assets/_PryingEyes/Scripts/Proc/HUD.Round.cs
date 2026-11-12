using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Il numero del round, rosso in basso a sinistra come nei Black Ops Zombies: compare con il primo round, resta
    /// finche' dura la partita e lampeggia quando un round comincia e quando finisce.</summary>
    public partial class HUD
    {
        Text roundText;
        float roundPulse;
        const float RoundPulseTime = 1.6f;
        static readonly Color RoundInk = new Color(0.66f, 0.09f, 0.08f);
        static readonly Color RoundHot = new Color(1f, 0.30f, 0.22f);

        void BuildRoundUI()
        {
            roundText = MakeText("Round", new Vector2(0f, 0f), new Vector2(26f, 100f), new Vector2(300f, 84f), 68, TextAnchor.LowerLeft, "", RoundInk, FontStyle.Bold);
        }

        /// <summary>n = 0 spegne il numero (nuova partita). 'pulse' lo fa lampeggiare.</summary>
        public void SetRound(int n, bool pulse = true)
        {
            if (roundText == null) return;
            roundText.text = n > 0 ? n.ToString() : "";
            if (pulse && n > 0) roundPulse = RoundPulseTime;
        }

        public string CurrentRound => roundText != null ? roundText.text : "";

        void UpdateRoundUI(float dt)
        {
            if (roundText == null || roundText.text.Length == 0) return;
            roundPulse = Mathf.Max(0f, roundPulse - dt);
            float k = roundPulse / RoundPulseTime;
            // lampeggia: due battiti che si spengono, piu' grande all'inizio
            float beat = k * (0.5f + 0.5f * Mathf.Cos((1f - k) * 12.5f));
            roundText.color = Color.Lerp(RoundInk, RoundHot, beat);
            float s = 1f + 0.28f * k;
            roundText.rectTransform.localScale = new Vector3(s, s, 1f);
        }
    }
}

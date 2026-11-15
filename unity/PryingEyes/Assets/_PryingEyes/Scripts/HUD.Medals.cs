using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>La medaglia (Medals.cs): in alto al centro, sotto la bussola, il nome in oro tra due filetti e i punti sotto; entra con un
    /// colpo di scala, resta un secondo e mezzo e svanisce. Una nuova prende il posto della precedente.</summary>
    public partial class HUD
    {
        Text medalText, medalSub;
        Image medalLineL, medalLineR;
        float medalT;
        const float MedalTime = 1.9f;

        void BuildMedalUI()
        {
            medalText = MakeText("Medaglia", new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(720, 42), 30, TextAnchor.MiddleCenter, "", Gold, FontStyle.Bold);
            medalSub = MakeText("MedagliaPunti", new Vector2(0.5f, 1f), new Vector2(0, -184), new Vector2(720, 24), 16, TextAnchor.MiddleCenter, "", Gold, FontStyle.Normal);
            medalLineL = Line("MedagliaFiletto1", new Vector2(-250, -150));
            medalLineR = Line("MedagliaFiletto2", new Vector2(250, -150));
            SetAlpha(medalText, Gold, 0f);
            SetAlpha(medalSub, Gold, 0f);
        }

        Image Line(string name, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(medalText.transform.parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(120, 2);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(Gold.r, Gold.g, Gold.b, 0f);
            return img;
        }

        public void ShowMedal(string name, int points)
        {
            if (medalText == null) return;
            medalText.text = name.ToUpperInvariant();
            medalSub.text = "MEDAGLIA   +" + points;
            medalT = MedalTime;
        }

        /// <summary>Per le prove: la medaglia mostrata adesso (vuota se svanita).</summary>
        public string MedalShown => medalText != null && medalT > 0f ? medalText.text : "";

        void UpdateMedalUI(float dt)
        {
            if (medalText == null || medalT <= 0f) return;
            medalT -= dt;
            float age = MedalTime - medalT;
            float a = Mathf.Clamp01(age / 0.08f) * Mathf.Clamp01(medalT / 0.45f);
            float s = 1f + 0.35f * Mathf.Exp(-age * 13f);
            medalText.rectTransform.localScale = Vector3.one * s;
            SetAlpha(medalText, Gold, a);
            SetAlpha(medalSub, Gold, a * 0.9f);
            var lc = new Color(Gold.r, Gold.g, Gold.b, a * 0.8f);
            float w = 60f + 60f * Mathf.Clamp01(age / 0.25f);
            medalLineL.color = lc; medalLineR.color = lc;
            medalLineL.rectTransform.sizeDelta = new Vector2(w, 2);
            medalLineR.rectTransform.sizeDelta = new Vector2(w, 2);
            if (medalT <= 0f) { SetAlpha(medalText, Gold, 0f); SetAlpha(medalSub, Gold, 0f); medalLineL.color = medalLineR.color = new Color(0, 0, 0, 0); }
        }
    }
}

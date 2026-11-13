using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>La fila dei perk, in alto a sinistra sotto l'obiettivo: quattro posti, ogni libro letto lascia la sua icona. Il Velo
    /// mostra il tasto [H] e un quadrante scuro che si svuota col tempo di recupero; la Rinascita si spegne quando l'hai usata sul
    /// piano. Un perk appena preso si ingrandisce e rientra.</summary>
    public partial class HUD
    {
        sealed class PerkSlotUi { public Image frame, icon, cool; public Text key; public float pop = 1f; public int shownId = -1; }
        PerkSlotUi[] perkUi;
        int perkShown;
        const float PerkSize = 34f, PerkGap = 8f, PerkX = 28f, PerkY = -120f;

        void BuildPerksUI()
        {
            perkUi = new PerkSlotUi[Perks.Slots];
            for (int i = 0; i < perkUi.Length; i++)
            {
                var s = new PerkSlotUi();
                Vector2 pos = new Vector2(PerkX + i * (PerkSize + PerkGap), PerkY);
                s.frame = MakeImage("PerkPosto" + i, Color.white);
                s.frame.sprite = PerkIcons.Empty();
                s.icon = MakeImage("PerkIcona" + i, Color.white);
                s.cool = MakeImage("PerkRecupero" + i, new Color(0f, 0f, 0f, 0.62f));
                s.cool.sprite = PerkIcons.White();
                s.cool.type = Image.Type.Filled;
                s.cool.fillMethod = Image.FillMethod.Radial360;
                s.cool.fillOrigin = (int)Image.Origin360.Top;
                s.cool.fillClockwise = false;
                PlacePerk(s.frame, pos); PlacePerk(s.icon, pos); PlacePerk(s.cool, pos + new Vector2(2f, -2f), PerkSize - 4f);   // perno al centro: l'icona si ingrandisce da li'
                s.key = MakeText("PerkTasto" + i, new Vector2(0f, 1f), pos + new Vector2(0f, -PerkSize - 2f), new Vector2(PerkSize, 16f), 12, TextAnchor.UpperCenter, "", Dim, FontStyle.Bold);
                s.icon.gameObject.SetActive(false); s.cool.gameObject.SetActive(false);
                perkUi[i] = s;
            }
        }

        static void PlacePerk(Image img, Vector2 pos, float size = PerkSize)
        {
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos + new Vector2(size * 0.5f, -size * 0.5f);
            rt.sizeDelta = new Vector2(size, size);
        }

        void UpdatePerksUI(float dt)
        {
            if (perkUi == null) return;
            int n = Perks.Count;
            if (n > perkShown && perkShown >= 0 && perkShown < perkUi.Length) perkUi[perkShown].pop = 0f;
            perkShown = n;
            for (int i = 0; i < perkUi.Length; i++)
            {
                var s = perkUi[i];
                bool has = i < n;
                if (s.icon.gameObject.activeSelf != has) s.icon.gameObject.SetActive(has);
                if (!has) { s.cool.gameObject.SetActive(false); s.key.text = ""; s.shownId = -1; continue; }
                var id = Perks.At(i);
                if (s.shownId != (int)id) { s.shownId = (int)id; s.icon.sprite = PerkIcons.Get(id); }
                s.pop = Mathf.Min(1f, s.pop + dt / 0.45f);
                float k = s.pop;
                float sc = k < 1f ? 1f + 0.7f * Mathf.Sin((1f - k) * Mathf.PI * 0.5f) * (1f - k) : 1f;
                s.icon.rectTransform.localScale = Vector3.one * sc;
                var c = Color.white;
                bool cd = false;
                string key = "";
                if (id == PerkId.Rinascita && !Perks.ReviveReady) c = new Color(1f, 1f, 1f, 0.32f);
                if (id == PerkId.Velo)
                {
                    key = "H";
                    float f = Perks.VeloCooldownFrac;
                    cd = f > 0f && !Perks.VeloOn;
                    s.cool.fillAmount = f;
                    if (Perks.VeloOn) c = new Color(1f, 1f, 1f, 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 14f));
                    else if (f > 0f) c = new Color(1f, 1f, 1f, 0.55f);
                    else key = "H";
                }
                s.icon.color = c;
                if (s.cool.gameObject.activeSelf != cd) s.cool.gameObject.SetActive(cd);
                s.key.text = key;
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Barra della vita del capo (Enemy.Boss, il Cardinale delle ondate ottave): nome inciso in serif e barra rossa in alto al
    /// centro, sotto la bussola. Compare sfumando quando il capo entra in gioco, la parte persa resta chiara un attimo prima di calare
    /// (si vede quanto ha fatto il colpo), sparisce sfumando quando cade.</summary>
    public partial class HUD
    {
        Text bossName;
        Image bossBack, bossFill, bossLag;
        float bossA, bossShown = 1f, bossLagV = 1f;
        const float BossW = 520f;

        void BuildBossUI()
        {
            bossName = MakeText("BossName", new Vector2(0.5f, 1), new Vector2(0, -132), new Vector2(700, 30), 22, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            bossName.font = Serif();
            bossBack = MakeImage("BossBack", new Color(0f, 0f, 0f, 0.55f));
            Place(bossBack.rectTransform, new Vector2(0, -166), new Vector2(BossW + 6f, 13f), new Vector2(0.5f, 1f));
            bossLag = MakeImage("BossLag", new Color(1f, 0.86f, 0.70f, 0.85f));
            Place(bossLag.rectTransform, new Vector2(-BossW * 0.5f, -169), new Vector2(BossW, 7f), new Vector2(0f, 1f));
            bossFill = MakeImage("BossFill", new Color(0.72f, 0.07f, 0.06f));
            Place(bossFill.rectTransform, new Vector2(-BossW * 0.5f, -169), new Vector2(BossW, 7f), new Vector2(0f, 1f));
            SetBossAlpha(0f);
        }

        static void Place(RectTransform rt, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        void SetBossAlpha(float a)
        {
            if (bossName == null) return;
            var c = Ink; c.a = a; bossName.color = c;
            bossBack.color = new Color(0f, 0f, 0f, 0.55f * a);
            bossLag.color = new Color(1f, 0.86f, 0.70f, 0.85f * a);
            bossFill.color = new Color(0.72f, 0.07f, 0.06f, a);
        }

        void UpdateBossUI(float dt)
        {
            if (bossName == null) return;
            var b = Enemy.Boss;
            bool on = b != null && !b.IsDead && b.maxHp > 0f;
            bossA = Mathf.MoveTowards(bossA, on ? 1f : 0f, dt * (on ? 1.5f : 0.8f));
            if (on)
            {
                bossName.text = "Il Cardinale";
                bossShown = Mathf.Clamp01(b.hp / b.maxHp);
            }
            else if (b == null) bossShown = Mathf.MoveTowards(bossShown, 0f, dt * 2f);
            // la parte appena persa resta chiara mezzo secondo, poi scende
            if (bossLagV < bossShown) bossLagV = bossShown;
            else bossLagV = Mathf.MoveTowards(bossLagV, bossShown, dt * 0.45f);
            bossFill.rectTransform.sizeDelta = new Vector2(BossW * bossShown, 7f);
            bossLag.rectTransform.sizeDelta = new Vector2(BossW * bossLagV, 7f);
            SetBossAlpha(bossA);
            if (bossA <= 0f && !on) { bossShown = 1f; bossLagV = 1f; }
        }
    }
}

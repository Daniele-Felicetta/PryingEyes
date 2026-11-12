using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Punti e potenziamenti nell'HUD: i "+N" che salgono accanto al mirino (come nei Black Ops Zombies, ogni guadagno
    /// compare un attimo), i timer dello Sguardo e dell'Offerta in basso al centro, il lampo bianco della Campana.</summary>
    public partial class HUD
    {
        sealed class FeedItem { public Text t; public float age = 99f; public Vector2 pos; public Color col; }
        FeedItem[] feed;
        int feedNext;
        const float FeedTime = 1.15f;
        static readonly Color Gold = new Color(0.96f, 0.78f, 0.30f);
        static readonly Color EyeRed = new Color(0.92f, 0.22f, 0.16f);

        sealed class BuffRow { public Text label; public Image back, fill; public Color col; public string name; }
        BuffRow[] buffs;
        Image flashImg;
        Color flashCol;
        float flashT, flashDur, flashPeak;
        const float BuffBarW = 170f;

        void BuildPuntiUI()
        {
            feed = new FeedItem[8];
            for (int i = 0; i < feed.Length; i++)
            {
                var t = MakeText("Punto" + i, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 30f), 20, TextAnchor.MiddleLeft, "", Ink, FontStyle.Bold);
                t.gameObject.SetActive(false);
                feed[i] = new FeedItem { t = t };
            }
            buffs = new[] { MakeBuff(0, "SGUARDO", EyeRed), MakeBuff(1, "OFFERTA", Gold) };
            flashImg = MakeImage("LampoBianco", Color.clear);
            var fr = flashImg.GetComponent<RectTransform>();
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
        }

        BuffRow MakeBuff(int idx, string name, Color col)
        {
            float y = 156f + idx * 30f;
            var b = new BuffRow { name = name, col = col };
            b.label = MakeText("Buff" + idx, new Vector2(0.5f, 0f), new Vector2(0f, y + 6f), new Vector2(300f, 20f), 14, TextAnchor.LowerCenter, "", col, FontStyle.Normal);
            b.back = MakeImage("BuffFondo" + idx, new Color(0f, 0f, 0f, 0.5f));
            b.fill = MakeImage("BuffPieno" + idx, col);
            PlaceBar(b.back, new Vector2(0f, y), 0.5f, BuffBarW);
            PlaceBar(b.fill, new Vector2(-BuffBarW * 0.5f, y), 0f, BuffBarW);
            b.label.gameObject.SetActive(false); b.back.gameObject.SetActive(false); b.fill.gameObject.SetActive(false);
            return b;
        }

        static void PlaceBar(Image img, Vector2 pos, float pivotX, float w)
        {
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(pivotX, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(w, 3f);
        }

        /// <summary>Un guadagno di punti: sale e svanisce accanto al mirino. Oro quando i punti sono doppi o l'uccisione e' col coltello.</summary>
        public void PointFeed(int n, bool doubled = false)
        {
            if (feed == null || n <= 0) return;
            var f = feed[feedNext];
            feedNext = (feedNext + 1) % feed.Length;
            f.age = 0f;
            f.t.text = "+" + n;
            f.t.fontSize = n >= 100 ? 25 : n >= 50 ? 21 : 16;
            f.col = doubled || n >= 130 ? Gold : n >= 50 ? Ink : Dim;
            f.pos = new Vector2(58f + Random.Range(0f, 8f), 6f + (feedNext % 3) * 15f);
            f.t.rectTransform.anchoredPosition = f.pos;
            f.t.gameObject.SetActive(true);
        }

        /// <summary>Lampo a tutto schermo (la Campana): parte forte e svanisce in 'dur' secondi.</summary>
        public void Flash(Color c, float dur, float peak = 0.9f)
        {
            flashCol = c; flashDur = flashT = Mathf.Max(0.05f, dur); flashPeak = peak;
        }

        void UpdatePuntiUI(float dt)
        {
            if (feed != null)
                foreach (var f in feed)
                {
                    if (f.age >= FeedTime) continue;
                    f.age += dt;
                    if (f.age >= FeedTime) { f.t.gameObject.SetActive(false); continue; }
                    float k = f.age / FeedTime;
                    f.t.rectTransform.anchoredPosition = f.pos + new Vector2(k * 16f, k * 42f);
                    float a = k < 0.12f ? k / 0.12f : 1f - Mathf.Pow((k - 0.12f) / 0.88f, 2f);
                    var c = f.col; c.a = Mathf.Clamp01(a);
                    f.t.color = c;
                }
            if (buffs != null)
            {
                UpdateBuff(buffs[0], Drops.InstaLeft);
                UpdateBuff(buffs[1], Drops.DoubleLeft);
            }
            if (flashImg != null)
            {
                if (flashT > 0f)
                {
                    flashT = Mathf.Max(0f, flashT - dt);
                    float k = flashT / flashDur;
                    var c = flashCol; c.a = flashPeak * k * k;
                    flashImg.color = c;
                }
                else if (flashImg.color.a > 0f) flashImg.color = Color.clear;
            }
        }

        void UpdateBuff(BuffRow b, float left)
        {
            bool on = left > 0f;
            if (b.label.gameObject.activeSelf != on)
            {
                b.label.gameObject.SetActive(on); b.back.gameObject.SetActive(on); b.fill.gameObject.SetActive(on);
            }
            if (!on) return;
            b.label.text = b.name + "  " + Mathf.CeilToInt(left);
            float blink = left < 6f ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 12f) : 1f;     // gli ultimi secondi lampeggia
            var c = b.col; c.a = blink;
            b.label.color = c; b.fill.color = c;
            b.fill.rectTransform.sizeDelta = new Vector2(BuffBarW * Mathf.Clamp01(left / Drops.Duration), 3f);
        }
    }
}

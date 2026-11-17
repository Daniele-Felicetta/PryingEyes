using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Resolution-independent brass engraving: a cathedral arch, watchful iris and hotel floor plan. 0.8.0: the eye is alive: it follows
    /// the pointer (or the entry picked with the keyboard or the controller), blinks every few seconds, and a little dust drifts in the room.
    /// Everything that moves stops with "Oscillazione visuale" at zero, and in the editor (the review images come out still).</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuEngraving : MaskableGraphic
    {
        public bool hero;
        float nextFrame;
        Vector2 gaze, gazeTarget;       // -1..1: dove guarda l'occhio (x a destra, y in alto)
        float lid = 1f;                 // 1 aperto, quasi 0 chiuso
        float blinkT = -1f, nextBlink = 3.5f;
        static readonly Color Gold = new Color(0.69f, 0.53f, 0.30f, 0.75f);
        static readonly Color Patina = new Color(0.19f, 0.36f, 0.33f, 0.55f);

        /// <summary>Per le prove: quanto e' aperto l'occhio e dove guarda.</summary>
        public float Lid => lid;
        public Vector2 Gaze => gaze;

        bool Animated => Application.isPlaying && GameSettings.Motion >= 0.01f;

        void Update()
        {
            if (!Animated)
            {
                if (lid != 1f || gaze != Vector2.zero) { lid = 1f; gaze = Vector2.zero; SetVerticesDirty(); }
                return;
            }
            float now = Time.unscaledTime;
            if (hero) Look(Time.unscaledDeltaTime, now);
            if (now < nextFrame) return;
            nextFrame = now + (hero ? 0.033f : 0.08f);
            SetVerticesDirty();
        }

        /// <summary>Lo sguardo verso il puntatore, o verso la voce scelta se si naviga con tastiera o controller; l'ammiccamento.</summary>
        void Look(float dt, float now)
        {
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 eye = RectTransformUtility.WorldToScreenPoint(cam, rectTransform.position), point = Input.mousePosition;
            var system = EventSystem.current;
            var selected = system != null ? system.currentSelectedGameObject : null;
            if (PadMenuInput.NavMode && selected != null && selected.activeInHierarchy) point = RectTransformUtility.WorldToScreenPoint(cam, selected.transform.position);
            Vector2 away = (point - eye) / Mathf.Max(1f, Screen.height * 0.6f);
            gazeTarget = new Vector2(Mathf.Clamp(away.x, -1f, 1f), Mathf.Clamp(away.y, -1f, 1f));
            gaze = Vector2.Lerp(gaze, gazeTarget, 1f - Mathf.Exp(-5f * dt));

            if (blinkT < 0f && now >= nextBlink) blinkT = 0f;
            if (blinkT >= 0f)
            {
                blinkT += dt;
                float k = blinkT / 0.30f;
                lid = k < 0.42f ? Mathf.Lerp(1f, 0.07f, k / 0.42f) : Mathf.Lerp(0.07f, 1f, Mathf.Clamp01((k - 0.42f) / 0.58f));
                if (k >= 1f)
                {
                    blinkT = -1f; lid = 1f;
                    float hash = Mathf.Abs(Mathf.Sin(now * 12.9898f) * 43758.5453f) % 1f;      // senza Random: non tocca i semi delle prove
                    nextBlink = now + 3.5f + 4.5f * hash;
                }
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!hero) { Background(vh); return; }
            // The arch is intentionally architectural rather than a flat occult badge.
            for (int i = 0; i < 5; i++)
            {
                float r = 185 + i * 10;
                Arc(vh, new Vector2(0, 15), r, 0, Mathf.PI, i == 0 ? Gold : Patina, 1);
                Line(vh, new Vector2(-r, 15), new Vector2(-r, -205), Gold * new Color(1, 1, 1, 0.28f), 1);
                Line(vh, new Vector2(r, 15), new Vector2(r, -205), Gold * new Color(1, 1, 1, 0.28f), 1);
            }
            for (int i = 0; i < 25; i++)
            {
                float a = i * Mathf.PI / 24;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Line(vh, new Vector2(0, 15) + p * 190, new Vector2(0, 15) + p * 219, Patina, 1);
            }
            Vector2 center = new Vector2(0, 9);
            float drift = !Animated ? 0 : Mathf.Sin(Time.unscaledTime * 0.23f) * 4;
            for (int i = 0; i < 3; i++)
            {
                float height = (75 + i * 9) * lid;
                for (int side = -1; side <= 1; side += 2)
                    for (int j = 0; j < 64; j++)
                    {
                        float x0 = -175 + 350f * j / 64, x1 = -175 + 350f * (j + 1) / 64;
                        float y0 = side * height * Mathf.Sin(Mathf.PI * j / 64), y1 = side * height * Mathf.Sin(Mathf.PI * (j + 1) / 64);
                        Line(vh, center + new Vector2(x0, y0), center + new Vector2(x1, y1), i == 0 ? Gold : Patina, 1.2f);
                    }
            }
            center.x += drift + gaze.x * 14f;
            center.y += gaze.y * 6f * lid;
            for (int i = 0; i < 4; i++) Arc(vh, center, 62 + i * 6, 0, Mathf.PI * 2, i % 2 == 0 ? Gold : Patina, 1, lid);
            for (int i = 0; i < 96; i++)
            {
                float a = i * Mathf.PI * 2 / 96;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * lid);
                Line(vh, center + d * (28 + (i % 3) * 3), center + d * 58, Gold * new Color(1, 1, 1, 0.55f), 1);
            }
            Arc(vh, center, 25, 0, Mathf.PI * 2, Gold, 2, lid);
            Line(vh, center + Vector2.down * 21 * lid, center + Vector2.up * 21 * lid, new Color(0.85f, 0.77f, 0.52f), 5);
            for (int i = 0; i < 6; i++)
            {
                float y = -122 - i * 13;
                Line(vh, new Vector2(-80 - i * 15, y), new Vector2(80 + i * 15, y), Patina, 1);
            }
            Line(vh, new Vector2(0, 105), new Vector2(0, 219), Gold, 1);
            Line(vh, new Vector2(-7, 210), new Vector2(7, 210), Gold, 1);
        }

        void Background(VertexHelper vh)
        {
            var r = rectTransform.rect;
            int start = vh.currentVertCount;
            Color low = new Color(0.018f, 0.04f, 0.045f), high = new Color(0.045f, 0.10f, 0.105f);
            vh.AddVert(new Vector2(r.xMin, r.yMin), low, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), high, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMax), high, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), low, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
            Color faint = new Color(0.32f, 0.45f, 0.37f, 0.10f);
            for (int i = -12; i <= 12; i++)
                Line(vh, new Vector2(i * 90, r.yMin), new Vector2(i * 18, -65), faint, 1);
            for (int i = 0; i < 9; i++)
            {
                float y = r.yMin + Mathf.Pow(i / 8f, 0.6f) * (r.height * 0.30f);
                Line(vh, new Vector2(r.xMin, y), new Vector2(r.xMax, y), faint, 1);
            }
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 5; i++)
                {
                    float x = side * (590 + i * 21);
                    Line(vh, new Vector2(x, r.yMin), new Vector2(x, r.yMax), faint, 1);
                }
            // la polvere: ferma com'era, oppure ogni granello sale piano e luccica (ogni granello ha la sua velocita' e il suo sfasamento)
            float t = Animated ? Time.unscaledTime : 0f;
            for (int i = 0; i < 100; i++)
            {
                float x = Mathf.Sin(i * 127.1f) * r.width * 0.48f, y = Mathf.Cos(i * 73.3f) * r.height * 0.45f;
                float alpha = 0.14f;
                if (t > 0f)
                {
                    float rise = 0.006f + 0.006f * Mathf.Abs(Mathf.Sin(i * 3.7f));
                    float v = ((y / r.height + 0.5f) + t * rise) % 1f;
                    y = (v - 0.5f) * r.height;
                    x += Mathf.Sin(t * 0.17f + i) * 7f;
                    alpha *= 0.55f + 0.45f * Mathf.Sin(t * 0.8f + i * 1.9f);
                }
                Quad(vh, new Vector2(x, y), new Vector2(x + 1.2f, y + 1.2f), new Color(0.74f, 0.63f, 0.42f, alpha));
            }
        }

        static void Arc(VertexHelper vh, Vector2 c, float r, float a0, float a1, Color color, float width, float yScale = 1f)
        {
            for (int i = 0; i < 96; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / 96f), b = Mathf.Lerp(a0, a1, (i + 1) / 96f);
                Line(vh, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * yScale) * r, c + new Vector2(Mathf.Cos(b), Mathf.Sin(b) * yScale) * r, color, width);
            }
        }

        static void Line(VertexHelper vh, Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 d = b - a, n = new Vector2(-d.y, d.x).normalized * width * 0.5f;
            int i = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero); vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero); vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Color color)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero); vh.AddVert(new Vector2(a.x, b.y), color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero); vh.AddVert(new Vector2(b.x, a.y), color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}

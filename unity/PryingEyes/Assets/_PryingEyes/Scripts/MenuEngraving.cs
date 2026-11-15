using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Resolution-independent brass engraving: a cathedral arch, watchful iris and hotel floor plan.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuEngraving : MaskableGraphic
    {
        public bool hero;
        float nextFrame;
        static readonly Color Gold = new Color(0.69f, 0.53f, 0.30f, 0.75f);
        static readonly Color Patina = new Color(0.19f, 0.36f, 0.33f, 0.55f);
        void Update()
        {
            if (!hero || GameSettings.Motion < 0.01f || Time.unscaledTime < nextFrame) return;
            nextFrame = Time.unscaledTime + 0.08f;
            SetVerticesDirty();
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
            float drift = GameSettings.Motion < 0.01f ? 0 : Mathf.Sin(Time.unscaledTime * 0.23f) * 4;
            for (int i = 0; i < 3; i++)
            {
                float height = 75 + i * 9;
                for (int side = -1; side <= 1; side += 2)
                    for (int j = 0; j < 64; j++)
                    {
                        float x0 = -175 + 350f * j / 64, x1 = -175 + 350f * (j + 1) / 64;
                        float y0 = side * height * Mathf.Sin(Mathf.PI * j / 64), y1 = side * height * Mathf.Sin(Mathf.PI * (j + 1) / 64);
                        Line(vh, center + new Vector2(x0, y0), center + new Vector2(x1, y1), i == 0 ? Gold : Patina, 1.2f);
                    }
            }
            center.x += drift;
            for (int i = 0; i < 4; i++) Arc(vh, center, 62 + i * 6, 0, Mathf.PI * 2, i % 2 == 0 ? Gold : Patina, 1);
            for (int i = 0; i < 96; i++)
            {
                float a = i * Mathf.PI * 2 / 96;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Line(vh, center + d * (28 + (i % 3) * 3), center + d * 58, Gold * new Color(1, 1, 1, 0.55f), 1);
            }
            Arc(vh, center, 25, 0, Mathf.PI * 2, Gold, 2);
            Line(vh, center + Vector2.down * 21, center + Vector2.up * 21, new Color(0.85f, 0.77f, 0.52f), 5);
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
            for (int i = 0; i < 100; i++)
            {
                float x = Mathf.Sin(i * 127.1f) * r.width * 0.48f, y = Mathf.Cos(i * 73.3f) * r.height * 0.45f;
                Quad(vh, new Vector2(x, y), new Vector2(x + 1.2f, y + 1.2f), new Color(0.74f, 0.63f, 0.42f, 0.14f));
            }
        }
        static void Arc(VertexHelper vh, Vector2 c, float r, float a0, float a1, Color color, float width)
        {
            for (int i = 0; i < 96; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / 96f), b = Mathf.Lerp(a0, a1, (i + 1) / 96f);
                Line(vh, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * r, color, width);
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

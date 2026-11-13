using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    public partial class HUD
    {
        readonly Image[] damageDirections = new Image[4];
        readonly Vector3[] damageOrigins = new Vector3[4];
        readonly float[] damageLeft = new float[4];
        int damageNext;
        const float DamageDirectionTime = 0.85f;
        static Sprite damageArc;

        public int DamageDirectionCount
        {
            get { int n = 0; foreach (float left in damageLeft) if (left > 0f) n++; return n; }
        }

        void BuildDamageDirections()
        {
            if (damageArc == null)
            {
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                        float radius = Mathf.Sqrt(u * u + v * v);
                        float angle = Mathf.Abs(Mathf.Atan2(u, v) * Mathf.Rad2Deg);
                        float alpha = Mathf.Clamp01((0.07f - Mathf.Abs(radius - 0.83f)) / 0.025f) * Mathf.Clamp01((35f - angle) / 8f);
                        pixels[y * size + x] = new Color(1, 1, 1, alpha);
                    }
                tex.SetPixels(pixels); tex.Apply();
                damageArc = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
            }
            for (int i = 0; i < damageDirections.Length; i++)
            {
                var image = MakeImage("DamageDirection" + i, Color.clear);
                image.sprite = damageArc;
                var rt = image.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one * 0.5f;
                rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.one * 190f;
                damageDirections[i] = image;
            }
        }

        void ShowDamageDirection(Vector3 origin)
        {
            int slot = damageNext++ % damageDirections.Length;
            damageOrigins[slot] = origin; damageLeft[slot] = DamageDirectionTime;
            UpdateDamageDirections(0f);
        }

        void UpdateDamageDirections(float dt)
        {
            var camera = Camera.main;
            if (camera == null) return;
            for (int i = 0; i < damageDirections.Length; i++)
            {
                if (damageDirections[i] == null) continue;
                damageLeft[i] = Mathf.Max(0f, damageLeft[i] - dt);
                var tint = RoundHot;
                tint.a = Mathf.Clamp01(damageLeft[i] / 0.35f);
                damageDirections[i].color = tint;
                if (damageLeft[i] <= 0f) continue;
                Vector3 relative = Quaternion.Euler(0, -camera.transform.eulerAngles.y, 0) * (damageOrigins[i] - camera.transform.position);
                float angle = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;
                damageDirections[i].rectTransform.localRotation = Quaternion.Euler(0, 0, -angle);
            }
        }
    }
}

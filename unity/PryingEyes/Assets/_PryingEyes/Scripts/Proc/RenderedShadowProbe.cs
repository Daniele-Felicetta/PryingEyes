#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Production.Rendering
{
    /// <summary>Measures the contribution of one light's shadows at fixed world points over a sequence of camera poses.</summary>
    internal sealed class RenderedShadowProbe : IDisposable
    {
        const int Width = 640, Height = 360;
        readonly Camera camera;
        readonly RenderTexture target;
        readonly Texture2D pixels;

        public RenderedShadowProbe(Camera template)
        {
            camera = new GameObject("Shadow probe").AddComponent<Camera>();
            camera.CopyFrom(template);
            camera.enabled = false;
            camera.aspect = (float)Width / Height;
            target = RenderTexture.GetTemporary(Width, Height, 24);
            camera.targetTexture = target;
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        }

        public void SetView(Vector3 position, Vector3 direction) =>
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));

        public RaycastHit[] FindReceivers(int layers, float range)
        {
            var receivers = new List<RaycastHit>();
            for (int y = 1; y < 18; y++)
                for (int x = 1; x < 30; x++)
                {
                    var ray = camera.ViewportPointToRay(new Vector3(x / 30f, y / 18f));
                    if (Physics.Raycast(ray, out var hit, range, layers, QueryTriggerInteraction.Ignore)) receivers.Add(hit);
                }
            return receivers.ToArray();
        }

        /// <summary>Returns fractional darkening, or NaN for a point that cannot be compared from this view.</summary>
        public float[] Sample(Light light, IReadOnlyList<RaycastHit> receivers, int layers)
        {
            var indices = new int[receivers.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = VisiblePixel(receivers[i], layers);

            var shadowed = Render();
            var original = light.shadows;
            Color32[] clear;
            try
            {
                light.shadows = LightShadows.None;
                clear = Render();
            }
            finally { light.shadows = original; }

            var samples = new float[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                int pixel = indices[i];
                if (pixel < 0) { samples[i] = float.NaN; continue; }
                float lit = Luminance(clear, pixel);
                samples[i] = Mathf.Clamp01((lit - Luminance(shadowed, pixel)) / Mathf.Max(0.08f, lit));
            }
            return samples;
        }

        int VisiblePixel(RaycastHit receiver, int layers)
        {
            Vector3 ray = receiver.point - camera.transform.position;
            float distance = ray.magnitude;
            if (Vector3.Dot(ray, receiver.normal) >= 0f) return -1;
            Vector3 uv = camera.WorldToViewportPoint(receiver.point);
            int x = Mathf.FloorToInt(uv.x * Width), y = Mathf.FloorToInt(uv.y * Height);
            if (uv.z <= 0f || x < 1 || x >= Width - 1 || y < 1 || y >= Height - 1) return -1;
            if (Physics.Raycast(camera.transform.position, ray / distance, distance - 0.15f, layers, QueryTriggerInteraction.Ignore)) return -1;
            return y * Width + x;
        }

        Color32[] Render()
        {
            var previous = RenderTexture.active;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                return pixels.GetPixels32();
            }
            finally { RenderTexture.active = previous; }
        }

        static float Luminance(Color32[] image, int pixel)
        {
            float sum = 0f;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    var c = image[pixel + y * Width + x];
                    sum += 0.213f * c.r + 0.715f * c.g + 0.072f * c.b;
                }
            return sum / (9f * 255f);
        }

        public void Dispose()
        {
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.Destroy(pixels);
            UnityEngine.Object.Destroy(camera.gameObject);
        }
    }
}
#endif

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Fasci di luce visibili dalle finestre (2026-11-15): la luna dalle finestre dell'albergo e del sanatorio, la luce
    /// colorata dalle vetrate delle chiese, con il pulviscolo che ci galleggia dentro. Ogni faro di finestra (RoomKits.WindowLights:
    /// "Luce della vetrata", "Chiarore del nartece", "Luce della finestra") ha un trapezio additivo lungo il suo asse, dal vetro fino
    /// al pavimento o all'80% della portata, girato a ogni fotogramma verso la camera attorno all'asse; la trama (generata) e' morbida
    /// ai bordi, piu' chiara vicino al vetro e rigata. Avvicinandosi all'asse il fascio sfuma (dentro non si vede un muro di nebbia),
    /// segue l'accensione del faro (il pool spegne quelli lontani) e il colore della luce. Al piu' 24 per piano; -noshafts li toglie.</summary>
    public sealed class LightShafts : MonoBehaviour
    {
        public static LightShafts I;
        public static bool disabled;
        /// <summary>Per i confronti nelle prove: nasconde tutti i fasci senza toglierli.</summary>
        public static bool Hidden;
        const int Max = 24;

        public sealed class Shaft
        {
            public Light light;
            public Transform beam, dust;
            public Material mat;
            public Color col;
            public float alpha, len, baseI, shown;
            public Vector3 origin, axis;
            public ParticleSystem motes;
        }

        readonly List<Shaft> shafts = new List<Shaft>();
        public IReadOnlyList<Shaft> Shafts => shafts;
        static Texture2D beamTex, dotTex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadArgs() { disabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noshafts") >= 0; }

        public static void Build(FloorInstance fi)
        {
            if (I != null) Destroy(I.gameObject);
            I = null;
            if (disabled || fi == null || fi.root == null) return;
            var go = new GameObject("FasciDiLuce");
            go.transform.SetParent(fi.root.transform, false);
            I = go.AddComponent<LightShafts>();
            foreach (var l in fi.lights)
            {
                if (l == null || l.type != LightType.Spot) continue;
                bool church = l.name == "Luce della vetrata" || l.name == "Chiarore del nartece";
                if (!church && l.name != "Luce della finestra") continue;
                I.Add(l, church);
                if (I.shafts.Count >= Max) break;
            }
            if (GameManager.autoTest) Debug.Log($"[PryingEyes] fasci di luce: {I.shafts.Count}");
        }

        void Add(Light l, bool church)
        {
            Vector3 origin = l.transform.position, axis = l.transform.forward;
            float len = l.range * 0.8f;
            if (Physics.Raycast(origin + axis * 0.3f, axis, out var hit, l.range, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                len = Mathf.Min(len, hit.distance + 0.3f);
            if (len < 1.5f) return;
            float w0 = church ? 0.75f : 0.95f;
            float w1 = w0 + len * Mathf.Tan(l.spotAngle * 0.5f * Mathf.Deg2Rad) * 0.24f;     // il fascio visibile e' piu' stretto del cono
            var s = new Shaft { light = l, origin = origin, axis = axis, len = len, baseI = Mathf.Max(0.01f, l.intensity), col = l.color };
            s.alpha = church ? 0.11f : 0.07f;
            var go = new GameObject("Fascio");
            go.transform.SetParent(transform, false);
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(axis);
            go.AddComponent<MeshFilter>().sharedMesh = Trapezoid(w0, w1, len);
            var mr = go.AddComponent<MeshRenderer>();
            s.mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            Fx.ConfigureUnlit(s.mat, BeamTex(), true);
            s.mat.SetColor("_BaseColor", new Color(s.col.r, s.col.g, s.col.b, 0f));
            mr.sharedMaterial = s.mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            s.beam = go.transform;
            s.dust = Dust(s, (w0 + w1) * 0.5f);
            shafts.Add(s);
        }

        /// <summary>Trapezio nel piano XZ locale: largo w0 sul vetro (z = 0), w1 in fondo (z = len); u di traverso, v lungo l'asse.</summary>
        static Mesh Trapezoid(float w0, float w1, float len)
        {
            var m = new Mesh { name = "Fascio" };
            m.vertices = new[] { new Vector3(-w0 * 0.5f, 0f, 0f), new Vector3(w0 * 0.5f, 0f, 0f), new Vector3(-w1 * 0.5f, 0f, len), new Vector3(w1 * 0.5f, 0f, len) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            var b = m.bounds; b.Expand(new Vector3(w1, w1, 0f)); m.bounds = b;   // gira attorno all'asse: limiti larghi
            return m;
        }

        static Texture2D BeamTex()
        {
            if (beamTex != null) return beamTex;
            const int W = 64, H = 128;
            beamTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Fascio" };
            var px = new Color32[W * H];
            var rng = new System.Random(71);
            var streak = new float[W];
            for (int x = 0; x < W; x++) streak[x] = 0.72f + 0.28f * (float)rng.NextDouble();
            for (int k = 0; k < 3; k++)                                         // righe morbide (la luce passa tra i piombi)
                for (int x = 1; x < W - 1; x++) streak[x] = (streak[x - 1] + streak[x] * 2f + streak[x + 1]) * 0.25f;
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                float along = Mathf.SmoothStep(0f, 1f, v / 0.1f) * (1f - Mathf.SmoothStep(0f, 1f, (v - 0.45f) / 0.55f));
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W - 0.5f;
                    float across = Mathf.Exp(-(u * u) / (2f * 0.17f * 0.17f)) * (1f - Mathf.SmoothStep(0.36f, 0.5f, Mathf.Abs(u)));
                    float a = across * along * streak[x];
                    px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(a * 255f, 0f, 255f));
                }
            }
            beamTex.SetPixels32(px);
            beamTex.Apply(true);
            return beamTex;
        }

        static Texture2D DotTex()
        {
            if (dotTex != null) return dotTex;
            const int N = 16;
            dotTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Pulviscolo" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            dotTex.SetPixels32(px);
            dotTex.Apply();
            return dotTex;
        }

        /// <summary>Pulviscolo: granelli minuscoli che compaiono, salgono e scendono piano e spariscono, dentro il fascio (non ruota con
        /// lui: e' un oggetto a parte allineato all'asse).</summary>
        Transform Dust(Shaft s, float width)
        {
            var go = new GameObject("Pulviscolo");
            go.transform.SetParent(transform, false);
            go.transform.position = s.origin + s.axis * (s.len * 0.45f);
            go.transform.rotation = Quaternion.LookRotation(s.axis);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.03f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.maxParticles = 50;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.playOnAwake = false;
            var em = ps.emission;
            em.rateOverTime = 7f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(width * 0.45f, width * 0.45f, s.len * 0.75f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.03f;
            noise.frequency = 0.25f;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            Fx.ConfigureUnlit(m, DotTex(), true);
            m.SetColor("_BaseColor", new Color(s.col.r, s.col.g, s.col.b, 0.55f));
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            s.motes = ps;
            ps.Play();
            return go.transform;
        }

        void LateUpdate()
        {
            var eyes = Seats.Eyes();         // la camera principale o, a schermo condiviso, un occhio per giocatore
            if (eyes.Count == 0) return;
            foreach (var s in shafts)
            {
                Vector3 cp = eyes[0].position;           // il fascio guarda l'occhio piu' vicino
                for (int e = 1; e < eyes.Count; e++)
                {
                    Vector3 q = eyes[e].position;
                    if ((q - s.origin).sqrMagnitude < (cp - s.origin).sqrMagnitude) cp = q;
                }
                bool on = !Hidden && s.light != null && s.light.enabled && s.light.intensity > 0.01f;
                if (s.beam.gameObject.activeSelf != on)
                {
                    s.beam.gameObject.SetActive(on);
                    s.dust.gameObject.SetActive(on);
                    if (on && s.motes != null && !s.motes.isPlaying) s.motes.Play();     // riattivato, il sistema di particelle resta fermo
                }
                if (!on) continue;
                // il piano del fascio contiene l'asse e guarda la camera
                Vector3 toCam = cp - s.origin;
                Vector3 n = toCam - s.axis * Vector3.Dot(toCam, s.axis);
                if (n.sqrMagnitude > 1e-6f) s.beam.rotation = Quaternion.LookRotation(s.axis, n.normalized);
                // vicino all'asse sfuma; segue la luce (colore e intensita')
                float along = Mathf.Clamp(Vector3.Dot(toCam, s.axis), 0f, s.len);
                float dist = (s.origin + s.axis * along - cp).magnitude;
                float a = s.alpha * Mathf.Clamp01((dist - 0.5f) / 2.2f) * Mathf.Clamp01(s.light.intensity / s.baseI);
                s.shown = a;
                var c = s.light.color;
                s.mat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, a));
            }
        }
    }
}

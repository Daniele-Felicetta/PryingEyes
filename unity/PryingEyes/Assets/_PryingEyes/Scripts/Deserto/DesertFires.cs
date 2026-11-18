using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    /// <summary>I fuochi dei campi, di notte: le quattro fiamme piu' vicine (entro 130 m) hanno una luce arancione che vibra, fumo che sale piegato dal vento e qualche scintilla. Le fiamme
    /// stesse sono nella mesh del fuoco (DesertProp.shader le accende con _DSNight); qui c'e' solo quello che manca a una mesh: la luce sul terreno, le tende, gli oggetti attorno, e il fumo.
    /// I fuochi li da' DesertScatter (Fires): lo stesso posto di sempre, perche' il mondo e' una funzione del seme.</summary>
    public sealed class DesertFires : MonoBehaviour
    {
        public static DesertFires I { get; private set; }

        const int Slots = 4;
        const float LightRange = 26f, LightIntensity = 3.4f;

        Transform player;
        readonly Light[] lights = new Light[Slots];
        readonly ParticleSystem[] smoke = new ParticleSystem[Slots], sparks = new ParticleSystem[Slots];
        readonly Vector3[] at = new Vector3[Slots];
        readonly bool[] used = new bool[Slots];
        readonly List<Vector3> near = new List<Vector3>();
        readonly bool[] taken = new bool[Slots];
        Vector3 sortFrom;
        System.Comparison<Vector3> byDistance;
        Material smokeMat, sparkMat;

        /// <summary>Quanti fuochi hanno la luce accesa in questo momento.</summary>
        public int Burning { get; private set; }
        public float Lit { get; private set; }

        /// <summary>Il fuoco con la luce accesa piu' vicino a un punto (per il crepitio e per le prove).</summary>
        public bool NearestBurning(Vector3 from, out Vector3 pos)
        {
            pos = default; float best = float.MaxValue; bool found = false;
            for (int s = 0; s < Slots; s++)
            {
                if (!used[s] || !lights[s].enabled) continue;
                float d = (at[s] - from).sqrMagnitude;
                if (d < best) { best = d; pos = at[s]; found = true; }
            }
            return found;
        }

        public static DesertFires Create(GameObject host, Transform followed)
        {
            var f = host.AddComponent<DesertFires>();
            f.player = followed;
            return f;
        }

        void Awake()
        {
            I = this;
            byDistance = (a, b) => (a - sortFrom).sqrMagnitude.CompareTo((b - sortFrom).sqrMagnitude);
            smokeMat = Fx.Unlit(Fx.PuffTex(), false);
            sparkMat = Fx.Unlit(Fx.SparkTex(), true);
            for (int i = 0; i < Slots; i++) Make(i);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (smokeMat != null) Destroy(smokeMat);
            if (sparkMat != null) Destroy(sparkMat);
        }

        void Make(int i)
        {
            var go = new GameObject("Fuoco " + i);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = LightRange; l.color = new Color(1f, 0.55f, 0.22f); l.intensity = 0f; l.shadows = LightShadows.None; l.enabled = false;
            lights[i] = l;
            smoke[i] = Particles(go.transform, "Fumo", smokeMat, true);
            sparks[i] = Particles(go.transform, "Scintille", sparkMat, false);
        }

        static ParticleSystem Particles(Transform parent, string name, Material m, bool isSmoke)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, isSmoke ? 0.9f : 0.5f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = isSmoke ? new ParticleSystem.MinMaxCurve(3.5f, 5.5f) : new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
            main.startSpeed = isSmoke ? new ParticleSystem.MinMaxCurve(0.3f, 0.7f) : new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            main.startSize = isSmoke ? new ParticleSystem.MinMaxCurve(0.45f, 0.75f) : new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = isSmoke ? new Color(0.30f, 0.28f, 0.26f, 0.34f) : new Color(1f, 0.62f, 0.22f, 1f);
            main.maxParticles = isSmoke ? 36 : 30;
            main.gravityModifier = isSmoke ? -0.04f : -0.02f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = isSmoke ? 9f : 22f; sh.radius = isSmoke ? 0.14f : 0.2f;
            var em = ps.emission; em.rateOverTime = 0f;
            var noise = ps.noise; noise.enabled = true; noise.strength = isSmoke ? 0.35f : 0.5f; noise.frequency = 0.35f;
            var sol = ps.sizeOverLifetime; sol.enabled = isSmoke;
            if (isSmoke) sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 3.2f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      isSmoke ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) } : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var fo = ps.forceOverLifetime; fo.enabled = true; fo.space = ParticleSystemSimulationSpace.World;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        void Update()
        {
            var sc = DesertScatter.I;
            var sky = DesertSky.I;
            if (player == null || sc == null) return;
            Lit = sky != null ? sky.Night : 0f;
            Vector3 p = player.position;
            // i piu' vicini
            near.Clear();
            foreach (var f in sc.Fires) near.Add(f);
            sortFrom = p;
            near.Sort(byDistance);
            if (near.Count > Slots) near.RemoveRange(Slots, near.Count - Slots);
            // chi gia' c'e' resta nel suo posto
            System.Array.Clear(taken, 0, taken.Length);
            for (int s = 0; s < Slots; s++)
            {
                bool keep = false;
                if (used[s])
                    for (int k = 0; k < near.Count; k++)
                        if (!taken[k] && (near[k] - at[s]).sqrMagnitude < 0.04f) { taken[k] = true; keep = true; break; }
                used[s] = keep;
            }
            for (int k = 0; k < near.Count; k++)
            {
                if (taken[k]) continue;
                for (int s = 0; s < Slots; s++)
                    if (!used[s]) { used[s] = true; at[s] = near[k]; lights[s].transform.position = near[k] + new Vector3(0f, 0.8f, 0f); break; }
            }
            var w = sky != null ? sky.Weather : new WeatherState { dirX = 1f };
            float t = Time.time;
            Burning = 0;
            for (int s = 0; s < Slots; s++)
            {
                bool on = used[s] && Lit > 0.03f;
                var l = lights[s];
                if (l.enabled != on) l.enabled = on;
                var em1 = smoke[s].emission; var em2 = sparks[s].emission;
                if (!on) { em1.rateOverTime = 0f; em2.rateOverTime = 0f; continue; }
                Burning++;
                float flick = 0.80f + 0.20f * Mathf.PerlinNoise(t * 7.5f + s * 13f, 0.31f) + 0.07f * Mathf.Sin(t * 23f + s);
                l.intensity = LightIntensity * Lit * flick;
                l.range = LightRange;
                l.transform.position = at[s] + new Vector3(0f, 0.75f + 0.05f * Mathf.Sin(t * 11f + s), 0f);
                em1.rateOverTime = 6f * Lit; em2.rateOverTime = 9f * Lit;
                var fo = smoke[s].forceOverLifetime; fo.x = w.dirX * (0.3f + 1.1f * w.wind); fo.z = w.dirZ * (0.3f + 1.1f * w.wind); fo.y = 0f;
                var fo2 = sparks[s].forceOverLifetime; fo2.x = w.dirX * (0.5f + 1.5f * w.wind); fo2.z = w.dirZ * (0.5f + 1.5f * w.wind); fo2.y = 0f;
                smoke[s].transform.position = at[s] + new Vector3(0f, 0.9f, 0f);
                sparks[s].transform.position = at[s] + new Vector3(0f, 0.5f, 0f);
            }
        }
    }
}

using UnityEngine;

namespace PryingEyes
{
    /// <summary>Lampadario guasto: quasi sempre acceso con un leggero ronzio, ogni tanto una raffica di sfarfallii
    /// o un breve spegnimento. Modula la luce diretta (in tempo reale) e l'emissione delle sue lampadine,
    /// che usano una copia del materiale per non far sfarfallare gli altri lampadari.</summary>
    public class Flicker : MonoBehaviour
    {
        public Light lamp;
        public Renderer[] bulbs;
        public Color emission = new Color(1f, 0.79f, 0.54f) * 4f;
        [Range(0f, 1f)] public float minLevel = 0.06f;

        /// <summary>Spegnimenti dall'avvio (letto dall'autotest).</summary>
        public static int dropouts;

        Material mat;
        float baseIntensity = 1f, level = 1f, target = 1f, stateT, tickT;
        int mode;   // 0 stabile, 1 raffica, 2 spento

        void Start()
        {
            if (lamp == null) lamp = GetComponent<Light>();
            if (lamp != null) baseIntensity = lamp.intensity;
            if (bulbs != null && bulbs.Length > 0 && bulbs[0] != null)
            {
                mat = new Material(bulbs[0].sharedMaterial);
                foreach (var b in bulbs) if (b != null) b.sharedMaterial = mat;
            }
            stateT = Random.Range(2f, 6f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            stateT -= dt;
            if (stateT <= 0f)
            {
                if (mode != 0) { mode = 0; stateT = Random.Range(4f, 12f); }
                else if (Random.value < 0.7f) { mode = 1; stateT = Random.Range(0.3f, 1.4f); tickT = 0f; }
                else { mode = 2; stateT = Random.Range(0.15f, 0.7f); dropouts++; }
            }

            if (mode == 0) target = 0.94f + 0.06f * Mathf.PerlinNoise(Time.time * 7f, 0.3f);
            else if (mode == 2) target = minLevel;
            else
            {
                tickT -= dt;
                if (tickT <= 0f)
                {
                    tickT = Random.Range(0.03f, 0.11f);
                    target = Random.value < 0.45f ? minLevel : Random.Range(0.55f, 1f);
                }
            }

            // il filamento non si spegne di colpo: un filo d'inerzia
            level = Mathf.MoveTowards(level, target, dt * (target > level ? 30f : 18f));
            if (lamp != null) lamp.intensity = baseIntensity * level;
            if (mat != null) mat.SetColor("_EmissionColor", emission * Mathf.Pow(level, 1.5f));
        }
    }
}

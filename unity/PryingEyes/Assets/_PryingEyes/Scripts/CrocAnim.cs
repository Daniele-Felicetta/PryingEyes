using UnityEngine;

namespace PryingEyes
{
    /// <summary>Idle croc animation: jaw snap, tail sway, breathing. Decor only.</summary>
    public class CrocAnim : MonoBehaviour
    {
        public Transform jaw;
        float jawT = 5f, jawA = 0f, phase;

        void Start()
        {
            phase = Random.value * 9f;
            jawT = 5f + Random.value * 9f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float t = Time.time + phase;
            transform.GetChild(0).localScale = new Vector3(0.55f, 0.32f * (1f + Mathf.Sin(t * 1.1f) * 0.025f), 1.5f);
            jawT -= dt;
            if (jawT <= 0f) { jawT = 6f + Random.value * 9f; jawA = 1f; }
            jawA = Mathf.Max(0f, jawA - dt * 1.4f);
            if (jaw != null) jaw.rotation = Quaternion.Euler(0.45f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(jawA)) * Mathf.Rad2Deg, 0, 0);
        }
    }
}

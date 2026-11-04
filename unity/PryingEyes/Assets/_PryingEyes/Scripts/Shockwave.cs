using UnityEngine;

namespace PryingEyes
{
    /// <summary>Expanding shockwave ring on enemy death. Self-destroys.</summary>
    public class Shockwave : MonoBehaviour
    {
        float t = 0f;
        const float DUR = 0.45f;

        public static void Spawn(Vector3 pos, Material glow)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.transform.position = pos + Vector3.up * 0.15f;
            go.transform.localScale = new Vector3(0.6f, 0.06f, 0.6f);
            var m = new Material(glow);
            go.GetComponent<MeshRenderer>().material = m;
            go.AddComponent<Shockwave>();
            Destroy(go, DUR + 0.1f);
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / DUR);
            float r = 0.6f + k * 5f;
            transform.localScale = new Vector3(r, 0.06f, r);
        }
    }
}

using UnityEngine;

namespace PryingEyes
{
    /// <summary>Il corpo di un monaco o di un abate, finita la caduta, sprofonda nel pavimento e sparisce invece di svanire di colpo
    /// (Enemy.Die: Destroy dopo 5 s). Attesa, poi 1,4 s di discesa che accelera e un filo di polvere a terra.</summary>
    public sealed class CorpseSink : MonoBehaviour
    {
        float t, delay = 3.3f, depth = 0.9f;
        Vector3 start;
        bool dusted;

        public static void Begin(GameObject body, float wait = 3.3f)
        {
            if (body == null) return;
            var s = body.AddComponent<CorpseSink>();
            s.delay = wait;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t < delay) { start = transform.position; return; }
            float k = Mathf.Clamp01((t - delay) / 1.4f);
            transform.position = start + Vector3.down * (depth * k * k);
            if (!dusted)
            {
                dusted = true;
                // col corpo fisico la polvere si alza sotto il bacino, che puo' essere caduto lontano da dove stava in piedi
                var rag = GetComponent<EnemyRagdoll>();
                Vector3 at = rag != null && rag.Bodies.Count > 0 && rag.Bodies[0] != null ? rag.Bodies[0].position : start;
                Fx.EnemyImpact(new Vector3(at.x, start.y + 0.05f, at.z), Vector3.up, new Color(0.42f, 0.38f, 0.32f));
            }
        }
    }
}

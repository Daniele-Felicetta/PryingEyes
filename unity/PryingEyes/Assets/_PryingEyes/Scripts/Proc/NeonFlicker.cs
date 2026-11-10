using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Tubo al neon stanco: ronzio di luce e, ogni tanto, una sequenza di scatti spento-acceso in cui si spegne anche
    /// il tubo (non solo la luce). Si ferma quando il pool delle luci spegne la lampada perche' lontana.</summary>
    public class NeonFlicker : MonoBehaviour
    {
        PoolLight pl;
        Renderer tube;
        Material onMat, offMat;
        float baseI, phase, burstT, offT;
        int burst;                  // scatti rimasti: dispari = spento, pari = acceso (a 0 resta acceso)
        bool tubeLit = true;

        public void Init(PoolLight p, Renderer tubeRenderer, Material on, Material off)
        {
            pl = p; tube = tubeRenderer; onMat = on; offMat = off;
            baseI = p.l.intensity;
            phase = Random.value * 100f;
            burstT = Random.Range(3f, 30f);
        }

        void Update()
        {
            if (pl == null || pl.l == null || !pl.l.enabled) return;
            float dt = Time.deltaTime;
            bool on = true;
            if (burst > 0)
            {
                offT -= dt;
                if (offT <= 0f) { burst--; offT = Random.Range(0.03f, 0.12f); }
                on = (burst & 1) == 0;
            }
            else
            {
                burstT -= dt;
                if (burstT <= 0f) { burstT = Random.Range(8f, 36f); burst = Random.Range(2, 5) * 2 - 1; offT = Random.Range(0.04f, 0.1f); on = false; }
            }
            pl.l.intensity = baseI * (on ? 0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 17f + phase, phase) : 0.03f);
            if (tube != null && on != tubeLit) { tubeLit = on; tube.sharedMaterial = on ? onMat : offMat; }
        }
    }
}

using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Lampada stanca: ronzio di luce con qualche calo improvviso (applique, lampadari). Si ferma se la luce e' spenta dal pool.</summary>
    public class LampFlicker : MonoBehaviour
    {
        PoolLight pl;
        float baseI, phase, off;

        public void Init(PoolLight p)
        {
            pl = p;
            baseI = p.l.intensity;
            phase = Random.value * 100f;
        }

        void Update()
        {
            if (pl == null || pl.l == null || !pl.l.enabled) return;
            if (off > 0f) { off -= Time.deltaTime; pl.l.intensity = baseI * 0.08f; return; }
            float n = Mathf.PerlinNoise(Time.time * 11f + phase, phase);
            pl.l.intensity = baseI * (0.82f + 0.3f * n);
            if (Random.value < 0.003f) off = Random.Range(0.05f, 0.35f);
        }
    }
}

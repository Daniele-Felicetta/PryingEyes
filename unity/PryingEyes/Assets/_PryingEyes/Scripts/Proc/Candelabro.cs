using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Candelabro con le fiamme come mesh a parte. Quelli d'arredo sono accesi e basta; quelli dell'obiettivo
    /// partono spenti, si accendono con F e, quando sono tutti accesi, l'ascensore di uscita si apre.</summary>
    public class Candelabro : MonoBehaviour, IInteract
    {
        public bool objective;
        public bool lit;
        public Renderer[] flames = new Renderer[0];
        public Light glow;
        public PoolLight pool;
        public PoolLight hint;      // alone freddo che segnala il candelabro da accendere
        public System.Action<Candelabro> onLit;
        float baseIntensity = 1f, phase;

        public Vector3 Point => transform.position + Vector3.up * 1.0f;
        public float Radius => 2.8f;
        public string Prompt => objective && !lit ? "ACCENDI LE CANDELE" : null;

        void Awake() { phase = Random.value * 50f; }

        public void Setup(bool on)
        {
            lit = on;
            if (glow != null && baseIntensity == 1f) baseIntensity = glow.intensity;
            if (hint != null) hint.wanted = !on;
            if (pool != null) pool.wanted = on;
            else if (glow != null) glow.enabled = on;
            foreach (var f in flames) if (f != null) f.enabled = on;
        }

        public void Use()
        {
            if (lit || !objective) return;
            Setup(true);
            Sfx.At(SoundBank.Ignite, transform.position + Vector3.up, 0.9f, Random.Range(0.95f, 1.05f), 2f, 28f);
            onLit?.Invoke(this);
        }

        void OnDestroy() { Interact.Remove(this); }

        void Update()
        {
            if (!lit || glow == null) return;
            float n = Mathf.PerlinNoise(Time.time * 7f + phase, phase * 0.37f);
            glow.intensity = baseIntensity * (0.72f + 0.55f * n);
        }
    }
}

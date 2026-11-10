using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Una pila per la torcia, abbandonata per terra. Una fascia azzurra emette un bagliore debole che si nota solo
    /// al buio e da vicino; F la raccoglie (fino a cinque di scorta).</summary>
    public class Pila : MonoBehaviour, IInteract
    {
        public Vector3 Point => transform.position + Vector3.up * 0.14f;
        public float Radius => 2.7f;
        public string Prompt => Torch.I != null && Torch.I.spares >= Torch.MaxSpares ? "HAI GIÀ CINQUE PILE" : "RACCOGLI LA PILA";

        static Material bodyMat, capMat, bandMat;

        public void Use()
        {
            var t = Torch.I;
            if (t == null || t.spares >= Torch.MaxSpares) return;
            t.AddSpare();
            Sfx.UI(SoundBank.BatteryPick, 0.55f, Random.Range(0.95f, 1.05f));
            GameManager.I?.hud?.Toast($"Pila raccolta. Scorte: {t.spares}");
            Destroy(gameObject);
        }

        void OnDestroy() { Interact.Remove(this); }

        static void Mats()
        {
            if (bodyMat != null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            bodyMat = new Material(sh) { name = "Pila_Corpo", color = new Color(0.07f, 0.075f, 0.06f) };
            bodyMat.SetFloat("_Metallic", 0.35f); bodyMat.SetFloat("_Smoothness", 0.55f);
            capMat = new Material(sh) { name = "Pila_Tappo", color = new Color(0.62f, 0.62f, 0.6f) };
            capMat.SetFloat("_Metallic", 0.95f); capMat.SetFloat("_Smoothness", 0.7f);
            bandMat = new Material(sh) { name = "Pila_Fascia", color = new Color(0.02f, 0.05f, 0.06f) };
            bandMat.EnableKeyword("_EMISSION");
            bandMat.SetColor("_EmissionColor", new Color(0.10f, 0.85f, 1f) * 3.2f);
        }

        static GameObject Part(Transform parent, string name, Material m, Vector3 pos, Vector3 scale)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = name;
            Util.Kill(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            // il cilindro di Unity sta in piedi sull'asse Y: lo corico lungo lo Z della pila
            g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            var mr = g.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        /// <summary>Crea una pila a terra in 'pos' con l'asse ruotato di 'yaw' gradi.</summary>
        public static Pila Make(Vector3 pos, float yaw, Transform parent)
        {
            Mats();
            var root = new GameObject("Pila");
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            const float D = 0.052f, L = 0.11f;
            float y = D * 0.5f;
            Part(root.transform, "Corpo", bodyMat, new Vector3(0f, y, 0f), new Vector3(D, L * 0.5f, D));
            Part(root.transform, "Fascia", bandMat, new Vector3(0f, y, 0.022f), new Vector3(D * 1.03f, 0.007f, D * 1.03f));
            Part(root.transform, "Polo", capMat, new Vector3(0f, y, L * 0.5f + 0.003f), new Vector3(0.020f, 0.0035f, 0.020f));
            Part(root.transform, "Fondo", capMat, new Vector3(0f, y, -L * 0.5f), new Vector3(D * 0.98f, 0.0015f, D * 0.98f));
            var lg = new GameObject("Bagliore");
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = new Vector3(0f, 0.16f, 0.03f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.45f, 0.92f, 1f);
            l.intensity = 0.55f;
            l.range = 3.0f;
            l.shadows = LightShadows.None;
            LightPool.Register(l, false, true);
            var pulse = lg.AddComponent<LightPulse>();
            pulse.l = l; pulse.baseI = 0.55f; pulse.amp = 0.3f; pulse.speed = 1.9f;
            var p = root.AddComponent<Pila>();
            Interact.Add(p);
            return p;
        }
    }

    /// <summary>Intensita' che respira piano (bagliore delle pile, faro dell'uscita).</summary>
    public class LightPulse : MonoBehaviour
    {
        public Light l;
        public float baseI = 1f, amp = 0.3f, speed = 2f;
        float ph;
        void Awake() { ph = Random.value * 6.28f; }
        void Update() { if (l != null && l.enabled) l.intensity = baseI * (1f + amp * Mathf.Sin(Time.time * speed + ph)); }
    }
}

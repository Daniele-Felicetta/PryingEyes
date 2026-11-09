using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Dardo magico lanciato dai monaci da lontano: nucleo emissivo, anelli in orbita,
    /// luce e scia. Nessun asset scaricato. Colpisce il giocatore o esplode sul muro.</summary>
    public class MagicBolt : MonoBehaviour
    {
        public float damage = 9f;
        public float speed = 9.5f;
        float life = 5f;
        Vector3 dir;
        Light glow;
        Transform spin;
        static Material coreMat, sparkMat, trailMat;

        public static void Launch(Vector3 origin, Vector3 target, float dmg)
        {
            var go = new GameObject("DardoMagico");
            go.layer = 2;   // Ignore Raycast: non blocca i colpi veri
            var b = go.AddComponent<MagicBolt>();
            b.Build();
            b.transform.position = origin;
            b.dir = (target - origin).normalized;
            b.damage = dmg;
            b.transform.rotation = Quaternion.LookRotation(b.dir);
        }

        static void EnsureMats()
        {
            if (coreMat != null) return;
            coreMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            coreMat.color = Color.black;
            coreMat.EnableKeyword("_EMISSION");
            coreMat.SetColor("_EmissionColor", new Color(0.35f, 1.9f, 3.0f));
            coreMat.SetFloat("_Metallic", 0.1f);
            coreMat.SetFloat("_Smoothness", 0.2f);

            sparkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            sparkMat.color = Color.black;
            sparkMat.EnableKeyword("_EMISSION");
            sparkMat.SetColor("_EmissionColor", new Color(0.10f, 0.55f, 1.1f));
            sparkMat.SetFloat("_Metallic", 0.1f);

            trailMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            trailMat.SetColor("_BaseColor", new Color(0.30f, 0.85f, 1f, 1f));
            trailMat.SetFloat("_Surface", 1f);
            trailMat.SetFloat("_Blend", 0f);
            trailMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            trailMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            trailMat.SetFloat("_ZWrite", 0f);
            trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            trailMat.renderQueue = (int)RenderQueue.Transparent;
        }

        void Build()
        {
            EnsureMats();
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Nucleo";
            Destroy(core.GetComponent<Collider>());
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * 0.30f;
            core.GetComponent<MeshRenderer>().material = coreMat;

            var ring = new GameObject("Anelli");
            ring.transform.SetParent(transform, false);
            spin = ring.transform;
            for (int i = 0; i < 3; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(ring.transform, false);
                float a = i * Mathf.PI * 2f / 3f;
                s.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.22f, Mathf.Sin(a) * 0.22f, 0);
                s.transform.localScale = Vector3.one * 0.075f;
                s.GetComponent<MeshRenderer>().material = sparkMat;
            }

            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.35f, 0.8f, 1f);
            glow.intensity = 2.2f;
            glow.range = 4.5f;
            glow.shadows = LightShadows.None;

            var tr = gameObject.AddComponent<TrailRenderer>();
            tr.time = 0.32f;
            tr.startWidth = 0.14f;
            tr.endWidth = 0f;
            tr.material = trailMat;
            tr.numCapVertices = 4;

            var sc = gameObject.AddComponent<SphereCollider>();
            sc.radius = 0.22f;
            sc.isTrigger = true;
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float step = speed * dt;
            if (Physics.SphereCast(transform.position, 0.17f, dir, out RaycastHit hit, step + 0.06f,
                    HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
            {
                Impact(hit.point);
                return;
            }
            transform.position += dir * step;
            if (spin != null) spin.Rotate(Vector3.forward, 420f * dt, Space.Self);
            float pulse = 0.85f + Mathf.Sin(Time.time * 16f) * 0.25f;
            if (glow != null) glow.intensity = 2.2f * pulse;
            life -= dt;
            if (life <= 0f) Destroy(gameObject);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<Enemy>() != null) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            p.Hurt(damage);
            GameManager.damageTaken++;
            if (GameManager.autoTest)
                Debug.Log($"[PryingEyes] magia colpisce il giocatore: {damage:F0} danni");
            Impact(transform.position);
        }

        void Impact(Vector3 p)
        {
            Shockwave.Spawn(p, coreMat);
            Sfx.At(SoundBank.Strike, p, 0.55f, Random.Range(0.8f, 1.05f), 3f, 28f);
            Destroy(gameObject);
        }
    }
}

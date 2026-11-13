using UnityEngine;

namespace PryingEyes
{
    /// <summary>Dardo emissivo con scia rastremata. Il segmento percorso verifica muri e capsule dei bersagli
    /// nello stesso ordine: danno deciso dall'ospite, grafica soltanto sugli ospitati.</summary>
    public class MagicBolt : MonoBehaviour
    {
        public float damage = 9f, speed = 9.5f;
        float life = 5f;
        Vector3 dir, launchedFrom;
        Light glow;
        Transform halo;
        bool visualOnly, ended;
        static Material coreMat, trailMat, haloMat;
        static Mesh shardMesh, haloMesh;
        static readonly Color Tint = new Color(0.48f, 0.82f, 1f);
        const float Radius = 0.17f;

        public static MagicBolt Launch(Vector3 origin, Vector3 target, float dmg, bool visualOnly = false)
        {
            var go = new GameObject("DardoMagico");
            var b = go.AddComponent<MagicBolt>();
            b.dir = (target - origin).normalized;
            if (b.dir.sqrMagnitude < 0.01f) b.dir = Vector3.forward;
            b.launchedFrom = origin; b.damage = dmg; b.visualOnly = visualOnly;
            go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(b.dir));
            b.Build();
            if (!visualOnly) Coop.Game?.BoltLaunched(origin, target, dmg);
            return b;
        }

        static void EnsureAssets()
        {
            if (coreMat != null) return;
            coreMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            coreMat.color = Color.black; coreMat.EnableKeyword("_EMISSION");
            coreMat.SetColor("_EmissionColor", new Color(1.8f, 2.8f, 3.2f));
            trailMat = Fx.Unlit(Fx.BeamTex(), true);
            trailMat.SetColor("_BaseColor", new Color(0.25f, 0.7f, 1f, 0.7f));
            haloMat = Fx.Unlit(Fx.PuffTex(), true);
            haloMat.SetColor("_BaseColor", new Color(0.2f, 0.65f, 1f, 0.55f));
            haloMesh = new Mesh { name = "Alone dardo" };
            haloMesh.vertices = new[] { new Vector3(-0.5f,-0.5f,0), new Vector3(0.5f,-0.5f,0), new Vector3(0.5f,0.5f,0), new Vector3(-0.5f,0.5f,0) };
            haloMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            haloMesh.triangles = new[] { 0,1,2, 0,2,3 }; haloMesh.RecalculateBounds();
            shardMesh = new Mesh { name = "Dardo sfaccettato" };
            shardMesh.vertices = new[] { new Vector3(0,0,0.5f), new Vector3(0,0,-0.5f),
                new Vector3(-0.5f,0,-0.12f), new Vector3(0,0.5f,-0.12f), new Vector3(0.5f,0,-0.12f), new Vector3(0,-0.5f,-0.12f) };
            shardMesh.triangles = new[] { 0,3,2, 0,4,3, 0,5,4, 0,2,5, 1,2,3, 1,3,4, 1,4,5, 1,5,2 };
            shardMesh.RecalculateNormals(); shardMesh.RecalculateBounds();
        }

        public static GameObject ChargeVisual()
        {
            EnsureAssets();
            var go = new GameObject("Sigillo di carica", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = shardMesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = coreMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        void Build()
        {
            var core = ChargeVisual(); core.transform.SetParent(transform, false);
            core.transform.localScale = new Vector3(0.035f, 0.035f, 0.35f);
            var haloGo = new GameObject("Alone", typeof(MeshFilter), typeof(MeshRenderer));
            halo = haloGo.transform; halo.SetParent(transform, false); halo.localScale = Vector3.one * 0.28f;
            haloGo.GetComponent<MeshFilter>().sharedMesh = haloMesh;
            var haloRenderer = haloGo.GetComponent<MeshRenderer>(); haloRenderer.sharedMaterial = haloMat;
            haloRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; haloRenderer.receiveShadows = false;
            glow = gameObject.AddComponent<Light>(); glow.type = LightType.Point;
            glow.color = Tint; glow.intensity = 1.6f; glow.range = 3f; glow.shadows = LightShadows.None;
            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.18f; trail.startWidth = 0.15f; trail.endWidth = 0f;
            trail.sharedMaterial = trailMat; trail.numCapVertices = 3; trail.minVertexDistance = 0.04f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; trail.receiveShadows = false;
        }

        void Update() => Advance(Time.deltaTime);
        void LateUpdate()
        {
            var camera = Camera.main;
            if (halo != null && camera != null) halo.rotation = camera.transform.rotation;
        }

        internal void Advance(float dt)
        {
            if (ended || dt <= 0f) return;
            Vector3 from = transform.position;
            float distance = speed * dt;
            if (Physics.CheckSphere(from, Radius, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
            { Impact(from, -dir); return; }
            bool wall = Physics.SphereCast(from, Radius, dir, out var wallHit, distance, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
            float first = wall ? wallHit.distance : distance;
            ITarget victim = null;
            for (int i = 0; i < Targets.Count; i++)
            {
                var target = Targets.At(i);
                if (!target.Alive) continue;
                float hit = BodyHit(from, dir, target);
                if (hit >= 0f && hit < first) { first = hit; victim = target; }
            }
            if (victim != null)
            {
                transform.position = from + dir * first;
                if (!visualOnly)
                {
                    victim.Hurt(damage, launchedFrom);
                    if (victim is LocalTarget) GameManager.damageTaken++;
                    if (GameManager.autoTest) Debug.Log($"[PryingEyes] magia colpisce il giocatore {victim.Slot}: {damage:F0} danni");
                }
                Impact(transform.position, -dir); return;
            }
            if (wall) { Impact(wallHit.point, wallHit.normal); return; }
            transform.position = from + dir * distance;
            if (glow != null) glow.intensity = 1.6f * (0.9f + Mathf.Sin(Time.time * 7f) * 0.1f);
            life -= dt;
            if (life <= 0f) { ended = true; Destroy(gameObject); }
        }

        // Raggio contro capsula verticale, allargata del raggio del dardo. Nessun salto del bersaglio a bassi fps.
        static float BodyHit(Vector3 from, Vector3 direction, ITarget target)
        {
            const float bodyRadius = 0.38f; // stesso raggio del CharacterController (HotelBuilder)
            float radius = bodyRadius + Radius;
            Vector3 o = from - target.Feet;
            float low = bodyRadius, high = Mathf.Max(low, target.BodyHeight - bodyRadius);
            float result = Mathf.Min(SphereHit(o, direction, Vector3.up * low, radius), SphereHit(o, direction, Vector3.up * high, radius));
            float a = direction.x * direction.x + direction.z * direction.z;
            float b = o.x * direction.x + o.z * direction.z;
            float c = o.x * o.x + o.z * o.z - radius * radius;
            if (c <= 0f && o.y >= low && o.y <= high) return 0f;
            float discriminant = b * b - a * c;
            if (a > 0.000001f && discriminant >= 0f)
            {
                float hit = (-b - Mathf.Sqrt(discriminant)) / a;
                float y = o.y + direction.y * hit;
                if (hit >= 0f && y >= low && y <= high) result = Mathf.Min(result, hit);
            }
            return result;
        }

        static float SphereHit(Vector3 from, Vector3 direction, Vector3 center, float radius)
        {
            Vector3 offset = from - center;
            float c = offset.sqrMagnitude - radius * radius;
            if (c <= 0f) return 0f;
            float b = Vector3.Dot(offset, direction), disc = b * b - c;
            if (disc < 0f) return float.PositiveInfinity;
            float hit = -b - Mathf.Sqrt(disc);
            return hit >= 0f ? hit : float.PositiveInfinity;
        }

        void Impact(Vector3 point, Vector3 normal)
        {
            if (ended) return;
            ended = true;
            Fx.EnemyImpact(point, normal, Tint);
            Sfx.At(SoundBank.Strike, point, 0.45f, Random.Range(0.95f, 1.1f), 2f, 22f);
            Destroy(gameObject);
        }
    }
}

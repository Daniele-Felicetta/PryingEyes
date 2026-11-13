using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    public partial class Enemy
    {
        AlienRig alienRig;

        static EnemyType BaseType(EnemyType kind) => kind switch
        {
            EnemyType.Veglia or EnemyType.Medusa => EnemyType.Occhio,
            EnemyType.Portachiavi => EnemyType.Abate,
            // i nemici dei piani a tema (EnemyTemi.cs) sono il monaco e l'abate con un altro costume
            EnemyType.Maschera or EnemyType.Direttore or EnemyType.Infermiere => EnemyType.Monaco,
            EnemyType.Primario => EnemyType.Abate,
            _ => kind
        };

        public EnemyType Archetype => BaseType(type);
        public static bool IsFlying(EnemyType kind) => BaseType(kind) == EnemyType.Occhio;

        void SetAlienStats()
        {
            (maxHp, speed, basePitch) = type switch
            {
                EnemyType.Veglia => (60f, 3.5f, 1.35f),
                EnemyType.Medusa => (140f, 1.9f, 0.68f),
                _ => (260f, 1.25f, 0.56f)
            };
            hp = maxHp;
        }

        static GameObject BuildAlien(EnemyType kind, out Material glow, out List<Material> flashes)
        {
            var root = new GameObject(kind.ToString());
            var rig = root.AddComponent<AlienRig>();
            rig.Build(kind);
            glow = rig.Glow;
            flashes = new List<Material> { rig.Shell };
            return root;
        }
    }

    public sealed partial class AlienRig : MonoBehaviour
    {
        readonly List<(Transform joint, Vector3 axis, float phase, float amplitude)> limbs = new();
        Enemy.EnemyType kind;
        Transform model;
        Material membrane;
        public Material Shell { get; private set; }
        public Material Glow { get; private set; }
        static Mesh sphere;

        public void Build(Enemy.EnemyType alien)
        {
            kind = alien;
            model = Joint(transform, "Corpo", Vector3.zero);
            bool flying = Enemy.IsFlying(kind);
            if (flying)
            {
                gameObject.AddComponent<SphereCollider>().radius = 0.36f;
                var body = gameObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = true;
            }
            else
            {
                var collider = gameObject.AddComponent<CapsuleCollider>();
                collider.center = Vector3.up * 0.78f;
                collider.radius = 0.46f;
                collider.height = 1.56f;
            }

            var colors = kind switch
            {
                Enemy.EnemyType.Veglia => (new Color(0.68f, 0.59f, 0.42f), new Color(1f, 0.64f, 0.24f)),
                Enemy.EnemyType.Medusa => (new Color(0.08f, 0.22f, 0.26f), new Color(0.23f, 0.90f, 1f)),
                _ => (new Color(0.23f, 0.16f, 0.10f), new Color(1f, 0.62f, 0.22f))
            };
            Shell = MaterialFor(colors.Item1 * 0.46f, 0.42f);
            membrane = MaterialFor(Color.Lerp(colors.Item1, colors.Item2, 0.17f) * 0.49f, 0.54f);
            Glow = EnemyMats.Glow(colors.Item2, 0.58f);
            if (kind == Enemy.EnemyType.Veglia)
            {
                membrane.color = new Color(0.075f, 0.085f, 0.105f);
                membrane.SetFloat("_Smoothness", 0.12f);
                membrane.SetFloat("_Cull", 0f);
                Shell.color = new Color(0.78f, 0.66f, 0.47f);
                Shell.SetFloat("_Smoothness", 0.2f);
            }
            else if (kind == Enemy.EnemyType.Portachiavi)
            {
                Shell.color = new Color(0.11f, 0.045f, 0.022f);
                membrane.color = new Color(0.43f, 0.28f, 0.10f);
                membrane.SetFloat("_Metallic", 0.8f);
                membrane.SetFloat("_Smoothness", 0.38f);
            }
            if (kind != Enemy.EnemyType.Medusa) DressRelic();

            Vector3 weakPoint;
            switch (kind)
            {
                case Enemy.EnemyType.Veglia: weakPoint = Vigil(); break;
                case Enemy.EnemyType.Medusa: weakPoint = Jelly(); break;
                default: weakPoint = KeyKeeper(); break;
            }
            var head = Joint(model, "Nucleo", weakPoint);
            head.tag = "Head";
            var weak = head.gameObject.AddComponent<SphereCollider>();
            weak.radius = 0.16f;
        }

        Vector3 Jelly()
        {
            Piece(model, Vector3.up * 0.12f, new Vector3(0.79f, 0.52f, 0.77f), Shell);
            Piece(model, new Vector3(0f, -0.03f, 0f), new Vector3(0.87f, 0.12f, 0.85f), membrane);
            Piece(model, new Vector3(0f, -0.08f, 0f), new Vector3(0.61f, 0.09f, 0.61f), Glow);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                Vector3 radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var tentacle = Limb(radial * 0.25f - Vector3.up * 0.09f, Vector3.Cross(Vector3.up, radial), angle, 28f);
                float length = i % 2 == 0 ? 0.76f : 0.55f;
                Piece(tentacle, -Vector3.up * length * 0.24f, new Vector3(0.095f, length * 0.62f, 0.095f), membrane);
                Piece(tentacle, -Vector3.up * length * 0.68f, new Vector3(0.06f, length * 0.43f, 0.06f), Shell);
                Piece(tentacle, -Vector3.up * length * 0.87f, new Vector3(0.038f, 0.09f, 0.038f), Glow);
            }
            var face = new Vector3(0f, 0.11f, 0.37f);
            Piece(model, face, new Vector3(0.17f, 0.17f, 0.10f), Glow);
            return face;
        }

        Transform Limb(Vector3 position, Vector3 axis, float offset, float amplitude)
        {
            var joint = Joint(model, "Arto", position);
            limbs.Add((joint, axis, offset, amplitude));
            return joint;
        }

        static float FlightTension(float time) => time <= Enemy.FlightWindup
            ? Mathf.SmoothStep(0,1,time/Enemy.FlightWindup)
            : 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Enemy.FlightWindup,Enemy.FlightDuration,time));

        static float FlightPitch(float time) => time <= Enemy.FlightWindup ? -16*FlightTension(time)
            : time <= Enemy.FlightContact ? Mathf.Lerp(-16,22,Mathf.SmoothStep(0,1,Mathf.InverseLerp(Enemy.FlightWindup,Enemy.FlightContact,time)))
            : Mathf.Lerp(22,0,Mathf.SmoothStep(0,1,Mathf.InverseLerp(Enemy.FlightContact,Enemy.FlightDuration,time)));

        public void Animate(float phase, float speed, bool hunting, bool striking, float flightTime = -1)
        {
            float rate = kind == Enemy.EnemyType.Veglia ? (hunting ? 5.5f : 2.5f) : kind == Enemy.EnemyType.Medusa ? 3.8f : 7f;
            float strength = kind == Enemy.EnemyType.Portachiavi ? Mathf.Clamp01(speed) : 1f;
            float t = Time.time * rate + phase;
            foreach (var limb in limbs)
                limb.joint.localRotation = Quaternion.AngleAxis(Mathf.Sin(t + limb.phase) * limb.amplitude * strength +
                    (flightTime >= 0 ? FlightTension(flightTime) * (kind == Enemy.EnemyType.Medusa ? 25 : 8) : 0), limb.axis);
            if (kind == Enemy.EnemyType.Veglia) AnimateVigil(t, striking, flightTime);
            else if (kind == Enemy.EnemyType.Medusa && flightTime >= 0)
            {
                float tension=FlightTension(flightTime);
                model.localScale=new Vector3(1-tension*0.14f,1+tension*0.16f,1-tension*0.14f);
                model.localRotation=Quaternion.Euler(FlightPitch(flightTime),0,0);
            }
            else if (kind == Enemy.EnemyType.Medusa) { model.localScale = Vector3.one * (1f + (striking ? 0.09f : 0.028f) * Mathf.Sin(t)); model.localRotation=Quaternion.identity; }
            else model.localRotation = Quaternion.Euler(striking ? 12f : Mathf.Sin(t * 0.5f) * 2f, 0f, Mathf.Sin(t * 0.7f) * 2f);
        }

        public void Collapse()
        {
            Glow.SetColor("_EmissionColor", Color.black);
            if (vigilFire != null) vigilFire.SetColor("_EmissionColor", Color.black);
            if (vigilHeart != null) vigilHeart.SetColor("_EmissionColor", Color.black);
            if (kind == Enemy.EnemyType.Portachiavi) model.localScale = new Vector3(1f, 0.58f, 1f);
            if (kind == Enemy.EnemyType.Veglia) model.localScale = new Vector3(1f, 0.55f, 1f);
        }

        void OnDestroy()
        {
            Destroy(Shell);
            Destroy(membrane);
            Destroy(Glow);
            if (vigilMetal != null) Destroy(vigilMetal);
            if (vigilFire != null) Destroy(vigilFire);
            if (vigilHeart != null) Destroy(vigilHeart);
            foreach (var mesh in relicMeshes) Destroy(mesh);
            foreach (var texture in relicTextures) Destroy(texture);
        }

        static Material MaterialFor(Color color, float smoothness)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        static Transform Joint(Transform parent, string name, Vector3 position)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.localPosition = position;
            return joint;
        }

        static void Segment(Transform parent, Vector3 a, Vector3 b, float width, Material material)
        {
            var piece = Piece(parent, (a + b) * 0.5f, new Vector3(width, (b - a).magnitude * 1.18f, width), material);
            piece.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        }

        static Transform Piece(Transform parent, Vector3 position, Vector3 scale, Material material, float yaw = 0f)
        {
            if (sphere == null)
            {
                var sample = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere = sample.GetComponent<MeshFilter>().sharedMesh;
                sample.SetActive(false);
                Destroy(sample);
            }
            var piece = Joint(parent, "Mesh", position);
            piece.localScale = scale;
            piece.localRotation = Quaternion.Euler(0f, yaw, 0f);
            piece.gameObject.AddComponent<MeshFilter>().sharedMesh = sphere;
            piece.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return piece;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    public partial class Enemy
    {
        AlienRig alienRig;

        static EnemyType BaseType(EnemyType kind) => kind switch
        {
            EnemyType.Falena or EnemyType.Medusa => EnemyType.Occhio,
            EnemyType.Carapace => EnemyType.Abate,
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
                EnemyType.Falena => (60f, 3.5f, 1.35f),
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

    public sealed class AlienRig : MonoBehaviour
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
                Enemy.EnemyType.Falena => (new Color(0.22f, 0.075f, 0.23f), new Color(1f, 0.36f, 0.72f)),
                Enemy.EnemyType.Medusa => (new Color(0.08f, 0.22f, 0.26f), new Color(0.23f, 0.90f, 1f)),
                _ => (new Color(0.23f, 0.16f, 0.10f), new Color(1f, 0.62f, 0.22f))
            };
            Shell = MaterialFor(colors.Item1 * 0.46f, 0.42f);
            membrane = MaterialFor(Color.Lerp(colors.Item1, colors.Item2, 0.17f) * 0.49f, 0.54f);
            Glow = EnemyMats.Glow(colors.Item2, 0.58f);

            Vector3 weakPoint;
            switch (kind)
            {
                case Enemy.EnemyType.Falena: weakPoint = Moth(); break;
                case Enemy.EnemyType.Medusa: weakPoint = Jelly(); break;
                default: weakPoint = Shellback(); break;
            }
            var head = Joint(transform, "Nucleo", weakPoint);
            head.tag = "Head";
            var weak = head.gameObject.AddComponent<SphereCollider>();
            weak.radius = 0.16f;
        }

        Vector3 Moth()
        {
            Piece(model, Vector3.zero, new Vector3(0.31f, 0.32f, 0.89f), Shell);
            var face = new Vector3(0f, 0.04f, 0.41f);
            foreach (int side in new[] { -1, 1 })
            {
                var wing = Limb(new Vector3(side * 0.13f, 0f, 0f), Vector3.forward, 0f, side * 46f);
                Piece(wing, new Vector3(side * 0.37f, 0f, 0.04f), new Vector3(0.91f, 0.11f, 0.49f), membrane, side * -23f);
                Piece(wing, new Vector3(side * 0.26f, -0.01f, -0.24f), new Vector3(0.64f, 0.10f, 0.36f), Shell, side * -32f);
                Piece(wing, new Vector3(side * 0.48f, 0.012f, 0.13f), new Vector3(0.44f, 0.10f, 0.055f), Glow, side * -23f);
                Piece(model, face + Vector3.right * side * 0.10f, new Vector3(0.10f, 0.14f, 0.09f), Glow);
                Segment(model, face + new Vector3(side * 0.07f, 0.08f, 0f), face + new Vector3(side * 0.20f, 0.26f, 0.12f), 0.045f, Shell);
            }
            return face;
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

        Vector3 Shellback()
        {
            Piece(model, new Vector3(0f, 0.80f, -0.15f), new Vector3(0.96f, 0.86f, 1.34f), Shell);
            for (int i = 0; i < 3; i++)
            {
                float z = 0.26f - i * 0.35f;
                Piece(model, new Vector3(0f, 1.14f, z), new Vector3(0.90f - i * 0.12f, 0.53f, 0.47f), membrane);
                foreach (int side in new[] { -1, 1 })
                {
                    var leg = Limb(new Vector3(side * 0.35f, 0.78f, z), Vector3.up, Mathf.PI * (i + (side + 1) / 2), 24f);
                    Segment(leg, Vector3.zero, new Vector3(side * 0.36f, 0.08f, -0.12f), 0.17f, Shell);
                    Segment(leg, new Vector3(side * 0.34f, 0.10f, -0.12f), new Vector3(side * 0.46f, -0.76f, -0.28f), 0.12f, membrane);
                }
            }
            Vector3 face = new Vector3(0f, 0.83f, 0.52f);
            Piece(model, face, new Vector3(0.47f, 0.43f, 0.40f), Shell);
            foreach (int side in new[] { -1, 1 })
            {
                Piece(model, face + new Vector3(side * 0.12f, 0.07f, 0.18f), new Vector3(0.10f, 0.075f, 0.06f), Glow);
                Segment(model, face + new Vector3(side * 0.20f, -0.09f, 0.02f), face + new Vector3(side * 0.27f, -0.30f, 0.38f), 0.13f, membrane);
            }
            return face + Vector3.forward * 0.21f;
        }

        Transform Limb(Vector3 position, Vector3 axis, float offset, float amplitude)
        {
            var joint = Joint(model, "Arto", position);
            limbs.Add((joint, axis, offset, amplitude));
            return joint;
        }

        public void Animate(float phase, float speed, bool hunting, bool striking)
        {
            float rate = kind == Enemy.EnemyType.Falena ? (hunting ? 22f : 15f) : kind == Enemy.EnemyType.Medusa ? 3.8f : 7f;
            float strength = kind == Enemy.EnemyType.Carapace ? Mathf.Clamp01(speed) : 1f;
            float t = Time.time * rate + phase;
            foreach (var limb in limbs)
                limb.joint.localRotation = Quaternion.AngleAxis(Mathf.Sin(t + limb.phase) * limb.amplitude * strength, limb.axis);
            model.localScale = Vector3.one * (1f + (striking ? 0.09f : 0.028f) * Mathf.Sin(t));
        }

        public void Collapse()
        {
            Glow.SetColor("_EmissionColor", Color.black);
            if (kind == Enemy.EnemyType.Carapace) model.localScale = new Vector3(1f, 0.58f, 1f);
        }

        void OnDestroy()
        {
            Destroy(Shell);
            Destroy(membrane);
            Destroy(Glow);
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

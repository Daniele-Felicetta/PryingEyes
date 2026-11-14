using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes
{
    public partial class Enemy
    {
        /// <summary>Self-contained city silhouettes; they require no prefab import and use the existing pursuit and melee.</summary>
        static GameObject BuildCity(EnemyType type, out Material glow, out List<Material> flashes)
        {
            bool guard = type == EnemyType.Vigile;
            var root = new GameObject(type.ToString());
            var body = root.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0, 1.05f, 0);
            body.height = guard ? 2.2f : 1.95f;
            body.radius = guard ? .40f : .32f;
            var cloth = CityMat(guard ? new Color(.075f, .12f, .19f) : new Color(.19f, .22f, .18f));
            var metal = CityMat(new Color(.12f, .14f, .16f));
            var accent = CityMat(guard ? new Color(.30f, .40f, .52f) : new Color(.72f, .42f, .10f));
            var skin = CityMat(new Color(.27f, .22f, .18f));
            glow = CityMat(guard ? new Color(.24f, .54f, .9f) : new Color(.9f, .7f, .24f));
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", glow.color * 1.5f);

            CityPart(root, PrimitiveType.Capsule, "Jacket", new Vector3(0, 1.20f, 0),
                new Vector3(.55f, .55f, .32f), cloth);
            var head = CityPart(root, PrimitiveType.Sphere, "Head", new Vector3(0, 1.87f, .015f),
                new Vector3(.39f, .44f, .37f), skin);
            head.tag = "Head";
            var hit = head.AddComponent<SphereCollider>();
            hit.radius = .21f;
            CityPart(root, PrimitiveType.Cube, guard ? "Visor" : "Mask", new Vector3(0, 1.87f, .20f),
                new Vector3(.39f, guard ? .16f : .19f, .09f), metal);
            CityPart(root, PrimitiveType.Cube, guard ? "Helmet" : "Cap", new Vector3(0, 2.09f, 0),
                new Vector3(.51f, .16f, .45f), guard ? metal : accent);
            CityPart(root, PrimitiveType.Cube, "Chest band", new Vector3(0, 1.48f, .27f),
                new Vector3(.52f, .085f, .035f), accent);
            for (int side = -1; side <= 1; side += 2)
            {
                CityPart(root, PrimitiveType.Capsule, side < 0 ? "Left leg" : "Right leg", new Vector3(side * .17f, .50f, 0),
                    new Vector3(.22f, .48f, .24f), cloth);
                CityPart(root, PrimitiveType.Cube, "Boot", new Vector3(side * .17f, .10f, .13f),
                    new Vector3(.23f, .20f, .40f), metal);
            }
            var left = CityPart(root, PrimitiveType.Capsule, "Left arm", new Vector3(-.42f, 1.30f, 0),
                new Vector3(.22f, .44f, .23f), cloth);
            var right = CityPart(root, PrimitiveType.Capsule, "Right arm", new Vector3(.42f, 1.30f, 0),
                new Vector3(.22f, .44f, .23f), cloth);
            if (guard)
            {
                CityPart(left, PrimitiveType.Cube, "Riot shield", new Vector3(-.05f, -.18f, .36f),
                    new Vector3(.65f, .95f, .10f), metal);
                CityPart(left, PrimitiveType.Cube, "Shield stripe", new Vector3(-.05f, -.18f, .42f),
                    new Vector3(.55f, .07f, .025f), accent);
            }
            else
            {
                CityPart(right, PrimitiveType.Cube, "Grabber", new Vector3(.05f, -.25f, .32f),
                    new Vector3(.055f, 1.05f, .07f), metal);
                CityPart(right, PrimitiveType.Cube, "Tool tip", new Vector3(.05f, -.76f, .34f),
                    new Vector3(.18f, .08f, .11f), accent);
            }
            var motion = root.AddComponent<CityEnemyMotion>();
            motion.left = left.transform;
            motion.right = right.transform;
            motion.leftLeg = root.transform.Find("Left leg");
            motion.rightLeg = root.transform.Find("Right leg");
            motion.guard = guard;
            flashes = new List<Material> { cloth, metal, accent, skin };
            return root;
        }

        static Material CityMat(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
            return material;
        }

        static GameObject CityPart(GameObject parent, PrimitiveType shape, string name, Vector3 position,
            Vector3 size, Material material)
        {
            // Attachment pivots stay at unit scale; mesh proportions must not shrink children.
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent.transform, false);
            pivot.transform.localPosition = position;
            var go = GameObject.CreatePrimitive(shape);
            go.name = "Mesh";
            go.transform.SetParent(pivot.transform, false);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var collider = go.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            return pivot;
        }
    }

    public sealed class CityEnemyMotion : MonoBehaviour
    {
        public Transform left, right, leftLeg, rightLeg;
        public bool guard;
        float attackTime = -1;
        bool wasAttacking;
        Enemy enemy;
        NavMeshAgent agent;

        void Awake()
        {
            enemy = GetComponent<Enemy>();
            agent = GetComponent<NavMeshAgent>();
        }

        void Update()
        {
            if (enemy == null) enemy = GetComponent<Enemy>();
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (enemy == null || enemy.hp <= 0 || left == null || right == null) return;
            float speed = agent != null && agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0;
            float swing = Mathf.Sin(Time.time * 7f) * Mathf.Clamp01(speed) * 22f;
            bool attacking = enemy.PreparingMelee || enemy.RecoveringMelee;
            if (attacking && !wasAttacking) attackTime = 0;
            wasAttacking = attacking;
            float rightAngle = -swing;
            if (attackTime >= 0)
            {
                attackTime += Time.deltaTime;
                if (attackTime < .50f) rightAngle = Mathf.Lerp(0, 58, Mathf.SmoothStep(0, 1, attackTime / .50f));
                else if (attackTime < .64f) rightAngle = Mathf.Lerp(58, -78, Mathf.SmoothStep(0, 1, (attackTime - .50f) / .14f));
                else rightAngle = Mathf.Lerp(-78, 0, Mathf.SmoothStep(0, 1, (attackTime - .64f) / .62f));
                if (!attacking) attackTime = -1;
            }
            left.localRotation = Quaternion.Euler(guard && attacking ? -25 : swing, 0, 0);
            right.localRotation = Quaternion.Euler(rightAngle, 0, 0);
            if (leftLeg != null) leftLeg.localRotation = Quaternion.Euler(-swing * .7f, 0, 0);
            if (rightLeg != null) rightLeg.localRotation = Quaternion.Euler(swing * .7f, 0, 0);
        }
    }
}

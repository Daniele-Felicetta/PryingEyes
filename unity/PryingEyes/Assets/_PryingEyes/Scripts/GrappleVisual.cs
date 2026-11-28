using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    internal sealed class GrappleVisual
    {
        static Material steel, brass, cable;
        const float WinchScale = 0.72f;
        readonly GameObject root;
        readonly Transform head, winch;
        readonly LineRenderer rope;

        internal GrappleVisual(Transform owner)
        {
            PrepareMaterials();
            root = new GameObject("Grapple");
            root.transform.SetParent(owner, false);
            winch = BuildWinch(root.transform);
            rope = root.AddComponent<LineRenderer>();
            rope.sharedMaterial = cable;
            rope.useWorldSpace = true;
            rope.positionCount = 9;
            rope.startWidth = 0.025f;
            rope.endWidth = 0.018f;
            rope.shadowCastingMode = ShadowCastingMode.Off;
            rope.receiveShadows = false;

            head = new GameObject("Three-prong hook").transform;
            head.SetParent(root.transform, false);
            Rod(head, PrimitiveType.Cylinder, Vector3.back * 0.30f, Vector3.back * 0.045f, 0.055f, steel);
            Rod(head, PrimitiveType.Cylinder, Vector3.back * 0.29f, Vector3.back * 0.23f, 0.079f, brass);
            Rod(head, PrimitiveType.Cylinder, Vector3.back * 0.12f, Vector3.back * 0.065f, 0.09f, brass);
            for (int i = 0; i < 3; i++)
            {
                Vector3 radial = Quaternion.AngleAxis(i * 120f, Vector3.forward) * Vector3.up;
                Vector3 elbow = radial * 0.135f - Vector3.forward * 0.075f;
                Rod(head, PrimitiveType.Cube, Vector3.back * 0.21f, elbow, 0.037f, steel);
                Rod(head, PrimitiveType.Cube, elbow, radial * 0.095f, 0.028f, steel);
            }
        }

        internal static Vector3 HarnessMount(Vector3 feet, Vector3 right, Vector3 forward, float height) =>
            feet + Vector3.up * (height * 0.76f) - right * 0.25f + forward * 0.20f;

        static Quaternion WinchRotation(Vector3 forward, Vector3 toward) =>
            Quaternion.LookRotation(Vector3.RotateTowards(forward, toward.normalized, 65f * Mathf.Deg2Rad, 0f));

        static readonly Vector3 Nozzle = Vector3.forward * (0.23f * WinchScale);

        internal static Vector3 Muzzle(Vector3 mount, Vector3 forward, Vector3 tip) =>
            mount + WinchRotation(forward, tip - mount) * Nozzle;

        static Transform BuildWinch(Transform parent)
        {
            var winch = new GameObject("Grapple winch").transform;
            winch.SetParent(parent, false);
            winch.localScale = Vector3.one * WinchScale;
            Rod(winch, PrimitiveType.Cylinder, Vector3.back * 0.12f, Vector3.forward * 0.14f, 0.117f, steel);
            Rod(winch, PrimitiveType.Cylinder, Vector3.back * 0.13f, Vector3.back * 0.102f, 0.107f, brass);
            Rod(winch, PrimitiveType.Cylinder, Vector3.back * 0.137f, Vector3.back * 0.126f, 0.078f, steel);
            Rod(winch, PrimitiveType.Cube, new Vector3(0.03f, -0.10f, -0.15f), new Vector3(0f, -0.06f, 0.07f), 0.07f, steel);
            Rod(winch, PrimitiveType.Cylinder, Vector3.forward * 0.10f, Vector3.forward * 0.23f, 0.078f, brass);
            Rod(winch, PrimitiveType.Cylinder, Vector3.left * 0.073f, Vector3.right * 0.073f, 0.14f, brass);
            Rod(winch, PrimitiveType.Cylinder, Vector3.left * 0.083f, Vector3.right * 0.083f, 0.095f, steel);
            foreach (var renderer in winch.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            return winch;
        }

        static void PrepareMaterials()
        {
            if (steel != null) return;
            steel = Metal("Grapple steel", new Color(0.27f, 0.30f, 0.33f), 0.74f);
            brass = Metal("Grapple brass", new Color(0.67f, 0.43f, 0.19f), 0.59f);
            cable = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Grapple cable" };
            cable.SetColor("_BaseColor", new Color(0.62f, 0.49f, 0.33f));
        }

        static Material Metal(string name, Color color, float metallic)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", 0.64f);
            return material;
        }

        static void Rod(Transform parent, PrimitiveType kind, Vector3 a, Vector3 b, float width, Material finish)
        {
            GameObject part = GameObject.CreatePrimitive(kind);
            Collider collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = (a + b) * 0.5f;
            part.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            part.transform.localScale = new Vector3(width, Vector3.Distance(a, b) * (kind == PrimitiveType.Cylinder ? 0.5f : 1f), width);
            part.GetComponent<Renderer>().sharedMaterial = finish;
        }

        internal void Draw(Vector3 mount, Vector3 forward, Vector3 tip, bool attached)
        {
            Quaternion rotation = WinchRotation(forward, tip - mount);
            Vector3 from = mount + rotation * Nozzle;
            Vector3 line = tip - from;
            if (!root.activeSelf) root.SetActive(true);
            winch.SetPositionAndRotation(mount, rotation);
            bool lineVisible = line.sqrMagnitude >= 0.025f;
            rope.enabled = lineVisible;
            if (head.gameObject.activeSelf != lineVisible) head.gameObject.SetActive(lineVisible);
            if (!lineVisible) return;
            head.SetPositionAndRotation(tip, Quaternion.LookRotation(line));
            Vector3 joint = head.TransformPoint(Vector3.back * 0.3f);
            float sag = Mathf.Min(line.magnitude * (attached ? 0.014f : 0.052f), attached ? 0.23f : 0.9f);
            for (int i = 0; i < rope.positionCount; i++)
            {
                float t = (float)i / (rope.positionCount - 1);
                rope.SetPosition(i, Vector3.Lerp(from, joint, t) + Vector3.down * (4f * t * (1f - t) * sag));
            }
        }

        internal void Hide() => root.SetActive(false);
    }
}

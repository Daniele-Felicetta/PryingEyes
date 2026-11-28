using UnityEngine;

namespace PryingEyes.Deserto
{
    public static partial class DesertBikeMesh
    {
        public static readonly Color PlanePaint = new Color(0.12f, 0.53f, 0.49f);

        sealed class PlaneKit
        {
            public readonly Material paint = Lit("Libeccio paint", PlanePaint, 0.28f, 0.48f);
            public readonly Material cream = Lit("Libeccio ivory", new Color(0.91f, 0.83f, 0.61f), 0.05f, 0.38f);
            public readonly Material dark = Lit("Libeccio graphite", new Color(0.08f, 0.10f, 0.10f), 0.35f, 0.32f);
            public readonly Material glass = Lit("Libeccio windscreen", new Color(0.13f, 0.27f, 0.32f), 0.55f, 0.86f);
            public readonly Mesh fuselage = DesertLathe.Tube("Fuselage",
                P(0, 0.73f, -2.8f, 0, 0.83f, -2.3f, 0, 0.98f, -1.15f, 0, 1.02f, -0.35f,
                    0, 1.04f, 0.65f, 0, 1.04f, 1.45f, 0, 1.04f, 1.65f),
                new[] { 0.055f, 0.13f, 0.34f, 0.48f, 0.48f, 0.40f, 0.28f }, 24);
            public readonly Mesh wing = DesertLathe.Tube("Tapered wing",
                P(-3.75f, 0, -0.17f, -3.59f, 0, -0.07f, -2.7f, 0, 0.03f, -0.7f, 0, 0.10f,
                    0.7f, 0, 0.10f, 2.7f, 0, 0.03f, 3.59f, 0, -0.07f, 3.75f, 0, -0.17f),
                new[] { 0.21f, 0.48f, 0.62f, 0.79f, 0.79f, 0.62f, 0.48f, 0.21f }, 20);
            public readonly Mesh fin = DesertLathe.Tube("Vertical fin",
                P(0, 0, 0.16f, 0, 0.38f, 0.06f, 0, 0.90f, -0.14f, 0, 0.99f, -0.18f),
                new[] { 0.53f, 0.42f, 0.27f, 0.14f }, 16);
            public readonly Mesh wheel = DesertLathe.Turn("Undercarriage tyre", new[]
            {
                new Vector2(0f, -0.065f), new Vector2(0.22f, -0.065f), new Vector2(0.30f, -0.036f),
                new Vector2(0.30f, 0.036f), new Vector2(0.22f, 0.065f), new Vector2(0f, 0.065f)
            }, 20);
            public readonly Mesh hub = DesertLathe.Tube("Propeller spinner",
                P(0, 0, -0.08f, 0, 0, 0.02f, 0, 0, 0.19f), new[] { 0.16f, 0.14f, 0.025f }, 20);
            public readonly Mesh blade = DesertLathe.Tube("Propeller blade",
                P(0, 0.09f, 0, 0.01f, 0.35f, 0, 0.045f, 0.72f, 0, 0.075f, 0.96f, 0),
                new[] { 0.055f, 0.115f, 0.080f, 0.025f }, 12);
            public readonly Mesh block = Cube("Libeccio fittings", Vector3.one);
        }

        static PlaneKit planeKit;

        static Transform Place(Transform parent, string name, Mesh mesh, Material material, Vector3 at, Vector3 size, Quaternion rotation)
        {
            var part = Part(parent, name, mesh, material);
            part.SetLocalPositionAndRotation(at, rotation);
            part.localScale = size;
            return part;
        }

        static void PlaneBrace(Transform parent, string name, Vector3 a, Vector3 b, float width, Material material)
        {
            Vector3 axis = b - a;
            Place(parent, name, planeKit.block, material, (a + b) * 0.5f, new Vector3(width, width, axis.magnitude), Quaternion.LookRotation(axis));
        }

        public static Rig BuildPlane(Transform parent, DesertBikeDef def)
        {
            var k = planeKit ?? (planeKit = new PlaneKit());
            var rig = new Rig { root = parent, eye = def.eye, steerRest = Quaternion.identity };
            rig.body = new GameObject("Airframe").transform;
            rig.body.SetParent(parent, false);
            var body = rig.body;
            var none = Quaternion.identity;
            Part(body, "Fuselage", k.fuselage, k.paint);
            Place(body, "Main wing", k.wing, k.cream, new Vector3(0f, 2.43f, 0.22f), new Vector3(1f, 0.125f, 1f), none);
            Place(body, "Tailplane", k.wing, k.cream, new Vector3(0f, 0.86f, -2.33f), new Vector3(0.30f, 0.085f, 0.52f), none);
            Place(body, "Vertical fin", k.fin, k.paint, new Vector3(0f, 0.80f, -2.33f), new Vector3(0.11f, 1f, 1f), none);
            Place(body, "Cockpit coaming", k.block, k.dark, new Vector3(0f, 1.48f, 0.19f), new Vector3(0.62f, 0.14f, 0.22f), none);
            var screen = Place(body, "Windscreen", k.block, k.glass, new Vector3(0f, 1.63f, 0.20f), new Vector3(0.62f, 0.35f, 0.028f), Quaternion.Euler(-22f, 0f, 0f));
            rig.screens.Add(screen.GetComponent<Renderer>());
            Place(body, "Seat back", k.block, k.dark, new Vector3(0f, 1.48f, -0.86f), new Vector3(0.47f, 0.37f, 0.19f), Quaternion.Euler(-12f, 0f, 0f));
            rig.steer = Place(body, "Control yoke", k.block, k.dark, def.grip - Vector3.right * def.grip.x, new Vector3(0.45f, 0.045f, 0.07f), none);
            for (int side = -1; side <= 1; side += 2)
            {
                rig.handPt[side < 0 ? 0 : 1] = new Vector3(side * def.grip.x, 0f, 0f);
                PlaneBrace(body, "Wing strut", new Vector3(side * 0.39f, 0.88f, 0.47f), new Vector3(side * 2.4f, 2.39f, 0.30f), 0.07f, k.dark);
                PlaneBrace(body, "Outer landing gear", new Vector3(side * 0.33f, 0.81f, 0.61f), new Vector3(side * 0.78f, 0.30f, 0.66f), 0.09f, k.dark);
                Place(body, "Main wheel", k.wheel, k.dark, new Vector3(side * 0.78f, 0.30f, 0.66f), Vector3.one, Quaternion.Euler(0f, 0f, 90f));
                Place(body, "Wing tip marking", k.block, k.paint, new Vector3(side * 3.18f, 2.43f, 0.22f), new Vector3(0.20f, 0.15f, 0.93f), none);
            }
            PlaneBrace(body, "Tailwheel leg", new Vector3(0f, 0.72f, -2.48f), new Vector3(0f, 0.16f, -2.55f), 0.065f, k.dark);
            Place(body, "Tailwheel", k.wheel, k.dark, new Vector3(0f, 0.16f, -2.55f), Vector3.one * 0.53f, Quaternion.Euler(0f, 0f, 90f));
            rig.propeller = Place(body, "Spinner", k.hub, k.cream, new Vector3(0f, 1.04f, 1.72f), Vector3.one, none);
            Place(rig.propeller, "Upper propeller blade", k.blade, k.dark, Vector3.zero, new Vector3(1f, 1f, 0.24f), none);
            Place(rig.propeller, "Lower propeller blade", k.blade, k.dark, Vector3.zero, new Vector3(1f, 1f, 0.24f), Quaternion.Euler(0f, 0f, 180f));
            return rig;
        }
    }
}

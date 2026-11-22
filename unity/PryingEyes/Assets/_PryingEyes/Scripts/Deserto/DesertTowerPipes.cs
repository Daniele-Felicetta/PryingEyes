using System.Collections.Generic;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>A continuous route on the east wing, whose rectangular footprint is shared by all tower floors.</summary>
    public sealed class DesertTowerPipes : MonoBehaviour
    {
        public const float Radius = 0.035f;
        public static readonly Vector3 Outward = Vector3.right;
        public static readonly Vector3 Across = Vector3.forward;
        static readonly List<DesertTowerPipes> Loaded = new List<DesertTowerPipes>();
        static Material iron, brass;

        Vector3[] bars;
        Mesh ironMesh, brassMesh;
        public int Count => bars.Length;
        public Vector3 Bar(int index) => transform.TransformPoint(bars[index]);
        public float RoofY { get; private set; }

        void OnEnable() => Loaded.Add(this);
        void OnDisable() => Loaded.Remove(this);
        void OnDestroy() { Destroy(ironMesh); Destroy(brassMesh); }

        public static void Build(Transform parent, Site site)
        {
            var east = site.levels[0].rooms[PlanGen.StackRooms - 1];
            var roofPlan = site.levels[site.LevelCount - 1];
            var top = roofPlan.rooms[PlanGen.StackRooms - 1];
            var root = new GameObject("Tower parkour pipes");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(east.x1 * 0.1f + FloorBuilder.TH, site.padY, east.CenterZ + 4.8f);
            var route = root.AddComponent<DesertTowerPipes>();
            route.RoofY = site.LevelY(site.LevelCount - 1) + top.wallH + 0.14f;
            float firstY = 2.08f, lastY = route.RoofY - site.padY + 1.1f;
            int steps = Mathf.CeilToInt((lastY - firstY) / 1.65f);
            route.bars = new Vector3[steps + 1];

            const float standoff = 0.59f, halfWidth = 0.62f;
            var bend = new[] { new Vector3(0.02f, 0f, -halfWidth), new Vector3(0.48f, 0f, -halfWidth),
                new Vector3(standoff, 0f, -0.52f), new Vector3(standoff, 0f, 0.52f),
                new Vector3(0.48f, 0f, halfWidth), new Vector3(0.02f, 0f, halfWidth) };
            var tube = DesertLathe.Tube("Iron pipe", bend, new[] { Radius, Radius, Radius, Radius, Radius, Radius }, 10);
            var socket = DesertLathe.Turn("Pipe mount", new[] { new Vector2(0f, -0.10f), new Vector2(0.12f, -0.10f),
                new Vector2(0.12f, 0.02f), new Vector2(0.064f, 0.06f), new Vector2(0.064f, 0.11f), new Vector2(0f, 0.11f) }, 12);
            var collar = DesertLathe.Turn("Brass grip collar", new[] { new Vector2(0.036f, -0.034f), new Vector2(0.047f, -0.027f),
                new Vector2(0.047f, 0.027f), new Vector2(0.036f, 0.034f) }, 12);
            var ironParts = new List<(Mesh, Matrix4x4)>();
            var brassParts = new List<(Mesh, Matrix4x4)>();
            for (int i = 0; i <= steps; i++)
            {
                // Stagger the bars so consecutive transfers move both up and sideways.
                var at = new Vector3(0f, Mathf.Lerp(firstY, lastY, i / (float)steps), i == 0 || i == steps ? 0f : (i % 2 == 0 ? -0.36f : 0.36f));
                route.bars[i] = at + Outward * standoff;
                ironParts.Add((tube, DesertLathe.At(at)));
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    ironParts.Add((socket, Matrix4x4.TRS(at + Vector3.forward * halfWidth * sign, Quaternion.Euler(0f, 0f, -90f), Vector3.one)));
                    brassParts.Add((collar, Matrix4x4.TRS(route.bars[i] + Vector3.forward * 0.43f * sign, Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
                }
            }
            if (iron == null) iron = Metal("Weathered tower iron", new Color(0.24f, 0.16f, 0.11f), 0.36f);
            if (brass == null) brass = Metal("Tower pipe grip collars", new Color(0.68f, 0.43f, 0.15f), 0.54f);
            route.ironMesh = Draw(root.transform, "Pipes", ironParts, iron);
            route.brassMesh = Draw(root.transform, "Grip collars", brassParts, brass);
            route.MakeRoofWalkable(roofPlan, site.LevelY(site.LevelCount - 1));
        }

        void MakeRoofWalkable(Plan plan, float baseY)
        {
            // The facade roof is a render-only mesh and the room ceiling faces down.
            // Give players the same surface from above and a solid parapet at its outer edges.
            foreach (var room in plan.rooms)
            {
                float roof = baseY + room.wallH + 0.14f;
                AddRoofBox(new Vector3(room.CenterX, roof - 0.07f, room.CenterZ), new Vector3(room.W * 0.1f, 0.14f, room.D * 0.1f));
                for (int i = 0; i < 4; i++)
                {
                    var side = (Side)i;
                    float line = Plan.Line(room, side) * 0.1f;
                    bool alongX = Plan.AlongX(side);
                    foreach (var segment in plan.Segments(room, side))
                    {
                        if (segment.other >= 0) continue;
                        float center = (segment.a0 + segment.a1) * 0.05f;
                        float length = (segment.a1 - segment.a0) * 0.1f;
                        AddRoofBox(new Vector3(alongX ? center : line, roof + 0.475f, alongX ? line : center),
                            new Vector3(alongX ? length : 0.4f, 0.95f, alongX ? 0.4f : length));
                    }
                }
            }
        }

        void AddRoofBox(Vector3 center, Vector3 size)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = transform.InverseTransformPoint(center);
            box.size = size;
        }

        static Material Metal(string name, Color color, float metallic)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", 0.36f);
            return material;
        }

        static Mesh Draw(Transform root, string name, List<(Mesh mesh, Matrix4x4 at)> parts, Material material)
        {
            var mesh = DesertLathe.Join(name, parts);
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return mesh;
        }

        public static DesertTowerPipes FindGrip(Vector3 feet, Vector3 facing, float reachHeight, out int index)
        {
            index = -1;
            if (Vector3.Dot(facing, -Outward) < 0.38f) return null;
            DesertTowerPipes best = null;
            float bestScore = float.PositiveInfinity;
            Vector3 reach = feet + Vector3.up * reachHeight;
            foreach (var route in Loaded)
            {
                if (route.bars == null || Mathf.Abs(route.transform.position.z - feet.z) > 1.4f) continue;
                for (int i = 0; i < route.Count; i++)
                {
                    Vector3 bar = route.Bar(i);
                    Vector3 delta = reach - bar;
                    if (delta.x < 0.19f || delta.x > 1f || Mathf.Abs(delta.z) > 0.57f || Mathf.Abs(delta.y) > 0.55f) continue;
                    float score = (delta - Outward * 0.33f).sqrMagnitude;
                    if (score >= bestScore || Physics.Linecast(reach, bar, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    best = route;
                    bestScore = score;
                    index = i;
                }
            }
            return best;
        }
    }
}

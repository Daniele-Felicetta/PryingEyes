using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    sealed class DesertLakeSurface : MonoBehaviour
    {
        static readonly int SwimmersId = Shader.PropertyToID("_Swimmers");
        static readonly int SwimmerCountId = Shader.PropertyToID("_SwimmerCount");
        static readonly int ImpactsId = Shader.PropertyToID("_Impacts");
        static readonly int ImpactCountId = Shader.PropertyToID("_ImpactCount");
        static readonly int LakeBoundsId = Shader.PropertyToID("_DSLakes");
        static readonly int LakeCountId = Shader.PropertyToID("_DSLakeCount");
        static readonly int LakeLevelsId = Shader.PropertyToID("_DSLakeLevels");
        static readonly List<DesertLakeSurface> active = new List<DesertLakeSurface>(2);
        readonly System.Random sprayRandom = new System.Random(84721);
        readonly Vector4[] swimmers = new Vector4[Seats.MaxSeats];
        readonly Vector4[] impacts = new Vector4[12];
        readonly List<Bounds> deepWater = new List<Bounds>(10);
        DesertLake lake;
        Mesh mesh, underwaterMesh;
        Material material, sprayMaterial, bubbleMaterial, underwaterMaterial;
        ParticleSystem spray, bubbles;
        int swimmerCount, lastCount, impactCursor, impactCount;
        internal int Contacts { get; private set; }

        internal static void BuildAll(Transform terrain)
        {
            var shader = Resources.Load<Shader>("Shaders/DesertLake");
            if (shader == null) return;
            var bounds = new Vector4[2];
            Vector4 levels = Vector4.zero;
            int count = 0;
            foreach (var lake in DesertWorld.Lakes)
            {
                levels[count] = lake.Surface;
                levels[2 + count] = lake.ShoreTurn;
                bounds[count++] = new Vector4(lake.X, lake.Z, 1f / lake.RadiusX, 1f / lake.RadiusZ);
                var surface = new GameObject("Desert lake").AddComponent<DesertLakeSurface>();
                surface.transform.SetParent(terrain, false);
                surface.Build(lake, shader);
                surface.gameObject.AddComponent<DesertLakeLife>().Build(lake, count - 1);
                active.Add(surface);
            }
            Shader.SetGlobalVectorArray(LakeBoundsId, bounds);
            Shader.SetGlobalInt(LakeCountId, count);
            Shader.SetGlobalVector(LakeLevelsId, levels);
        }

        void Build(DesertLake lake, Shader shader)
        {
            const int segments = 96;
            const float edge = 1.1f;
            this.lake = lake;
            transform.position = new Vector3(lake.X, lake.Surface + 0.012f, lake.Z);
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * (2f * Mathf.PI / segments);
                var (x, z) = lake.PointAt(angle, edge);
                vertices[i + 1] = new Vector3(x - lake.X, 0f, z - lake.Z);
                uv[i + 1] = new Vector2(edge, 0f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % segments + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            mesh = new Mesh { name = "Desert lake surface", vertices = vertices, uv = uv, triangles = triangles };
            material = new Material(shader);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            BuildNavBounds();
            BuildSpray();
            BuildUnderwaterView();
        }

        void BuildNavBounds()
        {
            const float swimRadius = 0.89f;
            float extent = lake.RadiusZ * DesertLake.OutlineExtent * swimRadius, row = extent * 0.18f;
            for (float z = lake.Z - extent + row * 0.5f; z < lake.Z + extent; z += row)
            {
                float reach = lake.RadiusX * DesertLake.OutlineExtent * swimRadius;
                float min = lake.X - reach, max = lake.X + reach;
                while (min < max && lake.RadiusAt(min, z) > swimRadius) min += 0.35f;
                while (max > min && lake.RadiusAt(max, z) > swimRadius) max -= 0.35f;
                if (max - min < 0.6f) continue;
                deepWater.Add(new Bounds(new Vector3((min + max) * 0.5f, lake.Surface - DesertLake.Depth * 0.5f - 0.2f, z),
                    new Vector3(max - min, DesertLake.Depth + 0.8f, row + 0.03f)));
            }
        }

        internal static void AddNavBlockers(Bounds bounds, List<NavMeshBuildSource> sources)
        {
            foreach (var surface in active)
            {
                if (surface == null) continue;
                foreach (var water in surface.deepWater)
                    if (bounds.Intersects(water))
                        sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox,
                            transform = Matrix4x4.Translate(water.center), size = water.size, area = 1 });
            }
        }

        internal static bool ContainsEye(Vector3 p)
        {
            return DesertWorld.LakeAt(p.x, p.z, out var lake, out float depth) &&
                p.y < lake.Surface && p.y >= lake.Surface - depth - 0.35f;
        }

        void BuildUnderwaterView()
        {
            var shader = Resources.Load<Shader>("Shaders/LakeUnderwater");
            if (shader == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            var view = new GameObject("Underwater visibility");
            view.transform.SetParent(transform, false);
            underwaterMesh = new Mesh
            {
                name = "Lake underwater screen",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
                bounds = new Bounds(Vector3.down * (DesertLake.Depth * 0.5f), new Vector3(lake.RadiusX * DesertLake.OutlineExtent * 2f, DesertLake.Depth + 3f, lake.RadiusZ * DesertLake.OutlineExtent * 2f))
            };
            underwaterMaterial = new Material(shader);
            underwaterMaterial.SetVector("_LakeVolume", new Vector4(lake.X, lake.Surface, lake.Z, lake.ShoreTurn));
            underwaterMaterial.SetVector("_LakeRadii", new Vector4(1f / lake.RadiusX, 1f / lake.RadiusZ, 0f, 0f));
            view.AddComponent<MeshFilter>().sharedMesh = underwaterMesh;
            var renderer = view.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = underwaterMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.allowOcclusionWhenDynamic = false;
        }

        void BuildSpray()
        {
            var shader = Resources.Load<Shader>("Shaders/LakeSpray");
            if (shader == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            sprayMaterial = new Material(shader);
            sprayMaterial.SetFloat("_WaterLevel", lake.Surface);
            bubbleMaterial = new Material(sprayMaterial);
            bubbleMaterial.SetFloat("_BubbleMode", 1f);
            spray = MakeParticles("Water spray", sprayMaterial, 0.95f, 256);
            bubbles = MakeParticles("Air bubbles", bubbleMaterial, -0.036f, 96);
        }

        ParticleSystem MakeParticles(string label, Material draw, float gravity, int capacity)
        {
            var drops = new GameObject(label);
            drops.transform.SetParent(transform, false);
            var system = drops.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = capacity;
            main.gravityModifier = gravity;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var renderer = drops.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 0.025f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.sharedMaterial = draw;
            system.Play();
            return system;
        }

        internal static bool Near(Vector3 camera)
        {
            foreach (var lake in DesertWorld.Lakes)
                if (Mathf.Abs(camera.x - lake.X) < lake.RadiusX + 44f &&
                    Mathf.Abs(camera.z - lake.Z) < lake.RadiusZ + 44f &&
                    Mathf.Abs(camera.y - lake.Surface) < 38f) return true;
            return false;
        }

        internal static void Contact(Vector3 position, Vector3 flow, float strength, bool step = false)
        {
            foreach (var surface in active)
            {
                if (surface == null || Mathf.Abs(surface.lake.Surface - position.y) > 0.2f ||
                    surface.lake.RadiusAt(position.x, position.z) >= 1f) continue;
                surface.EmitContact(position, flow, strength, step);
                break;
            }
        }

        internal static void StrokeContacts(Vector3 feet, float heading, float surface, float effort, float before, float phase)
        {
            if (effort <= 0.26f || Mathf.Repeat(phase - before, 1f) > 0.25f) return;
            Vector3 forward = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            for (int arm = 0; arm < 2; arm++)
            {
                if (!SwimStroke.Crossed(before, phase, arm, SwimStroke.Catch)) continue;
                Vector3 p = feet + forward * 0.84f + side * (arm == 0 ? -0.29f : 0.29f);
                p.y = surface + 0.016f;
                Contact(p, -forward, 0.21f + effort * 0.35f);
            }
        }

        internal static void Bubbles(Vector3 p)
        {
            foreach (var surface in active)
            {
                if (surface == null || surface.bubbles == null || p.y >= surface.lake.Surface - 0.16f ||
                    surface.lake.RadiusAt(p.x, p.z) >= 1f) continue;
                for (int i = 0; i < 6; i++)
                {
                    var drop = new ParticleSystem.EmitParams
                    {
                        position = p + new Vector3(surface.Sample(-0.13f, 0.13f), i * 0.027f, surface.Sample(-0.13f, 0.13f)),
                        velocity = new Vector3(surface.Sample(-0.17f, 0.17f), surface.Sample(0.24f, 0.62f), surface.Sample(-0.17f, 0.17f)),
                        startLifetime = surface.Sample(1.6f, 2.75f),
                        startSize = surface.Sample(0.022f, 0.059f),
                        startColor = new Color(0.75f, 0.92f, 0.94f, surface.Sample(0.42f, 0.81f))
                    };
                    surface.bubbles.Emit(drop, 1);
                }
                break;
            }
        }

        void EmitContact(Vector3 position, Vector3 flow, float strength, bool step)
        {
            Contacts++;
            impacts[impactCursor] = new Vector4(position.x, position.z, Time.time, strength);
            impactCursor = (impactCursor + 1) % impacts.Length;
            impactCount = Mathf.Min(impactCount + 1, impacts.Length);
            material.SetInt(ImpactCountId, impactCount);
            material.SetVectorArray(ImpactsId, impacts);
            Sfx.At(step ? SoundBank.Splash : SoundBank.SwimSplash, position, 0.13f + 0.42f * strength,
                Mathf.Lerp(1.15f, 0.81f, strength) * Random.Range(0.95f, 1.05f), 1.3f, 24f);
            if (spray == null) return;

            int count = Mathf.RoundToInt(6f + strength * 25f);
            for (int i = 0; i < count; i++)
            {
                float angle = Sample(0f, 2f * Mathf.PI);
                float radius = Mathf.Sqrt(Sample(0f, 1f));
                Vector3 spread = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                float lift = Sample(0.60f, 1.85f) * (0.40f + strength);
                var drop = new ParticleSystem.EmitParams
                {
                    position = position + spread * (0.045f + strength * 0.09f),
                    velocity = Vector3.up * lift + (spread * 1.38f + flow * 0.48f) * (0.4f + strength),
                    startLifetime = Mathf.Lerp(0.24f, 0.70f, Mathf.InverseLerp(1f, 3.5f, lift)),
                    startSize = Sample(0.008f, 0.025f) * (0.6f + strength),
                    rotation = Sample(0f, 360f),
                    startColor = new Color(0.75f, 0.91f, 1f, Sample(0.35f, 0.79f))
                };
                spray.Emit(drop, 1);
            }
        }

        float Sample(float min, float max) => Mathf.Lerp(min, max, (float)sprayRandom.NextDouble());

        void LateUpdate()
        {
            swimmerCount = 0;
            if (Seats.Split)
            {
                foreach (var seat in Seats.All) AddLocal(seat.player);
            }
            else AddLocal(GameManager.I != null ? GameManager.I.player : null);
            if (CoopGame.I != null)
                foreach (var remote in CoopGame.I.Players.Values)
                    if (remote != null && remote.Shown && remote.last.alive && remote.last.swimming)
                        AddWake(remote.Feet, remote.last.heading, remote.last.speedH);

            if (swimmerCount != lastCount) material.SetInt(SwimmerCountId, swimmerCount);
            if (swimmerCount > 0) material.SetVectorArray(SwimmersId, swimmers);
            lastCount = swimmerCount;
        }

        void AddLocal(PlayerController player)
        {
            if (player != null && player.alive && player.Swimming)
                AddWake(player.transform.position, player.SwimYaw, player.SpeedH);
        }

        void AddWake(Vector3 position, float heading, float speed)
        {
            if (swimmerCount >= swimmers.Length || lake.RadiusAt(position.x, position.z) >= 1f) return;
            float strength = 0.18f + 0.82f * Mathf.Clamp01(speed / PlayerController.SwimSpeed);
            swimmers[swimmerCount++] = new Vector4(position.x, position.z, Mathf.Sin(heading) * strength, Mathf.Cos(heading) * strength);
        }

        void OnDestroy()
        {
            active.Remove(this);
            Destroy(mesh);
            Destroy(material);
            if (sprayMaterial != null) Destroy(sprayMaterial);
            if (bubbleMaterial != null) Destroy(bubbleMaterial);
            if (underwaterMaterial != null) Destroy(underwaterMaterial);
            if (underwaterMesh != null) Destroy(underwaterMesh);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes
{
    /// <summary>NavMesh costruita all'avvio dai collider della scena (modulo AI di Unity, niente pacchetti): i nemici
    /// a piedi aggirano scale, banco, colonne e divani e salgono lo scalone fino al soppalco; i droni seguono lo stesso
    /// percorso in volo. Qui anche il test di vista usato per colpire: niente danni attraverso muri e pavimenti.
    /// Nei piani procedurali la navmesh si ricostruisce a ogni piano (Rebuild).</summary>
    public static class HotelNav
    {
        public const int EnemyLayer = 9, DoorLayer = 10;
        public const float AgentRadius = 0.4f, AgentClimb = 0.4f, VoxelSize = 0.1f;
        /// <summary>Cio' che blocca vista e movimento dei nemici: tutto tranne il giocatore (Ignore Raycast) e i nemici.</summary>
        public static readonly int EnvMask = ~((1 << 2) | (1 << EnemyLayer));

        /// <summary>Aree della navmesh sotto le soglie che le serrande dei round chiudono: una per apertura (da 3 a 31, le prime due
        /// sono di Unity). Normalmente si cammina come ovunque; a serrande calate i nemici perdono quelle aree dalla maschera
        /// (EnemyMask) e non passano: gli agenti ignorano i collider, contano solo le aree. Cosi' non c'e' nessuna ricostruzione e
        /// nessun carving (con una tessera sola per piano costerebbe troppo), e chiudere una zona non tocca le soglie delle altre.</summary>
        public const int FirstDoorArea = 3, LastDoorArea = 31;
        public struct DoorBox { public Vector3 center, size; public float yaw; public int area; }
        /// <summary>Le soglie da marcare alla prossima costruzione della navmesh (le riempie RoundDirector prima di Rebuild; Rebuild le rilegge).</summary>
        public static readonly List<DoorBox> DoorBoxes = new List<DoorBox>();
        /// <summary>Aree percorribili da chi cammina e da chi vola (CalculatePath): tutte, tranne le soglie sigillate.</summary>
        public static int EnemyMask = NavMesh.AllAreas;
        public static void SealAreas(IEnumerable<int> areas)
        {
            int m = NavMesh.AllAreas;
            foreach (int a in areas) m &= ~(1 << a);
            EnemyMask = m;
        }
        public static void Unseal() { EnemyMask = NavMesh.AllAreas; }

        static NavMeshDataInstance instance;
        static NavMeshData data;
        public static bool Ready { get; private set; }

        public static void Build()
        {
            if (Ready) return;
            Build(new Bounds(new Vector3(0f, 5f, 0f), new Vector3(84f, 14f, 60f)), null);
        }

        /// <summary>Toglie la navmesh attuale e ne costruisce una nuova nei limiti dati. 'blockers' sono radici di arredi
        /// da trattare come ostacoli non camminabili.</summary>
        public static void Rebuild(Bounds bounds, List<Transform> blockers)
        {
            Clear();
            Build(bounds, blockers);
        }

        public static void Clear()
        {
            if (instance.valid) NavMesh.RemoveNavMeshData(instance);
            instance = default;
            Proc.Util.Kill(data);
            data = null;
            Ready = false;
        }

        static void Build(Bounds bounds, List<Transform> blockers)
        {
            float t0 = Time.realtimeSinceStartup;
            var sources = new List<NavMeshBuildSource>();
            // porte escluse: si aprono e si chiudono, il percorso deve passarci
            int mask = ~((1 << 2) | (1 << EnemyLayer) | (1 << DoorLayer));
            var markups = new List<NavMeshBuildMarkup>();
            if (blockers != null)
                foreach (var b in blockers)
                    if (b != null) markups.Add(new NavMeshBuildMarkup { root = b, area = 1, overrideArea = true, applyToChildren = true });
            NavMeshBuilder.CollectSources(bounds, mask, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            // le soglie delle serrande: la navmesh sotto di loro e' un'area a parte (non blocca nulla finche' nessuno la toglie dalla maschera)
            foreach (var d in DoorBoxes)
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.ModifierBox,
                    transform = Matrix4x4.TRS(d.center, Quaternion.Euler(0f, d.yaw, 0f), Vector3.one),
                    size = d.size,
                    area = d.area,
                });
            var s = NavMesh.GetSettingsByID(0);
            s.agentRadius = AgentRadius;
            s.agentHeight = 1.9f;
            s.agentClimb = AgentClimb;      // gradini da 0.35; tavolino (0.56) e base della gabbia (0.5) restano ostacoli
            s.agentSlope = 45f;
            s.overrideVoxelSize = true;
            s.voxelSize = VoxelSize;       // il default eredita il raggio dell'agente di progetto: perde i varchi stretti fra gli arredi
            s.overrideTileSize = true;     // tessere ampie: i bordi tra tessere lasciavano fasce non camminabili
            s.tileSize = 1024;
            data = NavMeshBuilder.BuildNavMeshData(s, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) { Debug.LogWarning("[PryingEyes] navmesh: costruzione fallita, nemici in linea retta"); return; }
            instance = NavMesh.AddNavMeshData(data);
            Ready = instance.valid;
            var tri = NavMesh.CalculateTriangulation();
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in tri.vertices) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
            Debug.Log($"[PryingEyes] navmesh: {sources.Count} collider, {tri.indices.Length / 3} triangoli, " +
                      $"quota {minY:F1}..{maxY:F1} m, {DoorBoxes.Count} soglie, {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
        }

        /// <summary>Linea libera tra due punti (muri, pavimenti, soppalco, vetri e porte bloccano).</summary>
        public static bool ClearLine(Vector3 a, Vector3 b) =>
            !Physics.Linecast(a, b, EnvMask, QueryTriggerInteraction.Ignore);

        /// <summary>Punto della navmesh sotto p (per chi vola: il pavimento sotto, non il soppalco sopra).</summary>
        public static bool Ground(Vector3 p, out Vector3 g)
        {
            g = p;
            if (!Physics.Raycast(p, Vector3.down, out RaycastHit hit, 15f, EnvMask, QueryTriggerInteraction.Ignore)) return false;
            if (!NavMesh.SamplePosition(hit.point, out NavMeshHit nh, 2.5f, NavMesh.AllAreas)) return false;
            g = nh.position;
            return true;
        }
    }
}

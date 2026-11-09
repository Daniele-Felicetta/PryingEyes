using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes
{
    /// <summary>NavMesh costruita all'avvio dai collider della scena (modulo AI di Unity, niente pacchetti): i nemici
    /// a piedi aggirano scale, banco, colonne e divani e salgono lo scalone fino al soppalco; i droni seguono lo stesso
    /// percorso in volo. Qui anche il test di vista usato per colpire: niente danni attraverso muri e pavimenti.</summary>
    public static class HotelNav
    {
        public const int EnemyLayer = 9, DoorLayer = 10;
        /// <summary>Cio' che blocca vista e movimento dei nemici: tutto tranne il giocatore (Ignore Raycast) e i nemici.</summary>
        public static readonly int EnvMask = ~((1 << 2) | (1 << EnemyLayer));

        static NavMeshDataInstance instance;
        public static bool Ready { get; private set; }

        public static void Build()
        {
            if (Ready) return;
            float t0 = Time.realtimeSinceStartup;
            var bounds = new Bounds(new Vector3(0f, 5f, 0f), new Vector3(84f, 14f, 60f));
            var sources = new List<NavMeshBuildSource>();
            // porte escluse: si aprono e si chiudono, il percorso deve passarci
            int mask = ~((1 << 2) | (1 << EnemyLayer) | (1 << DoorLayer));
            NavMeshBuilder.CollectSources(bounds, mask, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            var s = NavMesh.GetSettingsByID(0);
            s.agentRadius = 0.4f;
            s.agentHeight = 1.9f;
            s.agentClimb = 0.4f;      // gradini da 0.35; tavolino (0.56) e base della gabbia (0.5) restano ostacoli
            s.agentSlope = 45f;
            var data = NavMeshBuilder.BuildNavMeshData(s, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) { Debug.LogWarning("[PryingEyes] navmesh: costruzione fallita, nemici in linea retta"); return; }
            instance = NavMesh.AddNavMeshData(data);
            Ready = instance.valid;
            var tri = NavMesh.CalculateTriangulation();
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in tri.vertices) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
            Debug.Log($"[PryingEyes] navmesh: {sources.Count} collider, {tri.indices.Length / 3} triangoli, " +
                      $"quota {minY:F1}..{maxY:F1} m, {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
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

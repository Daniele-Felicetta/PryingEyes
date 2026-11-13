using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    public sealed partial class ArchitectureTest
    {
        // -architecturetest -architecturepursuit: veri agenti da round, in gruppo, senza Warp durante l'inseguimento.
        IEnumerator RunPursuit()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            Check("piano disponibile per l'inseguimento", FloorDirector.I != null && FloorDirector.I.cur != null);
            if (failed > 0) yield break;
            var fi = FloorDirector.I.cur;
            GalleryRoute route = null;
            foreach (var candidate in fi.galleries)
                foreach (var point in candidate.points)
                    if (point.y > 7 && candidate.room.GalleryLevels == 2) { route = candidate; break; }
            Check("atrio su tre quote per l'inseguimento", route != null);
            if (route == null) yield break;
            var player = GameManager.I.player;
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = false;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            player.invulnerable = true;
            Time.timeScale = 4; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            var path = new NavMeshPath();
            bool Has(string flag) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), flag) >= 0;
            bool pressure = Has("-pursuitpressure");
            var types = new[] { Enemy.EnemyType.Monaco, Enemy.EnemyType.Abate, Enemy.EnemyType.Portachiavi,
                Enemy.EnemyType.Maschera, Enemy.EnemyType.Infermiere, Enemy.EnemyType.Primario, Enemy.EnemyType.Monaco, Enemy.EnemyType.Abate };
            for (int direction = 0; direction < 2; direction++)
            {
                Random.InitState(51000 + direction);
                string label = direction == 0 ? "discesa" : "salita";
                var points = route.points;
                Vector3 destination = direction == 0 ? points[0] : points[6];
                Vector3 startA = direction == 0 ? points[6] : points[0];
                Vector3 startB = direction == 0 ? points[7] : points[points.Length - 1];
                player.Teleport(destination); player.hp = 100; player.shield = 50;
                yield return new WaitForSeconds(0.3f);
                var group = new List<Enemy>();
                for (int attempt = 0; attempt < 24 && group.Count < types.Length; attempt++)
                {
                    Vector3 position = Vector3.Lerp(startA, startB, attempt / 23f);
                    if (!NavMesh.SamplePosition(position, out var sample, 0.9f, HotelNav.EnemyMask) || Mathf.Abs(sample.position.y - startA.y) > 0.5f) continue;
                    position = sample.position;
                    bool free = !Physics.CheckCapsule(position + Vector3.up * 0.5f, position + Vector3.up * 2.3f, 0.4f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
                    foreach (var other in group) if (Vector3.Distance(other.transform.position, position) < 1.6f) free = false;
                    if (!free) continue;
                    bool connected = NavMesh.CalculatePath(position, player.transform.position, HotelNav.EnemyMask, path) && path.status == NavMeshPathStatus.PathComplete;
                    Check(label + " percorso iniziale " + types[group.Count], connected);
                    var enemy = Enemy.Spawn(types[group.Count], position);
                    enemy.MakeRelentless(); group.Add(enemy);
                }
                Check(label + " gruppo completo di otto nemici", group.Count == types.Length);
                var arrived = new bool[group.Count]; var attacked = new bool[group.Count];
                var minHeight = new float[group.Count]; var maxHeight = new float[group.Count];
                var previousHeight = new float[group.Count]; var middle = new bool[group.Count];
                var validAgent = new bool[group.Count]; var maxJump = new float[group.Count];
                var finished = new bool[group.Count]; var lastPosition = new Vector3[group.Count];
                for (int i = 0; i < group.Count; i++) minHeight[i] = maxHeight[i] = group[i].transform.position.y;
                for (int i = 0; i < group.Count; i++) { previousHeight[i] = minHeight[i]; validAgent[i] = true; }
                float began = Time.time;
                Vector3 playerStart = player.transform.position;
                float playerMove = 0; bool driftReported = false;
                deadline = Time.time + (pressure ? 120 : 180);
                while (Time.time < deadline)
                {
                    bool complete = group.Count > 0;
                    playerMove = Mathf.Max(playerMove, Vector3.Distance(playerStart, player.transform.position));
                    if (!driftReported && playerMove > 0.5f)
                    {
                        driftReported = true;
                        Debug.Log($"[PryingEyes] architecturepursuit giocatore {label}: tempo={Time.time - began:F1} partenza={playerStart:F2} ora={player.transform.position:F2} destinazione={destination:F2}");
                    }
                    for (int i = 0; i < group.Count; i++)
                    {
                        var enemy = group[i];
                        if (finished[i]) continue;
                        Vector3 p = enemy.transform.position, delta = player.transform.position - p;
                        lastPosition[i] = p;
                        minHeight[i] = Mathf.Min(minHeight[i], p.y); maxHeight[i] = Mathf.Max(maxHeight[i], p.y);
                        middle[i] |= Mathf.Abs(p.y - 3.6f) < 0.35f;
                        maxJump[i] = Mathf.Max(maxJump[i], Mathf.Abs(p.y - previousHeight[i])); previousHeight[i] = p.y;
                        var agent = enemy.GetComponent<NavMeshAgent>();
                        validAgent[i] &= agent != null && agent.isOnNavMesh;
                        arrived[i] |= Mathf.Abs(delta.y) < 0.65f && new Vector2(delta.x, delta.z).magnitude < 3.2f;
                        attacked[i] |= enemy.PreparingMelee;
                        // Nella prova di percorso libera chi e' arrivato e prepara il colpo, come liberare la prima fila.
                        // La prova -pursuitpressure li tiene invece tutti vivi per l'intero assedio.
                        if (!pressure && arrived[i] && attacked[i]) { finished[i] = true; Enemy.Remove(enemy); }
                        complete &= finished[i];
                    }
                    if (!pressure && complete) break;
                    yield return null;
                }
                bool groupAttacked = false;
                for (int i = 0; i < group.Count; i++)
                {
                    var enemy = group[i];
                    if (!pressure)
                    {
                        Check(label + " " + types[i] + " raggiunge il giocatore e prepara il colpo", arrived[i] && attacked[i]);
                        Check(label + " " + types[i] + " attraversa entrambi i piani", minHeight[i] < 0.8f && maxHeight[i] > 6.8f);
                    }
                    Check(label + " " + types[i] + " segue le scale senza salti o cadute", middle[i] && validAgent[i] && maxJump[i] < 0.65f);
                    groupAttacked |= attacked[i];
                    Debug.Log($"[PryingEyes] architecturepursuit {label} {types[i]} tempo={Time.time - began:F1} pos={lastPosition[i]:F2} quote={minHeight[i]:F2}..{maxHeight[i]:F2} salto={maxJump[i]:F2} attacco={attacked[i]}");
                    if (enemy != null) Enemy.Remove(enemy);
                }
                Check(label + " il gruppo attacca dopo l'inseguimento", groupAttacked);
                Check(label + " non spinge il giocatore fermo fuori dalla posizione", playerMove < 0.25f);
                Debug.Log($"[PryingEyes] architecturepursuit giocatore {label}: spostamento massimo={playerMove:F2} m posizione finale={player.transform.position:F2}");
                yield return null;
            }
        }
    }

}

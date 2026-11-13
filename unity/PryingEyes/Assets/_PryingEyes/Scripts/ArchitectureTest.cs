using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -architecturetest: percorre le balconate senza recuperi o teletrasporti fra le tappe.</summary>
    public sealed class ArchitectureTest : MonoBehaviour
    {
        int checks, failed;
        bool shots;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-architecturetest") >= 0)
                new GameObject("ArchitectureTest").AddComponent<ArchitectureTest>();
        }
        void Check(string name, bool ok)
        {
            checks++; if (!ok) failed++;
            Debug.Log("[PryingEyes] architecturetest " + (ok ? "ok " : "FALLITO ") + name);
        }
        IEnumerator Start()
        {
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-architectureshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more;
                try { more = sequence.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break;
                yield return sequence.Current;
            }
            PlayerController.botDir = Vector3.zero;
            Debug.Log($"[PryingEyes] architecturetest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }
        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            var fi = FloorDirector.I.cur; var player = GameManager.I.player;
            Check("balconate generate", fi.galleries.Count > 0);
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = true;
            player.invulnerable = true; PlayerController.holdLook = false;
            var path = new NavMeshPath();
            Time.timeScale = 2f;
            string output = Path.GetFullPath("unity_logs/architecture"); Directory.CreateDirectory(output);
            for (int gallery = 0; gallery < Mathf.Min(2, fi.galleries.Count); gallery++)
            {
                var route = fi.galleries[gallery];
                var points = route.points;
                Debug.Log($"[PryingEyes] architecturetest sala {route.room.id} {route.room.kind}: {route.room.W * 0.1f:F1}x{route.room.D * 0.1f:F1} m, altezza {route.room.wallH:F1}, quota superiore {FloorBuilder.GalleryHeight:F1}");
                player.Teleport(points[0]);
                yield return new WaitForSeconds(0.3f);
                for (int leg = 1; leg < points.Length; leg++)
                {
                    bool sample = NavMesh.SamplePosition(points[leg], out var target, 0.6f, NavMesh.AllAreas);
                    bool from = NavMesh.SamplePosition(player.transform.position, out var origin, 0.6f, NavMesh.AllAreas);
                    bool connected = sample && from && NavMesh.CalculatePath(origin.position, target.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    Check($"sala {route.room.id} tappa {leg} collegata per i nemici", connected);
                    if (!connected) break;
                    float until = Time.time + 30f, repath = 0f;
                    while (Time.time < until && Vector3.Distance(player.transform.position, target.position) > 0.4f)
                    {
                        if (Time.time >= repath)
                        {
                            repath = Time.time + 0.2f;
                            if (NavMesh.SamplePosition(player.transform.position, out origin, 0.6f, NavMesh.AllAreas))
                                NavMesh.CalculatePath(origin.position, target.position, NavMesh.AllAreas, path);
                        }
                        Vector3 next = target.position;
                        if (path.corners.Length > 1)
                        {
                            next = path.corners[1];
                            if (Vector3.Distance(next, player.transform.position) < 0.4f && path.corners.Length > 2) next = path.corners[2];
                        }
                        var direction = next - player.transform.position; direction.y = 0;
                        PlayerController.botDir = direction.normalized;
                        yield return null;
                    }
                    PlayerController.botDir = Vector3.zero;
                    bool reached = Vector3.Distance(player.transform.position, target.position) <= 0.4f;
                    Check($"sala {route.room.id} tappa {leg} camminata reale", reached);
                    if (!reached)
                    {
                        Debug.Log($"[PryingEyes] architecturetest fermo a {player.transform.position}, atteso {target.position}");
                        break;
                    }
                }
                if (shots)
                {
                    player.enabled = false;
                    var cam = player.cam.GetComponent<Camera>(); cam.fieldOfView = 70f;
                    Vector3 center = new Vector3(route.room.CenterX, 2.8f, route.room.CenterZ);
                    cam.transform.SetPositionAndRotation(points[0] + new Vector3(-0.2f, 1.62f, 0.2f), Quaternion.LookRotation(center - points[0] - Vector3.up * 1.62f));
                    GameManager.CamShot(Path.Combine(output, "sala_" + route.room.id + "_terra.png"), cam, true);
                    cam.transform.SetPositionAndRotation(points[2] + Vector3.up * 1.62f, Quaternion.LookRotation(new Vector3(route.room.CenterX, 1f, route.room.CenterZ) - points[2] - Vector3.up * 1.62f));
                    GameManager.CamShot(Path.Combine(output, "sala_" + route.room.id + "_balconata.png"), cam, true);
                    player.enabled = true;
                }
            }
        }
    }
}

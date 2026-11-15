#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -lampadaritest [-lampadarishots]: i lampadari che crollano (Proc/Chandeliers.cs).
    /// Il piano ha lampadari abbattibili; un colpo da 30 non basta, il secondo lo fa cadere: si spegne, la catena si spezza, tocca
    /// terra in poco piu' di mezzo secondo e schiaccia i tre monaci che ci stavano sotto (le uccisioni pagano), non quello appena
    /// fuori dal raggio ne' chi gioca lontano; a terra fa da ostacolo (scava la navmesh) e non si colpisce piu'.</summary>
    public sealed class LampadariTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-lampadaritest") >= 0) new GameObject("LampadariTest").AddComponent<LampadariTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] lampadaritest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-lampadarishots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] lampadaritest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void View(string name)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/lampadari"); Directory.CreateDirectory(folder);
            LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, true);
        }

        static void Face(PlayerController player, Vector3 target)
        {
            Vector3 d = target - player.cam.position;
            player.yaw = Mathf.Atan2(d.x, d.z);
            player.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
        }

        static bool OnLevel(Vector3 at, float y, out Vector3 p)
        {
            p = at;
            if (!NavMesh.SamplePosition(at, out var nh, 0.5f, NavMesh.AllAreas) || Mathf.Abs(nh.position.y - y) > 0.35f) return false;
            p = nh.position;
            return true;
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            var cs = Chandeliers.I;
            Check($"ci sono lampadari da abbattere ({(cs != null ? cs.List.Count : 0)})", cs != null && cs.List.Count > 0);
            if (failed > 0) yield break;

            // il lampadario con piu' pavimento libero sotto, e da dove guardarlo
            Chandelier ch = null; int bestFree = -1; Vector3 stand = Vector3.zero; float floorY = 0f;
            int shown = 0;
            foreach (var c in cs.List)
            {
                // la quota del pavimento sotto l'impatto (il lampadario puo' cadere sulle panche o sulla fonte)
                Vector3 ip = c.ImpactPoint;
                float fy = HotelNav.Ground(ip + Vector3.up * 0.6f, out Vector3 gq) ? gq.y : ip.y;
                int free = 0;
                for (int k = 0; k < 8; k++)
                    if (OnLevel(new Vector3(ip.x, fy, ip.z) + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * c.Radius * 0.45f, fy, out _)) free++;
                bool seen = false;
                if (free > bestFree)
                {
                    Vector3 at = c.GetComponent<SphereCollider>().bounds.center;
                    for (int a = 0; a < 12 && !seen; a++)
                        foreach (float d in new[] { 7f, 8.5f, 6f, 10f })
                        {
                            Vector3 q = new Vector3(ip.x, fy, ip.z) + Quaternion.Euler(0f, a * 30f, 0f) * Vector3.forward * d;
                            if (!OnLevel(q, fy, out Vector3 qp)) continue;
                            // la vista arriva al lampadario: il primo ostacolo e' lui (la sua sfera) o niente
                            if (Physics.Linecast(qp + Vector3.up * 1.6f, at, out var lh, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) &&
                                lh.collider.GetComponentInParent<Chandelier>() != c) continue;
                            ch = c; bestFree = free; stand = qp; floorY = fy; seen = true;
                            break;
                        }
                }
                if (shown++ < 8) Debug.Log($"[PryingEyes] lampadaritest lampadario {c.index}: impatto {ip:F1}, pavimento {fy:F2}, raggio {c.Radius:F1}, liberi {free}/8, visto {seen}");
            }
            Check("uno si vede da lontano, con pavimento sotto", ch != null && bestFree >= 3);
            if (failed > 0) yield break;
            Vector3 impact = ch.ImpactPoint;
            Vector3 target = ch.GetComponent<SphereCollider>().bounds.center;
            float chain0 = ch.Chain != null ? ch.Chain.lossyScale.y : 0f;
            player.Teleport(stand + Vector3.up * 0.05f); player.hp = 100; player.shield = 50; player.invulnerable = false;
            yield return null;
            Face(player, target);
            Enemy.debugFreeze = true;
            var under = new List<Enemy>();
            for (int k = 0; k < 12 && under.Count < 3; k++)
            {
                Vector3 q = new Vector3(impact.x, floorY, impact.z) + Quaternion.Euler(0f, k * 120f + (k / 3) * 40f, 0f) * Vector3.forward * ch.Radius * (0.3f + (k / 3) * 0.12f);
                if (OnLevel(q, floorY, out Vector3 qp)) under.Add(Enemy.Spawn(Enemy.EnemyType.Monaco, qp));
            }
            Enemy safe = null;
            for (int k = 0; k < 12 && safe == null; k++)
            {
                Vector3 q = new Vector3(impact.x, floorY, impact.z) + Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward * (ch.Radius + 2.2f);
                if (OnLevel(q, floorY, out Vector3 qp) && Vector3.Distance(qp, stand) > 2f) safe = Enemy.Spawn(Enemy.EnemyType.Monaco, qp);
            }
            Check($"tre monaci sotto e uno appena fuori ({under.Count}, {(safe != null ? 1 : 0)})", under.Count == 3 && safe != null);
            yield return new WaitForSeconds(0.4f);
            View("prima");
            int score0 = gm.score;
            float health0 = player.hp + player.shield;
            Vector3 dir = (target - player.cam.position).normalized;
            ch.Shot(30f, target, dir);
            Check("un colpo da 30 non basta", !ch.Falling && !ch.Fallen);
            ch.Shot(35f, target, dir);
            yield return null;
            Check("il secondo lo fa cadere", ch.Falling || ch.Fallen);
            bool dark = true;
            foreach (var l in ch.Lights) if (l.wanted || (l.l != null && l.l.enabled)) dark = false;
            Check($"si spegne ({ch.Lights.Count} luci)", dark);
            if (ch.Chain != null) Check("la catena si spezza", ch.Chain.lossyScale.y < chain0 * 0.5f);
            yield return new WaitForSeconds(0.1f);
            View("caduta");
            yield return new WaitForSeconds(1.3f);
            View("dopo");
            float bottom = float.MaxValue;
            foreach (var r in ch.GetComponentsInChildren<Renderer>()) bottom = Mathf.Min(bottom, r.bounds.min.y);
            Check($"e' a terra (fondo a {bottom - impact.y:F2} m dal punto d'impatto)", ch.Fallen && Mathf.Abs(bottom - impact.y) < 0.35f);
            int dead = under.FindAll(e => e == null || e.IsDead).Count;
            Check($"schiaccia i monaci che stavano sotto ({dead}/3)", dead == 3);
            Check("quello appena fuori dal raggio resta in piedi", safe != null && !safe.IsDead);
            Check("chi gioca lontano resta illeso", Mathf.Abs(player.hp + player.shield - health0) < 0.01f);
            Check($"le uccisioni pagano ({gm.score - score0} punti)", gm.score > score0);
            var ob = ch.GetComponent<NavMeshObstacle>();
            Check("a terra fa da ostacolo (scava la navmesh)", ob != null && ob.carving && ob.enabled);
            ch.Shot(500f, target, dir);
            yield return null;
            Check("a terra non si colpisce piu'", ch.Fallen && !ch.Falling);
            Enemy.debugFreeze = false;
        }
    }
}
#endif

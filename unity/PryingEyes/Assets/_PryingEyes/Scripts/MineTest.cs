#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -minetest [-mineshots]: la mina del reliquiario (Mine.cs). Si parte con una; posata, finisce
    /// un metro davanti sul pavimento e se ne ha una in meno; si arma in 1,2 s; un monaco a 4 m non la fa scattare, uno a un metro si';
    /// lo scoppio lo uccide, chi gioca accanto resta illeso, la mina sparisce; senza mine il tasto non fa niente; l'HUD le conta.</summary>
    public sealed class MineTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-minetest") >= 0) new GameObject("MineTest").AddComponent<MineTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] minetest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-mineshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] minetest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void View(string name)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/mine"); Directory.CreateDirectory(folder);
            LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, true);
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("c'e' spazio per la prova", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            if (failed > 0) yield break;
            var eq = Equipment.Ensure(player);
            Check($"si parte con una mina ({eq.mines})", eq.mines == 1);
            player.Teleport(arena - forward * 1.0f + Vector3.up * 0.05f); player.hp = 100; player.shield = 50; player.invulnerable = false;
            player.yaw = Mathf.Atan2(forward.x, forward.z); player.pitch = 0.5f;
            yield return null; yield return null;
            var mine = eq.PlaceMine();
            yield return null;
            Vector3 mp = mine != null ? mine.transform.position : Vector3.zero;
            float ahead = Vector3.Dot(mp - player.transform.position, forward);
            Check($"posata un metro davanti, sul pavimento ({ahead:F2} m, quota {mp.y - arena.y:F2})", mine != null && ahead > 0.6f && ahead < 1.4f && Mathf.Abs(mp.y - arena.y) < 0.15f);
            Check($"ne resta una in meno ({eq.mines})", eq.mines == 0);
            Check("senza mine il tasto non ne posa altre", eq.PlaceMine() == null && Mine.Placed.Count == 1);
            yield return new WaitForSeconds(0.3f);
            Check("non e' ancora armata", mine != null && !mine.Armed);
            View("posata");
            yield return new WaitForSeconds(Mine.ArmTime);
            Check("dopo 1,2 s e' armata", mine != null && mine.Armed && !mine.Triggered);
            // un monaco lontano non la fa scattare, uno vicino si'
            Enemy.debugFreeze = true;
            Vector3 farP = mp + forward * 4f;
            if (NavMesh.SamplePosition(farP, out var fh, 0.6f, NavMesh.AllAreas)) farP = fh.position;
            var far = Enemy.Spawn(Enemy.EnemyType.Monaco, farP);
            yield return new WaitForSeconds(0.5f);
            Check("un monaco a 4 m non la fa scattare", mine != null && !mine.Triggered);
            Vector3 nearP = mp + forward * 1.0f;
            if (NavMesh.SamplePosition(nearP, out var nh, 0.6f, NavMesh.AllAreas)) nearP = nh.position;
            var near = Enemy.Spawn(Enemy.EnemyType.Monaco, nearP);
            float health = player.hp + player.shield;
            float t0 = Time.time;
            while (mine != null && Time.time - t0 < 1.5f) yield return null;
            Check($"uno a un metro la fa scoppiare ({Time.time - t0:F2} s)", mine == null && Time.time - t0 < 0.8f);
            yield return new WaitForSeconds(0.1f);
            View("scoppio");
            Check("lo scoppio lo uccide", near == null || near.IsDead);
            Check("chi gioca accanto resta illeso", Mathf.Abs(player.hp + player.shield - health) < 0.01f);
            Enemy.debugFreeze = false;
        }
    }
}
#endif

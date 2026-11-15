#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -traptest [-trapshots] -seed S -floor F: le trappole delle soglie sul piano vero.
    /// Ci sono e stanno su porte vere; chiuse finche' la porta e' chiusa; con la porta aperta la leva costa 1000 punti e accende;
    /// i monaci che attraversano la soglia muoiono e le uccisioni pagano; chi gioca e ci entra si scotta; finita la durata va in
    /// ricarica (la leva rifiuta e non prende punti), poi torna pronta. Foto della trappola accesa e della leva con -trapshots.</summary>
    public sealed class TrapTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-traptest") >= 0) new GameObject("TrapTest").AddComponent<TrapTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] traptest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-trapshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            PlayerController.botDir = Vector3.zero;
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] traptest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void View(string name)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/trappole"); Directory.CreateDirectory(folder);
            LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, true);
        }

        static void Face(PlayerController player, Vector3 target)
        {
            Vector3 d = target - player.cam.position;
            player.yaw = Mathf.Atan2(d.x, d.z);
            player.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);       // radianti, positivo = in basso
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            var traps = DoorTraps.I;
            Check("le trappole delle soglie ci sono", traps != null && traps.Traps.Count > 0);
            if (failed > 0) yield break;
            foreach (var t in traps.Traps)
                Check($"trappola {t.index} ({t.Name}) su una porta vera con almeno una leva", t.door != null && t.Levers.Count > 0);
            var trap = traps.Traps[0];
            Check("con la porta chiusa la trappola non si usa", !trap.Available || RoomAccessDoors.I.Access.IsOpen(trap.door.Opening.a) && RoomAccessDoors.I.Access.IsOpen(trap.door.Opening.b));
            // si aprono tutte le aree, come dopo averle comprate
            var all = new List<int>();
            for (int i = 0; i < FloorDirector.I.cur.plan.rooms.Count; i++) all.Add(i);
            RoomAccessDoors.I.ApplySnapshot(all, true);
            yield return null;
            Check("aperta la porta, la trappola e' disponibile", trap.Available);
            var lever = trap.Levers[0];
            Vector3 normal = -lever.transform.forward;
            Vector3 stand = lever.transform.position + normal * 1.3f;
            if (HotelNav.Ground(stand + Vector3.up, out Vector3 g)) stand = g;
            player.Teleport(stand + Vector3.up * 0.05f); player.hp = 100; player.shield = 50; player.invulnerable = false;
            yield return null;
            Face(player, lever.Point);
            yield return null;
            Check($"la leva propone l'accensione ({lever.Prompt})", lever.Prompt != null && lever.Prompt.Contains("1000"));
            if (shots) { yield return new WaitForSeconds(0.2f); View("leva_" + trap.Name.ToLower()); }
            gm.score = 3000;
            lever.Use();
            yield return null;
            Check("la leva accende la trappola", trap.state == 1);
            Check("la trappola costa 1000 punti", gm.score == 2000);
            // monaci da una parte della soglia, il giocatore dall'altra: per arrivare devono passarci
            Vector3 c = trap.Center;
            Vector3 across = trap.door.Shutter.alongX ? Vector3.forward : Vector3.right;
            Vector3 sideA = c + across * 3.2f, sideB = c - across * 3.2f;
            bool aOk = HotelNav.Ground(sideA + Vector3.up, out Vector3 ga), bOk = HotelNav.Ground(sideB + Vector3.up, out Vector3 gb);
            Check("ai due lati della soglia c'e' pavimento", aOk && bOk);
            player.Teleport(gb + Vector3.up * 0.05f);
            yield return null;
            Face(player, c + Vector3.up * 1.2f);
            var monks = new List<Enemy>();
            for (int i = 0; i < 3; i++)
            {
                var e = Enemy.Spawn(Enemy.EnemyType.Monaco, ga + Vector3.Cross(Vector3.up, across) * (i - 1) * 0.9f);
                e.MakeRelentless(); monks.Add(e);
            }
            int score0 = gm.score;
            if (shots) { yield return new WaitForSeconds(0.6f); View("accesa_" + trap.Name.ToLower()); }
            deadline = Time.time + 12f;
            while (Time.time < deadline && monks.Exists(m => m != null && !m.IsDead)) yield return null;
            int dead = monks.FindAll(m => m == null || m.IsDead).Count;
            Check($"i monaci che passano la soglia muoiono ({dead}/3)", dead == 3);
            Check("le uccisioni della trappola pagano", gm.score > score0);
            // chi gioca dentro la soglia si scotta
            player.Teleport(c + Vector3.up * 0.05f); player.hp = 100; player.shield = 50;
            float health = player.hp + player.shield;
            yield return new WaitForSeconds(0.7f);
            Check($"chi entra nella soglia accesa si scotta ({health:F0} -> {player.hp + player.shield:F0})", player.hp + player.shield < health - 5f);
            player.Teleport(stand + Vector3.up * 0.05f); player.hp = 100; player.shield = 50;
            yield return null;
            // fine della durata: ricarica, la leva rifiuta senza prendere punti, poi torna pronta
            trap.timer = 0.02f;
            yield return new WaitForSeconds(0.15f);            // in batchmode i fotogrammi durano meno di un millesimo
            Check("finita la durata la trappola va in ricarica", trap.state == 2);
            int score1 = gm.score;
            lever.Use();
            yield return null;
            Check("in ricarica la leva rifiuta e non prende punti", trap.state == 2 && gm.score == score1);
            trap.timer = 0.02f;
            yield return new WaitForSeconds(0.15f);            // in batchmode i fotogrammi durano meno di un millesimo
            Check("finita la ricarica la trappola torna pronta", trap.state == 0);
            // la seconda trappola, se c'e' (di solito dell'altro tipo): si accende e si fotografa
            if (traps.Traps.Count > 1)
            {
                var t2 = traps.Traps[1];
                var l2 = t2.Levers[0];
                player.Teleport((l2.transform.position - l2.transform.forward * 1.3f) + Vector3.up * 0.05f);
                yield return null;
                Face(player, t2.Center + Vector3.up * 1.2f);
                gm.score = 2000;
                l2.Use();
                yield return null;
                Check($"anche la trappola {t2.index} ({t2.Name}) si accende", t2.state == 1);
                if (shots) { yield return new WaitForSeconds(0.5f); View("accesa_" + t2.Name.ToLower()); }
            }
            Enemy.ClearAll();
        }
    }
}
#endif

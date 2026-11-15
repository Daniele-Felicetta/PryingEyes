#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -equiptest [-equipshots]: equipaggiamento e Prove col giocatore vero.
    /// Acquasanta: il lancio consuma la granata e la scaglia avanti; esplosa in mezzo a tre monaci li uccide e paga, chi l'ha lanciata a
    /// 6 m resta illeso. Carillon: posato suona, i monaci lasciano il giocatore e vanno da lui, poi esplode. Rifornimento a ogni ondata.
    /// Prova: tre uccisioni alla testa la superano e pagano; un colpo azzera la prova Intatto.</summary>
    public sealed class EquipTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;
        Vector3 arena, forward;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-equiptest") >= 0) new GameObject("EquipTest").AddComponent<EquipTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] equiptest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-equipshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            PlayerController.botDir = Vector3.zero;
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] equiptest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void View(string name)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/equipaggiamento"); Directory.CreateDirectory(folder);
            LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, true);
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("spazio di prova libero sulla navmesh reale", MeleeTest.FindArena(out arena, out forward));
            if (failed > 0) yield break;
            var eq = Equipment.I;
            Check("il giocatore ha l'equipaggiamento", eq != null);
            if (eq == null) yield break;
            if (!Torch.I.on) Torch.I.Toggle();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            void Place()
            {
                Enemy.ClearAll(); Fx.Clear();
                foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None)) Destroy(e.gameObject);
                player.Teleport(arena + Vector3.up * 0.05f); player.hp = 100; player.shield = 50; player.invulnerable = false;
                player.yaw = Mathf.Atan2(forward.x, forward.z); player.pitch = 0.05f;
            }
            var cc = player.GetComponent<CharacterController>();

            // ---------------- acquasanta: il lancio
            Place();
            yield return null;
            eq.grenades = 2;
            eq.Throw(0);
            yield return null;
            var flying = FindObjectsByType<Thrown>(FindObjectsSortMode.None);
            Check("il lancio consuma un'acquasanta", eq.grenades == 1);
            Check("l'ampolla vola", flying.Length == 1);
            if (flying.Length > 0)
            {
                Vector3 p0 = flying[0].transform.position;
                yield return new WaitForSeconds(0.35f);
                Check("l'ampolla va avanti", flying[0] != null && Vector3.Dot(flying[0].transform.position - p0, forward) > 1.5f);
                if (shots) View("acquasanta_lancio");
            }
            yield return new WaitForSeconds(2.2f);

            // ---------------- acquasanta in mezzo a tre monaci
            Place();
            yield return null;
            Enemy.debugFreeze = true;
            Vector3 heap = arena + forward * 6f;
            var monks = new List<Enemy>();
            for (int i = 0; i < 3; i++) monks.Add(Enemy.Spawn(Enemy.EnemyType.Monaco, heap + right * (i - 1) * 1.1f));
            yield return null;
            int score0 = gm.score; float health = player.hp + player.shield;
            Thrown.Spawn(0, heap + Vector3.up * 0.3f, Vector3.zero, cc);
            yield return new WaitForSeconds(1.9f);
            if (shots) { yield return new WaitForSeconds(0.12f); View("acquasanta_esplosione"); }
            yield return new WaitForSeconds(0.5f);
            int dead = monks.FindAll(m => m == null || m.IsDead).Count;
            Check($"l'acquasanta uccide i monaci vicini ({dead}/3)", dead == 3);
            Check("le uccisioni dell'acquasanta pagano", gm.score > score0);
            Check("chi l'ha lanciata a 6 m resta illeso", Mathf.Abs(player.hp + player.shield - health) < 0.01f);
            Enemy.debugFreeze = false;

            // ---------------- carillon: attira, poi esplode
            Place();
            yield return null;
            Vector3 box = arena + forward * 4f + right * 4.5f;
            if (!HotelNav.Ground(box + Vector3.up, out Vector3 gbox)) gbox = arena + forward * 5f;
            monks.Clear();
            for (int i = 0; i < 3; i++)
            {
                var m = Enemy.Spawn(Enemy.EnemyType.Monaco, arena + forward * 11f + right * (i - 1) * 1.2f);
                m.MakeRelentless(); monks.Add(m);
            }
            Thrown.Spawn(1, gbox + Vector3.up * 0.4f, Vector3.zero, cc);
            yield return new WaitForSeconds(1.0f);
            float d0 = 0f; foreach (var m in monks) if (m != null) d0 += Flat(m.transform.position, gbox);
            yield return new WaitForSeconds(3.0f);
            float d1 = 0f; foreach (var m in monks) if (m != null) d1 += Flat(m.transform.position, gbox);
            float toPlayer = 99f; foreach (var m in monks) if (m != null) toPlayer = Mathf.Min(toPlayer, Flat(m.transform.position, player.transform.position));
            Check($"i monaci vanno dal carillon (somma delle distanze {d0:F1} -> {d1:F1} m)", d1 < d0 - 4f);
            Check($"e lasciano stare il giocatore (il piu' vicino a {toPlayer:F1} m)", toPlayer > 2.2f);
            if (shots) View("carillon_attira");
            health = player.hp + player.shield;
            yield return new WaitForSeconds(4.0f);
            dead = monks.FindAll(m => m == null || m.IsDead).Count;
            Check($"il carillon esplode e uccide chi ha attirato ({dead}/3)", dead >= 2);
            Check("finito il carillon non ne resta traccia", FindObjectsByType<Thrown>(FindObjectsSortMode.None).Length == 0);

            // ---------------- rifornimento a ogni ondata
            eq.grenades = 1; eq.lures = 0;
            int r0 = RoundDirector.Round;
            RoundDirector.Round = r0 + 3;
            yield return null;
            Check($"tre ondate riforniscono ({eq.grenades} acquasante, {eq.lures} carillon)", eq.grenades == Equipment.MaxGrenades && eq.lures == 1);
            RoundDirector.Round = r0;

            // ---------------- Prove
            var prove = Prove.I;
            Check("le Prove ci sono", prove != null);
            if (prove != null)
            {
                prove.Begin(Prove.Kind.Testa, 3);
                int done0 = Prove.completed, score1 = gm.score;
                yield return null;
                for (int i = 0; i < 3; i++) gm.CreditKill(DamageSource.Gun, true);
                yield return null;
                Check("tre uccisioni alla testa superano la prova", Prove.completed == done0 + 1 && !prove.Active);
                Check("la prova superata paga", gm.score >= score1 + 3 * Punti.Testa + 400);
                prove.Begin(Prove.Kind.Intatto, 40);
                yield return new WaitForSeconds(1.2f);
                Check("Intatto conta i secondi", prove.progress >= 1);
                player.Hurt(1f, player.transform.position + forward);
                yield return null;
                Check("un colpo azzera la prova Intatto", prove.progress == 0);
                if (shots) View("prova_hud");
            }
            Enemy.ClearAll();
        }
    }
}
#endif

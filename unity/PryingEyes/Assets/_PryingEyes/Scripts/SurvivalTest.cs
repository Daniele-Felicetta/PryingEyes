using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -survivaltest [-seed N]: il ciclo a ondate della Torre da soli. La prima ondata parte da sola, nasce solo nelle aree
    /// aperte (e solo in quelle comprate dopo), mai addosso al giocatore e col tetto dei vivi; il premio arriva una volta; tre ondate aprono
    /// l'ascensore; la morte annulla l'ondata e rimette il numero; la quinta e' la Processione dei Lumi (solo Veglie, piu' numerose, regalo).
    /// I nemici li uccide la prova (senza punti: il premio si misura da solo).</summary>
    public sealed class SurvivalTest : MonoBehaviour
    {
        int checks, failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-survivaltest") >= 0)
                new GameObject("SurvivalTest").AddComponent<SurvivalTest>();
        }

        void Check(string name, bool ok, string detail = "")
        {
            checks++; if (!ok) failed++;
            Debug.Log("[PryingEyes] survivaltest " + (ok ? "ok " : "FALLITO ") + name + (detail.Length > 0 ? " (" + detail + ")" : ""));
        }

        IEnumerator Start()
        {
            var sequence = Run();
            while (true)
            {
                bool more;
                try { more = sequence.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break;
                yield return sequence.Current;
            }
            Time.timeScale = 1f;
            Debug.Log($"[PryingEyes] survivaltest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        static int Alive()
        {
            int n = 0;
            foreach (var e in Enemy.All) if (e != null && !e.IsDead) n++;
            return n;
        }

        static void KillAll()
        {
            foreach (var e in new List<Enemy>(Enemy.All))
                if (e != null && !e.IsDead) e.Damage(99999f, e.transform.position + Vector3.up, false, false);
        }

        readonly Dictionary<int, int> spawnsPerRoom = new Dictionary<int, int>();
        float waveDelay;

        /// <summary>Aspetta e gioca un'ondata uccidendo tutti i nemici appena nascono (ogni 0,4 s). Controlla le regole di nascita.</summary>
        IEnumerator PlayWave(int round, bool moth, int expectedTarget, int expectedCap)
        {
            var rd = RoundDirector.I; var fd = FloorDirector.I; var gm = GameManager.I; var access = RoomAccessDoors.I;
            string tag = $"ondata {round}: ";
            float t0 = Time.time;
            while (!rd.Fighting && Time.time - t0 < 45f) yield return null;
            waveDelay = Time.time - t0;
            Check(tag + "parte da sola", rd.Fighting && RoundDirector.Round == round, $"round {RoundDirector.Round}, dopo {waveDelay:F1} s");
            if (!rd.Fighting) yield break;
            Check(tag + "numero di nemici previsto", rd.Target == expectedTarget, $"{rd.Target} invece di {expectedTarget}");
            Check(tag + (moth ? "e' una Processione" : "non e' una Processione"), rd.Moth == moth);
            int scoreBefore = gm.score, statBefore = Drops.statSpawned;
            var seen = new HashSet<Enemy>();
            int outside = 0, notHunting = 0, tooClose = 0, wrongType = 0, maxAlive = 0;
            float killT = 0f, started = Time.time;
            while (rd.Fighting && Time.time - started < 150f)
            {
                foreach (var e in Enemy.All)
                {
                    if (e == null || e.IsDead || !seen.Add(e)) continue;
                    var room = fd.cur.RoomAt(e.transform.position);
                    if (room == null || !access.Access.IsOpen(room.id)) outside++;
                    else spawnsPerRoom[room.id] = (spawnsPerRoom.TryGetValue(room.id, out int c) ? c : 0) + 1;
                    if (e.alert != Alert.Hunting || !e.relentless) notHunting++;
                    if (Targets.NearestDistance(e.transform.position) < 3f) tooClose++;
                    if (moth && e.type != Enemy.EnemyType.Veglia) wrongType++;
                }
                maxAlive = Mathf.Max(maxAlive, rd.Alive);
                killT -= Time.deltaTime;
                if (killT <= 0f) { killT = 0.4f; KillAll(); }
                yield return null;
            }
            Check(tag + "finisce", !rd.Fighting, $"{Time.time - started:F0} s, {seen.Count} nati");
            Check(tag + "nascono tutti", seen.Count == expectedTarget && rd.Spawned == expectedTarget, $"{seen.Count}/{expectedTarget}, contati {rd.Spawned}");
            Check(tag + "mai in un'area chiusa", outside == 0, $"{outside} fuori");
            Check(tag + "sempre all'attacco", notHunting == 0, $"{notHunting} no");
            Check(tag + "mai addosso a chi gioca", tooClose == 0, $"{tooClose} a meno di 3 m");
            Check(tag + "tetto dei vivi rispettato", maxAlive <= expectedCap, $"massimo {maxAlive}, tetto {expectedCap}");
            if (moth) Check(tag + "solo Veglie", wrongType == 0, $"{wrongType} di altro tipo");
            // ogni uccisione paga comunque (awardPoints=false toglie solo i 10 per colpo): il resto e' il premio dell'ondata, versato una volta sola
            int kills = seen.Count * Punti.PerUccisione(DamageSource.Gun, false), expectScore = RoundRules.Reward(round) + kills;
            Check(tag + "premio una volta sola", gm.score - scoreBefore == expectScore, $"+{gm.score - scoreBefore}, atteso +{expectScore} (premio {RoundRules.Reward(round)} + uccisioni {kills})");
            Check(tag + (moth ? "il regalo della Processione cade" : "nessun regalo"), Drops.statSpawned - statBefore == (moth ? 1 : 0), $"{Drops.statSpawned - statBefore} potenziamenti");
            Check(tag + "pausa prima della successiva", !rd.Fighting && rd.NextWave > 8.5f && rd.NextWave <= RoundDirector.WaveBreak, $"{rd.NextWave:F1} s");
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 90f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            var fd = FloorDirector.I; var gm = GameManager.I; var pl = gm.player;
            var rd = RoundDirector.I; var access = RoomAccessDoors.I;
            Check("piano pronto", fd != null && fd.cur != null && rd != null && access != null);
            if (fd == null || fd.cur == null || rd == null || access == null) yield break;
            Drops.disabled = true;                        // niente cadute a caso: il regalo della Processione si conta da solo
            pl.invulnerable = true;
            Check("ciclo a ondate attivo", SurvivalMode.Enabled && RoundDirector.Enabled && !SurvivalMode.HoldWaves);
            Check("stealth spento", !Stealth.Enabled);
            Check("nessuna candela", fd.cur.candles.Count == 0);
            Check("nessun nemico prima dell'ondata", Alive() == 0 && !rd.Fighting && RoundDirector.Round == 0);
            Check("ascensore sigillato", !fd.ExitOpen);
            Time.timeScale = 3f;

            // ---- ondata 1: parte da sola dopo ~8 s dall'arrivo
            yield return PlayWave(1, false, RoundDirector.SurvivalTarget(1, 1), RoundDirector.SurvivalCap(1, 1));
            Check("la prima ondata parte dopo circa 8 s dall'arrivo", Mathf.Abs(waveDelay - RoundDirector.FirstWaveDelay) < 1.5f, $"{waveDelay:F1} s");
            Check("ondate superate: una", rd.FloorWaves == 1 && !fd.ExitOpen);

            // ---- si compra un'area e ci si va: i nemici nascono anche li', mai in quelle ancora chiuse
            PaidRoomDoor door = null;
            foreach (var d in access.Doors) if (d.TargetRoom >= 0) { door = d; break; }
            Check("c'e' una porta da comprare", door != null);
            int bought = -1;
            if (door != null)
            {
                int room = door.TargetRoom, from = access.OpenSide(door);
                Check("l'acquisto riesce coi punti", access.Access.TryBuy(from, room, 100000, out int cost) && cost > 0 && access.Access.IsOpen(room), $"{cost} punti");
                bought = room;
                yield return new WaitForSeconds(2.2f);
                var r = fd.cur.plan.rooms[room];
                if (NavMesh.SamplePosition(new Vector3(r.CenterX, 0.1f, r.CenterZ), out var spot, 6f, HotelNav.EnemyMask)) pl.Teleport(spot.position);
                Check("la porta pagata e' salita", door.Shutter.IsUp);
            }
            yield return PlayWave(2, false, RoundDirector.SurvivalTarget(2, 1), RoundDirector.SurvivalCap(2, 1));
            Check("ondate superate: due", rd.FloorWaves == 2 && !fd.ExitOpen);
            if (bought >= 0)
            {
                spawnsPerRoom.TryGetValue(bought, out int inBought);
                Debug.Log($"[PryingEyes] survivaltest nascite per area: {string.Join(", ", System.Linq.Enumerable.Select(spawnsPerRoom, kv => kv.Key + "=" + kv.Value))}");
                Check("nell'area comprata, dove sta il giocatore, nasce qualcuno", inBought > 0, $"area {bought}: {inBought} nascite");
            }

            // ---- ondata 3: l'ascensore si apre alla fine
            yield return PlayWave(3, false, RoundDirector.SurvivalTarget(3, 1), RoundDirector.SurvivalCap(3, 1));
            Check("tre ondate aprono l'ascensore", rd.FloorWaves == 3 && fd.ExitOpen);

            // ---- la morte annulla l'ondata
            rd.SurvivalNextWaveIn(0f);
            float w = Time.time;
            while ((!rd.Fighting || Alive() < 3) && Time.time - w < 40f) yield return null;
            int before = RoundDirector.Round;
            Check("ondata 4 in corso con almeno tre nemici", rd.Fighting && Alive() >= 3 && before == 4, $"round {before}, vivi {Alive()}");
            pl.invulnerable = false;
            pl.Hurt(99999f);
            yield return null; yield return null;
            Check("morte: l'ondata e' annullata e i nemici spariscono", !pl.alive && !rd.Fighting && Alive() == 0, $"vivi {Alive()}");
            Check("morte: il numero dell'ondata torna indietro", RoundDirector.Round == before - 1, $"round {RoundDirector.Round}");
            w = Time.time;
            while (!pl.alive && Time.time - w < 12f) yield return null;
            pl.invulnerable = true;
            Check("il giocatore rinasce", pl.alive);
            Check("l'ascensore resta aperto", fd.ExitOpen);
            yield return PlayWave(before, false, RoundDirector.SurvivalTarget(before, 1), RoundDirector.SurvivalCap(before, 1));

            // ---- la quinta e' la Processione dei Lumi
            rd.SurvivalNextWaveIn(0f);
            int mothTarget = Mathf.Min(100, Mathf.RoundToInt(RoundDirector.SurvivalTarget(5, 1) * RoundRules.MothCountMul));
            yield return PlayWave(5, true, mothTarget, RoundDirector.SurvivalCap(5, 1) + RoundRules.MothAliveBonus);
        }
    }
}

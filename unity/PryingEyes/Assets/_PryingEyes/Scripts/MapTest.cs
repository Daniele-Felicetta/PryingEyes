#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;
using PryingEyes.Proc.MapArt;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -maptest [-mapshots]: la mappa del piano (Proc/Map). Alla partenza la stanza dell'ascensore e' visitata e
    /// oltre le sue porte ce ne sono di ignote (macchie con gli occhi); la minimappa e' accesa e il foglio grande si apre e si richiude (M);
    /// girando per le stanze si colorano una a una, quelle ignote spariscono e la segreta compare solo entrandoci; le armi viste compaiono
    /// con il loro segno; i morti lasciano sangue; le porte sigillate hanno un sigillo con il prezzo; le scritte delle stanze ci sono; ogni
    /// fotogramma costa poco e non lascia spazzatura. Con -mapshots fotografa l'HUD (unity_logs/mappa).</summary>
    public sealed class MapTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-maptest") >= 0) new GameObject("MapTest").AddComponent<MapTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] maptest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-mapshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] maptest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        void Photo(string name)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/mappa"); Directory.CreateDirectory(folder);
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, true);
        }

        /// <summary>Teletrasporta il giocatore al centro di una stanza (sulla navmesh) e aspetta due fotogrammi.</summary>
        IEnumerator GoTo(PlayerController player, Room r)
        {
            Vector3 p = new Vector3(r.CenterX, 0.3f, r.CenterZ);
            if (NavMesh.SamplePosition(p, out var h, 4f, NavMesh.AllAreas)) p = h.position;
            player.Teleport(p + Vector3.up * 0.05f);
            yield return null; yield return null;
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null; yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var fd = FloorDirector.I; var gm = GameManager.I; var player = gm.player; var plan = fd.cur.plan;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            if (fd.InHub)
            {
                // -cattedrale -floor 0: la cattedrale non ha la mappa, ne' la grande ne' la minimappa, qualunque tasto si prema
                fd.DebugShowMap(true, true);
                yield return new WaitForSeconds(0.6f);
                Check("nella cattedrale non c'e' la mappa (ne' piccola ne' grande)", fd.map == null && gm.hud != null && gm.hud.Map != null && !gm.hud.Map.Visible);
                yield break;
            }
            var map = fd.map;
            Check("la mappa del piano esiste ed e' quella corrente", map != null && FloorMap.I == map && map.plan == plan);
            var view = gm.hud != null ? gm.hud.Map : null;
            Check("l'HUD ha la sua vista della mappa", view != null);
            if (failed > 0) yield break;

            // ----- 1) la partenza
            fd.DebugShowMap(false, true);
            yield return new WaitForSeconds(0.5f);
            var here = fd.cur.RoomAt(player.transform.position);
            Check($"il giocatore e' in una stanza ({(here != null ? here.kind.ToString() : "-")})", here != null);
            // si parte nella cabina dell'ascensore: sulla carta e' un segno, la stanza visitata e' quella a cui si apre
            var start = here;
            if (here != null && here.IsLift)
                foreach (var o in plan.openings)
                    if (o.style == OpenStyle.Ascensore && (o.a == here.id || o.b == here.id)) { start = plan.rooms[o.a == here.id ? o.b : o.a]; break; }
            Check("la stanza di partenza e' visitata", start != null && map.StateOf(start.id) == RoomSeen.Seen);
            Check($"oltre le sue porte ci sono stanze ignote ({map.RumorCount})", map.RumorCount >= 1);
            Check("la minimappa e' accesa e piccola", view.Visible && view.Bigness < 0.01f);
            Check($"le texture della carta costano {MapAssets.BuildMs:F0} ms al filo principale ({MapAssets.GenerateMs:F0} ms nell'altro)", MapAssets.BuildMs < 250f);
            Check($"ci sono le scritte delle stanze viste ({view.ActiveLabels})", view.ActiveLabels >= map.SeenCount);
            Photo("01_minimappa_partenza");

            // ----- 2) il foglio grande
            fd.DebugShowMap(true, false);
            yield return new WaitForSeconds(2.0f);
            Check("M apre il foglio grande", view.Visible && view.Bigness > 0.99f);
            string undrawn = view.UndrawnParts();
            Check("il foglio grande e' tutto disegnato (legenda, bussola, scritte: ognuno ha il suo materiale)" + (undrawn.Length > 0 ? ": mancano " + undrawn : ""), undrawn.Length == 0);
            Photo("02_foglio_partenza");
            fd.DebugShowMap(false, true);
            yield return new WaitForSeconds(0.6f);
            Check("...e lo richiude", view.Bigness < 0.01f);
            fd.DebugShowMap(false, false);
            yield return new WaitForSeconds(0.1f);
            Check("N spegne la minimappa", !view.Visible);
            fd.DebugShowMap(false, true);

            // ----- 3) il costo di un fotogramma, con la mappa piccola e con la grande
            for (int k = 0; k < 20; k++) yield return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            const int N = 60;
            for (int k = 0; k < N; k++) view.Tick(0.016f);
            double miniMs = sw.Elapsed.TotalMilliseconds / N;
            long miniAlloc = (GC.GetAllocatedBytesForCurrentThread() - alloc0) / N;
            Check($"un fotogramma di minimappa costa {miniMs:F2} ms e {miniAlloc} byte", miniMs < 2.5 && miniAlloc < 6000);
            fd.DebugShowMap(true, false);
            for (int k = 0; k < 40; k++) yield return null;
            sw.Restart(); alloc0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < N; k++) view.Tick(0.016f);
            double bigMs = sw.Elapsed.TotalMilliseconds / N;
            long bigAlloc = (GC.GetAllocatedBytesForCurrentThread() - alloc0) / N;
            Check($"un fotogramma di foglio grande costa {bigMs:F2} ms e {bigAlloc} byte", bigMs < 3.5 && bigAlloc < 8000);
            fd.DebugShowMap(false, true);

            // ----- 4) si gira per le stanze: ogni stanza visitata si colora, le ignote si fanno meno
            var order = new List<Room>();
            foreach (var r in plan.rooms) if (!r.IsLift && r.type != RoomType.Segreta) order.Add(r);
            int visited = 0, wrong = 0;
            foreach (var r in order)
            {
                yield return GoTo(player, r);
                var at = fd.cur.RoomAt(player.transform.position);
                if (at == null || at.id != r.id) continue;                  // il punto cade su una stanza vicina (curve, varchi): non conta
                visited++;
                if (map.StateOf(r.id) != RoomSeen.Seen) wrong++;
            }
            Check($"ogni stanza in cui si entra diventa visitata ({visited} su {order.Count}, {wrong} no)", visited >= order.Count / 2 && wrong == 0);
            var swp = System.Diagnostics.Stopwatch.StartNew();
            var fresh = new MapPainter(map.scene);
            for (int i = 0; i < plan.rooms.Count; i++) fresh.Room(i);
            double paintMs = swp.Elapsed.TotalMilliseconds;
            Check($"dipingere tutto il piano da capo costa {paintMs:F0} ms", paintMs < 600);
            yield return new WaitForSeconds(1.6f);
            Check($"visitate quasi tutte, restano {map.RumorCount} stanze ignote", map.SeenCount >= visited);
            bool hasSecret = plan.OfType(RoomType.Segreta).Count > 0;
            if (hasSecret)
            {
                var sr = plan.OfType(RoomType.Segreta)[0];
                Check("la stanza segreta non compare finche' non ci si entra", map.StateOf(sr.id) == RoomSeen.Hidden);
                fd.DebugExplore(sr.id);
                Check("...e dopo averla visitata compare", map.StateOf(sr.id) == RoomSeen.Seen);
            }
            fd.DebugShowMap(true, false);
            yield return new WaitForSeconds(2.0f);
            Photo("03_foglio_esplorato");
            fd.DebugShowMap(false, true);

            // ----- 5) le armi viste, e il sangue
            var walls = FindObjectsByType<WallBuy>(FindObjectsSortMode.None);
            Check($"ci sono armi sui muri ({walls.Length})", walls.Length > 0);
            if (walls.Length > 0)
            {
                var w = walls[0];
                Vector3 near = w.transform.position;
                if (NavMesh.SamplePosition(near, out var nh, 3f, NavMesh.AllAreas)) near = nh.position;
                player.Teleport(near + Vector3.up * 0.05f);
                yield return new WaitForSeconds(1.2f);
                Check($"un'arma vista da vicino compare sulla mappa ({map.foundWeapons.Count} viste)", map.foundWeapons.Contains(w) && map.scene.pois.Exists(p => p.kind == PoiKind.Weapon));
                Check("...con il suo nome e il suo prezzo", map.pois.labels.Exists(l => l.kind == LabelKind.Poi && !string.IsNullOrEmpty(l.text)));
            }
            int bloodBefore = map.scene.blood.Count;
            Vector3 pp = player.transform.position;
            for (int k = 0; k < 4; k++) FloorMap.NoteKill(pp + new Vector3(Mathf.Cos(k * 1.7f), 0f, Mathf.Sin(k * 1.7f)) * 0.4f, 0.4f + 0.15f * k);
            map.Refresh();
            Check($"i morti lasciano sangue sulla mappa ({map.scene.blood.Count - bloodBefore} macchie)", map.scene.blood.Count >= bloodBefore + 4 && map.blood.stain.Count > 0);

            // ----- 6) le porte sigillate (solo nel ciclo a ondate)
            if (map.scene.paidDoors)
            {
                int expected = 0;
                for (int i = 0; i < plan.openings.Count; i++)
                {
                    var o = plan.openings[i];
                    if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || map.scene.door[i] != DoorState.Sealed) continue;
                    if (map.scene.IsSeen(o.a) || map.scene.IsSeen(o.b)) expected++;
                }
                int seals = map.doors.labels.FindAll(l => l.kind == LabelKind.Price).Count;
                Check($"ogni porta sigillata vicino a una stanza vista ha il suo sigillo col prezzo ({seals} su {expected})", seals == expected);
            }
            else Debug.Log("[PryingEyes] maptest: ciclo senza porte a pagamento, niente sigilli da controllare");

            // ----- 7) un nemico che ti ha visto compare come un occhio (foto)
            if (shots)
            {
                Vector3 p0 = player.transform.position;
                Vector3 side = player.transform.right * 6f;
                var spot = p0 + side;
                if (NavMesh.SamplePosition(spot, out var sh, 2f, NavMesh.AllAreas)) spot = sh.position;
                Enemy.debugFreeze = true;
                var monk = Enemy.Spawn(Enemy.EnemyType.Monaco, spot);
                if (monk != null) monk.alert = Alert.Hunting;
                yield return new WaitForSeconds(0.4f);
                fd.DebugShowMap(false, true);
                yield return new WaitForSeconds(0.4f);
                Photo("04_minimappa_nemico");
                fd.DebugShowMap(true, false);
                yield return new WaitForSeconds(2.0f);
                Photo("05_foglio_nemico");
                Enemy.ClearAll(); Enemy.debugFreeze = false;
                fd.DebugShowMap(false, true);
            }

            // ----- 8) lo schermo condiviso: ognuno ha la sua vista
            if (Seats.Split)
            {
                int ok = 0;
                foreach (var s in Seats.All) if (s.hud != null && s.hud.Map != null && s.hud.Map.Visible) ok++;
                Check($"nello schermo condiviso ogni posto ha la sua mappa ({ok} su {Seats.All.Count})", ok == Seats.All.Count);
            }
            fd.DebugShowMap(false, false);
        }
    }
}
#endif

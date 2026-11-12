using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Prova della fase 4 (pianta a griglia: stanze grandi con un ruolo, arene, negozio, biblioteca, stanza segreta, guardiano
    /// dell'uscita), con lo schema delle altre:
    ///   PryingEyes.exe -batchmode [-nographics] -autotest -stealthtest -gridtest -seed N -floor N    esito in hotel_build/furtivo/griglia.txt
    /// Uscita 0 ok, 1 fallito, 2 piano non a griglia. Controlla i ruoli e le porte, le cornici colorate, la navmesh (tutto si raggiunge a
    /// piedi tranne la stanza segreta), i santuari nei loro ruoli, le armi sul muro, le colonne delle arene, le guardie (restano nella
    /// loro stanza) e il guardiano, e fa passare il giocatore a carponi nel passaggio basso della stanza segreta.</summary>
    public partial class StealthTest
    {
        IEnumerator CheckGrid()
        {
            var gm = GameManager.I;
            var fd = FloorDirector.I;
            var pl = gm.player;
            bool passed = true;
            void Check(string what, bool ok, string detail = "")
            {
                Log((ok ? "ok       " : "FALLITO  ") + what + (detail.Length > 0 ? "   [" + detail + "]" : ""));
                if (!ok) passed = false;
            }
            yield return new WaitForSeconds(1.5f);
            Drops.disabled = true;
            PlayerController.holdLook = true;
            pl.invulnerable = true;
            var plan = fd.cur.plan;
            Log($"piano {fd.floor} seme {fd.runSeed}: {plan.rooms.Count} stanze, tema {plan.theme}, pianta {(plan.grid ? "a griglia" : "ad ali")}, {plan.maxX / 10 - plan.minX / 10} x {plan.maxZ / 10 - plan.minZ / 10} m");
            if (!plan.grid) { Log("piano non a griglia: prova non applicabile"); Application.Quit(2); yield break; }
            int Count(RoomType t) => plan.OfType(t).Count;

            // ----- 1) ruoli e porte
            Log("--- ruoli e porte");
            Log($"ruoli: partenza {Count(RoomType.Partenza)}, boss {Count(RoomType.Boss)}, negozio {Count(RoomType.Negozio)}, biblioteca {Count(RoomType.Biblioteca)}, " +
                $"segreta {Count(RoomType.Segreta)}, arene {Count(RoomType.Candela)}, guardia {Count(RoomType.Guardia)}");
            Check("una partenza, un boss, un negozio, una biblioteca", Count(RoomType.Partenza) == 1 && Count(RoomType.Boss) == 1 && Count(RoomType.Negozio) == 1 && Count(RoomType.Biblioteca) == 1);
            Check("le arene sono tante quante le candele e almeno due", Count(RoomType.Candela) == fd.cur.candles.Count && Count(RoomType.Candela) == plan.objectives.Count && Count(RoomType.Candela) >= 2,
                $"arene {Count(RoomType.Candela)}, candele {fd.cur.candles.Count}, obiettivi {plan.objectives.Count}");
            bool doorsOk = true; string worst = "";
            foreach (var r in plan.rooms)
            {
                if (r.IsLift) continue;
                int d = plan.DoorCount(r);
                if (r.type == RoomType.Segreta ? d != 0 : (d < 1 || d > 4)) { doorsOk = false; worst = r + ": " + d + " porte"; }
            }
            Check("da una a quattro porte per stanza (la segreta solo il passaggio basso)", doorsOk, worst);
            var startRoom = plan.OfType(RoomType.Partenza)[0];
            var bossRoom = plan.OfType(RoomType.Boss)[0];
            Check("l'ascensore di arrivo e' nella partenza, quello d'uscita nel boss",
                plan.openings.Exists(o => o.style == OpenStyle.Ascensore && (o.a == plan.start || o.b == plan.start) && (o.a == startRoom.id || o.b == startRoom.id)) &&
                plan.openings.Exists(o => o.style == OpenStyle.Ascensore && (o.a == plan.exit || o.b == plan.exit) && (o.a == bossRoom.id || o.b == bossRoom.id)));
            bool leaves = true;
            foreach (var t in new[] { RoomType.Negozio, RoomType.Biblioteca, RoomType.Boss })
                foreach (var r in plan.OfType(t)) if (plan.DoorCount(r) > 2) leaves = false;
            Check("negozio, biblioteca e boss hanno al massimo due porte", leaves);

            // ----- 2) cornici colorate
            Log("--- cornici delle porte");
            bool frames = true; int nFrames = 0;
            foreach (var r in plan.rooms)
            {
                if (r.IsLift) continue;
                var go = fd.cur.roomGo[r.id];
                if (go == null) continue;
                bool doors = plan.openings.Exists(o => (o.a == r.id || o.b == r.id) && o.b >= 0 && !o.oneSided && o.style != OpenStyle.Ascensore);
                var f = go.transform.Find("Cornici di tipo");
                if (f != null) nFrames++;
                if (doors && f == null) frames = false;
            }
            Check("ogni stanza con porte ha le cornici colorate per tipo", frames, nFrames + " stanze con cornici");

            // ----- 3) navmesh
            Log("--- navmesh");
            var bad = Connectivity.Unreachable(fd.cur, out int cb, out bool eb);
            Check("dall'arrivo si raggiungono tutte le stanze (tranne la segreta), le candele e l'uscita", bad.Count == 0 && cb == 0 && !eb, $"stanze {bad.Count}, candele {cb}, uscita {(eb ? "no" : "si")}");
            if (bad.Count > 0 || cb > 0 || eb) DiagnoseBlocked(fd.cur, plan, bad);

            // ----- 4) santuari ai loro posti
            Log("--- santuari");
            Check("la Biblioteca e' nella stanza Biblioteca", fd.libraryRoom != null && fd.libraryRoom.type == RoomType.Biblioteca, fd.libraryRoom != null ? fd.libraryRoom.type.ToString() : "assente");
            Check("lo Scrigno e' nel Negozio", fd.chestRoom != null && fd.chestRoom.type == RoomType.Negozio, fd.chestRoom != null ? fd.chestRoom.type.ToString() : "assente");
            bool hasSecret = Count(RoomType.Segreta) > 0;
            Check("l'altare e' nella stanza segreta (se c'e')", !hasSecret || (fd.altarRoom != null && fd.altarRoom.type == RoomType.Segreta), fd.altarRoom != null ? fd.altarRoom.type.ToString() : "assente");

            // ----- 5) armi sui muri
            Log("--- armi sul muro");
            int inStart = 0, inShop = 0, total = 0;
            foreach (var w in FindObjectsByType<WallBuy>(FindObjectsSortMode.None))
            {
                total++;
                var rr = fd.cur.RoomAt(w.transform.position);
                if (rr == null) continue;
                if (rr.type == RoomType.Partenza) inStart++;
                if (rr.type == RoomType.Negozio) inShop++;
            }
            Check("un'arma sul muro della partenza e due nel Negozio", inStart >= 1 && inShop >= 2, $"partenza {inStart}, negozio {inShop}, totale {total}");

            // ----- 6) arredi delle arene
            Log("--- arene");
            bool columns = true;
            foreach (var r in plan.OfType(RoomType.Candela))
            {
                var go = fd.cur.roomGo[r.id];
                var pr = go != null ? go.transform.Find("Arredi") : null;
                int cols = 0, benches = 0;
                if (pr != null)
                    foreach (var t in pr.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Colonna") cols++;
                        else if (t.name.Contains("panca") || t.name.Contains("divano")) benches++;
                    }
                Log($"arena {r}: {r.W / 10f:F1} x {r.D / 10f:F1} m, colonne {cols}, panche/divani {benches}");
                if (cols < 1) columns = false;
            }
            Check("ogni arena ha almeno una colonna", columns);

            // ----- 7) guardie e guardiano
            Log("--- guardie");
            var guards = new List<Enemy>(); Enemy boss = null;
            bool wrongRoom = false; string wrong = "";
            foreach (var e in Enemy.All)
            {
                if (e == null) continue;
                var rr = fd.cur.RoomAt(e.transform.position);
                if (e.hasHome) guards.Add(e);
                if (e.hasHome && rr != null && rr.type == RoomType.Boss && e.maxHp >= 600f) boss = e;
                else if (rr == null || rr.type != RoomType.Guardia) { wrongRoom = true; wrong += (rr != null ? rr.type.ToString() : "fuori") + " "; }
            }
            Log($"nemici {Enemy.All.Count}, con una stanza di casa {guards.Count}");
            Check("c'e' un guardiano pesante e robusto nella stanza del boss", boss != null, boss != null ? $"{boss.type} {boss.maxHp:F0} punti vita" : "assente");
            Check("gli altri nascono tutti in stanze di guardia", !wrongRoom, wrong);
            Check("tutti i nemici del piano hanno una stanza di casa", guards.Count == Enemy.All.Count, $"{guards.Count}/{Enemy.All.Count}");

            // le guardie restano nella loro stanza: il piano gira a velocita' 6 per due minuti, il giocatore e' fermo in ascensore e i nemici non sentono nulla
            Enemy.debugNoSense = true;
            var homeRoom = new Dictionary<Enemy, Room>();
            var lastPos = new Dictionary<Enemy, Vector3>();
            var walked = new Dictionary<Enemy, float>();
            var outside = new Dictionary<Enemy, float>();
            foreach (var g in guards) { homeRoom[g] = fd.cur.RoomAt(g.transform.position); lastPos[g] = g.transform.position; walked[g] = 0f; outside[g] = 0f; }
            Time.timeScale = 6f;
            for (float t = 0f; t < 120f; t += Time.deltaTime)
            {
                yield return null;
                foreach (var g in guards)
                {
                    if (g == null || g.IsDead) continue;
                    Vector3 p = g.transform.position;
                    walked[g] += Vector3.Distance(p, lastPos[g]);
                    lastPos[g] = p;
                    var r = homeRoom[g];
                    if (r == null) continue;
                    float dx = Mathf.Max(r.x0 * 0.1f - p.x, 0f, p.x - r.x1 * 0.1f), dz = Mathf.Max(r.z0 * 0.1f - p.z, 0f, p.z - r.z1 * 0.1f);
                    outside[g] = Mathf.Max(outside[g], Mathf.Sqrt(dx * dx + dz * dz));
                }
            }
            Time.timeScale = 1f;
            float maxOut = 0f, maxWalk = 0f; int stuck = 0;
            foreach (var g in guards) { maxOut = Mathf.Max(maxOut, outside[g]); maxWalk = Mathf.Max(maxWalk, walked[g]); if (walked[g] < 3f && !Enemy.IsFlying(g.type)) stuck++; }
            Log($"dopo due minuti (a velocita' 6): sconfinamento massimo {maxOut:F1} m, cammino massimo {maxWalk:F0} m, fermi {stuck} su {guards.Count}");
            Check("le guardie non escono dalla loro stanza", maxOut < 1.5f, $"{maxOut:F1} m fuori");
            Check("le guardie pattugliano davvero (non sono bloccate)", guards.Count == 0 || stuck <= guards.Count / 3, $"fermi {stuck}/{guards.Count}");
            Enemy.debugNoSense = false;
            Enemy.ClearAll();

            // ----- 8) la stanza segreta: a carponi si entra, in piedi no, i nemici non ci arrivano
            Log("--- stanza segreta");
            if (!hasSecret) Log("nessuna stanza segreta su questo piano");
            else
            {
                var sr = plan.OfType(RoomType.Segreta)[0];
                var po = plan.openings.Find(o => o.style == OpenStyle.Passaggio && (o.a == sr.id || o.b == sr.id));
                Check("la stanza segreta ha un unico accesso: il passaggio basso", po != null && plan.openings.FindAll(o => (o.a == sr.id || o.b == sr.id) && o.b >= 0 && o.style != OpenStyle.Ascensore).Count == 1);
                if (po != null)
                {
                    float ox = po.alongX ? po.center * 0.1f : po.line * 0.1f, oz = po.alongX ? po.line * 0.1f : po.center * 0.1f;
                    Vector3 inward = po.alongX ? new Vector3(0f, 0f, Mathf.Sign(sr.CenterZ - oz)) : new Vector3(Mathf.Sign(sr.CenterX - ox), 0f, 0f);
                    int otherId = po.a == sr.id ? po.b : po.a;
                    var other = plan.rooms[otherId];
                    // in piedi non si passa: una capsula alta come il giocatore sbatte contro l'architrave
                    const float rad = 0.33f;
                    bool standBlocked = Physics.CheckCapsule(new Vector3(ox, 0.1f + rad, oz), new Vector3(ox, 1.75f - rad, oz), rad, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
                    bool crouchFree = !Physics.CheckCapsule(new Vector3(ox, 0.1f + rad, oz), new Vector3(ox, 1.15f - rad, oz), rad, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
                    Check("in piedi il passaggio e' chiuso, accovacciati e' libero", standBlocked && crouchFree, $"in piedi chiuso {standBlocked}, accovacciato libero {crouchFree}");
                    // i nemici non ci arrivano: nessun percorso sulla navmesh dalla stanza accanto alla segreta
                    bool noPath = true;
                    if (fd.SecretEntry(sr, out Vector3 inside) && NavMesh.SamplePosition(new Vector3(other.CenterX, 0f, other.CenterZ), out NavMeshHit oh, 4f, NavMesh.AllAreas))
                    {
                        var path = new NavMeshPath();
                        noPath = !(NavMesh.CalculatePath(oh.position, inside, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete);
                    }
                    Check("i nemici non hanno un percorso fino alla stanza segreta", noPath);
                    // il giocatore ci passa davvero, col controller: parte a 2,2 m dal buco, accovacciato, cammina verso l'interno
                    Vector3 from = new Vector3(ox, 0f, oz) - inward * 2.2f;
                    if (NavMesh.SamplePosition(from, out NavMeshHit fh, 1.5f, NavMesh.AllAreas)) from = fh.position;
                    PlayerController.holdLook = false;
                    PlayerController.botCrouch = true;
                    pl.Teleport(from);
                    yield return new WaitForSeconds(0.4f);
                    PlayerController.botDir = inward;
                    float tt = 0f;
                    while (tt < 9f && fd.cur.RoomAt(pl.transform.position) != sr) { tt += Time.deltaTime; yield return null; }
                    float depth = 0.5f;
                    for (float t2 = 0f; t2 < depth; t2 += Time.deltaTime) yield return null;
                    bool entered = fd.cur.RoomAt(pl.transform.position) == sr;
                    Check("il giocatore entra a carponi nella stanza segreta", entered, $"{tt:F1} s, a {Vector3.Distance(pl.transform.position, new Vector3(ox, pl.transform.position.y, oz)):F1} m dal buco");
                    PlayerController.botDir = Vector3.zero;
                    yield return new WaitForSeconds(0.3f);
                    // e ne esce
                    PlayerController.botDir = -inward;
                    tt = 0f;
                    while (tt < 9f && fd.cur.RoomAt(pl.transform.position) == sr) { tt += Time.deltaTime; yield return null; }
                    for (float t2 = 0f; t2 < depth; t2 += Time.deltaTime) yield return null;
                    bool left = fd.cur.RoomAt(pl.transform.position) != sr;
                    Check("e ne esce a carponi", left, $"{tt:F1} s");
                    PlayerController.botDir = Vector3.zero;
                    PlayerController.botCrouch = false;
                    PlayerController.holdLook = true;
                }
            }

            // ----- 8b) foto dei ruoli (solo con la grafica)
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) yield return GridPhotos(fd, plan);

            // ----- 9) la mappa: quello che si vede (le stanze visitate, i "?" oltre le porte aperte, la segreta solo dopo) si dipinge in un PNG
            Log("--- mappa");
            var around = new HashSet<int>();
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || o.style == OpenStyle.Passaggio) continue;
                if (o.a == startRoom.id) around.Add(o.b);
                if (o.b == startRoom.id) around.Add(o.a);
            }
            fd.DebugSetExplored(new[] { startRoom.id });
            int q0 = PaintMap(fd, plan, Dir + "/griglia_mappa_inizio.png");
            Check("all'inizio si vedono la partenza e, come '?', le stanze oltre le sue porte", q0 == around.Count && q0 >= 1, $"{q0} stanze '?', porte aperte verso {around.Count}");
            var all = new List<int>();
            foreach (var r in plan.rooms) if (!r.IsLift && r.type != RoomType.Segreta) all.Add(r.id);
            fd.DebugSetExplored(all);
            int q1 = PaintMap(fd, plan, Dir + "/griglia_mappa_tutta.png");
            Check("visitate tutte le stanze non resta nessun '?'", q1 == 0, $"{q1} stanze '?'");
            Check("la stanza segreta non compare sulla mappa finche' non ci si entra", !hasSecret || !PaintMapHas(fd, plan, plan.OfType(RoomType.Segreta)[0]));
            if (hasSecret)
            {
                fd.DebugExplore(plan.OfType(RoomType.Segreta)[0].id);
                Check("...e dopo esserci entrati compare", PaintMapHas(fd, plan, plan.OfType(RoomType.Segreta)[0]));
                PaintMap(fd, plan, Dir + "/griglia_mappa_con_segreta.png");
            }
            bool gfx = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            fd.DebugShowMap(true, false);
            yield return new WaitForSeconds(0.4f);
            if (gfx) GameManager.CamShot(Dir + "/griglia_mappa_ui.png", null, true);          // la mappa a tutto schermo com'e' nel gioco, con l'HUD
            fd.DebugShowMap(false, true);
            yield return new WaitForSeconds(0.3f);
            if (gfx) GameManager.CamShot(Dir + "/griglia_minimappa_ui.png", null, true);
            fd.DebugShowMap(false, false);

            Log(passed ? "ESITO: ok" : "ESITO: FALLITO");
            Application.Quit(passed ? 0 : 1);
        }

        /// <summary>Quando una stanza non si raggiunge: per ogni porta tra una stanza raggiunta e una no, quali punti della corsia (da -2,5 a +2,5 m
        /// dalla soglia, ogni mezzo metro) hanno navmesh ('#') e quali oggetti toccano il passaggio. Serve a capire cosa chiude la porta.</summary>
        void DiagnoseBlocked(FloorInstance fi, Plan plan, IEnumerable<Room> bad)
        {
            var badIds = new HashSet<int>();
            foreach (var r in bad) badIds.Add(r.id);
            // dove si ferma il percorso dall'arrivo verso la soglia di ogni porta tra una stanza raggiunta e una no: la stanza raggiunta in cui
            // si ferma si disegna a testo (una sola volta)
            if (Connectivity.StartPoint(fi, out Vector3 s0))
            {
                var dumped = new HashSet<int>();
                var pth = new NavMeshPath();
                foreach (var o in plan.openings)
                {
                    if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || o.style == OpenStyle.Passaggio) continue;
                    if (badIds.Contains(o.a) == badIds.Contains(o.b)) continue;
                    float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                    Room good = plan.rooms[badIds.Contains(o.a) ? o.b : o.a];
                    Vector3 inward = o.alongX ? new Vector3(0f, 0f, Mathf.Sign(good.CenterZ - oz)) : new Vector3(Mathf.Sign(good.CenterX - ox), 0f, 0f);
                    if (!NavMesh.SamplePosition(new Vector3(ox, 0f, oz) + inward * 0.8f, out NavMeshHit th, 1.0f, NavMesh.AllAreas)) continue;
                    NavMesh.CalculatePath(s0, th.position, NavMesh.AllAreas, pth);
                    if (pth.corners.Length == 0) continue;
                    Vector3 stall = pth.corners[pth.corners.Length - 1];
                    Log($"   verso la porta {plan.rooms[o.a]} - {plan.rooms[o.b]} in ({ox:F1},{oz:F1}) il percorso ({pth.status}) si ferma a ({stall.x:F1},{stall.z:F1}), a {Vector3.Distance(stall, th.position):F1} m dalla soglia");
                    if (pth.status != NavMeshPathStatus.PathComplete && dumped.Add(good.id)) DumpRoomMap(good, stall, th.position);
                }
                // le candele che non si raggiungono: dove si ferma il percorso e com'e' fatta la stanza attorno
                foreach (var cd in fi.candles)
                {
                    if (cd == null) continue;
                    Vector3 cp = cd.transform.position;
                    bool nearNav = NavMesh.SamplePosition(cp, out NavMeshHit ch, 0.9f, NavMesh.AllAreas);
                    if (nearNav && Connectivity.Reach(s0, cp, 0.9f)) continue;
                    Room cr = fi.RoomAt(cp);
                    Log($"   candela in ({cp.x:F1},{cp.z:F1}) nella stanza {(cr != null ? cr.ToString() : "?")}: navmesh entro 0,9 m {(nearNav ? "si" : "NO")}");
                    if (NavMesh.SamplePosition(cp, out NavMeshHit c2, 1.5f, NavMesh.AllAreas))
                    {
                        NavMesh.CalculatePath(s0, c2.position, NavMesh.AllAreas, pth);
                        Vector3 st = pth.corners.Length > 0 ? pth.corners[pth.corners.Length - 1] : s0;
                        Log($"      percorso ({pth.status}) fino a ({c2.position.x:F1},{c2.position.z:F1}) si ferma a ({st.x:F1},{st.z:F1})");
                        if (cr != null && dumped.Add(cr.id)) DumpRoomMap(cr, st, cp);
                    }
                    else if (cr != null && dumped.Add(cr.id)) DumpRoomMap(cr, s0, cp);
                }
            }
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || o.style == OpenStyle.Passaggio) continue;
                if (badIds.Contains(o.a) == badIds.Contains(o.b)) continue;
                float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                Vector3 lane = o.alongX ? Vector3.forward : Vector3.right;       // da una stanza all'altra
                var cells = new System.Text.StringBuilder();
                for (int i = -5; i <= 5; i++)
                    cells.Append(NavMesh.SamplePosition(new Vector3(ox, 0.1f, oz) + lane * (i * 0.5f), out _, 0.25f, NavMesh.AllAreas) ? '#' : '.');
                Log($"   porta {plan.rooms[o.a]} - {plan.rooms[o.b]} ({o.style}, larga {o.width * 0.1f:F1} m) in ({ox:F1},{oz:F1}): corsia da -2,5 a +2,5 m [{cells}]");
                var names = new SortedSet<string>();
                Vector3 half = o.alongX ? new Vector3(o.width * 0.05f + 0.6f, 1.0f, 2.2f) : new Vector3(2.2f, 1.0f, o.width * 0.05f + 0.6f);
                foreach (var c in Physics.OverlapBox(new Vector3(ox, 1.0f, oz), half, Quaternion.identity, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                {
                    var t = c.transform;
                    names.Add((t.parent != null ? t.parent.name + "/" : "") + t.name + " h" + c.bounds.max.y.ToString("F1"));
                }
                Log("      oggetti nel varco: " + string.Join(", ", names));
            }
        }

        /// <summary>Mappa a testo di una stanza, una cella ogni 40 cm: '#' navmesh, 'o' un arredo senza navmesh sotto, '.' vuoto senza navmesh (accanto
        /// ai muri e agli ingombri), 'S' il punto dove si ferma il percorso e 'P' la soglia da raggiungere. Le righe vanno da nord (z alto) a sud.</summary>
        void DumpRoomMap(Room r, Vector3 stall, Vector3 goal)
        {
            const float step = 0.4f;
            float x0 = r.x0 * 0.1f, x1 = r.x1 * 0.1f, z0 = r.z0 * 0.1f, z1 = r.z1 * 0.1f;
            Log($"   mappa di {r} (x {x0:F1}..{x1:F1}, z {z1:F1} in alto .. {z0:F1} in basso; 40 cm a cella; # navmesh, o arredo, . niente, S stallo, P soglia)");
            for (float z = z1 - step * 0.5f; z > z0; z -= step)
            {
                var sb = new System.Text.StringBuilder();
                for (float x = x0 + step * 0.5f; x < x1; x += step)
                {
                    if (Mathf.Abs(x - stall.x) < step * 0.5f && Mathf.Abs(z - stall.z) < step * 0.5f) { sb.Append('S'); continue; }
                    if (Mathf.Abs(x - goal.x) < step * 0.5f && Mathf.Abs(z - goal.z) < step * 0.5f) { sb.Append('P'); continue; }
                    if (NavMesh.SamplePosition(new Vector3(x, 0.1f, z), out _, 0.2f, NavMesh.AllAreas)) { sb.Append('#'); continue; }
                    bool prop = false;
                    foreach (var c in Physics.OverlapBox(new Vector3(x, 1.0f, z), new Vector3(step * 0.5f, 0.6f, step * 0.5f), Quaternion.identity, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                    {
                        for (Transform t = c.transform; t != null && !prop; t = t.parent) prop = t.parent != null && t.parent.name == "Arredi";
                        if (prop) break;
                    }
                    sb.Append(prop ? 'o' : '.');
                }
                Log("      " + sb);
            }
        }

        /// <summary>Un punto di vista in una stanza: 1,6 m dentro il varco che sta piu' lontano dal centro e da cui si vede 'target' (o il centro).</summary>
        bool Vantage(Plan plan, Room r, Vector3? target, out Vector3 stand, out Vector3 look)
        {
            Vector3 ctr = new Vector3(r.CenterX, 0f, r.CenterZ);
            look = target ?? ctr + Vector3.up * 1.3f;
            stand = default;
            float best = -1f; bool found = false;
            foreach (var o in plan.openings)
            {
                if ((o.a != r.id && o.b != r.id) || o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore) continue;
                float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                Vector3 inward = o.alongX ? new Vector3(0f, 0f, Mathf.Sign(r.CenterZ - oz)) : new Vector3(Mathf.Sign(r.CenterX - ox), 0f, 0f);
                if (!NavMesh.SamplePosition(new Vector3(ox, 0f, oz) + inward * 1.6f, out NavMeshHit h, 1.0f, NavMesh.AllAreas)) continue;
                if (!HotelNav.ClearLine(h.position + Vector3.up * 1.6f, look)) continue;
                float d = (h.position - ctr).magnitude;
                if (d > best) { best = d; stand = h.position; found = true; }
            }
            return found;
        }

        IEnumerator GridPhotos(FloorDirector fd, Plan plan)
        {
            var pl = GameManager.I.player;
            var torch = Torch.I;
            if (torch != null) { torch.charge = 1f; if (!torch.on) torch.Toggle(); }
            PlayerController.holdLook = true;
            IEnumerator Take(string name, Room r, Vector3? target = null, Vector3? from = null)
            {
                Vector3 stand, look;
                bool ok;
                if (from.HasValue) { stand = from.Value; look = target ?? new Vector3(r.CenterX, 1.3f, r.CenterZ); ok = true; }
                else ok = Vantage(plan, r, target, out stand, out look);
                if (!ok) { Log("   foto " + name + ": nessun punto di vista"); yield break; }
                pl.Teleport(stand);
                Aim(pl, look);
                yield return new WaitForSeconds(0.6f);
                GameManager.CamShot(Dir + "/griglia_" + name + ".png", null, false);
                Log("   foto " + name + " in " + r);
            }
            Log("--- foto dei ruoli");
            yield return Take("partenza", plan.OfType(RoomType.Partenza)[0]);
            int k = 0;
            foreach (var r in plan.OfType(RoomType.Candela)) yield return Take("arena" + (++k), r);
            if (Scrigno.Current != null && fd.chestRoom != null) yield return Take("negozio", fd.chestRoom, Scrigno.Current.transform.position + Vector3.up * 1.0f);   // sopra il coperchio (0,68 m): dentro il collider la linea non sarebbe mai libera
            if (Biblioteca.Current != null && fd.libraryRoom != null) yield return Take("biblioteca", fd.libraryRoom, Biblioteca.Current.stands[1].Point);
            yield return Take("boss", plan.OfType(RoomType.Boss)[0]);
            var guard = plan.OfType(RoomType.Guardia);
            if (guard.Count > 0) yield return Take("guardia", guard[0]);
            // una cornice da vicino: la porta di un'arena vista dalla stanza accanto, a 3 m
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || o.style == OpenStyle.Passaggio) continue;
                var ra = plan.rooms[o.a]; var rb = plan.rooms[o.b];
                Room arena = ra.type == RoomType.Candela ? ra : rb.type == RoomType.Candela ? rb : null;
                if (arena == null) continue;
                Room outside = arena == ra ? rb : ra;
                float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                Vector3 outward = o.alongX ? new Vector3(0f, 0f, Mathf.Sign(outside.CenterZ - oz)) : new Vector3(Mathf.Sign(outside.CenterX - ox), 0f, 0f);
                if (!NavMesh.SamplePosition(new Vector3(ox, 0f, oz) + outward * 3.0f, out NavMeshHit h, 1.0f, NavMesh.AllAreas)) continue;
                yield return Take("cornice_arena", outside, new Vector3(ox, 1.4f, oz), h.position);
                break;
            }
            // la stanza segreta: il buco visto da fuori e l'altare visto da dentro
            var secrets = plan.OfType(RoomType.Segreta);
            if (secrets.Count > 0)
            {
                var sr = secrets[0];
                var po = plan.openings.Find(o => o.style == OpenStyle.Passaggio && (o.a == sr.id || o.b == sr.id));
                if (po != null)
                {
                    float ox = po.alongX ? po.center * 0.1f : po.line * 0.1f, oz = po.alongX ? po.line * 0.1f : po.center * 0.1f;
                    var other = plan.rooms[po.a == sr.id ? po.b : po.a];
                    Vector3 outward = po.alongX ? new Vector3(0f, 0f, Mathf.Sign(other.CenterZ - oz)) : new Vector3(Mathf.Sign(other.CenterX - ox), 0f, 0f);
                    if (NavMesh.SamplePosition(new Vector3(ox, 0f, oz) + outward * 2.6f, out NavMeshHit h, 1.0f, NavMesh.AllAreas))
                        yield return Take("segreta_buco", other, new Vector3(ox, 0.7f, oz), h.position);
                }
                if (Altare.Current != null && fd.SecretEntry(sr, out Vector3 inside)) yield return Take("segreta_altare", sr, Altare.Current.transform.position + Vector3.up * 0.8f, inside);
            }
            if (torch != null && torch.on) torch.Toggle();
        }

        /// <summary>Dipinge in un PNG (10 px al metro) gli elementi della mappa a griglia; ritorna quante stanze sono mostrate come '?'. Le etichette vanno nel log.</summary>
        int PaintMap(FloorDirector fd, Plan plan, string file)
        {
            const float scale = 10f; const int margin = 20;
            float x0 = plan.minX * 0.1f, z1 = plan.maxZ * 0.1f;
            int W = Mathf.CeilToInt((plan.maxX - plan.minX) * 0.1f * scale) + margin * 2, H = Mathf.CeilToInt((plan.maxZ - plan.minZ) * 0.1f * scale) + margin * 2;
            var px = new Color[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.025f, 0.035f, 0.045f, 1f);
            float PX(float x) => margin + (x - x0) * scale;
            float PY(float z) => margin + (z1 - z) * scale;
            void Fill(Rect r, Color c)
            {
                int xa = Mathf.Max(0, Mathf.FloorToInt(r.x)), xb = Mathf.Min(W - 1, Mathf.CeilToInt(r.xMax) - 1);
                int ya = Mathf.Max(0, Mathf.FloorToInt(r.y)), yb = Mathf.Min(H - 1, Mathf.CeilToInt(r.yMax) - 1);
                for (int y = ya; y <= yb; y++)
                    for (int x = xa; x <= xb; x++)
                    {
                        int k = (H - 1 - y) * W + x;             // lo schermo ha y verso il basso, la texture verso l'alto
                        px[k] = Color.Lerp(px[k], new Color(c.r, c.g, c.b, 1f), c.a);
                    }
            }
            int unknown = 0;
            foreach (var it in fd.GridMapItems(plan, true, PX, PY, scale))
            {
                switch (it.kind)
                {
                    case FloorDirector.MapItem.Kind.Fill: Fill(it.r, it.c); break;
                    case FloorDirector.MapItem.Kind.Box:
                        Fill(new Rect(it.r.x, it.r.y, it.r.width, it.t), it.c); Fill(new Rect(it.r.x, it.r.yMax - it.t, it.r.width, it.t), it.c);
                        Fill(new Rect(it.r.x, it.r.y, it.t, it.r.height), it.c); Fill(new Rect(it.r.xMax - it.t, it.r.y, it.t, it.r.height), it.c);
                        break;
                    default:
                        if (it.text == "?") unknown++;
                        Log($"   mappa: '{it.text}' a ({it.r.center.x:F0},{it.r.center.y:F0})");
                        break;
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.Destroy(tex);
            return unknown;
        }

        /// <summary>Vero se la stanza compare nella mappa (come riquadro).</summary>
        bool PaintMapHas(FloorDirector fd, Plan plan, Room room)
        {
            foreach (var it in fd.GridMapItems(plan, true, x => x, z => -z, 1f))
                if (it.kind == FloorDirector.MapItem.Kind.Label && it.r.Contains(new Vector2(room.CenterX, -room.CenterZ))) return true;
            return false;
        }
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes.EditorTools
{
    /// <summary>Controllo di connettivita': genera N piani in edit-mode, ricostruisce la navmesh e verifica che dall'ascensore di
    /// arrivo si raggiunga ogni stanza, ogni candela e l'uscita. Argomenti: -seeds N (20), -seed0 N (3000), -fast (solo riepilogo).</summary>
    public static class ProcCheck
    {
        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public static void Run()
        {
            int n = int.TryParse(Arg("-seeds"), out int v) ? v : 20;
            int seed0 = int.TryParse(Arg("-seed0"), out int s0) ? s0 : 3000;
            EditorSceneManager.OpenScene(TorreBuilder.ScenePath);
            var player = GameObject.Find("Player");
            if (player != null) player.SetActive(false);
            var kit = Object.FindFirstObjectByType<ProcKit>();
            TorreBuilder.FillKit(kit);
            int badFloors = 0, roomsBad = 0, roomsAll = 0, candlesBad = 0, exitBad = 0, liftBad = 0, rawProblems = 0, rawRooms = 0, stillBad = 0, secrets = 0, clearedAll = 0;
            var sb = new StringBuilder();
            var kindBad = new Dictionary<string, int>();
            float tBuild = 0f, tNav = 0f;
            int upperAll = 0, upperBad = 0;
            // -propstats: quanti arredi di ogni tipo finiscono in quale tipo di stanza, sommati sui piani (dopo la riparazione)
            var propStats = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-propstats") >= 0 ? new SortedDictionary<string, SortedDictionary<string, int>>() : null;
            var themeFloors = new SortedDictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                int seed = seed0 + i * 17, floor = 1 + i % 8;
                var plan = PlanGen.Generate(seed, floor);
                if (plan == null) { sb.AppendLine($"seme {seed}: piano non generato ({PlanGen.LastFailure})"); badFloors++; continue; }
                LightPool.Clear();
                float t0 = Time.realtimeSinceStartup;
                var fi = FloorBuilder.Build(plan, null);
                Physics.SyncTransforms();
                float t1 = Time.realtimeSinceStartup;
                HotelNav.Rebuild(fi.bounds, fi.propRoots);
                float t2 = Time.realtimeSinceStartup;
                tBuild += t1 - t0; tNav += t2 - t1;
                // rete di sicurezza: quanti problemi ci sarebbero senza, e quanti restano con
                {
                    var dgTri = NavMesh.CalculateTriangulation();
                    float dgMinX = float.MaxValue, dgMaxX = float.MinValue, dgMinZ = float.MaxValue, dgMaxZ = float.MinValue;
                    foreach (var dgV in dgTri.vertices) { dgMinX = Mathf.Min(dgMinX, dgV.x); dgMaxX = Mathf.Max(dgMaxX, dgV.x); dgMinZ = Mathf.Min(dgMinZ, dgV.z); dgMaxZ = Mathf.Max(dgMaxZ, dgV.z); }
                    var dgPre = Connectivity.Unreachable(fi, out int dgCandles, out bool dgExit);
                    var dgDist = plan.WalkDist(plan.start);
                    var dgNames = new List<string>();
                    foreach (var dgR in dgPre) dgNames.Add($"{dgR.kind}@{dgDist[dgR.id]:F0}m");
                    sb.AppendLine($"seme {seed} piano {floor}: navmesh x {dgMinX:F0}..{dgMaxX:F0} z {dgMinZ:F0}..{dgMaxZ:F0} m; piano x {plan.minX * 0.1f:F0}..{plan.maxX * 0.1f:F0} z {plan.minZ * 0.1f:F0}..{plan.maxZ * 0.1f:F0} m; {plan.units} ali, {plan.rooms.Count} stanze; prima della riparazione: {dgPre.Count} stanze irraggiungibili [{string.Join(" ", dgNames.GetRange(0, Mathf.Min(8, dgNames.Count)))}], {dgCandles} candele, uscita {(dgExit ? "no" : "ok")}");
                    int stripped = Connectivity.Repair(fi, out int before);
                    if (before > 0) { rawProblems++; rawRooms += stripped; clearedAll += Connectivity.LastCleared; }
                    if (stripped > 0) sb.AppendLine($"seme {seed} piano {floor}: la riparazione ha svuotato {string.Join(", ", Connectivity.LastStripped)} ({before} problemi prima, {Connectivity.LastCleared} arredi tolti)");
                    var left = Connectivity.Unreachable(fi, out int cbLeft, out bool exitLeft);
                    if (left.Count + cbLeft + (exitLeft ? 1 : 0) > 0)
                    {
                        sb.AppendLine($"seme {seed} piano {floor}: DOPO LA RIPARAZIONE restano {left.Count} stanze, {cbLeft} candele, uscita {(exitLeft ? "no" : "ok")}");
                        stillBad++;
                    }
                    if (propStats != null)
                    {
                        string th = plan.theme.ToString();
                        themeFloors[th] = themeFloors.TryGetValue(th, out int tf) ? tf + 1 : 1;
                        foreach (var pr in fi.propRoots)
                            foreach (Transform pt in pr)
                            {
                                var prm = fi.RoomAt(pt.position);
                                string pk = prm != null ? prm.kind.ToString() : "?";
                                if (!propStats.TryGetValue(pt.name, out var pd)) propStats[pt.name] = pd = new SortedDictionary<string, int>();
                                pd[pk] = pd.TryGetValue(pk, out int pc) ? pc + 1 : 1;
                            }
                    }
                }
                bool bad = false;
                var path = new NavMeshPath();
                var lift = fi.arrival;
                if (lift == null || !NavMesh.SamplePosition(lift.frontPoint, out NavMeshHit fh, 2.5f, NavMesh.AllAreas))
                {
                    sb.AppendLine($"seme {seed} piano {floor}: arrivo fuori dalla navmesh"); badFloors++;
                    Cleanup(fi);
                    continue;
                }
                bool Reach(Vector3 target, float tol, out string why)
                {
                    why = "";
                    if (!NavMesh.SamplePosition(target, out NavMeshHit th, tol, NavMesh.AllAreas)) { why = "fuori navmesh"; return false; }
                    if (!NavMesh.CalculatePath(fh.position, th.position, NavMesh.AllAreas, path)) { why = "nessun percorso"; return false; }
                    if (path.status != NavMeshPathStatus.PathComplete)
                    {
                        var cs = path.corners;
                        var last = cs.Length > 0 ? cs[cs.Length - 1] : Vector3.zero;
                        var rr = fi.RoomAt(last);
                        why = $"percorso parziale, si ferma a ({last.x:F1},{last.z:F1}) in {(rr != null ? rr.ToString() : "?")}";
                        return false;
                    }
                    return true;
                }
                // la cabina di arrivo deve collegarsi alla navmesh della stanza
                if (!NavMesh.SamplePosition(lift.spawnPoint, out NavMeshHit sp, 1.5f, NavMesh.AllAreas)) { liftBad++; bad = true; sb.AppendLine($"seme {seed} piano {floor}: cabina di arrivo fuori navmesh"); }
                else if (!NavMesh.CalculatePath(sp.position, fh.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) { liftBad++; bad = true; sb.AppendLine($"seme {seed} piano {floor}: cabina isolata dalla stanza ({path.status})"); }
                foreach (var r in plan.rooms)
                {
                    if (r.IsLift) continue;
                    if (r.type == RoomType.Segreta) { secrets++; continue; }     // pianta a griglia: si entra solo a carponi da un passaggio basso, la navmesh (agente alto 1,9 m) non ci arriva
                    roomsAll++;
                    var ctr = new Vector3(r.CenterX, 0f, r.CenterZ);
                    // il centro puo' cadere su un arredo: si prova qualche punto attorno
                    bool ok = false; string why = "";
                    foreach (var off in new[] { Vector3.zero, new Vector3(1.2f, 0, 0), new Vector3(-1.2f, 0, 0), new Vector3(0, 0, 1.2f), new Vector3(0, 0, -1.2f), new Vector3(2.4f, 0, 2.4f) })
                        if (Reach(ctr + off, 1.0f, out why)) { ok = true; break; }
                    // il centro di una sala piena di tavoli puo' cadere su un'isola: basta un punto subito dentro una porta che si raggiunge
                    if (!ok)
                        foreach (var o in plan.openings)
                        {
                            if ((o.a != r.id && o.b != r.id) || o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || o.style == OpenStyle.Passaggio) continue;
                            float dx = o.alongX ? o.center * 0.1f : o.line * 0.1f, dz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                            Vector3 inw = o.alongX ? new Vector3(0f, 0f, Mathf.Sign(r.CenterZ - dz)) : new Vector3(Mathf.Sign(r.CenterX - dx), 0f, 0f);
                            if (Reach(new Vector3(dx, 0f, dz) + inw * 0.9f, 0.5f, out _)) { ok = true; break; }
                        }
                    if (!ok)
                    {
                        roomsBad++; bad = true;
                        string k = r.kind.ToString();
                        kindBad[k] = kindBad.TryGetValue(k, out int c) ? c + 1 : 1;
                        sb.AppendLine($"seme {seed} piano {floor}: stanza {r} irraggiungibile ({why})");
                    }
                }
                foreach (var c in fi.candles)
                    if (!Reach(c.transform.position, 0.9f, out string why)) { candlesBad++; bad = true; sb.AppendLine($"seme {seed} piano {floor}: candela irraggiungibile ({why})"); }
                foreach (var gallery in fi.galleries)
                    for (int point = 1; point < gallery.points.Length - 1; point++)
                    {
                        upperAll++;
                        if (!Reach(gallery.points[point], 0.6f, out string upperWhy))
                        {
                            upperBad++; bad = true;
                            sb.AppendLine($"seme {seed} piano {floor}: balconata sala {gallery.room.id} punto {point} irraggiungibile ({upperWhy})");
                        }
                    }
                if (fi.exit != null && !Reach(fi.exit.frontPoint, 1.5f, out string why2))
                {
                    exitBad++; bad = true;
                    var hits = Physics.OverlapSphere(fi.exit.frontPoint + Vector3.up * 0.6f, 0.5f);
                    var names = new List<string>();
                    foreach (var h in hits) names.Add(h.transform.parent != null ? h.transform.parent.name + "/" + h.name : h.name);
                    sb.AppendLine($"seme {seed} piano {floor}: uscita irraggiungibile ({why2}); davanti alla porta: {string.Join(", ", names)}; stanza ospite {(plan.rooms[plan.exit])}");
                }
                if (bad) badFloors++;
                Cleanup(fi);
            }
            sb.AppendLine($"[PryingEyes] CHECK piani con problemi prima della riparazione: {rawProblems} su {n} ({rawRooms} stanze svuotate, {clearedAll} arredi tolti dai varchi); ancora rotti dopo: {stillBad}");
            sb.AppendLine($"[PryingEyes] CHECK {n} piani: con problemi {badFloors}; stanze {roomsAll} (piu' {secrets} segrete a carponi), irraggiungibili {roomsBad}; candele irraggiungibili {candlesBad}; uscite {exitBad}; cabine isolate {liftBad}; " +
                          $"costruzione media {tBuild / n * 1000f:F0} ms, navmesh media {tNav / n * 1000f:F0} ms");
            foreach (var kv in kindBad) sb.AppendLine($"[PryingEyes] CHECK irraggiungibili per tipo: {kv.Key} x{kv.Value}");
            sb.AppendLine($"[PryingEyes] CHECK balconate: {upperAll} punti superiori, {upperBad} irraggiungibili dall'arrivo");
            Debug.Log(sb.ToString());
            System.IO.File.WriteAllText("C:/Users/danie/Desktop/prying-eyes/hotel_build/proccheck.txt", sb.ToString());
            if (propStats != null)
            {
                var ps = new StringBuilder();
                var tl = new List<string>();
                foreach (var kv in themeFloors) tl.Add($"{kv.Key} {kv.Value}");
                ps.AppendLine($"piani per tema: {string.Join(", ", tl)}");
                foreach (var kv in propStats)
                {
                    int tot = 0; var parts = new List<string>();
                    foreach (var k2 in kv.Value) { tot += k2.Value; parts.Add($"{k2.Key} {k2.Value}"); }
                    ps.AppendLine($"{kv.Key,-18} {tot,6}  [{string.Join(", ", parts)}]");
                }
                System.IO.File.WriteAllText("C:/Users/danie/Desktop/prying-eyes/hotel_build/propstats.txt", ps.ToString());
            }
        }

        /// <summary>Mappa ASCII della stanza che ospita l'arrivo (e dei suoi vicini): '#' fuori navmesh, '.' raggiungibile dall'arrivo,
        /// 'o' sulla navmesh ma isolato, 'S' arrivo. Argomenti -seed N -floor N.</summary>
        public static void Ascii()
        {
            int seed = int.TryParse(Arg("-seed"), out int a) ? a : 1, floor = int.TryParse(Arg("-floor"), out int b) ? b : 1;
            EditorSceneManager.OpenScene(TorreBuilder.ScenePath);
            var player = GameObject.Find("Player"); if (player != null) player.SetActive(false);
            TorreBuilder.FillKit(Object.FindFirstObjectByType<ProcKit>());
            var plan = PlanGen.Generate(seed, floor);
            var fi = FloorBuilder.Build(plan, null);
            Physics.SyncTransforms();
            HotelNav.Rebuild(fi.bounds, fi.propRoots);
            var sb = new StringBuilder();
            if (!Connectivity.StartPoint(fi, out Vector3 s)) { sb.AppendLine("arrivo fuori navmesh"); }
            else
            {
                var lo = plan.openings.Find(q => q.style == OpenStyle.Ascensore && (q.a == plan.start || q.b == plan.start));
                var host = plan.rooms[lo.a == plan.start ? lo.b : lo.a];
                if (Arg("-kind") != null) host = plan.rooms.Find(q => q.kind.ToString() == Arg("-kind")) ?? host;
                sb.AppendLine($"arrivo ({s.x:F1},{s.z:F1}) stanza ospite {host}; aperture:");
                foreach (var o in plan.openings) if (o.a == host.id || o.b == host.id) sb.AppendLine($"   {o.style} {(o.alongX ? "z" : "x")}={o.line * 0.1f:F1} centro {o.center * 0.1f:F1} larg {o.width * 0.1f:F1} verso {(o.b < 0 ? "esterno" : plan.rooms[o.a == host.id ? o.b : o.a].ToString())}");
                var path = new NavMeshPath();
                for (float z = host.z1 * 0.1f - 0.1f; z > host.z0 * 0.1f; z -= 0.3f)
                {
                    var row = new StringBuilder();
                    for (float x = host.x0 * 0.1f + 0.1f; x < host.x1 * 0.1f; x += 0.3f)
                    {
                        var p = new Vector3(x, 0f, z);
                        char ch = '#';
                        if (NavMesh.SamplePosition(p, out NavMeshHit h, 0.2f, NavMesh.AllAreas))
                        {
                            bool ok = NavMesh.CalculatePath(s, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                            ch = ok ? '.' : 'o';
                        }
                        if (Mathf.Abs(x - s.x) < 0.2f && Mathf.Abs(z - s.z) < 0.2f) ch = 'S';
                        row.Append(ch);
                    }
                    sb.AppendLine(row.ToString());
                }
            }
            // chi sta nelle righe vietate? colliders attorno a un punto (-pz) dato da riga di comando
            if (float.TryParse(Arg("-pz"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pz))
            {
                var lo2 = plan.openings.Find(q => q.style == OpenStyle.Ascensore && (q.a == plan.start || q.b == plan.start));
                var host2 = plan.rooms[lo2.a == plan.start ? lo2.b : lo2.a];
                for (float x = host2.x0 * 0.1f + 0.5f; x < host2.x1 * 0.1f; x += 1.0f)
                {
                    var hits = Physics.OverlapSphere(new Vector3(x, 0.6f, pz), 0.28f);
                    var names = new List<string>();
                    foreach (var h in hits) names.Add((h.transform.parent != null ? h.transform.parent.name + "/" : "") + h.name + (h.gameObject.layer != 0 ? "#L" + h.gameObject.layer : ""));
                    sb.AppendLine($"x={x:F1} z={pz:F1}: {string.Join(", ", names)}");
                }
            }
            Debug.Log(sb.ToString());
            System.IO.File.WriteAllText("C:/Users/danie/Desktop/prying-eyes/hotel_build/procascii.txt", sb.ToString());
        }

        static void Cleanup(FloorInstance fi)
        {
            Object.DestroyImmediate(fi.root);
            Interact.Clear();
            LightPool.Clear();
        }
    }
}

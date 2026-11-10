using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes.EditorTools
{
    /// <summary>Diagnostica della navmesh di un piano: una cella ogni 0,25 m in un PPM. Bianco = camminabile, grigio = pavimento
    /// di una stanza senza navmesh, rosso = stanza irraggiungibile senza navmesh, arancio = irraggiungibile con navmesh, verde = arrivo.
    /// Argomenti: -seed N -floor N -out file.ppm</summary>
    public static class NavMap
    {
        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public static void Run()
        {
            int seed = int.TryParse(Arg("-seed"), out int sd) ? sd : 3017;
            int floor = int.TryParse(Arg("-floor"), out int fl) ? fl : 2;
            string outPath = Arg("-out") ?? "C:/Users/danie/Desktop/prying-eyes/hotel_build/navmap.ppm";
            EditorSceneManager.OpenScene(TorreBuilder.ScenePath);
            var player = GameObject.Find("Player");
            if (player != null) player.SetActive(false);
            var kit = Object.FindFirstObjectByType<ProcKit>();
            TorreBuilder.FillKit(kit);
            var plan = PlanGen.Generate(seed, floor);
            if (plan == null) { Debug.LogError("[NavMap] piano non generato"); return; }
            LightPool.Clear();
            var fi = FloorBuilder.Build(plan, null);
            Physics.SyncTransforms();
            HotelNav.Rebuild(fi.bounds, fi.propRoots);

            const float cell = 0.25f;
            float x0 = plan.minX * 0.1f - 1f, z0 = plan.minZ * 0.1f - 1f;
            int W = Mathf.CeilToInt((plan.maxX * 0.1f - x0 + 1f) / cell), H = Mathf.CeilToInt((plan.maxZ * 0.1f - z0 + 1f) / cell);
            var rgb = new byte[W * H * 3];
            var bad = Connectivity.Unreachable(fi, out int candlesBad, out bool exitBad);
            var badSet = new HashSet<Room>(bad);
            bool hasStart = Connectivity.StartPoint(fi, out Vector3 start);
            int ci = hasStart ? Mathf.FloorToInt((start.x - x0) / cell) : -1, cj = hasStart ? Mathf.FloorToInt((start.z - z0) / cell) : -1;
            int navCells = 0, roomCells = 0;
            var nav = new bool[W * H];
            var roomAt = new Room[W * H];
            for (int j = 0; j < H; j++)
            {
                for (int i = 0; i < W; i++)
                {
                    float x = x0 + (i + 0.5f) * cell, z = z0 + (j + 0.5f) * cell;
                    var p = new Vector3(x, 0f, z);
                    int idx = j * W + i;
                    roomAt[idx] = fi.RoomAt(p);
                    nav[idx] = NavMesh.SamplePosition(p, out _, 0.05f, NavMesh.AllAreas);
                    if (roomAt[idx] != null) roomCells++;
                    if (nav[idx]) navCells++;
                }
            }
            // componente della navmesh raggiungibile dall'arrivo (vicini a 4 sulla griglia)
            var comp = new bool[W * H];
            int seedIdx = -1;
            if (hasStart)
                for (int dj = -6; dj <= 6 && seedIdx < 0; dj++)
                    for (int di = -6; di <= 6 && seedIdx < 0; di++)
                    {
                        int ii = ci + di, jj = cj + dj;
                        if (ii < 0 || jj < 0 || ii >= W || jj >= H) continue;
                        if (nav[jj * W + ii]) seedIdx = jj * W + ii;
                    }
            float cMinX = float.MaxValue, cMaxX = float.MinValue, cMinZ = float.MaxValue, cMaxZ = float.MinValue;
            int compCells = 0;
            if (seedIdx >= 0)
            {
                var q = new Queue<int>();
                void Visit(int n) { if (nav[n] && !comp[n]) { comp[n] = true; q.Enqueue(n); } }
                q.Enqueue(seedIdx); comp[seedIdx] = true;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    int ci2 = c % W, cj2 = c / W;
                    compCells++;
                    float cx = x0 + (ci2 + 0.5f) * cell, cz = z0 + (cj2 + 0.5f) * cell;
                    cMinX = Mathf.Min(cMinX, cx); cMaxX = Mathf.Max(cMaxX, cx); cMinZ = Mathf.Min(cMinZ, cz); cMaxZ = Mathf.Max(cMaxZ, cz);
                    if (ci2 > 0) Visit(c - 1);
                    if (ci2 < W - 1) Visit(c + 1);
                    if (cj2 > 0) Visit(c - W);
                    if (cj2 < H - 1) Visit(c + W);
                }
            }
            for (int j = 0; j < H; j++)
            {
                for (int i = 0; i < W; i++)
                {
                    int idx = j * W + i;
                    var room = roomAt[idx];
                    byte r = 8, g = 8, b = 10;
                    if (room != null) { r = 70; g = 70; b = 70; }
                    if (nav[idx]) { r = 240; g = 150; b = 40; }                       // navmesh non collegata all'arrivo
                    if (comp[idx]) { r = 235; g = 235; b = 235; }                     // collegata all'arrivo
                    if (room != null && badSet.Contains(room) && !nav[idx]) { r = 200; g = 40; b = 40; }
                    if (i == ci && j == cj) { r = 40; g = 230; b = 60; }
                    int row = H - 1 - j;                       // z verso l'alto
                    int k = (row * W + i) * 3;
                    rgb[k] = r; rgb[k + 1] = g; rgb[k + 2] = b;
                }
            }
            Debug.Log($"[NavMap] componente dell'arrivo: {compCells} celle su {navCells} di navmesh, x {cMinX:F1}..{cMaxX:F1} z {cMinZ:F1}..{cMaxZ:F1} m; arrivo x {start.x:F1} z {start.z:F1}");
            // profilo del corridoio della stanza 0 lungo la sua lunghezza: C = raggiungibile, o = navmesh non raggiungibile, . = nessuna navmesh
            var corr = plan.rooms[0];
            foreach (float zz in new[] { corr.z0 * 0.1f + 0.4f, corr.CenterZ, corr.z1 * 0.1f - 0.4f })
            {
                var profile = new System.Text.StringBuilder();
                for (float xx = corr.x0 * 0.1f - 0.5f; xx <= corr.x1 * 0.1f + 0.5f; xx += 0.25f)
                {
                    int ii = Mathf.FloorToInt((xx - x0) / cell), jj = Mathf.FloorToInt((zz - z0) / cell);
                    char ch = '.';
                    if (ii >= 0 && jj >= 0 && ii < W && jj < H) { int id2 = jj * W + ii; if (comp[id2]) ch = 'C'; else if (nav[id2]) ch = 'o'; }
                    profile.Append(ch);
                }
                Debug.Log($"[NavMap] corridoio {corr.kind}#{corr.id} z {zz:F1} x {corr.x0 * 0.1f:F1}..{corr.x1 * 0.1f:F1}: {profile}");
                if (zz == corr.CenterZ)
                {
                    // il primo tratto raggiungibile: il buco della navmesh e' appena prima, e li' cerchiamo il collider che lo tappa
                    string ps = profile.ToString();
                    int firstC = ps.IndexOf('C');
                    if (firstC > 2)
                    {
                        float xg = corr.x0 * 0.1f - 0.5f + (firstC - 2) * 0.25f;
                        var names = new List<string>();
                        foreach (var col in Physics.OverlapBox(new Vector3(xg, 1f, zz), new Vector3(0.6f, 1f, 1.6f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                            names.Add(col.gameObject.name + "@" + (col.transform.parent != null ? col.transform.parent.name : "-") + " L" + col.gameObject.layer);
                        Debug.Log($"[NavMap] buco della navmesh verso x {xg:F2}: {string.Join(", ", names)}");
                    }
                }
            }
            // porte fra una stanza raggiungibile dall'arrivo e una no: il taglio a livello di stanza (stesso test della riparazione)
            int roomCut = 0;
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided) continue;
                var ra = plan.rooms[o.a]; var rb = plan.rooms[o.b];
                if (ra.IsLift || rb.IsLift) continue;
                bool okA = Connectivity.RoomReach(start, ra), okB = Connectivity.RoomReach(start, rb);
                if (okA == okB) continue;
                roomCut++;
                if (roomCut > 10) continue;
                float dx = o.alongX ? o.center * 0.1f : o.line * 0.1f, dz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                Debug.Log($"[NavMap] porta fra stanze raggiungibili e no: {ra.kind}#{ra.id} ({(okA ? "si" : "no")}) | {rb.kind}#{rb.id} ({(okB ? "si" : "no")}) in x {dx:F1} z {dz:F1}, larga {o.width * 0.1f:F1} m, stile {o.style}");
            }
            Debug.Log($"[NavMap] porte fra stanze raggiungibili e no: {roomCut}");
            // frontiera: celle raggiunte con un vicino navigabile che non lo e' (la frattura della navmesh), e le porte vicine
            var border = new List<Vector2>();
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    int idx = j * W + i;
                    if (!comp[idx]) continue;
                    bool edge = (i > 0 && nav[idx - 1] && !comp[idx - 1]) || (i < W - 1 && nav[idx + 1] && !comp[idx + 1])
                             || (j > 0 && nav[idx - W] && !comp[idx - W]) || (j < H - 1 && nav[idx + W] && !comp[idx + W]);
                    if (edge) border.Add(new Vector2(x0 + (i + 0.5f) * cell, z0 + (j + 0.5f) * cell));
                }
            Debug.Log($"[NavMap] celle di frontiera: {border.Count}");
            foreach (var bp in border.GetRange(0, Mathf.Min(8, border.Count))) Debug.Log($"[NavMap] frontiera x {bp.x:F1} z {bp.y:F1}");
            int nearLog = 0;
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided || nearLog >= 10) continue;
                float dx = o.alongX ? o.center * 0.1f : o.line * 0.1f, dz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                var mid = new Vector2(dx, dz);
                if (!border.Exists(bp => Vector2.Distance(bp, mid) < 3.0f)) continue;
                nearLog++;
                Debug.Log($"[NavMap] porta vicina alla frontiera: {plan.rooms[o.a].kind}#{o.a} | {plan.rooms[o.b].kind}#{o.b} in x {dx:F1} z {dz:F1}, larga {o.width * 0.1f:F1} m, stile {o.style}, alongX {o.alongX}");
            }
            // porte che separano la componente dell'arrivo dal resto: un lato dentro, l'altro fuori
            int cutLog = 0;
            foreach (var o in plan.openings)
            {
                if (o.b < 0 || o.oneSided) continue;
                float dx = o.alongX ? o.center * 0.1f : o.line * 0.1f, dz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
                Vector3 p1 = o.alongX ? new Vector3(dx, 0f, dz - 0.6f) : new Vector3(dx - 0.6f, 0f, dz);
                Vector3 p2 = o.alongX ? new Vector3(dx, 0f, dz + 0.6f) : new Vector3(dx + 0.6f, 0f, dz);
                int i1 = Mathf.FloorToInt((p1.x - x0) / cell), j1 = Mathf.FloorToInt((p1.z - z0) / cell);
                int i2 = Mathf.FloorToInt((p2.x - x0) / cell), j2 = Mathf.FloorToInt((p2.z - z0) / cell);
                if (i1 < 0 || j1 < 0 || i1 >= W || j1 >= H || i2 < 0 || j2 < 0 || i2 >= W || j2 >= H) continue;
                int n1 = j1 * W + i1, n2 = j2 * W + i2;
                // taglio: un lato raggiunto e l'altro no, oppure un lato raggiunto e l'altro bloccato
                bool cut = (comp[n1] != comp[n2] && (nav[n1] || nav[n2])) || ((comp[n1] || comp[n2]) && (!nav[n1] || !nav[n2]));
                if (!cut) continue;
                cutLog++;
                if (cutLog > 12) continue;
                var blockers = new List<string>();
                foreach (var pt in new[] { p1, p2 })
                    foreach (var col in Physics.OverlapBox(pt + Vector3.up * 0.9f, new Vector3(0.7f, 0.9f, 0.7f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                        if (!col.transform.IsChildOf(fi.root.transform) || col.gameObject.name != "Guscio") blockers.Add(col.gameObject.name + "@" + col.transform.parent?.name);
                Debug.Log($"[NavMap] porta taglia: {plan.rooms[o.a].kind}#{o.a} | {plan.rooms[o.b].kind}#{o.b}, linea {o.line * 0.1f:F1} m, centro {o.center * 0.1f:F1} m, larga {o.width * 0.1f:F1} m, stile {o.style}, alongX {o.alongX}; lato1 comp {comp[n1]} nav {nav[n1]} stanza {roomAt[n1]?.kind}; lato2 comp {comp[n2]} nav {nav[n2]} stanza {roomAt[n2]?.kind}; ostacoli: {string.Join(", ", blockers)}");
            }
            Debug.Log($"[NavMap] porte che separano l'arrivo dal resto: {cutLog}");
            var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{W} {H}\n255\n");
            var all = new byte[header.Length + rgb.Length];
            System.Buffer.BlockCopy(header, 0, all, 0, header.Length);
            System.Buffer.BlockCopy(rgb, 0, all, header.Length, rgb.Length);
            File.WriteAllBytes(outPath, all);
            Debug.Log($"[NavMap] seme {seed} piano {floor}: {W}x{H} celle, navmesh {navCells}, stanze {roomCells}, irraggiungibili {bad.Count} (candele {candlesBad}, uscita {(exitBad ? "no" : "ok")}) -> {outPath}");
        }
    }
}

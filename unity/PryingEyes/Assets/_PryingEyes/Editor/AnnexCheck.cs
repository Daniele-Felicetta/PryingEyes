using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes.EditorTools
{
    /// <summary>Controllo degli annessi su molti piani, in edit-mode e col layout del ciclo a ondate. Per ogni piano: costruisce, mette cancelli e
    /// leve, ricostruisce la navmesh e riparala. Poi, per ogni annesso: (1) ingresso, interno, tesoro e chiave si raggiungono dall'arrivo;
    /// (2) leve e altare del tributo stanno sulla navmesh e si raggiungono senza aprire niente; (3) il cancello e' sul livello delle porte, con
    /// il suo collider; (4) con i cancelli come muri pieni (seconda navmesh, con le porte dentro) l'interno, il tesoro e la chiave NON si
    /// raggiungono dall'ingresso: nessuna via laterale; (5) ogni stanza e l'uscita restano raggiungibili.
    /// Argomenti: -seeds N (60), -seed0 N (9000), -out file (hotel_build/annexcheck.txt).</summary>
    public static class AnnexCheck
    {
        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static int ArgInt(string name, int def) => int.TryParse(Arg(name), out int v) ? v : def;

        /// <summary>La navmesh con i collider delle porte dentro (HotelNav li lascia fuori perche' si aprono): i cancelli degli annessi diventano
        /// muri pieni e si vede se qualche altra via porta dentro.</summary>
        static NavMeshDataInstance SealedBake(FloorInstance fi, out NavMeshData data)
        {
            var sources = new List<NavMeshBuildSource>();
            int mask = ~((1 << 2) | (1 << HotelNav.EnemyLayer));
            var markups = new List<NavMeshBuildMarkup>();
            foreach (var b in fi.propRoots)
                if (b != null) markups.Add(new NavMeshBuildMarkup { root = b, area = 1, overrideArea = true, applyToChildren = true });
            NavMeshBuilder.CollectSources(fi.bounds, mask, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            var s = NavMesh.GetSettingsByID(0);
            s.agentRadius = HotelNav.AgentRadius; s.agentHeight = 1.9f; s.agentClimb = HotelNav.AgentClimb; s.agentSlope = 45f;
            s.overrideVoxelSize = true; s.voxelSize = HotelNav.VoxelSize;
            s.overrideTileSize = true; s.tileSize = 2048;
            data = NavMeshBuilder.BuildNavMeshData(s, sources, fi.bounds, Vector3.zero, Quaternion.identity);
            return data != null ? NavMesh.AddNavMeshData(data) : default;
        }

        public static void Run()
        {
            int n = ArgInt("-seeds", 60), seed0 = ArgInt("-seed0", 9000);
            string outFile = Arg("-out") ?? "C:/Users/danie/Desktop/prying-eyes/hotel_build/annexcheck.txt";
            EditorSceneManager.OpenScene(TorreBuilder.ScenePath);
            var player = GameObject.Find("Player");
            if (player != null) player.SetActive(false);
            TorreBuilder.FillKit(Object.FindFirstObjectByType<ProcKit>());
            PlanGen.SurvivalLayout = true;
            var sb = new StringBuilder();
            int floors = 0, badFloors = 0, annexes = 0, annexBad = 0, leaks = 0, notReach = 0, roomsBad = 0, exitBad = 0, notSealedOnNav = 0, leverBad = 0, levers = 0, ungenerated = 0;
            int towers = 0, crypts = 0, gardens = 0, tall = 0;
            float tBuild = 0f, tNav = 0f, tSealed = 0f;
            long lights = 0;
            for (int i = 0; i < n; i++)
            {
                int seed = seed0 + i * 31, floor = 1 + i % 12;
                var plan = PlanGen.Generate(seed, floor);
                if (plan == null) { sb.AppendLine($"seme {seed} piano {floor}: piano non generato ({PlanGen.LastFailure})"); ungenerated++; badFloors++; continue; }
                LightPool.Clear();
                float t0 = Time.realtimeSinceStartup;
                var fi = FloorBuilder.Build(plan, null);
                Physics.SyncTransforms();
                AnnexRuntime.Place(fi);
                Physics.SyncTransforms();
                float t1 = Time.realtimeSinceStartup;
                HotelNav.Rebuild(fi.bounds, fi.propRoots);
                float t2 = Time.realtimeSinceStartup;
                Connectivity.Repair(fi, out int before);
                tBuild += t1 - t0; tNav += t2 - t1; lights += LightPool.Count;
                floors++;
                foreach (var r in plan.rooms) if (r.wallH >= 13f) tall++;
                bool bad = false;
                string head = $"seme {seed} piano {floor}";

                var left = Connectivity.Unreachable(fi, out int cbLeft, out bool exitLeft);
                if (left.Count > 0) { roomsBad += left.Count; bad = true; sb.AppendLine($"{head}: dopo la riparazione restano {left.Count} stanze irraggiungibili"); }
                if (exitLeft) { exitBad++; bad = true; sb.AppendLine($"{head}: uscita irraggiungibile"); }

                var lift = fi.arrival;
                if (lift == null || !NavMesh.SamplePosition(lift.frontPoint, out NavMeshHit fh, 2.5f, NavMesh.AllAreas))
                {
                    sb.AppendLine($"{head}: arrivo fuori dalla navmesh"); badFloors++; Cleanup(fi); continue;
                }
                var path = new NavMeshPath();
                bool Reach(Vector3 target, float tol, out string why)
                {
                    why = "";
                    if (!NavMesh.SamplePosition(target, out NavMeshHit th, tol, NavMesh.AllAreas)) { why = "fuori navmesh"; return false; }
                    if (!NavMesh.CalculatePath(fh.position, th.position, NavMesh.AllAreas, path)) { why = "nessun percorso"; return false; }
                    if (path.status != NavMeshPathStatus.PathComplete) { why = "percorso parziale"; return false; }
                    return true;
                }

                foreach (var route in fi.galleries)
                    for (int point = 0; point < route.points.Length; point++)
                    {
                        var p = route.points[point];
                        if (p.y < 3f || Reach(p, 0.6f, out string why)) continue;
                        bad = true;
                        sb.AppendLine($"{head}: gallery room {route.room.id} point {point} at {p} unreachable ({why})");
                    }

                foreach (var s in fi.annexSites)
                {
                    annexes++;
                    if (s.data.kind == AnnexKind.Torre) towers++; else if (s.data.kind == AnnexKind.Cripta) crypts++; else gardens++;
                    string tag = $"{head}: {s.data} ({s.data.gate})";
                    bool siteBad = false;
                    if (s.closer == null || s.closerCol == null || s.closerCol.gameObject.layer != HotelNav.DoorLayer)
                    { siteBad = true; sb.AppendLine($"{tag}: cancello senza collider sul livello delle porte"); }
                    if (!Reach(s.outside, 1.5f, out string w1)) { siteBad = true; notReach++; sb.AppendLine($"{tag}: ingresso non raggiungibile ({w1})"); }
                    if (!Reach(s.inside, 1.5f, out string w2)) { siteBad = true; notReach++; sb.AppendLine($"{tag}: interno non raggiungibile ({w2})"); }
                    if (!Reach(s.rewardPos, 1.5f, out string w3)) { siteBad = true; notReach++; sb.AppendLine($"{tag}: tesoro non raggiungibile ({w3})"); }
                    var keyBox = s.props.GetComponentInChildren<AnnexKey>(true);
                    if (keyBox != null && !Reach(s.keyPos, 1.5f, out string w4)) { siteBad = true; notReach++; sb.AppendLine($"{tag}: chiave non raggiungibile ({w4})"); }
                    foreach (var lv in s.props.GetComponentsInChildren<AnnexLever>(true))
                    {
                        levers++;
                        if (!Reach(lv.Point - Vector3.up * 0.95f, 1.6f, out string w5)) { siteBad = true; leverBad++; sb.AppendLine($"{tag}: leva in {lv.transform.position} non raggiungibile ({w5})"); }
                    }
                    var altar = s.props.GetComponentInChildren<AnnexAltar>(true);
                    if (altar != null && !Reach(altar.transform.position, 1.8f, out string w6)) { siteBad = true; notReach++; sb.AppendLine($"{tag}: altare non raggiungibile ({w6})"); }
                    if (siteBad) { annexBad++; bad = true; }
                }

                // ---- cancelli chiusi come muri: l'interno non si raggiunge dall'ingresso
                if (fi.annexSites.Count > 0)
                {
                    float t3 = Time.realtimeSinceStartup;
                    HotelNav.Clear();
                    var inst = SealedBake(fi, out NavMeshData sealedData);
                    tSealed += Time.realtimeSinceStartup - t3;
                    foreach (var s in fi.annexSites)
                    {
                        string tag = $"{head}: {s.data} ({s.data.gate})";
                        bool Between(Vector3 from, Vector3 to, out string state)
                        {
                            state = "";
                            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 1.5f, NavMesh.AllAreas)) { state = "ingresso fuori dalla navmesh sigillata"; return true; }     // anomalo, ma non e' una via laterale
                            if (!NavMesh.SamplePosition(to, out NavMeshHit b, 1.5f, NavMesh.AllAreas)) { state = "fuori dalla navmesh sigillata"; return false; }
                            return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                        }
                        foreach (var target in new[] { ("interno", s.inside), ("tesoro", s.rewardPos), ("chiave", s.keyPos) })
                        {
                            if (target.Item1 == "chiave" && s.props.GetComponentInChildren<AnnexKey>(true) == null) continue;
                            bool reached = Between(s.outside, target.Item2, out string st);
                            if (reached) { leaks++; bad = true; sb.AppendLine($"{tag}: VIA LATERALE, a cancello chiuso {target.Item1} si raggiunge dall'ingresso"); }
                            else if (st != "") { notSealedOnNav++; }
                        }
                    }
                    if (inst.valid) NavMesh.RemoveNavMeshData(inst);
                    if (sealedData != null) Util.Kill(sealedData);
                }
                if (bad) badFloors++;
                Cleanup(fi);
            }
            sb.AppendLine($"[PryingEyes] ANNEXCHECK {floors} piani costruiti ({ungenerated} non generati), {tall} sale alte, {annexes} annessi (torri {towers}, cripte {crypts}, giardini {gardens}), " +
                          $"piani con problemi {badFloors}, annessi con problemi {annexBad}, punti non raggiungibili {notReach}, leve non raggiungibili {leverBad}/{levers}, " +
                          $"VIE LATERALI {leaks}, stanze irraggiungibili {roomsBad}, uscite {exitBad}");
            sb.AppendLine($"[PryingEyes] ANNEXCHECK costruzione media {tBuild / Mathf.Max(1, floors) * 1000f:F0} ms, navmesh media {tNav / Mathf.Max(1, floors) * 1000f:F0} ms, navmesh sigillata media {tSealed / Mathf.Max(1, floors) * 1000f:F0} ms, " +
                          $"luci medie {lights / (float)Mathf.Max(1, floors):F0}, punti interni fuori dalla navmesh sigillata {notSealedOnNav}");
            Debug.Log(sb.ToString());
            System.IO.File.WriteAllText(outFile, sb.ToString());
            EditorApplication.Exit(badFloors == 0 ? 0 : 1);
        }

        static void Cleanup(FloorInstance fi)
        {
            Object.DestroyImmediate(fi.root);
            Interact.Clear();
            LightPool.Clear();
        }
    }
}

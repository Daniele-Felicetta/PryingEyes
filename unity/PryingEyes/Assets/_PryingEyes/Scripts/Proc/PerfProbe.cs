using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>-autotest -perftest (senza -nographics): quanto costa davvero un fotogramma del piano. In batchmode Unity non
    /// disegna le camere da solo (gli FPS sono finti: 6-10 mila), quindi a ogni fotogramma la sonda chiama cam.Render() sulla
    /// camera del giocatore, che rende a 1920x1080 su una RenderTexture, e poi legge un pixel: la lettura aspetta la GPU. Il
    /// tempo misurato e' la somma di logica di gioco, render sulla CPU (culling, ombre, comandi) e coda sulla GPU: un po'
    /// pessimista, perche' nel gioco vero CPU e GPU si sovrappongono. Passa in rassegna la stanza piu' grande di ogni tipo,
    /// guardando dalla soglia principale lungo la stanza, e aggiunge i risultati a hotel_build/perf.txt. Con -perfshots salva
    /// anche una foto per inquadratura (hotel_build/perf/etichetta_tema_stanza.png): e' la vista vera del giocatore, con
    /// nebbia, luci e post-processing del gioco. -perftag mette un'etichetta ai file. Il confronto prima/dopo vale solo con
    /// lo stesso seme e lo stesso piano.</summary>
    public static class PerfProbe
    {
        const string LogPath = "C:/Users/danie/Desktop/prying-eyes/hotel_build/perf.txt";
        const string ShotDir = "C:/Users/danie/Desktop/prying-eyes/hotel_build/perf/";
        const int Warm = 25, Frames = 120, MaxViews = 14;

        static bool Has(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs()) if (s == a) return true;
            return false;
        }

        static string Arg(string name, string def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        static void SavePng(RenderTexture rt, string path)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        struct View { public Room room; public Vector3 pos; public float yaw; }

        public static IEnumerator Run(FloorDirector fd)
        {
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            var cam = Camera.main;
            if (pl == null || cam == null || fd.cur == null) { Application.Quit(); yield break; }
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            PlayerController.holdLook = true;
            pl.invulnerable = true;

            var plan = fd.cur.plan;
            var views = Pick(plan);
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);

            string tag = Arg("-perftag", "x");
            bool shots = Has("-perfshots");
            if (shots) Directory.CreateDirectory(ShotDir);
            var sb = new StringBuilder();
            sb.AppendLine($"== [{tag}] piano {fd.floor} tema {plan.theme} seme {fd.runSeed + fd.floor * 1013}: {plan.rooms.Count} stanze, {LightPool.Count} luci, " +
                          $"{SystemInfo.graphicsDeviceName}, 1920x1080");
            double sumMean = 0, worstP99 = 0; int n = 0;
            // La GPU a riposo sta a circa 1 GHz (di 3 possibili) e impiega qualche centinaio di millisecondi a salire: senza scaldarla il primo
            // giro misura una scheda lenta (dopo ProcCheck, che gira senza grafica, l'albergo risultava 3 volte piu' lento del solito).
            float warmStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warmStart < 3f) { yield return null; cam.Render(); }
            foreach (var v in views)
            {
                Enemy.ClearAll();
                pl.Teleport(v.pos);
                pl.yaw = v.yaw; pl.pitch = 0f;
                // assestamento: i primi fotogrammi pagano shader, pool delle luci e navmesh; si disegna comunque
                for (int i = 0; i < Warm; i++) { yield return null; cam.Render(); }
                LightPool.Tick(true);
                yield return null;

                var tot = new List<float>(Frames);
                double sl = 0, sr = 0, sg = 0;
                double prevEnd = Time.realtimeSinceStartupAsDouble;
                for (int i = 0; i < Frames; i++)
                {
                    yield return null;                                       // qui e' girata tutta la logica del fotogramma
                    double t1 = Time.realtimeSinceStartupAsDouble;
                    cam.Render();                                            // in batchmode la camera non si disegna da sola
                    double t2 = Time.realtimeSinceStartupAsDouble;
                    RenderTexture.active = rt;
                    one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);              // aspetta che la GPU abbia finito
                    RenderTexture.active = null;
                    double t3 = Time.realtimeSinceStartupAsDouble;
                    sl += t1 - prevEnd; sr += t2 - t1; sg += t3 - t2;
                    tot.Add((float)((t3 - prevEnd) * 1000.0));
                    prevEnd = t3;
                }
                if (shots)
                {
                    cam.Render();
                    SavePng(rt, ShotDir + tag + "_" + plan.theme + "_" + v.room.kind + ".png");
                }
                tot.Sort();
                float mean = 0f; foreach (var x in tot) mean += x; mean /= tot.Count;
                float p50 = tot[tot.Count / 2], p95 = tot[(int)(tot.Count * 0.95f)], p99 = tot[(int)(tot.Count * 0.99f)], max = tot[tot.Count - 1];
                sumMean += mean; n++; worstP99 = System.Math.Max(worstP99, p99);
                var r = v.room;
                if (Has("-perfdebug"))          // elenco delle luci della stanza: tipo, se sono accese, intensita', portata, altezza
                {
                    sb.AppendLine($"  -- luci in {r.kind}:");
                    foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    {
                        var p = l.transform.position;
                        if (p.x < r.x0 * 0.1f || p.x > r.x1 * 0.1f || p.z < r.z0 * 0.1f || p.z > r.z1 * 0.1f) continue;
                        sb.AppendLine($"     {l.name,-22} {l.type,-6} acceso={l.enabled} int={l.intensity,6:F1} portata={l.range,5:F1} y={p.y,4:F1} ombre={l.shadows} colore=({l.color.r:F2},{l.color.g:F2},{l.color.b:F2})");
                    }
                }
                sb.AppendLine($"  {r.kind,-13} {r.W / 10f,4:F1}x{r.D / 10f,-4:F1} m  luci {LightPool.Active,2}/{LightPool.Count}: medio {mean,5:F1} ms " +
                              $"(logica {sl / Frames * 1000.0,4:F1} + render CPU {sr / Frames * 1000.0,4:F1} + attesa GPU {sg / Frames * 1000.0,4:F1})  " +
                              $"p50 {p50,5:F1}  p95 {p95,5:F1}  p99 {p99,5:F1}  max {max,5:F1}");
            }
            sb.AppendLine($"  >> {n} inquadrature: medio {sumMean / System.Math.Max(1, n):F1} ms ({1000.0 / (sumMean / System.Math.Max(1, n)):F0} fps), peggior p99 {worstP99:F1} ms");
            File.AppendAllText(LogPath, sb.ToString());
            Debug.Log("[PryingEyes] perf: " + sb);

            cam.targetTexture = null;
            rt.Release();
            Application.Quit();
        }

        /// <summary>La stanza piu' grande di ogni tipo (al massimo MaxViews, le piu' grandi per prime) con il punto di vista.</summary>
        static List<View> Pick(Plan plan)
        {
            var best = new Dictionary<RoomKind, Room>();
            foreach (var r in plan.rooms)
            {
                if (r.IsLift) continue;
                if (!best.TryGetValue(r.kind, out var b) || r.W * r.D > b.W * b.D) best[r.kind] = r;
            }
            var rooms = new List<Room>(best.Values);
            rooms.Sort((a, b) => (b.W * b.D).CompareTo(a.W * a.D));
            if (rooms.Count > MaxViews) rooms.RemoveRange(MaxViews, rooms.Count - MaxViews);
            var res = new List<View>();
            foreach (var r in rooms) res.Add(ViewOf(plan, r));
            return res;
        }

        static View ViewOf(Plan plan, Room r)
        {
            Vector3 ctr = new Vector3(r.CenterX, 0f, r.CenterZ);
            Vector3 dir = r.W >= r.D ? Vector3.right : Vector3.forward;
            Vector3 pos = ctr;
            if (r.kind == RoomKind.Corridoio)
            {
                pos = ctr - dir * (Mathf.Max(r.W, r.D) * 0.05f - 1.5f);          // da un'estremita', lungo il corridoio
            }
            else
            {
                Opening best = null; int bw = -1;
                foreach (var o in plan.openings)
                {
                    if ((o.a != r.id && o.b != r.id) || o.b < 0 || o.oneSided) continue;
                    if (plan.rooms[o.a == r.id ? o.b : o.a].IsLift) continue;
                    if (o.width > bw) { bw = o.width; best = o; }
                }
                if (best != null)
                {
                    Vector3 wall = best.alongX ? new Vector3(best.center * 0.1f, 0f, best.line * 0.1f) : new Vector3(best.line * 0.1f, 0f, best.center * 0.1f);
                    dir = best.alongX ? new Vector3(0f, 0f, Mathf.Sign(ctr.z - wall.z)) : new Vector3(Mathf.Sign(ctr.x - wall.x), 0f, 0f);
                    pos = wall + dir * 0.9f;
                }
            }
            if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) pos = hit.position;
            return new View { room = r, pos = pos, yaw = Mathf.Atan2(dir.x, dir.z) };
        }
    }
}

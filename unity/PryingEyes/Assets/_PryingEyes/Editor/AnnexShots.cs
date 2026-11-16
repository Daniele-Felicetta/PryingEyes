using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes.EditorTools
{
    /// <summary>Foto degli annessi (torri, cripte, giardini) e delle cattedrali in edit-mode, a occhio del giocatore, col piano del ciclo a ondate.
    /// Argomenti: -seed N -floor N -tag x -out cartella (default hotel_build) -only nome. Prima le inquadrature a cancello chiuso, poi si
    /// aprono tutti gli annessi (OpenInstant) e si fotografano gli interni. Uscita: annex_seme_piano_nome.png e annex_seme_piano.txt.</summary>
    public static class AnnexShots
    {
        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static int ArgInt(string name, int def) => int.TryParse(Arg(name), out int v) ? v : def;

        struct Shot { public string name; public Vector3 pos, look; public float fov; }

        static Vector3 Dir(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad));
        static Vector3 Pol(Vector3 axis, float r, float deg, float y) => new Vector3(axis.x, 0f, axis.z) + Dir(deg) * r + Vector3.up * y;
        const float Eye = 1.62f;

        public static void Run()
        {
            ShaderUtil.allowAsyncCompilation = false;
            int seed = ArgInt("-seed", 9062), floor = ArgInt("-floor", 3);
            string tag = Arg("-tag") != null ? "_" + Arg("-tag") : "";
            string outDir = Arg("-out") ?? TestPaths.Root;
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(TorreBuilder.ScenePath);
            var player = GameObject.Find("Player");
            if (player != null) player.SetActive(false);
            TorreBuilder.FillKit(Object.FindFirstObjectByType<ProcKit>());
            LightPool.Clear();
            PlanGen.SurvivalLayout = true;
            var plan = PlanGen.Generate(seed, floor);
            if (plan == null) { Debug.LogError("piano non generato: " + PlanGen.LastFailure); return; }
            var fi = FloorBuilder.Build(plan, null);
            Physics.SyncTransforms();
            AnnexRuntime.Place(fi);
            Physics.SyncTransforms();

            var report = new StringBuilder();
            report.AppendLine($"seme {seed} piano {floor}: {plan.rooms.Count} stanze, {plan.annexes.Count} annessi, {LightPool.Count} luci");
            foreach (var a in plan.annexes)
            {
                var h = plan.rooms[a.host];
                report.AppendLine($"  annesso {a} in {h} ({h.W * 0.1f:F1}x{h.D * 0.1f:F1} m, alta {h.wallH:F1}, balconate {(h.upperGallery ? h.GalleryLevels.ToString() : "no")}), asse ({a.AxisX:F1},{a.AxisZ:F1}), yaw {a.yaw}, quota {a.FloorY:F1}");
            }
            foreach (var r in plan.rooms) if (r.wallH >= 12f) report.AppendLine($"  cattedrale {r} {r.W * 0.1f:F1}x{r.D * 0.1f:F1} m, alta {r.wallH:F1}, balconate {r.GalleryLevels}");

            var mainCam = GameObject.Find("MainCamera");
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            var mc = mainCam != null ? mainCam.GetComponent<Camera>() : null;
            if (mc != null)
            {
                cam.CopyFrom(mc);
                var src = mc.GetUniversalAdditionalCameraData();
                var dst = cam.GetUniversalAdditionalCameraData();
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.antialiasingQuality = src.antialiasingQuality;
            }
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 250f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            string only = Arg("-only");

            void Take(Shot s)
            {
                if (only != null && s.name != only) return;
                camGo.transform.position = s.pos;
                camGo.transform.rotation = Quaternion.LookRotation(s.look - s.pos);
                cam.fieldOfView = s.fov;
                LightPool.Tick(false, s.pos);
                cam.Render();
                Save(cam, rt, outDir + $"/annex_{seed}_{floor}_{s.name}{tag}.png");
            }

            // ---- a cancelli chiusi
            var closed = new List<Shot>();
            foreach (var site in fi.annexSites)
            {
                if (site.data.kind == AnnexKind.Torre) closed.AddRange(TowerOutside(plan, site));
                else if (site.data.kind == AnnexKind.Cripta) closed.AddRange(CryptOutside(plan, site));
                else closed.AddRange(GardenOutside(plan, site));
            }
            foreach (var r in plan.rooms) if (r.wallH >= 12f) closed.AddRange(CathedralShots(fi, r));
            if (closed.Count > 0) { camGo.transform.position = closed[0].pos; camGo.transform.rotation = Quaternion.LookRotation(closed[0].look - closed[0].pos); cam.Render(); }
            foreach (var s in closed) Take(s);

            // ---- aperti: gli interni
            foreach (var site in fi.annexSites) site.OpenInstant();
            Physics.SyncTransforms();
            foreach (var site in fi.annexSites)
            {
                var list = site.data.kind == AnnexKind.Torre ? TowerInside(site) : site.data.kind == AnnexKind.Cripta ? CryptInside(site) : GardenInside(plan, site);
                foreach (var s in list) Take(s);
            }
            // veduta dall'alto di tutto il piano, senza soffitti
            if (only == null || only == "alto")
            {
                foreach (var c in fi.ceilings) c.enabled = false;
                plan.WalkBounds(out int x0, out int z0, out int x1, out int z1);
                Vector3 mid = new Vector3((x0 + x1) * 0.05f, 0f, (z0 + z1) * 0.05f);
                float ext = Mathf.Max((x1 - x0) * 0.1f / 1.78f, (z1 - z0) * 0.1f) * 0.58f;
                cam.orthographic = true; cam.orthographicSize = ext;
                camGo.transform.position = mid + Vector3.up * 60f;
                camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                LightPool.Tick(false, mid + Vector3.up * 1.5f);
                Save(cam, rt, outDir + $"/annex_{seed}_{floor}_alto{tag}.png");
                cam.orthographic = false;
                foreach (var c in fi.ceilings) c.enabled = true;
            }
            File.WriteAllText(outDir + $"/annex_{seed}_{floor}{tag}.txt", report.ToString());
            Debug.Log("[PryingEyes] foto annessi: " + report);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }

        // ------------------------------------------------------------------ torre
        static List<Shot> TowerOutside(Plan plan, AnnexSite s)
        {
            var a = s.data; var list = new List<Shot>();
            var axis = new Vector3(a.AxisX, 0f, a.AxisZ); float yaw = a.yaw, E = a.FloorY;
            var host = plan.rooms[a.host];
            list.Add(new Shot { name = "torre_porta", pos = Pol(axis, 6.4f, yaw, Eye), look = Pol(axis, 2.7f, yaw, 1.5f), fov = 72f });
            // da lontano, nella direzione col piu' spazio libero: la cella sospesa sopra la navata
            float bestD = 0f, bestA = 0f;
            for (int k = 0; k < 8; k++)
            {
                float ang = k * 45f; float d = 0f;
                while (d < 16f && host.Contains(axis.x + Dir(ang).x * (d + 1f), axis.z + Dir(ang).z * (d + 1f))) d += 1f;
                if (d > bestD) { bestD = d; bestA = ang; }
            }
            float dist = Mathf.Min(12f, bestD - 1f);
            list.Add(new Shot { name = "torre_vista", pos = Pol(axis, dist, bestA, Eye), look = new Vector3(a.CenterX, E + 1.0f, a.CenterZ), fov = 78f });
            // dal basso, sotto la cella
            list.Add(new Shot { name = "torre_sotto", pos = Pol(axis, 5.2f, yaw + 180f, Eye), look = new Vector3(a.CenterX, E + 0.2f, a.CenterZ), fov = 85f });
            return list;
        }

        static List<Shot> TowerInside(AnnexSite s)
        {
            var a = s.data; var list = new List<Shot>();
            var axis = new Vector3(a.AxisX, 0f, a.AxisZ); float yaw = a.yaw, E = a.FloorY;
            int steps = Mathf.RoundToInt(E / 0.2f);
            int k = 5;
            float ak = yaw + 22.5f + 22.5f * (k - 1) + 11.25f;
            list.Add(new Shot { name = "torre_scala", pos = Pol(axis, 1.5f, ak, 0.2f * k + Eye), look = Pol(axis, 1.5f, ak + 55f, 0.2f * (k + 3) + 1.2f), fov = 80f });
            int k2 = steps / 2;
            float ak2 = yaw + 22.5f + 22.5f * (k2 - 1) + 11.25f;
            list.Add(new Shot { name = "torre_scala_alta", pos = Pol(axis, 1.5f, ak2, 0.2f * k2 + Eye), look = Pol(axis, 1.5f, ak2 + 55f, 0.2f * (k2 + 3) + 1.2f), fov = 80f });
            float exitA = yaw + 22.5f + 22.5f * (steps - 1) + 11.25f;
            list.Add(new Shot { name = "torre_cella", pos = Pol(axis, 1.9f, exitA, E + Eye), look = s.rewardPos + Vector3.up * 0.8f, fov = 76f });
            Vector3 toAxis = (new Vector3(axis.x, 0f, axis.z) - s.rewardPos); toAxis.y = 0f; toAxis.Normalize();
            list.Add(new Shot { name = "torre_tesoro", pos = s.rewardPos + toAxis * 2.4f + Vector3.up * 1.6f, look = s.rewardPos + Vector3.up * 0.5f, fov = 70f });
            return list;
        }

        // ------------------------------------------------------------------ cripta
        static List<Shot> CryptOutside(Plan plan, AnnexSite s)
        {
            var a = s.data; var list = new List<Shot>();
            var axis = new Vector3(a.AxisX, 0f, a.AxisZ);
            Vector3 o = s.outside; Vector3 away = (o - axis); away.y = 0f; away.Normalize();
            list.Add(new Shot { name = "cripta_botola", pos = o + away * 1.4f + Vector3.up * Eye, look = axis + Vector3.up * 0.0f, fov = 74f });
            Vector3 side = Vector3.Cross(Vector3.up, away);
            list.Add(new Shot { name = "cripta_sala", pos = axis + (away * 0.5f + side * 0.9f).normalized * 6.5f + Vector3.up * Eye, look = axis + Vector3.up * 0.2f, fov = 74f });
            return list;
        }

        static List<Shot> CryptInside(AnnexSite s)
        {
            var a = s.data; var list = new List<Shot>();
            var axis = new Vector3(a.AxisX, 0f, a.AxisZ); float yaw = a.yaw, Y0 = a.FloorY;
            Vector3 fwd = Dir(yaw), right = Vector3.Cross(Vector3.up, fwd);
            Vector3 L(float uu, float vv, float yy) => axis + fwd * uu + right * vv + Vector3.up * yy;
            int steps = Mathf.RoundToInt(-Y0 / 0.2f) - 1;
            int k = 6;
            float a0 = yaw - 22.5f - 22.5f * (steps - k + 1) + 11.25f;
            list.Add(new Shot { name = "cripta_scala", pos = Pol(axis, 1.5f, a0, -0.2f * k + Eye), look = Pol(axis, 1.5f, a0 + 55f, -0.2f * (k + 3) + 0.9f), fov = 80f });
            list.Add(new Shot { name = "cripta_camera", pos = L(3.2f, 0f, Y0 + Eye), look = L(9.5f, 0f, Y0 + 1.3f), fov = 78f });
            list.Add(new Shot { name = "cripta_altare", pos = L(5.0f, 0f, Y0 + Eye), look = s.rewardPos + Vector3.up * 0.7f, fov = 72f });
            list.Add(new Shot { name = "cripta_volta", pos = L(6.0f, 0f, Y0 + Eye), look = L(2.5f, 0f, Y0 + 2.9f), fov = 80f });
            return list;
        }

        // ------------------------------------------------------------------ giardino
        static void GardenFrame(Annex a, out System.Func<float, float, float, Vector3> G, out float tI0, out float tI1, out float sF, out float tF)
        {
            Vector3 outV = a.side == Side.N ? Vector3.forward : a.side == Side.S ? Vector3.back : a.side == Side.E ? Vector3.right : Vector3.left;
            bool alongX = Plan.AlongX(a.side);
            Vector3 al = alongX ? Vector3.right : Vector3.forward;
            Vector3 origin = alongX ? new Vector3(a.center * 0.1f, 0f, a.line * 0.1f) : new Vector3(a.line * 0.1f, 0f, a.center * 0.1f);
            G = (t, ss, y) => origin + outV * t + al * ss + Vector3.up * y;
            int sign = (a.side == Side.N || a.side == Side.E) ? 1 : -1;
            int n0 = alongX ? a.z0 : a.x0, n1 = alongX ? a.z1 : a.x1;
            float tA = sign > 0 ? (n0 - a.line) * 0.1f : (a.line - n1) * 0.1f;
            float tB = sign > 0 ? (n1 - a.line) * 0.1f : (a.line - n0) * 0.1f;
            float sLo = ((alongX ? a.x0 : a.z0) - a.center) * 0.1f, sHi = ((alongX ? a.x1 : a.z1) - a.center) * 0.1f;
            tI0 = tA + 0.3f; tI1 = tB - 0.3f;
            sF = (sLo + 0.3f + sHi - 0.3f) * 0.5f;
            tF = Mathf.Lerp(tI0, tI1, 0.46f);
        }

        static List<Shot> GardenOutside(Plan plan, AnnexSite s)
        {
            GardenFrame(s.data, out var G, out float tI0, out float tI1, out float sF, out float tF);
            var list = new List<Shot>();
            list.Add(new Shot { name = "giardino_porta", pos = G(-3.4f, 0f, Eye), look = G(0f, 0f, 1.4f), fov = 72f });
            list.Add(new Shot { name = "giardino_alto", pos = G(tF - 9f, sF, 20f), look = G(tF, sF, 0f), fov = 70f });
            return list;
        }

        static List<Shot> GardenInside(Plan plan, AnnexSite s)
        {
            GardenFrame(s.data, out var G, out float tI0, out float tI1, out float sF, out float tF);
            var list = new List<Shot>();
            list.Add(new Shot { name = "giardino_passaggio", pos = G(0.5f, 0f, Eye), look = G(tI0 + 6f, 0f, 1.3f), fov = 74f });
            list.Add(new Shot { name = "giardino_fonte", pos = G(tI0 + 1.0f, 0f, Eye), look = G(tF, sF, 1.1f), fov = 78f });
            list.Add(new Shot { name = "giardino_cielo", pos = G(tI1 - 1.4f, sF - 3f, Eye), look = G(tI0, 0f, 3.4f), fov = 82f });
            list.Add(new Shot { name = "giardino_tesoro", pos = s.rewardPos + (G(tF, sF, 0f) - s.rewardPos).normalized * 3.0f + Vector3.up * 1.5f, look = s.rewardPos + Vector3.up * 0.5f, fov = 70f });
            return list;
        }

        // ------------------------------------------------------------------ cattedrali
        static List<Shot> CathedralShots(FloorInstance fi, Room r)
        {
            var list = new List<Shot>();
            bool alongX = r.W >= r.D;
            Vector3 c = new Vector3(r.CenterX, 0f, r.CenterZ);
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            float half = (alongX ? r.W : r.D) * 0.05f;
            list.Add(new Shot { name = "cattedrale_" + r.id + "_nave", pos = c - along * (half - 1.4f) + Vector3.up * Eye, look = c + along * (half * 0.4f) + Vector3.up * (r.wallH * 0.38f), fov = 84f });
            Vector3 hi = Vector3.zero; bool have = false;
            foreach (var g in fi.galleries) if (g.room == r) foreach (var p in g.points) if (!have || p.y > hi.y) { hi = p; have = true; }
            if (have) list.Add(new Shot { name = "cattedrale_" + r.id + "_balconata", pos = hi + Vector3.up * Eye, look = new Vector3(c.x, 1.0f, c.z), fov = 80f });
            return list;
        }

        static void Save(Camera cam, RenderTexture rt, string file)
        {
            cam.Render();
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[PryingEyes] foto " + file);
        }
    }
}

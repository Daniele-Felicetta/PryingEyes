using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes.EditorTools
{
    /// <summary>Genera un piano in edit-mode dalla scena Torre e lo fotografa: una veduta dall'alto (senza soffitti) e
    /// un'inquadratura da dentro per ogni tipo di stanza, a occhio del giocatore. Argomenti: -seed N -floor N -tag x
    /// -rooms Navata,Camera (elenco) -only nome (una sola foto). Uscita: hotel_build/proc_seme_piano_nome.png.</summary>
    public static class ProcShots
    {
        const string Out = "C:/Users/danie/Desktop/prying-eyes/hotel_build/";

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static int ArgInt(string name, int def) => int.TryParse(Arg(name), out int v) ? v : def;

        struct Shot { public string name; public Vector3 pos; public Vector3 look; public float fov; }

        public static void Run()
        {
            ShaderUtil.allowAsyncCompilation = false;
            int seed = ArgInt("-seed", 1), floor = ArgInt("-floor", 1);
            string tag = Arg("-tag") != null ? "_" + Arg("-tag") : "";
            var scene = EditorSceneManager.OpenScene(TorreBuilder.ScenePath);
            var player = GameObject.Find("Player");
            if (player != null) player.SetActive(false);
            var kit = Object.FindFirstObjectByType<ProcKit>();
            TorreBuilder.FillKit(kit);
            LightPool.Clear();
            var plan = PlanGen.Generate(seed, floor);
            if (plan == null) { Debug.LogError("piano non generato"); return; }
            var fi = FloorBuilder.Build(plan, null);
            Physics.SyncTransforms();
            Debug.Log($"[PryingEyes] foto piano seme {seed} piano {floor}: {plan.rooms.Count} stanze, {LightPool.Count} luci");

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

            var shots = new List<Shot>();
            // veduta dall'alto
            plan.ComputeBounds();
            Vector3 mid = new Vector3((plan.minX + plan.maxX) * 0.05f, 0f, (plan.minZ + plan.maxZ) * 0.05f);
            float ext = Mathf.Max((plan.maxX - plan.minX) * 0.1f / 1.78f, (plan.maxZ - plan.minZ) * 0.1f) * 0.58f;

            string rooms = Arg("-rooms");
            var want = rooms != null ? new HashSet<string>(rooms.Split(',')) : null;
            var seen = new HashSet<RoomKind>();
            foreach (var r in plan.rooms)
            {
                if (r.IsLift) continue;
                if (want != null ? !want.Contains(r.kind.ToString()) : seen.Contains(r.kind)) continue;
                seen.Add(r.kind);
                var s = EntryShot(plan, r);
                shots.Add(s);
            }
            // ascensore di arrivo dall'interno della cabina e dalla stanza
            if (fi.arrival != null && want == null)
            {
                shots.Add(new Shot { name = "ascensore", pos = fi.arrival.frontPoint + Vector3.up * 1.62f - fi.arrival.lookOut * 0.2f, look = fi.arrival.spawnPoint + Vector3.up * 1.3f, fov = 72f });
            }
            string only = Arg("-only");
            if (only != null) shots = shots.FindAll(s => s.name == only);

            // la prima inquadratura scalda gli shader
            cam.orthographic = false;
            if (shots.Count > 0) { camGo.transform.position = shots[0].pos; camGo.transform.rotation = Quaternion.LookRotation(shots[0].look - shots[0].pos); cam.Render(); }

            // -nav: disegna la navmesh (verde) sopra il pavimento nella veduta dall'alto
            GameObject navGo = null;
            if (Arg("-nav") != null || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nav") >= 0)
            {
                HotelNav.Rebuild(fi.bounds, fi.propRoots);
                var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                var vv = new Vector3[tri.vertices.Length];
                for (int i = 0; i < vv.Length; i++) vv[i] = tri.vertices[i] + Vector3.up * 0.08f;
                mesh.vertices = vv;
                mesh.triangles = tri.indices;
                mesh.RecalculateNormals();
                navGo = new GameObject("Nav");
                navGo.AddComponent<MeshFilter>().sharedMesh = mesh;
                var nm = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.1f, 1f, 0.2f, 1f) };
                navGo.AddComponent<MeshRenderer>().sharedMaterial = nm;
            }
            if (only == null || only == "alto")
            {
                foreach (var c in fi.ceilings) c.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = ext;
                camGo.transform.position = mid + Vector3.up * 40f;
                camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                LightPool.Tick(false, mid + Vector3.up * 1.5f);
                Save(cam, rt, $"proc_{seed}_{floor}_alto{tag}.png");
                foreach (var c in fi.ceilings) c.enabled = true;
                cam.orthographic = false;
            }
            foreach (var s in shots)
            {
                camGo.transform.position = s.pos;
                camGo.transform.rotation = Quaternion.LookRotation(s.look - s.pos);
                cam.fieldOfView = s.fov;
                LightPool.Tick(false, s.pos);
                cam.Render();
                Save(cam, rt, $"proc_{seed}_{floor}_{s.name}{tag}.png");
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
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
            File.WriteAllBytes(Out + file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[PryingEyes] foto " + file);
        }

        /// <summary>Inquadratura d'ingresso: dalla soglia principale, 0,7 m dentro la stanza, verso il suo centro.</summary>
        static Shot EntryShot(Plan plan, Room r)
        {
            Opening best = null; float bw = -1f;
            foreach (var o in plan.openings)
            {
                if ((o.a != r.id && o.b != r.id) || o.b < 0 || o.oneSided) continue;
                var other = plan.rooms[o.a == r.id ? o.b : o.a];
                float w = o.width + (other.kind == RoomKind.Nartece || other.kind == RoomKind.Corridoio || other.kind == RoomKind.Atrio ? 500 : 0) - (other.IsLift ? 900 : 0);
                if (w > bw) { bw = w; best = o; }
            }
            Vector3 ctr = new Vector3(r.CenterX, 1.35f, r.CenterZ);
            if (best == null) return new Shot { name = r.kind.ToString().ToLower(), pos = ctr, look = ctr + Vector3.forward, fov = 75f };
            Vector3 wall = best.alongX ? new Vector3(best.center * 0.1f, 0f, best.line * 0.1f) : new Vector3(best.line * 0.1f, 0f, best.center * 0.1f);
            Vector3 inw = new Vector3(ctr.x - wall.x, 0f, ctr.z - wall.z);
            // direzione verso l'interno: perpendicolare al muro
            Vector3 dir = best.alongX ? new Vector3(0f, 0f, Mathf.Sign(ctr.z - wall.z)) : new Vector3(Mathf.Sign(ctr.x - wall.x), 0f, 0f);
            Vector3 pos = wall + dir * 0.9f + Vector3.up * 1.62f;
            Vector3 look = pos + dir * 8f + Vector3.up * (-0.2f);
            // se la stanza e' piccola, guarda il centro
            float depth = Mathf.Abs(Vector3.Dot(ctr - wall, dir));
            if (depth < 5f) look = new Vector3(ctr.x, 1.2f, ctr.z);
            return new Shot { name = r.kind.ToString().ToLower(), pos = pos, look = look, fov = r.kind == RoomKind.Navata ? 80f : 76f };
        }
    }
}

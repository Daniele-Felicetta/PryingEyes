using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes.EditorTools
{
    /// <summary>Foto di taratura del buio e della torcia in edit-mode (senza lanciare il gioco): costruisce un piano con le stesse luci
    /// abbassate, lo stesso ambiente, nebbia e grading del gioco, e per ogni stanza scelta fotografa piu' varianti di fascio.
    /// Uscita: hotel_build/furtivo/torcia_&lt;variante&gt;_&lt;stanza&gt;.png, piu' gli sprite degli occhi dei segnalini. Argomenti: -seed N -floor N -rooms A,B -variants a,b.</summary>
    public static class StealthShots
    {
        const string Out = "C:/Users/danie/Desktop/prying-eyes/hotel_build/furtivo/";

        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        struct Beam { public float angle, inner, range, intensity; public bool cookie; public Color color; }
        struct Variant { public string name; public Beam[] beams; }

        static readonly Color Warm = new Color(1f, 0.93f, 0.80f);

        static Variant[] Variants() => new[]
        {
            new Variant { name = "larga_cookie", beams = new[] { new Beam { angle = 70f, inner = 50f, range = 34f, intensity = 36f, cookie = true, color = Warm } } },
            new Variant { name = "larga_nocookie", beams = new[] { new Beam { angle = 70f, inner = 52f, range = 34f, intensity = 36f, cookie = false, color = Warm } } },
            new Variant { name = "forte_cookie", beams = new[] { new Beam { angle = 70f, inner = 50f, range = 36f, intensity = 50f, cookie = true, color = Warm } } },
            new Variant
            {
                name = "doppia_cookie",
                beams = new[]
                {
                    new Beam { angle = 26f, inner = 10f, range = 40f, intensity = 70f, cookie = false, color = Warm },
                    new Beam { angle = 84f, inner = 56f, range = 16f, intensity = 16f, cookie = true, color = Warm },
                }
            },
            new Variant
            {
                name = "doppia",
                beams = new[]
                {
                    new Beam { angle = 26f, inner = 10f, range = 40f, intensity = 80f, cookie = false, color = Warm },
                    new Beam { angle = 84f, inner = 56f, range = 14f, intensity = 14f, cookie = false, color = Warm },
                }
            },
            new Variant { name = "lunga_cookie", beams = new[] { new Beam { angle = 56f, inner = 40f, range = 40f, intensity = 52f, cookie = true, color = Warm } } },
        };

        struct Shot { public string name; public Vector3 pos; public Vector3 look; public float fov; }

        public static void Run()
        {
            ShaderUtil.allowAsyncCompilation = false;
            Directory.CreateDirectory(Out);
            DumpSprites();
            int seed = int.TryParse(Arg("-seed"), out int sd) ? sd : 1000;
            int floor = int.TryParse(Arg("-floor"), out int fl) ? fl : 1;
            var scene = EditorSceneManager.OpenScene(TorreBuilder.ScenePath);
            var player = GameObject.Find("Player");
            if (player != null) player.SetActive(false);
            var kit = Object.FindFirstObjectByType<ProcKit>();
            TorreBuilder.FillKit(kit);
            LightPool.Clear();
            LightPool.Dim = FloorDirector.LightDim(floor);
            var plan = PlanGen.Generate(seed, floor);
            if (plan == null) { Debug.LogError("piano non generato"); return; }
            var fi = FloorBuilder.Build(plan, null);
            Physics.SyncTransforms();
            FloorDirector.ApplyLook(plan, floor);
            Debug.Log($"[PryingEyes] foto torcia seme {seed} piano {floor}: {plan.rooms.Count} stanze, {LightPool.Count} luci, Dim {LightPool.Dim:F2}");

            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            var mainCam = GameObject.Find("MainCamera");
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

            string roomsArg = Arg("-rooms");
            var want = new HashSet<string>((roomsArg ?? "Corridoio,Navata,Camera").Split(','));
            var seen = new HashSet<RoomKind>();
            var shots = new List<Shot>();
            foreach (var r in plan.rooms)
            {
                if (r.IsLift || !want.Contains(r.kind.ToString()) || seen.Contains(r.kind)) continue;
                seen.Add(r.kind);
                shots.Add(EntryShot(plan, r));
            }
            string varArg = Arg("-variants");
            var wantV = varArg != null ? new HashSet<string>(varArg.Split(',')) : null;

            // la prima inquadratura scalda gli shader
            if (shots.Count > 0) { camGo.transform.position = shots[0].pos; camGo.transform.rotation = Quaternion.LookRotation(shots[0].look - shots[0].pos); cam.Render(); }

            foreach (var s in shots)
            {
                camGo.transform.position = s.pos;
                camGo.transform.rotation = Quaternion.LookRotation(s.look - s.pos);
                cam.fieldOfView = s.fov;
                // senza torcia
                LightPool.Tick(false, s.pos);
                Save(cam, rt, $"torcia_nessuna_{s.name}.png");
                foreach (var v in Variants())
                {
                    if (wantV != null && !wantV.Contains(v.name)) continue;
                    var lights = new List<GameObject>();
                    foreach (var b in v.beams)
                    {
                        var g = new GameObject("Torcia_" + v.name);
                        g.transform.position = camGo.transform.position + camGo.transform.right * 0.08f - camGo.transform.up * 0.05f + camGo.transform.forward * 0.25f;
                        g.transform.rotation = camGo.transform.rotation;
                        var l = g.AddComponent<Light>();
                        l.type = LightType.Spot;
                        l.spotAngle = b.angle;
                        l.innerSpotAngle = b.inner;
                        l.range = b.range;
                        l.intensity = b.intensity;
                        l.color = b.color;
                        l.shadows = LightShadows.Soft;
                        if (b.cookie) l.cookie = Torch.Cookie();
                        lights.Add(g);
                    }
                    // alone debole intorno al fascio
                    var sg = new GameObject("Alone");
                    sg.transform.position = camGo.transform.position + camGo.transform.forward * 0.25f;
                    sg.transform.rotation = camGo.transform.rotation;
                    var sl = sg.AddComponent<Light>();
                    sl.type = LightType.Spot; sl.spotAngle = 125f; sl.innerSpotAngle = 60f; sl.range = 7f; sl.intensity = 1.5f;
                    sl.color = Warm; sl.shadows = LightShadows.None;
                    lights.Add(sg);
                    Save(cam, rt, $"torcia_{v.name}_{s.name}.png");
                    foreach (var g in lights) Object.DestroyImmediate(g);
                }
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

        /// <summary>Scrive gli sprite degli occhi dei segnalini ingranditi su sfondo scuro: un foglio con le sei aperture e le tre pupille.</summary>
        static void DumpSprites()
        {
            var m = typeof(HUD).GetMethod("EyeSprite", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) { Debug.LogWarning("EyeSprite non trovato"); return; }
            const int S = 4;
            int W = 64 * S, H = 40 * S;
            var sheet = new Texture2D(W * 3 + 40, H * 6 + 70, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(40, 38, 44, 255);
            sheet.SetPixels32(fill);
            for (int open = 0; open < 6; open++)
                for (int pupil = 0; pupil < 3; pupil++)
                {
                    var sp = (Sprite)m.Invoke(null, new object[] { open, pupil });
                    var tex = sp.texture;
                    Color tint = open == 5 ? new Color(1f, 0.16f, 0.12f) : open == 3 ? new Color(1f, 0.72f, 0.25f) : new Color(0.92f, 0.9f, 0.84f);
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++)
                        {
                            Color c = tex.GetPixelBilinear((x + 0.5f) / W, (y + 0.5f) / H);
                            int px = 10 + pupil * (W + 10) + x, py = 10 + (5 - open) * (H + 10) + y;
                            Color bg = sheet.GetPixel(px, py);
                            Color o = Color.Lerp(bg, new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b), c.a);
                            sheet.SetPixel(px, py, o);
                        }
                }
            sheet.Apply();
            File.WriteAllBytes(Out + "occhi_sprite.png", sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Debug.Log("[PryingEyes] sprite degli occhi scritti");
        }

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
            Vector3 dir = best.alongX ? new Vector3(0f, 0f, Mathf.Sign(ctr.z - wall.z)) : new Vector3(Mathf.Sign(ctr.x - wall.x), 0f, 0f);
            Vector3 pos = wall + dir * 0.9f + Vector3.up * 1.62f;
            Vector3 look = pos + dir * 8f + Vector3.up * (-0.2f);
            float depth = Mathf.Abs(Vector3.Dot(ctr - wall, dir));
            if (depth < 5f) look = new Vector3(ctr.x, 1.2f, ctr.z);
            return new Shot { name = r.kind.ToString().ToLower(), pos = pos, look = look, fov = r.kind == RoomKind.Navata ? 80f : 76f };
        }
    }
}

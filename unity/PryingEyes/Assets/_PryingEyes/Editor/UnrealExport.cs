using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes.EditorTools
{
    /// <summary>Esporta una stanza di un piano generato (seme e piano) per il prototipo Unreal e la fotografa con la camera
    /// di Unity dallo stesso punto: geometria (mesh grezze), materiali (proprieta' URP), luci, nebbia, grading, un monaco in
    /// posa e un'arma in mano. Nessuna regola di gioco: legge quello che FloorBuilder e RoomKits hanno costruito.
    /// Da CLI: Unity.exe -batchmode -projectPath ... -executeMethod PryingEyes.EditorTools.UnrealExport.Run -seed 1000 -floor 1
    /// -room Ristorante,SalaBallo,Camera -weapon ar_trinita -monk cammina:0.3 -out cartella -quit (senza -nographics).
    /// Uscita: scene.json, meshes/*.bin, textures/*.png, unity.png. Vedi docs/engineering/unreal-prototype.md.</summary>
    public static class UnrealExport
    {
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static int ArgInt(string name, int def) => int.TryParse(Arg(name), out int v) ? v : def;
        static float ArgFloat(string name, float def) => float.TryParse(Arg(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;

        [Serializable] class TexJ { public string prop; public string path; public float sx = 1, sy = 1, ox, oy; }
        [Serializable] class FloatJ { public string name; public float v; }
        [Serializable] class ColorJ { public string name; public float r, g, b, a; }
        [Serializable] class MatJ
        {
            public string name, shader, asset;
            public List<TexJ> tex = new List<TexJ>();
            public List<FloatJ> floats = new List<FloatJ>();
            public List<ColorJ> colors = new List<ColorJ>();
            public List<string> keywords = new List<string>();
        }
        [Serializable] class MeshJ { public string file, name; public int verts, subs; }
        [Serializable] class NodeJ { public string name; public int mesh; public int[] mats; public float[] m; public bool shadow = true; public string group; }
        [Serializable] class LightJ
        {
            public string name, type;
            public float[] pos, fwd;
            public float r, g, b, intensity, range, spot, inner;
            public bool shadows;
        }
        [Serializable] class LookJ
        {
            public bool fog; public float fogDensity; public float[] fogColor;
            public float[] ambSky, ambEquator, ambGround; public string ambMode; public float ambIntensity;
            public float postExposure, contrast, saturation, bloom, vignette, grain; public string tonemap;
            public string env; public float envIntensity;      // the reflection cube as a lat-long image (env.exr), and RenderSettings.reflectionIntensity
            public float[] splitShadows, splitHighlights; public float splitBalance, vignetteSmooth;     // Split Toning (sRGB colours) and the vignette's smoothness
        }
        [Serializable] class MonkJ { public float[] pos; public float yaw; public string anim; public float time; }
        [Serializable] class SceneJ
        {
            public string tag, room; public int seed, floor;
            public float[] camPos, camFwd, camUp; public float fov;
            public LookJ look = new LookJ();
            public MonkJ monk;
            public List<MeshJ> meshes = new List<MeshJ>();
            public List<MatJ> mats = new List<MatJ>();
            public List<NodeJ> nodes = new List<NodeJ>();
            public List<LightJ> lights = new List<LightJ>();
        }

        static string outDir;
        static readonly SceneJ S = new SceneJ();
        static readonly Dictionary<Mesh, int> meshIds = new Dictionary<Mesh, int>();
        static readonly Dictionary<Material, int> matIds = new Dictionary<Material, int>();
        static readonly Dictionary<Texture, string> texFiles = new Dictionary<Texture, string>();
        static int skipped, baked;

        static float[] V3(Vector3 v) => new[] { v.x, v.y, v.z };

        public static void Run()
        {
            ShaderUtil.allowAsyncCompilation = false;
            int seed = ArgInt("-seed", 1000), floor = ArgInt("-floor", 1);
            string kinds = Arg("-room") ?? "Ristorante,SalaBallo,Camera,Atrio";
            string weaponId = Arg("-weapon") ?? "ar_trinita";
            string monkSpec = Arg("-monk") ?? "cammina:0.3";
            float radius = ArgFloat("-radius", 7f);
            int w = ArgInt("-w", 1920), h = ArgInt("-h", 1080);
            // Unity's working directory is the project folder: the default goes to <repository>/unity_logs (git-ignored)
            outDir = (Arg("-out") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../unity_logs/unreal_export", seed + "_" + floor))).Replace('\\', '/');
            Directory.CreateDirectory(outDir + "/meshes");
            Directory.CreateDirectory(outDir + "/textures");

            EditorSceneManager.OpenScene(TorreBuilder.ScenePath);
            var mainCam = GameObject.Find("MainCamera");           // before the player goes inactive: the camera hangs from it and Find skips inactive objects
            var player = GameObject.Find("Player");
            if (player != null) player.SetActive(false);
            var kit = UnityEngine.Object.FindFirstObjectByType<ProcKit>();
            TorreBuilder.FillKit(kit);
            LightPool.Clear();
            var plan = PlanGen.Generate(seed, floor);
            if (plan == null) { Debug.LogError("[UnrealExport] piano non generato"); EditorApplication.Exit(2); return; }
            var fi = FloorBuilder.Build(plan, null);
            Physics.SyncTransforms();

            // l'atmosfera come la imposta il gioco all'inizio del piano (l'editor non esegue FloorDirector): nebbia, ambiente, riflessi,
            // grading del tema, esposizione di base + procedurale + tema + luminosita' scelta dal giocatore (0,5 = neutra)
            GameManager.proc = true;
            var gradingVolume = UnityEngine.Object.FindFirstObjectByType<Volume>();
            GameSettings.BindBrightness(gradingVolume);
            FloorDirector.ApplyLook(plan, floor);

            // la stanza: la prima del primo tipo che esiste
            Room room = null;
            foreach (var k in kinds.Split(','))
            {
                room = plan.rooms.Find(r => !r.IsLift && r.kind.ToString() == k.Trim());
                if (room != null) break;
            }
            if (room == null) { Debug.LogError("[UnrealExport] nessuna stanza tra " + kinds); EditorApplication.Exit(3); return; }
            var shot = PickShot(room);
            S.tag = seed + "_" + floor + "_" + room.kind;
            S.seed = seed; S.floor = floor; S.room = room.kind.ToString();
            Debug.Log($"[UnrealExport] piano {seed}/{floor}: {plan.rooms.Count} stanze; stanza {room.kind} id {room.id} ({room.x0},{room.z0})-({room.x1},{room.z1}) dm");

            // la camera come in ProcShots (copia della camera principale: post-processing, antialiasing)
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
                dst.volumeLayerMask = src.volumeLayerMask;
                Debug.Log($"[UnrealExport] camera principale copiata: post-processing {src.renderPostProcessing}, antialiasing {src.antialiasing}, hdr {mc.allowHDR}, msaa {mc.allowMSAA}");
            }
            else Debug.LogWarning("[UnrealExport] camera principale non trovata: la foto di Unity sarebbe senza post-processing");
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 250f;

            // l'arma in mano, con le braccia, come in gioco (ArmiShots): la camera va all'origine, poi la si sposta
            GameObject weaponRoot = null;
            var wdef = WeaponDefs.All[WeaponDefs.IndexOf(weaponId)];
            var wprefab = Resources.Load<GameObject>("Armi/" + weaponId);
            if (wprefab != null)
            {
                weaponRoot = new GameObject("Viewmodel");
                ArmiShots.BuildView(weaponRoot, wprefab, wdef, cam, false);
            }
            else Debug.LogWarning("[UnrealExport] manca il prefab dell'arma " + weaponId);

            Vector3 camPos = shot.pos;
            Vector3 camFwd = (shot.look - shot.pos).normalized;
            camGo.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(camFwd));
            cam.fieldOfView = shot.fov;
            S.camPos = V3(camPos); S.camFwd = V3(camFwd); S.camUp = V3(camGo.transform.up); S.fov = shot.fov;

            // il monaco in posa a 4 m davanti, rivolto alla camera
            var fbxPath = "Assets/_PryingEyes/Models/Monaco/monaco.fbx";
            var monkAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            GameObject monk = null;
            if (monkAsset != null && monkSpec != "none")
            {
                string[] ms = monkSpec.Split(':');
                float t = ms.Length > 1 ? float.Parse(ms[1], System.Globalization.CultureInfo.InvariantCulture) : 0.3f;
                Vector3 flat = new Vector3(camFwd.x, 0f, camFwd.z).normalized;
                Vector3 mp = camPos + flat * ArgFloat("-monkdist", 4.2f);
                mp.y = GroundY(mp, room);
                // il primo posto libero lungo la linea di vista
                foreach (float d in new[] { 4.2f, 3.6f, 5.0f, 3.0f, 6.0f, 2.4f })
                {
                    var q = camPos + flat * d; q.y = GroundY(q, room);
                    if (Free(q)) { mp = q; break; }
                }
                monk = (GameObject)PrefabUtility.InstantiatePrefab(monkAsset);
                monk.transform.SetPositionAndRotation(mp, Quaternion.LookRotation(-flat));
                foreach (var smr in monk.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMeshLod = 0;
                AnimationClip clip = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath)) if (o is AnimationClip c && c.name == ms[0]) clip = c;
                if (clip != null) clip.SampleAnimation(monk, t % Mathf.Max(0.01f, clip.length));
                else Debug.LogWarning("[UnrealExport] clip " + ms[0] + " non trovata");
                S.monk = new MonkJ { pos = V3(mp), yaw = monk.transform.eulerAngles.y, anim = ms[0], time = t };
            }

            if (ArgInt("-ramp", 0) != 0)
            {
                if (weaponRoot != null) UnityEngine.Object.DestroyImmediate(weaponRoot);
                if (monk != null) UnityEngine.Object.DestroyImmediate(monk);
                RampTest(cam, w, h);
                return;
            }
            if (ArgInt("-lab", 0) != 0)
            {
                if (weaponRoot != null) UnityEngine.Object.DestroyImmediate(weaponRoot);
                if (monk != null) UnityEngine.Object.DestroyImmediate(monk);
                LabTest(cam, w, h);
                return;
            }

            // le luci come le vede la camera
            LightPool.Tick(false, camPos);

            // aiuti per tarare il confronto: -lightfilter nome (un ! finale = nome esatto) lascia accese solo le luci col nome,
            // -ambient 0 spegne l'ambiente; valgono per la foto e per le luci esportate
            string only = Arg("-lightfilter");
            if (only != null)
            {
                bool exact = only.EndsWith("!");
                if (exact) only = only.Substring(0, only.Length - 1);
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (!(exact ? l.name.Equals(only, StringComparison.OrdinalIgnoreCase) : l.name.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0)) l.enabled = false;
            }
            if (ArgInt("-ambient", 1) == 0) { RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; }
            if (ArgInt("-fog", 1) == 0) RenderSettings.fog = false;
            if (ArgInt("-reflect", 1) == 0) RenderSettings.reflectionIntensity = 0f;
            var cube = RenderSettings.customReflectionTexture;
            Debug.Log($"[UnrealExport] custom reflection texture: {(cube != null ? cube.name + " " + cube.width + "x" + cube.height + " " + cube.GetType().Name + " " + AssetDatabase.GetAssetPath(cube) : "none")}; skybox {(RenderSettings.skybox != null ? RenderSettings.skybox.name + " " + RenderSettings.skybox.shader.name : "none")}; ambientProbe L0 {RenderSettings.ambientProbe[0, 0]:F4} {RenderSettings.ambientProbe[1, 0]:F4} {RenderSettings.ambientProbe[2, 0]:F4}");
            Debug.Log($"[UnrealExport] fog {RenderSettings.fog} {RenderSettings.fogMode} density {RenderSettings.fogDensity:F4} start {RenderSettings.fogStartDistance} end {RenderSettings.fogEndDistance}; ambient {RenderSettings.ambientMode}; reflections {RenderSettings.defaultReflectionMode} intensity {RenderSettings.reflectionIntensity}");

            // la foto di Unity
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render(); cam.Render();
            SavePng(rt, outDir + "/unity.png", w, h);
            if (ArgInt("-perf", 1) != 0) MeasureFrames(cam, rt, ArgInt("-perfframes", 300));

            // ---- esportazione
            CollectLook();
            var box = new Bounds(new Vector3(room.CenterX, 1.5f, room.CenterZ), new Vector3((room.x1 - room.x0) * 0.1f + 2f * radius, 14f, (room.z1 - room.z0) * 0.1f + 2f * radius));
            var rends = new List<Renderer>();
            if (fi.root != null) rends.AddRange(fi.root.GetComponentsInChildren<Renderer>(false));
            if (weaponRoot != null) rends.AddRange(weaponRoot.GetComponentsInChildren<Renderer>(false));
            foreach (var r in rends)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                bool weapon = weaponRoot != null && r.transform.IsChildOf(weaponRoot.transform);
                if (!weapon && !box.Intersects(r.bounds)) continue;
                AddRenderer(r, weapon ? "weapon" : "room");
            }
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                AddLight(l, box, camPos);

            File.WriteAllText(outDir + "/scene.json", JsonUtility.ToJson(S, true));
            Debug.Log($"[UnrealExport] {S.nodes.Count} oggetti, {S.meshes.Count} mesh ({baked} cotte dallo scheletro), {S.mats.Count} materiali, {S.lights.Count} luci, {skipped} saltati -> {outDir}");

            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            if (weaponRoot != null) UnityEngine.Object.DestroyImmediate(weaponRoot);
            if (monk != null) UnityEngine.Object.DestroyImmediate(monk);
        }

        /// <summary>-ramp 1: la risposta tonale del gioco. Dodici quadrati senza luce (shader Unlit) con valori noti, lontano sopra l'albergo,
        /// ripresi dalla camera con il suo post-processing; scrive ramp_unity.txt (valore lineare -> pixel 0-255) e ramp_unity.png. Serve a
        /// confrontare la curva con quella di Unreal (-pe_scene=room -pe_ramp), che usa gli stessi valori.</summary>
        static float[] RampValues
        {
            get   // 30 values, a factor 1.4 apart, from 0.0005 to 8.7: 6 columns x 5 rows
            {
                var v = new float[30];
                for (int i = 0; i < v.Length; i++) v[i] = 0.0005f * Mathf.Pow(1.4f, i);
                return v;
            }
        }

        static void RampTest(Camera cam, int w, int h)
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0f;
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) l.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            // the screen effects spread light between patches and darken the corners: off for this measurement
            var volume = UnityEngine.Object.FindFirstObjectByType<Volume>();
            if (volume != null && volume.profile != null)
            {
                if (volume.profile.TryGet(out Bloom bloom)) bloom.intensity.Override(0f);
                if (volume.profile.TryGet(out Vignette vignette)) vignette.intensity.Override(0f);
                if (volume.profile.TryGet(out FilmGrain grain)) grain.intensity.Override(0f);
            }
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            Vector3 origin = new Vector3(0f, 400f, 0f);
            cam.transform.SetPositionAndRotation(origin, Quaternion.identity);
            cam.fieldOfView = 90f;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var quads = new List<Transform>();
            var values = RampValues;
            for (int i = 0; i < values.Length; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = q.GetComponent<Collider>(); if (col != null) UnityEngine.Object.DestroyImmediate(col);
                q.transform.position = origin + new Vector3((i % 6 - 2.5f) * 2f, (2f - i / 6) * 2f, 10f);
                q.transform.localScale = Vector3.one * 1.4f;
                var m = new Material(shader);
                float v = values[i];
                m.SetColor("_BaseColor", new Color(v, v, v, 1f).gamma);       // the project is linear: SetColor converts a non-HDR colour, so give it in gamma
                q.GetComponent<Renderer>().sharedMaterial = m;
                quads.Add(q.transform);
            }
            cam.Render(); cam.Render();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = old;
            var lines = new System.Text.StringBuilder();
            for (int i = 0; i < quads.Count; i++)
            {
                var sp = cam.WorldToScreenPoint(quads[i].position);
                double r = 0, g = 0, b = 0; int n = 0;
                for (int dy = -12; dy <= 12; dy++)
                    for (int dx = -12; dx <= 12; dx++)
                    {
                        var c = tex.GetPixel(Mathf.Clamp((int)sp.x + dx, 0, w - 1), Mathf.Clamp((int)sp.y + dy, 0, h - 1));
                        r += c.r; g += c.g; b += c.b; n++;
                    }
                lines.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:G6} {1:F2} {2:F2} {3:F2}", values[i], 255 * r / n, 255 * g / n, 255 * b / n));
            }
            File.WriteAllText(outDir + "/ramp_unity.txt", lines.ToString());
            File.WriteAllBytes(outDir + "/ramp_unity.png", tex.EncodeToPNG());
            Debug.Log("[UnrealExport] rampa tonale scritta in " + outDir + "/ramp_unity.txt");
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>-lab 1: la scala della luce senza una stanza. Un quadrato Lit bianco di fronte alla camera, una luce puntiforme (con
        /// -labspot 1 un faretto) di intensita' -labi (1) alla camera, a -labd (3) metri, niente ambiente; scrive lab_unity.txt con il pixel
        /// centrale, da leggere con la rampa tonale. E' la prova gemella di -pe_scene=lab di Unreal: la teoria dice albedo x I / d^2.</summary>
        static void LabTest(Camera cam, int w, int h)
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0f;
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) l.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            var volume = UnityEngine.Object.FindFirstObjectByType<Volume>();
            if (volume != null && volume.profile != null)
            {
                if (volume.profile.TryGet(out Bloom bloom)) bloom.intensity.Override(0f);
                if (volume.profile.TryGet(out Vignette vignette)) vignette.intensity.Override(0f);
                if (volume.profile.TryGet(out FilmGrain grain)) grain.intensity.Override(0f);
            }
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            Vector3 origin = new Vector3(0f, 400f, 0f);
            cam.transform.SetPositionAndRotation(origin, Quaternion.identity);
            cam.fieldOfView = 60f;
            float dist = ArgFloat("-labd", 3f), intensity = ArgFloat("-labi", 1f);
            bool spot = ArgInt("-labspot", 0) != 0;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = q.GetComponent<Collider>(); if (col != null) UnityEngine.Object.DestroyImmediate(col);
            q.transform.position = origin + new Vector3(0f, 0f, dist);
            q.transform.localScale = Vector3.one * 20f;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_Metallic", 0f);
            q.GetComponent<Renderer>().sharedMaterial = m;
            var go = new GameObject("lab light");
            go.transform.position = origin;
            var light = go.AddComponent<Light>();
            light.type = spot ? LightType.Spot : LightType.Point;
            light.intensity = intensity; light.range = 100f; light.color = Color.white; light.shadows = LightShadows.None;
            if (spot) { light.spotAngle = 90f; light.innerSpotAngle = 20f; }
            cam.Render(); cam.Render();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = old;
            double r = 0, g = 0, b = 0; int n = 0;
            for (int dy = -10; dy <= 10; dy++)
                for (int dx = -10; dx <= 10; dx++)
                {
                    var c = tex.GetPixel(w / 2 + dx, h / 2 + dy);
                    r += c.r; g += c.g; b += c.b; n++;
                }
            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1:F2} {2:F2} {3:F2}", spot ? "spot" : "point", 255 * r / n, 255 * g / n, 255 * b / n);
            File.AppendAllText(outDir + "/lab_unity.txt", line + "\n");
            File.WriteAllBytes(outDir + "/lab_unity.png", tex.EncodeToPNG());
            Debug.Log("[UnrealExport] lab: " + line + " (intensity " + intensity + ", distance " + dist + " m)");
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>I fotogrammi della stessa inquadratura. Due misure, entrambe cam.Render() su una RenderTexture nell'editor in batchmode
        /// (dove nessuna camera si disegna da sola): una con la lettura di un pixel dopo ogni fotogramma, come PerfProbe, che aspetta la GPU
        /// a ogni giro e quindi non lascia sovrapporre CPU e GPU (pessimista); l'altra mette in coda tutti i fotogrammi e aspetta una volta
        /// sola alla fine, piu' vicina al gioco vero, dove le due lavorano insieme.</summary>
        static void MeasureFrames(Camera cam, RenderTexture rt, int frames)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            void Sync() { RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = null; }
            for (int i = 0; i < 60; i++) { cam.Render(); Sync(); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ms = new List<double>();
            for (int i = 0; i < frames; i++)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                cam.Render();
                Sync();
                ms.Add(sw.Elapsed.TotalMilliseconds - t0);
            }
            double sum = 0; foreach (var v in ms) sum += v;
            ms.Sort();
            double avg = sum / ms.Count;
            double q0 = sw.Elapsed.TotalMilliseconds;
            for (int i = 0; i < frames; i++) cam.Render();
            Sync();
            double queued = (sw.Elapsed.TotalMilliseconds - q0) / frames;
            UnityEngine.Object.DestroyImmediate(px);
            string F(double v) => v.ToString("F2", inv);
            string report = $"unity editor batchmode {rt.width}x{rt.height} frames={ms.Count}\n" +
                            $"frame_ms (wait for the GPU after every frame) avg={F(avg)} p50={F(ms[ms.Count / 2])} p95={F(ms[(int)(ms.Count * 0.95)])} p99={F(ms[(int)(ms.Count * 0.99)])} max={F(ms[ms.Count - 1])} fps_avg={(1000.0 / avg).ToString("F1", inv)}\n" +
                            $"frame_ms (queued, one wait at the end) avg={F(queued)} fps_avg={(1000.0 / queued).ToString("F1", inv)}\n";
            File.WriteAllText(outDir + "/unity_perf.txt", report);
            Debug.Log("[UnrealExport] " + report.Replace("\n", " | "));
        }

        static float GroundY(Vector3 p, Room room)
        {
            if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
            return 0f;
        }

        static Vector3 RoomPoint(Room r, float fx, float fz, float y) =>
            new Vector3(Mathf.Lerp(r.x0, r.x1, fx) * 0.1f, y, Mathf.Lerp(r.z0, r.z1, fz) * 0.1f);

        static bool Free(Vector3 floorPoint)
        {
            for (float h = 0.4f; h < 2.0f; h += 0.5f)
                if (Physics.CheckSphere(floorPoint + Vector3.up * h, 0.45f, ~0, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        /// <summary>Una veduta dall'interno: da un angolo libero della stanza verso il suo centro, a occhio del giocatore
        /// (-cam fx,fz e -look fx,fz, frazioni della pianta, per sceglierla a mano).</summary>
        static ProcShots.Shot PickShot(Room room)
        {
            float gy = GroundY(new Vector3(room.CenterX, 0f, room.CenterZ), room);
            float[] cams = null, looks = null;
            if (Arg("-cam") != null) cams = Array.ConvertAll(Arg("-cam").Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
            if (Arg("-look") != null) looks = Array.ConvertAll(Arg("-look").Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
            var candidates = new List<Vector2>();
            if (cams != null && cams.Length >= 2) candidates.Add(new Vector2(cams[0], cams[1]));
            else foreach (var c in new[] { new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.12f), new Vector2(0.12f, 0.88f), new Vector2(0.88f, 0.88f), new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.9f) }) candidates.Add(c);
            Vector3 pos = RoomPoint(room, candidates[0].x, candidates[0].y, gy + 1.62f);
            foreach (var c in candidates)
            {
                var p = RoomPoint(room, c.x, c.y, gy);
                if (Free(p)) { pos = new Vector3(p.x, gy + 1.62f, p.z); break; }
            }
            Vector2 lk = looks != null && looks.Length >= 2 ? new Vector2(looks[0], looks[1]) : new Vector2(0.55f, 0.55f);
            Vector3 look = RoomPoint(room, lk.x, lk.y, gy + 1.15f);
            return new ProcShots.Shot { name = "export", pos = pos, look = look, fov = ArgFloat("-fov", 74f) };
        }

        static void SavePng(RenderTexture rt, string path, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // ------------------------------------------------------------------------------------------------ look
        /// <summary>La cubemap di riflessione della scena come immagine 2:1 (latitudine-longitudine, float, EXR): la sorgente (HallProbe)
        /// non e' leggibile dal codice, quindi la si disegna con PEEquirect.shader.</summary>
        static bool ExportEnvironment(Cubemap cube)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_PryingEyes/Editor/PEEquirect.shader");
            if (shader == null) { Debug.LogWarning("[UnrealExport] manca PEEquirect.shader"); return false; }
            const int W = 1024, H = 512;
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetTexture("_Cube", cube);
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(null, rt, mat);
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(outDir + "/env.exr", tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP));
            double r = 0, g = 0, b = 0; float mx = 0f;
            var all = tex.GetPixels();
            var preview = new Texture2D(W, H, TextureFormat.RGB24, false);       // a look at it: gamma 2.2 of x / (1 + x), north at the top
            var pv = new Color[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                r += c.r; g += c.g; b += c.b; mx = Mathf.Max(mx, c.maxColorComponent);
                pv[i] = new Color(Mathf.Pow(c.r / (1f + c.r), 1f / 2.2f), Mathf.Pow(c.g / (1f + c.g), 1f / 2.2f), Mathf.Pow(c.b / (1f + c.b), 1f / 2.2f), 1f);
            }
            preview.SetPixels(pv); preview.Apply();
            File.WriteAllBytes(outDir + "/env_preview.png", preview.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(preview);
            double n = W * H;
            Debug.Log($"[UnrealExport] ambiente {cube.name}: media lineare ({r / n:F4}, {g / n:F4}, {b / n:F4}), massimo {mx:F2} -> env.exr");
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(mat);
            return true;
        }

        static void CollectLook()
        {
            var L = S.look;
            L.fog = RenderSettings.fog;
            L.fogDensity = RenderSettings.fogDensity;
            var fc = RenderSettings.fogColor.linear; L.fogColor = new[] { fc.r, fc.g, fc.b };
            L.ambMode = RenderSettings.ambientMode.ToString();
            var s = RenderSettings.ambientSkyColor.linear; L.ambSky = new[] { s.r, s.g, s.b };
            var e = RenderSettings.ambientEquatorColor.linear; L.ambEquator = new[] { e.r, e.g, e.b };
            var g = RenderSettings.ambientGroundColor.linear; L.ambGround = new[] { g.r, g.g, g.b };
            L.ambIntensity = RenderSettings.ambientIntensity;
            L.envIntensity = RenderSettings.reflectionIntensity;
            if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom && RenderSettings.customReflectionTexture is Cubemap cube && L.envIntensity > 0f)
                L.env = ExportEnvironment(cube) ? "env.exr" : null;
            var vol = UnityEngine.Object.FindFirstObjectByType<Volume>();
            var prof = vol != null ? vol.profile : null;       // the instance FloorDirector.ApplyLook wrote its overrides into, not the shared asset
            if (prof == null) return;
            if (prof.TryGet(out ColorAdjustments ca)) { L.postExposure = ca.postExposure.value; L.contrast = ca.contrast.value; L.saturation = ca.saturation.value; }
            if (prof.TryGet(out SplitToning st))
            {
                var sh = st.shadows.value; var hi = st.highlights.value;
                L.splitShadows = new[] { sh.r, sh.g, sh.b }; L.splitHighlights = new[] { hi.r, hi.g, hi.b }; L.splitBalance = st.balance.value;
            }
            if (prof.TryGet(out Bloom bl)) L.bloom = bl.intensity.value;
            if (prof.TryGet(out Vignette vg)) { L.vignette = vg.intensity.value; L.vignetteSmooth = vg.smoothness.value; }
            if (prof.TryGet(out FilmGrain fg)) L.grain = fg.intensity.value;
            if (prof.TryGet(out Tonemapping tm)) L.tonemap = tm.mode.value.ToString();
        }

        // ------------------------------------------------------------------------------------------------ luci
        static void AddLight(Light l, Bounds box, Vector3 camPos)
        {
            if (!l.enabled || !l.gameObject.activeInHierarchy || l.intensity <= 0.001f) return;
            if (l.type != LightType.Directional && l.type != LightType.Point && l.type != LightType.Spot) return;
            if (l.type != LightType.Directional && !new Bounds(l.transform.position, Vector3.one * (l.range * 2f)).Intersects(box)) return;
            var c = l.color.linear;
            S.lights.Add(new LightJ
            {
                name = l.name, type = l.type.ToString(), pos = V3(l.transform.position), fwd = V3(l.transform.forward),
                r = c.r, g = c.g, b = c.b, intensity = l.intensity, range = l.range, spot = l.spotAngle, inner = l.innerSpotAngle,
                shadows = l.shadows != LightShadows.None
            });
        }

        // ------------------------------------------------------------------------------------------------ renderer
        static void AddRenderer(Renderer r, string group)
        {
            Mesh mesh = null; Matrix4x4 m = r.transform.localToWorldMatrix;
            if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                mesh = mf != null ? mf.sharedMesh : null;
            }
            else if (r is SkinnedMeshRenderer smr)
            {
                mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                // la mesh cotta ha gia' la scala: la matrice resta senza
                m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                baked++;
            }
            if (mesh == null || mesh.vertexCount == 0) { skipped++; return; }
            int mi;
            if (r is SkinnedMeshRenderer) mi = WriteMesh(mesh, r.name + "_baked");
            else if (!meshIds.TryGetValue(mesh, out mi)) { mi = WriteMesh(mesh, mesh.name); meshIds[mesh] = mi; }
            var mats = r.sharedMaterials;
            var ids = new int[Mathf.Min(mats.Length, S.meshes[mi].subs)];
            for (int i = 0; i < ids.Length; i++) ids[i] = mats[i] != null ? MaterialId(mats[i]) : -1;
            var cols = new float[16];
            for (int c = 0; c < 4; c++) for (int rr = 0; rr < 4; rr++) cols[c * 4 + rr] = m[rr, c];
            S.nodes.Add(new NodeJ { name = r.name, mesh = mi, mats = ids, m = cols, shadow = r.shadowCastingMode != ShadowCastingMode.Off, group = group });
        }

        static int WriteMesh(Mesh mesh, string label)
        {
            int index = S.meshes.Count;
            string file = $"meshes/m{index}.bin";
            var verts = mesh.vertices; var normals = mesh.normals; var uvs = mesh.uv;
            int subs = mesh.subMeshCount;
            using (var bw = new BinaryWriter(File.Create(outDir + "/" + file)))
            {
                bw.Write(0x314D4550);
                bw.Write(verts.Length); bw.Write(subs);
                bw.Write(normals.Length == verts.Length ? 1 : 0);
                bw.Write(uvs.Length == verts.Length ? 1 : 0);
                foreach (var v in verts) { bw.Write(v.x); bw.Write(v.y); bw.Write(v.z); }
                if (normals.Length == verts.Length) foreach (var n in normals) { bw.Write(n.x); bw.Write(n.y); bw.Write(n.z); }
                if (uvs.Length == verts.Length) foreach (var u in uvs) { bw.Write(u.x); bw.Write(u.y); }
                for (int s = 0; s < subs; s++)
                {
                    var idx = mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetTriangles(s) : new int[0];
                    bw.Write(idx.Length);
                    foreach (var i in idx) bw.Write(i);
                }
            }
            S.meshes.Add(new MeshJ { file = file, name = label, verts = verts.Length, subs = subs });
            return index;
        }

        // ------------------------------------------------------------------------------------------------ materiali
        static int MaterialId(Material mat)
        {
            if (matIds.TryGetValue(mat, out int id)) return id;
            var mj = new MatJ { name = mat.name, shader = mat.shader != null ? mat.shader.name : "", asset = AssetDatabase.GetAssetPath(mat) };
            if (mat.shader != null)
            {
                var sh = mat.shader;
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    string pn = sh.GetPropertyName(i);
                    switch (sh.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color:
                            var c = mat.GetColor(pn);
                            mj.colors.Add(new ColorJ { name = pn, r = c.r, g = c.g, b = c.b, a = c.a });
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            mj.floats.Add(new FloatJ { name = pn, v = mat.GetFloat(pn) });
                            break;
                        case ShaderPropertyType.Texture:
                            var t = mat.GetTexture(pn);
                            if (t == null) break;
                            string tp = TextureFile(t);
                            if (tp == null) break;
                            var sc = mat.GetTextureScale(pn); var of = mat.GetTextureOffset(pn);
                            mj.tex.Add(new TexJ { prop = pn, path = tp, sx = sc.x, sy = sc.y, ox = of.x, oy = of.y });
                            break;
                    }
                }
            }
            foreach (var k in mat.shaderKeywords) mj.keywords.Add(k);
            id = S.mats.Count;
            S.mats.Add(mj);
            matIds[mat] = id;
            return id;
        }

        /// <summary>Una texture di progetto resta dov'e' (percorso Assets/...); una nata a runtime si salva come PNG nella cartella.</summary>
        static string TextureFile(Texture t)
        {
            if (texFiles.TryGetValue(t, out var cached)) return cached;
            string path = AssetDatabase.GetAssetPath(t);
            string res = null;
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/") && (path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg") || path.EndsWith(".tga")))
                res = path;
            else if (t is Texture2D t2 && t2.width > 0 && t2.height > 0 && t2.width <= 4096)
            {
                try
                {
                    // pixel grezi, senza conversioni di spazio colore: la PNG porta gli stessi byte della texture di Unity
                    var rtmp = RenderTexture.GetTemporary(t2.width, t2.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    var prev = RenderTexture.active;
                    Graphics.Blit(t2, rtmp);
                    RenderTexture.active = rtmp;
                    var copy = new Texture2D(t2.width, t2.height, TextureFormat.RGBA32, false);
                    copy.ReadPixels(new Rect(0, 0, t2.width, t2.height), 0, 0);
                    copy.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rtmp);
                    string rel = $"textures/t{texFiles.Count}.png";
                    File.WriteAllBytes(outDir + "/" + rel, copy.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(copy);
                    res = rel;
                }
                catch (Exception e) { Debug.LogWarning("[UnrealExport] texture " + t.name + ": " + e.Message); }
            }
            texFiles[t] = res;
            return res;
        }
    }
}

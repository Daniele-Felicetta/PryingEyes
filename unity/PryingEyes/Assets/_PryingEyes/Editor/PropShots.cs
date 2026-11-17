using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PryingEyes.Proc;

namespace PryingEyes.EditorTools
{
    /// <summary>Foto degli arredi del kit come li vede il giocatore, senza giocare: ogni arredo (-props a,b,c oppure tutti quelli con un prefisso,
    /// -prefix gu_ e' il predefinito) sta in una piccola sala buia con i materiali del kit, la luce calda di una lampada e, con -torch, il cono
    /// della torcia; nebbia, luce ambiente e grading sono quelli del tema (-theme guardaroba, albergo, teatro, sanatorio). Due viste per arredo:
    /// tre quarti da destra e frontale. Uscita: hotel_build/props/&lt;nome&gt;_3q.png e _fronte.png (-tag x li marca). -lod N forza il livello di
    /// dettaglio N (0, 1, 2) negli arredi che ne hanno (fase 5); -dist m scala la distanza della camera.</summary>
    public static class PropShots
    {
        static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static bool Flag(string name) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;

        public static void Run()
        {
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(TorreBuilder.ScenePath);
            var player = GameObject.Find("Player");
            if (player != null) player.SetActive(false);
            var kit = Object.FindFirstObjectByType<ProcKit>();
            TorreBuilder.FillKit(kit);
            LightPool.Clear();
            var theme = System.Enum.TryParse(Arg("-theme") ?? "guardaroba", true, out Theme th) ? th : Theme.Guardaroba;
            FloorDirector.ApplyLook(new Plan { theme = theme, seed = 5, floor = 3 }, 3);
            string tag = Arg("-tag") != null ? "_" + Arg("-tag") : "";
            string prefix = Arg("-prefix") ?? "gu_";

            var names = new List<string>();
            if (Arg("-props") != null) names.AddRange(Arg("-props").Split(','));
            else foreach (var p in kit.prefabs) if (p.name.StartsWith(prefix)) names.Add(p.name);
            names.Sort();

            // la sala: pavimento e due pareti con i materiali del kit
            var room = new GameObject("Sala di prova");
            void Slab(string name, string mat, Vector3 pos, Vector3 scale, Vector3 euler)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = name;
                q.transform.SetParent(room.transform, false);
                q.transform.position = pos; q.transform.eulerAngles = euler; q.transform.localScale = scale;
                q.GetComponent<MeshRenderer>().sharedMaterial = ProcKit.Mat(mat);
            }
            Slab("pavimento", "pav_parquet", new Vector3(0f, 0f, 0f), new Vector3(24f, 24f, 1f), new Vector3(90f, 0f, 0f));
            // un quad guarda verso -Z: per vederli dalla sala i muri vanno girati verso il centro
            Slab("fondo", "mur_damasco_verde", new Vector3(0f, 3f, -4.5f), new Vector3(24f, 6f, 1f), new Vector3(0f, 180f, 0f));
            Slab("lato", "mur_damasco_verde", new Vector3(-6f, 3f, 0f), new Vector3(24f, 6f, 1f), new Vector3(0f, -90f, 0f));

            var key = new GameObject("Lampada").AddComponent<Light>();
            key.type = LightType.Point; key.color = new Color(1f, 0.64f, 0.34f); key.range = 12f; key.shadows = LightShadows.Soft;
            var fill = new GameObject("Riempimento").AddComponent<Light>();
            fill.type = LightType.Point; fill.color = new Color(0.55f, 0.50f, 0.90f); fill.range = 10f; fill.shadows = LightShadows.None;
            float distMul = float.TryParse(Arg("-dist"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dm) ? dm : 1f;

            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            var main = GameObject.Find("MainCamera");
            var mc = main != null ? main.GetComponent<Camera>() : null;
            if (mc != null)
            {
                cam.CopyFrom(mc);
                var src = mc.GetUniversalAdditionalCameraData();
                var dst = cam.GetUniversalAdditionalCameraData();
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.antialiasingQuality = src.antialiasingQuality;
            }
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            Light torch = null;
            if (Flag("-torch"))
            {
                torch = camGo.AddComponent<Light>();
                torch.type = LightType.Spot; torch.spotAngle = 70f; torch.innerSpotAngle = 50f; torch.range = 34f; torch.intensity = 36f;
                torch.color = new Color(1f, 0.93f, 0.80f); torch.shadows = LightShadows.Soft; torch.cookie = Torch.Cookie();
            }
            Directory.CreateDirectory(TestPaths.Out("props"));
            bool warm = true;
            foreach (var name in names)
            {
                var prefab = ProcKit.Prefab(name);
                if (prefab == null) { Debug.LogWarning("[PryingEyes] arredio non nel kit: " + name); continue; }
                var go = (GameObject)Object.Instantiate(prefab);
                go.name = name;
                // -lod N forza il livello di dettaglio N degli arredi che hanno un LODGroup (per vedere come sono i livelli lontani, da vicino)
                if (int.TryParse(Arg("-lod"), out int forcedLod))
                    foreach (var lg in go.GetComponentsInChildren<LODGroup>()) lg.ForceLOD(forcedLod);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;          // il davanti guarda +Z
                var b = new Bounds(Vector3.zero, Vector3.zero); bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                float h = Mathf.Max(0.3f, b.max.y), ext = Mathf.Max(h, b.size.x * 0.62f, b.size.z * 0.62f);
                Vector3 target = new Vector3(b.center.x, Mathf.Clamp(h * 0.50f, 0.25f, 1.7f), b.center.z);
                key.transform.position = new Vector3(b.center.x + 0.9f, Mathf.Max(2.6f, h + 0.9f), b.max.z + 1.8f); key.intensity = 16f;
                fill.transform.position = new Vector3(b.center.x - 2.4f, 1.4f, b.max.z + 1.4f); fill.intensity = 3f;
                float d = (ext * 1.29f + b.extents.z + 0.4f) * distMul;     // l'arredo riempie circa il 70% dell'altezza della foto
                foreach (var (suffix, off) in new[] { ("3q", new Vector3(0.62f, 0.12f, 0.78f)), ("fronte", new Vector3(0f, 0.10f, 1f)) })
                {
                    cam.fieldOfView = 58f;
                    camGo.transform.position = target + off.normalized * d + Vector3.up * 0.2f;
                    camGo.transform.rotation = Quaternion.LookRotation(target - camGo.transform.position);
                    LightPool.Tick(false, camGo.transform.position);
                    cam.Render();
                    Save(cam, rt, $"{name}_{suffix}{tag}.png");
                }
                Object.DestroyImmediate(go);
                warm = !warm;
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Debug.Log($"[PryingEyes] foto arredi: {names.Count}");
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
            File.WriteAllBytes(TestPaths.Out("props/" + file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[PryingEyes] foto " + file);
        }
    }
}

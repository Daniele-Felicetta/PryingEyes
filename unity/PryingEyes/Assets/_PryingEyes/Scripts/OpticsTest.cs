using System;
using System.Collections;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -opticstest: il reticolo renderizzato deve coincidere con l'asse hitscan, anche con sway.</summary>
    public sealed class OpticsTest : MonoBehaviour
    {
        int checks, failures, exceptions;
        string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-opticstest") >= 0)
                new GameObject("OpticsTest").AddComponent<OpticsTest>();
        }

        void Awake()
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Validation/Optics"));
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-opticout") output = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
        }
        void OnDestroy() => Application.logMessageReceived -= OnLog;
        void OnLog(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }
        void Check(string name, bool ok, string detail = "")
        {
            checks++; if (!ok) failures++;
            Debug.Log($"[PryingEyes] opticstest {(ok ? "ok" : "FALLITO")} {name} {detail}");
        }

        IEnumerator Start()
        {
            var run = Run();
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("copione completo", false); break; }
                if (!more) break;
                yield return run.Current;
            }
            Check("nessuna eccezione", exceptions == 0);
            Debug.Log($"[PryingEyes] opticstest RISULTATO {(failures == 0 ? "SUPERATA" : "FALLITA")}: {checks - failures}/{checks}");
            Application.Quit(failures == 0 ? 0 : 1);
        }

        bool Capture(Camera camera, string name)
        {
            var rt = RenderTexture.GetTemporary(1280, 720, 24);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; camera.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                int marks = 0;
                for (int y = 358; y <= 362; y++) for (int x = 638; x <= 642; x++)
                {
                    Color c = tex.GetPixel(x, y);
                    bool red = c.r > 0.10f && c.r > c.g * 1.8f && c.r > c.b * 1.8f;
                    if (red) marks++;
                }
                File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
                return marks > 0;
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt); Destroy(tex);
            }
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            Check("grafica disponibile", SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null);
            var gm = GameManager.I; var player = gm.player; var weapon = player.weapon;
            player.enabled = false; player.invulnerable = true;
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = true;
            gm.hud.GetComponent<Canvas>().enabled = false;
            RenderSettings.fog = false;
            var camera = player.cam.GetComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(1000, 1.6f, 1000), Quaternion.identity);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.035f, 0.035f, 0.035f);
            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            target.transform.position = camera.transform.position + camera.transform.forward * 20f;
            target.transform.localScale = Vector3.one * 0.6f;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.color = new Color(0.02f, 0.8f, 0.02f); target.GetComponent<Renderer>().sharedMaterial = mat;
            Weapon.debugAds = true;
            for (int i = 0; i < WeaponDefs.All.Length; i++)
            {
                var d = WeaponDefs.All[i];
                if (d.zoom <= 1.4f && d.sight != "reflex" && d.sight != "holo" && d.sight != "ring") continue;
                weapon.ResetLoadout(); weapon.Equip(i); camera.fieldOfView = d.adsFov;
                for (int tick = 0; tick < 70; tick++) weapon.Tick(0.02f, false);
                yield return null;
                target.SetActive(false);
                weapon.RenderScopeNow();
                Check(d.id + " reticolo sull'asse a riposo", Capture(camera, d.id + "_idle"));
                for (int tick = 0; tick < 70; tick++) weapon.Tick(0.02f, false, 0f, true, false, new Vector2(5f, 3f));
                weapon.RenderScopeNow();
                Check(d.id + " reticolo sull'asse con sway", Capture(camera, d.id + "_sway"));
                target.SetActive(true); weapon.RenderScopeNow();
                Capture(camera, d.id + "_target"); // bersaglio verde e reticolo insieme, senza falsare il rilevamento del punto additivo
                if (d.zoom > 1.4f)
                {
                    var scope = Array.Find(player.cam.GetComponentsInChildren<Camera>(), c => c != camera && c.name == "ScopeCam");
                    if (scope == null) { Check(d.id + " camera ottica", false); continue; }
                    var ray = scope.ViewportPointToRay(new Vector3(0.5f, 0.5f));
                    float miss = Vector3.Cross(target.transform.position - ray.origin, ray.direction).magnitude;
                    Check(d.id + " scena ingrandita coincide con hitscan", miss < 0.005f, $"scarto a 20 m {miss:F4} m");
                }
                yield return null;
            }
            Weapon.debugAds = false;
            Destroy(target); Destroy(mat);
        }
    }
}

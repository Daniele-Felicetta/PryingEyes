using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.EditorTools
{
    /// <summary>Foto delle pose del corpo procedurale (SurvivorBody): una scheda per vista con tutte le pose (a riposo, con l'arma,
    /// in mira, in cammino, di corsa, accovacciato, a terra, morto). Servono a tarare la camminata e le prese senza avviare il gioco.
    /// Da CLI: Unity.exe -projectPath ... -executeMethod PryingEyes.EditorTools.SurvivorShots.Poses [-slot N] [-only a,b] -quit
    /// (con la scheda grafica: niente -nographics). Scrive hotel_build/survivors/poses_&lt;vista&gt;_&lt;id&gt;.png.</summary>
    public static class SurvivorShots
    {
        sealed class P
        {
            public string name; public SurvivorInput inp; public float phase = -1f; public int reloadSteps;
        }

        static SurvivorInput S(string weapon, float ads = 0f, float vel = 0f, bool crouch = false, bool sprint = false, float pitch = 0f,
                               float lean = 0f, bool downed = false, bool alive = true, float shot = 99f)
        {
            var i = SurvivorInput.Idle;
            i.weapon = string.IsNullOrEmpty(weapon) ? -1 : WeaponDefs.IndexOf(weapon);
            i.ads = ads; i.velocity = new Vector3(0f, 0f, vel); i.crouch = crouch; i.sprint = sprint; i.pitch = pitch; i.lean = lean;
            i.downed = downed; i.alive = alive; i.shotAge = shot;
            return i;
        }

        static List<P> All()
        {
            return new List<P>
            {
                new P { name = "idle", inp = S(null) },
                new P { name = "rifle", inp = S("ak_arcangelo") },
                new P { name = "rifle_ads", inp = S("ak_arcangelo", ads: 1f) },
                new P { name = "pistol", inp = S("p9_pieta") },
                new P { name = "pistol_ads", inp = S("p9_pieta", ads: 1f) },
                new P { name = "shotgun_ads", inp = S("sg_cappellano", ads: 1f) },
                new P { name = "sniper_ads", inp = S("snp_sentinella", ads: 1f) },
                new P { name = "walk_a", inp = S(null, vel: 1.6f), phase = 0.0f },
                new P { name = "walk_b", inp = S(null, vel: 1.6f), phase = 0.25f },
                new P { name = "walk_c", inp = S(null, vel: 1.6f), phase = 0.55f },
                new P { name = "walk_rifle", inp = S("ak_arcangelo", vel: 1.6f), phase = 0.1f },
                new P { name = "sprint_rifle", inp = S("ak_arcangelo", vel: 5.5f, sprint: true), phase = 0.15f },
                new P { name = "sprint_b", inp = S(null, vel: 5.5f, sprint: true), phase = 0.4f },
                new P { name = "crouch_rifle", inp = S("ak_arcangelo", crouch: true) },
                new P { name = "crouch_walk", inp = S(null, vel: 1.2f, crouch: true), phase = 0.3f },
                new P { name = "lean_ads", inp = S("ak_arcangelo", ads: 1f, lean: 1f) },
                new P { name = "look_down", inp = S("ak_arcangelo", pitch: 0.9f) },
                new P { name = "look_up_ads", inp = S("ak_arcangelo", ads: 1f, pitch: -0.7f) },
                new P { name = "reload_a", inp = S("ak_arcangelo"), reloadSteps = 14 },
                new P { name = "reload_b", inp = S("ak_arcangelo"), reloadSteps = 26 },
                new P { name = "knife", inp = S("kn_coltello") },
                new P { name = "lmg", inp = S("lmg_salterio") },
                new P { name = "downed", inp = S(null, downed: true) },
                new P { name = "dead", inp = S(null, alive: false) },
            };
        }

        static Material Flat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        static string Arg(string name, string def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Poses()
        {
            ShaderUtil.allowAsyncCompilation = false;
            int slot = int.Parse(Arg("-slot", "0"));
            string only = Arg("-only", "");
            var list = All();
            if (only.Length > 0)
            {
                var keep = new HashSet<string>(only.Split(','));
                list = list.FindAll(p => keep.Contains(p.name));
            }
            string outDir = TestPaths.Out("survivors");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.24f, 0.24f, 0.26f);
            RenderSettings.skybox = null;
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.2f;
            key.color = new Color(1f, 0.94f, 0.86f);
            key.transform.rotation = Quaternion.Euler(42f, -30f, 0f);
            key.shadows = LightShadows.Soft;
            Stealth.SetLightLayers(key, 3u);       // come le luci del mondo: illumina anche i corpi (strato di luce 2)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = Flat(new Color(0.16f, 0.16f, 0.17f));
            Physics.SyncTransforms();
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.07f, 0.08f);
            var ad = cam.GetUniversalAdditionalCameraData();
            ad.renderPostProcessing = false;
            ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            const int W = 330, H = 462, cols = 6;
            int rows = (list.Count + cols - 1) / cols;
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            var views = new (string name, Vector3 pos, Vector3 look, float fov)[]
            {
                ("lato", new Vector3(3.3f, 0.95f, 0.15f), new Vector3(0f, 0.92f, 0.1f), 36f),
                ("fronte", new Vector3(1.6f, 1.25f, 3.1f), new Vector3(0f, 0.92f, 0f), 36f),
                ("dietro", new Vector3(-1.5f, 1.45f, -3.0f), new Vector3(0f, 0.9f, 0f), 36f),
            };
            string viewsArg = Arg("-views", "lato,fronte");
            var cell = new Texture2D(W, H, TextureFormat.RGB24, false);
            foreach (var v in views)
            {
                if (!viewsArg.Contains(v.name)) continue;
                var sheet = new Texture2D(W * cols, H * rows, TextureFormat.RGB24, false);
                int n = 0;
                foreach (var p in list)
                {
                    var root = new GameObject("Corpo");
                    var body = SurvivorBody.Create(root.transform, slot, 0);
                    if (body == null) { Debug.LogError("[Survivor] corpo non creato"); return; }
                    body.autoStep = false;
                    body.input = p.inp;
                    body.debugPhase = p.phase;
                    for (int s = 0; s < 40; s++) body.Step(0.033f);
                    if (p.reloadSteps > 0)
                    {
                        var r = body.input; r.reloading = true; body.input = r;
                        for (int s = 0; s < p.reloadSteps; s++) body.Step(0.033f);
                    }
                    camGo.transform.position = v.pos;
                    camGo.transform.LookAt(v.look);
                    cam.fieldOfView = v.fov;
                    cam.Render();
                    cam.Render();
                    var old = RenderTexture.active;
                    RenderTexture.active = rt;
                    cell.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    cell.Apply();
                    RenderTexture.active = old;
                    int cx = n % cols, cy = rows - 1 - n / cols;
                    sheet.SetPixels32(cx * W, cy * H, W, H, cell.GetPixels32());
                    n++;
                    Object.DestroyImmediate(root);
                }
                sheet.Apply();
                string path = $"{outDir}/poses_{v.name}_{SurvivorBody.IdOf(slot)}.png";
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                Debug.Log($"[Survivor] scheda {v.name}: {path} (" + string.Join(", ", list.ConvertAll(p => p.name)) + ")");
                Object.DestroyImmediate(sheet);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(cell);
        }
    }
}

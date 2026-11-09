using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.EditorTools
{
    /// <summary>Renders Hotel.unity from gameplay-relevant poses to PNG (no playmode needed). Run via CLI.
    /// The shot camera copies the game camera (post-processing, anti-aliasing), so the PNGs match what the
    /// player sees. Optional "-shotTag x" writes shot_name_x.png (e.g. before/after comparisons).</summary>
    public static class ShotScene
    {
        struct Pose { public Vector3 pos; public Vector3 rot; public string name; }

        static string Tag() { var v = Arg("-shotTag"); return v != null ? "_" + v : ""; }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        public static void Shots()
        {
            // shader compilati subito: con la compilazione asincrona dell'editor, il primo fotogramma che usa una
            // variante nuova la disegna col segnaposto (le finestre viste dal salotto uscivano fucsia). Il gioco
            // compilato non ha il problema: le varianti sono gia' pronte.
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene("Assets/_PryingEyes/Scenes/Hotel.unity");
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            var main = GameObject.Find("MainCamera");
            var mainCam = main != null ? main.GetComponent<Camera>() : null;
            if (mainCam != null)
            {
                cam.CopyFrom(mainCam);
                var src = mainCam.GetUniversalAdditionalCameraData();
                var dst = cam.GetUniversalAdditionalCameraData();
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.antialiasingQuality = src.antialiasingQuality;
                Debug.Log("[PryingEyes] shot cam copies game cam: post=" + src.renderPostProcessing + " aa=" + src.antialiasing);
            }
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;

            var poses = new[]
            {
                new Pose { pos = new Vector3(0, 1.75f, 19f), rot = new Vector3(0, 180f, 0), name = "spawn_north" },
                new Pose { pos = new Vector3(0, 1.75f, 0f), rot = new Vector3(0, 180f, 0), name = "mid_stairs" },
                new Pose { pos = new Vector3(-20, 1.75f, 9f), rot = new Vector3(0, 90f, 0), name = "lounge" },
                new Pose { pos = new Vector3(-20, 2f, -14f), rot = new Vector3(0, 243f, 0), name = "crocage" },
                // reception e terzo lampadario (quello spento, poi caduto)
                new Pose { pos = new Vector3(12, 1.75f, 13f), rot = new Vector3(6.4f, 128.7f, 0), name = "reception" },
                // scalone e lucernario da vicino, guardando un po' in alto
                new Pose { pos = new Vector3(6, 1.75f, 9f), rot = new Vector3(-4.4f, 201.8f, 0), name = "scalone" },
                // ascensore centrale della parete ovest (cornice, freccia, porte)
                new Pose { pos = new Vector3(-27.5f, 1.7f, 3.5f), rot = new Vector3(3f, 246f, 0), name = "ascensore" },
                // materiali da vicino: pavimento sotto il lucernario (pozzanghere), parete sud con boiserie e applique
                new Pose { pos = new Vector3(-9f, 1.1f, 3.5f), rot = new Vector3(14f, 160f, 0), name = "pavimento" },
                new Pose { pos = new Vector3(-12f, 1.6f, 18.5f), rot = new Vector3(-6f, 35f, 0), name = "parete" },
                // occhi nel buio sotto il soppalco, da 11 m (distanza da gioco)
                new Pose { pos = new Vector3(-6f, 1.7f, -8f), rot = new Vector3(0.5f, 211f, 0), name = "occhi" },
            };
            // le particelle in edit-mode non girano da sole: avanzarle di qualche secondo prima di scattare
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                ps.Simulate(12f, true, true);
            string tag = Tag();
            var only = Arg("-onlyPose");   // "-onlyPose lounge": una sola inquadratura
            if (only != null) poses = System.Array.FindAll(poses, p => p.name == only);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            // fotogramma di riscaldamento: dopo OpenScene il primo render può uscire senza texture
            camGo.transform.position = poses[0].pos;
            camGo.transform.rotation = Quaternion.Euler(poses[0].rot);
            cam.Render();
            foreach (var p in poses)
            {
                camGo.transform.position = p.pos;
                camGo.transform.rotation = Quaternion.Euler(p.rot);
                // riscaldamento per ogni posa: oggetti che compaiono per la prima volta a meta' sessione uscivano
                // sbagliati nel primo fotogramma (finestre fucsia dal salotto); il secondo e' giusto
                cam.Render();
                cam.Render();
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                System.IO.File.WriteAllBytes(
                    "C:/Users/danie/Desktop/prying-eyes/hotel_build/shot_" + p.name + tag + ".png",
                    tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[PryingEyes] shot saved: " + p.name + tag);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }
    }
}

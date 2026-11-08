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

        static string Tag()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-shotTag") return "_" + args[i + 1];
            return "";
        }

        public static void Shots()
        {
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
            };
            string tag = Tag();
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

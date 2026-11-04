using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace PryingEyes.EditorTools
{
    /// <summary>Renders Hotel.unity from gameplay-relevant poses to PNG (no playmode needed). Run via CLI.</summary>
    public static class ShotScene
    {
        struct Pose { public Vector3 pos; public Vector3 rot; public string name; }

        public static void Shots()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_PryingEyes/Scenes/Hotel.unity");
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;

            var poses = new[]
            {
                new Pose { pos = new Vector3(0, 1.75f, 19f), rot = new Vector3(0, 180f, 0), name = "spawn_north" },
                new Pose { pos = new Vector3(0, 1.75f, 0f), rot = new Vector3(0, 180f, 0), name = "mid_stairs" },
                new Pose { pos = new Vector3(-20, 1.75f, 9f), rot = new Vector3(0, 90f, 0), name = "lounge" },
                new Pose { pos = new Vector3(-20, 2f, -14f), rot = new Vector3(0, 243f, 0), name = "crocage" },
            };
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
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
                    "C:/Users/danie/Desktop/prying-eyes/hotel_build/shot_" + p.name + ".png",
                    tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[PryingEyes] shot saved: " + p.name);
            }
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }
    }
}

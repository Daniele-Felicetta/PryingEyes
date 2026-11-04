using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace PryingEyes.EditorTools
{
    public static class DiagMat
    {
        struct Pose { public Vector3 pos; public Vector3 rot; public string name; }

        public static void Dump()
        {
            EditorSceneManager.OpenScene("Assets/_PryingEyes/Scenes/Hotel.unity");
            var floorGo = GameObject.Find("Floor");
            if (floorGo != null)
            {
                var mr = floorGo.GetComponent<MeshRenderer>();
                var mf = floorGo.GetComponent<MeshFilter>();
                var m = mr.sharedMaterial;
                var uv = mf.sharedMesh.uv;
                Debug.Log("[DIAG] floor mat=" + (m != null ? m.name : "NULL")
                    + " mainTex=" + (m != null && m.mainTexture != null ? m.mainTexture.name : "NULL")
                    + " baseMap=" + (m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : "NULL")
                    + " verts=" + mf.sharedMesh.vertexCount + " uv0=" + uv[0] + " uvLast=" + uv[uv.Length - 1]
                    + " scale=" + floorGo.transform.localScale);
            }
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !m.name.Contains("Floor")) continue;
                Debug.Log("[DIAG] renderer=" + r.gameObject.name
                    + " mat=" + m.name
                    + " shader=" + m.shader.name
                    + " mainTex=" + (m.mainTexture != null ? m.mainTexture.name : "NULL")
                    + " baseMap=" + (m.HasProperty("_BaseMap") ? (m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : "NULL") : "NO-PROP")
                    + " baseMapST=" + (m.HasProperty("_BaseMap_ST") ? m.GetVector("_BaseMap_ST").ToString() : "NO-PROP")
                    + " mainTexST=" + m.mainTextureScale.ToString());
                break;
            }
            // orientation disambiguation shots
            var camGo = new GameObject("DiagCam");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            var poses = new[]
            {
                new Pose { pos = new Vector3(0, 2.5f, 16f), rot = new Vector3(30f, 180f, 0), name = "diag_floorclose" },
                new Pose { pos = new Vector3(0, 1.75f, 10f), rot = new Vector3(10f, 180f, 0), name = "diag_quads" },
            };
            // test quads: same floorMat vs fresh material, at eye level
            var floorMat = GameObject.Find("Floor").GetComponent<MeshRenderer>().sharedMaterial;
            var q1 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q1.transform.position = new Vector3(-2f, 1.5f, 10f);
            q1.transform.rotation = Quaternion.Euler(0, 180f, 0);
            q1.transform.localScale = new Vector3(2f, 2f, 1f);
            q1.GetComponent<MeshRenderer>().material = floorMat;
            Object.DestroyImmediate(q1.GetComponent<MeshCollider>());
            var fresh = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            fresh.SetTexture("_BaseMap", floorMat.GetTexture("_BaseMap"));
            fresh.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            var q2 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q2.transform.position = new Vector3(2f, 1.5f, 10f);
            q2.transform.rotation = Quaternion.Euler(0, 180f, 0);
            q2.transform.localScale = new Vector3(2f, 2f, 1f);
            q2.GetComponent<MeshRenderer>().material = fresh;
            Object.DestroyImmediate(q2.GetComponent<MeshCollider>());
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
                System.IO.File.WriteAllBytes("C:/Users/danie/Desktop/prying-eyes/hotel_build/shot_" + p.name + ".png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[DIAG] shot: " + p.name);
            }
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Debug.Log("[DIAG] done");
        }
    }
}

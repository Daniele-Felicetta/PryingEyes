using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes.EditorTools
{
    /// <summary>Render and validate menu layouts without running the game or changing player display preferences.</summary>
    public static class MenuReview
    {
        static int failures;
        static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Debug.Log("MENU REVIEW " + (ok ? "PASS " : "FAIL ") + label);
        }

        public static void Run()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../unity_logs/menu-review"));
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Review camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = 10f;
            GameMenu.Create();
            var menu = GameMenu.I;
            if (menu.GetComponent<Canvas>() == null) menu.SendMessage("Awake");
            var canvas = menu.GetComponent<Canvas>();
            menu.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;

            var wide = VideoSettings.Modes(FullScreenMode.Windowed, 2);
            Check("QHD ultrawide preset", wide.Contains(new Vector2Int(3440, 1440)));
            Check("ultrawide filter", wide.TrueForAll(r => VideoSettings.Aspect(r.x, r.y) == "21:9 ULTRAWIDE"));
            var invalid = VideoSettings.Validate(new VideoSettings.Config { width = -1, height = 0, mode = (FullScreenMode)999, hz = -3, vsync = 99, fps = 777 });
            Check("invalid settings constrained", invalid.width == 640 && invalid.height == 360 && invalid.mode == FullScreenMode.FullScreenWindow && invalid.hz == 0 && invalid.vsync == 1 && invalid.fps == -1);
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(3440, 1440), new Vector2Int(1024, 768) })
            {
                var rt = new RenderTexture(size.x, size.y, 24); rt.Create(); camera.targetTexture = rt;
                canvas.scaleFactor = Mathf.Min(size.x / 1280f, size.y / 720f);
                menu.SendMessage("Show", menu.transform.Find("Velo/Principale").GetComponent<RectTransform>());
                Render("main", size, camera, menu, output);
                Click("Impostazioni"); Render("display", size, camera, menu, output);
                Click("Audio e comandi"); Render("comfort", size, camera, menu, output);
                Check("seven comfort sliders", menu.GetComponentsInChildren<Slider>().Length == 7);
                Click("Schermo"); Click("Formato ›"); Click("Formato ›");
                Check("ultrawide choice", GameObject.Find("Formato valore").GetComponent<Text>().text.Contains("21:9"));
                Render("ultrawide-options", size, camera, menu, output);
                Click("Indietro"); Click("Come si gioca"); Render("help", size, camera, menu, output);
                Click("Indietro"); Click("Cooperativa"); Render("coop", size, camera, menu, output); Click("Indietro");
                if (size.x == 1024)
                {
                    menu.SendMessage("Play"); menu.SendMessage("Pause");
                    Check("pause stops the world", GameMenu.Paused && GameMenu.Blocked && Time.timeScale == 0f);
                    Render("pause", size, camera, menu, output);
                    Click("Torna al menu"); Render("leave", size, camera, menu, output); Click("Annulla");
                    Click("Riprendi"); Check("resume releases input", !GameMenu.Blocked && Time.timeScale == 1f);
                }
                camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt);
            }
            UnityEngine.Object.DestroyImmediate(menu.gameObject);
            File.WriteAllText(Path.Combine(output, "result.txt"), failures == 0 ? "PASS" : "FAIL " + failures);
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Click(string label)
        {
            var go = GameObject.Find("Pulsante " + label);
            if (go == null) throw new InvalidOperationException(label);
            go.GetComponent<Button>().onClick.Invoke();
        }

        static void Render(string name, Vector2Int size, Camera camera, GameMenu menu, string output)
        {
            foreach (var art in menu.GetComponentsInChildren<MenuEngraving>()) { art.SetAllDirty(); }
            Canvas.ForceUpdateCanvases();
            camera.Render(); Canvas.ForceUpdateCanvases();
            foreach (var art in menu.GetComponentsInChildren<MenuEngraving>())
                Check("engraving rendered " + art.name, art.canvasRenderer.GetMesh().vertexCount > 0);
            foreach (var text in menu.GetComponentsInChildren<Text>())
            {
                Check(name + " text fits " + text.name, text.preferredHeight <= text.rectTransform.rect.height + 2f);
                var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
                foreach (var p in corners)
                {
                    var screen = camera.WorldToViewportPoint(p);
                    if (screen.x < -0.001f || screen.y < -0.001f || screen.x > 1.001f || screen.y > 1.001f)
                    { Check(name + " text in screen " + text.name, false); break; }
                }
            }
            camera.Render();
            var before = RenderTexture.active; RenderTexture.active = camera.targetTexture;
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply(); RenderTexture.active = before;
            File.WriteAllBytes(Path.Combine(output, name + "-" + size.x + "x" + size.y + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}

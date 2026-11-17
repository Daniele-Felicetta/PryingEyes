#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Dedicated display acceptance check; the launching harness must restore display/Screenmanager registry values.</summary>
    public sealed class DisplayTest : MonoBehaviour
    {
        int failed, checks;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-displaytest") >= 0)
                new GameObject("DisplayTest").AddComponent<DisplayTest>();
        }
        static FieldInfo Field(string name) => typeof(GameMenu).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("DISPLAYTEST " + (ok ? "PASS " : "FAIL ") + name); }
        static void Click(string label) => GameObject.Find("Pulsante " + label).GetComponent<Button>().onClick.Invoke();
        /// <summary>A row of the Schermo tab (0.8.0: choices with ‹ value ›, no longer two buttons).</summary>
        static MenuChoice Choice(string title) => GameObject.Find("Scelta " + title).GetComponent<MenuChoice>();
        IEnumerator Start()
        {
            var original = VideoSettings.Current;
            var run = Run();
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception ex) { Debug.LogException(ex); Check("complete sequence", false); break; }
                if (!more) break;
                yield return run.Current;
            }
            VideoSettings.Apply(original);
            yield return new WaitForSecondsRealtime(0.5f);
            Debug.Log($"DISPLAYTEST RESULT {(failed == 0 ? "PASS" : "FAIL")} {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }
        IEnumerator WaitForSize(int width, int height)
        {
            float deadline = Time.realtimeSinceStartup + 6f;
            do { yield return null; } while ((Screen.width != width || Screen.height != height) && Time.realtimeSinceStartup < deadline);
        }
        IEnumerator Run()
        {
            yield return null;
            var menu = GameMenu.I;
            Check("menu available and silent", menu != null && AudioListener.volume == 0f);
            int width = Screen.width, height = Screen.height;
            int saved = PlayerPrefs.GetInt("display_confirmed_v1", 0);
            Click("Impostazioni"); Click("Schermo");
            for (int i = 0; i < 3 && GameObject.Find("Modalità schermo valore").GetComponent<Text>().text != "Finestra"; i++) Choice("Modalità schermo").Step(1);
            for (int i = 0; i < 100 && GameObject.Find("Risoluzione valore").GetComponent<Text>().text != "1600 × 900"; i++) Choice("Risoluzione").Step(1);
            Click("Applica schermo");
            yield return WaitForSize(1600, 900);
            Check("preview changes real render resolution", Screen.width == 1600 && Screen.height == 900);
            Check("preview does not save", PlayerPrefs.GetInt("display_confirmed_v1", 0) == saved);
            Check("confirmation blocks underlying controls", !menu.transform.Find("Velo/Impostazioni").GetComponent<CanvasGroup>().interactable);
            Field("displayDeadline").SetValue(menu, Time.realtimeSinceStartup - 1f);
            yield return WaitForSize(width, height);
            Check("timeout restores resolution while paused", Screen.width == width && Screen.height == height && Time.timeScale == 0f);
            Check("timeout closes confirmation", !(bool)Field("displayPending").GetValue(menu));
            for (int i = 0; i < 100 && GameObject.Find("Risoluzione valore").GetComponent<Text>().text != "1600 × 900"; i++) Choice("Risoluzione").Step(1);
            Click("Applica schermo"); yield return WaitForSize(1600, 900);
            Click("Ripristina"); yield return WaitForSize(width, height);
            Check("explicit cancel restores resolution", Screen.width == width && Screen.height == height);
            for (int i = 0; i < 100 && GameObject.Find("Risoluzione valore").GetComponent<Text>().text != "1600 × 900"; i++) Choice("Risoluzione").Step(1);
            Click("Applica schermo"); yield return WaitForSize(1600, 900);
            Click("Mantieni impostazioni");
            Check("confirm persists applied resolution", PlayerPrefs.GetInt("display_confirmed_v1", 0) == 1 && PlayerPrefs.GetInt("display_width", 0) == Screen.width && PlayerPrefs.GetInt("display_height", 0) == Screen.height);
            Check("confirm returns to settings", !(bool)Field("displayPending").GetValue(menu) && menu.transform.Find("Velo/Impostazioni").GetComponent<CanvasGroup>().interactable);
        }
    }
}
#endif

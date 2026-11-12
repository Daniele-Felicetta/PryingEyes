using UnityEngine;

namespace PryingEyes
{
    // Input touch condiviso: ogni dito mantiene il proprio comando fino al rilascio.
    [DefaultExecutionOrder(-10000)]
    public class TouchControls : MonoBehaviour
    {
        public enum Action { Fire, Aim, Jump, Crouch, Sprint, Use, Reload, Swap, Mode, Torch, Battery, Ping, Left, Right, Pause, Knife, Velo, Map }
        static readonly bool[] held = new bool[24], down = new bool[24];
        static readonly string[] labels = { "FUOCO", "MIRA", "SALTA", "GIU", "CORRI", "USA", "RICARICA", "ARMA", "MODO", "TORCIA", "PILA", "PING", "<", ">", "II", "COLTELLO", "VELO", "MAPPA" };
        static readonly Rect[] buttons = {
            new Rect(1135, 480, 130, 110), new Rect(995, 480, 130, 110), new Rect(1135, 600, 130, 100),
            new Rect(995, 600, 130, 100), new Rect(155, 465, 125, 65), new Rect(855, 600, 130, 100),
            new Rect(855, 480, 130, 110), new Rect(1135, 390, 130, 75), new Rect(995, 390, 130, 75),
            new Rect(710, 600, 135, 100), new Rect(710, 510, 135, 75), new Rect(710, 420, 135, 75),
            new Rect(295, 620, 90, 80), new Rect(395, 620, 90, 80), new Rect(1200, 10, 65, 60),
            new Rect(855, 390, 130, 75), new Rect(855, 305, 130, 70), new Rect(1060, 10, 130, 60)
        };
        static readonly System.Collections.Generic.Dictionary<int, int> fingers = new System.Collections.Generic.Dictionary<int, int>();
        public static Vector2 Move { get; private set; }
        public static Vector2 Look { get; private set; }
        public static bool Held(Action a) => held[(int)a];
        public static bool Down(Action a) => down[(int)a];
        static Rect safe;
        static Vector2 joystick;
        static int moveFinger = -1;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern int IsMobileBrowser();
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
#if UNITY_IOS || UNITY_ANDROID || (UNITY_WEBGL && !UNITY_EDITOR)
#if UNITY_WEBGL && !UNITY_EDITOR
            if (IsMobileBrowser() == 0) return;
#endif
            var go = new GameObject("TouchControls");
            DontDestroyOnLoad(go);
            go.AddComponent<TouchControls>();
            Input.simulateMouseWithTouches = false;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
#endif
        }

        static Vector2 Position(Vector2 screen) => new Vector2((screen.x - safe.x) / safe.width * 1280f, (safe.yMax - screen.y) / safe.height * 720f);
        void Update()
        {
            ProcessTouches(Input.touches);
        }

        // Separato dal campionamento per verificare anche sequenze multitouch senza un telefono.
        internal static void ProcessTouches(Touch[] touches)
        {
            safe = Screen.safeArea;
            System.Array.Clear(down, 0, down.Length);
            System.Array.Clear(held, 0, held.Length);
            Look = Vector2.zero;
            if (GameMenu.Blocked || safe.width <= 0 || safe.height <= 0)
            {
                System.Array.Clear(held, 0, held.Length);
                fingers.Clear(); moveFinger = -1; Move = Vector2.zero;
                return;
            }
            foreach (Touch t in touches)
            {
                Vector2 pos = Position(t.position);
                if (t.phase == TouchPhase.Began)
                {
                    int target = -1;
                    for (int i = 0; i < buttons.Length; i++) if (buttons[i].Contains(pos)) { target = i; break; }
                    if (target < 0 && pos.x < 400f && moveFinger < 0) { target = -2; moveFinger = t.fingerId; joystick = pos; }
                    if (target < 0 && target != -2) target = -3;
                    fingers[t.fingerId] = target;
                    if (target >= 0) down[target] = true;
                }
                if (!fingers.TryGetValue(t.fingerId, out int action)) continue;
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    if (action == -2) { moveFinger = -1; Move = Vector2.zero; }
                    fingers.Remove(t.fingerId);
                }
                else if (action == -2) Move = Vector2.ClampMagnitude(new Vector2(pos.x - joystick.x, joystick.y - pos.y) / 90f, 1f);
                else if (action == -3 && t.phase == TouchPhase.Moved)
                    Look += new Vector2(t.deltaPosition.x / safe.width, t.deltaPosition.y / safe.height);
            }
            // Un rilascio non annulla il comando se un altro dito lo sta ancora tenendo.
            foreach (int action in fingers.Values) if (action >= 0) held[action] = true;
        }
        void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            ResetInput();
        }
        public static void ResetInput()
        {
            System.Array.Clear(held, 0, held.Length); System.Array.Clear(down, 0, down.Length);
            fingers.Clear(); moveFinger = -1; Move = Look = Vector2.zero;
        }
        void OnGUI()
        {
            if (GameMenu.Blocked || safe.width <= 0 || safe.height <= 0) return;
            GUI.matrix = Matrix4x4.TRS(new Vector3(safe.x, Screen.height - safe.yMax, 0), Quaternion.identity, new Vector3(safe.width / 1280f, safe.height / 720f, 1));
            var style = new GUIStyle(GUI.skin.button) { fontSize = 21, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.color = new Color(1, 1, 1, 0.62f);
            for (int i = 0; i < buttons.Length; i++) GUI.Box(buttons[i], labels[i], style);
            GUI.Box(new Rect(20, 550, 140, 140), "MOVE", style);
        }
    }
}

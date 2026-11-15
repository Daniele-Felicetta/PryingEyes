using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Display configuration. Only confirmed previews are persisted; tests never apply saved display settings.</summary>
    public static class VideoSettings
    {
        public struct Config
        {
            public int width, height, hz, vsync, fps;
            public FullScreenMode mode;
        }
        const string Key = "display_confirmed_v1";
        public static readonly int[] FrameLimits = { -1, 30, 60, 90, 120, 144, 165, 240 };
        public static Config Current => new Config { width = Screen.width, height = Screen.height,
            hz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value), mode = Screen.fullScreenMode,
            vsync = QualitySettings.vSyncCount, fps = Application.targetFrameRate };

        public static bool Automated => Array.Exists(Environment.GetCommandLineArgs(),
            a => a == "-batchmode" || a == "-autotest" || a == "-uitest" || a == "-guntest" || a == "-menureview" || a == "-displaytest");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (Automated || Application.isEditor || SplitScreen.IsChild) return;      // le finestre dello split screen hanno la loro misura (SplitScreen)
            Config c;
            if (PlayerPrefs.GetInt(Key, 0) == 1)
            {
                c = new Config { width = PlayerPrefs.GetInt("display_width", 1920), height = PlayerPrefs.GetInt("display_height", 1080),
                    hz = PlayerPrefs.GetInt("display_hz", 0), mode = (FullScreenMode)PlayerPrefs.GetInt("display_mode", 1),
                    vsync = PlayerPrefs.GetInt("display_vsync", 1), fps = PlayerPrefs.GetInt("display_fps", -1) };
            }
            else
            {
                c = Current; c.width = Screen.currentResolution.width; c.height = Screen.currentResolution.height;
                c.mode = FullScreenMode.FullScreenWindow; c.vsync = 1; c.fps = -1;
            }
            Apply(c);
        }

        public static Config Validate(Config c)
        {
            if (c.mode != FullScreenMode.Windowed && c.mode != FullScreenMode.FullScreenWindow && c.mode != FullScreenMode.ExclusiveFullScreen)
                c.mode = FullScreenMode.FullScreenWindow;
            c.width = Mathf.Clamp(c.width, 640, 16384); c.height = Mathf.Clamp(c.height, 360, 8640);
            c.hz = Mathf.Clamp(c.hz, 0, 1000); c.vsync = Mathf.Clamp(c.vsync, 0, 1);
            if (Array.IndexOf(FrameLimits, c.fps) < 0) c.fps = -1;
            if (c.mode == FullScreenMode.ExclusiveFullScreen)
            {
                var modes = Modes(c.mode, 0);
                if (!modes.Contains(new Vector2Int(c.width, c.height)))
                {
                    c.width = Screen.currentResolution.width; c.height = Screen.currentResolution.height;
                }
                var rates = Rates(c);
                if (!rates.Contains(c.hz)) c.hz = 0;
            }
            return c;
        }

        public static void Apply(Config c)
        {
            c = Validate(c);
            QualitySettings.vSyncCount = c.vsync;
            Application.targetFrameRate = c.fps;
            var refresh = new RefreshRate { numerator = (uint)c.hz, denominator = 1 };
            foreach (var r in Screen.resolutions)
                if (r.width == c.width && r.height == c.height && Mathf.RoundToInt((float)r.refreshRateRatio.value) == c.hz)
                { refresh = r.refreshRateRatio; break; }
            Screen.SetResolution(c.width, c.height, c.mode, refresh);
        }

        public static void Save(Config c)
        {
            c = Validate(c);
            PlayerPrefs.SetInt("display_width", c.width); PlayerPrefs.SetInt("display_height", c.height);
            PlayerPrefs.SetInt("display_hz", c.hz); PlayerPrefs.SetInt("display_mode", (int)c.mode);
            PlayerPrefs.SetInt("display_vsync", c.vsync); PlayerPrefs.SetInt("display_fps", c.fps);
            PlayerPrefs.SetInt(Key, 1); PlayerPrefs.Save();
        }

        // Windowed/borderless presets include QHD ultrawide. Exclusive modes come only from the monitor.
        public static List<Vector2Int> Modes(FullScreenMode mode, int aspect)
        {
            var modes = new List<Vector2Int>();
            void Add(int w, int h)
            {
                if (w < 640 || h < 360) return;
                float ratio = (float)w / h;
                if (aspect == 1 && Mathf.Abs(ratio - 16f / 9) > 0.04f || aspect == 2 && (ratio < 2.2f || ratio > 2.5f) ||
                    aspect == 3 && Mathf.Abs(ratio - 1.6f) > 0.04f) return;
                var v = new Vector2Int(w, h); if (!modes.Contains(v)) modes.Add(v);
            }
            foreach (var r in Screen.resolutions) Add(r.width, r.height);
            if (mode != FullScreenMode.ExclusiveFullScreen)
            {
                foreach (var r in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080),
                    new Vector2Int(2560, 1080), new Vector2Int(2560, 1440), new Vector2Int(3440, 1440), new Vector2Int(1920, 1200), new Vector2Int(2560, 1600) }) Add(r.x, r.y);
                Add(Screen.width, Screen.height); Add(Screen.currentResolution.width, Screen.currentResolution.height);
            }
            modes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return modes;
        }

        public static List<int> Rates(Config c)
        {
            var rates = new List<int> { 0 };
            if (c.mode != FullScreenMode.ExclusiveFullScreen) return rates;
            foreach (var r in Screen.resolutions)
                if (r.width == c.width && r.height == c.height)
                {
                    int hz = Mathf.RoundToInt((float)r.refreshRateRatio.value);
                    if (!rates.Contains(hz)) rates.Add(hz);
                }
            rates.Sort(); return rates;
        }

        public static string Aspect(int w, int h)
        {
            float r = (float)w / Mathf.Max(1, h);
            if (r > 2.2f && r < 2.5f) return "21:9 ULTRAWIDE";
            if (Mathf.Abs(r - 16f / 9) < 0.04f) return "16:9";
            if (Mathf.Abs(r - 1.6f) < 0.04f) return "16:10";
            return r.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1";
        }
    }
}

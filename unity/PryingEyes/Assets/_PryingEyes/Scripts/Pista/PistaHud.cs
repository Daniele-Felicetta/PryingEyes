using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes.Pista
{
    /// <summary>Everything drawn over the race, built by code like the rest of the interface: the loading screen, the speed in km/h with the gear and a bar of lights for the engine speed, the position, the lap and
    /// the times, the gap to the rider ahead and behind, the five lights of the start, the messages in the middle of the screen, the little map of the circuit with a dot for every rider (a friend in a
    /// cooperative game has the colour of their bike), and the table of the results. The map is painted once into a texture from the centre line of the circuit; only the dots move.</summary>
    public sealed class PistaHud
    {
        const int MapTex = 384;
        const float MapSize = 236f;
        internal static readonly Color Ink = new Color(0.96f, 0.95f, 0.91f), Dim = new Color(0.74f, 0.78f, 0.80f), Amber = new Color(1f, 0.74f, 0.18f), Red = new Color(0.96f, 0.24f, 0.18f), Green = new Color(0.38f, 0.88f, 0.46f);

        GameObject root, loadRoot;
        Canvas canvas, loadCanvas;
        // loading
        Image loadBar;
        Text loadTitle, loadStage, loadTrack;
        // race
        Text posText, lapText, timeText, bestText, lastText, aheadText, behindText, kmhText, unitText, gearText, gearLabel, bannerText, bannerSub, warnText, hintText, aidAbs, aidTc, trackText, speedMeta, statusText;
        Image statusBack;
        Image[] rpmSegs, draftSegs;
        Text draftLabel, styleLabel, deltaText, recordText, licenceGoals;
        RectTransform weightDot;
        Image[] lamps;
        RectTransform lampRow;
        Image mapBack;
        RawImage mapImg;
        RectTransform mapRect;
        readonly List<RectTransform> dots = new List<RectTransform>();
        readonly List<Image> dotImgs = new List<Image>();
        RectTransform meMark;
        float mapK, mapOx, mapOz;
        Texture2D mapTexture;
        // results
        RectTransform results;
        Text resultsTitle, resultsSub, resultsHint, licenceTitle;
        string resultsSubBase = "";
        Text[] cupLines;
        Text[] licenceLines;
        Text[][] rows;
        const int MaxRows = 16;
        // state
        float bannerT, bannerLen = 1f, hintT = 14f;
        int shownKmh = -1, shownGear = -99;
        string shownPos = "", shownLap = "";
        float flash;
        static Font face;
        static Sprite dotSprite, arrowSprite;
        public bool ResultsShown { get; private set; }
        /// <summary>The canvas of the race interface (the tests move it onto a camera to photograph it).</summary>
        public Canvas CanvasRef => canvas;

        static Font Face()
        {
            if (face == null) face = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial", "Liberation Sans" }, 48);
            return face;
        }

        // ------------------------------------------------------------------ small builders
        internal static RectTransform Node(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        internal static Text Label(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int fs, TextAnchor align, Color col, string text = "", FontStyle style = FontStyle.Bold, bool shadow = true, bool rich = false)
        {
            var rt = Node(parent, name, anchor, pivot, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Face(); t.fontSize = fs; t.alignment = align; t.color = col; t.fontStyle = style; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = rich;
            if (shadow)
            {
                var ol = rt.gameObject.AddComponent<Outline>();
                ol.effectColor = new Color(0f, 0f, 0f, 0.55f); ol.effectDistance = new Vector2(1.3f, -1.3f);
                var sh = rt.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.5f); sh.effectDistance = new Vector2(2.2f, -2.2f);
            }
            return t;
        }

        internal static Image Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color col, Sprite sprite = null)
        {
            var rt = Node(parent, name, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = col; img.raycastTarget = false;
            return img;
        }

        static Sprite Dot()
        {
            if (dotSprite != null) return dotSprite;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "PistaPunto", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - N * 0.5f) * (x + 0.5f - N * 0.5f) + (y + 0.5f - N * 0.5f) * (y + 0.5f - N * 0.5f));
                    float a = Mathf.Clamp01(N * 0.5f - 1f - r + 0.5f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return dotSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>A triangle pointing up with a notch at the back: the mark of the rider whose map this is.</summary>
        static Sprite Arrow()
        {
            if (arrowSprite != null) return arrowSprite;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "PistaFreccia", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;           // -1..1, up is +v
                    // the arrow: tip at (0,0.92), wings at (+-0.62,-0.80), a wedge cut out of the back from (0,-0.40)
                    float a = 0f;
                    if (v > -0.80f && v < 0.92f)
                    {
                        float half = 0.62f * (0.92f - v) / 1.72f;
                        float d = Mathf.Abs(u);
                        float notch = v < -0.40f ? 0.5f * (-0.40f - v) : 0f;
                        a = Mathf.Clamp01((half - d) * N * 0.5f + 0.5f) * (notch > 0f ? Mathf.Clamp01((d - notch) * N * 0.5f + 0.5f) : 1f);
                    }
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return arrowSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        internal static Canvas MakeCanvas(string name, int order, out GameObject go)
        {
            go = new GameObject(name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1280, 720); s.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return c;
        }

        // ------------------------------------------------------------------ construction
        public PistaHud()
        {
            canvas = MakeCanvas("PistaHud", 30, out root);
            loadCanvas = MakeCanvas("PistaCaricamento", 140, out loadRoot);
            BuildLoading();
            BuildRace();
            BuildResults();
            root.SetActive(false);
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            if (loadRoot != null) Object.Destroy(loadRoot);
            if (mapTexture != null) Object.Destroy(mapTexture);
        }

        void BuildLoading()
        {
            var rt = (RectTransform)loadRoot.transform;
            var back = Box(rt, "Fondo", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 3000f), new Color(0.02f, 0.025f, 0.03f, 1f));
            back.rectTransform.anchorMin = Vector2.zero; back.rectTransform.anchorMax = Vector2.one; back.rectTransform.offsetMin = back.rectTransform.offsetMax = Vector2.zero;
            loadTitle = Label(rt, "Titolo", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(1100f, 90f), 64, TextAnchor.MiddleCenter, Ink, "MOTO SU PISTA", FontStyle.Bold);
            loadTrack = Label(rt, "Tracciato", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(1100f, 40f), 26, TextAnchor.MiddleCenter, Amber, "", FontStyle.Normal);
            Box(rt, "BarraFondo", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -56f), new Vector2(460f, 6f), new Color(1f, 1f, 1f, 0.12f));
            loadBar = Box(rt, "Barra", new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-230f, -56f), new Vector2(2f, 6f), Amber);
            loadStage = Label(rt, "Fase", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -92f), new Vector2(900f, 30f), 20, TextAnchor.MiddleCenter, Dim, "", FontStyle.Normal);
        }

        void BuildRace()
        {
            var rt = (RectTransform)root.transform;
            var tl = new Vector2(0f, 1f); var tr = new Vector2(1f, 1f); var br = new Vector2(1f, 0f); var bl = new Vector2(0f, 0f); var tc = new Vector2(0.5f, 1f); var cc = new Vector2(0.5f, 0.5f);

            // top left: position, lap and times
            Box(rt, "FondoSinistra", tl, tl, new Vector2(12f, -8f), new Vector2(330f, 290f), new Color(0.02f, 0.03f, 0.04f, 0.34f));
            posText = Label(rt, "Posizione", tl, tl, new Vector2(26f, -14f), new Vector2(260f, 90f), 78, TextAnchor.UpperLeft, Ink, "", FontStyle.Bold, true, true);
            lapText = Label(rt, "Giro", tl, tl, new Vector2(28f, -108f), new Vector2(360f, 34f), 26, TextAnchor.UpperLeft, Amber, "", FontStyle.Bold);
            timeText = Label(rt, "Tempo", tl, tl, new Vector2(28f, -140f), new Vector2(360f, 50f), 42, TextAnchor.UpperLeft, Ink, "0:00.000", FontStyle.Bold);
            bestText = Label(rt, "Migliore", tl, tl, new Vector2(30f, -192f), new Vector2(360f, 26f), 20, TextAnchor.UpperLeft, Dim, "", FontStyle.Normal);
            lastText = Label(rt, "Ultimo", tl, tl, new Vector2(30f, -216f), new Vector2(360f, 26f), 20, TextAnchor.UpperLeft, Dim, "", FontStyle.Normal);
            recordText = Label(rt, "Record", tl, tl, new Vector2(30f, -240f), new Vector2(360f, 26f), 20, TextAnchor.UpperLeft, new Color(0.78f, 0.52f, 1f), "", FontStyle.Normal);
            licenceGoals = Label(rt, "Medaglie", tl, tl, new Vector2(30f, -264f), new Vector2(360f, 26f), 20, TextAnchor.UpperLeft, Dim, "", FontStyle.Bold, true, true);
            deltaText = Label(rt, "Scarto", tl, tl, new Vector2(232f, -146f), new Vector2(110f, 40f), 32, TextAnchor.UpperLeft, Ink, "", FontStyle.Bold);

            // top centre: the lights of the start and the gaps
            lampRow = Node(rt, "Semaforo", tc, tc, new Vector2(0f, -22f), new Vector2(400f, 70f));
            lamps = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                Box(lampRow, "LuceFondo" + i, cc, cc, new Vector2((i - 2) * 76f, 0f), new Vector2(62f, 62f), new Color(0.03f, 0.03f, 0.035f, 0.85f), Dot());
                lamps[i] = Box(lampRow, "Luce" + i, cc, cc, new Vector2((i - 2) * 76f, 0f), new Vector2(50f, 50f), new Color(0.22f, 0.04f, 0.04f, 1f), Dot());
            }
            lampRow.gameObject.SetActive(false);
            aheadText = Label(rt, "Davanti", tc, tc, new Vector2(0f, -24f), new Vector2(500f, 30f), 24, TextAnchor.UpperCenter, Red, "", FontStyle.Bold);
            behindText = Label(rt, "Dietro", tc, tc, new Vector2(0f, -54f), new Vector2(500f, 30f), 24, TextAnchor.UpperCenter, Green, "", FontStyle.Bold);

            // top right: the map
            mapRect = Node(rt, "Mappa", tr, tr, new Vector2(-22f, -22f), new Vector2(MapSize, MapSize));
            mapBack = Box(mapRect, "Fondo", cc, cc, Vector2.zero, new Vector2(MapSize, MapSize), new Color(0.02f, 0.03f, 0.035f, 0.56f));
            var imgNode = Node(mapRect, "Tracciato", cc, cc, Vector2.zero, new Vector2(MapSize, MapSize));
            mapImg = imgNode.gameObject.AddComponent<RawImage>(); mapImg.raycastTarget = false; mapImg.color = Color.white;
            trackText = Label(rt, "NomePista", tr, tr, new Vector2(-24f, -262f), new Vector2(MapSize + 80f, 24f), 16, TextAnchor.UpperRight, Dim, "", FontStyle.Normal);

            // bottom right: speed, gear, engine speed
            var cluster = Node(rt, "Cruscotto", br, br, new Vector2(-30f, 26f), new Vector2(420f, 170f));
            Box(cluster, "Fondo", br, br, new Vector2(16f, -12f), new Vector2(452f, 192f), new Color(0.02f, 0.03f, 0.04f, 0.34f));
            rpmSegs = new Image[24];
            for (int i = 0; i < rpmSegs.Length; i++)
                rpmSegs[i] = Box(cluster, "Giri" + i, br, new Vector2(0f, 0f), new Vector2(-420f + i * 17.5f, 126f), new Vector2(14f, 13f), new Color(1f, 1f, 1f, 0.08f));
            kmhText = Label(cluster, "Kmh", br, br, new Vector2(-128f, 36f), new Vector2(240f, 104f), 96, TextAnchor.LowerRight, Ink, "0", FontStyle.Bold);
            unitText = Label(cluster, "Unita", br, br, new Vector2(-128f, 10f), new Vector2(240f, 24f), 20, TextAnchor.LowerRight, Dim, "km/h", FontStyle.Normal);
            Box(cluster, "MarciaFondo", br, br, new Vector2(0f, 12f), new Vector2(100f, 106f), new Color(0.02f, 0.03f, 0.035f, 0.55f));
            gearText = Label(cluster, "Marcia", br, br, new Vector2(-8f, 14f), new Vector2(84f, 96f), 78, TextAnchor.MiddleCenter, Amber, "1", FontStyle.Bold);
            gearLabel = Label(cluster, "MarciaEtichetta", br, br, new Vector2(-8f, 94f), new Vector2(84f, 20f), 13, TextAnchor.MiddleCenter, Dim, "MARCIA", FontStyle.Normal);
            speedMeta = Label(cluster, "Meta", br, br, new Vector2(-128f, 0f), new Vector2(240f, 14f), 11, TextAnchor.LowerRight, Dim, "", FontStyle.Normal);

            // over the engine-speed bar: the slipstream (six little bars that fill as the rider ahead shelters more of the wind)
            draftLabel = Label(cluster, "ScieEtichetta", br, new Vector2(0f, 0f), new Vector2(-420f, 142f), new Vector2(60f, 20f), 15, TextAnchor.LowerLeft, new Color(Dim.r, Dim.g, Dim.b, 0.30f), "SCIA", FontStyle.Bold);
            draftSegs = new Image[6];
            for (int i = 0; i < draftSegs.Length; i++)
                draftSegs[i] = Box(cluster, "Scia" + i, br, new Vector2(0f, 0f), new Vector2(-366f + i * 17.5f, 146f), new Vector2(14f, 7f), new Color(1f, 1f, 1f, 0.08f));

            // left of the dashboard: where the rider's weight is (across = into the corner, up = forward over the tank) and how he sits
            Box(rt, "PesoFondo", br, br, new Vector2(-494f, 30f), new Vector2(86f, 86f), new Color(0.02f, 0.03f, 0.04f, 0.34f));
            Box(rt, "PesoAsseX", br, new Vector2(0.5f, 0.5f), new Vector2(-537f, 73f), new Vector2(76f, 1.5f), new Color(1f, 1f, 1f, 0.16f));
            Box(rt, "PesoAsseY", br, new Vector2(0.5f, 0.5f), new Vector2(-537f, 73f), new Vector2(1.5f, 76f), new Color(1f, 1f, 1f, 0.16f));
            weightDot = Box(rt, "PesoPunto", br, new Vector2(0.5f, 0.5f), new Vector2(-537f, 73f), new Vector2(13f, 13f), Amber, Dot()).rectTransform;
            styleLabel = Label(rt, "PesoStile", br, br, new Vector2(-494f, 120f), new Vector2(160f, 20f), 14, TextAnchor.LowerRight, new Color(Dim.r, Dim.g, Dim.b, 0.8f), "", FontStyle.Bold);

            // bottom left: the rider aids and the hints
            aidAbs = Label(rt, "ABS", bl, bl, new Vector2(30f, 26f), new Vector2(70f, 30f), 22, TextAnchor.LowerLeft, new Color(Dim.r, Dim.g, Dim.b, 0.28f), "ABS");
            aidTc = Label(rt, "TC", bl, bl, new Vector2(100f, 26f), new Vector2(70f, 30f), 22, TextAnchor.LowerLeft, new Color(Dim.r, Dim.g, Dim.b, 0.28f), "TC");
            hintText = Label(rt, "Comandi", bl, bl, new Vector2(30f, 66f), new Vector2(640f, 110f), 17, TextAnchor.LowerLeft, Ink, "", FontStyle.Normal);
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // the middle: banners and warnings
            bannerText = Label(rt, "Annuncio", cc, cc, new Vector2(0f, 130f), new Vector2(1200f, 96f), 72, TextAnchor.MiddleCenter, Ink, "", FontStyle.Bold);
            bannerSub = Label(rt, "AnnuncioSotto", cc, cc, new Vector2(0f, 72f), new Vector2(1200f, 40f), 28, TextAnchor.MiddleCenter, Dim, "", FontStyle.Normal);
            warnText = Label(rt, "Avviso", cc, cc, new Vector2(0f, -150f), new Vector2(1000f, 70f), 52, TextAnchor.MiddleCenter, Red, "", FontStyle.Bold);

            // the bottom middle: what the race is waiting for (the host to give the go, a friend who is still loading)
            var bc = new Vector2(0.5f, 0f);
            statusBack = Box(rt, "StatoFondo", bc, bc, new Vector2(0f, 150f), new Vector2(760f, 46f), new Color(0.02f, 0.03f, 0.04f, 0.5f));
            statusText = Label(rt, "Stato", bc, bc, new Vector2(0f, 156f), new Vector2(740f, 34f), 24, TextAnchor.MiddleCenter, Amber, "", FontStyle.Bold);
            statusBack.gameObject.SetActive(false);
        }

        /// <summary>A line at the bottom of the screen for what the race is waiting for; empty hides it.</summary>
        public void SetStatus(string text)
        {
            if (statusText == null) return;
            bool on = !string.IsNullOrEmpty(text);
            statusText.text = on ? text : "";
            statusBack.gameObject.SetActive(on);
        }

        void BuildResults()
        {
            var rt = (RectTransform)root.transform;
            var cc = new Vector2(0.5f, 0.5f);
            results = Node(rt, "Classifica", cc, cc, Vector2.zero, new Vector2(860f, 640f));
            var back = Box(results, "Fondo", cc, cc, Vector2.zero, new Vector2(860f, 640f), new Color(0.015f, 0.02f, 0.025f, 0.84f));
            Box(results, "Filetto", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(860f, 5f), Amber);
            resultsTitle = Label(results, "Titolo", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(800f, 60f), 46, TextAnchor.UpperCenter, Ink, "ARRIVO", FontStyle.Bold);
            resultsSub = Label(results, "Sotto", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(800f, 30f), 20, TextAnchor.UpperCenter, Dim, "", FontStyle.Normal);
            rows = new Text[MaxRows][];
            float[] xs = { 36f, 90f, 520f, 700f };
            var tl = new Vector2(0f, 1f);
            Label(results, "TestaPos", tl, tl, new Vector2(xs[0], -116f), new Vector2(60f, 22f), 14, TextAnchor.UpperLeft, Dim, "POS", FontStyle.Bold, false);
            Label(results, "TestaNome", tl, tl, new Vector2(xs[1], -116f), new Vector2(200f, 22f), 14, TextAnchor.UpperLeft, Dim, "PILOTA", FontStyle.Bold, false);
            Label(results, "TestaTempo", tl, tl, new Vector2(xs[2], -116f), new Vector2(170f, 22f), 14, TextAnchor.UpperLeft, Dim, "TEMPO", FontStyle.Bold, false);
            Label(results, "TestaGiro", tl, tl, new Vector2(xs[3], -116f), new Vector2(150f, 22f), 14, TextAnchor.UpperLeft, Dim, "MIGLIOR GIRO", FontStyle.Bold, false);
            for (int i = 0; i < MaxRows; i++)
            {
                rows[i] = new Text[4];
                float y = -142f - i * 27f;
                rows[i][0] = Label(results, "Pos" + i, tl, tl, new Vector2(xs[0], y), new Vector2(50f, 26f), 21, TextAnchor.UpperLeft, Ink, "", FontStyle.Bold, false);
                rows[i][1] = Label(results, "Nome" + i, tl, tl, new Vector2(xs[1], y), new Vector2(420f, 26f), 21, TextAnchor.UpperLeft, Ink, "", FontStyle.Bold, false);
                rows[i][2] = Label(results, "Tempo" + i, tl, tl, new Vector2(xs[2], y), new Vector2(170f, 26f), 21, TextAnchor.UpperLeft, Ink, "", FontStyle.Normal, false);
                rows[i][3] = Label(results, "Giro" + i, tl, tl, new Vector2(xs[3], y), new Vector2(150f, 26f), 21, TextAnchor.UpperLeft, Dim, "", FontStyle.Normal, false);
            }
            resultsHint = Label(results, "Comandi", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(820f, 30f), 19, TextAnchor.LowerCenter, Amber, "", FontStyle.Bold, false);
            // the verdict of a licence test, under the table: the medal in big letters and four lines (hidden in an ordinary race)
            licenceTitle = Label(results, "PatenteMedaglia", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -226f), new Vector2(800f, 70f), 56, TextAnchor.UpperCenter, Amber, "", FontStyle.Bold);
            licenceLines = new Text[4];
            for (int i = 0; i < licenceLines.Length; i++)
            {
                licenceLines[i] = Label(results, "PatenteRiga" + i, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -304f - i * 34f), new Vector2(820f, 30f), i == 0 ? 24 : 20, TextAnchor.UpperCenter,
                    i == 0 ? Ink : i == 3 ? Amber : Dim, "", i == 0 ? FontStyle.Bold : FontStyle.Normal, false);
                licenceLines[i].gameObject.SetActive(false);
            }
            licenceTitle.gameObject.SetActive(false);
            // the championship, under the table of a round (twelve riders end at -466): the cup and the round, what the round gave, the leaders, and what comes next (hidden in any other race)
            cupLines = new Text[4];
            for (int i = 0; i < cupLines.Length; i++)
            {
                cupLines[i] = Label(results, "CampionatoRiga" + i, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -474f - i * 29f), new Vector2(820f, 28f), i == 0 ? 23 : 19, TextAnchor.UpperCenter,
                    i == 0 ? Ink : i == 3 ? Amber : Dim, "", i == 0 ? FontStyle.Bold : FontStyle.Normal, false);
                cupLines[i].gameObject.SetActive(false);
            }
            results.gameObject.SetActive(false);
        }

        /// <summary>The championship under the table of a round: up to four lines (null or empty hides them).</summary>
        public void SetCup(string[] lines)
        {
            if (cupLines == null) return;
            for (int i = 0; i < cupLines.Length; i++)
            {
                if (cupLines[i] == null) continue;            // the labels are gone when the game is being closed (the director hides the table from OnDestroy)
                bool has = lines != null && i < lines.Length && !string.IsNullOrEmpty(lines[i]);
                cupLines[i].gameObject.SetActive(has);
                cupLines[i].text = has ? lines[i] : "";
            }
        }

        /// <summary>For the tests: the lines of the championship that are showing, one per line.</summary>
        public string CupLinesText
        {
            get
            {
                if (cupLines == null) return "";
                var sb = new System.Text.StringBuilder();
                foreach (var t in cupLines) if (t != null && t.gameObject.activeSelf) { if (sb.Length > 0) sb.Append('\n'); sb.Append(t.text); }
                return sb.ToString();
            }
        }

        /// <summary>The verdict of a licence test under the table of results: the medal (an empty title hides all of it) and up to four lines.</summary>
        public void SetLicence(string title, Color color, params string[] lines)
        {
            if (licenceTitle == null) return;
            bool on = !string.IsNullOrEmpty(title);
            licenceTitle.gameObject.SetActive(on);
            licenceTitle.text = on ? title : ""; licenceTitle.color = color;
            for (int i = 0; i < licenceLines.Length; i++)
            {
                bool has = on && lines != null && i < lines.Length && !string.IsNullOrEmpty(lines[i]);
                licenceLines[i].gameObject.SetActive(has);
                licenceLines[i].text = has ? lines[i] : "";
            }
        }

        /// <summary>For the tests: what the medal of the verdict and the title of the table of results say (the medal is empty when the verdict is hidden).</summary>
        public string LicenceTitleText { get { return licenceTitle != null && licenceTitle.gameObject.activeSelf ? licenceTitle.text : ""; } }
        public string ResultsTitleText { get { return resultsTitle != null ? resultsTitle.text : ""; } }
        public string ResultsHintText { get { return resultsHint != null ? resultsHint.text : ""; } }

        /// <summary>The big line of the table of results ("HAI VINTO", "ARRIVO 3°"), replaced when the race is not a race (a licence test).</summary>
        public void SetResultsTitle(string text)
        {
            if (resultsTitle == null) return;
            resultsTitle.text = text; resultsTitle.color = Ink;
        }

        // ------------------------------------------------------------------ the loading screen
        public void SetLoading(float progress, string stage)
        {
            if (loadRoot == null) return;
            if (!loadRoot.activeSelf) loadRoot.SetActive(true);
            loadBar.rectTransform.sizeDelta = new Vector2(Mathf.Max(2f, 460f * Mathf.Clamp01(progress)), 6f);
            loadStage.text = stage;
        }

        public void SetLoadingTrack(string text) { if (loadTrack != null) loadTrack.text = text; }

        public void HideLoading() { if (loadRoot != null) loadRoot.SetActive(false); root.SetActive(true); }

        public bool Loading => loadRoot != null && loadRoot.activeSelf;

        // ------------------------------------------------------------------ the map
        /// <summary>Paint the circuit into the map texture (once) and size the dots for 'count' riders.</summary>
        public void BuildMap(Circuit c, int count, string trackName)
        {
            trackText.text = trackName;
            var fit = PistaMap.FitFor(c, MapTex, 16f);
            mapK = fit.k; mapOx = fit.ox; mapOz = fit.oz;
            if (mapTexture != null) Object.Destroy(mapTexture);
            mapTexture = PistaMap.Paint(c, fit, 2.4f, "PistaMappa");
            mapImg.texture = mapTexture;

            foreach (var d in dots) if (d != null) Object.Destroy(d.gameObject);
            dots.Clear(); dotImgs.Clear();
            if (meMark != null) Object.Destroy(meMark.gameObject);
            var cc = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < count; i++) AddDot();
            // the mark of the local rider: an arrow with a dark rim, last so it draws on top
            var rim = Box(mapRect, "IoOrlo", new Vector2(0f, 0f), cc, Vector2.zero, new Vector2(22f, 22f), new Color(0f, 0f, 0f, 0.85f), Arrow());
            var arrow = Box(rim.rectTransform, "Io", cc, cc, Vector2.zero, new Vector2(16f, 16f), Color.white, Arrow());
            meMark = rim.rectTransform;
            meArrow = arrow;
        }

        Image meArrow;

        void AddDot()
        {
            var img = Box(mapRect, "Pilota" + dots.Count, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9f, 9f), Color.white, Dot());
            var ring = img.gameObject.AddComponent<Outline>();           // a friend has a white edge (see UpdateMap): the others are the computer's
            ring.effectColor = new Color(1f, 1f, 1f, 0.95f); ring.effectDistance = new Vector2(1.5f, -1.5f); ring.enabled = false;
            dots.Add(img.rectTransform); dotImgs.Add(img);
            if (meMark != null) meMark.SetAsLastSibling();
        }

        /// <summary>Move the dots: a rider is a coloured dot; the local one an arrow pointing the way it goes.</summary>
        void UpdateMap(Race race, Racer me)
        {
            float f = MapSize / MapTex;
            while (dots.Count < race.racers.Count) AddDot();             // a friend who joined after the map was made
            for (int i = 0; i < dots.Count; i++)
            {
                if (i >= race.racers.Count) { dots[i].gameObject.SetActive(false); continue; }
                var r = race.racers[i];
                bool mine = r == me;
                dots[i].gameObject.SetActive(!mine);
                if (mine) continue;
                dots[i].gameObject.SetActive(!r.dnf);
                dots[i].anchoredPosition = new Vector2((r.x * mapK + mapOx) * f, (r.z * mapK + mapOz) * f);
                var col = Livery.For(r.skin).main;
                dotImgs[i].color = r.crashed ? new Color(0.5f, 0.5f, 0.5f, 0.8f) : col;
                dots[i].sizeDelta = r.friend ? new Vector2(13f, 13f) : new Vector2(9f, 9f);
                var edge = dotImgs[i].GetComponent<Outline>();
                if (edge != null && edge.enabled != r.friend) edge.enabled = r.friend;
            }
            if (me != null && meMark != null)
            {
                meMark.anchoredPosition = new Vector2((me.x * mapK + mapOx) * f, (me.z * mapK + mapOz) * f);
                meMark.localRotation = Quaternion.Euler(0f, 0f, -me.heading * Mathf.Rad2Deg);
                meArrow.color = Livery.For(me.skin).main.maxColorComponent < 0.3f ? Color.white : Color.Lerp(Livery.For(me.skin).main, Color.white, 0.35f);
            }
        }

        // ------------------------------------------------------------------ messages
        public void Banner(string title, string sub, float seconds, Color? color = null)
        {
            bannerText.text = title; bannerSub.text = sub ?? "";
            bannerText.color = color ?? Ink;
            bannerT = bannerLen = Mathf.Max(0.2f, seconds);
        }

        public void SetHints(string text, float seconds) { hintText.text = text; hintT = seconds; }

        /// <summary>How far ahead (green, minus) or behind (red, plus) the lap of reference the rider is, in seconds; nothing when there is no reference.</summary>
        public void SetDelta(bool valid, float delta)
        {
            if (deltaText == null) return;
            if (!valid) { deltaText.text = ""; return; }
            deltaText.text = (delta <= 0f ? "-" : "+") + Mathf.Abs(delta).ToString("0.00");
            deltaText.color = delta <= 0f ? Green : Red;
        }

        /// <summary>The line under the times about the record the ghost is the picture of; empty hides it.</summary>
        public void SetRecord(string text) { if (recordText != null) recordText.text = text ?? ""; }

        /// <summary>A licence test: the three times to beat under the record, each in the colour of its medal (rich text; empty hides the line).</summary>
        public void SetLicenceGoals(string richText) { if (licenceGoals != null) licenceGoals.text = richText ?? ""; }
        public string LicenceGoalsText { get { return licenceGoals != null ? licenceGoals.text : ""; } }

        public static string Clock(float s)
        {
            if (s < 0f) return "-:--.---";
            int ms = Mathf.RoundToInt(s * 1000f);
            return (ms / 60000) + ":" + ((ms / 1000) % 60).ToString("00") + "." + (ms % 1000).ToString("000");
        }

        static string Gap(float g) { return g < 0f ? "" : (g >= 60f ? Mathf.FloorToInt(g / 60f) + ":" + (g % 60f).ToString("00.0") : g.ToString("0.00")); }

        static string Ordinal(int p) { return p + "°"; }

        // ------------------------------------------------------------------ every frame
        public void Tick(float dt, Race race, Racer me, in BikeView v, bool pad, bool lampsShown)
        {
            if (!root.activeSelf) return;
            flash += dt;
            int total = race.racers.Count;
            bool racing = race.phase == RacePhase.Racing || race.phase == RacePhase.Over;
            // position and lap
            string pos = "<size=78>" + me.position + "</size><size=34>/" + total + "</size>";
            if (pos != shownPos) { shownPos = pos; posText.text = pos; }
            string lap = "GIRO " + me.CurrentLap(race.laps) + "/" + race.laps;
            if (lap != shownLap) { shownLap = lap; lapText.text = lap; }
            float cur = me.finished ? me.finishTime : racing ? race.time - me.lapStart : 0f;
            timeText.text = me.finished ? Clock(me.finishTime) : Clock(Mathf.Max(0f, cur));
            bestText.text = me.bestLap >= 0f ? "MIGLIORE  " + Clock(me.bestLap) : "";
            lastText.text = me.lastLap >= 0f ? "ULTIMO  " + Clock(me.lastLap) : "";

            // gaps
            string ahead = "", behind = "";
            if (racing && !me.finished && race.phase == RacePhase.Racing)
            {
                if (me.position > 1)
                {
                    var a = race.standings[me.position - 2];
                    float g = race.GapToAhead(me);
                    if (g >= 0f) ahead = a.name + "   +" + Gap(g);
                }
                if (me.position < race.standings.Count)
                {
                    var b = race.standings[me.position];
                    float g = race.GapToAhead(b);
                    if (g >= 0f && !b.finished) behind = b.name + "   -" + Gap(g);
                }
            }
            aheadText.text = lampsShown ? "" : ahead; behindText.text = lampsShown ? "" : behind;
            if (lampsShown) { aheadText.text = ""; behindText.text = ""; }
            // the lights
            lampRow.gameObject.SetActive(lampsShown);
            if (lampsShown)
            {
                bool go = race.phase == RacePhase.Racing;
                for (int i = 0; i < 5; i++)
                {
                    bool on = go || i < race.lightsOn;
                    lamps[i].color = on ? (go ? new Color(0.30f, 1f, 0.45f) : new Color(1f, 0.12f, 0.08f)) : new Color(0.20f, 0.035f, 0.035f);
                }
            }

            // speed, gear, engine speed
            int kmh = Mathf.RoundToInt(Mathf.Abs(v.speed) * 3.6f);
            if (kmh != shownKmh) { shownKmh = kmh; kmhText.text = kmh.ToString(); }
            int gear = v.crashed ? -1 : v.gear;
            if (gear != shownGear) { shownGear = gear; gearText.text = gear < 0 ? "-" : gear == 0 ? "N" : gear.ToString(); }
            gearText.color = v.shifting ? Dim : Amber;
            var spec = me.bike != null ? me.bike.spec : null;
            float idle = spec != null ? spec.idleRpm : 1300f, red = spec != null ? spec.redlineRpm : 14000f, lim = spec != null ? spec.limiterRpm : 14500f;
            float k01 = Mathf.Clamp01((v.rpm - idle) / (lim - idle));
            int lit = Mathf.RoundToInt(k01 * rpmSegs.Length);
            bool blink = v.limiter && ((int)(flash * 14f) & 1) == 0;
            for (int i = 0; i < rpmSegs.Length; i++)
            {
                float segRpm = idle + (lim - idle) * (i + 1f) / rpmSegs.Length;
                bool on = i < lit;
                Color col = segRpm > red ? Red : segRpm > red * 0.88f ? Amber : new Color(0.92f, 0.94f, 0.95f);
                if (blink) { on = true; col = Red; }
                rpmSegs[i].color = on ? col : new Color(1f, 1f, 1f, 0.09f);
            }
            // the slipstream and the weight
            float dr = Mathf.Clamp01(me.draft);
            int litD = Mathf.CeilToInt(dr * draftSegs.Length - 0.001f);
            Color sc = new Color(0.55f, 0.82f, 1f);
            for (int i = 0; i < draftSegs.Length; i++) draftSegs[i].color = i < litD ? sc : new Color(1f, 1f, 1f, 0.08f);
            draftLabel.color = litD > 0 ? sc : new Color(Dim.r, Dim.g, Dim.b, 0.30f);
            weightDot.anchoredPosition = new Vector2(-537f + Mathf.Clamp(v.hang, -1f, 1f) * 34f, 73f + Mathf.Clamp(v.body, -1f, 1f) * 34f);
            if (me.bike != null) styleLabel.text = PistaMode.StyleNames[(int)me.bike.style].ToUpperInvariant();
            aidAbs.color = v.absOn ? Amber : new Color(Dim.r, Dim.g, Dim.b, 0.28f);
            aidTc.color = v.tcsOn ? Amber : new Color(Dim.r, Dim.g, Dim.b, 0.28f);

            // messages
            if (bannerT > 0f)
            {
                bannerT -= dt;
                float a = Mathf.Clamp01(bannerT / 0.5f) * Mathf.Clamp01((bannerLen - bannerT) / 0.12f);
                float pop = 1f + 0.22f * Mathf.Clamp01(1f - (bannerLen - bannerT) / 0.25f);
                var c = bannerText.color; c.a = a; bannerText.color = c;
                var s = bannerSub.color; s.a = a * 0.9f; bannerSub.color = s;
                bannerText.rectTransform.localScale = Vector3.one * pop;
                if (bannerT <= 0f) { bannerText.text = ""; bannerSub.text = ""; }
            }
            string warn = "";
            if (me.wrongWay && race.phase == RacePhase.Racing) warn = "CONTROMANO";
            else if (v.crashed) warn = "CADUTO";
            if (warn.Length > 0)
            {
                warnText.text = warn;
                var wc = warnText.color; wc.a = ((int)(flash * 3f) & 1) == 0 || v.crashed ? 1f : 0.35f; warnText.color = wc;
            }
            else warnText.text = "";
            if (hintT > 0f)
            {
                hintT -= dt;
                var hc = hintText.color; hc.a = Mathf.Clamp01(hintT / 1.5f) * 0.9f; hintText.color = hc;
                if (hintT <= 0f) hintText.text = "";
            }
            UpdateMap(race, me);
        }

        // ------------------------------------------------------------------ results
        public void ShowResults(Race race, Racer me, string trackName, string hint)
        {
            ResultsShown = true;
            results.gameObject.SetActive(true);
            mapRect.gameObject.SetActive(false); trackText.gameObject.SetActive(false);
            bannerT = 0f; bannerText.text = ""; bannerSub.text = ""; warnText.text = ""; hintText.text = "";
            bool won = me.finished && me.position == 1;
            resultsTitle.text = !me.finished ? "GARA FINITA" : won ? "HAI VINTO" : "ARRIVO  " + Ordinal(me.position);
            resultsTitle.color = won ? Amber : Ink;
            resultsSubBase = trackName + "   -   " + race.laps + (race.laps == 1 ? " giro" : " giri");
            resultsSub.text = resultsSubBase;
            resultsHint.text = hint;
            RefreshResults(race, me);
        }

        public void RefreshResults(Race race, Racer me)
        {
            float winner = -1f;
            for (int i = 0; i < race.standings.Count; i++) if (race.standings[i].finished) { winner = race.standings[i].finishTime; break; }
            for (int i = 0; i < MaxRows; i++)
            {
                bool has = i < race.standings.Count;
                for (int c = 0; c < 4; c++) rows[i][c].text = "";
                if (!has) continue;
                var r = race.standings[i];
                bool mine = r == me;
                Color col = mine ? Amber : Ink;
                for (int c = 0; c < 3; c++) rows[i][c].color = col;
                rows[i][0].text = (i + 1).ToString();
                rows[i][1].text = r.name;
                if (r.dnf) rows[i][2].text = "ritirato";
                else if (r.finished) rows[i][2].text = i == 0 || winner < 0f ? Clock(r.finishTime) : "+" + Gap(r.finishTime - winner);
                else rows[i][2].text = "giro " + r.CurrentLap(race.laps);
                rows[i][3].text = r.bestLap >= 0f ? Clock(r.bestLap) : "";
            }
        }

        /// <summary>What the race paid (the career), at the end of the line under the title of the table: "premio 660 cr". Empty takes it away.</summary>
        public void SetResultsNote(string note)
        {
            if (resultsSub == null) return;
            resultsSub.text = resultsSubBase + (string.IsNullOrEmpty(note) ? "" : "   -   " + note);
        }

        /// <summary>For the tests: the line under the title of the table of results, with the prize if there is one.</summary>
        public string ResultsSubText { get { return resultsSub != null ? resultsSub.text : ""; } }

        public void HideResults()
        {
            ResultsShown = false;
            SetLicence(null, Color.white);
            SetCup(null);
            if (results != null) results.gameObject.SetActive(false);
            if (mapRect != null) mapRect.gameObject.SetActive(true);
            if (trackText != null) trackText.gameObject.SetActive(true);
        }

        public void SetVisible(bool on) { if (root != null) root.SetActive(on && !Loading); }
    }
}

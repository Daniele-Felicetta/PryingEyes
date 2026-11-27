using System.Collections.Generic;
using PryingEyes.Deserto;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Desert Select menu. Each seat opens its own overlay with M, Tab or a tap of Back.
    /// Pages share keyboard, mouse and controller navigation. The world keeps running while the overlay is open.</summary>
    public partial class HUD
    {
        // ------------------------------------------------------------------ misure e colori (il pannello e' disegnato a 1060 x 610 e si rimpicciolisce per starci nel posto)
        const float HubW = 1060f, HubH = 610f, HubNavH = 54f, HubFootH = 34f, HubPad = 16f;
        const float HubContentY = HubNavH + 10f, HubContentH = HubH - HubNavH - HubFootH - 20f, HubContentW = HubW - 2f * HubPad;
        static readonly Color HubBoard = new Color(0.075f, 0.058f, 0.045f, 0.975f), HubBar = new Color(0.115f, 0.088f, 0.066f, 1f), HubCard = new Color(0.135f, 0.105f, 0.08f, 1f),
            HubCardHi = new Color(0.20f, 0.155f, 0.105f, 1f), HubBrassC = new Color(0.76f, 0.60f, 0.30f, 1f), HubInkC = new Color(0.93f, 0.86f, 0.70f, 1f),
            HubDimC = new Color(0.93f, 0.86f, 0.70f, 0.55f), HubRedC = new Color(0.90f, 0.30f, 0.22f, 1f), HubGreenC = new Color(0.52f, 0.82f, 0.42f, 1f), HubTealC = new Color(0.30f, 0.80f, 0.82f, 1f);

        static int hubClosedFrame = -1;
        /// <summary>Il menu di Select si e' chiuso in questo fotogramma: l'Esc che l'ha chiuso non deve aprire anche la pausa (GameMenu).</summary>
        public static bool HubClosedThisFrame => hubClosedFrame == Time.frameCount;

        /// <summary>Quello che chi guarda il menu ha fatto in questo fotogramma, uguale per tastiera, mouse e controller.</summary>
        public struct HubInput
        {
            /// <summary>Un passo nella direzione (+1 destra o su, -1 sinistra o giu'): il primo scatta subito, poi si ripete tenendo premuto.</summary>
            public int nx, ny;
            public bool confirm, back, prev, next;
            /// <summary>Il mouse: dov'e' nel sistema del pannello (x a destra, y in basso, dall'angolo in alto a sinistra), se e' dentro, se si e' mosso, il clic sinistro, la rotella.</summary>
            public Vector2 pointer;
            public bool pointerIn, moved, click, held;
            public float wheel;
            public int digit;                // Page number, or zero when no number was pressed.
            /// <summary>Il mouse nel sistema del contenuto di una pagina (l'angolo in alto a sinistra e' l'origine, y verso il basso).</summary>
            public Vector2 Local => pointer - new Vector2(HubPad, HubContentY);
        }

        /// <summary>Una pagina del menu di Select: si costruisce una volta (sotto 'parent', che ha l'angolo in alto a sinistra come origine e la misura del contenuto), si accende e si spegne col menu e
        /// ogni fotogramma legge i comandi e si ridisegna. Le coordinate delle pagine sono quelle del pannello, y verso il basso.</summary>
        public abstract class HubPage
        {
            public string label;
            public RectTransform root;
            public abstract void Build(HUD h, RectTransform parent);
            public virtual void OnShow(HUD h) { }
            public virtual void OnHide(HUD h) { }
            public abstract void Tick(HUD h, in HubInput inp, float dt);
            /// <summary>La riga dei comandi in fondo al pannello.</summary>
            public virtual string Hint => "";
            /// <summary>La pagina ha una domanda aperta (il "sei sicuro?" del negozio): indietro e le schede sono suoi finche' non e' chiusa, e il menu non si chiude.</summary>
            public virtual bool CapturesBack => false;
            /// <summary>Il menu e il deserto hanno finito: la pagina lascia andare quello che si era costruita (la vetrina 3D del negozio).</summary>
            public virtual void Dispose(HUD h) { }
        }

        RectTransform hubRoot, hubPanel, hubFrame, hubContent;
        CanvasGroup hubGroup;
        Text hubPoints, hubHint, hubToast;
        Image hubSelLine;
        readonly List<Text> hubTabText = new List<Text>();
        readonly List<Image> hubTabBack = new List<Image>();
        readonly List<Rect> hubTabRect = new List<Rect>();
        List<HubPage> hubPages;
        int hubPage, hubFaults;
        bool hubOpen, hubDead, hubDisposed;
        float hubOpenT, hubLastHp, hubReopenAt, hubToastT;
        Vector3 hubLastMouse;
        Seat hubSeat;
        Vector2 hubLaidFor;
        int hubHeldX, hubHeldY;
        float hubRepX, hubRepY;

        // ---- per le prove
        public bool HubIsOpen => hubOpen;
        public int HubPageIndex => hubPage;
        public int HubPageCount => hubPages != null ? hubPages.Count : 0;
        public HubPage HubCurrentPage => hubPages != null && hubPage >= 0 && hubPage < hubPages.Count ? hubPages[hubPage] : null;
        public IReadOnlyList<HubPage> HubPages { get { EnsureHub(); return hubPages; } }
        public RectTransform HubPanelRect => hubPanel;
        public string HubToastText => hubToast != null ? hubToast.text : "";
        public string HubPointsText => hubPoints != null ? hubPoints.text : "";
        /// <summary>Per le prove: il menu e' stato spento perche' ha inciampato tre volte.</summary>
        public bool HubIsDead => hubDead;

        static bool HubKeyboardToggle => SeatInput.GetKeyDown(KeyCode.M) || SeatInput.GetKeyDown(KeyCode.Tab);

        void UpdateDesertHubUI()
        {
            if (hubDead) return;
            try { TickDesertHub(); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                if (++hubFaults >= 3)
                {
                    hubDead = true;
                    if (hubOpen) CloseHub(false);
                    if (hubRoot != null) hubRoot.gameObject.SetActive(false);
                }
            }
        }

        void TickDesertHub()
        {
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            var fd = Proc.FloorDirector.I;
            bool here = DesertMode.OnSurface && pl != null;
            if (!here)
            {
                if (hubOpen) CloseHub(false);
                if (!DesertMode.Active && hubRoot != null && !hubDisposed) { DisposeHubPages(); hubDisposed = true; }
                return;
            }
            hubDisposed = false;
            float dt = Time.unscaledDeltaTime;
            if (!hubOpen)
            {
                if (Time.unscaledTime < hubReopenAt) return;
                if (!pl.alive || pl.Downed || fd.transitioning || GameMenu.InMenu || GameMenu.Paused || GameMenu.Overlay) return;
                if (HubKeyboardToggle || TouchControls.Down(TouchControls.Action.Map)) OpenHub(-1);
                return;
            }
            TickOpenHub(pl, fd, dt);
        }

        // ------------------------------------------------------------------ apertura e chiusura
        /// <summary>Apre il menu (alla pagina data, o all'ultima se -1). Pubblico per le prove.</summary>
        public bool OpenHub(int page = -1)
        {
            if (hubDead || hubOpen) return false;
            EnsureHub();
            hubOpen = true; hubOpenT = 0f; hubSeat = Seats.Current;
            hubHeldX = hubHeldY = 0; hubToastT = 0f; hubToast.text = "";
            hubLaidFor = Vector2.zero;
            Seats.SetOverlay(hubSeat, true);
            hubRoot.gameObject.SetActive(true);
            hubRoot.SetAsLastSibling();
            hubGroup.alpha = 0f;
            hubLastMouse = Input.mousePosition;
            var gm = GameManager.I;
            hubLastHp = gm != null && gm.player != null ? gm.player.hp : 0f;
            SelectHubPage(page >= 0 ? page : hubPage, true);
            Sfx.UI(SoundBank.GrimorioApre, 0.7f);
            return true;
        }

        public void CloseHub(bool sound = true)
        {
            if (!hubOpen) return;
            hubOpen = false;
            if (hubPages != null && hubPage < hubPages.Count) hubPages[hubPage].OnHide(this);
            hubRoot.gameObject.SetActive(false);
            hubClosedFrame = Time.frameCount;
            hubReopenAt = Time.unscaledTime + 0.55f;          // il tocco di Select che chiude finisce al rilascio, e il rilascio e' un tocco: non deve riaprirlo
            Seats.SetOverlay(hubSeat, false);
            if ((hubSeat == null || hubSeat.keyboard) && !GameManager.autoTest) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (sound) Sfx.UI(SoundBank.BookSeal, 0.5f);
        }

        public void SelectHubPage(int i, bool force = false)
        {
            if (hubPages == null || hubPages.Count == 0) return;
            i = ((i % hubPages.Count) + hubPages.Count) % hubPages.Count;
            if (i == hubPage && !force) return;
            if (hubOpen && hubPage < hubPages.Count) hubPages[hubPage].OnHide(this);
            hubPage = i;
            for (int k = 0; k < hubPages.Count; k++) hubPages[k].root.gameObject.SetActive(k == i);
            if (hubOpen) hubPages[i].OnShow(this);
            RefreshHubTabs();
            if (hubOpen && !force) Sfx.UI(SoundBank.MagiaScelta, 0.35f, 1.15f);
        }

        /// <summary>Una riga di avviso in fondo al pannello (rossa se e' un rifiuto, dorata se e' andata bene), per tre secondi.</summary>
        public void HubSay(string s, bool bad)
        {
            if (hubToast == null) return;
            hubToast.text = s; hubToast.color = bad ? HubRedC : HubBrassC; hubToastT = 3.4f;
        }

        // ------------------------------------------------------------------ ogni fotogramma di menu aperto
        void TickOpenHub(PlayerController pl, Proc.FloorDirector fd, float dt)
        {
            // ferito, morto, in pausa o in un passaggio: il menu si chiude (non ferma il tempo, e un nemico non aspetta chi compra una moto)
            if (!pl.alive || pl.hp < hubLastHp - 0.5f || GameMenu.InMenu || GameMenu.Paused || fd.transitioning) { CloseHub(false); return; }
            hubLastHp = pl.hp;
            hubOpenT += dt;
            float k = Mathf.Clamp01(hubOpenT / 0.22f);
            hubGroup.alpha = k;
            var sz = ((RectTransform)transform).rect.size;
            if (sz != hubLaidFor) { hubLaidFor = sz; LayoutHub(sz); }
            float pop = Mathf.Lerp(0.96f, 1f, 1f - (1f - k) * (1f - k));
            hubPanel.localScale = Vector3.one * (hubFit * pop);

            var inp = ReadHubInput(dt);
            var page = hubPages[hubPage];
            bool captured = page.CapturesBack;                       // una domanda aperta (il negozio): indietro e le schede sono suoi
            if (inp.back && !captured) { CloseHub(); return; }
            // la barra: le pagine vicine, i numeri, un clic
            int go = int.MinValue;
            if (!captured)
            {
                go = inp.prev ? hubPage - 1 : inp.next ? hubPage + 1 : inp.digit > 0 && inp.digit <= hubPages.Count ? inp.digit - 1 : int.MinValue;
                if (inp.click && inp.pointerIn && inp.pointer.y < HubNavH)
                    for (int i = 0; i < hubTabRect.Count; i++) if (hubTabRect[i].Contains(inp.pointer)) go = i;
            }
            if (go != int.MinValue) { SelectHubPage(go); inp.confirm = false; inp.click = false; page = hubPages[hubPage]; }
            page.Tick(this, inp, dt);
            if (!hubOpen) return;
            // i punti, il suggerimento e l'avviso
            var gm = GameManager.I;
            string pts = (gm != null ? gm.score : 0).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');
            if (hubPoints.text != pts) hubPoints.text = pts;
            string hint = hubPages[hubPage].Hint;
            if (hubHint.text != hint) hubHint.text = hint;
            if (hubToastT > 0f)
            {
                hubToastT -= dt;
                var c = hubToast.color; c.a = Mathf.Clamp01(hubToastT / 0.6f); hubToast.color = c;
                if (hubToastT <= 0f) hubToast.text = "";
            }
            hubLastMouse = Input.mousePosition;
        }

        float hubFit = 1f;

        /// <summary>Il pannello non si ingrandisce mai (il testo di uGUI non si ridisegna a una scala diversa da quella del canvas: ingrandito di un decimo e' sfocato, con un doppio contorno) e si
        /// rimpicciolisce solo se il posto e' piu' piccolo di lui. Il canvas dell'HUD e' "Expand" su 1280 x 720, quindi in pratica sta sempre a scala 1.</summary>
        void LayoutHub(Vector2 sz)
        {
            hubFit = Mathf.Min(1f, Mathf.Min(sz.x / (HubW + 24f), sz.y / (HubH + 24f)));
        }

        /// <summary>Un passo di navigazione con ripetizione: scatta subito, poi dopo 0,38 s ogni 0,07 s finche' si tiene premuto.</summary>
        static int HubStep(ref int last, ref float t, int cur, float dt)
        {
            if (cur == 0) { last = 0; return 0; }
            if (cur != last) { last = cur; t = 0.38f; return cur; }
            t -= dt;
            if (t <= 0f) { t = 0.07f; return cur; }
            return 0;
        }

        HubInput ReadHubInput(float dt)
        {
            var inp = new HubInput();
            var pad = PadControls.Mine.mapper;
            int hx = 0, hy = 0;
            if (SeatInput.GetKey(KeyCode.LeftArrow) || SeatInput.GetKey(KeyCode.A)) hx = -1;
            else if (SeatInput.GetKey(KeyCode.RightArrow) || SeatInput.GetKey(KeyCode.D)) hx = 1;
            if (SeatInput.GetKey(KeyCode.UpArrow) || SeatInput.GetKey(KeyCode.W)) hy = 1;
            else if (SeatInput.GetKey(KeyCode.DownArrow) || SeatInput.GetKey(KeyCode.S)) hy = -1;
            if (hx == 0) hx = pad.NavX;
            if (hy == 0) hy = pad.NavY;
            inp.nx = HubStep(ref hubHeldX, ref hubRepX, hx, dt);
            inp.ny = HubStep(ref hubHeldY, ref hubRepY, hy, dt);
            inp.confirm = SeatInput.GetKeyDown(KeyCode.Return) || SeatInput.GetKeyDown(KeyCode.KeypadEnter) || SeatInput.GetKeyDown(KeyCode.Space) || pad.Confirm;
            inp.back = SeatInput.GetKeyDown(KeyCode.Escape) || HubKeyboardToggle || SeatInput.GetKeyDown(KeyCode.Backspace) || SeatInput.GetMouseButtonDown(1)
                || pad.Cancel || pad.Back;
            inp.prev = SeatInput.GetKeyDown(KeyCode.Q) || SeatInput.GetKeyDown(KeyCode.PageUp) || SeatInput.GetKeyDown(KeyCode.LeftBracket) || pad.Prev;
            inp.next = SeatInput.GetKeyDown(KeyCode.E) || SeatInput.GetKeyDown(KeyCode.PageDown) || SeatInput.GetKeyDown(KeyCode.RightBracket) || pad.Next;
            for (int i = 0; i < hubPages.Count; i++) if (SeatInput.GetKeyDown(KeyCode.Alpha1 + i)) inp.digit = i + 1;
            if (Seats.Keyboard)
            {
                Vector3 mouse = Input.mousePosition;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(hubPanel, mouse, null, out Vector2 lp))
                {
                    inp.pointer = new Vector2(lp.x + HubW * 0.5f, HubH * 0.5f - lp.y);
                    inp.pointerIn = inp.pointer.x >= 0f && inp.pointer.y >= 0f && inp.pointer.x <= HubW && inp.pointer.y <= HubH;
                }
                inp.moved = (mouse - hubLastMouse).sqrMagnitude > 4f;
                inp.click = SeatInput.GetMouseButtonDown(0);
                inp.held = SeatInput.GetMouseButton(0);
                inp.wheel = SeatInput.GetAxis("Mouse ScrollWheel");
            }
            return inp;
        }

        // ------------------------------------------------------------------ costruzione
        void EnsureHub()
        {
            if (hubRoot != null) return;
            var go = new GameObject("MenuSelect", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            hubRoot = (RectTransform)go.transform;
            hubRoot.anchorMin = Vector2.zero; hubRoot.anchorMax = Vector2.one; hubRoot.offsetMin = hubRoot.offsetMax = Vector2.zero;
            hubGroup = go.GetComponent<CanvasGroup>();
            var dim = new GameObject("Velo", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(hubRoot, false);
            var drt = (RectTransform)dim.transform;
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = drt.offsetMax = Vector2.zero;
            var dimImg = dim.GetComponent<Image>(); dimImg.color = new Color(0f, 0f, 0f, 0.5f); dimImg.raycastTarget = false;

            var pg = new GameObject("Pannello", typeof(RectTransform));
            pg.transform.SetParent(hubRoot, false);
            hubPanel = (RectTransform)pg.transform;
            hubPanel.anchorMin = hubPanel.anchorMax = hubPanel.pivot = new Vector2(0.5f, 0.5f);
            hubPanel.anchoredPosition = Vector2.zero; hubPanel.sizeDelta = new Vector2(HubW, HubH);
            hubFrame = Box(hubPanel, "Cornice", Vector2.zero, new Vector2(HubW, HubH));
            Img(hubFrame, "Fondo", Vector2.zero, new Vector2(HubW, HubH), HubBoard, null);
            // la cornice d'ottone con i chiodi (come la minimappa)
            Img(hubFrame, "FiloAlto", new Vector2(5f, -5f), new Vector2(HubW - 10f, 2f), HubBrassC, null);
            Img(hubFrame, "FiloBasso", new Vector2(5f, -(HubH - 7f)), new Vector2(HubW - 10f, 2f), HubBrassC, null);
            Img(hubFrame, "FiloSinistro", new Vector2(5f, -5f), new Vector2(2f, HubH - 10f), HubBrassC, null);
            Img(hubFrame, "FiloDestro", new Vector2(HubW - 7f, -5f), new Vector2(2f, HubH - 10f), HubBrassC, null);
            var stud = CircleSprite();
            foreach (var p in new[] { new Vector2(2f, -2f), new Vector2(HubW - 9f, -2f), new Vector2(2f, -(HubH - 9f)), new Vector2(HubW - 9f, -(HubH - 9f)) }) Img(hubFrame, "Chiodo", p, new Vector2(7f, 7f), HubBrassC, stud);

            // la barra di navigazione
            Img(hubFrame, "Barra", new Vector2(8f, -8f), new Vector2(HubW - 16f, HubNavH - 8f), HubBar, null);
            Img(hubFrame, "BarraFilo", new Vector2(8f, -(HubNavH - 1f)), new Vector2(HubW - 16f, 1.5f), new Color(HubBrassC.r, HubBrassC.g, HubBrassC.b, 0.55f), null);
            hubPages = MakeHubPages();
            const float tabY = 14f, tabH = 38f;
            float x = 66f;
            var badgeL = HubText(hubFrame, "LB", 14f, tabY + 6f, 44f, 26f, 12, TextAnchor.MiddleCenter, HubDimC, FontStyle.Bold);
            badgeL.text = "Q  LB";
            for (int i = 0; i < hubPages.Count; i++)
            {
                var pgd = hubPages[i];
                float w = Mathf.Max(84f, 28f + pgd.label.Length * 9f);
                var back = Img(hubFrame, "Scheda" + i, new Vector2(x, -tabY), new Vector2(w, tabH), Color.clear, null);
                var t = HubText(hubFrame, "Etichetta" + i, x, tabY, w, tabH, 17, TextAnchor.MiddleCenter, HubDimC, FontStyle.Bold);
                t.text = (i + 1) + "  " + pgd.label;
                hubTabBack.Add(back); hubTabText.Add(t); hubTabRect.Add(new Rect(x, tabY, w, tabH));
                x += w + 8f;
            }
            hubSelLine = Img(hubFrame, "Sottolinea", new Vector2(0f, -(tabY + tabH - 2f)), new Vector2(80f, 3f), HubBrassC, null);
            var badgeR = HubText(hubFrame, "RB", x - 4f, tabY + 6f, 52f, 26f, 12, TextAnchor.MiddleCenter, HubDimC, FontStyle.Bold);
            badgeR.text = "E  RB";
            // i punti, a destra
            Img(hubFrame, "Moneta", new Vector2(HubW - 196f, -(tabY + 9f)), new Vector2(20f, 20f), HubBrassC, stud);
            HubText(hubFrame, "PuntiEtichetta", HubW - 170f, tabY + 2f, 60f, 16f, 11, TextAnchor.MiddleLeft, HubDimC, FontStyle.Bold).text = "PUNTI";
            hubPoints = HubText(hubFrame, "Punti", HubW - 172f, tabY + 14f, 150f, 24f, 22, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold, true);
            hubPoints.text = "0";

            // il contenuto
            hubContent = Box(hubFrame, "Contenuto", new Vector2(HubPad, -HubContentY), new Vector2(HubContentW, HubContentH));
            foreach (var pgd in hubPages)
            {
                pgd.root = Box(hubContent, "Pagina " + pgd.label, Vector2.zero, new Vector2(HubContentW, HubContentH));
                pgd.Build(this, pgd.root);
                pgd.root.gameObject.SetActive(false);
            }

            // il piede: i comandi e l'avviso
            Img(hubFrame, "PiedeFilo", new Vector2(HubPad, -(HubH - HubFootH - 6f)), new Vector2(HubW - 2f * HubPad, 1f), new Color(HubBrassC.r, HubBrassC.g, HubBrassC.b, 0.35f), null);
            hubHint = HubText(hubFrame, "Comandi", HubPad + 2f, HubH - HubFootH - 2f, HubW * 0.62f, 24f, 12, TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal);
            hubToast = HubText(hubFrame, "Avviso", HubW * 0.40f, HubH - HubFootH - 2f, HubW * 0.58f - HubPad, 24f, 14, TextAnchor.MiddleRight, HubBrassC, FontStyle.Bold);
            hubToast.text = "";
            hubRoot.gameObject.SetActive(false);
            RefreshHubTabs();
        }

        void RefreshHubTabs()
        {
            if (hubTabText.Count == 0) return;
            for (int i = 0; i < hubTabText.Count; i++)
            {
                bool on = i == hubPage;
                hubTabText[i].color = on ? HubInkC : HubDimC;
                hubTabBack[i].color = on ? new Color(HubBrassC.r, HubBrassC.g, HubBrassC.b, 0.18f) : Color.clear;
            }
            var r = hubTabRect[Mathf.Clamp(hubPage, 0, hubTabRect.Count - 1)];
            hubSelLine.rectTransform.anchoredPosition = new Vector2(r.x + 6f, -(r.y + r.height - 2f));
            hubSelLine.rectTransform.sizeDelta = new Vector2(r.width - 12f, 3f);
        }

        // ------------------------------------------------------------------ attrezzi per le pagine (coordinate del pannello: y verso il basso)
        static Text HubText(Transform parent, string name, float x, float y, float w, float h, int fs, TextAnchor a, Color col, FontStyle st, bool serif = false)
        {
            var t = Txt(parent, name, new Vector2(x, -y), new Vector2(w, h), fs, a, col, st);
            if (serif) t.font = Serif();
            t.supportRichText = true;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        static Image HubRect(Transform parent, string name, float x, float y, float w, float h, Color c) => Img(parent, name, new Vector2(x, -y), new Vector2(w, h), c, null);

        static bool HubIn(Vector2 p, float x, float y, float w, float h) => p.x >= x && p.y >= y && p.x <= x + w && p.y <= y + h;

        static string HubNum(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');

        /// <summary>Una barra orizzontale piena per 'v' (0..1) su un fondo scuro: restituisce il riempimento.</summary>
        static Image HubMeter(Transform parent, string name, float x, float y, float w, float h, float v, Color fill, Color back)
        {
            HubRect(parent, name + "Fondo", x, y, w, h, back);
            var f = HubRect(parent, name, x, y, Mathf.Max(0f, w * Mathf.Clamp01(v)), h, fill);
            return f;
        }

        static void HubSetMeter(Image fill, float w, float v) { fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, w * Mathf.Clamp01(v)), fill.rectTransform.sizeDelta.y); }

        /// <summary>Le pagine, nell'ordine della barra. Per aggiungerne una: una classe che eredita HubPage e una riga qui.</summary>
        List<HubPage> MakeHubPages() => new List<HubPage>
        {
            new HubMapPage(),
            new HubBookPage(),
            new HubSpellPage(),
            new HubStorePage(),
            new HubCameraPage(),
            new HubCheatPage(),
        };

        /// <summary>Il deserto e' finito: le pagine lasciano quello che avevano (la vetrina del negozio).</summary>
        void DisposeHubPages()
        {
            if (hubPages == null) return;
            foreach (var p in hubPages) { try { p.Dispose(this); } catch (System.Exception e) { Debug.LogException(e); } }
        }
    }
}

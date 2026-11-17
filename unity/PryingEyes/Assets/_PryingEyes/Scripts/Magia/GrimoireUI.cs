using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Il grimorio aperto (2026-11-17), come le due tavole dell'utente: la tavola stessa ("Potenziamenti delle Armi" / "Perk del
    /// Dodecaedro Arcano Maledetto", GrimoirePage) con sopra, nei diciannove vani, le icone di bronzo dei nodi e il loro stato (posseduto:
    /// rosso acceso; da comprare: ambra; chiuso: spento; scelto: anello d'oro), le linee che si accendono tra i nodi, e accanto una scheda di
    /// pergamena col nodo scelto (nome, effetto, prezzo o cosa manca) e il piede con punti, essenza e comandi.
    /// Un libro per giocatore (nello schermo condiviso ogni posto ha il suo), figlio dell'HUD di chi legge: sta dentro la sua porzione di
    /// schermo e si adatta alla sua forma (tavola e scheda affiancate in una finestra larga, una sopra l'altra in una alta), occupa al
    /// massimo l'88% dell'altezza e lascia vedere il gioco attorno. Non ferma il tempo e non blocca gli altri: blocca solo i comandi di chi
    /// legge (GameMenu.Overlay e' del posto: Seats.SetOverlay), che finche' legge non si muove e non puo' essere colpito. La stanza segreta
    /// e' al riparo (i nemici non passano dal cunicolo); se chi legge e' ferito o muore, o parte la pausa, il libro si chiude.
    /// Comandi: mouse (passa sopra e clicca; il destro chiude), frecce o WASD e Invio/F, controller di chi legge (croce o stick, A, B);
    /// Esc chiude. Comprare costa punti (Grimoire.Price); su un nodo interno gia' tuo il clic lo rende la magia (o il sigillo) attiva.</summary>
    public sealed class GrimoireUI : MonoBehaviour
    {
        static readonly Dictionary<int, GrimoireUI> books = new Dictionary<int, GrimoireUI>();
        static int closedFrame = -1;
        static Font sans;

        /// <summary>Il libro di chi gira (null se non ne ha mai aperto uno).</summary>
        public static GrimoireUI I { get { books.TryGetValue(Seats.SeatIndex, out var u); return u; } }
        /// <summary>Chi gira sta leggendo.</summary>
        public static bool Open { get { var u = I; return u != null && u.visible; } }
        /// <summary>Qualcuno sta leggendo (luce del libro sull'altare).</summary>
        public static bool AnyOpen { get { foreach (var u in books.Values) if (u != null && u.visible) return true; return false; } }
        /// <summary>Quel posto sta leggendo (null: il giocatore singolo).</summary>
        public static bool OpenFor(Seat s) { books.TryGetValue(s != null ? s.index : 0, out var u); return u != null && u.visible; }
        /// <summary>Un libro si e' chiuso in questo fotogramma: l'Esc che l'ha chiuso non deve aprire anche la pausa (GameMenu).</summary>
        public static bool ClosedThisFrame => closedFrame == Time.frameCount;
        /// <summary>Per le prove: nodi comprati da quando il gioco e' partito.</summary>
        public static int statBought;

        sealed class NodeUi
        {
            public RectTransform root;
            public Image glow, veil, icon, ring;
            public float r, pop = 1f;
            public Vector2 pos;              // nello spazio della tavola (pixel, y verso l'alto, origine al centro)
        }

        sealed class LineUi { public int a, b; public Image line, glow; }

        RectTransform self, content, group, plateRt, cardRt, linesRoot;
        CanvasGroup cg;
        Image dim, selRing, cardDisc, cardRim, cardIcon, sep;
        RawImage plateImg, cardImg;
        Text cardName, cardTier, cardBody, cardToast, cardFoot;
        readonly NodeUi[] nodes = new NodeUi[Grimoire.Count];
        readonly List<LineUi> lines = new List<LineUi>();
        readonly Grimoire.Lock[] status = new Grimoire.Lock[Grimoire.Count];
        GrimoirePage.Plate plate;
        Grimoire.Book book;
        GrimoireAltar altar;
        /// <summary>Chi legge: comandi, acquisti, essenza e punti sono i suoi (Seats.Enter). Null fuori dallo schermo condiviso.</summary>
        Seat reader;
        int key, sel = Grimoire.FirstInner;
        bool visible;
        float openT, toastT, lastHp, cardW;
        Vector2 laidFor;
        Vector3 lastMouse;
        int lastNavX, lastNavY;
        // l'ultimo stato scritto nella scheda: i testi si rifanno solo se qualcosa cambia
        int shownSel = -1, shownScore = int.MinValue, shownEss = -1, shownActive = -2;
        uint shownMask = uint.MaxValue;
        bool shownPad, shownDirty = true;

        const float Pad = 14f, Gap = 10f, CardWSide = 330f, CardWMax = 560f, CardH = 350f, MaxFrac = 0.88f, Margin = 22f;
        static readonly Color InkC = new Color(0.17f, 0.07f, 0.04f), RedInkC = new Color(0.50f, 0.04f, 0.03f), Faded = new Color(0.30f, 0.18f, 0.11f);

        public static void Show(Grimoire.Book b, GrimoireAltar a)
        {
            var u = I;
            if (u == null)
            {
                var go = new GameObject("Grimorio", typeof(RectTransform));
                var hud = GameManager.I != null ? GameManager.I.hud : null;
                if (hud != null) go.transform.SetParent(hud.transform, false);
                u = go.AddComponent<GrimoireUI>();
                books[Seats.SeatIndex] = u;
            }
            u.OpenBook(b, a);
        }

        /// <summary>Chiude il libro di chi gira.</summary>
        public static void Hide() { var u = I; if (u != null) u.Close(); }

        /// <summary>Chiude tutti i libri aperti (fine del piano, ritorno al menu).</summary>
        public static void HideAll() { foreach (var u in new List<GrimoireUI>(books.Values)) if (u != null) u.Close(); }

        void Awake() { key = Seats.SeatIndex; Build(); }

        void OnDestroy()
        {
            if (books.TryGetValue(key, out var me) && me == this) books.Remove(key);
            if (visible) { visible = false; Seats.SetOverlay(reader, false); }
        }

        // ------------------------------------------------------------------ costruzione
        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.zero;
            return rt;
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        static Image NewImage(Transform parent, string name, Sprite s, Color c)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s; img.color = c; img.raycastTarget = false; img.preserveAspect = true;
            return img;
        }

        static void Size(Image img, float s) { img.rectTransform.sizeDelta = new Vector2(s, s); }

        /// <summary>Un riquadro ancorato in alto a sinistra della scheda: x, y dal suo angolo (y verso il basso), larghezza e altezza.</summary>
        static void SetTL(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
        }

        static Text NewText(Transform parent, string name, int fs, TextAnchor a, Color c, FontStyle st, bool serif = true)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            if (!serif && sans == null) sans = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 32);
            t.font = serif ? HUD.Serif() : sans;
            t.fontSize = fs; t.alignment = a; t.color = c; t.fontStyle = st; t.lineSpacing = 1.04f;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false; t.supportRichText = true;
            return t;
        }

        static RectTransform Segment(Transform parent, string name, out Image img)
        {
            var rt = NewRect(name, parent);
            rt.pivot = new Vector2(0f, 0.5f);
            img = rt.gameObject.AddComponent<Image>();
            img.sprite = PerkIcons.White(); img.raycastTarget = false;
            return rt;
        }

        void Build()
        {
            self = (RectTransform)transform;
            if (GetComponentInParent<Canvas>() == null)
            {
                // senza HUD (prove, scena vuota): un canvas suo
                var canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 60;
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            else Stretch(self);
            gameObject.AddComponent<RectMask2D>();                   // niente esce dalla porzione di schermo di chi legge

            content = NewRect("Contenuto", self);
            Stretch(content);
            cg = content.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false; cg.interactable = false;
            // il gioco resta visibile attorno al libro, appena scurito
            var dimRt = NewRect("Velo", content);
            Stretch(dimRt);
            dim = dimRt.gameObject.AddComponent<Image>();
            dim.color = new Color(0.02f, 0.005f, 0.005f, 0.74f); dim.raycastTarget = false;
            group = NewRect("Libro", content);

            // la tavola: lo spazio locale e' quello dei suoi pixel (la scala la da' Relayout)
            plateRt = NewRect("Tavola", group);
            plateImg = plateRt.gameObject.AddComponent<RawImage>();
            plateImg.raycastTarget = false;
            linesRoot = NewRect("Linee", plateRt);
            for (int i = 0; i < Grimoire.Count; i++)
                foreach (int j in Grimoire.Neighbors(i))
                {
                    if (j < i) continue;
                    var L = new LineUi { a = i, b = j };
                    Segment(linesRoot, "Alone", out L.glow);
                    Segment(linesRoot, "Linea", out L.line);
                    lines.Add(L);
                }
            for (int i = 0; i < Grimoire.Count; i++)
            {
                var n = new NodeUi { root = NewRect("Nodo" + i, plateRt) };
                n.glow = NewImage(n.root, "Bagliore", GrimoireIcons.Glow(), new Color(1f, 0.1f, 0.05f, 0f));
                if (i == Grimoire.Supreme) n.veil = NewImage(n.root, "Velo", GrimoireIcons.Disc(), new Color(0.03f, 0.005f, 0.005f, 0.6f));
                n.icon = NewImage(n.root, "Icona", null, Color.white);
                if (i == Grimoire.Supreme) n.icon.enabled = false;   // il supremo ha gia' il suo emblema nella tavola (il teschio, il caprone)
                n.ring = NewImage(n.root, "Anello", GrimoireIcons.Halo(i == Grimoire.Supreme), new Color(1f, 0.2f, 0.1f, 0f));
                nodes[i] = n;
            }
            selRing = NewImage(plateRt, "Scelto", GrimoireIcons.Halo(), new Color(1f, 0.88f, 0.55f, 0f));

            // la scheda del nodo scelto
            cardRt = NewRect("Scheda", group);
            cardImg = cardRt.gameObject.AddComponent<RawImage>();
            cardImg.texture = GrimoirePage.Card(); cardImg.raycastTarget = false;
            cardDisc = NewImage(cardRt, "MedaglioneFondo", GrimoireIcons.Disc(), new Color(0.10f, 0.025f, 0.025f, 0.97f));
            cardRim = NewImage(cardRt, "MedaglioneAnello", GrimoireIcons.Halo(), new Color(0.62f, 0.38f, 0.16f, 1f));
            cardIcon = NewImage(cardRt, "MedaglioneIcona", null, Color.white);
            cardName = NewText(cardRt, "Nome", 20, TextAnchor.UpperLeft, RedInkC, FontStyle.Bold);
            cardTier = NewText(cardRt, "Classe", 11, TextAnchor.UpperLeft, Faded, FontStyle.Italic);
            cardBody = NewText(cardRt, "Corpo", 14, TextAnchor.UpperLeft, InkC, FontStyle.Normal);
            cardToast = NewText(cardRt, "Avviso", 13, TextAnchor.MiddleCenter, RedInkC, FontStyle.BoldAndItalic);
            sep = NewImage(cardRt, "Filo", PerkIcons.White(), new Color(0.30f, 0.12f, 0.07f, 0.55f));
            cardFoot = NewText(cardRt, "Piede", 13, TextAnchor.UpperLeft, InkC, FontStyle.Normal, false);
            content.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ disposizione
        /// <summary>Il centro del vano i nello spazio della tavola (pixel, y verso l'alto, origine al centro).</summary>
        Vector2 Local(int i) => new Vector2(plate.slot[i].x - plate.w * 0.5f, plate.h * 0.5f - plate.slot[i].y);

        /// <summary>Mette sui vani della tavola le icone, gli aloni e le linee (si rifa a ogni apertura: i due libri hanno tavole diverse).</summary>
        void ApplyPlate()
        {
            plateRt.sizeDelta = new Vector2(plate.w, plate.h);
            for (int i = 0; i < Grimoire.Count; i++)
            {
                var n = nodes[i];
                n.r = plate.radius[i]; n.pos = Local(i);
                n.root.anchoredPosition = n.pos; n.root.sizeDelta = new Vector2(2f * n.r, 2f * n.r);
                Size(n.glow, n.r * 3.3f); Size(n.ring, n.r * 2.62f);
                if (n.veil != null) Size(n.veil, n.r * 2.0f);
                Size(n.icon, n.r * 2.0f);
            }
            foreach (var L in lines)
            {
                Vector2 a = nodes[L.a].pos, b = nodes[L.b].pos, d = b - a;
                float len = d.magnitude;
                if (len < 1f) continue;
                Vector2 u = d / len;
                // dal bordo di un vano al bordo dell'altro: dentro i vani le linee non passano
                Vector2 p0 = a + u * (nodes[L.a].r * 1.2f), p1 = b - u * (nodes[L.b].r * 1.2f);
                float l = Mathf.Max(0f, (p1 - p0).magnitude);
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                foreach (var pair in new[] { (L.glow, 13f), (L.line, 4.5f) })
                {
                    var rt = pair.Item1.rectTransform;
                    rt.anchoredPosition = p0; rt.sizeDelta = new Vector2(l, pair.Item2);
                    rt.localRotation = Quaternion.Euler(0f, 0f, ang);
                }
            }
        }

        /// <summary>Tavola e scheda nello spazio che c'e': affiancate se la finestra e' larga, una sopra l'altra se e' alta; la tavola
        /// non supera mai l'88% dell'altezza, cosi' attorno si vede ancora il gioco.</summary>
        void Relayout()
        {
            Vector2 area = self.rect.size;
            if (area.x < 80f || area.y < 80f || plate == null) return;
            laidFor = area;
            float pw = plate.w, ph = plate.h;
            float cap = MaxFrac * area.y / ph;
            float fitSide = Mathf.Min((area.x - CardWSide - Gap - 2f * Pad) / pw, (area.y - 2f * Pad) / ph, cap);
            float fitStack = Mathf.Min((area.x - 2f * Pad) / pw, (area.y - CardH - Gap - 2f * Pad) / ph, cap);
            bool side = fitSide >= fitStack;
            float fit = Mathf.Max(0.05f, side ? fitSide : fitStack);
            float cw = side ? CardWSide : Mathf.Clamp(area.x - 2f * Pad, 240f, CardWMax);
            plateRt.localScale = new Vector3(fit, fit, 1f);
            if (side)
            {
                float total = pw * fit + Gap + cw, x0 = -total * 0.5f;
                plateRt.anchoredPosition = new Vector2(x0 + pw * fit * 0.5f, 0f);
                cardRt.anchoredPosition = new Vector2(x0 + pw * fit + Gap + cw * 0.5f, 0f);
            }
            else
            {
                float total = ph * fit + Gap + CardH, top = total * 0.5f;
                plateRt.anchoredPosition = new Vector2(0f, top - ph * fit * 0.5f);
                cardRt.anchoredPosition = new Vector2(0f, -top + CardH * 0.5f);
            }
            if (!Mathf.Approximately(cw, cardW)) { cardW = cw; LayoutCard(cw); }
        }

        void LayoutCard(float cw)
        {
            cardRt.sizeDelta = new Vector2(cw, CardH);
            float m = Margin, inner = cw - 2f * m;
            SetTL(cardDisc.rectTransform, m, m, 62f, 62f);
            SetTL(cardRim.rectTransform, m - 4f, m - 4f, 70f, 70f);
            SetTL(cardIcon.rectTransform, m + 7f, m + 7f, 48f, 48f);
            float tx = m + 62f + 12f, tw = cw - tx - m;
            SetTL(cardName.rectTransform, tx, m + 1f, tw, 26f);
            SetTL(cardTier.rectTransform, tx, m + 29f, tw, 34f);
            SetTL(cardBody.rectTransform, m, m + 76f, inner, 154f);
            SetTL(cardToast.rectTransform, m, 252f, inner, 34f);
            SetTL(sep.rectTransform, m, 288f, inner, 1.5f);
            SetTL(cardFoot.rectTransform, m, 292f, inner, 46f);
        }

        // ------------------------------------------------------------------ apertura e chiusura
        void OpenBook(Grimoire.Book b, GrimoireAltar a)
        {
            book = b; altar = a; reader = Seats.Current;
            var hud = GameManager.I != null ? GameManager.I.hud : null;
            if (hud != null && transform.parent != hud.transform) { transform.SetParent(hud.transform, false); Stretch(self); }
            transform.SetAsLastSibling();                            // sopra tutto il resto dell'HUD
            plate = GrimoirePage.Get(b);
            plateImg.texture = plate.tex;
            ApplyPlate();
            var ns = Grimoire.Of(b);
            for (int i = 0; i < Grimoire.Count; i++) if (i != Grimoire.Supreme) nodes[i].icon.sprite = GrimoireIcons.Engraved(ns[i].icon);
            // la prima scelta: la magia (o il sigillo) attiva, o il primo nodo che puoi comprare
            var s = Magic.State;
            int active = b == Grimoire.Book.Stregoneria ? s.SelectedSpell : s.SelectedInfusion;
            sel = active >= 0 ? active : FirstBuyable(s);
            visible = true; openT = 0f; toastT = 0f; cardToast.text = "";
            laidFor = Vector2.zero; shownDirty = true;
            cg.alpha = 0f; group.localScale = Vector3.one * 0.95f;   // parte dall'inizio dell'apertura anche se il fotogramma finisce prima di Update
            content.gameObject.SetActive(true);
            Seats.SetOverlay(reader, true);
            lastMouse = Input.mousePosition;
            lastNavX = lastNavY = 0;
            lastHp = GameManager.I != null && GameManager.I.player != null ? GameManager.I.player.hp : 0f;
            Sfx.UI(SoundBank.GrimorioApre, 0.75f);
            Relayout();
            Refresh();
        }

        int FirstBuyable(MagicState s)
        {
            for (int i = 0; i < Grimoire.Count; i++) if (s.Status(book, i) == Grimoire.Lock.Libero) return i;
            return 0;
        }

        public void Close()
        {
            if (!visible) return;
            visible = false;
            content.gameObject.SetActive(false);
            closedFrame = Time.frameCount;
            Seats.SetOverlay(reader, false);
            if ((reader == null || reader.keyboard) && !GameManager.autoTest) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            Sfx.UI(SoundBank.BookSeal, 0.5f);
        }

        // ------------------------------------------------------------------ comandi
        void Update()
        {
            if (!visible) return;
            using (Seats.Enter(reader)) UpdateBody();
        }

        void UpdateBody()
        {
            float dt = Time.unscaledDeltaTime;
            openT += dt;
            float k = Mathf.Clamp01(openT / 0.25f);
            cg.alpha = k;
            group.localScale = Vector3.one * Mathf.Lerp(0.95f, 1f, 1f - (1f - k) * (1f - k));
            if (self.rect.size != laidFor) Relayout();
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            // ferito o morto (non dovrebbe succedere nel sacrario): il libro si chiude
            if (pl == null || !pl.alive || pl.hp < lastHp - 0.5f) { Close(); return; }
            lastHp = pl.hp;
            if (GameMenu.InMenu || GameMenu.Paused) { Close(); return; }

            // mouse: il vano sotto il puntatore (solo per chi ha tastiera e mouse)
            Vector3 mouse = Input.mousePosition;
            int hover = -1;
            if (Seats.Keyboard && RectTransformUtility.ScreenPointToLocalPointInRectangle(plateRt, mouse, null, out Vector2 local))
            {
                float best = float.MaxValue;
                for (int i = 0; i < Grimoire.Count; i++)
                {
                    float d = (nodes[i].pos - local).magnitude;
                    if (d < nodes[i].r * 1.2f && d < best) { best = d; hover = i; }
                }
            }
            if ((mouse - lastMouse).sqrMagnitude > 4f && hover >= 0 && hover != sel) { sel = hover; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); }
            lastMouse = mouse;
            if (SeatInput.GetMouseButtonDown(0) && hover >= 0) { sel = hover; Activate(sel); }

            // tastiera e controller (solo quello di chi legge: nello split screen gli altri continuano a giocare)
            var pad = PadControls.Mine.mapper;
            int nx = pad.NavX, ny = pad.NavY;
            Vector2 nav = Vector2.zero;
            if (SeatInput.GetKeyDown(KeyCode.LeftArrow) || SeatInput.GetKeyDown(KeyCode.A) || (nx < 0 && lastNavX >= 0)) nav = Vector2.left;
            else if (SeatInput.GetKeyDown(KeyCode.RightArrow) || SeatInput.GetKeyDown(KeyCode.D) || (nx > 0 && lastNavX <= 0)) nav = Vector2.right;
            else if (SeatInput.GetKeyDown(KeyCode.UpArrow) || SeatInput.GetKeyDown(KeyCode.W) || (ny > 0 && lastNavY <= 0)) nav = Vector2.up;
            else if (SeatInput.GetKeyDown(KeyCode.DownArrow) || SeatInput.GetKeyDown(KeyCode.S) || (ny < 0 && lastNavY >= 0)) nav = Vector2.down;
            lastNavX = nx; lastNavY = ny;
            if (nav != Vector2.zero) Navigate(nav);
            if (SeatInput.GetKeyDown(KeyCode.Return) || SeatInput.GetKeyDown(KeyCode.KeypadEnter) || SeatInput.GetKeyDown(KeyCode.Space) || (openT > 0.3f && SeatInput.GetKeyDown(KeyCode.F)) || pad.Confirm) Activate(sel);
            if (SeatInput.GetKeyDown(KeyCode.Escape) || SeatInput.GetKeyDown(KeyCode.Tab) || SeatInput.GetKeyDown(KeyCode.Backspace) || SeatInput.GetMouseButtonDown(1) || pad.Cancel || TouchControls.Down(TouchControls.Action.Pause)) { Close(); return; }

            if (toastT > 0f) { toastT -= dt; cardToast.color = new Color(RedInkC.r, RedInkC.g, RedInkC.b, Mathf.Clamp01(toastT / 0.6f)); if (toastT <= 0f) cardToast.text = ""; }
            Refresh();
        }

        /// <summary>Il vano piu' vicino nella direzione data (entro 70 gradi), pesando la distanza.</summary>
        void Navigate(Vector2 dir)
        {
            Vector2 from = nodes[sel].pos;
            int best = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < Grimoire.Count; i++)
            {
                if (i == sel) continue;
                Vector2 d = nodes[i].pos - from;
                float len = d.magnitude;
                if (len < 1f) continue;
                float ang = Vector2.Angle(dir, d);
                if (ang > 70f) continue;
                float score = len * (1f + ang / 45f);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) { sel = best; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); }
        }

        /// <summary>Compra il nodo, o (se e' gia' tuo e' interno) lo rende la magia / il sigillo attivo. Pubblico per le prove.</summary>
        public bool Activate(int i)
        {
            using (Seats.Enter(reader)) return ActivateBody(i);
        }

        bool ActivateBody(int i)
        {
            var s = Magic.State;
            var gm = GameManager.I;
            var st = s.Status(book, i);
            var node = Grimoire.Of(book)[i];
            if (st == Grimoire.Lock.Posseduto)
            {
                if (i >= Grimoire.FirstInner && s.Select(book, i))
                {
                    Say(book == Grimoire.Book.Stregoneria ? $"La mano sinistra prepara: {node.name}" : $"Il sigillo pronto: {node.name}");
                    Sfx.UI(SoundBank.MagiaSigillo, 0.5f, 1.2f);
                }
                else Say($"«{node.name}» è già tuo.");
                return false;
            }
            if (st != Grimoire.Lock.Libero) { Say(Grimoire.LockText(book, s.Mask(book), i, st)); Sfx.UI(SoundBank.BookNo, 0.5f); return false; }
            int price = s.Price(book, i);
            int have = gm != null ? gm.score : 0;
            if (have < price) { Say($"Servono {Num(price)} punti: ti mancano {Num(price - have)}."); Sfx.UI(SoundBank.BookNo, 0.5f); return false; }
            gm?.AddPoints(-price);
            s.Buy(book, i);
            statBought++;
            nodes[i].pop = 0f;
            Sfx.UI(SoundBank.GrimorioNodo, 0.85f);
            Say(i >= Grimoire.FirstInner
                ? (book == Grimoire.Book.Stregoneria ? $"{node.name}: la mano sinistra la conosce (3)." : $"{node.name}: la mano sinistra sa tracciarlo sull'arma (3 tenuto).")
                : $"{node.name}: {node.effect}.");
            gm?.hud?.Flash(new Color(0.85f, 0.08f, 0.06f), 0.45f, 0.35f);
            return true;
        }

        void Say(string s) { cardToast.text = s; cardToast.color = RedInkC; toastT = 3.2f; }

        // ------------------------------------------------------------------ disegno
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        static string Num(int n) => n.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');

        void Refresh()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            var s = Magic.State;
            uint mask = s.Mask(book);
            float t = Time.unscaledTime;
            int activeSel = book == Grimoire.Book.Stregoneria ? s.SelectedSpell : s.SelectedInfusion;
            for (int i = 0; i < Grimoire.Count; i++) status[i] = s.Status(book, i);
            for (int i = 0; i < Grimoire.Count; i++)
            {
                var n = nodes[i];
                var st = status[i];
                bool owned = st == Grimoire.Lock.Posseduto, free = st == Grimoire.Lock.Libero, active = i == activeSel;
                n.pop = Mathf.Min(1f, n.pop + Time.unscaledDeltaTime / 0.5f);
                n.root.localScale = Vector3.one * (n.pop < 1f ? 1f + 0.35f * Mathf.Sin((1f - n.pop) * Mathf.PI) : 1f);
                float breathe = 0.5f + 0.5f * Mathf.Sin(t * 2.2f + i);
                // le sfumature sono poche: l'interfaccia fonde i colori in spazio lineare, quindi un alfa di 0,1 su un vano nero si vede gia'
                if (n.veil != null) n.veil.color = new Color(0.03f, 0.005f, 0.005f, owned ? 0f : free ? 0.45f : 0.74f);
                else n.icon.color = owned ? Color.white : free ? new Color(0.80f, 0.72f, 0.62f, 1f) : new Color(0.34f, 0.30f, 0.30f, 0.85f);
                float g = n.veil != null ? 0.4f : 1f;                  // il supremo ha gia' il suo emblema: l'alone e' appena un velo
                n.glow.color = owned ? new Color(1f, 0.10f, 0.05f, ((active ? 0.17f : 0.10f) + 0.05f * breathe) * g)
                             : free ? new Color(1f, 0.62f, 0.18f, (0.045f + 0.03f * breathe) * g) : new Color(1f, 0.1f, 0.05f, 0f);
                n.ring.color = active ? new Color(1f, 0.78f, 0.32f, 0.80f + 0.20f * breathe)
                             : owned ? new Color(1f, 0.20f, 0.10f, 0.80f)
                             : free ? new Color(0.88f, 0.62f, 0.26f, 0.42f) : new Color(0f, 0f, 0f, 0f);
            }
            // il nodo scelto: anello d'oro che respira
            var sn = nodes[sel];
            selRing.rectTransform.anchoredPosition = sn.pos;
            Size(selRing, sn.r * 3.0f);
            selRing.color = new Color(1f, 0.88f, 0.55f, 0.55f + 0.35f * (0.5f + 0.5f * Mathf.Sin(t * 4f)));
            // le linee: rosse tra due nodi tuoi, ambra tra un tuo e uno che puoi comprare; il resto lo fa la tavola
            foreach (var L in lines)
            {
                bool oa = status[L.a] == Grimoire.Lock.Posseduto, ob = status[L.b] == Grimoire.Lock.Posseduto;
                bool fa = status[L.a] == Grimoire.Lock.Libero, fb = status[L.b] == Grimoire.Lock.Libero;
                if (oa && ob)
                {
                    L.line.color = new Color(0.98f, 0.16f, 0.10f, 0.95f);
                    L.glow.color = new Color(1f, 0.10f, 0.05f, 0.30f + 0.10f * Mathf.Sin(t * 2f + L.a));
                }
                else if ((oa && fb) || (ob && fa))
                {
                    L.line.color = new Color(0.90f, 0.56f, 0.18f, 0.80f);
                    L.glow.color = new Color(0.95f, 0.5f, 0.1f, 0.16f);
                }
                else { L.line.color = new Color(0f, 0f, 0f, 0f); L.glow.color = new Color(0f, 0f, 0f, 0f); }
            }
            RefreshCard(gm, s, mask, activeSel);
        }

        void RefreshCard(GameManager gm, MagicState s, uint mask, int activeSel)
        {
            int score = gm.score;
            var pl = gm.player;
            int ess = Mathf.FloorToInt(pl.Arcana.Essence), essMax = Mathf.FloorToInt(pl.Arcana.EssenceMax);
            bool pad = PadControls.Preferred;
            if (!shownDirty && sel == shownSel && mask == shownMask && score == shownScore && ess == shownEss && activeSel == shownActive && pad == shownPad) return;
            shownDirty = false; shownSel = sel; shownMask = mask; shownScore = score; shownEss = ess; shownActive = activeSel; shownPad = pad;
            var node = Grimoire.Of(book)[sel];
            var sst = status[sel];
            bool owned = sst == Grimoire.Lock.Posseduto;
            cardIcon.sprite = GrimoireIcons.Engraved(node.icon);
            cardIcon.color = owned ? Color.white : new Color(0.84f, 0.76f, 0.66f, 1f);
            cardRim.color = owned ? new Color(0.85f, 0.20f, 0.10f, 1f) : new Color(0.62f, 0.38f, 0.16f, 1f);
            cardName.text = node.name;
            cardTier.text = Grimoire.TierName(book, node.tier) + (node.tier == Grimoire.Tier.Interno ? (book == Grimoire.Book.Stregoneria ? " — una magia della mano sinistra" : " — un sigillo da tracciare sull'arma") : "");
            string red = Hex(RedInkC), faded = Hex(Faded);
            int price = s.Price(book, sel);
            bool active = sel == activeSel;
            string stat, act = "";
            switch (sst)
            {
                case Grimoire.Lock.Posseduto:
                    stat = active ? "Già tuo — è quello attivo" : "Già tuo";
                    if (sel >= Grimoire.FirstInner && !active) act = pad ? "[A] rendilo attivo" : "[Clic / F] rendilo attivo";
                    break;
                case Grimoire.Lock.Libero:
                    stat = score >= price ? $"Libero — {Num(price)} punti" : $"Libero — {Num(price)} punti (ti mancano {Num(price - score)})";
                    act = pad ? "[A] acquista" : "[Clic / F] acquista";
                    break;
                default:
                    stat = "Chiuso — " + Grimoire.LockText(book, mask, sel, sst);
                    break;
            }
            cardBody.text = char.ToUpper(node.effect[0]) + node.effect.Substring(1) + ".\n" +
                            $"<i><size=12><color=#{faded}>«{node.lore}»</color></size></i>\n\n" +
                            $"<b><color=#{red}>{stat}</color></b>" + (act.Length > 0 ? $"\n<i><size=12>{act}</size></i>" : "");
            cardFoot.text = $"<b>PUNTI {Num(score)}</b>" + (essMax > 0 ? $"      <b>ESSENZA {ess}/{essMax}</b>" : "") + "\n" +
                            "<size=11>" + (pad ? "croce: scegli   A: acquista   B: chiudi" : "frecce o mouse: scegli   Invio o clic: acquista   Esc: chiudi") + "</size>";
        }

        // ------------------------------------------------------------------ per le prove
        /// <summary>Il nodo scelto.</summary>
        public int Selected { get => sel; set { sel = Mathf.Clamp(value, 0, Grimoire.Count - 1); shownDirty = true; } }
        public Grimoire.Book BookShown => book;
        /// <summary>Il rettangolo del libro (tavola e scheda) e quello dell'area in cui sta, in pixel dello schermo (per controllare che
        /// stia dentro la porzione di chi legge).</summary>
        public Rect ScreenRectOfBook()
        {
            var pc = new Vector3[4]; plateRt.GetWorldCorners(pc);
            var cc = new Vector3[4]; cardRt.GetWorldCorners(cc);
            float x0 = Mathf.Min(pc[0].x, cc[0].x), y0 = Mathf.Min(pc[0].y, cc[0].y), x1 = Mathf.Max(pc[2].x, cc[2].x), y1 = Mathf.Max(pc[2].y, cc[2].y);
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }
        public Rect ScreenRectOfArea()
        {
            var c = new Vector3[4];
            self.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }
    }
}

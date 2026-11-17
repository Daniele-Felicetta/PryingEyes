using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Il grimorio aperto (2026-11-16), come le due tavole dell'utente: la doppia pagina di pergamena bruciata (GrimoirePage) a
    /// tutto schermo, i nastri dei titoli ("Perk del Dodecaedro Arcano Maledetto / Le 19 Virtu' della Stregoneria", "Potenziamenti delle
    /// Armi / I 19 Sigilli dell'Arsenale Maledetto"), a sinistra le tre classi di nodi (esterni, interni, supremo) e la scheda del nodo
    /// scelto, al centro-destra il dodecaedro: dodici medaglioni a spine sull'anello esterno, sei sull'esagono interno, il supremo al
    /// centro, collegati da linee che si accendono di rosso quando li possiedi.
    /// Comandi: mouse (passa sopra e clicca), frecce o WASD e Invio/F, controller (croce o stick, A, B); Esc chiude. Comprare costa
    /// punti (Grimoire.Price); su un nodo interno gia' tuo il clic lo rende la magia (o il sigillo) attiva. Non ferma il tempo: la stanza
    /// segreta e' al riparo (i nemici non passano dal cunicolo). Blocca i comandi del gioco con GameMenu.Overlay.</summary>
    public sealed class GrimoireUI : MonoBehaviour
    {
        public static GrimoireUI I;
        public static bool Open => I != null && I.visible;
        /// <summary>Per le prove: nodi comprati da quando il gioco e' partito.</summary>
        public static int statBought;

        sealed class NodeUi
        {
            public RectTransform root;
            public Image glow, medal, disc, icon, halo;
            public float size, pop = 1f;
        }

        sealed class LineUi { public int a, b; public Image line, glow; }

        Canvas canvas;
        CanvasGroup group;
        RectTransform bookRect;
        RawImage spread;
        Text titleL, titleR, tiers, infoName, infoTier, infoEffect, infoLore, infoStatus, infoAction, footer, toast;
        Image infoMedal, infoDisc, infoIcon;
        readonly NodeUi[] nodes = new NodeUi[Grimoire.Count];
        readonly List<LineUi> lines = new List<LineUi>();
        Grimoire.Book book;
        GrimoireAltar altar;
        /// <summary>Schermo condiviso: chi legge. Comandi, acquisti, essenza e punti sono i suoi (Seats.Enter); il libro copre tutto lo
        /// schermo e, finche' e' aperto, il tempo si ferma per tutti (GameMenu.Refresh).</summary>
        Seat reader;
        int sel = Grimoire.FirstInner;
        bool visible;
        float openT, toastT, lastHp;
        Vector3 lastMouse;
        int lastNavX, lastNavY;
        const float BookW = 1240f, BookH = BookW * GrimoirePage.H / GrimoirePage.W;

        static readonly Color InkC = new Color(0.17f, 0.07f, 0.04f), RedInkC = new Color(0.45f, 0.04f, 0.03f), Faded = new Color(0.30f, 0.18f, 0.11f);

        public static void Show(Grimoire.Book b, GrimoireAltar a)
        {
            if (I == null) I = new GameObject("GrimoireUI").AddComponent<GrimoireUI>();
            I.OpenBook(b, a);
        }

        public static void Hide() { if (I != null) I.Close(); }

        void Awake() { I = this; Build(); gameObject.SetActive(true); canvas.enabled = false; }
        void OnDestroy() { if (I == this) { I = null; if (visible) GameMenu.Overlay = false; } }

        // ------------------------------------------------------------------ costruzione
        static Text MakeText(Transform parent, string name, Vector2 pos, Vector2 size, int fs, TextAnchor a, Color c, FontStyle st, bool serif = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = serif ? HUD.Serif() : Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 32);
            t.fontSize = fs; t.alignment = a; t.color = c; t.fontStyle = st;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false; t.supportRichText = true;
            return t;
        }

        static Image MakeImage(Transform parent, string name, Sprite s, Vector2 pos, float size, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(size, size);
            var i = go.AddComponent<Image>();
            i.sprite = s; i.color = c; i.raycastTarget = false; i.preserveAspect = true;
            return i;
        }

        /// <summary>Da coordinate della pagina (u da sinistra, v dall'alto) a punto nel libro (unita' del canvas, centro del libro = 0).</summary>
        static Vector2 BookPoint(float u, float v) => new Vector2((u - 0.5f) * BookW, (0.5f - v) * BookH);
        static Vector2 BookPoint(Vector2 uv) => BookPoint(uv.x, uv.y);

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            group = gameObject.AddComponent<CanvasGroup>();
            // fondo scuro con la vignetta
            var bg = new GameObject("Fondo");
            bg.transform.SetParent(transform, false);
            var brt = bg.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            var bimg = bg.AddComponent<Image>(); bimg.color = new Color(0.02f, 0.005f, 0.005f, 0.86f); bimg.raycastTarget = false;
            // il libro
            var bk = new GameObject("Libro");
            bk.transform.SetParent(transform, false);
            bookRect = bk.AddComponent<RectTransform>();
            bookRect.anchorMin = bookRect.anchorMax = new Vector2(0.5f, 0.5f);
            bookRect.sizeDelta = new Vector2(BookW, BookH);
            spread = bk.AddComponent<RawImage>(); spread.raycastTarget = false;
            // le linee (sotto i nodi)
            var linesRoot = new GameObject("Linee").AddComponent<RectTransform>();
            linesRoot.SetParent(bookRect, false);
            for (int i = 0; i < Grimoire.Count; i++)
                foreach (int j in Grimoire.Neighbors(i))
                {
                    if (j < i) continue;
                    var L = new LineUi { a = i, b = j };
                    L.glow = Segment(linesRoot, "Alone", BookPoint(GrimoirePage.NodePos(i)), BookPoint(GrimoirePage.NodePos(j)), 9f);
                    L.line = Segment(linesRoot, "Linea", BookPoint(GrimoirePage.NodePos(i)), BookPoint(GrimoirePage.NodePos(j)), 3f);
                    lines.Add(L);
                }
            // i nodi
            for (int i = 0; i < Grimoire.Count; i++)
            {
                var t = Grimoire.TierOf(i);
                float size = t == Grimoire.Tier.Supremo ? 104f : t == Grimoire.Tier.Interno ? 74f : 60f;
                var n = new NodeUi { size = size };
                var go = new GameObject("Nodo" + i);
                go.transform.SetParent(bookRect, false);
                n.root = go.AddComponent<RectTransform>();
                n.root.anchorMin = n.root.anchorMax = new Vector2(0.5f, 0.5f);
                n.root.anchoredPosition = BookPoint(GrimoirePage.NodePos(i));
                n.root.sizeDelta = new Vector2(size, size);
                n.glow = MakeImage(n.root, "Bagliore", GrimoireIcons.Disc(), Vector2.zero, size * 1.65f, new Color(1f, 0.1f, 0.05f, 0f));
                n.medal = MakeImage(n.root, "Medaglione", GrimoireIcons.Medal(), Vector2.zero, size * 1.18f, new Color(0.55f, 0.33f, 0.16f, 1f));
                n.disc = MakeImage(n.root, "Disco", GrimoireIcons.Disc(), Vector2.zero, size * 0.98f, new Color(0.10f, 0.025f, 0.025f, 0.97f));
                n.icon = MakeImage(n.root, "Icona", null, Vector2.zero, size * 0.66f, Color.white);
                n.halo = MakeImage(n.root, "Scelto", GrimoireIcons.Halo(), Vector2.zero, size * 1.45f, new Color(1f, 0.82f, 0.45f, 0f));
                nodes[i] = n;
            }
            // titoli nei nastri
            var tl = GrimoirePage.TitleLeft; var tr = GrimoirePage.TitleRight;
            titleL = MakeText(bookRect, "TitoloSinistra", BookPoint(tl.center), new Vector2(tl.width * BookW * 0.92f, tl.height * BookH), 22, TextAnchor.MiddleCenter, InkC, FontStyle.BoldAndItalic);
            titleR = MakeText(bookRect, "TitoloDestra", BookPoint(tr.center), new Vector2(tr.width * BookW * 0.92f, tr.height * BookH), 22, TextAnchor.MiddleCenter, InkC, FontStyle.BoldAndItalic);
            // le tre classi di nodi
            tiers = MakeText(bookRect, "Classi", BookPoint(0.235f, 0.375f), new Vector2(0.34f * BookW, 0.33f * BookH), 14, TextAnchor.UpperLeft, InkC, FontStyle.Italic);
            tiers.lineSpacing = 1.05f;
            // la scheda del nodo scelto
            Vector2 card = BookPoint(0.235f, 0.715f);
            infoMedal = MakeImage(bookRect, "SchedaMedaglione", GrimoireIcons.Medal(), card + new Vector2(-0.125f * BookW, 0.035f * BookH), 92f, new Color(0.55f, 0.33f, 0.16f, 1f));
            infoDisc = MakeImage(bookRect, "SchedaDisco", GrimoireIcons.Disc(), card + new Vector2(-0.125f * BookW, 0.035f * BookH), 78f, new Color(0.10f, 0.025f, 0.025f, 0.97f));
            infoIcon = MakeImage(bookRect, "SchedaIcona", null, card + new Vector2(-0.125f * BookW, 0.035f * BookH), 52f, Color.white);
            infoName = MakeText(bookRect, "SchedaNome", card + new Vector2(0.035f * BookW, 0.075f * BookH), new Vector2(0.25f * BookW, 34f), 21, TextAnchor.MiddleLeft, RedInkC, FontStyle.Bold);
            infoTier = MakeText(bookRect, "SchedaClasse", card + new Vector2(0.035f * BookW, 0.035f * BookH), new Vector2(0.25f * BookW, 20f), 12, TextAnchor.MiddleLeft, Faded, FontStyle.Italic);
            infoEffect = MakeText(bookRect, "SchedaEffetto", card + new Vector2(0.035f * BookW, -0.025f * BookH), new Vector2(0.25f * BookW, 44f), 14, TextAnchor.UpperLeft, InkC, FontStyle.Normal);
            infoLore = MakeText(bookRect, "SchedaMotto", card + new Vector2(-0.01f * BookW, -0.085f * BookH), new Vector2(0.34f * BookW, 34f), 12, TextAnchor.UpperLeft, Faded, FontStyle.Italic);
            infoStatus = MakeText(bookRect, "SchedaStato", card + new Vector2(-0.01f * BookW, -0.13f * BookH), new Vector2(0.34f * BookW, 22f), 14, TextAnchor.MiddleLeft, RedInkC, FontStyle.Bold);
            infoAction = MakeText(bookRect, "SchedaAzione", card + new Vector2(-0.01f * BookW, -0.165f * BookH), new Vector2(0.34f * BookW, 20f), 12, TextAnchor.MiddleLeft, InkC, FontStyle.Italic);
            // piede: punti, essenza, comandi
            footer = MakeText(transform, "Piede", new Vector2(0f, -BookH * 0.5f - 2f), new Vector2(1100f, 24f), 14, TextAnchor.MiddleCenter, new Color(0.92f, 0.84f, 0.70f), FontStyle.Normal, false);
            var sh = footer.gameObject.AddComponent<Shadow>(); sh.effectColor = Color.black; sh.effectDistance = new Vector2(1.5f, -1.5f);
            footer.rectTransform.anchoredPosition = new Vector2(0f, -BookH * 0.5f + 10f);
            toast = MakeText(bookRect, "Avviso", BookPoint(0.235f, 0.905f), new Vector2(0.40f * BookW, 40f), 14, TextAnchor.MiddleCenter, RedInkC, FontStyle.BoldAndItalic);
        }

        static Image Segment(Transform parent, string name, Vector2 a, Vector2 b, float w)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = a;
            Vector2 d = b - a;
            rt.sizeDelta = new Vector2(d.magnitude, w);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var img = go.AddComponent<Image>();
            img.sprite = PerkIcons.White(); img.raycastTarget = false;
            return img;
        }

        // ------------------------------------------------------------------ apertura e chiusura
        void OpenBook(Grimoire.Book b, GrimoireAltar a)
        {
            book = b; altar = a; reader = Seats.Current;
            canvas.targetDisplay = reader != null ? reader.display : 0;          // su piu' monitor il libro si apre sul monitor di chi legge
            spread.texture = GrimoirePage.Spread(b);
            string t = Grimoire.Title(b), sub = Grimoire.Subtitle(b);
            titleL.text = b == Grimoire.Book.Stregoneria ? "Perk del Dodecaedro\nArcano Maledetto" : t;
            titleR.text = b == Grimoire.Book.Stregoneria ? "Le 19 Virtù\ndella Stregoneria" : "I 19 Sigilli\ndell'Arsenale Maledetto";
            var ns = Grimoire.Of(b);
            for (int i = 0; i < Grimoire.Count; i++) nodes[i].icon.sprite = GrimoireIcons.Get(ns[i].icon);
            // la prima scelta: il primo nodo che puoi comprare, o la magia attiva
            var s = Magic.State;
            int active = b == Grimoire.Book.Stregoneria ? s.SelectedSpell : s.SelectedInfusion;
            sel = active >= 0 ? active : FirstBuyable(s);
            visible = true; openT = 0f; toastT = 0f;
            canvas.enabled = true;
            GameMenu.Overlay = true;
            lastMouse = Input.mousePosition;
            lastHp = GameManager.I != null && GameManager.I.player != null ? GameManager.I.player.hp : 0f;
            Sfx.UI(SoundBank.GrimorioApre, 0.75f);
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
            canvas.enabled = false;
            GameMenu.Overlay = false;
            if (!GameManager.autoTest) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
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
            group.alpha = k;
            bookRect.localScale = Vector3.one * Mathf.Lerp(0.95f, 1f, 1f - (1f - k) * (1f - k));
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            // ferito o morto (non dovrebbe succedere nel sacrario): il libro si chiude
            if (pl == null || !pl.alive || pl.hp < lastHp - 0.5f) { Close(); return; }
            lastHp = pl.hp;
            if (GameMenu.InMenu || GameMenu.Paused) { Close(); return; }

            // mouse: il nodo sotto il puntatore
            Vector3 mouse = Input.mousePosition;
            int hover = -1;
            if (Seats.Keyboard && RectTransformUtility.ScreenPointToLocalPointInRectangle(bookRect, mouse, null, out Vector2 local))
            {
                float best = float.MaxValue;
                for (int i = 0; i < Grimoire.Count; i++)
                {
                    float d = (nodes[i].root.anchoredPosition - local).magnitude;
                    if (d < nodes[i].size * 0.6f && d < best) { best = d; hover = i; }
                }
            }
            if ((mouse - lastMouse).sqrMagnitude > 4f && hover >= 0 && hover != sel) { sel = hover; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); }
            lastMouse = mouse;
            if (SeatInput.GetMouseButtonDown(0) && hover >= 0) { sel = hover; Activate(sel); }

            // tastiera e controller
            int nx = PadControls.NavX, ny = PadControls.NavY;
            Vector2 nav = Vector2.zero;
            if (SeatInput.GetKeyDown(KeyCode.LeftArrow) || SeatInput.GetKeyDown(KeyCode.A) || (nx < 0 && lastNavX >= 0)) nav = Vector2.left;
            else if (SeatInput.GetKeyDown(KeyCode.RightArrow) || SeatInput.GetKeyDown(KeyCode.D) || (nx > 0 && lastNavX <= 0)) nav = Vector2.right;
            else if (SeatInput.GetKeyDown(KeyCode.UpArrow) || SeatInput.GetKeyDown(KeyCode.W) || (ny > 0 && lastNavY <= 0)) nav = Vector2.up;
            else if (SeatInput.GetKeyDown(KeyCode.DownArrow) || SeatInput.GetKeyDown(KeyCode.S) || (ny < 0 && lastNavY >= 0)) nav = Vector2.down;
            lastNavX = nx; lastNavY = ny;
            if (nav != Vector2.zero) Navigate(nav);
            if (SeatInput.GetKeyDown(KeyCode.Return) || SeatInput.GetKeyDown(KeyCode.KeypadEnter) || SeatInput.GetKeyDown(KeyCode.Space) || (openT > 0.3f && SeatInput.GetKeyDown(KeyCode.F)) || PadControls.Confirm) Activate(sel);
            if (SeatInput.GetKeyDown(KeyCode.Escape) || SeatInput.GetKeyDown(KeyCode.Tab) || SeatInput.GetKeyDown(KeyCode.Backspace) || PadControls.Cancel || TouchControls.Down(TouchControls.Action.Pause)) { Close(); return; }

            if (toastT > 0f) { toastT -= dt; toast.color = new Color(RedInkC.r, RedInkC.g, RedInkC.b, Mathf.Clamp01(toastT / 0.6f)); if (toastT <= 0f) toast.text = ""; }
            Refresh();
        }

        /// <summary>Il nodo piu' vicino nella direzione data (entro 70 gradi), pesando la distanza.</summary>
        void Navigate(Vector2 dir)
        {
            Vector2 from = nodes[sel].root.anchoredPosition;
            int best = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < Grimoire.Count; i++)
            {
                if (i == sel) continue;
                Vector2 d = nodes[i].root.anchoredPosition - from;
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
            if (have < price) { Say($"Servono {price} punti: ti mancano {price - have}."); Sfx.UI(SoundBank.BookNo, 0.5f); return false; }
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

        void Say(string s) { toast.text = s; toastT = 3.2f; }

        // ------------------------------------------------------------------ disegno
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        void Refresh()
        {
            var s = Magic.State;
            uint mask = s.Mask(book);
            float t = Time.unscaledTime;
            int activeSel = book == Grimoire.Book.Stregoneria ? s.SelectedSpell : s.SelectedInfusion;
            for (int i = 0; i < Grimoire.Count; i++)
            {
                var n = nodes[i];
                var st = s.Status(book, i);
                bool owned = st == Grimoire.Lock.Posseduto, free = st == Grimoire.Lock.Libero, isSel = i == sel;
                n.pop = Mathf.Min(1f, n.pop + Time.unscaledDeltaTime / 0.5f);
                float popK = n.pop < 1f ? 1f + 0.45f * Mathf.Sin((1f - n.pop) * Mathf.PI) : 1f;
                n.root.localScale = Vector3.one * (popK * (isSel ? 1.08f : 1f));
                n.icon.color = owned ? new Color(1f, 0.90f, 0.74f) : free ? new Color(0.86f, 0.74f, 0.56f) : new Color(0.42f, 0.33f, 0.26f, 0.85f);
                n.medal.color = owned ? new Color(0.78f, 0.22f, 0.12f) : free ? new Color(0.60f, 0.38f, 0.18f) : new Color(0.30f, 0.20f, 0.13f);
                float breathe = 0.5f + 0.5f * Mathf.Sin(t * 2.2f + i);
                n.glow.color = owned ? new Color(1f, 0.12f, 0.06f, (i == activeSel ? 0.55f : 0.32f) + 0.12f * breathe) : new Color(1f, 0.12f, 0.06f, 0f);
                n.halo.color = isSel ? new Color(1f, 0.85f, 0.5f, 0.55f + 0.35f * breathe) : (i == activeSel ? new Color(1f, 0.25f, 0.12f, 0.6f) : new Color(1f, 0.85f, 0.5f, 0f));
            }
            foreach (var L in lines)
            {
                bool oa = Grimoire.Has(mask, L.a), ob = Grimoire.Has(mask, L.b);
                bool fa = s.Status(book, L.a) == Grimoire.Lock.Libero, fb = s.Status(book, L.b) == Grimoire.Lock.Libero;
                Color c = oa && ob ? new Color(0.95f, 0.16f, 0.10f, 0.95f) : (oa && fb) || (ob && fa) ? new Color(0.80f, 0.48f, 0.16f, 0.8f) : new Color(0.30f, 0.12f, 0.07f, 0.55f);
                L.line.color = c;
                L.glow.color = oa && ob ? new Color(1f, 0.1f, 0.05f, 0.28f + 0.08f * Mathf.Sin(t * 2f + L.a)) : new Color(0f, 0f, 0f, 0f);
            }
            // le tre classi
            int no = Grimoire.CountTier(mask, Grimoire.Tier.Esterno), ni = Grimoire.CountTier(mask, Grimoire.Tier.Interno), nsu = Grimoire.CountTier(mask, Grimoire.Tier.Supremo);
            string red = Hex(RedInkC);
            tiers.text =
                $"<b><color=#{red}>· {Grimoire.TierName(book, Grimoire.Tier.Esterno)} (12)</color></b>   <size=11>{no} tuoi</size>\n{Grimoire.TierText(book, Grimoire.Tier.Esterno)}\n\n" +
                $"<b><color=#{red}>· {Grimoire.TierName(book, Grimoire.Tier.Interno)} (6)</color></b>   <size=11>{ni} tuoi</size>\n{Grimoire.TierText(book, Grimoire.Tier.Interno)}\n\n" +
                $"<b><color=#{red}>· {Grimoire.TierName(book, Grimoire.Tier.Supremo)} (1)</color></b>   <size=11>{(nsu > 0 ? "tuo" : "")}</size>\n{Grimoire.TierText(book, Grimoire.Tier.Supremo)}";
            // la scheda
            var node = Grimoire.Of(book)[sel];
            var sst = s.Status(book, sel);
            infoIcon.sprite = GrimoireIcons.Get(node.icon);
            infoIcon.color = sst == Grimoire.Lock.Posseduto ? new Color(1f, 0.90f, 0.74f) : new Color(0.86f, 0.74f, 0.56f);
            infoMedal.color = sst == Grimoire.Lock.Posseduto ? new Color(0.78f, 0.22f, 0.12f) : new Color(0.60f, 0.38f, 0.18f);
            infoName.text = node.name;
            infoTier.text = Grimoire.TierName(book, node.tier) + (node.tier == Grimoire.Tier.Interno ? (book == Grimoire.Book.Stregoneria ? " — una magia della mano sinistra" : " — un sigillo da tracciare sull'arma") : "");
            infoEffect.text = char.ToUpper(node.effect[0]) + node.effect.Substring(1) + ".";
            infoLore.text = "«" + node.lore + "»";
            int price = s.Price(book, sel);
            bool active = sel == activeSel;
            switch (sst)
            {
                case Grimoire.Lock.Posseduto:
                    infoStatus.text = active ? "Già tuo — è quello attivo" : "Già tuo";
                    infoAction.text = sel >= Grimoire.FirstInner && !active ? "[Clic / F / A] rendilo attivo" : "";
                    break;
                case Grimoire.Lock.Libero:
                    int have = GameManager.I != null ? GameManager.I.score : 0;
                    infoStatus.text = have >= price ? $"Libero — {price:N0} punti".Replace(',', '.') : $"Libero — {price:N0} punti (ti mancano {price - have:N0})".Replace(',', '.');
                    infoAction.text = "[Clic / F / A] acquista";
                    break;
                default:
                    infoStatus.text = "Chiuso — " + Grimoire.LockText(book, mask, sel, sst);
                    infoAction.text = "";
                    break;
            }
            var pl = GameManager.I != null ? GameManager.I.player : null;
            int score = GameManager.I != null ? GameManager.I.score : 0;
            footer.text = $"PUNTI {score:N0}".Replace(',', '.') + (pl != null ? $"     ESSENZA {Mathf.FloorToInt(pl.Arcana.Essence)}/{Mathf.FloorToInt(pl.Arcana.EssenceMax)}" : "") +
                          (PadControls.Preferred ? "     croce: scegli   A: acquista   B: chiudi" : "     frecce o mouse: scegli   Invio o clic: acquista   Esc: chiudi");
        }

        /// <summary>Per le prove: il nodo scelto e il libro aperto.</summary>
        public int Selected { get => sel; set => sel = Mathf.Clamp(value, 0, Grimoire.Count - 1); }
        public Grimoire.Book BookShown => book;
    }
}

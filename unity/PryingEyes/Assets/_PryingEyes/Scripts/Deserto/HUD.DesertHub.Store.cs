using System.Collections.Generic;
using PryingEyes.Deserto;
using PryingEyes.Pista;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    public partial class HUD
    {
        // i colori del sito: carta chiara e inchiostro scuro (la pagina e' un sito, non una tavola del gioco), arancione di Bozzo
        static readonly Color SiteChrome = new Color(0.15f, 0.145f, 0.14f), SiteField = new Color(0.255f, 0.245f, 0.24f), SitePaper = new Color(0.945f, 0.925f, 0.88f),
            SitePaperDeep = new Color(0.885f, 0.855f, 0.79f), SiteInk = new Color(0.13f, 0.11f, 0.09f), SiteDim = new Color(0.43f, 0.39f, 0.33f), SiteOrange = new Color(0.95f, 0.43f, 0.10f),
            SiteOrangeDeep = new Color(0.72f, 0.30f, 0.06f), SiteHead = new Color(0.10f, 0.10f, 0.11f), SiteGreen = new Color(0.20f, 0.52f, 0.27f), SiteGrey = new Color(0.62f, 0.59f, 0.53f),
            SiteTrack = new Color(0.80f, 0.76f, 0.68f), SiteCream = new Color(0.95f, 0.92f, 0.84f);

        /// <summary>Un testo del sito su fondo chiaro: senza l'ombra nera dei testi del gioco.</summary>
        static Text SiteText(Transform parent, string name, float x, float y, float w, float h, int fs, TextAnchor a, Color c, FontStyle st, bool serif = false)
        {
            var t = HubText(parent, name, x, y, w, h, fs, a, c, st, serif);
            var sh = t.GetComponent<Shadow>();
            if (sh != null) sh.enabled = false;
            return t;
        }

        /// <summary>La pagina "Bozzo Moto Store": un sito inventato dentro al gioco (la barra del programma con l'indirizzo, la testata con la marca e i punti, il catalogo a sinistra e la scheda della moto a destra) dove
        /// si comprano, coi punti di chi gioca, le moto del garage del deserto: le nove della pista, ognuna con la sua foto (la vetrina della pista, GarageStage, che la fa girare su un piatto, senza pilota), i suoi
        /// numeri di guida (DesertBikeDef) e il suo prezzo. L'acquisto e' in due tempi (un "sei sicuro?" con Conferma e Annulla) e finisce in una ricevuta: la moto e' sua per sempre, resta nel personaggio
        /// (DesertGarage) e nasce subito nella sua piazzola del garage all'aperto accanto alla scalinata (DesertBikes). La moto di casa, Ruggine, non si vende: e' gia' di tutti.</summary>
        public sealed class HubStorePage : HubPage
        {
            sealed class Item { public DesertBikeDef def; public RectTransform root; public Image back, bar, dot; public Text name, klass, price, tag; }

            const float ListX = 14f, ListY = 140f, ListW = 288f, ListH = 348f, RowH = 58f;
            const float PhotoX = 318f, PhotoY = 124f, PhotoW = 420f, PhotoH = 280f, InfoX = 756f, InfoW = 258f;
            const float BodyY = 112f, BodyH = HubContentH - BodyY;
            static readonly Rect BuyRect = new Rect(InfoX, 394f, InfoW, 46f);
            static readonly Rect YesRect = new Rect(278f, 296f, 228f, 44f), NoRect = new Rect(522f, 296f, 228f, 44f);

            readonly List<Item> items = new List<Item>();
            RectTransform listRoot, dialog;
            RawImage photo;
            Text urlText, pointsValue, nameText, klassText, priceText, specsText, blurbText, noteText, buyText, ownedCount;
            readonly Image[] barFill = new Image[4];
            readonly Text[] barValue = new Text[4];
            Image buyBack, shade, yesBack, noBack;
            Text dlgTitle, dlgBody, dlgHint, yesText, noText;
            GarageStage stage;
            int sel, phase;                     // phase: 0 il catalogo, 1 "sei sicuro?", 2 la ricevuta
            float scroll, dialogT;
            bool yes = true, dirty = true;
            int cachedBay;
            string receipt = "";
            int orderNo;
            static int orders;

            public HubStorePage() { label = "Bozzo Moto Store"; }

            public override bool CapturesBack => phase != 0;

            public override string Hint => phase == 0
                ? "Su e giù: scegli la moto     Sinistra e destra: gira la moto     Invio  A  clic: compra     Q E  LB RB: pagina     Esc  B  Select: chiudi"
                : phase == 1 ? "Sinistra e destra: scegli     Invio  A: conferma     Esc  B: annulla" : "Invio  A  Esc  B: chiudi la ricevuta";

            /// <summary>Per le prove: la moto scelta nel catalogo, se c'e' una domanda aperta e se l'ordine e' stato fatto.</summary>
            public DesertBikeDef Current => sel >= 0 && sel < items.Count ? items[sel].def : null;
            public int Phase => phase;
            public int Count => items.Count;
            public GarageStage Stage => stage;
            public void Choose(int i) { sel = Mathf.Clamp(i, 0, Mathf.Max(0, items.Count - 1)); dirty = true; }

            public override void Build(HUD h, RectTransform parent)
            {
                // la barra del programma: tre tondi e l'indirizzo
                HubRect(parent, "Barra", 0f, 0f, HubContentW, 30f, SiteChrome);
                var circle = CircleSprite();
                Img(parent, "Rosso", new Vector2(14f, -10f), new Vector2(11f, 11f), new Color(0.92f, 0.34f, 0.30f), circle);
                Img(parent, "Giallo", new Vector2(32f, -10f), new Vector2(11f, 11f), new Color(0.95f, 0.76f, 0.26f), circle);
                Img(parent, "Verde", new Vector2(50f, -10f), new Vector2(11f, 11f), new Color(0.35f, 0.76f, 0.38f), circle);
                HubRect(parent, "Indirizzo", 78f, 5f, HubContentW - 94f, 20f, SiteField);
                Img(parent, "Lucchetto", new Vector2(90f, -11f), new Vector2(8f, 8f), HubGreenC, circle);
                urlText = HubText(parent, "IndirizzoTesto", 106f, 5f, HubContentW - 130f, 20f, 12, TextAnchor.MiddleLeft, new Color(0.80f, 0.78f, 0.74f), FontStyle.Normal);
                urlText.supportRichText = false;

                // il sito
                HubRect(parent, "Sito", 0f, 30f, HubContentW, HubContentH - 30f, SitePaper);
                HubRect(parent, "Testata", 0f, 30f, HubContentW, 60f, SiteHead);
                Img(parent, "LogoRuota", new Vector2(16f, -42f), new Vector2(36f, 36f), SiteOrange, circle);
                Img(parent, "LogoGomma", new Vector2(21f, -47f), new Vector2(26f, 26f), SiteHead, circle);
                Img(parent, "LogoMozzo", new Vector2(30f, -56f), new Vector2(8f, 8f), SiteOrange, circle);
                var brand = HubText(parent, "Marca", 62f, 38f, 360f, 32f, 26, TextAnchor.MiddleLeft, SiteCream, FontStyle.Bold, true);
                brand.text = "<color=#F26E1A>BOZZO</color> MOTO STORE";
                var tag = HubText(parent, "Slogan", 64f, 68f, 400f, 16f, 11, TextAnchor.MiddleLeft, new Color(0.72f, 0.69f, 0.62f), FontStyle.Normal);
                tag.text = "Le moto del sagrato · consegna immediata al garage"; tag.supportRichText = false;
                var links = HubText(parent, "Menu", 520f, 50f, 330f, 20f, 13, TextAnchor.MiddleRight, new Color(0.85f, 0.82f, 0.74f), FontStyle.Normal);
                links.text = "<b><color=#F26E1A>Catalogo</color></b>     Garage     Offerte     Assistenza";
                HubRect(parent, "PuntiSfondo", 868f, 44f, 146f, 34f, SiteOrange);
                var pl = HubText(parent, "PuntiEtichetta", 876f, 45f, 130f, 13f, 9, TextAnchor.MiddleLeft, new Color(1f, 0.90f, 0.78f), FontStyle.Bold);
                pl.text = "I TUOI PUNTI"; pl.supportRichText = false;
                pointsValue = HubText(parent, "PuntiValore", 876f, 57f, 130f, 20f, 18, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
                pointsValue.supportRichText = false;
                // la striscia delle offerte
                HubRect(parent, "Striscia", 0f, 90f, HubContentW, 22f, SiteOrangeDeep);
                var strip = HubText(parent, "StrisciaTesto", 0f, 90f, HubContentW, 22f, 11, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                strip.text = "CONSEGNA GRATUITA AL GARAGE DEL SAGRATO   ·   SI PAGA SOLO IN PUNTI   ·   NIENTE RESI: LA SABBIA NON PERDONA";
                strip.supportRichText = false;

                // il catalogo
                var cat = SiteText(parent, "CatalogoTitolo", ListX, BodyY + 6f, 180f, 20f, 13, TextAnchor.MiddleLeft, SiteOrangeDeep, FontStyle.Bold);
                cat.text = "CATALOGO"; cat.supportRichText = false;
                ownedCount = SiteText(parent, "CatalogoConto", ListX + 150f, BodyY + 6f, ListW - 150f, 20f, 11, TextAnchor.MiddleRight, SiteDim, FontStyle.Normal);
                HubRect(parent, "CatalogoFilo", ListX, ListY - 2f, ListW, 1.5f, SiteOrange);
                var view = Box(parent, "CatalogoVista", new Vector2(ListX, -ListY), new Vector2(ListW, ListH));
                view.gameObject.AddComponent<RectMask2D>();
                listRoot = Box(view, "Righe", Vector2.zero, new Vector2(ListW, 100f));
                foreach (var d in DesertBikeDefs.All)
                {
                    if (d.Free) continue;
                    var it = new Item { def = d };
                    float y = items.Count * RowH;
                    it.root = Box(listRoot, "Moto " + d.name, new Vector2(0f, -y), new Vector2(ListW, RowH - 4f));
                    it.back = Img(it.root, "Fondo", Vector2.zero, new Vector2(ListW, RowH - 4f), SitePaperDeep, null);
                    it.bar = Img(it.root, "Barra", Vector2.zero, new Vector2(4f, RowH - 4f), SiteOrange, null);
                    it.dot = Img(it.root, "Livrea", new Vector2(14f, -(RowH - 4f - 16f) * 0.5f), new Vector2(16f, 16f), Livery.For(d.skin).main, circle);
                    it.name = SiteText(it.root, "Nome", 40f, 6f, 160f, 24f, 16, TextAnchor.MiddleLeft, SiteInk, FontStyle.Bold);
                    it.klass = SiteText(it.root, "Classe", 40f, 30f, 160f, 18f, 11, TextAnchor.MiddleLeft, SiteDim, FontStyle.Normal);
                    it.price = SiteText(it.root, "Prezzo", 150f, 6f, ListW - 160f, 24f, 15, TextAnchor.MiddleRight, SiteOrangeDeep, FontStyle.Bold);
                    it.tag = SiteText(it.root, "Stato", 150f, 30f, ListW - 160f, 18f, 10, TextAnchor.MiddleRight, SiteGreen, FontStyle.Bold);
                    it.name.text = d.name; it.klass.text = d.klass; it.name.supportRichText = false; it.klass.supportRichText = false;
                    items.Add(it);
                }
                listRoot.sizeDelta = new Vector2(ListW, items.Count * RowH);

                // la foto: la vetrina del garage, senza pilota
                HubRect(parent, "FotoBordo", PhotoX - 3f, PhotoY - 3f, PhotoW + 6f, PhotoH + 6f, SiteInk);
                photo = Box(parent, "Foto", new Vector2(PhotoX, -PhotoY), new Vector2(PhotoW, PhotoH)).gameObject.AddComponent<RawImage>();
                photo.color = new Color(1f, 1f, 1f, 1f); photo.raycastTarget = false;
                var cap = SiteText(parent, "FotoDidascalia", PhotoX, PhotoY + PhotoH + 4f, PhotoW, 14f, 10, TextAnchor.MiddleLeft, SiteDim, FontStyle.Italic);
                cap.text = "Foto dello studio Bozzo: la moto gira da sola. Sinistra e destra la fanno girare a mano."; cap.supportRichText = false;
                blurbText = SiteText(parent, "Descrizione", PhotoX, PhotoY + PhotoH + 24f, PhotoW, 70f, 14, TextAnchor.UpperLeft, SiteInk, FontStyle.Normal);

                // la scheda
                nameText = SiteText(parent, "Nome", InfoX, PhotoY - 4f, InfoW, 36f, 28, TextAnchor.MiddleLeft, SiteInk, FontStyle.Bold, true);
                nameText.supportRichText = false;
                klassText = SiteText(parent, "Classe", InfoX, PhotoY + 32f, InfoW, 18f, 12, TextAnchor.MiddleLeft, SiteOrangeDeep, FontStyle.Bold);
                klassText.supportRichText = false;
                priceText = SiteText(parent, "Prezzo", InfoX, PhotoY + 52f, InfoW, 44f, 34, TextAnchor.MiddleLeft, SiteOrangeDeep, FontStyle.Bold);
                for (int i = 0; i < 4; i++)
                {
                    float y = PhotoY + 106f + i * 30f;
                    var l = SiteText(parent, "BarraNome" + i, InfoX, y, 150f, 16f, 11, TextAnchor.MiddleLeft, SiteDim, FontStyle.Bold);
                    l.text = DesertBikeDef.BarNames[i].ToUpperInvariant().Replace("A'", "À"); l.supportRichText = false;
                    barValue[i] = SiteText(parent, "BarraValore" + i, InfoX + 120f, y, InfoW - 120f, 16f, 11, TextAnchor.MiddleRight, SiteInk, FontStyle.Bold);
                    barValue[i].supportRichText = false;
                    HubRect(parent, "BarraFondo" + i, InfoX, y + 17f, InfoW, 7f, SiteTrack);
                    barFill[i] = HubRect(parent, "BarraPiena" + i, InfoX, y + 17f, 10f, 7f, SiteOrange);
                }
                specsText = SiteText(parent, "Dati", InfoX, PhotoY + 232f, InfoW, 36f, 11, TextAnchor.UpperLeft, SiteDim, FontStyle.Normal);
                buyBack = HubRect(parent, "Compra", BuyRect.x, BuyRect.y, BuyRect.width, BuyRect.height, SiteOrange);
                buyText = HubText(parent, "CompraTesto", BuyRect.x, BuyRect.y, BuyRect.width, BuyRect.height, 15, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                buyText.supportRichText = false;
                noteText = SiteText(parent, "Nota", InfoX, BuyRect.y + BuyRect.height + 4f, InfoW, 30f, 10, TextAnchor.UpperLeft, SiteDim, FontStyle.Normal);

                // la domanda e la ricevuta: un velo sul sito e un riquadro
                shade = HubRect(parent, "Velo", 0f, BodyY, HubContentW, BodyH, new Color(0f, 0f, 0f, 0.58f));
                dialog = Box(parent, "Riquadro", new Vector2(254f, -150f), new Vector2(520f, 214f));
                Img(dialog, "Fondo", Vector2.zero, new Vector2(520f, 214f), SitePaper, null);
                Img(dialog, "Testa", Vector2.zero, new Vector2(520f, 8f), SiteOrange, null);
                dlgTitle = SiteText(dialog, "Titolo", 24f, 20f, 472f, 32f, 24, TextAnchor.MiddleLeft, SiteInk, FontStyle.Bold, true);
                dlgBody = SiteText(dialog, "Corpo", 24f, 58f, 472f, 84f, 15, TextAnchor.UpperLeft, SiteInk, FontStyle.Normal);
                yesBack = HubRect(parent, "Si", YesRect.x, YesRect.y, YesRect.width, YesRect.height, SiteOrange);
                noBack = HubRect(parent, "No", NoRect.x, NoRect.y, NoRect.width, NoRect.height, SiteGrey);
                yesText = HubText(parent, "SiTesto", YesRect.x, YesRect.y, YesRect.width, YesRect.height, 15, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                noText = HubText(parent, "NoTesto", NoRect.x, NoRect.y, NoRect.width, NoRect.height, 15, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                yesText.supportRichText = false; noText.supportRichText = false;
                dlgHint = SiteText(dialog, "Aiuto", 24f, 190f, 472f, 18f, 10, TextAnchor.MiddleLeft, SiteDim, FontStyle.Normal);
                dlgHint.supportRichText = false;
                SetDialogVisible(false);
            }

            void SetDialogVisible(bool on)
            {
                shade.gameObject.SetActive(on); dialog.gameObject.SetActive(on);
                bool two = on && phase == 1;
                yesBack.gameObject.SetActive(on); yesText.gameObject.SetActive(on);
                noBack.gameObject.SetActive(two); noText.gameObject.SetActive(two);
            }

            public override void OnShow(HUD h)
            {
                phase = 0; dirty = true; dialogT = 0f;
                SetDialogVisible(false);
                // senza scheda grafica (le prove con -nographics) la vetrina non si puo' disegnare: il catalogo e i numeri restano, la foto no
                if (stage == null && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    try { stage = GarageStage.Create(60f * (Seats.SeatIndex + 1)); }
                    catch (System.Exception e) { Debug.LogException(e); stage = null; }
                }
                if (stage != null) { stage.SetActive(true); photo.texture = stage.Picture; }
                scroll = Mathf.Clamp(sel * RowH - ListH * 0.5f + RowH * 0.5f, 0f, Mathf.Max(0f, items.Count * RowH - ListH));
            }

            public override void OnHide(HUD h)
            {
                phase = 0;
                SetDialogVisible(false);
                if (stage != null) stage.SetActive(false);
            }

            public override void Dispose(HUD h)
            {
                if (stage != null) { Object.Destroy(stage.gameObject); stage = null; }
                if (photo != null) photo.texture = null;
            }

            int BayNumber(int seat, DesertBikeDef d)
            {
                int bay = DesertGarage.BayOf(seat, d);
                if (bay >= 0) return bay + 1;
                int di = DesertBikeDefs.IndexOf(d), n = 0;
                foreach (var o in DesertGarage.Owned(seat)) if (DesertBikeDefs.IndexOf(o) < di) n++;
                return n + 1;
            }

            public override void Tick(HUD h, in HubInput inp, float dt)
            {
                var gm = GameManager.I;
                int have = gm != null ? gm.score : 0;
                int seat = Seats.SeatIndex;
                var d = Current;
                if (stage != null && d != null)
                {
                    stage.Show(d.pista, d.skin, -1, false);
                    stage.Tick(dt);
                }
                if (phase == 0) TickCatalogue(h, inp, have, seat, dt);
                else TickDialog(h, inp, have, seat, dt);
                Refresh(have, seat, dt);
                if (phase != 0) PaintDialog();
            }

            void TickCatalogue(HUD h, in HubInput inp, int have, int seat, float dt)
            {
                int hover = -1;
                bool onBuy = false;
                if (inp.pointerIn)
                {
                    Vector2 q = inp.Local;
                    if (q.x >= ListX && q.x <= ListX + ListW && q.y >= ListY && q.y <= ListY + ListH)
                    {
                        int idx = Mathf.FloorToInt((q.y - ListY + scroll) / RowH);
                        if (idx >= 0 && idx < items.Count) hover = idx;
                    }
                    onBuy = BuyRect.Contains(q);
                }
                int before = sel;
                if (inp.moved && hover >= 0) sel = hover;
                if (inp.click && hover >= 0) sel = hover;
                if (inp.wheel != 0f && inp.pointerIn) sel = Mathf.Clamp(sel + (inp.wheel < 0f ? 1 : -1), 0, items.Count - 1);
                if (inp.ny != 0) sel = Mathf.Clamp(sel - inp.ny, 0, items.Count - 1);
                if (sel != before) { dirty = true; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); }
                if (inp.nx != 0 && stage != null) stage.SetSpin(stage.Spin + inp.nx * 40f);
                if (inp.confirm || (inp.click && onBuy)) Ask(h, have, seat);
            }

            /// <summary>Compra: se e' gia' sua o non ha i punti lo dice, altrimenti apre il "sei sicuro?".</summary>
            void Ask(HUD h, int have, int seat)
            {
                var d = Current;
                if (d == null) return;
                if (DesertGarage.Owns(seat, d)) { h.HubSay("La " + d.name + " è già nel tuo garage, piazzola " + BayNumber(seat, d) + ".", false); Sfx.UI(SoundBank.BookNo, 0.4f); return; }
                if (have < d.price) { h.HubSay("Per la " + d.name + " servono ancora " + HubNum(d.price - have) + " punti.", true); Sfx.UI(SoundBank.BookNo, 0.5f); return; }
                phase = 1; dialogT = 0f; yes = true;
                dlgTitle.text = "Confermi l'ordine?";
                dlgBody.text = "Bozzo Moto Store ti consegna la <b>" + d.name + "</b> nel garage del sagrato, piazzola " + BayNumber(seat, d) + ".\n\nCosto: <b>" + HubNum(d.price) + " punti</b>. Dopo l'acquisto te ne restano <b>" + HubNum(have - d.price) + "</b>.";
                yesText.text = "CONFERMA"; noText.text = "ANNULLA";
                SetDialogVisible(true);
                Sfx.UI(SoundBank.MagiaScelta, 0.35f, 1.1f);
            }

            void TickDialog(HUD h, in HubInput inp, int have, int seat, float dt)
            {
                dialogT += dt;
                if (phase == 2)
                {
                    if (inp.confirm || inp.back || inp.click) { phase = 0; SetDialogVisible(false); }
                    return;
                }
                bool go = false;
                if (inp.nx != 0) { yes = inp.nx < 0; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); }
                if (inp.pointerIn)
                {
                    Vector2 q = inp.Local;
                    if (inp.moved) { if (YesRect.Contains(q)) yes = true; else if (NoRect.Contains(q)) yes = false; }
                    if (inp.click) { if (YesRect.Contains(q)) { yes = true; go = true; } else if (NoRect.Contains(q)) { yes = false; go = true; } }
                }
                if (inp.confirm) go = true;
                if (go && yes && dialogT < 0.35f) go = false;                      // un invio di troppo, ancora in mano dalla pagina di prima, non compra
                if (inp.back || (go && !yes)) { phase = 0; SetDialogVisible(false); h.HubSay("Ordine annullato.", false); return; }
                if (go) Buy(h, have, seat);
            }

            void Buy(HUD h, int have, int seat)
            {
                var d = Current;
                if (d == null) { phase = 0; SetDialogVisible(false); return; }
                var r = DesertGarage.TryBuy(seat, d, out int missing);
                dirty = true;
                switch (r)
                {
                    case DesertGarage.Result.Bought:
                    {
                        orderNo = 2400 + (++orders) * 17 + DesertBikeDefs.IndexOf(d) * 31;
                        phase = 2; dialogT = 0f;
                        dlgTitle.text = "Ordine confermato!";
                        dlgBody.text = "La tua <b>" + d.name + "</b> ti aspetta nel garage del sagrato, piazzola " + BayNumber(seat, d) + ", col muso verso il deserto. Avvicinati e premi <b>F</b> per salirci.\n\n<color=#6E6454>Ordine BM-" + orderNo + "  ·  pagato " + HubNum(d.price) + " punti</color>";
                        yesText.text = "CONTINUA";
                        SetDialogVisible(true);
                        Sfx.UI(SoundBank.GrimorioNodo, 0.85f);
                        h.HubSay("Comprata: " + d.name + ". È nel garage.", false);
                        h.Flash(new Color(0.95f, 0.43f, 0.10f), 0.4f, 0.3f);
                        break;
                    }
                    case DesertGarage.Result.NotEnoughPoints:
                        phase = 0; SetDialogVisible(false);
                        h.HubSay("Servono ancora " + HubNum(missing) + " punti.", true); Sfx.UI(SoundBank.BookNo, 0.5f);
                        break;
                    case DesertGarage.Result.AlreadyOwned:
                        phase = 0; SetDialogVisible(false);
                        h.HubSay("La " + d.name + " è già tua.", false);
                        break;
                    default:
                        phase = 0; SetDialogVisible(false);
                        h.HubSay("L'ordine non è andato a buon fine.", true); Sfx.UI(SoundBank.BookNo, 0.5f);
                        break;
                }
            }

            void Refresh(int have, int seat, float dt)
            {
                pointsValue.text = HubNum(have);
                var d = Current;
                if (d == null) return;
                if (dirty)
                {
                    dirty = false;
                    cachedBay = BayNumber(seat, d);
                    urlText.text = "https://www.bozzomoto.store/negozio/" + d.id;
                    nameText.text = d.name;
                    klassText.text = d.klass.ToUpperInvariant();
                    blurbText.text = d.tagline;
                    float kmh = d.top * 3.6f, boost = d.boost * 3.6f;
                    barValue[0].text = Mathf.RoundToInt(kmh) + " km/h";
                    barValue[1].text = "x" + HubDecimal2(d.accel);
                    barValue[2].text = "x" + HubDecimal2(d.turn);
                    barValue[3].text = "x" + HubDecimal2(d.brake);
                    for (int i = 0; i < 4; i++) HubSetMeter(barFill[i], InfoW, Mathf.Max(0.04f, d.Bar(i)));
                    specsText.text = (d.hp > 0f ? Mathf.RoundToInt(d.hp) + " CV  ·  " + Mathf.RoundToInt(d.kg) + " kg  ·  " : "") + "turbo " + Mathf.RoundToInt(boost) + " km/h\nmotore: " + DesertBikeDefs.EngineWords(d.engine);
                }
                bool owned = DesertGarage.Owns(seat, d);
                bool can = have >= d.price;
                priceText.text = HubNum(d.price) + "  <size=14>punti</size>";
                buyText.text = owned ? "NEL TUO GARAGE" : can ? "COMPRA   " + HubNum(d.price) + " punti" : "SERVONO " + HubNum(d.price - have) + " PUNTI IN PIÙ";
                buyBack.color = owned ? SiteGreen : can ? SiteOrange : SiteGrey;
                noteText.text = owned ? "Piazzola " + cachedBay + " del garage, accanto alla scalinata della cattedrale." : "Consegna immediata: la moto nasce nella piazzola " + cachedBay + " del garage.";
                int ownedN = DesertGarage.Owned(seat).Count - 1;
                ownedCount.text = ownedN + " su " + items.Count + " nel garage";

                // le righe: la scelta in arancione, le tue con un'etichetta verde
                float target = Mathf.Clamp(sel * RowH - ListH * 0.5f + RowH * 0.5f, 0f, Mathf.Max(0f, items.Count * RowH - ListH));
                scroll = Mathf.Abs(scroll - target) < 0.5f ? target : Mathf.Lerp(scroll, target, 1f - Mathf.Exp(-14f * dt));
                listRoot.anchoredPosition = new Vector2(0f, scroll);
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    bool mine = DesertGarage.Owns(seat, it.def);
                    bool selected = i == sel;
                    it.back.color = selected ? new Color(0.99f, 0.86f, 0.70f) : SitePaperDeep;
                    it.bar.enabled = selected;
                    it.price.text = mine ? "" : HubNum(it.def.price);
                    it.price.color = have >= it.def.price ? SiteOrangeDeep : SiteGrey;
                    it.tag.text = mine ? "NEL GARAGE" : "";
                }
            }

            static string HubDecimal2(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

            /// <summary>Colora la domanda: il bottone scelto pieno, l'altro spento (si fa dopo Refresh, ogni fotogramma).</summary>
            void PaintDialog()
            {
                if (phase == 1)
                {
                    yesBack.color = yes ? SiteOrange : new Color(SiteOrange.r, SiteOrange.g, SiteOrange.b, 0.45f);
                    noBack.color = !yes ? SiteInk : SiteGrey;
                    dlgHint.text = "Sinistra e destra scelgono  ·  Invio o A: " + (yes ? "conferma" : "annulla") + "  ·  Esc o B: annulla";
                }
                else if (phase == 2)
                {
                    yesBack.color = SiteGreen;
                    dlgHint.text = "Invio, A o Esc: continua a guardare il catalogo";
                }
            }
        }
    }
}

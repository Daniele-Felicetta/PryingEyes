using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    public partial class HUD
    {
        static readonly Color HubOwnedBack = new Color(0.36f, 0.075f, 0.06f, 1f), HubBuyBack = new Color(0.30f, 0.205f, 0.075f, 1f), HubPoorBack = new Color(0.20f, 0.15f, 0.10f, 1f),
            HubLockedBack = new Color(0.105f, 0.088f, 0.078f, 1f), HubGoldRing = new Color(0.98f, 0.80f, 0.40f, 1f);

        static string HubHex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        static string HubSentence(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1) + ".";

        /// <summary>Un numero di gioco con la virgola solo se serve (2,5 s, 30 essenza).</summary>
        static string HubDecimal(float v) =>
            Mathf.Abs(v - Mathf.Round(v)) < 0.01f ? Mathf.RoundToInt(v).ToString() : v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

        T HubFind<T>() where T : HubPage
        {
            if (hubPages != null) foreach (var p in hubPages) if (p is T t) return t;
            return null;
        }

        /// <summary>La pagina Libri del menu di Select: i due grimori (Stregoneria e Arsenale) con i loro diciannove nodi, quelli che hai, quelli in vendita coi loro prezzi e quelli ancora chiusi, e a destra la
        /// scheda del nodo scelto. Si compra qui, coi punti di chi gioca, come al libro della stanza segreta (GrimoireUI): stesse regole, stessi prezzi (Grimoire.Price, MagicState.Buy). Su un nodo interno gia' tuo
        /// il comando lo rende la magia o il sigillo attivo. Le tessere sono due file di esterni (uno sopra l'altro, a coppie: l'interno k sta sotto i due esterni che lo aprono), una fila di interni e il supremo.</summary>
        public sealed class HubBookPage : HubPage
        {
            sealed class Tile { public RectTransform root; public Image back, ring, icon; public Text name, price; public Rect rect; }

            const float TW = 102f, TH = 84f, TG = 8f, GridW = 6f * TW + 5f * TG;
            const float CardX = GridW + 28f, CardW = HubContentW - CardX;
            const float OuterY = 68f, InnerY = 272f, SupremeY = 384f, SupremeH = 64f;
            readonly Tile[] tiles = new Tile[Grimoire.Count];
            readonly Image[] btnBack = new Image[2], btnRing = new Image[2];
            readonly Text[] btnText = new Text[2];
            readonly Rect[] btnRect = { new Rect(0f, 0f, 316f, 40f), new Rect(324f, 0f, 316f, 40f) };
            readonly Rect actionRect = new Rect(CardX + 12f, 352f, CardW - 24f, 34f);
            Text secOuter, secInner, secSupreme, cardName, cardTier, cardEffect, cardLore, cardState, cardAction, cardPoints;
            Image cardIcon, cardMedal, cardActionBack;
            int book, sel = Grimoire.FirstInner, shownBook = -1;

            public HubBookPage() { label = "Libri"; }

            public override string Hint => "Frecce o croce: scegli     Invio  A  clic: compra o prepara     Q E  LB RB: pagina     Esc  B  Select: chiudi";

            /// <summary>Il libro e il nodo: per chi ci manda da un'altra pagina (Magie).</summary>
            public void Focus(Grimoire.Book b, int node)
            {
                book = (int)b;
                sel = Mathf.Clamp(node, 0, Grimoire.Count - 1);
                ApplyBook();
            }

            public int Book => book;
            public int Selected => sel;

            public override void Build(HUD h, RectTransform parent)
            {
                for (int b = 0; b < 2; b++)
                {
                    var r = btnRect[b];
                    btnRing[b] = HubRect(parent, "LibroAnello" + b, r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f, HubGoldRing);
                    btnBack[b] = HubRect(parent, "Libro" + b, r.x, r.y, r.width, r.height, HubCard);
                    btnText[b] = HubText(parent, "LibroNome" + b, r.x + 14f, r.y, r.width - 28f, r.height, 15, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                }
                secOuter = HubText(parent, "SezioneEsterni", 0f, 48f, GridW, 18f, 11, TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal);
                secInner = HubText(parent, "SezioneInterni", 0f, 252f, GridW, 18f, 11, TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal);
                secSupreme = HubText(parent, "SezioneSupremo", 0f, 364f, GridW, 18f, 11, TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal);
                for (int i = 0; i < Grimoire.Count; i++)
                {
                    bool supreme = i == Grimoire.Supreme;
                    float x, y, w = TW, hgt = TH;
                    if (i < Grimoire.Outer) { x = (i / 2) * (TW + TG); y = OuterY + (i % 2) * (TH + TG); }
                    else if (!supreme) { x = (i - Grimoire.FirstInner) * (TW + TG); y = InnerY; }
                    else { x = 0f; y = SupremeY; w = GridW; hgt = SupremeH; }
                    var t = new Tile { rect = new Rect(x, y, w, hgt) };
                    t.root = Box(parent, "Nodo" + i, new Vector2(x, -y), new Vector2(w, hgt));
                    t.ring = Img(t.root, "Anello", new Vector2(-3f, 3f), new Vector2(w + 6f, hgt + 6f), HubGoldRing, null);
                    t.back = Img(t.root, "Fondo", Vector2.zero, new Vector2(w, hgt), HubCard, null);
                    if (!supreme)
                    {
                        t.icon = Img(t.root, "Icona", new Vector2((w - 38f) * 0.5f, -5f), new Vector2(38f, 38f), Color.white, null);
                        t.name = HubText(t.root, "Nome", 2f, 43f, w - 4f, 26f, 10, TextAnchor.UpperCenter, HubInkC, FontStyle.Bold);
                        t.price = HubText(t.root, "Prezzo", 2f, 68f, w - 4f, 14f, 11, TextAnchor.MiddleCenter, HubInkC, FontStyle.Bold);
                    }
                    else
                    {
                        t.icon = Img(t.root, "Icona", new Vector2(14f, -10f), new Vector2(44f, 44f), Color.white, null);
                        t.name = HubText(t.root, "Nome", 70f, 8f, w - 84f, 26f, 16, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                        t.price = HubText(t.root, "Prezzo", 70f, 36f, w - 84f, 20f, 13, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                    }
                    t.name.supportRichText = false;
                    tiles[i] = t;
                }

                // la scheda del nodo scelto
                HubRect(parent, "Scheda", CardX, 48f, CardW, 398f, HubCard);
                HubRect(parent, "SchedaFilo", CardX, 48f, CardW, 2f, HubBrassC);
                cardMedal = Img(parent, "Medaglione", new Vector2(CardX + 12f, -62f), new Vector2(72f, 72f), HubBrassC, CircleSprite());
                Img(parent, "MedaglioneFondo", new Vector2(CardX + 15f, -65f), new Vector2(66f, 66f), new Color(0.10f, 0.07f, 0.05f), CircleSprite());
                cardIcon = Img(parent, "SchedaIcona", new Vector2(CardX + 24f, -74f), new Vector2(48f, 48f), Color.white, null);
                cardName = HubText(parent, "SchedaNome", CardX + 96f, 60f, CardW - 108f, 30f, 21, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold, true);
                cardTier = HubText(parent, "SchedaTipo", CardX + 96f, 92f, CardW - 108f, 40f, 12, TextAnchor.UpperLeft, HubDimC, FontStyle.Normal);
                cardEffect = HubText(parent, "SchedaEffetto", CardX + 14f, 144f, CardW - 28f, 88f, 15, TextAnchor.UpperLeft, HubInkC, FontStyle.Normal);
                cardLore = HubText(parent, "SchedaStoria", CardX + 14f, 234f, CardW - 28f, 50f, 12, TextAnchor.UpperLeft, HubDimC, FontStyle.Italic);
                HubRect(parent, "SchedaSeparatore", CardX + 14f, 290f, CardW - 28f, 1f, new Color(HubBrassC.r, HubBrassC.g, HubBrassC.b, 0.35f));
                cardState = HubText(parent, "SchedaStato", CardX + 14f, 298f, CardW - 28f, 50f, 14, TextAnchor.UpperLeft, HubInkC, FontStyle.Bold);
                cardActionBack = HubRect(parent, "Azione", actionRect.x, actionRect.y, actionRect.width, actionRect.height, HubCardHi);
                cardAction = HubText(parent, "AzioneTesto", actionRect.x, actionRect.y, actionRect.width, actionRect.height, 14, TextAnchor.MiddleCenter, HubInkC, FontStyle.Bold);
                cardPoints = HubText(parent, "SchedaPunti", CardX + 14f, 394f, CardW - 28f, 20f, 12, TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal);
                ApplyBook();
            }

            static string SectionText(Grimoire.Book b, Grimoire.Tier t) =>
                "<color=#" + HubHex(HubBrassC) + "><b>" + Grimoire.TierName(b, t).ToUpperInvariant() + "</b></color>   " + Grimoire.TierText(b, t);

            void ApplyBook()
            {
                var b = (Grimoire.Book)book;
                var ns = Grimoire.Of(b);
                for (int i = 0; i < Grimoire.Count; i++)
                {
                    tiles[i].name.text = ns[i].name;
                    tiles[i].icon.sprite = GrimoireIcons.Engraved(ns[i].icon);
                }
                secOuter.text = SectionText(b, Grimoire.Tier.Esterno);
                secInner.text = SectionText(b, Grimoire.Tier.Interno);
                secSupreme.text = SectionText(b, Grimoire.Tier.Supremo);
                shownBook = book;
            }

            void SetBook(int k)
            {
                if (k == book) return;
                book = k;
                sel = FirstWanted();
                ApplyBook();
                Sfx.UI(SoundBank.MagiaScelta, 0.3f, 1.2f);
            }

            /// <summary>Dove mettere la scelta aprendo un libro: la magia o il sigillo attivo, se no il primo nodo che si puo' comprare.</summary>
            int FirstWanted()
            {
                var s = Magic.State;
                var b = (Grimoire.Book)book;
                int active = b == Grimoire.Book.Stregoneria ? s.SelectedSpell : s.SelectedInfusion;
                if (active >= 0) return active;
                for (int i = 0; i < Grimoire.Count; i++) if (s.Status(b, i) == Grimoire.Lock.Libero) return i;
                return 0;
            }

            public override void OnShow(HUD h)
            {
                if (shownBook != book) ApplyBook();
                if (sel < 0 || sel >= Grimoire.Count + 2) sel = FirstWanted();
            }

            Vector2 Center(int i) => i < Grimoire.Count ? tiles[i].rect.center : btnRect[i - Grimoire.Count].center;

            /// <summary>Il vano piu' vicino nella direzione data (y verso il basso), entro 70 gradi, pesando la distanza.</summary>
            void Navigate(Vector2 dir)
            {
                Vector2 from = Center(sel);
                int best = -1;
                float bestScore = float.MaxValue;
                for (int i = 0; i < Grimoire.Count + 2; i++)
                {
                    if (i == sel) continue;
                    Vector2 d = Center(i) - from;
                    float len = d.magnitude;
                    if (len < 1f) continue;
                    float ang = Vector2.Angle(dir, d);
                    if (ang > 70f) continue;
                    float score = len * (1f + ang / 45f);
                    if (score < bestScore) { bestScore = score; best = i; }
                }
                if (best >= 0) { sel = best; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); }
            }

            public override void Tick(HUD h, in HubInput inp, float dt)
            {
                int hover = -1;
                bool onAction = false;
                if (inp.pointerIn)
                {
                    Vector2 q = inp.Local;
                    for (int i = 0; i < Grimoire.Count; i++) if (tiles[i].rect.Contains(q)) hover = i;
                    for (int k = 0; k < 2; k++) if (btnRect[k].Contains(q)) hover = Grimoire.Count + k;
                    onAction = actionRect.Contains(q) && cardActionBack.enabled;
                }
                if (inp.moved && hover >= 0 && hover != sel) { sel = hover; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); }
                if (inp.click)
                {
                    if (hover >= 0) { sel = hover; Activate(h); }
                    else if (onAction) Activate(h);
                }
                if (inp.ny != 0) Navigate(new Vector2(0f, -inp.ny));
                else if (inp.nx != 0) Navigate(new Vector2(inp.nx, 0f));
                if (inp.confirm) Activate(h);
                Refresh(h);
            }

            /// <summary>Compra il nodo, o (se e' gia' tuo e interno) lo rende la magia o il sigillo attivo; su un libro lo apre.</summary>
            void Activate(HUD h)
            {
                if (sel >= Grimoire.Count) { SetBook(sel - Grimoire.Count); return; }
                var s = Magic.State;
                var gm = GameManager.I;
                var b = (Grimoire.Book)book;
                var node = Grimoire.Of(b)[sel];
                var st = s.Status(b, sel);
                if (st == Grimoire.Lock.Posseduto)
                {
                    if (sel >= Grimoire.FirstInner && s.Select(b, sel))
                    {
                        h.HubSay(b == Grimoire.Book.Stregoneria ? "La mano sinistra prepara: " + node.name : "Il sigillo pronto: " + node.name, false);
                        Sfx.UI(SoundBank.MagiaSigillo, 0.5f, 1.2f);
                    }
                    else h.HubSay("«" + node.name + "» è già tuo.", false);
                    return;
                }
                if (st != Grimoire.Lock.Libero) { h.HubSay(Grimoire.LockText(b, s.Mask(b), sel, st), true); Sfx.UI(SoundBank.BookNo, 0.5f); return; }
                int price = s.Price(b, sel);
                int have = gm != null ? gm.score : 0;
                if (have < price) { h.HubSay("Servono " + HubNum(price) + " punti: ti mancano " + HubNum(price - have) + ".", true); Sfx.UI(SoundBank.BookNo, 0.5f); return; }
                gm.AddPoints(-price);
                s.Buy(b, sel);
                GrimoireUI.statBought++;
                Sfx.UI(SoundBank.GrimorioNodo, 0.85f);
                h.HubSay(sel >= Grimoire.FirstInner
                    ? (b == Grimoire.Book.Stregoneria ? node.name + ": la mano sinistra la conosce." : node.name + ": la mano sinistra sa tracciarlo sull'arma.")
                    : node.name + ": " + node.effect + ".", false);
                h.Flash(new Color(0.85f, 0.08f, 0.06f), 0.45f, 0.35f);
            }

            void Refresh(HUD h)
            {
                var s = Magic.State;
                var gm = GameManager.I;
                int have = gm != null ? gm.score : 0;
                var b = (Grimoire.Book)book;
                var ns = Grimoire.Of(b);
                uint mask = s.Mask(b);
                int active = b == Grimoire.Book.Stregoneria ? s.SelectedSpell : s.SelectedInfusion;
                for (int i = 0; i < Grimoire.Count; i++)
                {
                    var t = tiles[i];
                    var st = s.Status(b, i);
                    int price = s.Price(b, i);
                    bool selected = sel == i;
                    Color back = st == Grimoire.Lock.Posseduto ? HubOwnedBack : st == Grimoire.Lock.Libero ? (have >= price ? HubBuyBack : HubPoorBack) : HubLockedBack;
                    t.back.color = selected ? Color.Lerp(back, Color.white, 0.14f) : back;
                    t.ring.enabled = selected;
                    t.icon.color = st == Grimoire.Lock.Posseduto || st == Grimoire.Lock.Libero ? Color.white : new Color(1f, 1f, 1f, 0.28f);
                    t.price.text = st == Grimoire.Lock.Posseduto ? (i == active ? "ATTIVA" : "TUO") : st == Grimoire.Lock.Libero ? HubNum(price) : "chiuso";
                    t.price.color = st == Grimoire.Lock.Posseduto ? (i == active ? HubBrassC : HubGreenC)
                        : st == Grimoire.Lock.Libero ? (have >= price ? HubInkC : new Color(0.85f, 0.45f, 0.38f)) : HubDimC;
                }
                for (int k = 0; k < 2; k++)
                {
                    var bk = (Grimoire.Book)k;
                    uint mk = s.Mask(bk);
                    int n = 0;
                    for (int i = 0; i < Grimoire.Count; i++) if (Grimoire.Has(mk, i)) n++;
                    bool on = k == book, cur = sel == Grimoire.Count + k;
                    var c = on ? new Color(HubBrassC.r * 0.55f, HubBrassC.g * 0.45f, HubBrassC.b * 0.25f, 1f) : HubCard;
                    btnBack[k].color = cur ? Color.Lerp(c, Color.white, 0.14f) : c;
                    btnRing[k].enabled = cur;
                    btnText[k].color = on ? HubInkC : HubDimC;
                    btnText[k].text = (k == 0 ? "Libro della Stregoneria" : "Libro dell'Arsenale") + "    <color=#" + HubHex(on ? HubBrassC : HubDimC) + ">" + n + " / " + Grimoire.Count + "</color>";
                }

                string action = "";
                bool actionOn = false;
                if (sel < Grimoire.Count)
                {
                    var node = ns[sel];
                    var st = s.Status(b, sel);
                    int price = s.Price(b, sel);
                    cardIcon.enabled = true; cardIcon.sprite = tiles[sel].icon.sprite;
                    cardName.text = node.name;
                    cardTier.text = Grimoire.TierName(b, node.tier) + (node.tier == Grimoire.Tier.Esterno ? ": sempre in vendita" : node.tier == Grimoire.Tier.Interno ? ": ne serve uno dei due esterni sopra" : ": servono quattro interni");
                    cardEffect.text = HubSentence(node.effect);
                    cardLore.text = "«" + node.lore + "»";
                    if (st == Grimoire.Lock.Posseduto)
                    {
                        bool inner = sel >= Grimoire.FirstInner;
                        cardState.text = "<color=#" + HubHex(HubGreenC) + ">GIÀ TUO</color>" + (inner && sel == active ? "   ·   <color=#" + HubHex(HubBrassC) + ">attiva</color>" : "");
                        if (inner && sel != active) { action = b == Grimoire.Book.Stregoneria ? "A   PREPARA QUESTA MAGIA" : "A   PREPARA QUESTO SIGILLO"; actionOn = true; }
                    }
                    else if (st == Grimoire.Lock.Libero)
                    {
                        bool can = have >= price;
                        cardState.text = "IN VENDITA   <color=#" + HubHex(HubBrassC) + ">" + HubNum(price) + " punti</color>" + (can ? "" : "\n<color=#E0614A>ti mancano " + HubNum(price - have) + "</color>");
                        action = can ? "A   COMPRA PER " + HubNum(price) + " PUNTI" : "SERVONO " + HubNum(price - have) + " PUNTI IN PIÙ";
                        actionOn = can;
                    }
                    else cardState.text = "<color=#E0614A>BLOCCATO</color>\n" + Grimoire.LockText(b, mask, sel, st);
                }
                else
                {
                    int k = sel - Grimoire.Count;
                    var bk = (Grimoire.Book)k;
                    uint mk = s.Mask(bk);
                    cardIcon.enabled = false;
                    cardName.text = Grimoire.Title(bk);
                    cardTier.text = Grimoire.Subtitle(bk);
                    cardEffect.text = "Esterni " + Grimoire.CountTier(mk, Grimoire.Tier.Esterno) + " / " + Grimoire.Outer + "     Interni " + Grimoire.CountTier(mk, Grimoire.Tier.Interno) + " / " + Grimoire.Inner
                        + "     Supremo " + Grimoire.CountTier(mk, Grimoire.Tier.Supremo) + " / 1";
                    cardLore.text = bk == Grimoire.Book.Stregoneria ? "Le magie della mano sinistra nascono dai sei nodi interni di questo libro." : "I sigilli che la mano sinistra traccia sull'arma nascono dai sei nodi interni di questo libro.";
                    cardState.text = k == book ? "<color=#" + HubHex(HubBrassC) + ">LIBRO APERTO</color>" : "";
                    if (k != book) { action = "A   APRI QUESTO LIBRO"; actionOn = true; }
                }
                cardMedal.enabled = cardIcon.enabled;
                cardAction.text = action;
                cardActionBack.enabled = action.Length > 0;
                cardActionBack.color = actionOn ? new Color(0.50f, 0.15f, 0.10f, 1f) : HubCardHi;
                cardAction.color = actionOn ? HubInkC : HubDimC;
                cardPoints.text = "Hai " + HubNum(have) + " punti";
            }
        }

        /// <summary>La pagina Magie del menu di Select: le sei magie della mano sinistra (e l'Essenza Maledetta) e i sei sigilli che si tracciano sull'arma (e l'Arma Maledetta), con quanta essenza costano, quanto
        /// aspettano e quale e' pronta; l'essenza di adesso sotto. Un invio sceglie la magia o il sigillo (i tasti 6 e 7 lo fanno in gioco); su uno che non hai porta alla pagina Libri, dove si compra.</summary>
        public sealed class HubSpellPage : HubPage
        {
            sealed class Row { public RectTransform root; public Image back, ring, icon, cool; public Text name, effect, cost, tag; public Rect rect; }

            const float ColW = 500f, ColGap = 28f, RowH = 50f, RowGap = 4f, RowY = 56f;
            readonly Row[][] rows = { new Row[7], new Row[7] };
            readonly Text[] heads = new Text[2], subs = new Text[2];
            Image essFill;
            Text essText, essNote;
            int col, row;
            int shownBook = -1;

            public HubSpellPage() { label = "Magie"; }

            public override string Hint => "Frecce o croce: scegli     Invio  A  clic: prepara (o va in Libri se non l'hai)     Q E  LB RB: pagina     Esc  B  Select: chiudi";

            static int NodeOf(int r) => r < 6 ? Grimoire.FirstInner + r : Grimoire.Supreme;

            public override void Build(HUD h, RectTransform parent)
            {
                string[] titles = { "Magie della mano sinistra", "Sigilli sull'arma" };
                string[] notes = { "Tasto 3 lancia · 6 cambia magia · controller: LB e croce destra", "Tasto 3 tenuto traccia il sigillo · 7 cambia sigillo · controller: LB tenuto e croce sinistra" };
                for (int c = 0; c < 2; c++)
                {
                    float x0 = c * (ColW + ColGap);
                    heads[c] = HubText(parent, "Titolo" + c, x0, 0f, ColW, 30f, 21, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold, true);
                    heads[c].text = titles[c]; heads[c].supportRichText = false;
                    subs[c] = HubText(parent, "Nota" + c, x0, 31f, ColW, 18f, 11, TextAnchor.MiddleLeft, HubDimC, FontStyle.Normal);
                    subs[c].text = notes[c]; subs[c].supportRichText = false;
                    for (int r = 0; r < 7; r++)
                    {
                        float y = RowY + r * (RowH + RowGap);
                        var w = new Row { rect = new Rect(x0, y, ColW, RowH) };
                        w.root = Box(parent, "Riga" + c + r, new Vector2(x0, -y), new Vector2(ColW, RowH));
                        w.ring = Img(w.root, "Anello", new Vector2(-3f, 3f), new Vector2(ColW + 6f, RowH + 6f), HubGoldRing, null);
                        w.back = Img(w.root, "Fondo", Vector2.zero, new Vector2(ColW, RowH), HubCard, null);
                        w.icon = Img(w.root, "Icona", new Vector2(8f, -7f), new Vector2(36f, 36f), Color.white, null);
                        w.name = HubText(w.root, "Nome", 54f, 3f, 318f, 20f, 15, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                        w.effect = HubText(w.root, "Effetto", 54f, 22f, 318f, 26f, 10, TextAnchor.UpperLeft, HubDimC, FontStyle.Normal);
                        w.cost = HubText(w.root, "Costo", 376f, 4f, 116f, 18f, 12, TextAnchor.MiddleRight, HubInkC, FontStyle.Bold);
                        w.tag = HubText(w.root, "Stato", 376f, 24f, 116f, 18f, 11, TextAnchor.MiddleRight, HubInkC, FontStyle.Bold);
                        w.cool = Img(w.root, "Recupero", new Vector2(0f, -(RowH - 3f)), new Vector2(0f, 3f), HubRedC, null);
                        w.name.supportRichText = false; w.effect.supportRichText = false;
                        rows[c][r] = w;
                    }
                }
                // l'essenza
                float ey = RowY + 7 * (RowH + RowGap) + 4f;
                var el = HubText(parent, "EssenzaNome", 0f, ey, 90f, 20f, 12, TextAnchor.MiddleLeft, HubBrassC, FontStyle.Bold);
                el.text = "ESSENZA"; el.supportRichText = false;
                essFill = HubMeter(parent, "Essenza", 82f, ey + 5f, 360f, 12f, 0f, new Color(0.62f, 0.22f, 0.72f), new Color(0.05f, 0.04f, 0.05f, 1f));
                essText = HubText(parent, "EssenzaNumeri", 452f, ey, 120f, 20f, 13, TextAnchor.MiddleLeft, HubInkC, FontStyle.Bold);
                essNote = HubText(parent, "EssenzaNota", 580f, ey, HubContentW - 580f, 20f, 11, TextAnchor.MiddleRight, HubDimC, FontStyle.Normal);
            }

            void ApplyTexts()
            {
                for (int c = 0; c < 2; c++)
                {
                    var b = (Grimoire.Book)c;
                    var ns = Grimoire.Of(b);
                    for (int r = 0; r < 7; r++)
                    {
                        int node = NodeOf(r);
                        var w = rows[c][r];
                        w.name.text = ns[node].name;
                        w.effect.text = HubSentence(ns[node].effect);
                        w.icon.sprite = GrimoireIcons.Engraved(ns[node].icon);
                        float cost = c == 0 ? MagicState.SpellCost(node) : MagicState.InfusionCost(node);
                        float cd = c == 0 ? MagicState.SpellCooldown(node) : MagicState.InfusionCooldown(node);
                        w.cost.text = HubDecimal(cost) + " essenza";
                        if (cd > 0f && (c == 0 || node == Grimoire.Supreme)) w.cost.text += " · " + HubDecimal(cd) + " s";
                    }
                }
                shownBook = 1;
            }

            public override void OnShow(HUD h)
            {
                if (shownBook < 0) ApplyTexts();
                // si parte dalla magia preparata
                var s = Magic.State;
                if (s.SelectedSpell >= Grimoire.FirstInner) { col = 0; row = Mathf.Min(6, s.SelectedSpell - Grimoire.FirstInner); }
            }

            public override void Tick(HUD h, in HubInput inp, float dt)
            {
                int hc = -1, hr = -1;
                if (inp.pointerIn)
                {
                    Vector2 q = inp.Local;
                    for (int c = 0; c < 2; c++) for (int r = 0; r < 7; r++) if (rows[c][r].rect.Contains(q)) { hc = c; hr = r; }
                }
                if (inp.moved && hc >= 0 && (hc != col || hr != row)) { col = hc; row = hr; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); }
                if (inp.ny != 0) { int nr = Mathf.Clamp(row - inp.ny, 0, 6); if (nr != row) { row = nr; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); } }
                if (inp.nx != 0) { int nc = Mathf.Clamp(col + inp.nx, 0, 1); if (nc != col) { col = nc; Sfx.UI(SoundBank.MagiaScelta, 0.25f, 1.4f); } }
                if (inp.click && hc >= 0) { col = hc; row = hr; Activate(h); }
                else if (inp.confirm) Activate(h);

                var s = Magic.State;
                for (int c = 0; c < 2; c++)
                {
                    var b = (Grimoire.Book)c;
                    int active = c == 0 ? s.SelectedSpell : s.SelectedInfusion;
                    for (int r = 0; r < 7; r++)
                    {
                        int node = NodeOf(r);
                        var w = rows[c][r];
                        var st = s.Status(b, node);
                        bool selected = c == col && r == row;
                        bool owned = st == Grimoire.Lock.Posseduto;
                        Color back = owned ? (node == active ? new Color(0.42f, 0.12f, 0.08f, 1f) : HubOwnedBack) : st == Grimoire.Lock.Libero ? HubBuyBack : HubLockedBack;
                        w.back.color = selected ? Color.Lerp(back, Color.white, 0.14f) : back;
                        w.ring.enabled = selected;
                        w.icon.color = owned || st == Grimoire.Lock.Libero ? Color.white : new Color(1f, 1f, 1f, 0.28f);
                        w.name.color = owned || st == Grimoire.Lock.Libero ? HubInkC : HubDimC;
                        w.tag.text = owned ? (node == active ? "ATTIVA" : "PRONTA") : st == Grimoire.Lock.Libero ? "IN VENDITA" : "BLOCCATA";
                        w.tag.color = owned ? (node == active ? HubBrassC : HubGreenC) : st == Grimoire.Lock.Libero ? HubInkC : new Color(0.85f, 0.45f, 0.38f);
                        float frac = owned ? s.CooldownFrac(node, c == 1) : 0f;
                        w.cool.rectTransform.sizeDelta = new Vector2(ColW * frac, 3f);
                    }
                }
                float max = s.EssenceMax;
                HubSetMeter(essFill, 360f, max > 0f ? s.Essence / max : 0f);
                essText.text = Mathf.RoundToInt(s.Essence) + " / " + Mathf.RoundToInt(max);
                essNote.text = "torna di " + HubDecimal(s.Regen(false)) + " al secondo, +" + HubDecimal(s.KillGain) + " a ogni uccisione";
            }

            void Activate(HUD h)
            {
                var s = Magic.State;
                var b = (Grimoire.Book)col;
                int node = NodeOf(row);
                var st = s.Status(b, node);
                var nd = Grimoire.Of(b)[node];
                if (st == Grimoire.Lock.Posseduto)
                {
                    if (s.Select(b, node))
                    {
                        h.HubSay(b == Grimoire.Book.Stregoneria ? "La mano sinistra prepara: " + nd.name : "Il sigillo pronto: " + nd.name, false);
                        Sfx.UI(SoundBank.MagiaSigillo, 0.5f, 1.2f);
                    }
                    return;
                }
                var page = h.HubFind<HubBookPage>();
                if (page == null) return;
                page.Focus(b, node);
                h.HubSay(st == Grimoire.Lock.Libero ? "«" + nd.name + "» si compra qui: A." : Grimoire.LockText(b, s.Mask(b), node, st), st != Grimoire.Lock.Libero);
                Sfx.UI(SoundBank.MagiaScelta, 0.3f, 1.2f);
                h.SelectHubPage(h.hubPages.IndexOf(page));
            }
        }
    }
}

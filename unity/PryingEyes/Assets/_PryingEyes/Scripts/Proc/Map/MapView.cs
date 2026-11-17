using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PryingEyes.Proc.MapArt;

namespace PryingEyes.Proc
{
    /// <summary>La mappa di un giocatore, nel suo HUD (uGUI, cosi' la fotografano anche le prove e segue la scala dello schermo condiviso).
    /// Sta in un angolo come minimappa (una finestra sulla carta, centrata su di lui) e, con M, cresce fino a diventare il foglio grande: la stessa
    /// carta, lo stesso inchiostro, solo piu' in alto e con la cornice, il titolo, il conto e la legenda. La carta e il disegno (FloorMap, dipinto
    /// da MapPainter) sono statici e vivono in un canvas annidato (si ribatchano solo quando cambiano); le scritte stanno in un altro, e quello che
    /// si muove (te, i compagni, gli occhi, il ping) si ridisegna a ogni fotogramma in un terzo, piccolo.
    /// Regole del foglio: i nomi e i conti stanno sul bordo, sulla tavola scura; sulla carta solo il disegno e le scritte a mano.</summary>
    public sealed partial class MapView
    {
        // ---- impaginazione (unita di riferimento dell'HUD, 1280 x 720)
        const float MiniSize = 208f, MiniMargin = 18f, MiniPad = 8f, MiniScale = 5.9f;
        const float BigSideMargin = 54f, BigTopMargin = 16f, BigBottomMargin = 16f;
        const float BigPadSide = 18f, BigPadTop = 84f, BigPadBottom = 124f;
        const float MorphTime = 0.36f;
        static readonly Color BoardColor = new Color(0.082f, 0.064f, 0.050f, 1f);

        readonly HUD hud;
        readonly RectTransform rootRt, sheetRt, windowRt, contentRt, staticRt, labelsRt, liveRt, washLayer, inkLayer, stainLayer;
        readonly Image dim, board, windowBg, shade;
        MapGraphic paperGfx;

        sealed class SetGfx { public MapGraphic wash, ink, stain; }
        SetGfx[] roomGfx;
        SetGfx doorsGfx, poisGfx, bloodGfx;
        int[] roomSeenVersion;
        int doorsSeenVersion, poisSeenVersion, bloodSeenVersion, boundVersion = -1;

        FloorMap map;
        float k;                       // 0: minimappa, 1: foglio grande
        V2 center;                     // il punto della carta al centro della finestra (metri)
        bool centerInit;
        Vector2 winCenter, winSize;    // la finestra com'e' adesso, nelle unita dell'HUD (rispetto al centro dello schermo)
        float scale = MiniScale;       // pixel dell'HUD per metro
        float shake;

        public bool Visible => rootRt != null && rootRt.gameObject.activeSelf;

        /// <summary>Spegne la vista (l'HUD lo fa se la mappa continua a inciampare: vedi HUD.UpdateMapUI).</summary>
        public void Hide() { if (rootRt != null) rootRt.gameObject.SetActive(false); }
        /// <summary>0 minimappa .. 1 foglio grande (per le prove).</summary>
        public float Bigness => k;

        public MapView(HUD hud, Transform hudRoot)
        {
            MapAssets.Ensure();
            this.hud = hud;
            rootRt = NewRt("Mappa", hudRoot);
            Stretch(rootRt);
            dim = NewImage("Velo", rootRt, new Color(0f, 0f, 0f, 0f));
            Stretch(dim.rectTransform);
            sheetRt = NewRt("Foglio", rootRt);
            Middle(sheetRt);
            board = NewImage("Tavola", sheetRt, BoardColor);
            Stretch(board.rectTransform);
            windowRt = NewRt("Finestra", sheetRt);
            Middle(windowRt);
            windowBg = windowRt.gameObject.AddComponent<Image>();
            windowBg.color = new Color(0.02f, 0.016f, 0.013f, 1f);
            windowBg.raycastTarget = false;
            windowRt.gameObject.AddComponent<RectMask2D>();
            contentRt = NewRt("Contenuto", windowRt);
            contentRt.anchorMin = contentRt.anchorMax = new Vector2(0.5f, 0.5f);
            contentRt.pivot = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;
            staticRt = NewCanvas("Fisso", contentRt);
            washLayer = NewLayer("Acquerello", staticRt);
            inkLayer = NewLayer("Inchiostro", staticRt);
            stainLayer = NewLayer("Macchie", staticRt);
            labelsRt = NewCanvas("Scritte", contentRt);
            liveRt = NewCanvas("Vivo", contentRt);
            shade = NewImage("Ombra", windowRt, new Color(1f, 1f, 1f, 0.9f));
            shade.sprite = MapAssets.Shade();
            Stretch(shade.rectTransform);
            BuildLive();
            BuildChrome();
            rootRt.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ piccoli attrezzi
        internal static RectTransform NewRt(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Uno strato: un punto (rettangolo senza dimensioni) nell'origine della carta. I disegni si ancorano al suo angolo, quindi l'origine
        /// deve essere esattamente li' (un RectTransform nuovo e' 100 x 100 e centrato: sposterebbe tutto di 50 m).</summary>
        static RectTransform NewLayer(string name, Transform parent)
        {
            var rt = NewRt(name, parent);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        static RectTransform NewCanvas(string name, Transform parent)
        {
            var rt = NewRt(name, parent);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.gameObject.AddComponent<Canvas>();       // canvas annidato: si ribatcha da solo, l'ordine di disegno resta quello della gerarchia
            return rt;
        }

        internal static Image NewImage(string name, Transform parent, Color c)
        {
            var rt = NewRt(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c; img.raycastTarget = false;
            return img;
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        internal static void Middle(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Spegne o riaccende un elemento senza perderlo: uGUI non ricostruisce un renderer in cull (la sua mesh e il suo materiale restano
        /// da fare), e quando torna visibile nessuno lo rimette in coda. Qui al ritorno lo si segna da rifare.</summary>
        static void SetCull(Graphic g, bool cull)
        {
            var cr = g.canvasRenderer;
            if (cr.cull == cull) return;
            cr.cull = cull;
            if (!cull) g.SetAllDirty();
        }

        static MapGraphic NewGraphic(string name, RectTransform parent)
        {
            var rt = NewRt(name, parent);
            return rt.gameObject.AddComponent<MapGraphic>();
        }

        // ------------------------------------------------------------------ un piano nuovo
        void Bind(FloorMap m)
        {
            Unbind();
            map = m;
            centerInit = false;
            int n = m.plan.rooms.Count;
            // la carta: un foglio piu' grande del piano (il margine di una minimappa) e della proporzione della texture, cosi' copre per intero la
            // finestra del foglio grande, che e' molto piu' larga che alta, senza stirare le macchie
            var pb = PaperRect(PlanRect(m.plan, 26f));
            var pm = new Mesh2D();
            int a = pm.Vert(new V2(pb.x0, pb.y0), new V2(0f, 0f), Col.F(1f, 1f, 1f));
            pm.Vert(new V2(pb.x0, pb.y1), new V2(0f, 1f), Col.F(1f, 1f, 1f));
            pm.Vert(new V2(pb.x1, pb.y1), new V2(1f, 1f), Col.F(1f, 1f, 1f));
            pm.Vert(new V2(pb.x1, pb.y0), new V2(1f, 0f), Col.F(1f, 1f, 1f));
            pm.Quad(a, a + 1, a + 2, a + 3);
            paperGfx = NewGraphic("Carta", washLayer);
            paperGfx.transform.SetAsFirstSibling();
            paperGfx.Set(pm, MapAssets.Paper, pb);
            roomGfx = new SetGfx[n];
            roomSeenVersion = new int[n];
            for (int i = 0; i < n; i++) { roomGfx[i] = NewSet("Stanza" + i); roomSeenVersion[i] = -1; }
            doorsGfx = NewSet("Porte"); poisGfx = NewSet("Segni"); bloodGfx = NewSet("Sangue");
            doorsSeenVersion = poisSeenVersion = bloodSeenVersion = -1;
            boundVersion = -1;
            liveGfx.Set(liveMesh, MapAssets.Line, PlanRect(m.plan, 70f));       // quello che si muove puo' stare ovunque nel piano (e un po' fuori)
            LabelsReset();
            ChromeReset();
        }

        void Unbind()
        {
            if (paperGfx != null) Object.Destroy(paperGfx.gameObject);
            paperGfx = null;
            if (roomGfx != null) foreach (var s in roomGfx) DestroySet(s);
            DestroySet(doorsGfx); DestroySet(poisGfx); DestroySet(bloodGfx);
            roomGfx = null; doorsGfx = poisGfx = bloodGfx = null;
            map = null;
        }

        SetGfx NewSet(string name)
        {
            var s = new SetGfx
            {
                wash = NewGraphic(name, washLayer), ink = NewGraphic(name, inkLayer), stain = NewGraphic(name, stainLayer),
            };
            s.wash.enabled = s.ink.enabled = s.stain.enabled = false;
            return s;
        }

        static void DestroySet(SetGfx s)
        {
            if (s == null) return;
            if (s.wash != null) Object.Destroy(s.wash.gameObject);
            if (s.ink != null) Object.Destroy(s.ink.gameObject);
            if (s.stain != null) Object.Destroy(s.stain.gameObject);
        }

        /// <summary>Il rettangolo piu' piccolo che contiene 'r', ha lo stesso centro e la proporzione della texture della carta.</summary>
        static R2 PaperRect(R2 r)
        {
            float aspect = MapTextures.PaperW / (float)MapTextures.PaperH;
            var c = r.Center;
            float w = Mathf.Max(r.W, r.H * aspect), h = w / aspect;
            return new R2(c.x - w * 0.5f, c.y - h * 0.5f, c.x + w * 0.5f, c.y + h * 0.5f);
        }

        static R2 PlanRect(Plan plan, float margin)
        {
            bool any = false; R2 b = default;
            foreach (var r in plan.rooms)
            {
                var q = new R2(r.x0 * 0.1f, r.z0 * 0.1f, r.x1 * 0.1f, r.z1 * 0.1f);
                b = any ? b.Union(q) : q; any = true;
            }
            return b.Grow(margin);
        }

        /// <summary>Porta nelle sue mesh quello che il pittore ha rifatto da quando l'ha visto.</summary>
        void Sync()
        {
            if (map == null || map.version == boundVersion) return;
            boundVersion = map.version;
            var plan = map.plan;
            for (int i = 0; i < roomGfx.Length; i++)
            {
                if (map.roomVersion[i] == roomSeenVersion[i]) continue;
                roomSeenVersion[i] = map.roomVersion[i];
                var r = plan.rooms[i];
                var bounds = new R2(r.x0 * 0.1f, r.z0 * 0.1f, r.x1 * 0.1f, r.z1 * 0.1f).Grow(9f);
                Fill(roomGfx[i], map.rooms[i], bounds);
            }
            var all = PlanRect(plan, 8f);
            if (map.doorsVersion != doorsSeenVersion) { doorsSeenVersion = map.doorsVersion; Fill(doorsGfx, map.doors, all); }
            if (map.poisVersion != poisSeenVersion) { poisSeenVersion = map.poisVersion; Fill(poisGfx, map.pois, all); }
            if (map.bloodVersion != bloodSeenVersion) { bloodSeenVersion = map.bloodVersion; Fill(bloodGfx, map.blood, all); }
            LabelsSync();
            ChromeSync();
        }

        static void Fill(SetGfx g, Art art, R2 bounds)
        {
            if (art == null) { g.wash.enabled = g.ink.enabled = g.stain.enabled = false; return; }
            g.wash.Set(art.wash, MapAssets.Wash, bounds); g.wash.enabled = art.wash.Count > 0;
            g.ink.Set(art.ink, MapAssets.Line, bounds); g.ink.enabled = art.ink.Count > 0;
            g.stain.Set(art.stain, MapAssets.Stain, bounds); g.stain.enabled = art.stain.Count > 0;
        }

        // ------------------------------------------------------------------ ogni fotogramma
        /// <summary>Lo chiama l'HUD (nel posto del suo giocatore). Mostra la minimappa, il foglio grande o niente, e li tiene aggiornati.</summary>
        public void Tick(float dt)
        {
            var fd = FloorDirector.I;
            var fm = FloorMap.I;
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            int si = Seats.SeatIndex;
            bool wantBig = fd != null && fd.MapOpen(si), wantMini = fd != null && fd.MiniOn(si);
            bool ready = fd != null && fm != null && pl != null && !GameMenu.Blocked && !fd.transitioning;
            if (!ready || (!wantBig && !wantMini))
            {
                if (rootRt.gameObject.activeSelf) rootRt.gameObject.SetActive(false);
                k = wantBig ? k : 0f;
                return;
            }
            if (!rootRt.gameObject.activeSelf) rootRt.gameObject.SetActive(true);
            if (map != fm) Bind(fm);
            Sync();
            k = Mathf.MoveTowards(k, wantBig ? 1f : 0f, dt / MorphTime);
            float e = Ease(k);
            Vector3 pp = pl.transform.position;
            var player = new V2(pp.x, pp.z);
            if (!centerInit) { center = player; centerInit = true; }
            else center = V2.Lerp(center, player, 1f - Mathf.Exp(-9f * dt));
            Layout(e, player, dt);
            UpdateLabels(e, dt);
            PaintLive(dt, e, player, pl);
            UpdateChrome(e);
        }

        void Layout(float e, V2 player, float dt)
        {
            var hr = rootRt.rect;
            float W = hr.width, H = hr.height;
            // la finestra: dall'angolo in alto a destra al centro dello schermo
            float miniW = MiniSize - 2f * MiniPad;
            var miniC = new Vector2(W * 0.5f - MiniMargin - MiniSize * 0.5f, H * 0.5f - MiniMargin - MiniSize * 0.5f);
            float bigW = Mathf.Max(200f, W - 2f * BigSideMargin - 2f * BigPadSide), bigH = Mathf.Max(160f, H - BigTopMargin - BigBottomMargin - BigPadTop - BigPadBottom);
            var bigC = new Vector2(0f, (BigBottomMargin + BigPadBottom - BigTopMargin - BigPadTop) * 0.5f);
            winSize = Vector2.Lerp(new Vector2(miniW, miniW), new Vector2(bigW, bigH), e);
            winCenter = Vector2.Lerp(miniC, bigC, e);
            float padL = Mathf.Lerp(MiniPad, BigPadSide, e), padR = padL, padT = Mathf.Lerp(MiniPad, BigPadTop, e), padB = Mathf.Lerp(MiniPad, BigPadBottom, e);
            var sheetSize = new Vector2(winSize.x + padL + padR, winSize.y + padT + padB);
            var sheetCenter = winCenter + new Vector2((padR - padL) * 0.5f, (padT - padB) * 0.5f);
            // il cuore: la minimappa trema quando qualcuno ti ha visto
            float threat = Mathf.Clamp01((Stealth.Level - 0.45f) / 0.55f);
            shake = Mathf.MoveTowards(shake, threat * (1f - e), dt * 4f);
            var jitter = shake > 0.01f ? new Vector2(Mathf.Sin(Time.unscaledTime * 53f), Mathf.Cos(Time.unscaledTime * 47f)) * shake * 1.6f : Vector2.zero;
            sheetRt.anchoredPosition = sheetCenter + jitter; sheetRt.sizeDelta = sheetSize;
            windowRt.anchoredPosition = winCenter - sheetCenter; windowRt.sizeDelta = winSize;
            dim.color = new Color(0f, 0f, 0f, 0.9f * e);
            shade.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.92f, 0.55f, e));
            // la carta: a poco piu' di 5 px al metro in tutte e due le viste; il foglio grande inquadra tutto il piano
            var pr = PlanRect(map.plan, 5f);
            float fit = Mathf.Min(winSize.x / Mathf.Max(1f, pr.W), winSize.y / Mathf.Max(1f, pr.H));
            scale = Mathf.Lerp(MiniScale, fit, e);
            V2 c = V2.Lerp(center, pr.Center, e);
            contentRt.localScale = new Vector3(scale, scale, 1f);
            contentRt.anchoredPosition = new Vector2(-c.x * scale, -c.y * scale);
        }
    }
}

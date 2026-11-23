using System.Collections.Generic;
using PryingEyes.Deserto;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>La minimappa del deserto (0.9.1), in alto a destra come quella dei piani (N la accende e la spegne): una finestra di 512 m di sabbia centrata su chi gioca, con
    /// il nord in alto (DesertMapGen la dipinge, DesertMapTiles la tiene aggiornata) e sopra, ridisegnati a ogni fotogramma, te (la freccia rossa, che gira con lo sguardo), i compagni
    /// (nel colore del loro posto, con un anello rosso che pulsa se sono a terra), i nemici svegli entro 90 m (puntini rossi, un anello scuro per i boss) e, quando la cattedrale e' fuori
    /// dalla finestra, una freccia d'oro sul bordo che dice da che parte e'. La tua moto, quando e' parcheggiata, e' un tondo turchese dove l'hai lasciata (sul bordo, dalla sua parte, se e'
    /// fuori dalla finestra e non sotto la freccia d'oro). Di notte la carta si fa blu e scura; sottoterra (le catacombe) si spegne a meta'. E' un di piu': se
    /// inciampa tre volte si spegne per sempre e il resto dell'HUD va avanti.</summary>
    public partial class HUD
    {
        const float DmSize = 208f, DmMargin = 18f, DmPad = 8f, DmWindow = DmSize - 2f * DmPad;
        /// <summary>Pixel dell'HUD per metro del mondo.</summary>
        const float DmScale = DmWindow / DesertMapGen.WindowSize;
        const float DmEnemyRange = 90f;
        static readonly Color DmBrass = new Color(0.76f, 0.60f, 0.30f, 0.95f), DmBoard = new Color(0.082f, 0.064f, 0.050f, 0.96f), DmInk = new Color(0.20f, 0.13f, 0.08f),
            DmRed = new Color(0.93f, 0.16f, 0.12f), DmRim = new Color(0.07f, 0.05f, 0.04f, 0.92f),
            // la moto: turchese, un colore che nessun altro segno della carta ha (rosso i nemici e te, d'oro la cattedrale, verde/azzurro/corallo/ambra i compagni)
            DmBike = new Color(0.30f, 0.80f, 0.82f);

        RectTransform dmRoot, dmWin;
        RawImage dmImage;
        Image dmArrowRim, dmArrow, dmHomeRim, dmHome, dmBikeRim;
        readonly List<Image> dmEnemyRims = new List<Image>(), dmEnemyCores = new List<Image>();
        readonly Image[] dmMateRims = new Image[3], dmMateCores = new Image[3];
        DesertMapTiles dmTiles;
        int dmFaults;
        bool dmDead;
        float dmTintT;
        Color dmTint = Color.white;

        /// <summary>Per le prove: la minimappa e' accesa, quanti quadrati le servono e quanti sono pronti, che parte della texture mostra, quanti nemici e compagni segna, se c'e' la freccia di casa.</summary>
        public bool DesertMapShown => dmRoot != null && dmRoot.gameObject.activeSelf;
        public int DesertMapWanted => dmTiles != null ? dmTiles.Wanted : 0;
        public int DesertMapReady => dmTiles != null ? dmTiles.Ready : 0;
        public DesertMapTiles DesertMapPaper => dmTiles;
        public Rect DesertMapUv => dmImage != null ? dmImage.uvRect : default;
        public bool DesertMapHomeShown => dmHome != null && dmHome.gameObject.activeSelf;
        public Vector2 DesertMapHomeAt => dmHome != null ? dmHome.rectTransform.anchoredPosition : Vector2.zero;
        /// <summary>Per le prove: c'e' il tondo della tua moto parcheggiata, dove sta sulla carta (pixel dal centro, nord in alto) e quanti pixel dell'HUD fa un metro.</summary>
        public bool DesertMapBikeShown => dmBikeRim != null && dmBikeRim.gameObject.activeSelf;
        public Vector2 DesertMapBikeAt => dmBikeRim != null ? dmBikeRim.rectTransform.anchoredPosition : Vector2.zero;
        public const float DesertMapPixelsPerMetre = DmScale;
        public int DesertMapEnemyDots { get { int n = 0; foreach (var d in dmEnemyRims) if (d != null && d.gameObject.activeSelf) n++; return n; } }
        public int DesertMapMateDots { get { int n = 0; foreach (var d in dmMateRims) if (d != null && d.gameObject.activeSelf) n++; return n; } }
        /// <summary>Per le prove: la riga dell'obiettivo come la vede il giocatore.</summary>
        public string DesertObjectiveShown => objectiveText != null ? objectiveText.text : "";
        public Color DesertMapTint => dmImage != null ? dmImage.color : Color.white;

        /// <summary>La carta del deserto (le tessere intorno a chi gioca): nasce alla prima domanda, di chi sia (la minimappa o la pagina Mappa del menu di Select, HUD.DesertHub.cs), ed e' una sola per HUD.</summary>
        DesertMapTiles DesertPaper()
        {
            if (dmTiles == null) dmTiles = new DesertMapTiles();
            return dmTiles;
        }

        void UpdateDesertMapUI()
        {
            if (dmDead) return;
            try { TickDesertMap(); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                if (++dmFaults >= 3) { dmDead = true; if (dmRoot != null) dmRoot.gameObject.SetActive(false); }
            }
        }

        void TickDesertMap()
        {
            if (!DesertMode.Active)
            {
                if (dmRoot != null && dmRoot.gameObject.activeSelf) dmRoot.gameObject.SetActive(false);
                if (dmTiles != null) { dmTiles.Dispose(); dmTiles = null; if (dmImage != null) dmImage.texture = null; }
                return;
            }
            var fd = Proc.FloorDirector.I;
            var gm = GameManager.I;
            var pl = gm != null ? gm.player : null;
            bool show = fd != null && pl != null && fd.MiniOn(Seats.SeatIndex) && !GameMenu.Blocked && !fd.transitioning;
            if (!show) { if (dmRoot != null && dmRoot.gameObject.activeSelf) dmRoot.gameObject.SetActive(false); return; }
            if (dmRoot == null) BuildDesertMap();
            if (!dmRoot.gameObject.activeSelf) dmRoot.gameObject.SetActive(true);
            Vector3 p = pl.transform.position;
            DesertPaper();
            if (dmImage.texture != dmTiles.Texture) dmImage.texture = dmTiles.Texture;
            dmTiles.Update(p.x, p.z);
            dmImage.uvRect = DesertMapTiles.UvRect(p.x, p.z);

            // la carta di notte e' blu e scura, sottoterra e' ancora piu' spenta (la carta e' quella della sabbia, non delle catacombe)
            dmTintT -= Time.unscaledDeltaTime;
            if (dmTintT <= 0f)
            {
                dmTintT = 0.25f;
                float day = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(DesertMode.Clock.Now.daylight * 1.25f));
                // (0.34, 0.40, 0.62 lasciava la carta quasi nera: le impronte dei luoghi, scure d'inchiostro, non si leggevano piu', come si vede nella foto 21 della prova)
                dmTint = Color.Lerp(new Color(0.46f, 0.55f, 0.85f), Color.white, day);
                if (p.y < DesertWorld.Height(p.x, p.z) - 3f) dmTint *= 0.55f;
                dmTint.a = 1f;
            }
            dmImage.color = dmTint;

            // te: la freccia rossa, ruotata come lo sguardo (yaw in radianti, 0 verso nord, positivo verso est)
            var rot = Quaternion.Euler(0f, 0f, -pl.yaw * Mathf.Rad2Deg);
            dmArrowRim.rectTransform.localRotation = rot; dmArrow.rectTransform.localRotation = rot;

            // i compagni
            int m = 0;
            var game = Coop.Game;
            if (Coop.Active && game != null)
                for (int slot = 0; slot < CoopProtocol.MaxPlayers && m < dmMateRims.Length; slot++)
                {
                    if (!game.Players.TryGetValue(slot, out var peer) || peer == null || !peer.hasState || !peer.Shown) continue;
                    Vector3 f = peer.Feet;
                    var at = DmLocal(f, p);
                    bool down = peer.last.alive && peer.last.downed;
                    var rim = dmMateRims[m]; var core = dmMateCores[m];
                    if (!peer.last.alive || !InDmWindow(at)) { rim.gameObject.SetActive(false); m++; continue; }
                    rim.gameObject.SetActive(true);
                    rim.rectTransform.anchoredPosition = at;
                    rim.color = down ? Color.Lerp(DmRim, DmRed, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)) : DmRim;
                    core.color = MateColors[Mathf.Clamp(slot, 0, MateColors.Length - 1)];
                    m++;
                }
            // schermo condiviso: gli altri posti (questo HUD e' di chi gira adesso, il suo giocatore e' 'pl')
            if (Seats.Split)
                for (int si = 0; si < Seats.All.Count && m < dmMateRims.Length; si++)
                {
                    var other = Seats.All[si];
                    if (other.player == null || other.player == pl) continue;
                    var rim = dmMateRims[m]; var core = dmMateCores[m];
                    var at = DmLocal(other.player.transform.position, p);
                    if (!other.player.alive || !InDmWindow(at)) { rim.gameObject.SetActive(false); m++; continue; }
                    rim.gameObject.SetActive(true);
                    rim.rectTransform.anchoredPosition = at;
                    rim.color = DmRim;
                    core.color = MateColors[Mathf.Clamp(other.index, 0, MateColors.Length - 1)];
                    m++;
                }
            for (; m < dmMateRims.Length; m++) if (dmMateRims[m] != null && dmMateRims[m].gameObject.activeSelf) dmMateRims[m].gameObject.SetActive(false);

            // i nemici svegli, vicini
            int n = 0;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.IsDead || e.dormant) continue;
                Vector3 d = e.transform.position - p; d.y = 0f;
                if (d.sqrMagnitude > DmEnemyRange * DmEnemyRange) continue;
                if (n >= 48) break;
                while (dmEnemyRims.Count <= n) AddEnemyDot();
                var rim = dmEnemyRims[n]; var core = dmEnemyCores[n];
                bool boss = Enemy.IsBoss(e.type);
                if (!rim.gameObject.activeSelf) rim.gameObject.SetActive(true);
                rim.rectTransform.anchoredPosition = DmLocal(e.transform.position, p);
                float s = boss ? 13f : 8f;
                rim.rectTransform.sizeDelta = new Vector2(s, s); core.rectTransform.sizeDelta = new Vector2(s - 3f, s - 3f);
                core.color = boss ? new Color(0.60f, 0.04f, 0.04f) : DmRed;
                n++;
            }
            for (; n < dmEnemyRims.Count; n++) if (dmEnemyRims[n].gameObject.activeSelf) dmEnemyRims[n].gameObject.SetActive(false);

            // la strada di casa sul bordo, se la cattedrale non e' nella finestra
            DesertWorld.CathedralCentre(out float hx, out float hz);
            float dx = hx - p.x, dz = hz - p.z, lim = DesertMapGen.WindowSize * 0.5f - 14f;
            bool outside = (Mathf.Abs(dx) > lim || Mathf.Abs(dz) > lim) && pl.alive;
            if (dmHome.gameObject.activeSelf != outside) { dmHome.gameObject.SetActive(outside); dmHomeRim.gameObject.SetActive(outside); }
            if (outside)
            {
                float k = (DmWindow * 0.5f - 11f) / Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                var at = new Vector2(dx * k, dz * k);
                var hr = Quaternion.Euler(0f, 0f, -Mathf.Atan2(dx, dz) * Mathf.Rad2Deg);
                dmHomeRim.rectTransform.anchoredPosition = at; dmHome.rectTransform.anchoredPosition = at;
                dmHomeRim.rectTransform.localRotation = hr; dmHome.rectTransform.localRotation = hr;
            }

            // la tua moto, parcheggiata: un tondo turchese dove l'hai lasciata (in sella sei la freccia rossa, e a terra la ritrovi dal puntino). Fuori dalla finestra sta sul bordo, dalla sua
            // parte, a meno che finisca sotto la freccia d'oro di casa: la moto appena nata, ai piedi della scalinata, non ha bisogno di un secondo segno
            bool bikeDot = false;
            var ownBike = pl.alive ? DesertBikes.Own(pl) : null;
            if (ownBike != null && ownBike.rider == null)
            {
                var bat = DmLocal(ownBike.transform.position, p);
                bikeDot = true;
                if (!InDmWindow(bat))
                {
                    bat *= (DmWindow * 0.5f - 8f) / Mathf.Max(Mathf.Abs(bat.x), Mathf.Abs(bat.y));
                    if (dmHome.gameObject.activeSelf && (bat - dmHome.rectTransform.anchoredPosition).sqrMagnitude < 18f * 18f) bikeDot = false;
                }
                if (bikeDot) dmBikeRim.rectTransform.anchoredPosition = bat;
            }
            if (dmBikeRim.gameObject.activeSelf != bikeDot) dmBikeRim.gameObject.SetActive(bikeDot);
        }

        static Vector2 DmLocal(Vector3 world, Vector3 centre) => new Vector2((world.x - centre.x) * DmScale, (world.z - centre.z) * DmScale);
        static bool InDmWindow(Vector2 at) => Mathf.Abs(at.x) < DmWindow * 0.5f + 5f && Mathf.Abs(at.y) < DmWindow * 0.5f + 5f;

        // ------------------------------------------------------------------ costruzione
        RectTransform DmMarker(string name, Transform parent, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(size, size);
            return rt;
        }

        Image DmDisc(string name, Transform parent, float size, Color col)
        {
            var img = DmMarker(name, parent, size).gameObject.AddComponent<Image>();
            img.sprite = CircleSprite(); img.color = col; img.raycastTarget = false;
            return img;
        }

        Image DmArrowImg(string name, Transform parent, float size, Color col)
        {
            var img = DmMarker(name, parent, size).gameObject.AddComponent<Image>();
            img.sprite = ArrowSprite(); img.color = col; img.raycastTarget = false;
            return img;
        }

        static Text DmText(Transform parent, string name, Vector2 pos, Vector2 size, int fs, TextAnchor a, Color col, FontStyle style)
        {
            var t = Txt(parent, name, pos, size, fs, a, col, style);
            var sh = t.GetComponent<Shadow>();
            if (sh != null) { sh.effectColor = new Color(0.93f, 0.85f, 0.66f, 0.65f); sh.effectDistance = new Vector2(0.8f, -0.8f); }
            return t;
        }

        void BuildDesertMap()
        {
            var go = new GameObject("MinimappaDeserto", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            dmRoot = (RectTransform)go.transform;
            dmRoot.anchorMin = dmRoot.anchorMax = dmRoot.pivot = new Vector2(1f, 1f);
            dmRoot.anchoredPosition = new Vector2(-DmMargin, -DmMargin);
            dmRoot.sizeDelta = new Vector2(DmSize, DmSize);
            // la tavola scura e la cornice d'ottone con i chiodi, come quella dei piani
            Img(dmRoot, "Tavola", Vector2.zero, new Vector2(DmSize, DmSize), DmBoard, null);
            Img(dmRoot, "FiloAlto", new Vector2(4f, -4f), new Vector2(DmSize - 8f, 2f), DmBrass, null);
            Img(dmRoot, "FiloBasso", new Vector2(4f, -(DmSize - 6f)), new Vector2(DmSize - 8f, 2f), DmBrass, null);
            Img(dmRoot, "FiloSinistro", new Vector2(4f, -4f), new Vector2(2f, DmSize - 8f), DmBrass, null);
            Img(dmRoot, "FiloDestro", new Vector2(DmSize - 6f, -4f), new Vector2(2f, DmSize - 8f), DmBrass, null);
            var stud = CircleSprite();
            Img(dmRoot, "ChiodoA", new Vector2(1f, -1f), new Vector2(7f, 7f), DmBrass, stud);
            Img(dmRoot, "ChiodoB", new Vector2(DmSize - 8f, -1f), new Vector2(7f, 7f), DmBrass, stud);
            Img(dmRoot, "ChiodoC", new Vector2(1f, -(DmSize - 8f)), new Vector2(7f, 7f), DmBrass, stud);
            Img(dmRoot, "ChiodoD", new Vector2(DmSize - 8f, -(DmSize - 8f)), new Vector2(7f, 7f), DmBrass, stud);
            dmWin = Box(dmRoot, "Finestra", new Vector2(DmPad, -DmPad), new Vector2(DmWindow, DmWindow));
            dmWin.gameObject.AddComponent<RectMask2D>();
            var paper = Box(dmWin, "Carta", Vector2.zero, new Vector2(DmWindow, DmWindow));
            dmImage = paper.gameObject.AddComponent<RawImage>();
            dmImage.raycastTarget = false;
            // l'ombra morbida sul bordo della carta, la stessa dei piani (e' un di piu': senza non cambia niente)
            try { Proc.MapAssets.Ensure(); Img(dmWin, "Ombra", Vector2.zero, new Vector2(DmWindow, DmWindow), new Color(1f, 1f, 1f, 0.85f), Proc.MapAssets.Shade()); }
            catch (System.Exception e) { Debug.LogWarning("[PryingEyes] minimappa del deserto: niente ombra sul bordo (" + e.Message + ")"); }

            // il nord, in un tondo scuro in alto
            Img(dmWin, "NordFondo", new Vector2(DmWindow * 0.5f - 8f, -1f), new Vector2(16f, 16f), DmBoard, CircleSprite());
            var north = Txt(dmWin, "Nord", new Vector2(DmWindow * 0.5f - 8f, -1.5f), new Vector2(16f, 16f), 12, TextAnchor.MiddleCenter, new Color(0.93f, 0.85f, 0.66f), FontStyle.Bold);
            north.text = "N"; north.supportRichText = false;
            // la scala: cento metri
            float bar = 100f * DmScale;
            Img(dmWin, "ScalaFilo", new Vector2(9f, -(DmWindow - 12f)), new Vector2(bar, 2f), DmInk, null);
            Img(dmWin, "ScalaA", new Vector2(9f, -(DmWindow - 15f)), new Vector2(2f, 8f), DmInk, null);
            Img(dmWin, "ScalaB", new Vector2(9f + bar - 2f, -(DmWindow - 15f)), new Vector2(2f, 8f), DmInk, null);
            var scale = DmText(dmWin, "ScalaTesto", new Vector2(9f, -(DmWindow - 27f)), new Vector2(60f, 12f), 10, TextAnchor.UpperLeft, DmInk, FontStyle.Bold);
            scale.text = "100 m"; scale.supportRichText = false;

            // le cose che si muovono, sopra la carta (centro della finestra = chi gioca)
            for (int i = 0; i < dmMateRims.Length; i++)
            {
                dmMateRims[i] = DmDisc("Compagno" + i, dmWin, 11f, DmRim);
                dmMateCores[i] = DmDisc("Colore", dmMateRims[i].transform, 7f, Color.white);
                dmMateRims[i].gameObject.SetActive(false);
            }
            dmBikeRim = DmDisc("MotoOrlo", dmWin, 13f, DmRim);
            DmDisc("Moto", dmBikeRim.transform, 9f, DmBike);
            dmBikeRim.gameObject.SetActive(false);
            dmHomeRim = DmArrowImg("CasaOrlo", dmWin, 19f, DmRim);
            dmHome = DmArrowImg("Casa", dmWin, 13f, DesertHome.Gold);
            dmHomeRim.gameObject.SetActive(false); dmHome.gameObject.SetActive(false);
            dmArrowRim = DmArrowImg("TuOrlo", dmWin, 20f, DmRim);
            dmArrow = DmArrowImg("Tu", dmWin, 14f, DmRed);
            dmRoot.gameObject.SetActive(false);
        }

        void AddEnemyDot()
        {
            var rim = DmDisc("Nemico" + dmEnemyRims.Count, dmWin, 8f, DmRim);
            var core = DmDisc("Nucleo", rim.transform, 5f, DmRed);
            // sotto la freccia di chi gioca
            rim.transform.SetSiblingIndex(dmArrowRim != null ? dmArrowRim.transform.GetSiblingIndex() : rim.transform.GetSiblingIndex());
            rim.gameObject.SetActive(false);
            dmEnemyRims.Add(rim); dmEnemyCores.Add(core);
        }
    }
}

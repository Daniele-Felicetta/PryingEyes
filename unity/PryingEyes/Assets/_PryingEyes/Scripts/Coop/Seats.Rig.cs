using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Nascita e disposizione dei posti dello split screen: il corpo, la camera e l'HUD dei giocatori 2-4 (gli stessi pezzi che
    /// HotelBuilder.PlayerAndGame monta per il giocatore 1, ma a gioco avviato), e le porzioni di schermo.</summary>
    public static partial class Seats
    {
        /// <summary>Il canvas Overlay di un monitor: gli HUD dei posti che stanno su quel monitor ci stanno sotto, ognuno nel suo rettangolo. Il
        /// monitor 0 e' la finestra del gioco; gli altri sono display di Unity accesi con SplitScreen.ActivateDisplays (un canvas per monitor:
        /// Canvas.targetDisplay vale per tutto il canvas).</summary>
        sealed class DisplayRoot
        {
            public int display;
            public GameObject go;
            public RectTransform dividers;      // le righe fra le porzioni di questo monitor
            public GameObject cover;            // "in pausa" sui monitor oltre il primo (il menu sta sul primo)
            public Vector2Int size;             // la misura con cui sono stati disposti camere e HUD l'ultima volta
        }

        static readonly List<DisplayRoot> roots = new List<DisplayRoot>();

        /// <summary>Su quanti monitor sta lo schermo condiviso in corso (1: tutto nella finestra del gioco; fuori dallo split screen, 1).</summary>
        public static int Monitors => Mathf.Max(1, roots.Count);

        static DisplayRoot RootOf(int display) => display >= 0 && display < roots.Count ? roots[display] : (roots.Count > 0 ? roots[0] : null);

        /// <summary>Avvia lo split screen con 'count' giocatori (2..4): il giocatore della scena diventa il posto 0 e nascono gli altri.
        /// Va chiamato dal menu, prima di "Gioca" (il piano non e' ancora cominciato). I monitor che si usano sono quelli voluti
        /// (SplitScreen.WantedMonitors: la scelta del menu, o -splitmonitors) fra quelli collegati.</summary>
        public static bool Begin(int count, bool p1Pad, out string error, IList<PryingEyes.Characters.CharacterRecord> cast = null)
        {
            error = null;
            var gm = GameManager.I;
            if (Split) { error = "Lo split screen e' gia' avviato"; return false; }
            if (count < SplitScreen.MinPlayers || count > SplitScreen.MaxPlayers) { error = "Da due a quattro giocatori"; return false; }
            if (gm == null || gm.player == null || gm.player.cam == null || gm.player.weapon == null || gm.hud == null) { error = "Il gioco non e' pronto"; return false; }
            if (!GameManager.proc) { error = "Lo split screen c'e' solo ai piani dell'hotel"; return false; }
            if (Coop.Active || Coop.WaitingForHost) { error = "C'e' gia' una partita in rete"; return false; }

            All.Clear(); byRoot.Clear();
            // chi gioca in ogni posto: i personaggi scelti (o, dove manca la scelta, il locale e poi i piu' recenti non ancora presi), con un
            // aspetto diverso per ognuno; il posto 0 e' il personaggio locale e la sua cattedrale e' quella gia' in uso
            var chosen = ChooseCast(count, cast, out var looks);
            PryingEyes.Characters.Roster.SetLocal(chosen[0]);
            Cattedrale.Fabbrica.Seleziona(chosen[0]);
            var proto = gm.player;
            var s0 = new Seat(0)
            {
                character = chosen[0], look = looks[0], cathedral = Cattedrale.Fabbrica.Corrente,
                keyboard = true, player = proto, hud = gm.hud, camera = proto.cam.GetComponent<Camera>(), pad = PadControls.Primary,
                stealth = Stealth.Current, medals = Medals.Current, interact = Proc.Interact.Mine, wallBuy = WallBuy.Mine,
                score = gm.score, kills = gm.kills,
                shotsFired = GameManager.shotsFired, shotsHit = GameManager.shotsHit, damageTaken = GameManager.damageTaken,
                headshots = GameManager.headshots, deaths = GameManager.deaths,
                equipment = Equipment.I, knife = Knife.I, torch = Torch.I, guide = Guide.I, rig = StealthRig.I, prove = Prove.I,
            };
            s0.target = Targets.Local;
            All.Add(s0);
            cur = s0;                          // i globali sono gia' i suoi
            proto.weapon.SetViewLayer(s0.viewLayer);       // arma e mani del giocatore 1 sul suo strato: le camere degli altri non lo disegnano

            // i monitor: la finestra del gioco e, se il giocatore li vuole e ci sono, gli altri; un canvas per monitor
            int monitors = SplitScreen.MonitorsFor(count, SplitScreen.WantedMonitors);
            SplitScreen.ActivateDisplays(monitors);
            EnsureRoots(monitors);
            s0.display = 0;
            for (int i = 1; i < count; i++)
            {
                var s = Build(i, s0, SplitScreen.DisplayOf(i, count, monitors), chosen[i], looks[i]);
                if (s == null) { End(); error = "Non riesco a creare il giocatore " + (i + 1); return false; }
                All.Add(s);
                Targets.Add(s.target);
            }
            AssignPads(p1Pad);
            // ognuno vede gli altri: un manichino per posto, su uno strato che la sua camera non disegna (Layout)
            foreach (var s in All) s.avatar = SeatAvatar.Create(s);
            Layout(true);
            if (monitors > 1)
                Debug.Log($"[PryingEyes] split: {count} giocatori su {monitors} monitor ({SplitScreen.DescribeDisplays(monitors)}; sul monitor: {DisplayCounts(count, monitors)}), controller " + PadSummary());
            else
                Debug.Log($"[PryingEyes] split: {count} giocatori in una sola finestra, schermo {Screen.width}x{Screen.height}, controller " + PadSummary());
            return true;
        }

        /// <summary>I personaggi dei 'count' posti. 'cast' e' la scelta del menu (puo' mancare o avere buchi): il posto 0 senza scelta e' il
        /// personaggio locale; gli altri che non hanno scelto prendono il piu' recente non ancora in campo, e se non ce ne sono si crea un
        /// ospite ("Giocatore 3"). Nessun personaggio compare due volte. 'looks' sono gli aspetti in questa partita: mai due uguali.</summary>
        static List<PryingEyes.Characters.CharacterRecord> ChooseCast(int count, IList<PryingEyes.Characters.CharacterRecord> cast, out List<string> looks)
        {
            var roster = PryingEyes.Characters.Roster.All;
            var result = new List<PryingEyes.Characters.CharacterRecord>();
            var taken = new HashSet<string>();
            looks = new List<string>();
            for (int i = 0; i < count; i++)
            {
                var pick = cast != null && i < cast.Count ? cast[i] : null;
                if (pick == null && i == 0) pick = PryingEyes.Characters.Roster.Local;
                if (pick != null && result.Contains(pick)) pick = null;
                if (pick == null)
                    foreach (var r in roster)
                        if (!result.Contains(r) && (pick == null || string.CompareOrdinal(r.savedAt, pick.savedAt) > 0)) pick = r;
                if (pick == null) pick = PryingEyes.Characters.Roster.CreateGuest(i, taken);
                if (pick == null) pick = new PryingEyes.Characters.CharacterRecord { id = "ospite-" + i, name = "Giocatore " + (i + 1) };      // il disco non scrive: si gioca lo stesso, senza salvare
                result.Add(pick);
                string look = PryingEyes.Characters.CharacterLooks.Resolve(pick.look, taken);
                taken.Add(look);
                looks.Add(look);
            }
            return result;
        }

        static string DisplayCounts(int count, int monitors)
        {
            var sb = new System.Text.StringBuilder();
            for (int d = 0; d < monitors; d++) { if (d > 0) sb.Append('+'); sb.Append(SplitScreen.PlayersOn(d, count, monitors)); }
            return sb.ToString();
        }

        /// <summary>Finisce lo split screen (si torna al menu: la scena viene ricaricata, qui si azzera lo stato statico).</summary>
        public static void End()
        {
            if (All.Count == 0 && cur == null) return;
            var s0 = Primary;
            if (s0 != null && cur != s0) Install(s0);
            for (int i = 0; i < All.Count; i++) if (All[i].target != null && i > 0) Targets.Remove(All[i].target);
            for (int i = 0; i < All.Count; i++) if (All[i].avatar != null) { Object.Destroy(All[i].avatar.gameObject); All[i].avatar = null; }
            for (int i = All.Count - 1; i >= 1; i--) if (All[i].root != null) Object.Destroy(All[i].root.gameObject);
            if (s0 != null && s0.camera != null) { s0.camera.rect = new Rect(0f, 0f, 1f, 1f); s0.camera.targetDisplay = 0; }
            if (s0 != null) s0.display = 0;
            All.Clear(); byRoot.Clear(); cur = null;
            PadControls.ClearChannels();
            foreach (var r in roots) if (r.go != null) Object.Destroy(r.go);
            bool wasMulti = roots.Count > 1;
            roots.Clear();
            if (wasMulti) SplitScreen.Release();       // i monitor accesi non si possono spegnere: le loro finestre si nascondono
        }

        // ------------------------------------------------------------------ controller
        /// <summary>Un controller a ciascuno: i posti 2..N prendono i collegati in ordine; il posto 1 resta su tastiera e mouse (ma adotta da solo
        /// il primo controller libero su cui si preme un tasto), oppure ne prende uno subito se p1Pad.</summary>
        static void AssignPads(bool p1Pad)
        {
            var slots = SplitScreen.AssignPads(PadControls.ConnectedMask(), All.Count, p1Pad);
            for (int i = 0; i < All.Count; i++)
            {
                var s = All[i];
                if (s.pad == null) s.pad = PadControls.NewChannel();
                if (i == 0)
                {
                    if (slots[0] >= 0) PadControls.Enable(slots[0]);
                    else { PadControls.Disable(); PadControls.EnableAuto(); }
                }
                else PadControls.Assign(s.pad, slots[i]);
            }
        }

        public static string PadSummary()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < All.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var ch = All[i].pad;
                sb.Append(i + 1).Append('=').Append(ch != null && ch.index >= 0 ? (ch.index + 1).ToString() : "-");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ costruzione di un posto
        static Seat Build(int index, Seat proto, int display, PryingEyes.Characters.CharacterRecord pg, string look)
        {
            var gm = GameManager.I;
            var pp = proto.player;
            var seat = new Seat(index) { keyboard = false, score = 500, display = display, character = pg, look = look, cathedral = Cattedrale.Fabbrica.Apri(pg) };
            var rootGo = new GameObject("Posto" + (index + 1));
            seat.root = rootGo.transform;
            byRoot[seat.root] = seat;
            seat.target = new SeatTarget(seat);

            using (Enter(seat))
            {
                // HUD: sotto il canvas del suo monitor, ognuno nel suo rettangolo
                var hudGo = new GameObject("HUD " + (index + 1), typeof(RectTransform));
                hudGo.transform.SetParent(RootOf(display).go.transform, false);
                var hud = hudGo.AddComponent<HUD>();
                hud.seat = seat;                                         // la mappa (Proc/Map/MapView) sta nell'HUD: segue il monitor del posto

                // corpo: inattivo finche' non e' montato, cosi' gli Awake vedono tutto al loro posto
                var p = new GameObject("Giocatore " + (index + 1));
                p.SetActive(false);
                p.layer = 2;                                         // Ignore Raycast: la propria capsula non blocca mai l'hitscan
                p.transform.SetParent(seat.root, false);
                p.transform.position = pp.transform.position;
                var cc = p.AddComponent<CharacterController>();
                cc.height = 1.75f; cc.radius = 0.38f; cc.center = new Vector3(0f, 0.875f, 0f);
                var ctrl = p.AddComponent<PlayerController>();
                ctrl.yaw = pp.yaw;

                var camGo = new GameObject("Camera");
                camGo.layer = 2;
                camGo.transform.SetParent(p.transform, false);
                camGo.transform.localPosition = new Vector3(0f, 1.62f, 0f);
                var cam = camGo.AddComponent<Camera>();
                cam.CopyFrom(proto.camera);
                cam.tag = "Untagged";                                // la principale e' quella del giocatore 1 (e' l'unica con l'AudioListener)
                cam.targetDisplay = display;                         // il suo monitor (Layout lo rimette a ogni cambio di misura)
                CopyUrp(proto.camera, cam);
                ctrl.cam = camGo.transform;

                var w = p.AddComponent<Weapon>();
                w.bodyMat = pp.weapon.bodyMat; w.gripMat = pp.weapon.gripMat; w.flashMat = pp.weapon.flashMat;

                seat.player = ctrl; seat.hud = hud; seat.camera = cam;
                Reload();
                w.Bind(camGo.transform, hud);
                ctrl.weapon = w;
                ctrl.hud = hud;
                hud.SetAmmo(30, 120);
                hud.SetBars(100, 50);
                hud.SetScore(seat.score);                            // senza questo il tabellone del giocatore 2..4 dice 0 fino al primo guadagno
                p.SetActive(true);                                   // Awake: avvolti nello stesso posto
                ctrl.SetSpawn(pp.transform.position);
            }
            return seat;
        }

        /// <summary>Le impostazioni URP della camera principale (post-processing, anti-aliasing, volumi) alle altre.</summary>
        static void CopyUrp(Camera src, Camera dst)
        {
            var sd = src.GetUniversalAdditionalCameraData();
            var dd = dst.GetUniversalAdditionalCameraData();
            dd.renderPostProcessing = sd.renderPostProcessing;
            dd.antialiasing = sd.antialiasing;
            dd.antialiasingQuality = sd.antialiasingQuality;
            dd.volumeLayerMask = sd.volumeLayerMask;
            dd.renderShadows = sd.renderShadows;
            dd.requiresDepthOption = sd.requiresDepthOption;
            dd.requiresColorOption = sd.requiresColorOption;
            dd.stopNaN = sd.stopNaN;
            dd.dithering = sd.dithering;
        }

        // ------------------------------------------------------------------ schermo
        /// <summary>Un canvas per monitor usato: il primo e' quello della finestra del gioco (e ha l'autista che rifa il layout quando cambia
        /// una misura), gli altri vanno ai monitor accesi.</summary>
        static void EnsureRoots(int monitors)
        {
            for (int d = roots.Count; d < monitors; d++)
            {
                var r = new DisplayRoot { display = d };
                r.go = new GameObject(d == 0 ? "SchermoDiviso" : "SchermoDiviso " + (d + 1), typeof(RectTransform));
                var canvas = r.go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 0;
                canvas.targetDisplay = d;
                if (d == 0) r.go.AddComponent<SeatsDriver>();
                // le righe che dividono le porzioni
                var lines = new GameObject("Linee", typeof(RectTransform));
                lines.transform.SetParent(r.go.transform, false);
                r.dividers = lines.GetComponent<RectTransform>();
                r.dividers.anchorMin = Vector2.zero; r.dividers.anchorMax = Vector2.one; r.dividers.offsetMin = r.dividers.offsetMax = Vector2.zero;
                r.dividers.SetAsLastSibling();
                if (d > 0) r.cover = BuildCover(r.go.transform);
                roots.Add(r);
            }
        }

        /// <summary>Il velo "in pausa" dei monitor oltre il primo: il menu e la pausa stanno nella finestra del gioco; qui il mondo e' fermo e gli HUD
        /// nascosti, e il velo lo dice.</summary>
        static GameObject BuildCover(Transform parent)
        {
            var go = new GameObject("Pausa", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var veil = go.AddComponent<Image>();
            veil.color = new Color(0.018f, 0.035f, 0.039f, 0.78f); veil.raycastTarget = false;
            var tgo = new GameObject("Testo", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(1000f, 160f);
            var t = tgo.AddComponent<Text>();
            t.font = HUD.Serif(); t.fontSize = 64; t.alignment = TextAnchor.MiddleCenter; t.color = new Color(0.86f, 0.80f, 0.68f);
            t.text = "IN PAUSA"; t.raycastTarget = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>Camere e HUD nelle loro porzioni di schermo, ognuna dentro il suo monitor (si rifa se la misura di un monitor cambia).</summary>
        public static void Layout(bool force = false)
        {
            if (All.Count < 2 || roots.Count == 0) return;
            bool changed = force;
            foreach (var r in roots)
            {
                var size = SplitScreen.DisplaySize(r.display);
                if (size != r.size) { r.size = size; changed = true; }
            }
            if (!changed) return;
            int monitors = roots.Count;
            int baseMask = All[0].camera != null ? All[0].camera.cullingMask : -1;
            for (int i = 0; i < All.Count; i++)
            {
                var s = All[i];
                s.display = SplitScreen.DisplayOf(i, All.Count, monitors);
                s.view = SplitScreen.ViewOn(i, All.Count, monitors);
                if (s.camera != null)
                {
                    s.camera.targetDisplay = s.display;
                    s.camera.rect = s.view;
                    // la sua arma e le sue mani, i corpi degli altri e non il suo
                    s.camera.cullingMask = (baseMask & ~AllViewLayersMask & ~AllAvatarLayersMask) | (1 << s.viewLayer)
                                           | (AllAvatarLayersMask & ~(1 << (FirstAvatarLayer + s.index)));
                }
                PlaceHud(s);
            }
            foreach (var r in roots) BuildDividers(r, SplitScreen.PlayersOn(r.display, All.Count, monitors));
        }

        /// <summary>Il rapporto larghezza/altezza della porzione di schermo del posto, in pixel veri del suo monitor (per il campo visivo); 'fallback'
        /// se il monitor non ha misura (prove senza finestra).</summary>
        public static float ViewAspect(Seat s, float fallback)
        {
            var root = s != null ? RootOf(s.display) : null;
            if (root == null || root.size.x <= 0 || root.size.y <= 0) return fallback;
            float w = s.view.width * root.size.x, h = s.view.height * root.size.y;
            return h > 0.5f && w > 0.5f ? w / h : fallback;
        }

        static void PlaceHud(Seat s)
        {
            if (s.hud == null) return;
            var root = RootOf(s.display);
            if (root == null) return;
            if (s.hud.transform.parent != root.go.transform) s.hud.transform.SetParent(root.go.transform, false);
            var rt = s.hud.GetComponent<RectTransform>();
            if (rt == null) return;
            var px = root.size;
            var size = new Vector2(s.view.width * px.x, s.view.height * px.y);
            // lo stesso adattamento del CanvasScaler dell'HUD da solo (riferimento 1280x720, "Expand"): il layout non cambia, solo la scala
            float scale = Mathf.Max(0.05f, Mathf.Min(size.x / 1280f, size.y / 720f));
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = new Vector3(scale, scale, 1f);
            rt.sizeDelta = size / scale;
            rt.anchoredPosition = new Vector2((s.view.x + s.view.width * 0.5f - 0.5f) * px.x, (s.view.y + s.view.height * 0.5f - 0.5f) * px.y);
            s.hud.transform.SetAsFirstSibling();
        }

        /// <summary>Le righe fra le porzioni di un monitor, secondo quanti giocatori ci stanno ('n': 1 nessuna, 2 una verticale, 3 e 4 anche
        /// una orizzontale).</summary>
        static void BuildDividers(DisplayRoot r, int n)
        {
            if (r.dividers == null) return;
            for (int i = r.dividers.childCount - 1; i >= 0; i--) Object.Destroy(r.dividers.GetChild(i).gameObject);
            if (n < 2) return;
            // linea verticale al centro (2, 3 e 4 giocatori: sempre), orizzontale a meta' (3 e 4)
            var v = Line(r, "Verticale", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), true);
            if (n >= 3) Line(r, "Orizzontale", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), false);
            if (n == 3 && v != null) { v.anchorMin = new Vector2(0.5f, 0.5f); v.anchorMax = new Vector2(0.5f, 1f); }       // il terzo sta sotto a tutta larghezza: la verticale sale solo nella parte alta
        }

        static RectTransform Line(DisplayRoot r, string name, Vector2 a, Vector2 b, bool vertical)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(r.dividers, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = a; rt.anchorMax = b;
            rt.sizeDelta = vertical ? new Vector2(3f, 0f) : new Vector2(0f, 3f);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.92f);
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>Per le prove: il velo "in pausa" del monitor e' acceso.</summary>
        public static bool PauseCoverShown(int display)
        {
            var r = display > 0 && display < roots.Count ? roots[display] : null;
            return r != null && r.cover != null && r.cover.activeSelf;
        }

        /// <summary>Mostra o nasconde gli HUD di tutti (il menu li copre); sui monitor oltre il primo, al posto degli HUD, il velo "in pausa".</summary>
        public static void SetHudsVisible(bool visible)
        {
            foreach (var s in All)
            {
                if (s.hud == null) continue;
                var c = s.hud.GetComponent<Canvas>();
                if (c != null) c.enabled = visible;
            }
            foreach (var r in roots)
            {
                if (r.dividers != null) r.dividers.gameObject.SetActive(visible);
                if (r.cover != null) r.cover.SetActive(!visible);
            }
        }

    }

    /// <summary>Sta sul canvas del primo monitor dello split screen: ogni fotogramma, se un monitor cambia misura, rifa le porzioni; e nei primi
    /// secondi dopo l'accensione di un monitor rimette il fuoco sulla finestra del gioco.</summary>
    sealed class SeatsDriver : MonoBehaviour
    {
        void LateUpdate() { Seats.Layout(); SplitScreen.Tick(); }
    }
}

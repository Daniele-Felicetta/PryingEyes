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
        static GameObject hudRoot;            // canvas Overlay condiviso: gli HUD dei posti ci stanno sotto, ognuno nel suo rettangolo
        static RectTransform dividers;
        static Vector2Int laidOut;

        /// <summary>Avvia lo split screen con 'count' giocatori (2..4): il giocatore della scena diventa il posto 0 e nascono gli altri.
        /// Va chiamato dal menu, prima di "Gioca" (il piano non e' ancora cominciato).</summary>
        public static bool Begin(int count, bool p1Pad, out string error)
        {
            error = null;
            var gm = GameManager.I;
            if (Split) { error = "Lo split screen e' gia' avviato"; return false; }
            if (count < SplitScreen.MinPlayers || count > SplitScreen.MaxPlayers) { error = "Da due a quattro giocatori"; return false; }
            if (gm == null || gm.player == null || gm.player.cam == null || gm.player.weapon == null || gm.hud == null) { error = "Il gioco non e' pronto"; return false; }
            if (!GameManager.proc) { error = "Lo split screen c'e' solo ai piani dell'hotel"; return false; }
            if (Coop.Active || Coop.WaitingForHost) { error = "C'e' gia' una partita in rete"; return false; }

            All.Clear(); byRoot.Clear();
            var proto = gm.player;
            var s0 = new Seat(0)
            {
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

            EnsureHudRoot();
            for (int i = 1; i < count; i++)
            {
                var s = Build(i, s0);
                if (s == null) { End(); error = "Non riesco a creare il giocatore " + (i + 1); return false; }
                All.Add(s);
                Targets.Add(s.target);
            }
            AssignPads(p1Pad);
            Layout(true);
            Debug.Log($"[PryingEyes] split: {count} giocatori in una sola finestra, schermo {Screen.width}x{Screen.height}, controller " + PadSummary());
            return true;
        }

        /// <summary>Finisce lo split screen (si torna al menu: la scena viene ricaricata, qui si azzera lo stato statico).</summary>
        public static void End()
        {
            if (All.Count == 0 && cur == null) return;
            var s0 = Primary;
            if (s0 != null && cur != s0) Install(s0);
            for (int i = 0; i < All.Count; i++) if (All[i].target != null && i > 0) Targets.Remove(All[i].target);
            for (int i = All.Count - 1; i >= 1; i--) if (All[i].root != null) Object.Destroy(All[i].root.gameObject);
            if (s0 != null && s0.camera != null) s0.camera.rect = new Rect(0f, 0f, 1f, 1f);
            All.Clear(); byRoot.Clear(); cur = null;
            PadControls.ClearChannels();
            if (hudRoot != null) Object.Destroy(hudRoot);
            hudRoot = null; dividers = null; laidOut = default;
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
        static Seat Build(int index, Seat proto)
        {
            var gm = GameManager.I;
            var pp = proto.player;
            var seat = new Seat(index) { keyboard = false, score = 500 };
            var rootGo = new GameObject("Posto" + (index + 1));
            seat.root = rootGo.transform;
            byRoot[seat.root] = seat;
            seat.target = new SeatTarget(seat);

            using (Enter(seat))
            {
                // HUD: sotto il canvas condiviso, ognuno nel suo rettangolo
                var hudGo = new GameObject("HUD " + (index + 1), typeof(RectTransform));
                hudGo.transform.SetParent(hudRoot.transform, false);
                var hud = hudGo.AddComponent<HUD>();
                hud.seat = seat;

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
        static void EnsureHudRoot()
        {
            if (hudRoot != null) return;
            hudRoot = new GameObject("SchermoDiviso", typeof(RectTransform));
            var canvas = hudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            hudRoot.AddComponent<SeatsDriver>();
            // le righe che dividono le porzioni
            var lines = new GameObject("Linee", typeof(RectTransform));
            lines.transform.SetParent(hudRoot.transform, false);
            dividers = lines.GetComponent<RectTransform>();
            dividers.anchorMin = Vector2.zero; dividers.anchorMax = Vector2.one; dividers.offsetMin = dividers.offsetMax = Vector2.zero;
            dividers.SetAsLastSibling();
        }

        /// <summary>Camere e HUD nelle loro porzioni di schermo (si rifa se la finestra cambia misura).</summary>
        public static void Layout(bool force = false)
        {
            if (All.Count < 2) return;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (!force && size == laidOut) return;
            laidOut = size;
            int baseMask = All[0].camera != null ? All[0].camera.cullingMask : -1;
            for (int i = 0; i < All.Count; i++)
            {
                var s = All[i];
                s.view = SplitScreen.ViewOf(i, All.Count);
                if (s.camera != null)
                {
                    s.camera.rect = s.view;
                    s.camera.cullingMask = (baseMask & ~AllViewLayersMask) | (1 << s.viewLayer);
                }
                PlaceHud(s, size);
            }
            BuildDividers();
        }

        static void PlaceHud(Seat s, Vector2Int px)
        {
            if (s.hud == null) return;
            if (hudRoot != null && s.hud.transform.parent != hudRoot.transform) s.hud.transform.SetParent(hudRoot.transform, false);
            var rt = s.hud.GetComponent<RectTransform>();
            if (rt == null) return;
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

        static void BuildDividers()
        {
            if (dividers == null) return;
            for (int i = dividers.childCount - 1; i >= 0; i--) Object.Destroy(dividers.GetChild(i).gameObject);
            int n = All.Count;
            // linea verticale al centro (2, 3 e 4 giocatori: sempre), orizzontale a meta' (3 e 4)
            Line("Verticale", new Vector2(0.5f, 0f), new Vector2(0.5f, n >= 3 ? 1f : 1f), true);
            if (n >= 3) Line("Orizzontale", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), false);
            if (n == 3) { /* il terzo sta sotto a tutta larghezza: la verticale sale solo nella parte alta */
                var v = dividers.Find("Verticale") as RectTransform;
                if (v != null) { v.anchorMin = new Vector2(0.5f, 0.5f); v.anchorMax = new Vector2(0.5f, 1f); }
            }
        }

        static void Line(string name, Vector2 a, Vector2 b, bool vertical)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(dividers, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = a; rt.anchorMax = b;
            rt.sizeDelta = vertical ? new Vector2(3f, 0f) : new Vector2(0f, 3f);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.92f);
            img.raycastTarget = false;
        }

        /// <summary>Mostra o nasconde gli HUD di tutti (il menu li copre).</summary>
        public static void SetHudsVisible(bool visible)
        {
            foreach (var s in All)
            {
                if (s.hud == null) continue;
                var c = s.hud.GetComponent<Canvas>();
                if (c != null) c.enabled = visible;
            }
            if (dividers != null) dividers.gameObject.SetActive(visible);
        }

    }

    /// <summary>Sta sul canvas condiviso dello split screen: ogni fotogramma, se la finestra cambia misura, rifa le porzioni.</summary>
    sealed class SeatsDriver : MonoBehaviour
    {
        void LateUpdate() { Seats.Layout(); }
    }
}

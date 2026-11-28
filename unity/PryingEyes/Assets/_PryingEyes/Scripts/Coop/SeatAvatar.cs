using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Il corpo di un giocatore dello schermo condiviso come lo vedono gli altri (dalla 0.7.1: prima ognuno aveva solo le braccia in
    /// prima persona e nessuno vedeva nessuno), e dalla terza persona anche il corpo che il giocatore vede da dietro. E' un sopravvissuto
    /// vero (SurvivorBody: l'aspetto dipende dal posto) che imbraccia l'arma in mano al giocatore, con il nome sopra la testa, girato ogni
    /// volta verso la camera che disegna; se i modelli mancano resta il manichino dei compagni della cooperativa (RemotePlayer.BuildBody).
    /// Sta sullo strato del suo posto (Seats.FirstAvatarLayer + indice), che la sua camera non disegna (Seats.Layout) salvo in terza persona
    /// (ThirdPersonView): ognuno vede gli altri e, di norma, non se stesso. Segue il corpo vero (PlayerController) a fine fotogramma: niente
    /// rete, niente collider (colpi e nemici non lo vedono), niente luce (la torcia vera del posto c'e' gia').</summary>
    public sealed class SeatAvatar : MonoBehaviour
    {
        Seat seat;
        Transform visual, legL, legR, headT, torchT;
        SurvivorBody body;
        CharacterController cc;
        TextMesh label;
        float walkPhase, groundLost;
        Vector3 lastPos;
        bool placed;
        static int knifeIndex = -2;

        static readonly List<SeatAvatar> all = new List<SeatAvatar>();
        static bool hooked;

        public Seat Seat => seat;
        public SurvivorBody Body => body;
        public int Layer => Seats.FirstAvatarLayer + (seat != null ? Mathf.Clamp(seat.index, 0, Seats.MaxSeats - 1) : 0);

        /// <summary>Il corpo di un posto visto dagli altri (con il nome sopra la testa).</summary>
        public static SeatAvatar Create(Seat seat) => Create(seat, true);

        /// <summary>Il corpo del giocatore stesso per la terza persona (senza il nome): fuori dallo schermo condiviso il posto e' fittizio, con
        /// l'indice dello slot (quello di rete in cooperativa, 0 da soli).</summary>
        public static SeatAvatar CreateOwn(PlayerController p, int slot)
        {
            var seat = Seats.OfSlot(slot) ?? new Seat(Mathf.Clamp(slot, 0, Seats.MaxSeats - 1)) { player = p, stealth = Stealth.Current, character = PryingEyes.Characters.Roster.Local, look = Coop.MyLook };
            if (seat.player == null) seat.player = p;
            return Create(seat, false);
        }

        static SeatAvatar Create(Seat seat, bool named)
        {
            var go = new GameObject((named ? "Corpo visto dagli altri " : "Corpo in terza persona ") + (seat.index + 1));
            var a = go.AddComponent<SeatAvatar>();
            a.seat = seat;
            a.body = SurvivorBody.Create(go.transform, seat.index, a.Layer, seat.look);
            if (a.body != null) a.visual = a.body.transform;
            else a.visual = RemotePlayer.BuildBody(go.transform, seat.index, out a.legL, out a.legR, out a.headT, out a.torchT);
            if (named)
            {
                var tg = new GameObject("Nome");
                tg.transform.SetParent(go.transform, false);
                tg.transform.localPosition = new Vector3(0f, 2.15f, 0f);
                a.label = tg.AddComponent<TextMesh>();
                a.label.font = HUD.Serif();
                tg.GetComponent<MeshRenderer>().sharedMaterial = a.label.font.material;
                a.label.text = seat.Name;
                a.label.fontSize = 64; a.label.characterSize = 0.045f;
                a.label.anchor = TextAnchor.MiddleCenter; a.label.alignment = TextAlignment.Center;
                a.label.color = new Color(0.95f, 0.92f, 0.82f, 0.95f);
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = a.Layer;
            a.Follow(0f);
            return a;
        }

        void OnEnable()
        {
            all.Add(this);
            if (!hooked) { hooked = true; RenderPipelineManager.beginCameraRendering += FaceCamera; }
        }

        void OnDisable() { all.Remove(this); placed = false; groundLost = 0f; }

        /// <summary>Prima che ogni camera disegni, i nomi guardano lei (una camera per posto: un nome solo non puo' guardarle tutte).</summary>
        static void FaceCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || a.label == null) continue;
                Vector3 d = a.label.transform.position - eye;
                if (d.sqrMagnitude > 1e-6f) a.label.transform.rotation = Quaternion.LookRotation(d);
            }
        }

        void LateUpdate() { Follow(Time.deltaTime); }

        void Follow(float dt)
        {
            var p = seat != null ? seat.player : null;
            if (p == null) { if (visual != null && visual.gameObject.activeSelf) visual.gameObject.SetActive(false); return; }
            if (visual != null && !visual.gameObject.activeSelf) visual.gameObject.SetActive(true);
            if (cc == null) cc = p.GetComponent<CharacterController>();
            Vector3 pos = p.transform.position;
            Vector3 vel = Vector3.zero;
            if (placed && dt > 0f)
            {
                vel = (pos - lastPos) / dt;
                if (new Vector2(vel.x, vel.z).magnitude > 12f) vel = Vector3.zero;       // un teletrasporto non fa correre il corpo
            }
            placed = true;
            lastPos = pos;
            float heading = p.Riding ? p.RideHeading : p.OnClimbWall ? p.ClimbWallYaw : p.Swimming ? p.SwimYaw : p.yaw;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, heading * Mathf.Rad2Deg, 0f));
            if (visual == null) return;
            if (body != null)
            {
                // in sella la radice dello scheletro segue il telaio, rollio e beccheggio compresi: bacino, piedi e mani sono relativi alla moto
                var bk = p.Riding ? p.Bike : null;
                if (bk != null) visual.SetPositionAndRotation(bk.Body.position, bk.BodyRotation);
                else { visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity; }
            }
            if (body != null) DriveBody(p, vel, dt);
            else AnimateMannequin(p, dt, p.Riding ? 0f : Mathf.Min(6f, new Vector2(vel.x, vel.z).magnitude));
            if (label != null)
            {
                if (label.gameObject.activeSelf != p.alive) label.gameObject.SetActive(p.alive);
                string want = p.Downed ? seat.Name + "\nA TERRA " + Mathf.CeilToInt(p.BleedLeft) + " s" : seat.Name;
                if (label.text != want) label.text = want;
                label.color = p.Downed ? new Color(1f, 0.42f, 0.36f, 1f) : new Color(0.95f, 0.92f, 0.82f, 0.95f);
            }
        }

        /// <summary>Il corpo vero: velocita', sguardo, assetto e arma del giocatore; passo, braccia e dita le fa SurvivorBody.</summary>
        void DriveBody(PlayerController p, Vector3 vel, float dt)
        {
            var w = p.weapon;
            var inp = body.input;
            var bk = p.Riding ? p.Bike : null;
            inp.seated = bk != null;                                           // in sella: seduto, i piedi sulle pedane e le mani sulle manopole (SurvivorBody)
            if (bk != null) { inp.gripLeft = bk.GripWorld(0); inp.gripRight = bk.GripWorld(1); bk.FillSeat(ref inp); }
            inp.velocity = p.Riding ? Vector3.zero : vel;                      // in sella le gambe non camminano
            inp.pitch = p.pitch; inp.lean = p.lean;
            inp.crouch = p.IsCrouching && !p.Riding; inp.sprint = p.IsSprinting && !p.Riding;
            inp.sliding = p.IsSliding && !p.Riding;
            inp.alive = p.alive; inp.downed = p.Downed;
            bool supported = cc == null || cc.isGrounded || p.Mantling || p.OnClimbWall || p.Riding;
            groundLost = supported ? 0f : groundLost + dt;
            // Briefly keep the feet planted over a step down. An ascending jump and the apex stay airborne.
            inp.grounded = supported || (groundLost < 0.07f && vel.y <= 0f && vel.y > -2.2f);
            inp.climb = p.Mantling || p.OnClimbWall ? 1f : 0f;
            inp.gliding = p.Gliding;
            inp.swimming = p.Swimming;
            inp.swimPhase = p.SwimPhase;
            inp.swimEffort = p.SwimEffort;
            inp.swimSurface = p.SwimSurface;
            inp.swimTurn = p.SwimTurn;
            inp.swimSubmersion = p.SwimSubmersion;
            inp.swimDivePitch = p.SwimDivePitch;
            int idx = w != null ? w.CurrentIndex : -1;
            if (w != null && w.KnifeBusy)
            {
                if (knifeIndex == -2) knifeIndex = WeaponDefs.IndexOf("kn_coltello");
                if (knifeIndex >= 0) idx = knifeIndex;
            }
            inp.weapon = p.OnClimbWall || p.Gliding || p.Swimming ? -1 : idx;
            inp.ads = w != null && !w.KnifeBusy ? w.ads : 0f;
            inp.reloading = w != null && w.reloading;
            inp.shotAge = Mathf.Max(0f, Time.time - seat.stealth.lastShotTime);
            body.input = inp;
        }

        /// <summary>Il manichino di riserva: gambe che oscillano, accovacciato, a terra, morto (come i compagni della cooperativa).</summary>
        void AnimateMannequin(PlayerController p, float dt, float v)
        {
            walkPhase += dt * (3f + v * 1.6f);
            float swing = Mathf.Sin(walkPhase) * 32f * Mathf.Clamp01(v / 2.5f);
            if (legL != null) { legL.localRotation = Quaternion.Euler(swing, 0f, 0f); legR.localRotation = Quaternion.Euler(-swing, 0f, 0f); }
            float k = dt > 0f ? 1f - Mathf.Exp(-10f * dt) : 1f, k8 = dt > 0f ? 1f - Mathf.Exp(-8f * dt) : 1f;
            var vs = visual.localScale;
            visual.localScale = new Vector3(1f, Mathf.Lerp(vs.y, p.IsCrouching ? 0.72f : 1f, k), 1f);
            // a terra: semisdraiato sul fianco (0.7.2); morto: disteso
            Quaternion pose = !p.alive ? Quaternion.Euler(-88f, 0f, 0f) : p.Downed ? Quaternion.Euler(-62f, 0f, 14f) : Quaternion.identity;
            Vector3 lift = !p.alive ? new Vector3(0f, 0.18f, 0f) : p.Downed ? new Vector3(0f, 0.12f, -0.15f) : Vector3.zero;
            visual.localRotation = Quaternion.Slerp(visual.localRotation, pose, k8);
            visual.localPosition = Vector3.Lerp(visual.localPosition, lift, k8);
        }
    }
}

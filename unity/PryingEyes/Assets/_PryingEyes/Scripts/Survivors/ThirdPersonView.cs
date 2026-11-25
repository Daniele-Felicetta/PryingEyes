using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>La visuale in terza persona (impostazione "Prospettiva" del menu, GameSettings.ThirdPerson; predefinita: prima persona).
    /// La camera si mette dietro la spalla destra del proprio personaggio, che si vede intero (SeatAvatar, un sopravvissuto vero) con
    /// l'arma in mano; arma e mani della prima persona si nascondono. Il gioco non se ne accorge: l'occhio (PlayerController.cam) resta dov'e'
    /// e da li' partono colpi, prompt e torcia; solo che, per far cadere i colpi dove sta il mirino della camera, subito dopo averlo
    /// messo in posa lo si gira verso il punto che il mirino inquadra (il raggio dalla camera: senza questo il mirino sarebbe sfasato di una
    /// spanna a ogni distanza). La camera vera viene spostata solo mentre disegna (beginCameraRendering/endCameraRendering) e rimessa
    /// all'occhio subito dopo: nessun altro codice la vede altrove. Con un'ottica ingrandita in mira si torna alla prima persona (il
    /// mirino ingrandito e' la seconda camera dell'arma); da morti pure. Un componente per giocatore (un posto dello schermo condiviso ha il
    /// suo), creato la prima volta che serve e tolto quando la terza persona e' spenta.</summary>
    public sealed class ThirdPersonView : MonoBehaviour
    {
        /// <summary>Quanti componenti vivono: PlayerController chiama AfterEyePose solo se serve (impostazione accesa o uno da spegnere).</summary>
        public static int Count { get; private set; }

        const float Radius = 0.20f, MaxTurn = 25f, BodyClearance = 0.66f, RiderClearance = 1.25f;

        PlayerController player;
        Camera cam;
        SeatAvatar avatar;
        bool ownAvatar;
        float blend;                          // 0 prima persona .. 1 terza persona
        int computedFrame = -10;
        Vector3 renderPos;
        Quaternion renderRot;
        Pose rideFrom;
        float rideTransition = 1f;
        float boomDistance, followHeight;
        bool showBody;
        // la camera mentre si disegna
        bool swapped;
        Vector3 savedPos; Quaternion savedRot; int savedMask;
        // la luce di riempimento del corpo (vedi UpdateFill)
        const float FillIntensity = 5f, FillRange = 7f;
        const float FillLitFrom = 0.04f, FillLitTo = 0.4f;       // Stealth.PlayerLum: sotto il primo valore la luce e' piena, sopra il secondo e' spenta
        static readonly Vector3 FillOffset = new Vector3(0.5f, 0.9f, -2.1f);
        Light fill;
        uint fillMask;

        /// <summary>Solo per le prove: moltiplica la luce di riempimento (0 = spenta; le foto al buio con e senza).</summary>
        public static float fillScale = 1f;

        /// <summary>Quanto e' in terza persona adesso (0..1): la HUD o altri possono adattarsi.</summary>
        public float Blend => blend;
        /// <summary>La luce di riempimento del proprio corpo (nulla finche' non serve).</summary>
        public Light Fill => fill;

        public static ThirdPersonView Of(PlayerController p) => p != null && p.TryGetComponent(out ThirdPersonView v) ? v : null;

        /// <summary>Dove sta la volata dell'arma del corpo che si vede in terza persona: le tracce dei colpi partono da li', non dall'arma
        /// nascosta della prima persona (che sta a un palmo dall'occhio, cioe' dietro la spalla del personaggio).</summary>
        public static bool MuzzleOf(PlayerController p, out Vector3 pos)
        {
            pos = default;
            var v = Of(p);
            if (v == null || !v.showBody || v.avatar == null || v.avatar.Body == null || !v.avatar.Body.HasWeapon) return false;
            pos = v.avatar.Body.Muzzle.position;
            return true;
        }

        /// <summary>Chiamata da PlayerController appena ha messo in posa l'occhio, prima che l'arma spari o un prompt guardi.</summary>
        public static void AfterEyePose(PlayerController p, Vector3 stableEye, Vector3 lookAngles)
        {
            if (p == null || p.cam == null) return;
            if (!p.TryGetComponent(out ThirdPersonView v))
            {
                if (!GameSettings.ThirdPerson) return;
                v = p.gameObject.AddComponent<ThirdPersonView>();
            }
            v.Compute(p, stableEye, lookAngles);
        }

        public static void Suspend(PlayerController p)
        {
            if (p.TryGetComponent(out ThirdPersonView view)) view.ClearView();
        }

        public static void BeginRideTransition(PlayerController p)
        {
            if (p.cam == null || !p.cam.TryGetComponent(out Camera camera)) return;
            var view = Of(p);
            if (view == null) view = p.gameObject.AddComponent<ThirdPersonView>();
            view.player = p;
            view.cam = camera;
            if (Time.frameCount - view.computedFrame > 1)
            {
                view.renderPos = p.cam.position;
                view.renderRot = p.cam.rotation;
            }
            view.rideFrom = new Pose(view.renderPos, view.renderRot);
            view.rideTransition = 0f;
            view.computedFrame = Time.frameCount;
        }

        void OnEnable()
        {
            Count++;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        void OnDisable()
        {
            Count--;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            if (swapped && cam != null) Restore(cam);
            DropAvatar();
            if (fill != null) Destroy(fill.gameObject);
            fill = null; fillMask = 0;
        }

        void DropAvatar()
        {
            if (ownAvatar && avatar != null) Destroy(avatar.gameObject);
            avatar = null; ownAvatar = false;
        }

        static Seat SeatOf(PlayerController p)
        {
            for (int i = 0; i < Seats.All.Count; i++) if (Seats.All[i].player == p) return Seats.All[i];
            return null;
        }

        int ViewLayer()
        {
            var s = SeatOf(player);
            return s != null ? s.viewLayer : 2;
        }

        bool ScopedAim()
        {
            var w = player.weapon;
            return w != null && w.ads > 0.5f && w.Def.zoom >= 1.4f;
        }

        void EnsureAvatar()
        {
            int slot = Mathf.Clamp(Seats.Slot, 0, Seats.MaxSeats - 1);
            if (avatar != null && ownAvatar && avatar.Seat != null && avatar.Seat.index != slot) DropAvatar();      // lo slot di rete e' arrivato dopo
            if (avatar != null) return;
            var seat = SeatOf(player);
            if (seat != null && seat.avatar != null) { avatar = seat.avatar; ownAvatar = false; return; }
            avatar = SeatAvatar.CreateOwn(player, slot);
            ownAvatar = true;
        }

        void ClearView()
        {
            blend = 0f;
            showBody = false;
            rideTransition = 1f;
            computedFrame = -10;
            if (fill != null) fill.enabled = false;
            if (player.Bike != null) player.Bike.SetRiderCamera(false);
            if (!GameSettings.ThirdPerson || !player.alive) DropAvatar();
            else if (ownAvatar && avatar != null && avatar.gameObject.activeSelf) avatar.gameObject.SetActive(false);
            if (!GameSettings.ThirdPerson) Destroy(this);
        }

        void Compute(PlayerController p, Vector3 stableEye, Vector3 lookAngles)
        {
            player = p;
            cam = p.cam != null ? p.cam.GetComponent<Camera>() : null;
            if (cam == null) return;
            float dt = Time.deltaTime;
            bool riding = p.Riding;
            bool continuing = blend > 0f && rideTransition > 0f && computedFrame == Time.frameCount - 1;
            blend = Mathf.MoveTowards(blend, GameSettings.ThirdPerson && (riding || !ScopedAim()) ? 1f : 0f, dt * 5f);
            if (blend <= 0.001f)
            {
                if (rideTransition < 1f) SetRenderPose(p.cam.position, p.cam.rotation);
                else ClearView();
                return;
            }
            EnsureAvatar();
            if (avatar != null && !avatar.gameObject.activeSelf) avatar.gameObject.SetActive(true);

            Transform eyeT = p.cam;
            Vector3 eye = eyeT.position;
            float ads = !riding && p.weapon != null ? p.weapon.ads : 0f;
            Vector3 framing = GameSettings.ThirdPersonFraming;
            Vector3 offset = new Vector3(framing.x * Mathf.Lerp(1f, 2f / 3f, ads), framing.y + 0.24f * ads,
                -framing.z * Mathf.Lerp(1f, 1.18f / GameSettings.ThirdDistanceDefault, ads));
            if (riding) { offset.y += 0.52f; offset.z -= 0.7f; }
            else if (p.IsCrouching) offset.z *= 0.9f;
            followHeight = continuing && Mathf.Abs(followHeight - stableEye.y) < 0.6f
                ? Mathf.Lerp(followHeight, stableEye.y, 1f - Mathf.Exp(-14f * dt))
                : stableEye.y;
            stableEye.y = Mathf.Clamp(followHeight, stableEye.y - 0.20f, stableEye.y + 0.20f);
            float boomPitch = Mathf.Clamp(lookAngles.x, -10f, 25f);
            Vector3 desired = stableEye + Quaternion.Euler(boomPitch, lookAngles.y, 0f) * offset;
            Vector3 dir = desired - eye;
            float len = dir.magnitude;
            dir /= len;
            float dist = UnobstructedDistance(eye, dir, len);
            boomDistance = continuing ? Mathf.Min(dist, boomDistance + 4f * dt) : dist;
            float bodyClearance = riding ? RiderClearance : BodyClearance;
            float clearance = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(bodyClearance - 0.11f, bodyClearance + 0.29f, boomDistance));
            float viewBlend = blend * clearance;
            SetRenderPose(eye + dir * (boomDistance * viewBlend),
                Quaternion.Slerp(eyeT.rotation, Quaternion.Euler(lookAngles), viewBlend));
            showBody = boomDistance * viewBlend > bodyClearance && (renderPos - eye).sqrMagnitude > bodyClearance * bodyClearance;
            if (riding) p.Bike.SetRiderCamera(showBody);

            // Use the rendered ray even while the boom is moving or retracting.
            Vector3 fwd = renderRot * Vector3.forward;
            int mask = HotelNav.EnvMask | (1 << HotelNav.EnemyLayer);
            Vector3 target = Physics.Raycast(renderPos, fwd, out var th, 150f, mask, QueryTriggerInteraction.Ignore) ? th.point : renderPos + fwd * 150f;
            Vector3 toTarget = target - eye;
            if (toTarget.sqrMagnitude > 0.04f)
            {
                Quaternion conv = Quaternion.FromToRotation(fwd, toTarget.normalized);
                conv.ToAngleAxis(out float ang, out Vector3 ax);
                if (ang > MaxTurn) conv = Quaternion.AngleAxis(MaxTurn, ax);
                eyeT.rotation = conv * renderRot;
            }
            UpdateFill(eye);
        }

        static float UnobstructedDistance(Vector3 eye, Vector3 dir, float len)
        {
            float dist = len;
            if (Physics.SphereCast(eye, Radius, dir, out var hit, len, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0f, hit.distance - 0.05f);
            // A sphere cast can miss a wall already touching the sphere at its origin.
            if (Physics.CheckSphere(eye, Radius, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) &&
                Physics.Raycast(eye, dir, out var edge, len, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Min(dist, Mathf.Max(0f, edge.distance - Radius));
            return dist;
        }

        void SetRenderPose(Vector3 pos, Quaternion rot)
        {
            if (rideTransition < 1f)
            {
                rideTransition = Mathf.MoveTowards(rideTransition, 1f, Mathf.Min(Time.deltaTime, 0.05f) / 0.4f);
                float t = Mathf.SmoothStep(0f, 1f, rideTransition);
                Vector3 eye = cam.transform.position;
                pos = Vector3.Lerp(rideFrom.position, pos, t);
                Vector3 offset = pos - eye;
                float len = offset.magnitude;
                if (len > 0.001f)
                {
                    Vector3 dir = offset / len;
                    pos = eye + dir * UnobstructedDistance(eye, dir, len);
                }
                rot = Quaternion.Slerp(rideFrom.rotation, rot, t);
            }
            renderPos = pos;
            renderRot = rot;
            computedFrame = Time.frameCount;
        }

        /// <summary>La torcia non illumina il proprio corpo (SurvivorLight.TorchMask: dalla testa brucerebbe l'arma), quindi dove le lampade
        /// mancano o sono al 40% la schiena del personaggio sarebbe una macchia nera. Una luce debole, che illumina SOLO lo strato del suo
        /// corpo (mai il mondo), lo segue due metri dietro e un metro sopra la testa; senza ombre. Dove le lampade gia' illuminano il giocatore
        /// (Stealth.PlayerLum, la stessa misura del furtivo, che non conta la sua torcia) si attenua fino a spegnersi, cosi' il corpo non
        /// brilla piu' dell'ambiente; passando dalla luce al buio cambia piano piano, come la misura.</summary>
        void UpdateFill(Vector3 eye)
        {
            bool on = fillScale > 0f && showBody && avatar != null && avatar.Body != null;
            if (!on) { if (fill != null) fill.enabled = false; return; }
            if (fill == null)
            {
                var go = new GameObject("RiempimentoTerzaPersona");
                go.transform.SetParent(transform, false);
                fill = go.AddComponent<Light>();
                fill.type = LightType.Point;
                fill.range = FillRange;
                fill.color = new Color(1f, 0.94f, 0.84f);
                fill.shadows = LightShadows.None;
                fill.bounceIntensity = 0f;
            }
            uint mask = SurvivorLight.FillMask(avatar.Body.Slot);
            if (mask != fillMask) { fillMask = mask; Stealth.SetLightLayers(fill, mask); }
            float dark = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FillLitFrom, FillLitTo, Stealth.PlayerLum));
            fill.intensity = FillIntensity * fillScale * dark * Mathf.InverseLerp(0.3f, 1f, blend);
            fill.transform.position = eye + Quaternion.Euler(0f, player.yaw * Mathf.Rad2Deg, 0f) * FillOffset;
            fill.enabled = true;
        }

        void BeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            // The controller can pause while the same camera keeps rendering behind a menu.
            if (c == null || c != cam || swapped || computedFrame < 0) return;
            var t = c.transform;
            savedPos = t.position; savedRot = t.rotation; savedMask = c.cullingMask;
            swapped = true;
            t.SetPositionAndRotation(renderPos, renderRot);
            int mask = savedMask;
            if (showBody && avatar != null)
            {
                mask |= 1 << avatar.Layer;
                mask &= ~(1 << ViewLayer());
            }
            else if (avatar != null) mask &= ~(1 << avatar.Layer);
            c.cullingMask = mask;
        }

        void EndCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (swapped && c == cam) Restore(c);
        }

        void Restore(Camera c)
        {
            c.transform.SetPositionAndRotation(savedPos, savedRot);
            c.cullingMask = savedMask;
            swapped = false;
        }
    }
}

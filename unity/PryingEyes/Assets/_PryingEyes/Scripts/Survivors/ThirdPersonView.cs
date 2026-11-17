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

        // posa della camera: spalla destra, un po' in alto, dietro (in mira piu' vicina e piu' sulla spalla)
        const float Side = 0.55f, SideAds = 0.32f, Up = 0.14f, Back = 2.1f, BackAds = 1.15f, Radius = 0.2f, MaxTurn = 25f;

        PlayerController player;
        Camera cam;
        SeatAvatar avatar;
        bool ownAvatar;
        float blend;                          // 0 prima persona .. 1 terza persona
        int computedFrame = -10;
        Vector3 renderPos;
        Quaternion renderRot;
        // la camera mentre si disegna
        bool swapped;
        Vector3 savedPos; Quaternion savedRot; int savedMask;
        // la luce di riempimento del corpo (vedi UpdateFill)
        const float FillIntensity = 1f, FillRange = 7f;
        static readonly Vector3 FillOffset = new Vector3(0.5f, 0.9f, -2.1f);
        Light fill;
        uint fillMask;

        /// <summary>Solo per le prove: spegne la luce di riempimento (le foto al buio con e senza).</summary>
        public static bool fillOff;

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
            if (v == null || v.blend < 0.5f || v.avatar == null || v.avatar.Body == null || !v.avatar.Body.HasWeapon) return false;
            pos = v.avatar.Body.Muzzle.position;
            return true;
        }

        /// <summary>Chiamata da PlayerController appena ha messo in posa l'occhio, prima che l'arma spari o un prompt guardi.</summary>
        public static void AfterEyePose(PlayerController p)
        {
            if (p == null || p.cam == null) return;
            if (!p.TryGetComponent(out ThirdPersonView v))
            {
                if (!GameSettings.ThirdPerson) return;
                v = p.gameObject.AddComponent<ThirdPersonView>();
            }
            v.Compute(p);
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

        /// <summary>Posa della camera dietro la spalla e orientamento dell'occhio verso il punto inquadrato.</summary>
        void Compute(PlayerController p)
        {
            player = p;
            cam = p.cam != null ? p.cam.GetComponent<Camera>() : null;
            if (cam == null) return;
            float dt = Time.deltaTime;
            bool want = GameSettings.ThirdPerson && p.alive && !ScopedAim();
            blend = Mathf.MoveTowards(blend, want ? 1f : 0f, dt * 5f);
            if (blend <= 0.001f)
            {
                if (fill != null) fill.enabled = false;
                if (!GameSettings.ThirdPerson || !p.alive) { DropAvatar(); if (!GameSettings.ThirdPerson) { Destroy(this); } }
                else if (ownAvatar && avatar != null) avatar.gameObject.SetActive(false);
                return;
            }
            EnsureAvatar();
            if (avatar != null && !avatar.gameObject.activeSelf) avatar.gameObject.SetActive(true);

            Transform eyeT = p.cam;
            Vector3 eye = eyeT.position;
            Quaternion look = eyeT.rotation;
            float ads = p.weapon != null ? p.weapon.ads : 0f;
            Vector3 offset = new Vector3(Mathf.Lerp(Side, SideAds, ads), Up, -Mathf.Lerp(Back, BackAds, ads));
            if (p.IsCrouching) offset.z *= 0.9f;
            Vector3 desired = eye + look * offset;
            Vector3 dir = desired - eye;
            float len = dir.magnitude;
            dir = len > 1e-4f ? dir / len : -(look * Vector3.forward);
            float dist = len;
            if (Physics.SphereCast(eye, Radius, dir, out var hit, len, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0.12f, hit.distance - 0.05f);
            Vector3 pos = eye + dir * dist;

            // il punto che il mirino inquadra: da li' in poi l'occhio guarda li' (i colpi partono dall'occhio)
            Vector3 fwd = look * Vector3.forward;
            int mask = HotelNav.EnvMask | (1 << HotelNav.EnemyLayer);
            Vector3 target = Physics.Raycast(pos, fwd, out var th, 150f, mask, QueryTriggerInteraction.Ignore) ? th.point : pos + fwd * 150f;
            Vector3 toTarget = target - eye;
            if (toTarget.sqrMagnitude > 0.04f)
            {
                Quaternion conv = Quaternion.FromToRotation(fwd, toTarget.normalized);
                conv.ToAngleAxis(out float ang, out Vector3 ax);
                if (ang > MaxTurn) conv = Quaternion.AngleAxis(MaxTurn, ax);
                eyeT.rotation = Quaternion.Slerp(look, conv * look, blend);
            }
            renderPos = Vector3.Lerp(eye, pos, blend);
            renderRot = look;
            computedFrame = Time.frameCount;
            UpdateFill(eye);
        }

        /// <summary>La torcia non illumina il proprio corpo (SurvivorLight.TorchMask: dalla testa brucerebbe l'arma), quindi dove le lampade
        /// mancano o sono al 40% la schiena del personaggio sarebbe una macchia nera. Una luce debole, che illumina SOLO lo strato del suo
        /// corpo (mai il mondo), lo segue due metri dietro e un metro sopra la testa; senza ombre.</summary>
        void UpdateFill(Vector3 eye)
        {
            bool on = !fillOff && blend > 0.3f && avatar != null && avatar.Body != null;
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
            fill.intensity = FillIntensity * Mathf.InverseLerp(0.3f, 1f, blend);
            fill.transform.position = eye + Quaternion.Euler(0f, player.yaw * Mathf.Rad2Deg, 0f) * FillOffset;
            fill.enabled = true;
        }

        void BeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c == null || c != cam || swapped || Time.frameCount - computedFrame > 1 || blend <= 0.001f) return;
            var t = c.transform;
            savedPos = t.position; savedRot = t.rotation; savedMask = c.cullingMask;
            swapped = true;
            t.SetPositionAndRotation(renderPos, renderRot);
            int mask = savedMask;
            if (avatar != null) mask |= 1 << avatar.Layer;
            if (blend > 0.3f) mask &= ~(1 << ViewLayer());           // arma e mani della prima persona
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

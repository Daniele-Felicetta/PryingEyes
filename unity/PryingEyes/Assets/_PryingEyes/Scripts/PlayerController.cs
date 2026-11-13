using UnityEngine;

namespace PryingEyes
{
    /// <summary>Quake-style FPS controller: WASD, sprint, crouch/slide, jump (coyote+buffer), lean vero (Q/E), mouse look,
    /// rinculo della visuale con ritorno.
    /// Il lean e' un'inclinazione del busto attorno al bacino: la testa sporge di ~40 cm e si inclina di ~20 gradi, la
    /// collisione impedisce di entrare nei muri e i nemici vedono e colpiscono il corpo dove si trova davvero (LeanedPoint).</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Move")]
        // ritmo da COD Zombies: camminata lenta, sprint breve, poca inerzia
        public float walk = 3.9f, sprint = 5.9f, crouchSpeed = 2.2f;
        public float accelGround = 20f, accelAir = 6f, friction = 8.5f;
        public float jumpVel = 4.6f, gravity = 17.5f;
        public float heightStand = 1.75f, heightCrouch = 1.15f, eyeStand = 1.62f, eyeCrouch = 1.02f;
        // radianti per unità di "Mouse X" (= pixel x 0.1 nell'Input Manager): 0.021 = 0.0021 rad/pixel come nel web.
        // Prima era 1.2, cioè circa 57 volte più sensibile del web.
        public float sens = 0.021f;

        [Header("Lean")]
        public float leanAngle = 26f;          // inclinazione del busto a lean pieno (gradi)
        public float leanSlide = 0.11f;        // spostamento laterale in piu' (m): il corpo scivola oltre l'angolo
        public float leanRoll = 19f;           // inclinazione della visuale (gradi)
        public float leanPivot = 0.88f;        // altezza del bacino (m): il busto ruota attorno a questo punto
        public float leanSpeed = 9f;
        public float leanHeadRadius = 0.17f;   // la testa non entra nei muri

        [Header("Life")]
        public float hp = 100f, shield = 50f;
        public bool alive = true;
        /// <summary>Durante i cambi di piano: nessun danno.</summary>
        public bool invulnerable;
        /// <summary>Solo per il bot di prova: direzione del mondo in cui camminare (zero = fermo).</summary>
        public static Vector3 botDir;
        /// <summary>Solo per il bot rigoroso (-botstrict): se non e' zero il giocatore guarda in quella direzione (come chi mira a cio' che
        /// vuole usare), mentre botDir resta il movimento.</summary>
        public static Vector3 botLook;
        /// <summary>Solo per le prove (-autotest): tiene premuto il tasto per accovacciarsi (il passaggio basso della stanza segreta).</summary>
        public static bool botCrouch;
        /// <summary>Solo per le prove: lean forzato (-1 sinistra, +1 destra); NaN = usa la tastiera.</summary>
        public static float debugLean = float.NaN;
        float lastDamage = -99f, respawnT = 0f;
        /// <summary>Vita massima: 100, 150 con Pelle di pietra (Perks.cs). I secondi che restano di tregua dopo una Rinascita sul posto.</summary>
        public PlayerPerks PerkState { get; } = new PlayerPerks();
        public float MaxHp => PerkState.MaxHp;
        float reviveInv;
        public bool Reviving => reviveInv > 0f;

        [Header("Refs")]
        public Weapon weapon;
        public Transform cam;
        public HUD hud;

        CharacterController cc;
        public float BodyHeight => cc != null ? cc.height : heightStand;
        Vector3 vel;
        Vector3 spawnPos = new Vector3(0, 0.1f, 19f);
        int lastStep;

        // rinculo della visuale (gradi): il bersaglio sale a ogni colpo e rientra; la visuale lo segue
        float recTargetP, recTargetY, recP, recY, recRecover = 8f, recDelay;
        float simYaw, simPitch;

        /// <summary>Superficie sotto i piedi per il suono dei passi: 0 marmo, 1 moquette, 2 legno del soppalco.
        /// Le moquette non hanno collider (sono solo disegnate): si riconoscono dai rettangoli di HotelBuilder.</summary>
        int Surface()
        {
            Vector3 p = transform.position;
            if (GameManager.proc)
            {
                var fd = Proc.FloorDirector.I;
                var room = fd != null && fd.cur != null ? fd.cur.RoomAt(p) : null;
                return room != null ? Proc.FloorBuilder.Surface(room, fd.cur.plan.theme) : 0;
            }
            if (p.y > 3.5f) return 2;
            bool In(float x0, float x1, float z0, float z1) => p.x > x0 && p.x < x1 && p.z > z0 && p.z < z1;
            if (In(-18f, -2f, -11f, 19f) || In(21f, 27f, -14f, 6f)) return 1;
            return 0;
        }

        /// <summary>Sposta il giocatore senza cambiare il punto di rinascita.</summary>
        public void Teleport(Vector3 p)
        {
            if (cc == null) cc = GetComponent<CharacterController>();
            cc.enabled = false;
            transform.position = p;
            cc.enabled = true;
            vel = Vector3.zero;
            Enemy.IgnorePlayerBodies();      // riaccendere il controller cancella le coppie che i volanti ignoravano
        }

        /// <summary>Sposta il punto di (ri)nascita e ci porta subito il giocatore (usato dal bot per il soppalco).</summary>
        public void SetSpawn(Vector3 p)
        {
            spawnPos = p;
            if (cc == null) cc = GetComponent<CharacterController>();
            cc.enabled = false;              // con il controller acceso la posizione impostata a mano non vale
            transform.position = p;
            cc.enabled = true;
            vel = Vector3.zero;
            Enemy.IgnorePlayerBodies();
        }

        /// <summary>Per la componente furtiva: come si muove il giocatore adesso (velocita' orizzontale, corsa, accovacciato).</summary>
        public float SpeedH { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }

        public float yaw = Mathf.PI, pitch, lean, bobPhase;
        float coyote, jumpBuf, slideT, eyeH, fovKick;
        bool crouch, slide;
        Vector3 slideDir;

        // ------------------------------------------------------------------ lean
        Vector3 Right() => new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));

        /// <summary>Spostamento laterale (m, verso destra) a quell'altezza dal suolo, col lean attuale: il bacino sta fermo,
        /// piu' su si sporge di piu'.</summary>
        float LeanLateral(float h)
        {
            float th = lean * leanAngle * Mathf.Deg2Rad;
            float arm = Mathf.Max(0f, h - leanPivot);
            return arm * Mathf.Sin(th) + lean * leanSlide * Mathf.Clamp01(arm / Mathf.Max(0.1f, eyeStand - leanPivot));
        }

        /// <summary>Dove si trova davvero il corpo del giocatore a quell'altezza (m dai piedi): usato dai nemici per vedere e
        /// colpire; con il lean la testa sporge dal riparo, il bacino no.</summary>
        public Vector3 LeanedPoint(float h) => LeanedPointFor(transform.position, yaw, lean, h);

        /// <summary>Lo stesso per un altro giocatore (un compagno in cooperativa: piedi, direzione e lean arrivano dalla rete), con le stesse misure.</summary>
        public Vector3 LeanedPointFor(Vector3 feet, float yawR, float leanV, float h)
        {
            Vector3 p = feet + Vector3.up * h;
            if (Mathf.Abs(leanV) < 0.001f) return p;
            float th = leanV * leanAngle * Mathf.Deg2Rad;
            float arm = Mathf.Max(0f, h - leanPivot);
            float lateral = arm * Mathf.Sin(th) + leanV * leanSlide * Mathf.Clamp01(arm / Mathf.Max(0.1f, eyeStand - leanPivot));
            Vector3 right = new Vector3(Mathf.Cos(yawR), 0f, -Mathf.Sin(yawR));
            return p + right * lateral - Vector3.up * (arm * (1f - Mathf.Cos(th)));
        }

        /// <summary>Quanto lean (0..1) si puo' fare dal lato 'sign' (+1 destra) senza che la testa entri in un muro.</summary>
        float LeanRoom(float sign)
        {
            float reach = (eyeStand - leanPivot) * Mathf.Sin(leanAngle * Mathf.Deg2Rad) + leanSlide;
            Vector3 start = transform.position + Vector3.up * Mathf.Max(eyeH, 1.0f);
            Vector3 dir = Right() * sign;
            if (Physics.SphereCast(start, leanHeadRadius, dir, out RaycastHit hit, reach + 0.04f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                return Mathf.Clamp01((hit.distance - 0.01f) / reach);
            return 1f;
        }

        // ------------------------------------------------------------------ rinculo
        /// <summary>Scatto della visuale a ogni colpo (gradi: v verso l'alto, h verso destra) e velocita' con cui rientra.</summary>
        public void Kick(float v, float h, float recover)
        {
            recTargetP += v;
            recTargetY += h;
            recRecover = recover;
            recDelay = 0.05f;
            // tetto della salita senza compensare: oltre ~20 gradi si guarda il soffitto e il rinculo diventa solo fastidio
            recTargetP = Mathf.Min(recTargetP, 20f);
            recTargetY = Mathf.Clamp(recTargetY, -14f, 14f);
        }

        public float ViewKickPitch => recP;
        /// <summary>Solo per le prove: ferma la rotazione automatica dell'autotest (le foto devono guardare dove le voglio io).</summary>
        public static bool holdLook;
        public float LeanRoomPublic(float sign) => LeanRoom(sign);

        void Awake()
        {
            // riallinea i valori serializzati nella scena al ritmo lento da orda
            walk = 3.9f; sprint = 5.9f; crouchSpeed = 2.2f;
            accelGround = 20f; accelAir = 6f; friction = 8.5f;
            jumpVel = 4.6f; gravity = 17.5f;
            PerkState.Changed += OnPerksChanged;
            cc = GetComponent<CharacterController>();
            cc.minMoveDistance = 0f;       // con 10.000 fps (batchmode) 4 m/s sono 0,4 mm a fotogramma: sotto il minimo di default (1 mm) non ci si muoveva
            PlayerAvoidance.Bind(gameObject, Targets.Local);
            eyeH = eyeStand;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (GameManager.autoTest) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        void Start()
        {
            Debug.Log("[PryingEyes] player boot: cam=" + (cam != null) + " weapon=" + (weapon != null)
                + " hud=" + (hud != null) + " gunBound=" + (weapon != null && weapon.IsBound));
        }

        void Update()
        {
            if (cam == null || weapon == null || (GameMenu.Blocked && !Coop.Active)) return;
            float dt = Time.deltaTime;
            TickSurvival(dt);
            if (!alive)
            {
                respawnT -= dt;
                if (respawnT <= 0f) Respawn();
                return;
            }
            bool inputEnabled = !GameMenu.Blocked;

            // --- mouse look (camera faces +z at yaw 0; movement basis must match) ---
            float ms = sens * GameSettings.Sensitivity * weapon.SensMult;
            float touchSens = 4f * GameSettings.Sensitivity * weapon.SensMult;
            float dYaw = inputEnabled ? Input.GetAxis("Mouse X") * ms + TouchControls.Look.x * touchSens : 0f;
            float dPitch = inputEnabled ? -Input.GetAxis("Mouse Y") * ms - TouchControls.Look.y * touchSens : 0f;
            yaw += dYaw;
            pitch += dPitch;
            pitch = Mathf.Clamp(pitch, -1.5f, 1.5f);
            Vector2 lookDelta = new Vector2(dYaw * Mathf.Rad2Deg, -dPitch * Mathf.Rad2Deg);

            // --- input ---
            float f = inputEnabled ? (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0) + TouchControls.Move.y : 0f;
            float s = inputEnabled ? (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0) + TouchControls.Move.x : 0f;
            if (inputEnabled && GameManager.autoTest && !holdLook)
            {
                var e = Enemy.Nearest(transform.position);
                if (e != null)
                {
                    Vector3 aim = (e.AimPoint - cam.position).normalized;
                    yaw = Mathf.Atan2(aim.x, aim.z);
                    pitch = -Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f));
                }
                else if (botLook.sqrMagnitude > 0.01f)
                {
                    yaw = Mathf.MoveTowardsAngle(yaw * Mathf.Rad2Deg, Mathf.Atan2(botLook.x, botLook.z) * Mathf.Rad2Deg, 220f * dt) * Mathf.Deg2Rad;
                    pitch = Mathf.MoveTowards(pitch, -Mathf.Asin(Mathf.Clamp(botLook.normalized.y, -1f, 1f)), 3f * dt);
                }
                else if (botDir.sqrMagnitude > 0.01f)
                    yaw = Mathf.MoveTowardsAngle(yaw * Mathf.Rad2Deg, Mathf.Atan2(botDir.x, botDir.z) * Mathf.Rad2Deg, 220f * dt) * Mathf.Deg2Rad;
                else { yaw += 0.8f * dt; }
                if (botDir.sqrMagnitude > 0.01f)
                {
                    f = Vector3.Dot(botDir, new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)));
                    s = Vector3.Dot(botDir, new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw)));
                }
            }
            bool wantCrouch = inputEnabled ? Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl) || TouchControls.Held(TouchControls.Action.Crouch) || (GameManager.autoTest && botCrouch) : crouch;
            bool sprintInput = inputEnabled && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || TouchControls.Held(TouchControls.Action.Sprint)) && f > 0;
            bool wantSprint = sprintInput && !wantCrouch
                              && !(weapon.Def.zoom >= 3.9f && weapon.ads > 0.5f);   // con l'ottica forte in mira Shift tiene il fiato
            if (inputEnabled && (Input.GetKeyDown(KeyCode.Space) || TouchControls.Down(TouchControls.Action.Jump))) jumpBuf = 0.15f; else jumpBuf -= dt;

            float hSpeed = new Vector2(vel.x, vel.z).magnitude;
            if (!slide && !crouch && sprintInput && wantCrouch && hSpeed > 4.4f && cc.isGrounded)
            {
                slide = true; slideT = 0.85f; crouch = true;
                slideDir = new Vector3(vel.x, 0, vel.z).normalized;
                fovKick = 6f;
            }
            if (slide)
            {
                slideT -= dt;
                if (!cc.isGrounded || slideT <= 0f || hSpeed < 2.5f || jumpBuf > 0f)
                { slide = false; crouch = wantCrouch; }
            }
            else crouch = wantCrouch;
            // Non espandere la capsula se il soffitto impedisce di rialzarsi.
            if (!crouch && !slide && cc.height < heightStand - 0.01f)
            {
                float radius = Mathf.Max(0.01f, cc.radius - cc.skinWidth);
                Vector3 bottom = transform.position + Vector3.up * (cc.height - radius);
                Vector3 top = transform.position + Vector3.up * (heightStand - radius);
                if (Physics.CheckCapsule(bottom, top, radius, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) crouch = true;
            }

            var wd = weapon.Def;
            float maxSpeed = (crouch && !slide) ? crouchSpeed : (wantSprint && !slide ? sprint : walk);
            maxSpeed *= wd.moveMult;
            maxSpeed *= Mathf.Lerp(1f, wd.adsMoveMult, weapon.ads);
            if (weapon.reloading && wd.style != ReloadStyle.Tube) maxSpeed *= 0.92f;
            if (sedT > 0f) maxSpeed *= 0.55f;       // sedativo dell'infermiere

            // wish dir (yaw plane): forward = (sin yaw, 0, cos yaw) to match camera
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
            Vector3 wish = new Vector3(sin * f + cos * s, 0, cos * f - sin * s);
            float wishAmount = Mathf.Clamp01(wish.magnitude);
            wish = wish.normalized;
            maxSpeed *= wishAmount; // lo stick regola la velocità, non la lunghezza della base usata dall'accelerazione

            Vector3 hv = new Vector3(vel.x, 0, vel.z);
            if (slide)
            {
                float sp = Mathf.Max(0f, hv.magnitude - hv.magnitude * 1.6f * dt * 0.4f);
                Vector3 dir = hv.magnitude > 0.01f ? hv.normalized : slideDir;
                dir = (dir + wish * (0.12f * dt * 10f)).normalized;
                float keep = Mathf.Max(sp, 3.2f * (slideT / 0.85f));
                vel.x = dir.x * keep; vel.z = dir.z * keep;
            }
            else if (cc.isGrounded)
            {
                float speed = hv.magnitude;
                if (wish.sqrMagnitude < 0.01f)
                {
                    float ns = Mathf.Max(0f, speed - speed * friction * dt);
                    if (speed > 0.01f) { vel.x *= ns / speed; vel.z *= ns / speed; }
                }
                else
                {
                    float cur = vel.x * wish.x + vel.z * wish.z;
                    float add = maxSpeed - cur;
                    if (add > 0f)
                    {
                        float acc = Mathf.Min(accelGround * dt * maxSpeed, add);
                        vel.x += wish.x * acc; vel.z += wish.z * acc;
                    }
                    else if (add < 0f)
                    {
                        float brake = Mathf.Min(-add, accelGround * dt * maxSpeed);
                        vel.x -= wish.x * brake; vel.z -= wish.z * brake;
                    }
                    Vector3 lat = new Vector3(-wish.z, 0, wish.x);
                    float latV = vel.x * lat.x + vel.z * lat.z;
                    float k = Mathf.Min(1f, 10f * dt);
                    vel.x -= lat.x * latV * k; vel.z -= lat.z * latV * k;
                }
            }
            else
            {
                float cur = vel.x * wish.x + vel.z * wish.z;
                float add = maxSpeed * 0.9f - cur;
                if (add > 0f && wish.sqrMagnitude > 0.01f)
                {
                    float acc = Mathf.Min(accelAir * dt * maxSpeed, add);
                    vel.x += wish.x * acc; vel.z += wish.z * acc;
                }
                vel.x *= 1f - 0.08f * dt; vel.z *= 1f - 0.08f * dt;
            }

            vel.y -= gravity * dt;
            vel.y = Mathf.Max(vel.y, -22f);
            if (cc.isGrounded) coyote = 0.13f; else coyote -= dt;
            if (inputEnabled && jumpBuf > 0f && (cc.isGrounded || coyote > 0f))
            {
                vel.y = jumpVel * (crouch ? 0.92f : 1f);
                coyote = 0f; jumpBuf = 0f; slide = false;
            }

            cc.height = Mathf.Lerp(cc.height, crouch || slide ? heightCrouch : heightStand, 11f * dt);
            cc.center = new Vector3(0, cc.height / 2f, 0);
            cc.Move(vel * dt);

            // --- lean: Q a sinistra, E a destra; non in corsa ne' in scivolata, non nei muri ---
            float leanWant = 0f;
            if (inputEnabled && !slide && !wantSprint) leanWant = float.IsNaN(debugLean) ? (Input.GetKey(KeyCode.E) || TouchControls.Held(TouchControls.Action.Right) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) || TouchControls.Held(TouchControls.Action.Left) ? 1f : 0f) : debugLean;
            float room = leanWant != 0f ? LeanRoom(leanWant) : 1f;
            float leanTarget = leanWant * room;
            // se la testa e' gia' contro un muro rientra piu' in fretta
            float ls = Mathf.Abs(leanTarget) < Mathf.Abs(lean) ? leanSpeed * 1.4f : leanSpeed;
            lean += (leanTarget - lean) * Mathf.Min(1f, ls * dt);
            if (Mathf.Abs(lean) < 0.0005f) lean = 0f;

            // --- rinculo della visuale ---
            if (recDelay > 0f) recDelay -= dt;
            else
            {
                float rate = recRecover * (1f + Mathf.Abs(recTargetP) * 0.12f);
                recTargetP = Mathf.MoveTowards(recTargetP, 0f, rate * dt);
                recTargetY = Mathf.MoveTowards(recTargetY, 0f, rate * 0.6f * dt);
            }
            float fol = 1f - Mathf.Exp(-34f * dt);
            recP += (recTargetP - recP) * fol;
            recY += (recTargetY - recY) * fol;

            // --- camera ---
            float hs = new Vector2(vel.x, vel.z).magnitude;
            SpeedH = hs;
            IsSprinting = wantSprint && !slide && hs > 1f;
            IsCrouching = crouch || slide;
            float motion = GameSettings.Motion;
            float bobAmp = (crouch ? 0.45f : 1f) * (1f - weapon.ads * 0.8f) * Mathf.Min(1f, hs / 6f) * motion;
            bobPhase += dt * (4f + hs * 1.35f) * (cc.isGrounded ? 1f : 0.15f);
            // un passo per ogni ciclo del dondolio (bobY = sin(2*fase): un ciclo ogni pi greco di fase)
            int stepIdx = Mathf.FloorToInt(bobPhase / Mathf.PI);
            if (stepIdx != lastStep)
            {
                lastStep = stepIdx;
                if (cc.isGrounded && hs > 1.2f)
                {
                    int surf = Surface();
                    Synth.Step(surf, Mathf.Lerp(0.16f, 0.38f, Mathf.Clamp01(hs / 8f)) * (crouch ? 0.5f : 1f));
                    // i preti sentono i passi: piu' lontano di corsa, quasi niente accovacciati o sulla moquette
                    float surfK = surf == 1 ? 0.6f : surf == 2 ? 1.1f : 1.15f;
                    float loud = crouch || slide ? 0.4f : wantSprint ? 1.6f : 0.95f;
                    Noise.Emit(transform.position, 6.8f * loud * surfK * PerkState.StepNoise, NoiseKind.Step);     // Passi di cenere: -40%
                }
            }
            float bobY = Mathf.Sin(bobPhase * 2f) * 0.032f * bobAmp;
            float bobX = Mathf.Cos(bobPhase) * 0.026f * bobAmp;
            float targetEye = (crouch || slide ? eyeCrouch : eyeStand) + (slide ? -0.12f : 0f);
            eyeH += (targetEye - eyeH) * Mathf.Min(1f, 11f * dt);

            Camera c = cam.GetComponent<Camera>();
            float baseFov = GameSettings.Fov;
            float targetFov = baseFov + ((wantSprint && !slide && hs > 1f ? 7f : 0f) + (slide ? 5f : 0f) + fovKick) * motion
                - weapon.ads * (baseFov - wd.adsFov);
            fovKick *= Mathf.Max(0f, 1f - 6f * dt);
            c.fieldOfView += (targetFov - c.fieldOfView) * Mathf.Min(1f, 10f * dt);

            float strafeLean = Mathf.Clamp(-(vel.x * cos - vel.z * sin) * 0.0035f, -0.035f, 0.035f);
            // lean: la testa si sposta davvero (busto che ruota attorno al bacino) e la visuale si inclina
            Vector3 right = new Vector3(cos, 0f, -sin);
            float th = lean * leanAngle * Mathf.Deg2Rad;
            float arm = Mathf.Max(0.1f, eyeH - leanPivot);
            float latOff = arm * Mathf.Sin(th) + lean * leanSlide;
            float drop = arm * (1f - Mathf.Cos(th));
            cam.position = new Vector3(
                transform.position.x + bobX * cos + right.x * latOff,
                transform.position.y + eyeH + bobY - drop,
                transform.position.z - bobX * sin + right.z * latOff);
            float roll = (strafeLean * Mathf.Rad2Deg + (slide ? 5f : 0f)) * motion - lean * leanRoll;
            cam.rotation = Quaternion.Euler(pitch * Mathf.Rad2Deg - recP, yaw * Mathf.Rad2Deg + recY, roll);

            // perk attivabile: il tempo degli effetti scorre in TickSurvival, anche nel menu cooperativo
            if (alive && !GameMenu.Blocked && PerkState.Has(PerkId.Velo) && (Input.GetKeyDown(KeyCode.H) || TouchControls.Down(TouchControls.Action.Velo)) && PerkState.TryVelo())
            {
                hud?.Toast("Il Velo ti avvolge: per cinque secondi ti vedono solo da vicino.");
                Sfx.UI(SoundBank.PerkVelo, 0.65f);
            }

            weapon.Tick(dt, f > 0 && wantSprint, hs, cc.isGrounded, crouch, lookDelta);
            hud?.SetBars(hp, shield);

            // --- armi sui muri: prompt + acquisto con F ---
            if (inputEnabled && !GameManager.autoTest && cam != null)
            {
                bool onWall = WallBuy.Tick(cam);
                bool onThing = !onWall && Proc.Interact.Tick(cam);
                if ((Input.GetKeyDown(KeyCode.F) || TouchControls.Down(TouchControls.Action.Use))) { if (onWall) WallBuy.TryBuy(); else if (onThing) Proc.Interact.TryUse(); }
                // dopo gli altri prompt (che cancellano la riga a ogni fotogramma): la presa alle spalle
                if (!onWall && !onThing) Knife.I?.PromptTick();
            }
        }

        /// <summary>Secondi di sedativo rimasti: finché dura ti muovi al 55%. Lo dà la puntura dell'infermiere.</summary>
        public float sedT;

        void TickSurvival(float dt)
        {
            PerkState.Tick(dt);
            reviveInv = Mathf.Max(0f, reviveInv - dt);
            sedT = Mathf.Max(0f, sedT - dt);
            if (!alive) return;
            if (Time.time - lastDamage > 6f && shield < 50f) shield = Mathf.Min(50f, shield + 8f * dt);
            if (Time.time - lastDamage > 10f && hp < MaxHp) hp = Mathf.Min(MaxHp, hp + 3f * PerkState.MaxHpMult * dt);
        }

        public void Sedate(float seconds)
        {
            if (!alive || invulnerable) return;
            if (sedT <= 0f) hud?.Toast("Il sedativo ti appesantisce le gambe.");
            sedT = Mathf.Max(sedT, seconds);
        }

        public void Hurt(float dmg, Vector3? origin = null)
        {
            // in cooperativa la pausa non ferma il mondo, quindi non rende nemmeno immuni
            if (!alive || (GameMenu.Blocked && !Coop.Active) || invulnerable || reviveInv > 0f) return;
            lastDamage = Time.time;
            dmg *= 1.5f;
            float absorbed = Mathf.Min(shield, dmg * 0.6f);
            shield -= absorbed;
            hp -= dmg - absorbed;
            hud?.DamageFlash(origin);
            hud?.SetBars(hp, shield);
            Synth.Hurt();
            // il colpo scuote la visuale
            Kick(1.2f, Random.Range(-1f, 1f) * 1.2f, 10f);
            if (hp <= 0f && PerkState.TakeRevive())
            {
                // Rinascita: ti rialzi sul posto con meta' vita e tre secondi di tregua; il libro si riaprira' al piano dopo
                hp = MaxHp * 0.5f; shield = 50f; reviveInv = 3f;
                hud?.SetBars(hp, shield);
                hud?.Banner("Rinascita", "Il libro ti rialza. Una volta per piano.");
                hud?.Flash(new Color(0.55f, 1f, 0.62f), 0.9f, 0.65f);
                Sfx.UI(SoundBank.PerkRevive, 0.85f);
                return;
            }
            if (hp <= 0f)
            {
                hp = 0f; alive = false; respawnT = 2.5f;
                // si perde meta' dei punti e l'ultimo perk comprato (FloorDirector.DeathPenalty): la morte pesa, ma non e' la fine
                string pen = GameManager.proc ? Proc.FloorDirector.I?.DeathPenalty() : null;
                hud?.Banner("Sei morto", string.IsNullOrEmpty(pen) ? "L'hotel ti riporta all'ingresso." : pen);
                hud?.SetPrompt("");
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>Un perk e' arrivato o e' andato perso: la vita massima puo' essere cambiata. Pelle di pietra riempie la vita nuova.</summary>
        public void OnPerksChanged(bool healToFull)
        {
            hp = healToFull ? MaxHp : Mathf.Min(hp, MaxHp);
            hud?.SetBars(hp, shield);
        }

        /// <summary>Cura (Sete di sangue): fino alla vita massima.</summary>
        public void Heal(float n)
        {
            if (!alive || n <= 0f) return;
            hp = Mathf.Min(MaxHp, hp + n);
            hud?.SetBars(hp, shield);
        }

        void Respawn()
        {
            alive = true; hp = MaxHp; shield = 50f; reviveInv = sedT = 0f;
            SetSpawn(spawnPos);
            if (GameManager.proc) Proc.FloorDirector.I?.OnRespawn(); else yaw = Mathf.PI;
            pitch = 0f;
            lean = 0f; recP = recY = recTargetP = recTargetY = 0f;
            // mantieni le armi comprate, con caricatore pieno e riserva almeno quella di partenza
            weapon.RespawnAmmo();
            Torch.I?.OnRespawn();
            hud?.SetBars(hp, shield);
            Cursor.lockState = GameMenu.Blocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = GameMenu.Blocked;
            if (GameManager.autoTest) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
    }
}

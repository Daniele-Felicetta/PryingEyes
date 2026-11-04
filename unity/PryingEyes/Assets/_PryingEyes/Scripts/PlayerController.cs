using UnityEngine;

namespace PryingEyes
{
    /// <summary>Quake-style FPS controller: WASD, sprint, crouch/slide, jump (coyote+buffer), Q/E lean, mouse look.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Move")]
        public float walk = 6f, sprint = 9f, crouchSpeed = 3f;
        public float accelGround = 10f, accelAir = 2f, friction = 8f;
        public float jumpVel = 7f, gravity = 22f;
        public float heightStand = 1.8f, heightCrouch = 1.1f, eyeStand = 1.65f, eyeCrouch = 1.0f;
        public float sens = 1.2f;

        [Header("Life")]
        public float hp = 100f, shield = 50f;
        public bool alive = true;
        float lastDamage = -99f, respawnT = 0f;

        [Header("Refs")]
        public Weapon weapon;
        public Transform cam;
        public HUD hud;

        CharacterController cc;
        Vector3 vel;
        public float yaw = Mathf.PI, pitch, lean, bobPhase;
        float coyote, jumpBuf, slideT, eyeH, fovKick;
        bool crouch, slide;
        Vector3 slideDir;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            eyeH = eyeStand;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Start()
        {
            Debug.Log("[PryingEyes] player boot: cam=" + (cam != null) + " weapon=" + (weapon != null)
                + " hud=" + (hud != null) + " gunBound=" + (weapon != null && weapon.IsBound));
        }

        void Update()
        {
            if (cam == null || weapon == null) return;
            float dt = Time.deltaTime;
            if (!alive)
            {
                respawnT -= dt;
                if (respawnT <= 0f) Respawn();
                return;
            }

            // --- mouse look (camera faces +z at yaw 0; movement basis must match) ---
            yaw += Input.GetAxis("Mouse X") * sens;
            pitch -= Input.GetAxis("Mouse Y") * sens;
            pitch = Mathf.Clamp(pitch, -1.5f, 1.5f);

            // --- input ---
            float f = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            float s = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            bool wantCrouch = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl);
            bool wantSprint = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && f > 0 && !wantCrouch;
            if (Input.GetKeyDown(KeyCode.Space)) jumpBuf = 0.15f; else jumpBuf -= dt;

            float hSpeed = new Vector2(vel.x, vel.z).magnitude;
            if (!slide && wantSprint && wantCrouch && hSpeed > 6f && cc.isGrounded)
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

            float maxSpeed = (crouch && !slide) ? crouchSpeed : (wantSprint && !slide ? sprint : walk);
            if (weapon != null && weapon.ads > 0.5f) maxSpeed *= 0.55f;

            // wish dir (yaw plane): forward = (sin yaw, 0, cos yaw) to match camera
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
            Vector3 wish = new Vector3(sin * f + cos * s, 0, cos * f - sin * s);
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            Vector3 hv = new Vector3(vel.x, 0, vel.z);
            if (slide)
            {
                float sp = Mathf.Max(0f, hv.magnitude - hv.magnitude * 1.6f * dt * 0.4f);
                Vector3 dir = hv.magnitude > 0.01f ? hv.normalized : slideDir;
                dir = (dir + wish * (0.12f * dt * 10f)).normalized;
                float keep = Mathf.Max(sp, 4.5f * (slideT / 0.85f));
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
            if (jumpBuf > 0f && (cc.isGrounded || coyote > 0f))
            {
                vel.y = jumpVel * (crouch ? 0.92f : 1f);
                coyote = 0f; jumpBuf = 0f; slide = false;
            }

            cc.height = Mathf.Lerp(cc.height, crouch || slide ? heightCrouch : heightStand, 11f * dt);
            cc.center = new Vector3(0, cc.height / 2f, 0);
            cc.Move(vel * dt);

            // --- lean ---
            float leanT = ((Input.GetKey(KeyCode.Q) ? 1 : 0) - (Input.GetKey(KeyCode.E) ? 1 : 0)) * 0.16f;
            lean += (leanT - lean) * Mathf.Min(1f, 12f * dt);

            // --- camera ---
            float hs = new Vector2(vel.x, vel.z).magnitude;
            float bobAmp = (crouch ? 0.45f : 1f) * (weapon != null ? 1f - weapon.ads * 0.8f : 1f) * Mathf.Min(1f, hs / 6f);
            bobPhase += dt * (4f + hs * 1.35f) * (cc.isGrounded ? 1f : 0.15f);
            float bobY = Mathf.Sin(bobPhase * 2f) * 0.032f * bobAmp;
            float bobX = Mathf.Cos(bobPhase) * 0.026f * bobAmp;
            float targetEye = (crouch || slide ? eyeCrouch : eyeStand) + (slide ? -0.12f : 0f);
            eyeH += (targetEye - eyeH) * Mathf.Min(1f, 11f * dt);

            Camera c = cam.GetComponent<Camera>();
            float targetFov = 75f + (wantSprint && !slide && hs > 1f ? 7f : 0f) + (slide ? 5f : 0f) + fovKick
                - (weapon != null ? weapon.ads * 23f : 0f);
            fovKick *= Mathf.Max(0f, 1f - 6f * dt);
            c.fieldOfView += (targetFov - c.fieldOfView) * Mathf.Min(1f, 10f * dt);

            float strafeLean = Mathf.Clamp(-(vel.x * cos - vel.z * sin) * 0.0035f, -0.035f, 0.035f);
            cam.position = new Vector3(
                transform.position.x + bobX * cos + lean * cos,
                transform.position.y + eyeH + bobY,
                transform.position.z - bobX * sin + lean * -sin);
            cam.rotation = Quaternion.Euler(pitch * Mathf.Rad2Deg - (weapon != null ? weapon.recoil * 20f : 0f),
                yaw * Mathf.Rad2Deg, (strafeLean + lean * 0.9f + (slide ? 0.09f : 0f)) * Mathf.Rad2Deg);

            // --- regen ---
            if (Time.time - lastDamage > 4f && shield < 50f) shield = Mathf.Min(50f, shield + 14f * dt);
            if (Time.time - lastDamage > 7f && hp < 100f) hp = Mathf.Min(100f, hp + 6f * dt);

            weapon?.Tick(dt, f > 0 && wantSprint);
            hud?.SetBars(hp, shield);
        }

        public void Hurt(float dmg)
        {
            if (!alive) return;
            lastDamage = Time.time;
            float absorbed = Mathf.Min(shield, dmg * 0.6f);
            shield -= absorbed;
            hp -= dmg - absorbed;
            hud?.DamageFlash();
            hud?.SetBars(hp, shield);
            if (hp <= 0f)
            {
                hp = 0f; alive = false; respawnT = 2.5f;
                hud?.Banner("SEI MORTO", "Respawn in corso...");
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void Respawn()
        {
            alive = true; hp = 100f; shield = 50f;
            transform.position = new Vector3(0, 0.1f, 19f);
            vel = Vector3.zero; yaw = Mathf.PI; pitch = 0f;
            weapon.mag = weapon.magSize; weapon.reserve = 120; weapon.reloading = false;
            hud?.SetBars(hp, shield);
            hud?.Toast("Respawnato");
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}

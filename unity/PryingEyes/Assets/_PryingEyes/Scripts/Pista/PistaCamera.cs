using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.Pista
{
    public enum CamMode { Chase = 0, Far = 1, Cockpit = 2, Orbit = 3 }

    /// <summary>The camera of the race: it replaces the camera of the person on foot (that one is switched off while the mode lasts). Three views for the rider (behind and above, far behind, and inside the
    /// helmet) and a slow orbit for the end of the race. Behind the bike the camera lags the turn of the bike a little (that lag is what makes a corner feel like a corner), leans with the bike by a fifth, and
    /// opens its field of view with the speed; in the helmet it follows the leaning bike but keeps the horizon more level than the bike does, as a rider's head does. The view never goes under the ground.</summary>
    public sealed class PistaCamera
    {
        public Camera cam;
        CamMode viewMode = CamMode.Chase;
        bool viewBack;
        float yaw, fov = 62f, shake, orbit, introT = -1f, roll;
        Vector3 smoothPos;
        bool init;
        PistaFx fx;
        Camera original;
        bool originalEnabled;
        AudioListener originalListener;
        bool listenerEnabled;
        Vector3 introFrom;
        float introLen = 3.2f;

        /// <summary>The view: another one is a cut (the picture jumps), so the effects of speed start again from nothing instead of smearing the one frame across it.</summary>
        public CamMode mode
        {
            get { return viewMode; }
            set { if (viewMode == value) return; viewMode = value; if (fx != null) fx.Reset(); }
        }

        /// <summary>Looking back (held): turning the view round is a cut as well.</summary>
        public bool lookBack
        {
            get { return viewBack; }
            set { if (viewBack == value) return; viewBack = value; if (fx != null) fx.Reset(); }
        }

        public static PistaCamera Create()
        {
            var pc = new PistaCamera();
            var main = Camera.main;
            var go = new GameObject("Camera pista");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.06f; cam.farClipPlane = 6000f; cam.fieldOfView = 62f;
            if (main != null)
            {
                cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; cam.allowHDR = main.allowHDR; cam.allowMSAA = main.allowMSAA;
                pc.original = main; pc.originalEnabled = main.enabled;
                pc.originalListener = main.GetComponent<AudioListener>();
                if (pc.originalListener != null) { pc.listenerEnabled = pc.originalListener.enabled; pc.originalListener.enabled = false; }
                main.enabled = false;
                main.tag = "Untagged";
            }
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            go.AddComponent<AudioListener>();
            pc.cam = cam;
            return pc;
        }

        /// <summary>Give the camera of the person on foot back (the race is over, or the menu is coming).</summary>
        public void Destroy()
        {
            if (original != null) { original.enabled = originalEnabled; original.tag = "MainCamera"; }
            if (originalListener != null) originalListener.enabled = listenerEnabled;
            if (fx != null) { fx.Destroy(); fx = null; }
            if (cam != null) Object.Destroy(cam.gameObject);
            cam = null;
        }

        public void Cycle()
        {
            mode = mode == CamMode.Chase ? CamMode.Far : mode == CamMode.Far ? CamMode.Cockpit : CamMode.Chase;
        }

        public void Shake(float amount) { shake = Mathf.Max(shake, amount); }

        /// <summary>The effects of speed (motion blur, a fringe of colour) for this frame, from the speed in m/s of the bike the camera follows; the volume is made the first time.</summary>
        public void TickFx(float dt, float speed)
        {
            if (fx == null) fx = PistaFx.Create();
            fx.Update(dt, speed);
        }

        public PistaFx Fx => fx;

        /// <summary>The bike was put somewhere else (back on the track after a fall): the camera does not swing round to follow, it is there at once.</summary>
        public void Snap() { init = false; if (fx != null) fx.Reset(); }

        /// <summary>A short flight from the front of the grid to the place behind the bike: the first seconds of a race.</summary>
        public void StartIntro(Vector3 from, float seconds = 3.2f) { introFrom = from; introT = 0f; introLen = Mathf.Max(0.5f, seconds); }

        public bool InIntro => introT >= 0f && introT < introLen;

        /// <summary>Place the camera for this frame. 'view' is the state of the bike being followed (already interpolated), 'rig' its pose in the scene.</summary>
        public void Update(float dt, in BikeView v, PistaBikeRig rig, PistaWorld world)
        {
            if (cam == null) return;
            Vector3 bike = new Vector3(v.x, v.y, v.z);
            float speed = Mathf.Abs(v.speed);
            // the yaw the camera has: it trails the bike's heading
            if (!init) { yaw = v.heading; smoothPos = bike; init = true; }
            float k = Mathf.Lerp(5.5f, 9.0f, Mathf.Clamp01(speed / 60f));
            if (mode == CamMode.Orbit) { orbit += 28f * Mathf.Deg2Rad * dt; yaw = v.heading + 2.4f + orbit; }
            else yaw += Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, v.heading * Mathf.Rad2Deg) * Mathf.Deg2Rad * (1f - Mathf.Exp(-k * dt));
            float useYaw = lookBack && mode != CamMode.Cockpit ? yaw + Mathf.PI : yaw;
            Vector3 fwd = new Vector3(Mathf.Sin(useYaw), 0f, Mathf.Cos(useYaw));
            float tFov = 62f + Mathf.Clamp(speed, 0f, 90f) * 0.17f;
            Vector3 pos; Quaternion rot;

            if (mode == CamMode.Cockpit && rig != null)
            {
                // a seat over the tank, behind the bars, looking a little down: the front of the tank, the bars and the gloves fill the bottom of the picture (the rider's own head is hidden)
                float hang = Mathf.Clamp(v.hang, -1f, 1f);
                pos = rig.body.TransformPoint(new Vector3(hang * 0.07f, 1.19f - v.tuck * 0.035f, 0.05f));
                float leanDeg = v.lean * Mathf.Rad2Deg;
                rot = rig.body.rotation * Quaternion.Euler(9f - v.tuck * 3f, v.lean * 16f, leanDeg * 0.55f);
                if (lookBack) rot = rot * Quaternion.Euler(0f, 180f, 0f);
                tFov = 78f + Mathf.Clamp(speed, 0f, 90f) * 0.12f;
                smoothPos = pos;
            }
            else
            {
                float dist, height, look;
                if (mode == CamMode.Far) { dist = 7.4f + speed * 0.020f; height = 2.7f; look = 8f; }
                else if (mode == CamMode.Orbit) { dist = 6.5f; height = 1.7f; look = 0f; }
                else { dist = 3.9f + speed * 0.014f; height = 1.48f + v.tuck * -0.06f; look = 6f; }
                // lift a little over a wheelie
                height += Mathf.Max(0f, v.pitch) * 0.8f;
                pos = bike - fwd * dist + Vector3.up * height;
                float ground = world != null ? world.GroundY(pos.x, pos.z) : v.y;
                pos.y = Mathf.Max(pos.y, ground + 0.45f);
                Vector3 aim = bike + Vector3.up * (1.05f - v.tuck * 0.05f) + (mode == CamMode.Orbit ? Vector3.zero : fwd * look);
                if (mode != CamMode.Orbit) aim += new Vector3(fwd.z, 0f, -fwd.x) * (v.lean * 0.9f);     // look a little into the corner
                if (lookBack) aim = bike + Vector3.up * 1.0f - fwd * look;
                rot = Quaternion.LookRotation((aim - pos).normalized, Vector3.up);
                rot = rot * Quaternion.Euler(0f, 0f, Mathf.Clamp(-v.lean * Mathf.Rad2Deg * 0.22f, -14f, 14f) * (mode == CamMode.Orbit ? 0f : 1f));
            }

            // the opening flight
            if (introT >= 0f && introT < introLen)
            {
                introT += dt;
                float e = Mathf.Clamp01(introT / introLen);
                e = e * e * (3f - 2f * e);
                Vector3 look = bike + Vector3.up * 1.0f;
                Vector3 pFrom = introFrom;
                Quaternion rFrom = Quaternion.LookRotation((look - pFrom).normalized, Vector3.up);
                pos = Vector3.Lerp(pFrom, pos, e);
                rot = Quaternion.Slerp(rFrom, rot, e);
            }

            // the shake: rough ground and speed are a tremor; a crash is a blow that dies away
            shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);
            float tremor = shake + (v.surface == SurfaceKind.Kerb ? 0.012f : v.surface == SurfaceKind.Gravel || v.surface == SurfaceKind.Grass ? 0.02f : 0f) * Mathf.Clamp01(speed / 25f)
                + 0.0016f * Mathf.Clamp01((speed - 40f) / 50f);
            if (tremor > 0f) pos += new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * (tremor * GameSettings.Motion);

            TickFx(dt, speed);
            fov = Mathf.Lerp(fov, tFov, 1f - Mathf.Exp(-3f * dt));
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(pos, rot);
        }
    }
}

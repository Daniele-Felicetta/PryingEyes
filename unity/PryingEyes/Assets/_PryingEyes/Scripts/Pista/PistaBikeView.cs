using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>One bike in the scene with its rider, its engine note and its smoke and sparks. A bike simulated on this machine is drawn between the last two steps of the physics (so a monitor at any
    /// refresh rate sees it move smoothly); a friend's bike is drawn easing toward the last pose that arrived, carried forward at the speed it had. The local rider has the tire and wind sounds too; the
    /// others only the engine (a few sources are enough to hear a pack go by).</summary>
    public sealed class PistaBikeView : MonoBehaviour
    {
        [System.NonSerialized] public PistaBikeRig rig;
        [System.NonSerialized] public Racer racer;
        public bool local;
        [System.NonSerialized] public BikeView prev, cur, shown, target;
        public bool hasState;
        [System.NonSerialized] public Livery livery;

        AudioSource engine, squeal, rough, wind;
        AudioLowPassFilter engineFilter;
        float sparkT, smokeT, dustT, packetAge, flashT;
        bool braking;
        public float Distance;          // from the camera, for the level of detail of the effects

        public static PistaBikeView Create(Transform parent, Racer r, bool local, int style, Livery liv)
        {
            var go = new GameObject("Moto " + r.name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PistaBikeView>();
            v.racer = r; v.local = local; v.livery = liv;
            v.rig = PistaBikeMesh.Build(go.transform, style, liv);
            v.engine = v.Voice(SoundBank.SportEngine, local ? 5f : 7f, local ? 120f : 170f, local ? 0.55f : 1f, 0f);
            v.engineFilter = go.AddComponent<AudioLowPassFilter>();
            v.engineFilter.cutoffFrequency = 4000f;
            if (local)
            {
                v.squeal = v.Voice(SoundBank.TyreSqueal, 3f, 60f, 0.4f, 0f);
                v.rough = v.Voice(SoundBank.KerbRough, 3f, 60f, 0.4f, 0f);
                v.wind = v.Voice(SoundBank.WindRush, 3f, 60f, 0f, 0f);
            }
            return v;
        }

        AudioSource Voice(AudioClip clip, float minD, float maxD, float spatial, float vol)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.playOnAwake = false; s.volume = vol; s.spatialBlend = spatial; s.minDistance = minD; s.maxDistance = maxD;
            s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = local ? 0f : 0.5f;
            if (clip != null) { s.time = Random.value * clip.length * 0.9f; s.Play(); }
            return s;
        }

        /// <summary>The newest state of the physics: it becomes the end of the interval being drawn.</summary>
        public void Push(BikeView v)
        {
            if (!hasState) { prev = cur = shown = v; hasState = true; return; }
            prev = cur; cur = v;
        }

        /// <summary>A pose that arrived from the network.</summary>
        public void Receive(BikeView v)
        {
            target = v; packetAge = 0f;
            if (!hasState) { prev = cur = shown = v; hasState = true; }
        }

        public void Render(float alpha, float dt, bool mute)
        {
            if (!hasState) return;
            BikeView v;
            if (racer != null && racer.remote)
            {
                packetAge += dt;
                float lead = Mathf.Min(packetAge, 0.25f);
                var t = target;
                t.x += Mathf.Sin(t.heading) * t.speed * lead; t.z += Mathf.Cos(t.heading) * t.speed * lead;
                float k = 1f - Mathf.Exp(-14f * dt);
                shown = BikeView.Lerp(shown, t, k);
                shown.gear = t.gear; shown.crashed = t.crashed; shown.braking = t.braking; shown.slidingF = t.slidingF; shown.slidingR = t.slidingR; shown.scraping = t.scraping; shown.crashKind = t.crashKind;
                v = shown;
            }
            else v = BikeView.Lerp(prev, cur, Mathf.Clamp01(alpha));
            shown = v;
            braking = v.braking;
            rig.Pose(v, dt);
            var cam = Camera.main;
            Distance = cam != null ? (cam.transform.position - new Vector3(v.x, v.y, v.z)).magnitude : 0f;
            rig.Lights(braking, PistaDirector.I != null && PistaDirector.I.Dark);
            Audio(v, dt, mute);
            Effects(v, dt);
        }

        void Audio(in BikeView v, float dt, bool mute)
        {
            float fx = GameSettings.Effects * (mute ? 0f : 1f);
            float rpm01 = Mathf.Clamp01((v.rpm - 1200f) / 13000f);
            if (engine != null)
            {
                float pitchT = Mathf.Clamp(v.rpm / 4800f, 0.3f, 3.3f);
                engine.pitch = Mathf.Lerp(engine.pitch, pitchT, 1f - Mathf.Exp(-24f * dt));
                float volT = (v.crashed ? 0.25f : Mathf.Lerp(0.30f, 0.95f, Mathf.Clamp01(rpm01 * 0.55f + v.throttle * 0.45f))) * fx * (local ? 0.8f : 0.7f);
                engine.volume = Mathf.Lerp(engine.volume, volT, 1f - Mathf.Exp(-12f * dt));
                if (engineFilter != null) engineFilter.cutoffFrequency = Mathf.Lerp(engineFilter.cutoffFrequency, Mathf.Lerp(1400f, 9000f, v.throttle * 0.8f + 0.2f * rpm01), 1f - Mathf.Exp(-8f * dt));
            }
            if (!local) return;
            float speed = Mathf.Abs(v.speed);
            if (squeal != null)
            {
                float s = 0f;
                if (!v.crashed && speed > 4f)
                {
                    float f = Mathf.Max(v.slidingF ? 1f : 0f, v.slidingR ? 0.8f : 0f, v.absOn ? 0.45f : 0f, v.tcsOn ? 0.35f : 0f, Mathf.Clamp01((Mathf.Max(v.slipF, v.slipR) - 0.12f) * 2.5f));
                    s = f * Mathf.Clamp01(speed / 25f);
                }
                if (v.crashed && speed > 3f) s = 0.9f;
                squeal.volume = Mathf.Lerp(squeal.volume, s * 0.55f * fx, 1f - Mathf.Exp(-14f * dt));
                squeal.pitch = 0.9f + 0.25f * Mathf.Clamp01(speed / 80f) + (v.slidingF ? -0.1f : 0f);
            }
            if (rough != null)
            {
                bool kerb = v.surface == SurfaceKind.Kerb, grav = v.surface == SurfaceKind.Gravel || v.surface == SurfaceKind.Grass;
                var want = grav ? SoundBank.GravelRough : SoundBank.KerbRough;
                if (rough.clip != want && (kerb || grav)) { rough.clip = want; rough.Play(); }
                float r = (kerb || grav) && speed > 2f ? Mathf.Clamp01(speed / 30f) * (kerb ? 0.55f : 0.7f) : 0f;
                rough.volume = Mathf.Lerp(rough.volume, r * fx, 1f - Mathf.Exp(-14f * dt));
                rough.pitch = 0.7f + 0.6f * Mathf.Clamp01(speed / 50f);
            }
            if (wind != null)
            {
                float w = Mathf.Pow(Mathf.Clamp01((speed - 8f) / 75f), 1.2f) * 0.7f;
                wind.volume = Mathf.Lerp(wind.volume, w * fx, 1f - Mathf.Exp(-6f * dt));
                wind.pitch = 0.85f + 0.4f * Mathf.Clamp01(speed / 85f);
            }
        }

        void Effects(in BikeView v, float dt)
        {
            if (Distance > 70f) return;
            Vector3 pos = new Vector3(v.x, v.y, v.z);
            Vector3 fwd = new Vector3(Mathf.Sin(v.heading), 0f, Mathf.Cos(v.heading)), right = new Vector3(fwd.z, 0f, -fwd.x);
            float speed = Mathf.Abs(v.speed);
            // sparks: knee or peg on the road in a hard corner, and everything while the fallen bike slides
            bool sparking = (v.scraping && speed > 12f) || (v.crashed && speed > 4f);
            if (sparking)
            {
                sparkT -= dt;
                if (sparkT <= 0f)
                {
                    sparkT = v.crashed ? 0.025f : 0.045f;
                    float side = v.lean >= 0f ? 1f : -1f;
                    Vector3 at = pos + right * (side * (v.crashed ? 0.3f : 0.55f)) + Vector3.up * 0.04f;
                    int n = v.crashed ? 3 : 2;
                    for (int i = 0; i < n; i++)
                    {
                        var vel = -fwd * (speed * 0.25f) + right * (side * Random.Range(0.4f, 1.6f)) + Vector3.up * Random.Range(0.6f, 2.4f);
                        Fx.Particle(1, true, at, vel, new Vector3(0f, -9f, 0f), 0.035f, 0.01f, Random.Range(0.18f, 0.40f), new Color(1f, Random.Range(0.55f, 0.85f), 0.2f), 1f);
                    }
                }
            }
            // smoke from a sliding or locked tire, and a wheelie's burnout
            bool smoking = !v.crashed && speed > 6f && (v.slidingR || v.slidingF || (v.tcsOn && v.throttle > 0.9f && speed < 30f));
            if (smoking || (v.crashed && speed > 6f))
            {
                smokeT -= dt;
                if (smokeT <= 0f)
                {
                    smokeT = 0.06f;
                    Vector3 at = pos - fwd * 0.6f + Vector3.up * 0.05f;
                    Fx.Particle(0, false, at, -fwd * (speed * 0.08f) + Vector3.up * 0.5f + right * Random.Range(-0.3f, 0.3f), Vector3.zero, 0.2f, 1.1f, 1.0f, new Color(0.82f, 0.82f, 0.82f), 0.35f);
                }
            }
            // dust and gravel thrown up by the rear wheel off the asphalt
            if ((v.surface == SurfaceKind.Gravel || v.surface == SurfaceKind.Grass) && speed > 5f)
            {
                dustT -= dt;
                if (dustT <= 0f)
                {
                    dustT = Mathf.Lerp(0.09f, 0.03f, Mathf.Clamp01(speed / 40f));
                    Vector3 at = pos - fwd * 0.65f + Vector3.up * 0.06f;
                    var tint = v.surface == SurfaceKind.Gravel ? new Color(0.72f, 0.66f, 0.55f) : new Color(0.35f, 0.42f, 0.22f);
                    Fx.Particle(0, false, at, -fwd * (speed * 0.12f) + Vector3.up * Random.Range(0.4f, 1.4f) + right * Random.Range(-0.8f, 0.8f), Vector3.zero, 0.12f, 0.7f, 0.8f, tint, 0.4f);
                }
            }
        }

        public void SetCockpit(bool on) { if (rig != null) rig.SetCockpit(on); }

        void OnDestroy()
        {
            if (rig == null) return;
            // the materials made for this rider (the shared ones belong to the kit)
            if (rig.paint != null) Destroy(rig.paint);
            if (rig.paint2 != null) Destroy(rig.paint2);
            if (rig.suit != null) Destroy(rig.suit);
            if (rig.suit2 != null) Destroy(rig.suit2);
            if (rig.helmet != null) Destroy(rig.helmet);
            if (rig.tailMat != null) Destroy(rig.tailMat);
            if (rig.lensMat != null) Destroy(rig.lensMat);
        }
    }
}

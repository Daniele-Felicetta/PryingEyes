using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.Pista
{
    /// <summary>What speed does to the picture: a volume of its own on top of the game's one, made the first time the race camera is told a speed and thrown away with it, holding the camera's motion
    /// blur (with the objects' own motion, so that the bike the camera follows, which moves with it, stays sharp while the road and the trees streak) and a fringe of colour at the edges of the screen,
    /// both growing with the speed of the bike that is followed: nothing at 79 km/h and under, all of it from 281. The game's grade, bloom, vignette and grain are not touched: this volume names only the
    /// two effects the other one does not have. How much of it there is, is the player's: it follows 'Oscillazione visuale' of the settings (at zero the effects are off, like the bob of the walking
    /// player).
    /// The blur is worked out as a shutter, not as a strength: the override of the pipeline blurs over twice its intensity times the distance the picture moves between two frames (and in the mode that
    /// knows the objects it has no upper limit for the streak, the 'clamp' only works for the camera alone), so a fixed intensity would make a faster machine blur less and a slower one more, and the
    /// ground next to the camera would streak across half the screen. Here the intensity is the time the shutter stays open (4.2 ms at full speed) divided by twice the game time of the frame being
    /// drawn (not smoothed: a frame that came late has moved the picture further), and never more than a half: whatever the machine, the streak is as long as the picture moves in that time.</summary>
    public sealed class PistaFx
    {
        /// <summary>The time the shutter stays open at full speed (in seconds), the most the intensity of the pipeline's blur may be (a half is a streak as long as the frame's own movement: a shutter
        /// open all the time) and the strength of the colour fringe at full speed.</summary>
        public static float Exposure = 0.0042f, BlurCap = 0.5f, FringeMax = 0.12f;

        public const float MsFrom = 22f, MsTo = 78f;                // 79 km/h and 281 km/h

        GameObject go;
        VolumeProfile profile;
        MotionBlur blur;
        ChromaticAberration fringe;
        float level;                                                // the smoothed share of full speed, 0 to 1
        float frameDt = 1f / 60f;                                   // the game time of the frame being drawn: what the picture moved in is what the shutter is measured against
        int quiet;                                                  // frames still to be left without effects after a cut
        float hold = -1f;                                           // a level kept whatever the speed is (the photographs), or -1

        /// <summary>The share of the effect a speed in m/s earns: 0 up to 79 km/h, 1 from 281 km/h, smooth in between.</summary>
        public static float ShareOf(float speedMs)
        {
            float t = Mathf.Clamp01((Mathf.Abs(speedMs) - MsFrom) / (MsTo - MsFrom));
            return t * t * (3f - 2f * t);
        }

        /// <summary>The intensity of the pipeline's blur that makes the shutter stay open for 'Exposure' times this share of full speed when a frame lasts 'frame' seconds.</summary>
        public static float IntensityFor(float share, float frame)
        {
            return Mathf.Min(BlurCap, Exposure * share / (2f * Mathf.Max(0.001f, frame)));
        }

        public bool Alive => go != null;
        public float Level => level;
        public float FrameTime => frameDt;
        public float BlurNow => blur != null ? blur.intensity.value : 0f;
        public float FringeNow => fringe != null ? fringe.intensity.value : 0f;
        public bool BlurActive => blur != null && blur.active;

        public static PistaFx Create()
        {
            var fx = new PistaFx();
            fx.go = new GameObject("Pista effetti");
            var vol = fx.go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            vol.weight = 1f;
            fx.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = fx.profile;
            fx.blur = fx.profile.Add<MotionBlur>(true);
            fx.blur.mode.Override(MotionBlurMode.CameraAndObjects);
            fx.blur.quality.Override(MotionBlurQuality.High);
            fx.blur.intensity.Override(0f);
            fx.fringe = fx.profile.Add<ChromaticAberration>(true);
            fx.fringe.intensity.Override(0f);
            fx.Apply(0f);
            return fx;
        }

        /// <summary>One frame: the level follows the speed (it rises in about 0.6 s and falls in about 0.4, so that a crash or a stop does not snap the picture), scaled by the player's setting.</summary>
        public void Update(float dt, float speedMs)
        {
            frameDt = Mathf.Clamp(dt, 0.002f, 0.1f);                // not smoothed: a frame that came late has moved the picture further, and has to be blurred for less
            if (quiet > 0) { quiet--; level = 0f; Apply(0f); return; }
            float target = hold >= 0f ? hold : ShareOf(speedMs) * GameSettings.Motion;
            float rate = target > level ? 1.7f : 2.5f;
            level = hold >= 0f ? target : Mathf.MoveTowards(level, target, rate * Mathf.Max(0f, dt));
            Apply(level);
        }

        /// <summary>A cut or a put-back on the track: the picture before it has nothing to do with the one after, so nothing is blurred across it (the next two frames have no effect at all).</summary>
        public void Reset() { level = 0f; quiet = 2; Apply(0f); }

        /// <summary>For the photographs: take a share of full speed at once and keep it, whatever the speed and the settings say (a negative share lets go).</summary>
        public void Force(float share)
        {
            hold = share < 0f ? -1f : Mathf.Clamp01(share);
            if (hold >= 0f) { level = hold; quiet = 0; Apply(level); }
        }

        void Apply(float k)
        {
            if (blur == null) return;
            blur.intensity.value = IntensityFor(k, frameDt);
            fringe.intensity.value = FringeMax * k;
            bool on = k > 0.002f;
            blur.active = on; fringe.active = on;
        }

        public void Destroy()
        {
            if (go != null) Object.Destroy(go);
            if (profile != null) Object.Destroy(profile);
            go = null; profile = null; blur = null; fringe = null;
        }
    }
}

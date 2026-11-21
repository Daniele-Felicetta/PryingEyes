using System.Collections;
using System.Collections.Generic;
using PryingEyes.Deserto;
using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The director of a race on a circuit (one for the whole game; the races follow each other inside it). It owns the circuit, the rules (Race), the scenery, the bikes in the scene, the camera, the
    /// sky and the interface, and runs them every frame: it reads the controls once per frame, steps the physics at a fixed 60 Hz (the bikes are drawn between the last two steps, so that any refresh rate
    /// looks smooth), lights the start gantry, follows the laps and the finish, and lets the player start again (the same circuit or a new one) or go back to the menu. Setup and teardown of a race are in
    /// PistaDirector.Setup.cs; the cooperative game (friends' bikes arriving as poses) is in PistaDirector.Coop.cs.</summary>
    public sealed partial class PistaDirector : MonoBehaviour
    {
        public static PistaDirector I { get; private set; }

        public const float Dt = 1f / 60f;

        public int seed;
        [System.NonSerialized] public Circuit circuit;
        [System.NonSerialized] public Race race;
        [System.NonSerialized] public PistaWorld world;
        [System.NonSerialized] public PistaCamera cam;
        [System.NonSerialized] public PistaHud hud;
        [System.NonSerialized] public DesertSky sky;
        [System.NonSerialized] public Racer me;
        [System.NonSerialized] public PistaBikeView myView;
        public readonly List<PistaBikeView> views = new List<PistaBikeView>();
        /// <summary>The race is built and running (the loading screen is gone).</summary>
        public bool Ready { get; private set; }
        public bool Dark => sky != null && sky.Night > 0.35f;
        /// <summary>For the automated runs: how many races have been set up in this game (the first is 1).</summary>
        public int RaceCount { get; private set; }
        /// <summary>For the photographs of the tests: somebody else places the camera.</summary>
        [System.NonSerialized] public bool freeCamera;

        readonly PistaInput input = new PistaInput();
        BikeInputs held;
        int pendingShift;
        float acc, startWait, goFlash;
        int lastLights;
        BikeDriver autopilot;
        bool autopilotIsBot;
        AudioSource beep;
        bool resultsShown;
        bool started;

        void Awake()
        {
            I = this;
            beep = gameObject.AddComponent<AudioSource>();
            beep.playOnAwake = false; beep.spatialBlend = 0f; beep.bypassReverbZones = true;
        }

        void OnDestroy()
        {
            if (I == this)
            {
                CleanupRace();
                DesertSky.FogScale = 1f;
                if (sky != null) Destroy(sky.gameObject);
                if (cam != null) cam.Destroy();
                if (hud != null) hud.Destroy();
                I = null;
                PistaMode.End();
            }
        }

        public static PistaDirector Create(int seed)
        {
            var go = new GameObject("Pista");
            var d = go.AddComponent<PistaDirector>();
            d.seed = seed;
            return d;
        }

        // ------------------------------------------------------------------ every frame
        void Update()
        {
            if (!Ready || race == null || me == null || myView == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (replaying) { ReplayUpdate(dt); CoopUpdate(dt); return; }
            bool blocked = GameMenu.Blocked;
            float speed = me.bike != null ? Mathf.Max(me.bike.u, 0f) : 0f;

            // ---- the controls
            if (resultsShown)
            {
                if (!blocked) { input.ReadResults(); ResultsKeys(); }
                held = default(BikeInputs);
            }
            else
            {
                held = input.Read(dt, speed, blocked || race.phase == RacePhase.Over);
                pendingShift += held.shift;
                if (input.cameraPressed && !blocked) { cam.Cycle(); }
                if (input.ghostPressed && !blocked) ToggleGhost();
                if (input.resetPressed && !blocked) TryRespawn();
            }
            cam.lookBack = input.lookBackHeld && !blocked && !resultsShown;

            // ---- the start
            if (race.phase == RacePhase.Grid && !started)
            {
                startWait -= dt;
                if (startWait <= 0f && CanStartRace()) BeginCountdown();
            }

            // ---- the physics, 60 steps a second whatever the frame rate
            acc += dt;
            int guard = 0;
            while (acc >= Dt && guard++ < 6) { StepOnce(); acc -= Dt; }
            if (acc >= Dt) acc = 0f;                 // a very long frame: the rest is dropped, not caught up
            float alpha = acc / Dt;

            // ---- the lights
            if (race.lightsOn != lastLights)
            {
                lastLights = race.lightsOn;
                world.SetLamps(lastLights);
                if (lastLights > 0) Beep(false);
            }
            if (goFlash > 0f && (goFlash -= dt) <= 0f) world.SetLamps(0);

            // ---- the picture
            bool mute = blocked && !Coop.Active;
            for (int i = 0; i < views.Count; i++) views[i].Render(alpha, dt, mute);
            TimingRender(alpha, dt);
            if (!freeCamera) cam.Update(dt, myView.shown, myView.rig, world);
            myView.SetCockpit(cam.mode == CamMode.Cockpit);
            bool lampsShown = race.phase == RacePhase.Countdown || (race.phase == RacePhase.Racing && race.sinceGo < 1.6f);
            hud.Tick(dt, race, me, myView.shown, input.pad, lampsShown);
            if (resultsShown) hud.RefreshResults(race, me);
            CoopUpdate(dt);
        }

        void StepOnce()
        {
            if (me.bike != null && !me.remote)
            {
                if (autopilot != null)
                {
                    if (race.phase == RacePhase.Racing) me.input = autopilot.Drive(Dt);
                    else me.input = default(BikeInputs);
                }
                else if (race.phase == RacePhase.Racing && !me.finished)
                {
                    var i = held; i.shift = pendingShift; pendingShift = 0;
                    me.input = i;
                }
                else me.input = default(BikeInputs);
            }
            race.Step(Dt);
            for (int k = 0; k < views.Count; k++)
            {
                var r = views[k].racer;
                if (r.bike != null && !r.remote) views[k].Push(MakeView(r));
            }
            TimingStep();
            ReplayRecord();
            CoopStep();
        }

        /// <summary>The picture of a bike simulated here. Before the start the physics does not run (the bikes stand on the grid), so the engine speed is made up from the throttle.</summary>
        BikeView MakeView(Racer r)
        {
            bool mine = r == me;
            float thr = mine ? (autopilot != null ? r.input.throttle : held.throttle) : r.bike.throttleOut;
            bool brk = mine ? (r.input.brakeFront > 0.05f || r.input.brakeRear > 0.05f) : r.bike.longG < -0.25f;
            if (mine && autopilot == null && race.phase != RacePhase.Racing) brk = false;
            var v = BikeView.From(r.bike, thr, brk);
            if (race.phase == RacePhase.Grid || race.phase == RacePhase.Countdown)
            {
                float rev = mine ? held.throttle : (race.phase == RacePhase.Countdown ? Mathf.Clamp01((race.lightsOn - 3f) / 2f) : 0f);
                v.rpm = Mathf.Lerp(r.bike.spec.idleRpm, r.bike.spec.launchRpm, rev);
                v.throttle = rev;
            }
            return v;
        }

        // ------------------------------------------------------------------ the race
        void BeginCountdown()
        {
            started = true;
            race.StartCountdown();
            lastLights = 0;
            hud.Banner("PRONTI", circuit.name, 2.2f);
            CoopCountdownStarted();
        }

        void TryRespawn()
        {
            if (race.phase != RacePhase.Racing || me.bike == null || !me.bike.crashed || me.bike.crashTime < 0.6f) return;
            race.Respawn(me);
        }

        void Beep(bool high)
        {
            var clip = high ? SoundBank.BeepHigh : SoundBank.BeepLow;
            if (clip != null && beep != null) beep.PlayOneShot(clip, (high ? 0.7f : 0.55f) * GameSettings.Effects);
        }

        void HookRace()
        {
            race.OnGo = () =>
            {
                world.SetLamps(0, true); goFlash = 1.3f; Beep(true);
                hud.Banner("VIA!", "", 1.0f, new Color(0.38f, 0.92f, 0.5f));
            };
            race.OnLap = r =>
            {
                if (r != me) return;
                bool record;
                TimingLapDone(r, out record);
                if (r.finished) return;
                bool best = r.lastLap > 0f && Mathf.Approximately(r.lastLap, r.bestLap) && r.lap > 1;
                if (licence != null) { hud.Banner("GIRO " + r.lap + " / " + race.laps, PistaHud.Clock(r.lastLap) + LicenceLapText(record), 2.8f, LicenceColor(LastLapMedal)); return; }
                hud.Banner("GIRO " + r.lap + " / " + race.laps, PistaHud.Clock(r.lastLap) + (record ? "   -   NUOVO RECORD" : best ? "   -   il migliore" : ""), 2.6f, record || best ? new Color(0.78f, 0.52f, 1f) : (Color?)null);
            };
            race.OnFinish = r =>
            {
                if (r != me) return;
                if (licence != null) hud.Banner("TRAGUARDO", PistaHud.Clock(r.lastLap) + LicenceLapText(LastLapWasRecord), 4f, LicenceColor(LastLapMedal) ?? new Color(1f, 0.74f, 0.18f));
                else hud.Banner("TRAGUARDO", me.position + "°  -  " + PistaHud.Clock(r.finishTime) + (LastLapWasRecord ? "   -   NUOVO RECORD DEL GIRO" : ""), 4f, new Color(1f, 0.74f, 0.18f));
                Beep(true);
                if (CupActive) CupFinished();
                if (autopilot == null && me.bike != null)
                {
                    autopilot = new BikeDriver(me.bike, circuit, race.LineFor(me.bikeIndex), seed ^ 0x5151) { pace = 0.62f, mistakes = 0f };
                    autopilot.Reset(); autopilotIsBot = false;
                }
            };
            race.OnCrash = r =>
            {
                if (r != me) return;
                cam.Shake(r.crashKind == CrashKind.Wall || r.crashKind == CrashKind.Highside ? 0.35f : 0.18f);
            };
            race.OnRespawn = r => { if (r == me) cam.Snap(); };
            race.OnOver = () =>
            {
                if (cam.mode == CamMode.Cockpit) cam.mode = CamMode.Chase;
                ShowResults();
            };
        }

        void ShowResults()
        {
            resultsShown = true;
            ReplayFinish();
            bool host = CanRestart();
            CupResults();                  // the round counts for the season as soon as the table is up, and the foot of the table depends on whether there is a next round
            string hint = CupActive ? CupHint() : licence != null ? (input.pad ? "A  riprova   -   Y  replay   -   START  menu" : "INVIO  riprova   -   T  replay   -   ESC  menu")
                : Coop.Active && !host ? (input.pad ? "L'ospite decide la prossima   -   Y  replay   -   START  menu" : "L'ospite decide la prossima   -   T  replay   -   ESC  menu")
                : (input.pad ? "A  ancora   -   X  nuova pista   -   Y  replay   -   START  menu" : "INVIO  ancora   -   N  nuova pista   -   T  replay   -   ESC  menu");
            hud.ShowResults(race, me, circuit.name, hint);
            LicenceResults();
        }

        void ResultsKeys()
        {
            if (input.menuReplay && StartReplay()) return;
            if (!CanRestart()) return;
            if (CupActive) { if (input.menuAgain) CupNext(); return; }          // a cup goes round its calendar: no 'again' and no 'new circuit'
            if (input.menuAgain) RestartRace(false);
            else if (input.menuNew && licence == null) RestartRace(true);
        }

        /// <summary>Alone, or as the host of a cooperative game: the others wait for the host to start.</summary>
        bool CanRestart() { return !Coop.Active || Coop.IsHost; }

        /// <summary>Alone the lights start by themselves a moment after the intro; the host of a cooperative race waits for every friend to have built the circuit and for Enter (CoopCanStart); a friend
        /// waits for the host's go (CoopOnMessage).</summary>
        bool CanStartRace()
        {
            if (!coopRace || !Coop.Active) return true;
            return Coop.IsHost && CoopCanStart();
        }

        public void RestartRace(bool newTrack)
        {
            if (!CanRestart()) return;
            if (newTrack) seed = PistaMode.NewSeed(false);
            if (Coop.IsHost && Coop.Game != null) Coop.Game.PistaNewRace(seed);       // another race of the session: a new epoch, and the friends build it too
            StopAllCoroutines();
            StartCoroutine(SetupRace(seed, true));
        }

        /// <summary>The line under "In pausa": the circuit, the lap, the place and the time of the race.</summary>
        public string PauseText()
        {
            string head = CupActive ? Cups.All[cupIndex].name.ToUpperInvariant() + "  ·  GARA " + Mathf.Min(cupSeason.round + 1, Cups.All[cupIndex].Rounds) + "/" + Cups.All[cupIndex].Rounds
                : licence != null ? "PATENTE  ·  " + licence.test.name.ToUpperInvariant() : "GARA SU PISTA";
            if (race == null || me == null || circuit == null) return head;
            string t = race.phase == RacePhase.Racing || race.phase == RacePhase.Over ? PistaHud.Clock(race.time) : "in griglia";
            return head + "  ·  " + circuit.name + "   ·   GIRO " + me.CurrentLap(race.laps) + "/" + race.laps + "   ·   " + me.position + "° SU " + race.racers.Count + "   ·   " + t;
        }

        // ------------------------------------------------------------------ for the tests and the other scripts
        public bool Resulting => resultsShown;
        public BikeView ViewOfMe => myView != null ? myView.shown : default(BikeView);
        public bool AutopilotOn => autopilot != null;
        public bool BotDriven => autopilot != null && autopilotIsBot;
    }
}

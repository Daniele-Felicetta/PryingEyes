using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>Timing against yourself: a trace of the lap being ridden (the clock against the distance, every ten metres) and a recording of the bike (a frame every 1/20 s); when a lap is over and clean,
    /// PistaRecords keeps it if it is the best of its kind for this bike on this circuit. The best lap of the kind being ridden (the standing-start lap, or a flying lap) is the reference: the ghost
    /// of it rides beside the player, a see-through bike at the place it was at the same time into the lap, and the interface shows how many seconds ahead or behind the player is, ten metres after ten
    /// metres. A lap with a fall in it is not kept (the way back onto the track must never be a way to a record). G shows or hides the ghost.</summary>
    public sealed partial class PistaDirector
    {
        LapTrace trace;
        PoseTape lapTape;
        bool traceStarted, lapSealed;
        int tapeStep, lapCrashes0, lapKind;
        Ghost reference;
        PistaBikeView ghostView;
        bool ghostOn = true;
        float lastDelta;
        bool deltaValid;
        /// <summary>For the tests: the laps kept, the last lap was a new record, and the last delta shown.</summary>
        public int LapsKept { get; private set; }
        public bool LastLapWasRecord { get; private set; }
        public Ghost Reference => reference;
        public bool GhostShown => ghostView != null && ghostView.gameObject.activeSelf;
        public bool DeltaShown => deltaValid;
        public float DeltaNow => lastDelta;

        string BikeId { get { return Bikes.Get(me.bikeIndex).id; } }

        /// <summary>A race is set up: a fresh trace and tape for the first lap, and the reference for it (the best standing-start lap of this bike here).</summary>
        void TimingBegin()
        {
            TimingEnd();
            trace = new LapTrace(circuit.length); lapTape = new PoseTape();
            traceStarted = false; lapSealed = false; tapeStep = 0; lapCrashes0 = 0; lapKind = 0;
            LapsKept = 0; LastLapWasRecord = false;
            SelectReference(0);
        }

        void TimingEnd()
        {
            if (ghostView != null) Destroy(ghostView.gameObject);
            ghostView = null; reference = null; trace = null; lapTape = null; deltaValid = false;
            if (hud != null) { hud.SetDelta(false, 0f); hud.SetRecord(""); hud.SetLicenceGoals(null); }
        }

        void SelectReference(int kind)
        {
            lapKind = kind;
            reference = me != null && circuit != null ? PistaRecords.Best(circuit, BikeId, kind) : null;
            if (reference == null && ghostView != null) ghostView.gameObject.SetActive(false);
            hud.SetRecord(reference != null ? (PistaRecords.Persistent(circuit.seed) ? "RECORD  " : "FANTASMA  ") + PistaHud.Clock(reference.lapTime) : "");
        }

        /// <summary>After every step of the physics: the distance and the time of the lap go on the trace, and every third step (20 a second) a frame of the bike goes on the tape.</summary>
        void TimingStep()
        {
            if (trace == null || race.phase != RacePhase.Racing || me == null || me.bike == null || me.remote || me.finished) return;
            float dist = me.progress - me.lap * circuit.length;
            float time = race.time - me.lapStart;
            if (dist >= 0f && !traceStarted) { trace.Start(time - dist / Mathf.Max(me.speed, 4f)); traceStarted = true; }
            if (traceStarted) trace.Feed(dist, time);
            if (tapeStep++ % 3 == 0) lapTape.Add(ToFrame(MakeView(me)));
        }

        /// <summary>The player has just finished a lap (the race has already counted it): seal the trace, keep the lap if it is clean and the best, start the next, and say so.</summary>
        void TimingLapDone(Racer r, out bool record)
        {
            record = false;
            if (trace == null) return;
            bool clean = r.crashes == lapCrashes0 && traceStarted;
            int kind = lapKind;
            LicenceLap(r, kind, clean);
            var before = clean ? PistaRecords.Best(circuit, BikeId, kind) : null;
            trace.Seal(r.lastLap);
            if (clean && PistaRecords.Submit(circuit, BikeId, kind, r.lastLap, trace, lapTape))
            {
                LapsKept++;
                record = before != null || PistaRecords.Persistent(circuit.seed);
            }
            LastLapWasRecord = record;
            trace = new LapTrace(circuit.length); lapTape = new PoseTape();
            traceStarted = false; tapeStep = 0; lapCrashes0 = r.crashes;
            if (!r.finished) SelectReference(1);
        }

        /// <summary>Every frame: the ghost where it was at this time into the lap, and the gap to it.</summary>
        void TimingRender(float alpha, float dt)
        {
            bool racing = race != null && race.phase == RacePhase.Racing && me != null && !me.finished && !resultsShown;
            deltaValid = false;
            if (racing && reference != null)
            {
                float t = race.time - me.lapStart;
                if (ghostOn && t <= reference.lapTime + 1f)
                {
                    if (ghostView == null) ghostView = PistaBikeView.CreateGhost(transform, Bikes.Get(me.bikeIndex).meshStyle, PistaMode.RiderLivery(me.skin));
                    if (!ghostView.gameObject.activeSelf) ghostView.gameObject.SetActive(true);
                    ghostView.SetScripted(ToView(reference.tape.At(t)));
                    ghostView.Render(alpha, dt, true);
                }
                else if (ghostView != null && ghostView.gameObject.activeSelf) ghostView.gameObject.SetActive(false);
                if (traceStarted) deltaValid = LapTrace.Delta(reference.trace, me.progress - me.lap * circuit.length, t, out lastDelta);
            }
            else if (ghostView != null && ghostView.gameObject.activeSelf) ghostView.gameObject.SetActive(false);
            hud.SetDelta(deltaValid, lastDelta);
        }

        void ToggleGhost()
        {
            ghostOn = !ghostOn;
            hud.Banner(ghostOn ? "FANTASMA" : "FANTASMA SPENTO", reference != null ? "il giro migliore di questa moto" : "ancora nessun giro da inseguire", 1.4f, new Color(0.55f, 0.85f, 1f));
        }

        // ------------------------------------------------------------------ the two forms of a bike's pose
        static PoseFrame ToFrame(in BikeView v)
        {
            ushort f = 0;
            if (v.crashed) f |= PoseFrame.Crashed; if (v.braking) f |= PoseFrame.Braking; if (v.slidingF) f |= PoseFrame.SlidingF; if (v.slidingR) f |= PoseFrame.SlidingR;
            if (v.scraping) f |= PoseFrame.Scraping; if (v.absOn) f |= PoseFrame.AbsOn; if (v.tcsOn) f |= PoseFrame.TcsOn; if (v.shifting) f |= PoseFrame.Shifting; if (v.limiter) f |= PoseFrame.Limiter;
            return new PoseFrame
            {
                x = v.x, y = v.y, z = v.z, heading = v.heading, lean = v.lean, pitch = v.pitch, steer = v.steer, hang = v.hang, tuck = v.tuck, body = v.body, speed = v.speed, rpm = v.rpm,
                spinR = v.spinR, spinF = v.spinF, forkC = v.forkC, shockC = v.shockC, throttle = v.throttle, slipF = v.slipF, slipR = v.slipR,
                gear = (byte)Mathf.Clamp(v.gear, 0, 255), crashKind = (byte)v.crashKind, surface = (byte)v.surface, flags = f
            };
        }

        static BikeView ToView(in PoseFrame p)
        {
            return new BikeView
            {
                x = p.x, y = p.y, z = p.z, heading = p.heading, lean = p.lean, pitch = p.pitch, steer = p.steer, hang = p.hang, tuck = p.tuck, body = p.body, speed = p.speed, rpm = p.rpm,
                spinR = p.spinR, spinF = p.spinF, forkC = p.forkC, shockC = p.shockC, throttle = p.throttle, slipF = p.slipF, slipR = p.slipR, gear = p.gear,
                crashed = p.Has(PoseFrame.Crashed), braking = p.Has(PoseFrame.Braking), slidingF = p.Has(PoseFrame.SlidingF), slidingR = p.Has(PoseFrame.SlidingR), scraping = p.Has(PoseFrame.Scraping),
                absOn = p.Has(PoseFrame.AbsOn), tcsOn = p.Has(PoseFrame.TcsOn), shifting = p.Has(PoseFrame.Shifting), limiter = p.Has(PoseFrame.Limiter),
                crashKind = (CrashKind)p.crashKind, surface = (SurfaceKind)p.surface
            };
        }
    }
}

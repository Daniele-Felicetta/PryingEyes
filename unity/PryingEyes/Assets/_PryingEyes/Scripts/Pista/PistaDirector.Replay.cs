using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>The replay of the race just ridden. While the race runs, a frame of every rider goes on a tape every third step of the physics (20 a second, riders on this machine from the physics, the others
    /// from the poses that came in); when the results are on the screen, T starts the replay: the race is shown again from the lights going out, every bike put where the tape says it was, from a television edit
    /// made by ReplayEdit (the start from past the line, cuts to the falls, the overtakes and the laps of the player, the finish at the line) or from a camera and a rider the viewer picks. Space pauses, the
    /// arrows jump, up and down change the speed, C changes camera, Tab changes rider, H hides the interface, P takes a photograph without it (into the pista-foto folder), T goes back to the results.</summary>
    public sealed partial class PistaDirector
    {
        RaceRecording recording;
        int recordStep;
        float recordFrom = -1f;
        bool replaying, replayPaused, replayAtEnd, photoBusy, haveDrawn, manualValid;
        float replayT, replaySpeed = 1f;
        List<Shot> replayShots;
        int replayIx, replayMode, replayFollow;
        Shot replayShot, lastDrawn, manualShot;
        PistaReplayUi replayUi;
        Quaternion camRot; bool camRotInit;
        string lastPhoto = "";

        static readonly float[] ReplaySpeeds = { 0.25f, 0.5f, 1f, 2f, 4f };
        static readonly ShotKind[] ManualKinds = { ShotKind.Chase, ShotKind.Far, ShotKind.Onboard, ShotKind.Pan, ShotKind.Fixed, ShotKind.Heli };
        static readonly string[] ModeNames = { "TELEVISIONE", "INSEGUIMENTO", "LONTANA", "CASCO", "PANORAMICA", "A BORDO PISTA", "ELICOTTERO" };
        static readonly string[] KindNames = { "inseguimento", "lontana", "casco", "panoramica", "a bordo pista", "elicottero", "partenza", "traguardo" };

        // ------------------------------------------------------------------ for the tests and the other scripts
        public bool Replaying { get { return replaying; } }
        public RaceRecording Recording { get { return recording; } }
        public List<Shot> ReplayPlan { get { return replayShots; } }
        public float ReplayTime { get { return replayT; } }
        public bool ReplayPaused { get { return replayPaused; } }
        public bool ReplayAtEnd { get { return replayAtEnd; } }
        public float ReplaySpeed { get { return replaySpeed; } }
        public Shot ReplayShotNow { get { return replayShot; } }
        public int ReplayModeNow { get { return replayMode; } }
        public int ReplayFollowNow { get { return replayFollow; } }
        public string LastPhoto { get { return lastPhoto; } }
        public bool ReplayUiShown { get { return replayUi != null && replayUi.Shown; } }
        /// <summary>The interface of the replay, for the photographs of the tests.</summary>
        public Canvas ReplayCanvas { get { return replayUi != null ? replayUi.CanvasRef : null; } }
        /// <summary>Show or hide the interface of the replay (the H key does the same).</summary>
        public void ReplaySetUi(bool on) { if (replayUi != null) replayUi.SetShown(on); }
        public bool CanReplay { get { return recording != null && recording.Frames > 40 && race != null && race.phase == RacePhase.Over && resultsShown && !replaying; } }

        // ------------------------------------------------------------------ the tape
        /// <summary>After every step of the physics: every third step (20 a second) a frame of every rider goes on the tape. The tape starts at the lights going out.</summary>
        void ReplayRecord()
        {
            if (race == null || race.phase != RacePhase.Racing) return;
            if (recording == null)
            {
                var names = new string[race.racers.Count];
                for (int i = 0; i < names.Length; i++) names[i] = race.racers[i].name;
                recording = new RaceRecording(names.Length, names, race.racers.IndexOf(me), circuit.length, race.laps);
                recordStep = 0; recordFrom = -1f;
            }
            if (recordStep++ % 3 != 0) return;
            if (recordFrom < 0f) recordFrom = race.time;
            int n = Mathf.Min(recording.count, race.racers.Count);
            for (int i = 0; i < n; i++)
            {
                var r = race.racers[i];
                PoseFrame f;
                if (r.bike != null && !r.remote) f = ToFrame(MakeView(r));
                else if (i < views.Count && views[i].hasState) f = ToFrame(views[i].shown);
                else f = new PoseFrame { x = r.x, y = r.y, z = r.z, heading = r.heading, lean = r.lean, speed = r.speed };
                recording.Add(i, f, r.progress);
            }
        }

        /// <summary>The race is over: who crossed the line, and when (on the clock of the tape).</summary>
        void ReplayFinish()
        {
            if (recording == null || race == null) return;
            for (int i = 0; i < recording.count && i < race.racers.Count; i++)
            {
                var r = race.racers[i];
                recording.finish[i] = r.finished && recordFrom >= 0f ? Mathf.Max(0f, r.finishTime - recordFrom) : -1f;
            }
        }

        void ReplayClear()
        {
            if (replayUi != null) { replayUi.Destroy(); replayUi = null; }
            replaying = false; replayShots = null; recording = null; recordFrom = -1f; haveDrawn = false; manualValid = false; photoBusy = false;
        }

        // ------------------------------------------------------------------ playing it back
        public bool StartReplay()
        {
            if (!CanReplay || recording.me < 0) return false;
            recording.Analyse();
            replayShots = ReplayEdit.Plan(recording, circuit, seed);
            if (replayShots.Count == 0) return false;
            replaying = true; replayT = 0f; replaySpeed = 1f; replayPaused = false; replayAtEnd = false; replayIx = 0; replayMode = 0; replayFollow = recording.me;
            haveDrawn = false; manualValid = false; camRotInit = false;
            hud.HideResults(); hud.SetVisible(false);
            for (int i = 0; i < views.Count; i++) views[i].scripted = true;
            if (ghostView != null) ghostView.gameObject.SetActive(false);
            replayUi = new PistaReplayUi(recording, circuit.name);
            cam.lookBack = false;
            return true;
        }

        /// <summary>Back to the results: the bikes stand where the race left them, the interface is the race's again.</summary>
        public void EndReplay()
        {
            if (!replaying) return;
            replaying = false;
            for (int i = 0; i < views.Count; i++) { views[i].scripted = false; views[i].SetCockpit(false); }
            if (replayUi != null) { replayUi.Destroy(); replayUi = null; }
            hud.SetVisible(true);
            ShowResults();
            cam.mode = CamMode.Chase; cam.Snap();
        }

        void ReplayUpdate(float dt)
        {
            bool blocked = GameMenu.Blocked;
            float dur = recording.Duration;
            if (!blocked) input.ReadReplay(); else input.ClearReplay();
            if (input.rpExit) { EndReplay(); return; }
            if (input.rpPause) { if (replayAtEnd) ReplaySeekTo(0f); else replayPaused = !replayPaused; }
            if (input.rpHome) ReplaySeekTo(0f); else if (input.rpEnd) ReplaySeekTo(dur);
            if (input.rpSeek != 0f) ReplaySeekTo(replayT + input.rpSeek);
            if (input.rpScrub != 0f) ReplaySeekTo(replayT + input.rpScrub * 6f * dt);
            if (input.rpSpeed != 0) ReplayStepSpeed(input.rpSpeed);
            if (input.rpCam) ReplayCycleCamera();
            if (input.rpRider != 0) ReplayFollowNext(input.rpRider);
            if (input.rpHide && replayUi != null) replayUi.SetShown(!replayUi.Shown);
            if (input.rpPhoto) StartCoroutine(ReplayPhoto());
            if (!blocked && !replayPaused)
            {
                replayT += dt * replaySpeed;
                if (replayT >= dur) { replayT = dur; replayPaused = true; replayAtEnd = true; }
            }
            DrawReplay(dt, blocked);
        }

        /// <summary>Jump to a moment of the race.</summary>
        public void ReplaySeekTo(float t)
        {
            if (!replaying) return;
            float dur = recording.Duration;
            bool wasEnd = replayAtEnd;
            replayT = Mathf.Clamp(t, 0f, dur);
            replayAtEnd = replayT >= dur - 1e-3f;
            if (wasEnd && !replayAtEnd) replayPaused = false;
            camRotInit = false;
        }

        public void ReplaySetPaused(bool on) { replayPaused = on; if (!on && replayAtEnd) ReplaySeekTo(0f); }

        public void ReplaySetSpeed(float speed)
        {
            int best = 0;
            for (int i = 1; i < ReplaySpeeds.Length; i++) if (Mathf.Abs(ReplaySpeeds[i] - speed) < Mathf.Abs(ReplaySpeeds[best] - speed)) best = i;
            replaySpeed = ReplaySpeeds[best];
            if (replayUi != null) replayUi.Toast("VELOCITA' x" + replaySpeed.ToString("0.##"), 1.2f);
        }

        void ReplayStepSpeed(int dir)
        {
            int cur = 2;
            for (int i = 0; i < ReplaySpeeds.Length; i++) if (Mathf.Approximately(ReplaySpeeds[i], replaySpeed)) cur = i;
            ReplaySetSpeed(ReplaySpeeds[Mathf.Clamp(cur + dir, 0, ReplaySpeeds.Length - 1)]);
        }

        /// <summary>The television edit, then each camera by hand on the rider on screen.</summary>
        public void ReplayCycleCamera()
        {
            if (!replaying) return;
            int next = (replayMode + 1) % (ManualKinds.Length + 1);
            if (replayMode == 0) replayFollow = replayShot.subject;
            replayMode = next; manualValid = false; haveDrawn = false;
            if (replayUi != null) replayUi.Toast(ModeNames[replayMode], 1.4f);
        }

        /// <summary>The next (or previous) rider in the order of the race at this moment: the camera by hand follows them.</summary>
        public void ReplayFollowNext(int dir)
        {
            if (!replaying) return;
            int n = recording.count, from = replayMode == 0 ? replayShot.subject : replayFollow;
            int pos = recording.PositionAt(from, replayT) - 1;
            int want = ((pos + dir) % n + n) % n;
            int pick = from;
            for (int r = 0; r < n; r++) if (recording.PositionAt(r, replayT) - 1 == want) { pick = r; break; }
            replayFollow = pick;
            if (replayMode == 0) replayMode = 1;
            manualValid = false; haveDrawn = false;
            if (replayUi != null) replayUi.Toast(recording.names[pick].ToUpperInvariant(), 1.4f);
        }

        Shot ReplayShotAt(float t)
        {
            if (replayMode == 0)
            {
                int n = replayShots.Count;
                while (replayIx > 0 && replayShots[replayIx].t0 > t) replayIx--;
                while (replayIx < n - 1 && replayShots[replayIx].t1 <= t) replayIx++;
                return replayShots[replayIx];
            }
            var kind = ManualKinds[replayMode - 1];
            if (!manualValid || manualShot.kind != kind || manualShot.subject != replayFollow || t < manualShot.t0 || t >= manualShot.t1)
            {
                manualShot = ReplayEdit.Manual(recording, circuit, kind, replayFollow, t, seed);
                manualValid = true;
            }
            return manualShot;
        }

        string ReplayModeLabel(in Shot s)
        {
            return replayMode == 0 ? "TV  -  " + KindNames[(int)s.kind] : ModeNames[replayMode];
        }

        /// <summary>The picture of this frame of the replay: every bike where the tape says, the camera of the shot, the interface.</summary>
        void DrawReplay(float dt, bool blocked)
        {
            Shot s = ReplayShotAt(replayT);
            bool cut = !haveDrawn || s.kind != lastDrawn.kind || s.subject != lastDrawn.subject || s.t0 != lastDrawn.t0;
            lastDrawn = s; haveDrawn = true; replayShot = s;
            if (cut) { cam.Snap(); camRotInit = false; }
            bool mute = blocked || replayPaused || Mathf.Abs(replaySpeed - 1f) > 0.01f;
            float rdt = replayPaused ? 0.0001f : dt;
            for (int i = 0; i < views.Count && i < recording.count; i++)
            {
                var v = views[i];
                v.SetScripted(ToView(recording.PoseAt(i, replayT)));
                v.Render(0f, rdt, mute);
                v.SetCockpit(i == s.subject && s.kind == ShotKind.Onboard);
            }
            var sv = views[Mathf.Clamp(s.subject, 0, views.Count - 1)];
            if (s.GameCamera)
            {
                cam.mode = s.kind == ShotKind.Chase ? CamMode.Chase : s.kind == ShotKind.Far ? CamMode.Far : CamMode.Cockpit;
                if (!freeCamera) cam.Update(dt, sv.shown, sv.rig, world);
            }
            else if (!freeCamera)
            {
                CamPose p;
                if (ShotCamera.Pose(s, recording, replayT, out p))
                {
                    Vector3 pos = new Vector3(p.x, p.y, p.z), look = new Vector3(p.lx, p.ly, p.lz);
                    float ground = world != null ? world.GroundY(pos.x, pos.z) : pos.y - 1f;
                    if (pos.y < ground + 0.45f) pos.y = ground + 0.45f;
                    Vector3 d = look - pos;
                    if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
                    var want = Quaternion.LookRotation(d.normalized, Vector3.up);
                    bool smooth = s.kind == ShotKind.Pan || s.kind == ShotKind.Grid || s.kind == ShotKind.Finish;
                    // a long lens follows the rider with a human's lag, but an operator does not lose him: the lag goes when the lens is nearly off him (the pass of a narrow picture; the test of the cameras
                    // measured a fifth of the frames of the pan cameras without the rider with the plain lag); a fixed camera, a helicopter and a paused picture are exact
                    if (!camRotInit || !smooth || replayPaused) { camRot = want; camRotInit = true; }
                    else
                    {
                        float err = Quaternion.Angle(camRot, want), limit = Mathf.Max(0.6f, 0.15f * p.fov);
                        float rate = 18f * (1f + 6f * Mathf.Max(0f, err / limit - 1f));
                        camRot = Quaternion.Slerp(camRot, want, 1f - Mathf.Exp(-rate * dt));
                    }
                    cam.cam.transform.SetPositionAndRotation(pos, camRot);
                    cam.cam.fieldOfView = p.fov;
                    cam.TickFx(dt, sv.shown.speed);
                }
            }
            if (replayUi != null) replayUi.Tick(dt, recording, replayT, replayPaused, replaySpeed, ReplayModeLabel(s), s.subject, replayAtEnd);
        }

        // ------------------------------------------------------------------ the photograph
        /// <summary>A photograph of the replay without the interface, into the folder of the photographs (an automated run puts it in the cache, not in the player's folder). The camera of the picture draws
        /// the scene again into a picture of its own, twice as wide as the window (at most 4K), so the photograph does not depend on the size or the state of the window.</summary>
        public IEnumerator ReplayPhoto()
        {
            if (photoBusy || !replaying) yield break;
            photoBusy = true;
            bool was = replayUi != null && replayUi.Shown;
            if (replayUi != null) replayUi.SetShown(false);
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try
            {
                var camera = cam.cam;
                int W = Mathf.Clamp(Screen.width * 2, 1280, 3840), H = Mathf.Max(720, Mathf.RoundToInt(W / Mathf.Max(0.5f, (float)Screen.width / Mathf.Max(1, Screen.height))));
                var rt = RenderTexture.GetTemporary(W, H, 24);
                var prevTarget = camera.targetTexture;
                camera.targetTexture = rt;
                camera.Render();
                camera.targetTexture = prevTarget;
                tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                var old = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = old;
                RenderTexture.ReleaseTemporary(rt);
            }
            catch (Exception e) { Debug.LogWarning("[PryingEyes] pista foto: " + e.Message); if (tex != null) Destroy(tex); tex = null; }
            string path = "";
            if (tex != null)
            {
                try
                {
                    string dir = PhotoDir();
                    Directory.CreateDirectory(dir);
                    string name = circuit != null ? circuit.name : "pista";
                    foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
                    path = Path.Combine(dir, name.Replace(' ', '-') + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                }
                catch (Exception e) { Debug.LogWarning("[PryingEyes] pista foto: " + e.Message); path = ""; }
                Destroy(tex);
            }
            lastPhoto = path;
            if (replayUi != null)
            {
                replayUi.SetShown(was);
                replayUi.Toast(path.Length > 0 ? "FOTO SALVATA" : "FOTO NON RIUSCITA", 2.2f);
            }
            photoBusy = false;
        }

        static string PhotoDir()
        {
            return PistaMode.Volatile ? Path.Combine(Application.temporaryCachePath, "pista-foto-prova") : Path.Combine(Application.persistentDataPath, "pista-foto");
        }
    }
}

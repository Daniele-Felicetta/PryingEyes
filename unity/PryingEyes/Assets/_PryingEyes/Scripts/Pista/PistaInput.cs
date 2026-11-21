using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The controls of the bike. Keyboard: W or Up gas, S or Down front brake, Space rear brake, A D or Left Right lean in and out of the corners, Shift tuck behind the screen, J and L shift the
    /// body left and right and I and K forward and back (the right hand's twin of the controller's right stick), E and Q gear up and down (only without the automatic gearbox), C camera, R back on the track after a
    /// fall that has not been cleared. Controller: right trigger gas, left trigger front brake, X rear brake, left stick steer, right stick body shift (sideways and forward or back), A tuck, RB and LB gears, Y camera. The keys and the triggers ramp the way a hand does: the gas opens in a third of a second, the front brake builds up, and the steering
    /// grows the longer the key is held, slower the faster the bike goes (a tap is a small correction, not a fall), and above 36 km/h the keys reach only part of the lean (KeyShaper: the lab rides with it).</summary>
    public sealed class PistaInput
    {
        readonly KeyShaper keys = new KeyShaper();
        float bodyX, bodyY;

        /// <summary>For the automated runs: when set, the bike takes these inputs instead of the keyboard.</summary>
        public static bool BotOn;
        public static BikeInputs Bot;

        /// <summary>Which device gave the last command (the hints on the screen name its keys).</summary>
        public bool pad;
        public bool cameraPressed, lookBackHeld, resetPressed, ghostPressed;
        /// <summary>Enter (A on the controller) this frame: the host of a cooperative race gives the go with it.</summary>
        public bool startPressed;

        public void Reset() { keys.Reset(); bodyX = bodyY = 0f; }

        static float Dead(float v, float dead) { float a = Mathf.Abs(v); return a < dead ? 0f : Mathf.Sign(v) * Mathf.Min(1f, (a - dead) / (1f - dead)); }

        public BikeInputs Read(float dt, float speed, bool blocked)
        {
            cameraPressed = false; resetPressed = false; startPressed = false; ghostPressed = false;
            if (BotOn) return Bot;
            var inp = new BikeInputs();
            if (blocked) { Reset(); return inp; }
            startPressed = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

            // ---- keyboard
            bool up = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            bool down = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            bool rear = Input.GetKey(KeyCode.Space);
            bool tuck = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int shift = Input.GetKeyDown(KeyCode.E) ? 1 : Input.GetKeyDown(KeyCode.Q) ? -1 : 0;
            cameraPressed = Input.GetKeyDown(KeyCode.C);
            ghostPressed = Input.GetKeyDown(KeyCode.G);
            resetPressed = Input.GetKeyDown(KeyCode.R);
            lookBackHeld = Input.GetKey(KeyCode.V);

            keys.Step(up, down, rear, left, right, speed, dt);
            // the weight of the body, with the right hand: it moves the way a body does, not like a switch
            float kx = (Input.GetKey(KeyCode.L) ? 1f : 0f) - (Input.GetKey(KeyCode.J) ? 1f : 0f);
            float ky = (Input.GetKey(KeyCode.I) ? 1f : 0f) - (Input.GetKey(KeyCode.K) ? 1f : 0f);
            bodyX = Mathf.MoveTowards(bodyX, kx, 4.2f * dt);
            bodyY = Mathf.MoveTowards(bodyY, ky, 4.2f * dt);
            float lean = bodyX, fwdPad = 0f;
            bool any = up || down || left || right || rear || kx != 0f || ky != 0f;

            // ---- controller (the raw triggers and sticks: the mapper of the game only knows buttons for these)
            float sPad = 0f, tPad = 0f, bPad = 0f, rPad = 0f;
            int idx = PadControls.Index;
            if (idx >= 0 && PadControls.Connected)
            {
                var raw = PadControls.Poll(idx);
                if (raw.connected)
                {
                    tPad = raw.rightTrigger / 255f; bPad = raw.leftTrigger / 255f;
                    float lx = Dead(raw.lx / 32767f, 0.14f);
                    sPad = Mathf.Sign(lx) * Mathf.Pow(Mathf.Abs(lx), 1.35f);
                    float rx = Dead(raw.rx / 32767f, 0.2f);
                    if (rx != 0f) lean = rx;
                    fwdPad = Dead(raw.ry / 32767f, 0.2f);
                    rPad = (raw.buttons & PadControls.X) != 0 ? 1f : 0f;
                    if ((raw.buttons & PadControls.A) != 0) tuck = true;
                    if (padPrev != raw.buttons)
                    {
                        ushort pressed = (ushort)(raw.buttons & ~padPrev);
                        if ((pressed & PadControls.RightShoulder) != 0) shift = 1;
                        if ((pressed & PadControls.LeftShoulder) != 0) shift = -1;
                        if ((pressed & PadControls.Y) != 0) cameraPressed = true;
                        if ((pressed & PadControls.B) != 0) resetPressed = true;
                        if ((pressed & PadControls.BackBtn) != 0) ghostPressed = true;
                        if ((pressed & PadControls.A) != 0) startPressed = true;
                        padPrev = raw.buttons;
                    }
                    if (tPad > 0.02f || bPad > 0.02f || Mathf.Abs(sPad) > 0.05f || rPad > 0f || rx != 0f || fwdPad != 0f) { pad = true; }
                }
            }
            if (any) pad = false;

            inp.throttle = Mathf.Max(keys.thr, tPad);
            inp.brakeFront = Mathf.Max(keys.bf, bPad);
            inp.brakeRear = Mathf.Max(keys.br, rPad);
            inp.steer = Mathf.Abs(sPad) > Mathf.Abs(keys.steer) ? sPad : keys.steer;
            inp.riderLean = lean;
            inp.riderFwd = Mathf.Abs(fwdPad) > Mathf.Abs(bodyY) ? fwdPad : bodyY;
            inp.tuck = tuck;
            inp.shift = shift;
            return inp;
        }

        ushort padPrev;

        /// <summary>After the race: Enter (A on the controller) races again on the same circuit, N (X) on a new one, T (Y) watches the replay.</summary>
        public bool menuAgain, menuNew, menuReplay;

        // ---- the replay: Space (A) pauses, the arrows (D-pad) jump five seconds (thirty with Shift), up and down (the bumpers) change the speed, the triggers wind it back and forward, C (X) changes the camera,
        // Tab (Y) the rider, H (left stick) hides the interface, P (right stick) takes a photograph, T or Enter (B) goes back to the results
        public bool rpExit, rpPause, rpCam, rpHide, rpPhoto, rpHome, rpEnd;
        public int rpSpeed, rpRider;
        public float rpSeek, rpScrub;

        public void ClearReplay() { rpExit = rpPause = rpCam = rpHide = rpPhoto = rpHome = rpEnd = false; rpSpeed = rpRider = 0; rpSeek = rpScrub = 0f; }

        public void ReadReplay()
        {
            ClearReplay();
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            rpExit = Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Backspace);
            rpPause = Input.GetKeyDown(KeyCode.Space);
            rpCam = Input.GetKeyDown(KeyCode.C); rpHide = Input.GetKeyDown(KeyCode.H); rpPhoto = Input.GetKeyDown(KeyCode.P);
            rpHome = Input.GetKeyDown(KeyCode.Home); rpEnd = Input.GetKeyDown(KeyCode.End);
            rpSpeed = ((Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) ? 1 : 0) - ((Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) ? 1 : 0);
            float big = shift ? 30f : 5f;
            rpSeek = ((Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) ? big : 0f) - ((Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) ? big : 0f);
            rpRider = Input.GetKeyDown(KeyCode.Tab) ? (shift ? -1 : 1) : 0;
            int idx = PadControls.Index;
            if (idx >= 0 && PadControls.Connected)
            {
                var raw = PadControls.Poll(idx);
                if (raw.connected)
                {
                    ushort pressed = (ushort)(raw.buttons & ~padPrev);
                    if (pressed != 0) pad = true;
                    if ((pressed & PadControls.A) != 0) rpPause = true;
                    if ((pressed & PadControls.B) != 0) rpExit = true;
                    if ((pressed & PadControls.X) != 0) rpCam = true;
                    if ((pressed & PadControls.Y) != 0) rpRider = 1;
                    if ((pressed & PadControls.RightShoulder) != 0) rpSpeed = 1;
                    if ((pressed & PadControls.LeftShoulder) != 0) rpSpeed = -1;
                    if ((pressed & PadControls.DpadRight) != 0) rpSeek = 5f;
                    if ((pressed & PadControls.DpadLeft) != 0) rpSeek = -5f;
                    if ((pressed & PadControls.LeftStick) != 0) rpHide = true;
                    if ((pressed & PadControls.RightStick) != 0) rpPhoto = true;
                    float scrub = raw.rightTrigger / 255f - raw.leftTrigger / 255f;
                    if (Mathf.Abs(scrub) > 0.15f) { rpScrub = scrub; pad = true; }
                    padPrev = raw.buttons;
                }
            }
        }

        public void ReadResults()
        {
            menuAgain = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            menuNew = Input.GetKeyDown(KeyCode.N);
            menuReplay = Input.GetKeyDown(KeyCode.T);
            cameraPressed = resetPressed = false;
            int idx = PadControls.Index;
            if (idx >= 0 && PadControls.Connected)
            {
                var raw = PadControls.Poll(idx);
                if (raw.connected)
                {
                    ushort pressed = (ushort)(raw.buttons & ~padPrev);
                    if ((pressed & PadControls.A) != 0) { menuAgain = true; pad = true; }
                    if ((pressed & PadControls.X) != 0) { menuNew = true; pad = true; }
                    if ((pressed & PadControls.Y) != 0) { menuReplay = true; pad = true; }
                    padPrev = raw.buttons;
                }
            }
        }
    }
}

using UnityEngine;

namespace PryingEyes.Pista
{
    /// <summary>The controls of the bike. Keyboard: W or Up gas, S or Down front brake, Space rear brake, A D or Left Right lean in and out of the corners, Shift tuck behind the screen, E and Q gear up and down
    /// (only without the automatic gearbox), C camera, R back on the track after a fall that has not been cleared. Controller: right trigger gas, left trigger front brake, X rear brake, left stick steer,
    /// right stick body shift, A tuck, RB and LB gears, Y camera. The keys and the triggers ramp the way a hand does: the gas opens in a third of a second, the front brake builds up, and the steering
    /// grows the longer the key is held, slower the faster the bike goes (a tap is a small correction, not a fall).</summary>
    public sealed class PistaInput
    {
        float thr, bf, br, steer;

        /// <summary>For the automated runs: when set, the bike takes these inputs instead of the keyboard.</summary>
        public static bool BotOn;
        public static BikeInputs Bot;

        /// <summary>Which device gave the last command (the hints on the screen name its keys).</summary>
        public bool pad;
        public bool cameraPressed, lookBackHeld, resetPressed;
        /// <summary>Enter (A on the controller) this frame: the host of a cooperative race gives the go with it.</summary>
        public bool startPressed;

        public void Reset() { thr = bf = br = steer = 0f; }

        static float Dead(float v, float dead) { float a = Mathf.Abs(v); return a < dead ? 0f : Mathf.Sign(v) * Mathf.Min(1f, (a - dead) / (1f - dead)); }

        public BikeInputs Read(float dt, float speed, bool blocked)
        {
            cameraPressed = false; resetPressed = false; startPressed = false;
            if (BotOn) return Bot;
            var inp = new BikeInputs();
            if (blocked) { thr = bf = br = steer = 0f; return inp; }
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
            resetPressed = Input.GetKeyDown(KeyCode.R);
            lookBackHeld = Input.GetKey(KeyCode.V);

            thr = Mathf.MoveTowards(thr, up ? 1f : 0f, (up ? 3.2f : 7f) * dt);
            bf = Mathf.MoveTowards(bf, down ? 1f : 0f, (down ? 2.4f : 9f) * dt);
            br = Mathf.MoveTowards(br, rear ? 1f : 0f, (rear ? 4f : 9f) * dt);
            float target = (right ? 1f : 0f) - (left ? 1f : 0f);
            float k = Mathf.Clamp01(speed / 70f);
            float rate = Mathf.Lerp(4.6f, 1.7f, k);
            if (target != 0f && Mathf.Sign(target) != Mathf.Sign(steer) && steer != 0f) rate *= 2.2f;       // reversing is quicker than building up
            if (target == 0f) rate = Mathf.Lerp(7.5f, 4.5f, k);
            steer = Mathf.MoveTowards(steer, target, rate * dt);
            float lean = 0f;
            bool any = up || down || left || right || rear;

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
                    lean = Dead(raw.rx / 32767f, 0.2f);
                    rPad = (raw.buttons & PadControls.X) != 0 ? 1f : 0f;
                    if ((raw.buttons & PadControls.A) != 0) tuck = true;
                    if (padPrev != raw.buttons)
                    {
                        ushort pressed = (ushort)(raw.buttons & ~padPrev);
                        if ((pressed & PadControls.RightShoulder) != 0) shift = 1;
                        if ((pressed & PadControls.LeftShoulder) != 0) shift = -1;
                        if ((pressed & PadControls.Y) != 0) cameraPressed = true;
                        if ((pressed & PadControls.B) != 0) resetPressed = true;
                        if ((pressed & PadControls.A) != 0) startPressed = true;
                        padPrev = raw.buttons;
                    }
                    if (tPad > 0.02f || bPad > 0.02f || Mathf.Abs(sPad) > 0.05f || rPad > 0f) { pad = true; }
                }
            }
            if (any) pad = false;

            inp.throttle = Mathf.Max(thr, tPad);
            inp.brakeFront = Mathf.Max(bf, bPad);
            inp.brakeRear = Mathf.Max(br, rPad);
            inp.steer = Mathf.Abs(sPad) > Mathf.Abs(steer) ? sPad : steer;
            inp.riderLean = lean;
            inp.tuck = tuck;
            inp.shift = shift;
            return inp;
        }

        ushort padPrev;

        /// <summary>After the race: Enter (A on the controller) races again on the same circuit, N (X) on a new one.</summary>
        public bool menuAgain, menuNew;

        public void ReadResults()
        {
            menuAgain = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            menuNew = Input.GetKeyDown(KeyCode.N);
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
                    padPrev = raw.buttons;
                }
            }
        }
    }
}

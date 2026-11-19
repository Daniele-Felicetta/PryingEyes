using UnityEngine;
using PryingEyes.Deserto;

namespace PryingEyes
{
    /// <summary>Guidare la moto del deserto (DesertBike). Mentre si guida UpdateBody non muove il corpo a piedi: chiama RideTick, che usa lo stesso CharacterController (quindi terreno, muri, scale,
    /// salti e cadute sono quelli di sempre, e i nemici colpiscono lo stesso corpo) ma con la dinamica della moto: gas, freno, retromarcia, sterzo che dipende dalla velocita', aderenza laterale
    /// (il freno a mano la toglie: si derapa), turbo, volo quando il terreno sparisce sotto le ruote e urto contro i muri (la velocita' persa in un fotogramma e' il danno).
    /// La camera e' sull'occhio del pilota (con lo sguardo libero fino a 100 gradi: il mouse gira solo la testa, la rotta la danno A e D). Il mirino dell'arma si nasconde: in sella non si spara.
    /// Si scende con Usa (F), da fermi o frenando da soli.</summary>
    public partial class PlayerController
    {
        DesertBike bike;
        float rideHeading, rideSteer, rideLook, rideLookPrev, rideLookIdle, rideTop = DesertBike.TopSpeed;
        float rideSpeedShown, rideCrashCool, rideNoiseT, rideBlend = 1f, rideDip, rideVyPrev, rideStepOffset, rideLandCool;
        bool rideGrounded = true, rideDismount, rideHintDismount;
        int rideKmh;
        string rideHint;
        Vector3 rideCamFrom;

        public bool Riding => bike != null;
        public DesertBike Bike => bike;
        /// <summary>Velocita' lungo la rotta in m/s (negativa in retromarcia).</summary>
        public float RideSpeed => rideSpeedShown;
        /// <summary>Verso dove punta la moto (radianti, come yaw): lo sguardo puo' essere altrove. Va ai compagni in cooperativa.</summary>
        public float RideHeading => rideHeading;
        /// <summary>I secondi in cui la moto non puo' ferire ancora (dopo un urto): per le prove.</summary>
        public float RideCrashCooldown => rideCrashCool;
        /// <summary>Le ruote toccano il suolo (falso in volo): per le prove.</summary>
        public bool RideGrounded => rideGrounded;
        /// <summary>Per le prove: come premere Usa in corsa (la moto frena da sola e il pilota scende appena si ferma).</summary>
        public void RideRequestDismount() { if (Riding) rideDismount = true; }

        /// <summary>Sale in sella: il corpo va sulla moto, l'arma sparisce, la visuale resta dov'era e si trasferisce sull'occhio del pilota in mezzo secondo.</summary>
        public void Mount(DesertBike b)
        {
            if (b == null || Riding || !alive || Downed || b.rider != null || b.puppet) return;
            CancelMantle();
            bike = b;
            b.Take(this);
            rideHeading = b.Heading;
            rideCamFrom = cam != null ? cam.position : transform.position;
            rideBlend = 0f;
            Teleport(b.transform.position);
            if (cc != null)
            {
                rideStepOffset = cc.stepOffset;
                cc.stepOffset = 0.5f;               // le dune ruvide e i sassi bassi: la moto li scavalca
                cc.height = heightStand; cc.center = new Vector3(0f, heightStand * 0.5f, 0f);
            }
            slide = false; crouch = false; lean = 0f; recP = recY = recTargetP = recTargetY = 0f;
            rideSteer = 0f; rideTop = DesertBike.TopSpeed; rideSpeedShown = 0f; rideCrashCool = 0f; rideDip = 0f; rideLandCool = 0f;
            rideGrounded = true; rideDismount = false; rideNoiseT = 0f; rideVyPrev = 0f;
            rideLook = Mathf.Clamp(Mathf.DeltaAngle(rideHeading * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad, -1.75f, 1.75f);
            rideLookPrev = rideLook; rideLookIdle = 0f;
            yaw = rideHeading + rideLook;
            if (weapon != null) { weapon.ads = 0f; if (weapon.ViewRootT != null) weapon.ViewRootT.gameObject.SetActive(false); }       // in mira la sensibilita' del mouse sarebbe ridotta
            rideHint = null;
            hud?.SetPrompt("");
            hud?.Toast("In sella: W gas, S freno e retromarcia, A e D sterzo, Maiusc turbo, Spazio freno a mano, " + PadControls.UseKey + " scendi.");
        }

        /// <summary>Scende di lato (a sinistra, poi a destra, dietro, davanti: dove la capsula sta libera) e lascia la moto parcheggiata dov'e'. Se non c'e' spazio resta in sella.</summary>
        public void Dismount()
        {
            var b = bike;
            if (b == null) return;
            Vector3 root = b.transform.position;
            float h = rideHeading;
            Vector3 fwd = new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)), right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3[] tries = { -right * 1.05f, right * 1.05f, -fwd * 1.6f, fwd * 1.6f };
            bool found = false;
            Vector3 spot = root;
            foreach (var d in tries)
            {
                Vector3 p = root + d;
                if (!Physics.Raycast(p + Vector3.up * 1.6f, Vector3.down, out var gh, 3.6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                p.y = gh.point.y + 0.03f;
                if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.3f, 0.34f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                spot = p; found = true; break;
            }
            if (!found) { hud?.Toast("Non c'e' spazio per scendere qui."); rideDismount = false; return; }
            b.Park(root, h);
            EndRide();
            Teleport(spot);
            pitch = 0f;
        }

        /// <summary>Lascia la moto dove si trova (si e' andati a terra, e' morto, e' cambiato qualcosa): niente teletrasporto del corpo.</summary>
        public void AbortRide()
        {
            var b = bike;
            if (b == null) return;
            b.Park(b.transform.position, rideHeading);
            EndRide();
        }

        void EndRide()
        {
            bike = null;
            rideDismount = false;
            vel = Vector3.zero;
            SpeedH = 0f; IsSprinting = false; IsCrouching = false;
            if (cc != null) cc.stepOffset = rideStepOffset > 0f ? rideStepOffset : 0.3f;
            if (weapon != null && weapon.ViewRootT != null) weapon.ViewRootT.gameObject.SetActive(true);
            hud?.SetPrompt("");
            DesertBike.BotReset();
        }

        /// <summary>Un fotogramma in sella (chiamato da UpdateBody al posto del movimento a piedi, con il mouse gia' sommato a yaw e i comandi di movimento gia' letti in 'f' e 's').</summary>
        void RideTick(float dt, float f, float s, bool inputEnabled)
        {
            var b = bike;
            dt = Mathf.Min(dt, 0.05f);
            bool bot = GameManager.autoTest && DesertBike.BotActive;          // i comandi forzati: solo le prove (anche quella a due processi, che ha -autotest)

            // ---- sguardo: il mouse ha gia' girato 'yaw'; quello che eccede la rotta e' la testa girata di lato
            float look = Mathf.DeltaAngle(rideHeading * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            if (Mathf.Abs(look - rideLookPrev) > 0.0015f) rideLookIdle = 0f; else rideLookIdle += dt;
            look = Mathf.Clamp(look, -1.75f, 1.75f);

            // ---- comandi
            float throttle = Mathf.Clamp(f, -1f, 1f), steerIn = Mathf.Clamp(s, -1f, 1f);
            bool boostKey = inputEnabled && (SeatInput.GetKey(KeyCode.LeftShift) || SeatInput.GetKey(KeyCode.RightShift) || TouchControls.Held(TouchControls.Action.Sprint));
            bool handbrake = inputEnabled && (SeatInput.GetKey(KeyCode.Space) || TouchControls.Held(TouchControls.Action.Jump));
            if (bot) { throttle = DesertBike.botThrottle; steerIn = DesertBike.botSteer; boostKey = DesertBike.botBoost; handbrake = DesertBike.botHandbrake; }
            bool usePressed = inputEnabled && (SeatInput.GetKeyDown(KeyCode.F) || TouchControls.Down(TouchControls.Action.Use));
            if (usePressed)
            {
                if (Mathf.Abs(rideSpeedShown) < 4f) { Dismount(); if (bike == null) return; }
                else { rideDismount = !rideDismount; hud?.Toast(rideDismount ? "Rallenti: scendi appena ti fermi." : "Resti in sella."); }
            }
            if (rideDismount && Mathf.Abs(rideSpeedShown) < 3.5f) { Dismount(); if (bike == null) return; }
            if (rideDismount) { throttle = Mathf.Min(throttle, -0.6f); boostKey = false; handbrake = false; }          // frena da sola

            // ---- la velocita' attuale, scomposta lungo la rotta e di traverso
            Vector3 fwd = new Vector3(Mathf.Sin(rideHeading), 0f, Mathf.Cos(rideHeading)), right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 hv = new Vector3(vel.x, 0f, vel.z);
            float along = Vector3.Dot(hv, fwd), lat = Vector3.Dot(hv, right);
            bool grounded = rideGrounded;

            // ---- gas, freno, retromarcia, resistenza
            float topGoal = boostKey && throttle > 0.1f ? DesertBike.BoostSpeed : DesertBike.TopSpeed;
            rideTop = Mathf.MoveTowards(rideTop, topGoal, (topGoal > rideTop ? 5f : 9f) * dt);
            bool braking = false;
            if (grounded)
            {
                if (throttle > 0.05f)
                {
                    if (along < -0.3f) { along = Mathf.MoveTowards(along, 0f, 16f * dt); braking = true; }
                    else along += 12f * throttle * Mathf.Clamp01(1f - along / rideTop) * dt;
                }
                else if (throttle < -0.05f)
                {
                    if (along > 0.6f) { along = Mathf.MoveTowards(along, 0f, 17f * -throttle * dt); braking = true; }
                    else along = Mathf.MoveTowards(along, -DesertBike.ReverseSpeed, 4.5f * -throttle * dt);
                }
                else along = Mathf.MoveTowards(along, 0f, (1.4f + 0.006f * along * along) * dt);
                if (handbrake) { along = Mathf.MoveTowards(along, 0f, 20f * dt); braking = braking || along > 1f; }
                if (along > rideTop) along = Mathf.MoveTowards(along, rideTop, 6f * dt);          // finito il turbo, la velocita' rientra piano
            }

            // ---- il terreno davanti deve esserci: i blocchi di sabbia si costruiscono man mano, e a 80 km/h si arriva prima di loro
            var terr = DesertTerrain.I;
            if (terr != null && Mathf.Abs(along) > 7f)
            {
                Vector3 ahead = transform.position + fwd * (Mathf.Sign(along) * (Mathf.Abs(along) * 0.9f + 8f));
                if (!terr.Ready(ahead, 0f)) along = Mathf.MoveTowards(along, Mathf.Sign(along) * 7f, 14f * dt);
            }

            // ---- sterzo: il manubrio si gira piano, la curva e' stretta da lenti e larga da veloci, da fermi non si gira
            float rate = Mathf.Abs(steerIn) > Mathf.Abs(rideSteer) ? 4.2f : 7f;
            rideSteer = Mathf.MoveTowards(rideSteer, steerIn, rate * dt);
            float sp = Mathf.Abs(along);
            float turn = Mathf.Lerp(2.3f, 0.55f, Mathf.Pow(Mathf.Clamp01(sp / DesertBike.TopSpeed), 0.8f));
            float omega = rideSteer * turn * Mathf.Clamp01(sp / 1.8f) * (along < 0f ? -1f : 1f) * (grounded ? 1f : 0.15f);
            if (handbrake && grounded && sp > 4f) omega *= 1.4f;
            hv = fwd * along + right * lat;
            rideHeading += omega * dt;
            fwd = new Vector3(Mathf.Sin(rideHeading), 0f, Mathf.Cos(rideHeading)); right = new Vector3(fwd.z, 0f, -fwd.x);
            along = Vector3.Dot(hv, fwd); lat = Vector3.Dot(hv, right);
            lat *= Mathf.Exp(-(grounded ? (handbrake ? 1.2f : 9f) : 0.15f) * dt);              // l'aderenza: la velocita' di traverso svanisce, col freno a mano quasi no (si derapa)
            hv = fwd * along + right * lat;

            // ---- verticale e movimento (stessa capsula del giocatore a piedi)
            rideVyPrev = vel.y;
            vel.x = hv.x; vel.z = hv.z;
            vel.y = grounded ? -4f : Mathf.Max(vel.y - gravity * dt, -30f);
            bool wasGrounded = grounded;
            cc.Move(vel * dt);
            Vector3 av = cc.velocity; av.y = 0f;                // quello che il corpo ha fatto davvero (prima dell'aggancio al suolo, che sovrascrive la velocita' del controller)
            rideGrounded = cc.isGrounded;
            if (!rideGrounded && wasGrounded)
            {
                // un piccolo salto del terreno (cresta, gradino in discesa) non stacca le ruote: si resta a terra se il suolo e' vicino
                float snap = 0.30f + 0.012f * Mathf.Abs(along);
                if (Physics.Raycast(transform.position + Vector3.up * 0.25f, Vector3.down, out var gh, 0.25f + snap, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) && gh.normal.y > 0.7f)
                {
                    cc.Move(Vector3.down * Mathf.Max(0f, gh.distance - 0.25f + 0.02f));
                    rideGrounded = true;
                }
            }
            if (rideCrashCool > 0f) rideCrashCool -= dt;
            if (rideLandCool > 0f) rideLandCool -= dt;

            // ---- urto: la velocita' che il muro ha tolto in un fotogramma. Il resto (salite, sfregamenti) non conta.
            float afterAlong = Vector3.Dot(av, fwd);
            float loss = along - afterAlong;
            if (along > 8f && loss > 8f && rideCrashCool <= 0f)
            {
                rideCrashCool = 0.8f;
                b.Impact((loss - 8f) / 14f);
                Kick(2.6f, Random.Range(-1f, 1f) * 2.2f, 7f);
                fovKick -= 5f;
                rideDip = Mathf.Max(rideDip, 0.14f);
                float dmg = Mathf.Min(45f, (loss - 8f) * 2.4f);
                if (dmg > 3f) Hurt(dmg, transform.position - fwd * 2f);
                if (bike == null) return;
            }
            if (loss > 0.06f * Mathf.Max(along, 1f) + 0.5f) { vel.x = av.x; vel.z = av.z; }          // contro un ostacolo la velocita' vera e' quella che il corpo ha fatto
            // atterraggio dopo il volo
            if (rideGrounded && !wasGrounded && rideVyPrev < -6.5f && rideLandCool <= 0f)
            {
                rideLandCool = 0.4f;
                float hard = Mathf.Clamp01((-rideVyPrev - 6.5f) / 14f);
                rideDip = Mathf.Max(rideDip, 0.04f + 0.2f * hard);
                b.Impact(hard * 0.8f);
                Kick(1.2f + 2.4f * hard, 0f, 8f);
                if (rideVyPrev < -15f) { Hurt((-rideVyPrev - 15f) * 4f, transform.position + Vector3.up); if (bike == null) return; }
            }
            if (rideGrounded) vel.y = -4f;
            Vector3 hvNow = new Vector3(vel.x, 0f, vel.z);
            float hs = hvNow.magnitude;
            rideSpeedShown = Vector3.Dot(hvNow, fwd);

            // ---- sguardo e uscita dal sistema: yaw = rotta + testa girata (la testa torna dritta se non la si muove mentre si corre)
            if (rideLookIdle > 1.2f && hs > 3f) look = Mathf.MoveTowards(look, 0f, 1.6f * dt);
            rideLook = look; rideLookPrev = look;
            yaw = rideHeading + look;

            // ---- la moto prende la posa, il corpo la segue
            bool boosting = boostKey && throttle > 0.1f && rideTop > DesertBike.TopSpeed + 1f;
            b.Pose(transform.position, rideHeading, rideGrounded, vel, rideSteer, Mathf.Clamp01(throttle), boosting, braking, handbrake, dt);
            SpeedH = hs;
            IsSprinting = hs > 7.5f;
            IsCrouching = false;
            hud?.SetBars(hp, shield);
            int kmh = Mathf.RoundToInt(Mathf.Abs(rideSpeedShown) * 3.6f);
            if (rideHint == null || kmh != rideKmh || rideDismount != rideHintDismount)
            {
                rideKmh = kmh; rideHintDismount = rideDismount;
                rideHint = kmh + " km/h" + (rideDismount ? "    frena per scendere" : "    [" + PadControls.UseKey + "] scendi");
            }
            hud?.SetPrompt(rideHint);

            // ---- il rumore: il motore si sente, piu' lontano quanto piu' si corre
            rideNoiseT -= dt;
            if (rideNoiseT <= 0f) { rideNoiseT = 0.45f; if (hs > 1.5f) Noise.Emit(transform.position, Mathf.Min(26f, 7f + 0.85f * hs), NoiseKind.Other); }          // da fermi il motore gira al minimo e non fa rumore

            // ---- camera sull'occhio del pilota
            Camera c = cam.GetComponent<Camera>();
            float motion = GameSettings.Motion;
            float baseFov = SplitScreen.Active ? SplitScreen.FovFor(GameSettings.Fov, Seats.ViewAspect(Seats.Current, c.aspect)) : GameSettings.Fov;
            float targetFov = baseFov + (17f * Mathf.Clamp01(hs / DesertBike.BoostSpeed) + fovKick) * motion;
            fovKick *= Mathf.Max(0f, 1f - 6f * dt);
            c.fieldOfView += (targetFov - c.fieldOfView) * Mathf.Min(1f, 8f * dt);
            rideBlend = Mathf.MoveTowards(rideBlend, 1f, dt / 0.4f);
            rideDip = Mathf.MoveTowards(rideDip, 0f, 0.7f * dt);
            Vector3 eye = Vector3.Lerp(rideCamFrom, b.EyeWorld, Mathf.SmoothStep(0f, 1f, rideBlend));
            // vibrazione del motore e del terreno: poca, e solo in marcia
            float shake = Mathf.Clamp01(hs / 20f) * 0.006f * motion;
            float t = Time.time;
            Vector3 jitter = new Vector3(Mathf.PerlinNoise(t * 31f, 0.3f) - 0.5f, Mathf.PerlinNoise(t * 37f, 5.1f) - 0.5f, 0f) * (2f * shake);
            eye += b.Body.rotation * jitter + Vector3.down * rideDip;
            cam.position = eye;
            // la testa segue una parte dell'inclinazione della moto (beccheggio e rollio), non tutta
            Quaternion bodyPart = Quaternion.Euler(0f, rideHeading * Mathf.Rad2Deg, 0f) * Quaternion.Euler(b.PitchVisual * 0.5f, 0f, b.RollVisual * 0.5f * motion);
            cam.rotation = bodyPart * Quaternion.Euler(pitch * Mathf.Rad2Deg - recP, look * Mathf.Rad2Deg + recY, 0f);

            // il rinculo della visuale (urti, atterraggi) rientra come a piedi
            if (recDelay > 0f) recDelay -= dt;
            else
            {
                float rr = recRecover * (1f + Mathf.Abs(recTargetP) * 0.12f);
                recTargetP = Mathf.MoveTowards(recTargetP, 0f, rr * dt);
                recTargetY = Mathf.MoveTowards(recTargetY, 0f, rr * 0.6f * dt);
            }
            float fol = 1f - Mathf.Exp(-34f * dt);
            recP += (recTargetP - recP) * fol;
            recY += (recTargetY - recY) * fol;
        }
    }
}

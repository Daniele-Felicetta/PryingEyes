using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Deserto;

namespace PryingEyes
{
    /// <summary>Guidare la moto del deserto (DesertBike). Mentre si guida UpdateBody non muove il corpo a piedi: chiama RideTick, che usa lo stesso CharacterController (quindi terreno, muri, scale,
    /// salti e cadute sono quelli di sempre, e i nemici colpiscono lo stesso corpo) ma con la dinamica della moto: gas, freno, retromarcia, sterzo che dipende dalla velocita', aderenza laterale
    /// (il freno a mano la toglie: si derapa), turbo, volo quando il terreno sparisce sotto le ruote e urto contro i muri (la velocita' persa in una frenata da urto, sommando i fotogrammi di un decimo di secondo, e' il danno).
    /// La camera e' sull'occhio del pilota (con lo sguardo libero fino a 100 gradi: il mouse gira solo la testa, la rotta la danno A e D). L'arma sta riposta (Weapon.Ride.cs) e si alza con fuoco,
    /// mira, ricarica o il cambio arma (RideGunTick): si spara con la destra, la sinistra resta sul manubrio.
    /// Si scende con Usa (F), da fermi o frenando da soli.</summary>
    public partial class PlayerController
    {
        DesertBike bike;
        float rideHeading, rideSteer, rideLook, rideLookPrev, rideLookIdle, rideTop = DesertBike.TopSpeed;
        float rideSpeedShown, rideCrashCool, rideNoiseT, rideDip, rideVyPrev, rideStepOffset, rideLandCool;
#if !PE_DISTRIBUTION_BUILD
        float rideMountedAt;
#endif
        float rideGunIdle = 99f;                           // i secondi dall'ultima volta che l'arma e' servita (fuoco, mira, ricarica, cambio): oltre un secondo e mezzo torna giu'
        // la velocita' fatta lungo la rotta negli ultimi istanti (tempo di guida, velocita'), in un anello: serve a riconoscere una frenata da urto
        const int RideHistLen = 256;
        const float RideHitWindow = 0.14f, RideHitDrop = 8f;
        readonly float[] rideHistT = new float[RideHistLen], rideHistV = new float[RideHistLen];
        int rideHistN, rideHistHead;
        float rideSimT;
        bool rideGrounded = true, rideDismount, rideHintDismount, rideBoosting;
        string rideHint;

        public bool Riding => bike != null;
        /// <summary>Il turbo e' inserito (Maiusc col gas, e la velocita' oltre il massimo normale): lo legge il tachimetro.</summary>
        public bool RideBoosting => rideBoosting;
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

        /// <summary>L'ultimo contatto laterale del controller in sella (la superficie e' piu' ripida di 45 gradi: non il suolo): contro che cosa, dove, con quale normale e in quale fotogramma. Lo leggono le
        /// prove che cercano gli urti contro il niente (-desertonly motopiano): senza questo una frenata si vede ma non si sa a cosa dare la colpa.</summary>
        public struct RideContact { public Collider collider; public Vector3 point, normal; public int frame; }
        RideContact rideContact;
        public RideContact RideLastContact => rideContact;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (bike == null || hit.normal.y > 0.7f) return;
            rideContact = new RideContact { collider = hit.collider, point = hit.point, normal = hit.normal, frame = Time.frameCount };
        }

        /// <summary>Per le prove: gli ultimi 48 fotogrammi della guida (passo, velocita' comandata lungo la rotta, quella che il corpo ha fatto, la differenza e quanto ne ha accumulato la frenata da urto), per
        /// capire un urto che non ha un contatto (RideTraceNext e' il prossimo posto da scrivere, in giro).</summary>
        public struct RideFrame { public float dt, along, done, loss, acc; public bool grounded; }
        public readonly RideFrame[] RideTrace = new RideFrame[48];
        public int RideTraceNext { get; private set; }

        /// <summary>Sale in sella: il corpo va sulla moto, l'arma sparisce, la visuale resta dov'era e si trasferisce sull'occhio del pilota in mezzo secondo.</summary>
        public void Mount(DesertBike b)
        {
            if (b == null || Riding || !alive || Downed || b.rider != null || b.puppet) return;
            CancelMantle();
            ThirdPersonView.BeginRideTransition(this);
            bike = b;
            b.Take(this);
            rideHeading = b.Heading;
#if !PE_DISTRIBUTION_BUILD
            rideMountedAt = Time.time;
#endif
            Teleport(b.transform.position);
            ridePlowed.Clear(); RidePlowCount = 0;              // il Teleport ha spento e riacceso il controller: le coppie da ignorare sono sparite con lui
            rideGhosts.Clear(); rideGhostT = 0f;
            if (cc != null)
            {
                rideStepOffset = cc.stepOffset;
                cc.stepOffset = 0.5f;               // le dune ruvide e i sassi bassi: la moto li scavalca
                cc.height = heightStand; cc.center = new Vector3(0f, heightStand * 0.5f, 0f);
            }
            slide = false; crouch = false; lean = 0f; viewRecoil.Clear();
            rideSteer = 0f; rideTop = b.Def.top; rideSpeedShown = 0f; rideCrashCool = 0f; rideDip = 0f; rideLandCool = 0f; rideHistN = 0;
            rideGrounded = true; rideDismount = false; rideNoiseT = 0f; rideVyPrev = 0f; rideBoosting = false;
            rideLook = Mathf.Clamp(Mathf.DeltaAngle(rideHeading * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad, -1.75f, 1.75f);
            rideLookPrev = rideLook; rideLookIdle = 0f;
            yaw = rideHeading + rideLook;
            rideGunIdle = 99f;
            if (weapon != null) { weapon.SetRideMode(true); weapon.ads = 0f; if (weapon.ViewRootT != null) weapon.ViewRootT.gameObject.SetActive(false); }       // arma riposta: la si alza quando serve (in mira la sensibilita' del mouse sarebbe ridotta)
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
            ThirdPersonView.BeginRideTransition(this);
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

        // ---- le moto parcheggiate degli altri posti (schermo condiviso): chi esce dal garage non le trova davanti
        readonly List<DesertBike> rideGhosts = new List<DesertBike>();
        float rideGhostT;

        /// <summary>Ogni 0,2 s: le moto parcheggiate di un altro posto entro otto metri diventano attraversabili per questo pilota, e tornano solide quando e' a piu' di dodici (o quando qualcuno ci sale).
        /// Da soli e in cooperativa le moto sono tutte dello stesso posto e non cambia niente.</summary>
        void RideGhostBikes(float dt)
        {
            if (cc == null || bike == null) return;
            rideGhostT -= dt;
            if (rideGhostT > 0f) return;
            rideGhostT = 0.2f;
            Vector3 p = transform.position;
            var all = DesertBike.All;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || o == bike || o.puppet || o.slot == bike.slot || o.rider != null || rideGhosts.Contains(o)) continue;
                Vector3 d = o.transform.position - p; d.y = 0f;
                if (d.sqrMagnitude > 64f) continue;
                if (o.GhostFor(cc, true)) rideGhosts.Add(o);
            }
            for (int i = rideGhosts.Count - 1; i >= 0; i--)
            {
                var o = rideGhosts[i];
                if (o == null) { rideGhosts.RemoveAt(i); continue; }
                Vector3 d = o.transform.position - p; d.y = 0f;
                if (d.sqrMagnitude > 144f || o.rider != null) { o.GhostFor(cc, false); rideGhosts.RemoveAt(i); }
            }
        }

        void RideGhostEnd()
        {
            for (int i = 0; i < rideGhosts.Count; i++) if (rideGhosts[i] != null) rideGhosts[i].GhostFor(cc, false);
            rideGhosts.Clear();
        }

        void EndRide()
        {
            RidePlowEnd();
            RideGhostEnd();
            bike = null;
            rideDismount = false; rideBoosting = false;
            vel = Vector3.zero;
            SpeedH = 0f; IsSprinting = false; IsCrouching = false;
            if (cc != null) cc.stepOffset = rideStepOffset > 0f ? rideStepOffset : 0.3f;
            weapon?.SetRideMode(false);
            if (weapon != null && weapon.ViewRootT != null) weapon.ViewRootT.gameObject.SetActive(true);
            hud?.SetPrompt("");
            DesertBike.BotReset();
        }

        /// <summary>Un fotogramma in sella (chiamato da UpdateBody al posto del movimento a piedi, con il mouse gia' sommato a yaw e i comandi di movimento gia' letti in 'f' e 's').</summary>
        void RideTick(float dt, float f, float s, bool inputEnabled, Vector2 lookDelta)
        {
            var b = bike;
            var def = b.Def;                  // i numeri di questa moto (velocita', ripresa, frenata, sterzo)
            dt = Mathf.Min(dt, 0.05f);
            RideGhostBikes(dt);
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
            float topGoal = boostKey && throttle > 0.1f ? def.boost : def.top;
            rideTop = Mathf.MoveTowards(rideTop, topGoal, (topGoal > rideTop ? 5f : 9f) * dt);
            bool braking = false;
            if (grounded)
            {
                if (throttle > 0.05f)
                {
                    if (along < -0.3f) { along = Mathf.MoveTowards(along, 0f, 16f * def.brake * dt); braking = true; }
                    else along += 12f * def.accel * throttle * Mathf.Clamp01(1f - along / rideTop) * dt;
                }
                else if (throttle < -0.05f)
                {
                    if (along > 0.6f) { along = Mathf.MoveTowards(along, 0f, 17f * def.brake * -throttle * dt); braking = true; }
                    else along = Mathf.MoveTowards(along, -def.reverse, 4.5f * -throttle * dt);
                }
                else along = Mathf.MoveTowards(along, 0f, (1.4f + 0.006f * along * along) * dt);
                if (handbrake) { along = Mathf.MoveTowards(along, 0f, 20f * def.brake * dt); braking = braking || along > 1f; }
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
            float turn = Mathf.Lerp(2.3f, 0.55f, Mathf.Pow(Mathf.Clamp01(sp / def.top), 0.8f)) * def.turn;
            float omega = rideSteer * turn * Mathf.Clamp01(sp / 1.8f) * (along < 0f ? -1f : 1f) * (grounded ? 1f : 0.15f);
            if (handbrake && grounded && sp > 4f) omega *= 1.4f;
            hv = fwd * along + right * lat;
            rideHeading += omega * dt;
            fwd = new Vector3(Mathf.Sin(rideHeading), 0f, Mathf.Cos(rideHeading)); right = new Vector3(fwd.z, 0f, -fwd.x);
            along = Vector3.Dot(hv, fwd); lat = Vector3.Dot(hv, right);
            lat *= Mathf.Exp(-(grounded ? (handbrake ? 1.2f : 9f) : 0.15f) * dt);              // l'aderenza: la velocita' di traverso svanisce, col freno a mano quasi no (si derapa)
            hv = fwd * along + right * lat;
            // ---- investire: oltre i 20 km/h chi cammina sulla strada non e' un muro (PlayerController.Ride.Plow.cs): il corpo lo attraversa, il nemico prende il colpo, la moto perde un po' di velocita'
            if (RidePlow(dt, ref hv, along)) { along = Vector3.Dot(hv, fwd); lat = Vector3.Dot(hv, right); }

            // ---- verticale e movimento (stessa capsula del giocatore a piedi)
            rideVyPrev = vel.y;
            vel.x = hv.x; vel.z = hv.z;
            vel.y = grounded ? -4f : Mathf.Max(vel.y - gravity * dt, -30f);
            bool wasGrounded = grounded;
            Vector3 pBefore = transform.position;
            cc.Move(vel * dt);
            // quello che il corpo ha fatto davvero in questo spostamento (prima dell'aggancio al suolo). Non si legge cc.velocity: il controller divide lo spostamento per il tempo VERO del fotogramma, e 'dt' qui e' al piu'
            // 50 ms: in un fotogramma lungo (148 ms alla prima medaglia, alla nascita di un blocco di sabbia) a 59 km/h dava 19 km/h, una frenata da urto contro il niente e fino a 45 punti di danno al pilota
            Vector3 av = (transform.position - pBefore) / Mathf.Max(dt, 1e-4f); av.y = 0f;
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

            // ---- urto: la velocita' che il muro toglie. Il corpo tocca il muro a meta' di un fotogramma e si ferma nel successivo (o nei successivi): contando la perdita un fotogramma alla volta, un muro
            // preso a 50 km/h non era un urto una volta su sei (e a 36 km/h quasi sempre), a seconda di dove cadeva il fotogramma. Si guarda quindi la velocita' fatta (lungo la rotta) negli ultimi 0,14 s di
            // tempo di guida: se la piu' alta di allora supera gli 8 m/s e adesso ne e' stata tolta piu' di 8 (una frenata di oltre 57 m/s2, il triplo del freno a mano), e' un urto qualunque sia la durata
            // dei fotogrammi. Il resto (salite, sfregamenti, il freno) non conta. Prima si sommavano le perdite di ogni fotogramma oltre 60 m/s2 (60 x passo): a mille fotogrammi al secondo il rumore dei decimali
            // della posizione (a 500 m dall'origine 6 cm/s a fotogramma, quanto la soglia) le sommava senza fine, e ogni dieci-venti secondi di corsa a 83 km/h c'era un urto contro il niente.
            float afterAlong = Vector3.Dot(av, fwd);
            float loss = along - afterAlong;
            rideSimT += dt;
            float vTop = afterAlong;
            for (int i = 1; i <= rideHistN; i++)
            {
                int hk = (rideHistHead - i + RideHistLen) % RideHistLen;
                if (rideSimT - rideHistT[hk] > RideHitWindow) break;
                if (rideHistV[hk] > vTop) vTop = rideHistV[hk];
            }
            rideHistT[rideHistHead] = rideSimT; rideHistV[rideHistHead] = afterAlong;
            rideHistHead = (rideHistHead + 1) % RideHistLen; if (rideHistN < RideHistLen) rideHistN++;
            float drop = vTop - afterAlong;
            RideTrace[RideTraceNext++ % RideTrace.Length] = new RideFrame { dt = dt, along = along, done = afterAlong, loss = loss, acc = drop, grounded = rideGrounded };
            if (vTop > RideHitDrop && drop > RideHitDrop && rideCrashCool <= 0f)
            {
                float lost = drop;
                rideHistN = 0;
                rideCrashCool = 0.8f;
                b.Impact((lost - 8f) / 14f);
                Kick(2.6f, Random.Range(-1f, 1f) * 2.2f, 7f);
                fovKick -= 5f;
                rideDip = Mathf.Max(rideDip, 0.14f);
                float dmg = Mathf.Min(45f, (lost - 8f) * 2.4f);
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
            bool boosting = boostKey && throttle > 0.1f && rideTop > def.top + 1f;
            rideBoosting = boosting;
            b.Pose(transform.position, rideHeading, rideGrounded, vel, rideSteer, Mathf.Clamp01(throttle), boosting, braking, handbrake, dt);
            SpeedH = hs;
            IsSprinting = hs > 7.5f;
            IsCrouching = false;
            hud?.SetBars(hp, shield);
            // la velocita' la dice il tachimetro (HUD.DesertSpeed.cs): il suggerimento dice solo come si scende
            if (rideHint == null || rideDismount != rideHintDismount)
            {
                rideHintDismount = rideDismount;
                rideHint = rideDismount ? "frena per scendere" : "[" + PadControls.UseKey + "] scendi";
            }
            hud?.SetPrompt(rideHint);

            // ---- il rumore: il motore si sente, piu' lontano quanto piu' si corre
            rideNoiseT -= dt;
            if (rideNoiseT <= 0f) { rideNoiseT = 0.45f; if (hs > 1.5f) Noise.Emit(transform.position, Mathf.Min(26f, 7f + 0.85f * hs), NoiseKind.Other); }          // da fermi il motore gira al minimo e non fa rumore

            // ---- camera sull'occhio del pilota
            Camera c = cam.GetComponent<Camera>();
            float motion = GameSettings.Motion;
            float baseFov = SplitScreen.Active ? SplitScreen.FovFor(GameSettings.Fov, Seats.ViewAspect(Seats.Current, c.aspect)) : GameSettings.Fov;
            float targetFov = baseFov + (17f * Mathf.Clamp01(hs / def.boost) + fovKick) * motion;
            fovKick *= Mathf.Max(0f, 1f - 6f * dt);
            c.fieldOfView += (targetFov - c.fieldOfView) * Mathf.Min(1f, 8f * dt);
            rideDip = Mathf.MoveTowards(rideDip, 0f, 0.7f * dt);
            Vector3 eye = b.EyeWorld;
            // vibrazione del motore e del terreno: poca, e solo in marcia
            float shake = Mathf.Clamp01(hs / 20f) * 0.006f * motion;
            float t = Time.time;
            Vector3 jitter = new Vector3(Mathf.PerlinNoise(t * 31f, 0.3f) - 0.5f, Mathf.PerlinNoise(t * 37f, 5.1f) - 0.5f, 0f) * (2f * shake);
            eye += b.Body.rotation * jitter + Vector3.down * rideDip;
            cam.position = eye;
            // la testa segue una parte dell'inclinazione della moto (beccheggio e rollio), non tutta
            Quaternion bodyPart = Quaternion.Euler(0f, rideHeading * Mathf.Rad2Deg, 0f) * Quaternion.Euler(b.PitchVisual * 0.5f, 0f, b.RollVisual * 0.5f * motion);
            float headPitch = pitch * Mathf.Rad2Deg - viewRecoil.Pitch;
            cam.rotation = bodyPart * Quaternion.Euler(headPitch, look * Mathf.Rad2Deg + viewRecoil.Yaw, 0f);
            if (GameSettings.ThirdPerson || ThirdPersonView.Count > 0)
                ThirdPersonView.AfterEyePose(this, b.EyeWorld,
                    new Vector3(headPitch + b.PitchVisual * 0.5f, yaw * Mathf.Rad2Deg + viewRecoil.Yaw, 0f));

            // il rinculo della visuale (urti, atterraggi) rientra come a piedi
            viewRecoil.Advance(dt);

            RideGunTick(dt, hs, lookDelta, inputEnabled);
        }

        /// <summary>L'arma in sella: sta riposta finche' non si preme fuoco, mira, ricarica o si cambia arma; un secondo e mezzo dopo l'ultimo uso torna giu'. Dopo la camera: l'arma e' figlia dell'occhio.
        /// Il resto (cadenza, munizioni, rinculo, rumore) e' Weapon.Tick come a piedi, con la dispersione allargata dalla velocita' della moto.</summary>
        void RideGunTick(float dt, float speed, Vector2 lookDelta, bool inputEnabled)
        {
            var w = weapon;
            if (w == null || !w.RideMode) return;
            bool fire = inputEnabled && (SeatInput.GetMouseButton(0) || TouchControls.Held(TouchControls.Action.Fire));
            bool other = inputEnabled && (SeatInput.GetMouseButton(1) || SeatInput.GetKeyDown(KeyCode.R) || SeatInput.GetKeyDown(KeyCode.Alpha1) || SeatInput.GetKeyDown(KeyCode.Alpha2) || SeatInput.GetKeyDown(KeyCode.X)
                || Mathf.Abs(SeatInput.GetAxis("Mouse ScrollWheel")) > 0.01f || TouchControls.Held(TouchControls.Action.Aim) || TouchControls.Down(TouchControls.Action.Reload) || TouchControls.Down(TouchControls.Action.Swap));
            if (Weapon.rideBotFire) fire = true;          // la prova che spara da sola (anche con la moto ferma: DesertBike.BotActive non c'entra; la alzano solo le prove)
            if (fire || other || w.reloading) rideGunIdle = 0f; else rideGunIdle += dt;
            w.RideUp(rideGunIdle < 1.5f, speed / (bike != null ? bike.Def.boost : DesertBike.BoostSpeed));
            w.Tick(dt, false, 0f, rideGrounded, false, lookDelta);
            if (bike != null) bike.ShowRightArm(!w.RideGunUp);          // la destra e' sull'arma: il suo braccio sulla moto non si vede (nel suo posto c'e' quello dell'arma)
        }
    }
}

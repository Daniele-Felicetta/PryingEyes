using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>Una moto del deserto: un oggetto nel mondo che si monta con Usa (F) quando e' parcheggiato e che, mentre qualcuno la guida, si muove con lui. La guida vera (gas, freno, sterzo,
    /// collisioni, camera) sta in PlayerController.Ride.cs, perche' deve girare nell'aggiornamento del giocatore (ingressi del suo posto, corpo con il CharacterController); qui ci sono il modello,
    /// la posa (beccheggio dalle due ruote, rollio in curva, sterzo, rotazione delle ruote), il motore e il vento, i fari, la polvere e il collider da parcheggiata.
    /// Le moto dei compagni in cooperativa (RemotePlayer) sono la stessa cosa in 'puppet': la posa arriva dalla rete e nessuno le puo' montare.</summary>
    public sealed class DesertBike : MonoBehaviour, IInteract
    {
        public const float TopSpeed = 23f, BoostSpeed = 31f, ReverseSpeed = 5f;
        public static readonly List<DesertBike> All = new List<DesertBike>();

        // Solo per le prove (-autotest): comandi forzati al posto della tastiera (gas -1..1, sterzo -1..1, turbo, freno a mano)
        public static float botThrottle, botSteer;
        public static bool botBoost, botHandbrake;
        public static bool BotActive => botThrottle != 0f || botSteer != 0f || botBoost || botHandbrake;
        public static void BotReset() { botThrottle = botSteer = 0f; botBoost = botHandbrake = false; Weapon.rideBotFire = false; }

        /// <summary>Chi la guida (null: parcheggiata, o la moto di un compagno).</summary>
        [System.NonSerialized] public PlayerController rider;
        /// <summary>La moto di un compagno (cooperativa): la posa la da' la rete, nessuno la monta.</summary>
        [System.NonSerialized] public bool puppet;
        [System.NonSerialized] public Vector3 home;
        [System.NonSerialized] public float homeHeading;
        /// <summary>Di chi e' (il posto dello schermo condiviso, lo slot della cooperativa, 0 da soli): alla rinascita torna a casa quella del giocatore (DesertBikes.ReturnHome).</summary>
        [System.NonSerialized] public int slot;
        /// <summary>Quale moto e' (DesertBikeDefs): il modello, la livrea, il motore e i numeri della guida. La moto arrangiata di sempre se nessuno dice altro.</summary>
        public DesertBikeDef Def { get => def ?? (def = DesertBikeDefs.Starter); private set => def = value; }
        DesertBikeDef def;

        DesertBikeMesh.Rig rig;
        BoxCollider box;
        AudioSource engine, wind;
        float heading, spin, steerVis, revs, dustT, interactT, settleT, outT;
        float lastSpeed, lastThrottle, lastHs;
        bool lastBoost, lastBrake, lastGrounded = true, settled, lit;
        bool exteriorCamera;
        Vector3 lastVel;
        DesertTracks.Pen penRear, penFront;                 // i nastri di traccia delle due ruote (DesertTracks): si spezzano quando la moto si ferma, si stacca o si parcheggia

        /// <summary>Inclinazioni visive in gradi (beccheggio positivo = muso in basso, rollio positivo = a sinistra): la camera del pilota ne segue una parte.</summary>
        public float PitchVisual { get; private set; }
        public float RollVisual { get; private set; }
        public float Heading => heading;
        public bool InUse => rider != null || puppet;
        public Transform Body => rig != null ? rig.body : transform;
        /// <summary>L'assetto del telaio (direzione, rollio e beccheggio): il corpo del pilota visto da fuori lo segue.</summary>
        public Quaternion BodyRotation => Body.rotation;
        /// <summary>Il pugno di un lato sulla manopola, nel mondo (0 la sinistra, 1 la destra di chi guarda avanti; gira con lo sterzo): dove il corpo visto da fuori mette la mano.</summary>
        public Vector3 GripWorld(int side) => rig != null ? rig.steer.TransformPoint(rig.handPt[Mathf.Clamp(side, 0, 1)]) : transform.position + Vector3.up;
        public Vector3 EyeWorld => rig != null ? rig.body.TransformPoint(rig.eye) : transform.position + Vector3.up * 1.4f;
        /// <summary>La sella di questa moto per il corpo di chi la guida visto da fuori (SurvivorBody): dove sta il bacino, dove poggiano i piedi, quanto si china il busto. La moto arrangiata lascia i valori di sempre.</summary>
        public void FillSeat(ref SurvivorInput inp)
        {
            var d = Def;
            if (!d.Starter) { inp.seatHips = d.seatHips; inp.seatPegs = d.pegs; inp.seatLean = d.seatLean; }
            else { inp.seatHips = Vector3.zero; inp.seatPegs = Vector3.zero; inp.seatLean = 0f; }
        }
        public float WheelSpin => spin;
        public float SteerVisual => steerVis;
        public bool HeadlightOn => rig != null && rig.headlight != null && rig.headlight.enabled;
        /// <summary>Per le prove: posata sul terreno vero, e il collider da parcheggiata acceso.</summary>
        public bool Settled => settled;
        public bool ColliderOn => box != null && box.enabled;
        /// <summary>Il corpo di chi guida un'altra moto attraversa (on) o urta di nuovo (off) questa moto parcheggiata. Serve allo schermo condiviso: i garage dei posti stanno uno dietro l'altro (stessa x, una
        /// fila ogni 3,4 m verso sud) e la moto del posto davanti sbarrerebbe l'uscita a quello dietro. Dice se ha potuto (il collider c'e' e il controller e' acceso).</summary>
        public bool GhostFor(CharacterController other, bool on)
        {
            if (box == null || other == null || !other.enabled) return false;
            Physics.IgnoreCollision(box, other, on);
            return true;
        }
        /// <summary>Per le prove: si vedono le braccia del pilota sul manubrio (non in uno schermo condiviso).</summary>
        public bool ArmsShown => rig != null && rig.ArmsShown;
        /// <summary>Per le prove: si vede il braccio destro sul manubrio (non quando la destra impugna l'arma, ne' in uno schermo condiviso).</summary>
        public bool RightArmShown => rig != null && rig.glove[1] != null && rig.glove[1].gameObject.activeSelf;
        /// <summary>La destra lascia il manubrio per l'arma (o ci torna): il braccio destro della moto sparisce (o riappare).</summary>
        public void ShowRightArm(bool on) { if (rig != null) rig.ShowRightArm(on); }
        public void SetRiderCamera(bool exterior)
        {
            if (exteriorCamera == exterior) return;
            exteriorCamera = exterior;
            if (rig == null) return;
            rig.ShowArms(!exterior && !Seats.Split);
            rig.SetScreens(exterior);
        }
        public float EngineVolume => engine != null ? engine.volume : 0f;
        public float EnginePitch => engine != null ? engine.pitch : 0f;

        // ------------------------------------------------------------------ creazione
        /// <summary>Una moto parcheggiata a terra in 'feet', rivolta verso 'heading' (radianti, come il giocatore: 0 = +z). 'def': quale moto (null: quella arrangiata di sempre).</summary>
        public static DesertBike Create(Transform parent, Vector3 feet, float heading, string name = "Moto", DesertBikeDef def = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = feet;
            var b = go.AddComponent<DesertBike>();
            b.Def = def ?? DesertBikeDefs.Starter;
            b.home = feet; b.homeHeading = heading;
            b.Build();
            b.Park(feet, heading);
            return b;
        }

        void Build()
        {
            if (Def == null) Def = DesertBikeDefs.Starter;
            rig = Def.IsPlane ? DesertBikeMesh.BuildPlane(transform, Def)
                : Def.IsPista ? DesertBikeMesh.BuildPista(transform, Def) : DesertBikeMesh.Build(transform, false);
            box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, Def.IsPlane ? 0.85f : 0.55f, 0f);
            box.size = Def.IsPlane ? new Vector3(1.05f, 1.6f, 3.7f) : new Vector3(0.62f, 1.1f, 1.95f);
            engine = Voice(Def.Starter ? SoundBank.Engine : SoundBank.ForEngine(Def.engine), 3f, 90f, 1f);
            wind = Voice(SoundBank.SandHiss, 2f, 45f, 0.35f);
            engine.pitch = Def.Starter ? 0.62f : PistaPitch(0f);
        }

        /// <summary>Il suono di un motore della pista a quel regime (0 minimo, 1 linea rossa): il ciclo e' registrato a 4800 giri (la pista suona a rpm/4800).</summary>
        float PistaPitch(float revs01) => Mathf.Clamp(Mathf.Lerp(Def.idleRpm, Def.redlineRpm * 0.95f, revs01) / 4800f, 0.3f, 3.3f);

        AudioSource Voice(AudioClip clip, float minDist, float maxDist, float spatial)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.playOnAwake = false; s.volume = 0f;
            s.spatialBlend = spatial; s.minDistance = minDist; s.maxDistance = maxDist;
            s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = 0f;
            if (clip != null) { s.time = Random.value * clip.length * 0.9f; s.Play(); }
            return s;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); Interact.Add(this); }

        void OnDisable() { All.Remove(this); Interact.Remove(this); }

        void OnDestroy()
        {
            All.Remove(this);
            Interact.Remove(this);
            // i materiali fatti per questa moto (vernice, targa, fari delle moto della pista) non se ne vanno con l'oggetto
            if (rig != null) foreach (var m in rig.ownedMats) if (m != null) Destroy(m);
        }

        // ------------------------------------------------------------------ IInteract: salire
        public Vector3 Point => transform.position + Vector3.up * 0.8f;
        public float Radius => 3.1f;
        public string Prompt => rider == null && !puppet ? (Def.IsPlane ? "Sali sul Libeccio" : Def.Starter ? "Sali in moto" : "Sali sulla " + Def.name) : null;

        public void Use()
        {
            var seat = Seats.Current;
            var p = seat != null && seat.player != null ? seat.player : GameManager.I != null ? GameManager.I.player : null;
            if (p == null) return;
            p.Mount(this);
        }

        // ------------------------------------------------------------------ parcheggio e salita
        /// <summary>La moto resta li' in piedi sul cavalletto (appena inclinata a sinistra, lo sterzo girato), con il collider: nessuno ci cammina attraverso.</summary>
        public void Park(Vector3 feet, float heading)
        {
            rider = null;
            penRear?.Lift(); penFront?.Lift();
            this.heading = heading;
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, heading * Mathf.Rad2Deg, 0f));
            PitchVisual = 0f; RollVisual = 7f; steerVis = -12f; spin = Mathf.Repeat(spin, 360f);
            if (Def.IsPlane) RollVisual = steerVis = 0f;
            if (rig != null)
            {
                rig.body.localRotation = Quaternion.Euler(0f, 0f, RollVisual);
                rig.steer.localRotation = rig.steerRest * Quaternion.Euler(0f, steerVis, 0f);
                rig.ShowArms(false);
                rig.SetScreens(true);
            }
            if (box != null) box.enabled = true;
            settled = false; settleT = 0f;
            lastSpeed = lastThrottle = lastHs = 0f; lastBoost = lastBrake = false; lastVel = Vector3.zero;
        }

        /// <summary>La piazzola di questa moto: dove torna alla rinascita del suo giocatore (DesertBikes.ReturnHome) e se si perde nel vuoto.</summary>
        public void SetHome(Vector3 at, float heading) { home = at; homeHeading = heading; }

        /// <summary>Un pilota sale in sella: il collider si spegne (il suo corpo deve starci sopra) e si vedono le braccia, se la camera e' la sua.</summary>
        public void Take(PlayerController p)
        {
            rider = p;
            exteriorCamera = false;
            DesertBikes.NoteRide(this);
            penRear?.Lift(); penFront?.Lift();
            if (box != null) box.enabled = false;
            if (rig != null) { rig.ShowRightArm(true); rig.ShowArms(!Seats.Split); rig.SetScreens(false); }          // in uno schermo condiviso le braccia sulla moto le vedrebbero anche gli altri posti: li' il pilota e' il suo corpo (SeatAvatar). Il cupolino non c'e' per chi ci guarda attraverso
            lastGrounded = true;
        }

        /// <summary>Per la moto di un compagno: dalla rete la si accende o la si spegne (nascosta quando lui e' a piedi).</summary>
        public void SetPuppet(bool on)
        {
            puppet = on;
            if (box != null) box.enabled = false;
            if (rig != null) { rig.ShowArms(false); rig.SetScreens(true); }
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
        }

        // ------------------------------------------------------------------ la posa, a ogni fotogramma di chi guida
        static bool Ground(Vector3 p, out float y, out bool sand)
        {
            sand = false; y = p.y;
            if (!Physics.Raycast(p + Vector3.up * 0.8f, Vector3.down, out var h, 2.4f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return false;
            y = h.point.y;
            sand = DesertTerrain.IsSand(h.collider);
            return true;
        }

        /// <summary>Mette la moto dove e' il pilota e la inclina come il terreno: beccheggio da due raggi (uno per ruota), rollio in curva, sterzo, ruote che girano. Per il volo il muso segue la
        /// velocita'. 'steer' -1..1, 'throttle' 0..1 (i giri del motore), 'brake' accende il fanalino.</summary>
        public void Pose(Vector3 feet, float headingRad, bool grounded, Vector3 vel, float steer, float throttle, bool boost, bool brake, bool handbrake, float dt)
        {
            heading = headingRad;
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, headingRad * Mathf.Rad2Deg, 0f));
            Vector3 fwd = new Vector3(Mathf.Sin(headingRad), 0f, Mathf.Cos(headingRad));
            Vector3 hv = new Vector3(vel.x, 0f, vel.z);
            float hs = hv.magnitude, speed = Vector3.Dot(hv, fwd);
            bool sand = false;
            float pitchT;
            if (grounded)
            {
                float front = Def.IsPlane ? 0.66f : DesertBikeMesh.WheelBase * 0.5f;
                float rear = Def.IsPlane ? 2.55f : DesertBikeMesh.WheelBase * 0.5f;
                bool f = Ground(feet + fwd * front, out float yF, out sand);
                bool r = Ground(feet - fwd * rear, out float yR, out bool sandR);
                sand |= sandR;
                pitchT = f && r ? -Mathf.Atan2(yF - yR, front + rear) * Mathf.Rad2Deg : 0f;
            }
            else pitchT = Mathf.Clamp(-Mathf.Atan2(vel.y, Mathf.Max(hs, 2f)) * Mathf.Rad2Deg, -35f, 35f);
            PitchVisual += (pitchT - PitchVisual) * (1f - Mathf.Exp(-(grounded ? 14f : 3.5f) * dt));
            float leanLimit = Def.IsPlane ? (grounded ? 0f : 34f) : (handbrake ? 30f : 22f) * (grounded ? 1f : 0.3f);
            float rollT = -steer * Mathf.Clamp01(Mathf.Abs(speed) / 11f) * leanLimit;
            RollVisual += (rollT - RollVisual) * (1f - Mathf.Exp(-7f * dt));
            if (rig != null)
            {
                rig.body.localRotation = Quaternion.Euler(PitchVisual, 0f, RollVisual);
                steerVis += (steer * 28f - steerVis) * (1f - Mathf.Exp(-14f * dt));
                rig.steer.localRotation = rig.steerRest * Quaternion.Euler(0f, steerVis, 0f);
                if (Def.IsPlane)
                {
                    spin = Mathf.Repeat(spin + Mathf.Lerp(210f, 1320f, throttle) * dt, 360f);
                    rig.propeller.localRotation = Quaternion.Euler(0f, 0f, spin);
                }
                else
                {
                    spin = Mathf.Repeat(spin + speed / DesertBikeMesh.WheelRadius * Mathf.Rad2Deg * dt, 360f);
                    var wheel = Quaternion.Euler(spin, 0f, 0f);
                    rig.rearPivot.localRotation = wheel;
                    rig.frontPivot.localRotation = wheel;
                }
                rig.PoseArms();
            }
            // le tracce delle ruote sulla sabbia (DesertTracks): un nastro per ruota; si interrompe quando la moto e' in aria o ferma. Chi lo allunga decide da solo se sotto la ruota c'e' sabbia.
            if (!Def.IsPlane && grounded && hs > 1.2f)
            {
                float kTrack = Mathf.Clamp01(hs / Def.top);
                float deepTrack = Mathf.Clamp01(0.35f + 0.35f * kTrack + (handbrake ? 0.35f : 0f) + (boost ? 0.15f : 0f));
                Vector3 halfBase = fwd * (DesertBikeMesh.WheelBase * 0.5f);
                DesertTracks.Lay(penRear ?? (penRear = new DesertTracks.Pen(0.15f)), feet - halfBase, deepTrack);
                DesertTracks.Lay(penFront ?? (penFront = new DesertTracks.Pen(0.12f)), feet + halfBase, deepTrack * 0.8f);
            }
            else { penRear?.Lift(); penFront?.Lift(); }
            // la polvere che la ruota di dietro tira su sulla sabbia
            if (grounded && sand && hs > 3.2f)
            {
                dustT -= dt;
                if (dustT <= 0f)
                {
                    dustT = Mathf.Lerp(0.085f, 0.024f, Mathf.Clamp01(hs / 24f));
                    Vector3 at = feet - fwd * (DesertBikeMesh.WheelBase * 0.5f) + Vector3.up * 0.07f;
                    float k = Mathf.Clamp01(hs / 24f);
                    int n = (boost || handbrake) ? 2 : 1;
                    Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                    for (int i = 0; i < n; i++)
                    {
                        var v = -fwd * (hs * 0.10f) + right * Random.Range(-0.9f, 0.9f) + Vector3.up * Random.Range(0.35f, 1.15f) * (0.6f + k);
                        Fx.Particle(0, false, at + right * Random.Range(-0.1f, 0.1f), v, Vector3.zero, 0.14f, 0.55f + 0.6f * k, 0.9f + 0.7f * k, DustTint(), 0.22f);
                    }
                }
            }
            lastSpeed = speed; lastThrottle = throttle; lastHs = hs; lastBoost = boost; lastBrake = brake; lastGrounded = grounded; lastVel = vel;
        }

        static Color DustTint()
        {
            var sky = DesertSky.I;
            float day = sky != null ? Mathf.Lerp(0.16f, 1f, sky.State.daylight) * (1f - 0.5f * sky.Night) : 1f;
            return new Color(0.78f * day, 0.62f * day, 0.42f * day);
        }

        /// <summary>Un colpo: un urto contro un muro o l'atterraggio duro dopo un salto. 'strength' 0..1: il tonfo e una nuvola di sabbia.</summary>
        public void Impact(float strength)
        {
            float k = Mathf.Clamp01(strength);
            Sfx.At(SoundBank.BikeCrash, transform.position + Vector3.up * 0.4f, Mathf.Lerp(0.3f, 1f, k), Random.Range(0.92f, 1.08f), 3f, 80f, 1f);
            int n = 4 + Mathf.RoundToInt(10f * k);
            Vector3 at = transform.position + Vector3.up * 0.1f;
            for (int i = 0; i < n; i++)
            {
                var v = new Vector3(Random.Range(-1f, 1f), Random.Range(0.4f, 1.6f), Random.Range(-1f, 1f)) * (1.2f + 2.4f * k);
                Fx.Particle(0, false, at, v, Vector3.zero, 0.2f, 0.9f + 0.7f * k, 1.0f + 0.6f * k, DustTint(), 0.25f);
            }
        }

        // ------------------------------------------------------------------ ogni fotogramma: motore, vento, luci, parcheggio
        void Update()
        {
            float dt = Time.deltaTime;
            bool use = InUse;
            UpdateAudio(dt, use);
            UpdateLights(dt, use);
            if (use) return;
            // parcheggiata: si ricorda a chi guarda (Interact.Clear la dimentica a certi cambi di scena) e si posa sul terreno vero appena c'e'
            interactT -= dt;
            if (interactT <= 0f) { interactT = 1f; Interact.Add(this); }
            if (!settled)
            {
                settleT -= dt;
                if (settleT <= 0f)
                {
                    settleT = 0.4f;
                    box.enabled = false;           // il raggio non deve trovare la moto stessa
                    bool found = Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, out var h, 6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
                    box.enabled = true;
                    if (found && Mathf.Abs(h.point.y - transform.position.y) < 1.2f)         // il tetto di un edificio, o un ramo, non e' il suolo
                    {
                        transform.position = new Vector3(transform.position.x, h.point.y, transform.position.z);
                        settled = true;
                    }
                }
            }
            // persa nel vuoto (il terreno non c'era): torna dov'era nata
            if (transform.position.y < DesertWorld.GroundY - 60f) Park(home, homeHeading);
        }

        void UpdateAudio(float dt, bool use)
        {
            if (GameMenu.Blocked && !Coop.Active) use = false;            // il menu di pausa zittisce il motore
            float v = Mathf.Abs(lastSpeed) * (TopSpeed / Def.top);          // le marce si contano sulla velocita' massima della moto: una piu' veloce sale piu' in alto prima del cambio
            float revsT;
            if (!use) revsT = 0f;
            else if (Def.IsPlane) revsT = Mathf.Lerp(0.16f, 0.95f, lastThrottle);
            else
            {
                // quattro marce: i giri salgono nella marcia e ricadono al cambio
                float lo, hi;
                if (v < 5.5f) { lo = 0f; hi = 5.5f; } else if (v < 11f) { lo = 5.5f; hi = 11f; } else if (v < 17f) { lo = 11f; hi = 17f; } else { lo = 17f; hi = 31f; }
                float inGear = Mathf.Clamp01((v - lo) / (hi - lo));
                revsT = Mathf.Lerp(0.22f, 0.92f, inGear);
                if (lastThrottle < 0.05f) revsT -= 0.12f;                  // fuori dal gas il motore frena
                if (v < 0.4f) revsT = 0.12f + 0.55f * lastThrottle;        // al minimo, con un colpo di gas
                if (!lastGrounded) revsT = Mathf.Max(revsT, 0.85f);        // in aria gira a vuoto
            }
            revs = Mathf.MoveTowards(revs, revsT, (revsT > revs ? 2.6f : 1.7f) * dt);
            float volT = use ? Mathf.Lerp(0.20f, 0.85f, Mathf.Clamp01(revs * 0.65f + lastThrottle * 0.35f)) : 0f;
            if (engine != null)
            {
                engine.pitch = Def.Starter ? Mathf.Lerp(0.62f, 2.3f, revs) : PistaPitch(revs);
                engine.volume = Mathf.Lerp(engine.volume, volT * GameSettings.Effects, 1f - Mathf.Exp(-(use ? 10f : 5f) * dt));
            }
            if (wind != null)
            {
                float vw = use ? Mathf.Pow(Mathf.Clamp01((lastHs - 5f) / 24f), 1.3f) * 0.6f : 0f;
                wind.pitch = 0.8f + 0.55f * Mathf.Clamp01(lastHs / 30f);
                wind.volume = Mathf.Lerp(wind.volume, vw * GameSettings.Effects, 1f - Mathf.Exp(-6f * dt));
            }
        }

        void UpdateLights(float dt, bool use)
        {
            if (rig == null || rig.headlight == null) return;
            var sky = DesertSky.I;
            bool dark = sky != null && (sky.Night > 0.28f || sky.Indoor > 0.5f);
            bool on = use && dark;
            if (rig.headlight.enabled != on) rig.headlight.enabled = on;
            if (on != lit)
            {
                lit = on;
                rig.lensMat.SetColor("_EmissionColor", new Color(1f, 0.93f, 0.78f) * (on ? 2.4f : 0.5f));
            }
            outT -= dt;
            if (outT <= 0f)
            {
                outT = 0.1f;
                float k = use ? (lastBrake ? 2.6f : dark ? 1.3f : 0.7f) : 0.12f;
                rig.tailMat.SetColor("_EmissionColor", new Color(1f, 0.08f, 0.04f) * k);
                DesertBikeMesh.SetGlassLight(sky != null ? (1f - sky.Night) * (1f - 0.6f * sky.Indoor) : 1f);
            }
        }
    }
}

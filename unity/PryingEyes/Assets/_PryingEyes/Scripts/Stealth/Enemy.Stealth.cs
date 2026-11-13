using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes
{
    /// <summary>Percezione dei preti (monaco, abate, occhio). Ognuno pattuglia i piani e ti nota in due modi:
    /// con la vista (cono davanti a se', linea libera, e a una distanza che dipende da quanto sei illuminato, dalla torcia e
    /// da come ti muovi: vedi Stealth.SightRange) e con l'udito (passi, spari, ping, candele: Noise.Emit; i muri attutiscono).
    /// Man mano che ti nota sale la consapevolezza (awareness, 0..1): da 0,32 e' sospettoso e va a controllare, a 1 ti caccia.
    /// Se ti perde per qualche secondo va nell'ultimo punto in cui ti ha visto, si guarda intorno e poi torna a pattugliare.
    /// Nella hall classica (niente piani) restano onniscienti come prima.</summary>
    public partial class Enemy
    {
        public Alert alert = Alert.Patrol;
        public float awareness;
        public bool IsDead => dead;
        public bool Seeing => seeing;
        public Vector3 LastKnown => lastKnown;
        /// <summary>Solo per le prove: i nemici restano fermi (la percezione gira lo stesso).</summary>
        public static bool debugFreeze;
        /// <summary>Solo per le prove: la percezione e' spenta e lo stato impostato a mano resta com'e' (foto dei segnalini).</summary>
        public static bool debugNoSense;
        /// <summary>Punti di pattuglia del piano (centri delle stanze e qualche punto in piu' nelle grandi), messi dal FloorDirector.</summary>
        public static readonly List<Vector3> PatrolPoints = new List<Vector3>();

        bool seeing, seenCache, hasGoal, scanning, arrivedScan, firstScan;
        float visGain, senseT, alertT, lostT, turnT, waitT, scanT, searchLeft, scanYaw, scanBase, scanClock, stuckT;
        Vector3 lastKnown, stimulus, goal, moveGoal, pathDest;
        bool moveHold;
        float moveMul = 1f;

        /// <summary>Onnisciente: nella hall classica, e per chi nasce in un round (Enemy.Rounds.cs): sa sempre dove sei e ti viene incontro.</summary>
        bool Omniscient => !Stealth.Enabled || relentless;

        /// <summary>Dove sta il segnalino sopra la testa.</summary>
        public Vector3 MarkerPoint => transform.position + Vector3.up * (type == EnemyType.Portachiavi ? 1.65f : Archetype == EnemyType.Occhio ? 0.85f : Archetype == EnemyType.Abate ? 3.0f : type == EnemyType.Direttore ? 2.95f : 2.3f);

        float EyeHeight => type == EnemyType.Portachiavi ? 0.9f : Archetype == EnemyType.Occhio ? 0f : Archetype == EnemyType.Abate ? 2.0f : type == EnemyType.Direttore ? 1.9f : 1.55f;
        float FovHalf => Archetype == EnemyType.Occhio ? 90f : Archetype == EnemyType.Abate ? 58f : 64f;
        float LoseTime => Archetype == EnemyType.Occhio ? 3.0f : 4.5f;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static float YawTo(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

        // ------------------------------------------------------------------ comandi dall'esterno
        /// <summary>Dimentica il giocatore (rinascita): torna a pattugliare.</summary>
        public void Forget()
        {
            awareness = 0f; seeing = false; seenCache = false; lostT = 0f; hasGoal = false;
            if (alert != Alert.Patrol) SetAlert(Alert.Patrol);
        }

        public static void ForgetAll()
        {
            foreach (var e in all) if (e != null) e.Forget();
        }

        /// <summary>Chi cercava proprio quel giocatore (rinato) perde la pista; gli altri continuano. Da solo e' ForgetAll.</summary>
        public static void ForgetOf(ITarget t)
        {
            foreach (var e in all) if (e != null && !e.puppet && (e.tgt == null || e.tgt == t)) e.Forget();
        }

        /// <summary>Va a controllare un punto (candela accesa, uscita): cerca come se avesse perso una pista.</summary>
        public void Investigate(Vector3 pos)
        {
            if (dead || puppet || Omniscient) return;
            lastKnown = pos;
            awareness = Mathf.Max(awareness, 0.6f);
            if (alert == Alert.Hunting) return;
            if (alert != Alert.Searching) SetAlert(Alert.Searching);
            else { SetGoalNear(pos); searchLeft = 14f; arrivedScan = false; firstScan = true; }
        }

        /// <summary>Solo per le prove.</summary>
        public void DebugSetAlert(Alert a) { awareness = a == Alert.Hunting ? 1f : a == Alert.Patrol ? 0f : 0.6f; SetAlert(a); }

        // ------------------------------------------------------------------ udito
        public void Hear(Vector3 pos, float radius, NoiseKind kind)
        {
            if (dead || puppet || Omniscient) return;
            Vector3 ep = transform.position + Vector3.up * (type == EnemyType.Portachiavi ? EyeHeight : Archetype == EnemyType.Occhio ? 0f : 1.4f);
            float d = Vector3.Distance(ep, pos);
            float eff = radius * (Archetype == EnemyType.Occhio ? 0.55f : Archetype == EnemyType.Abate ? 1.2f : 1f);   // l'occhio non ha orecchie
            if (d > eff) return;
            if (!HotelNav.ClearLine(ep, pos + Vector3.up * 0.4f))
            {
                eff *= 0.5f;                                        // i muri attutiscono
                if (d > eff) return;
            }
            float loud = 1f - d / Mathf.Max(eff, 0.5f);
            float err = Mathf.Lerp(0.5f, 3.5f, Mathf.Clamp01(d / Mathf.Max(eff, 1f)));
            Vector3 where = pos + new Vector3(Random.Range(-err, err), 0f, Random.Range(-err, err));
            switch (kind)
            {
                case NoiseKind.Gunshot:
                case NoiseKind.Ping:
                case NoiseKind.Candle:
                    if (alert == Alert.Hunting) { if (!seeing) lastKnown = where; break; }
                    lastKnown = where;
                    awareness = Mathf.Max(awareness, 0.6f);
                    if (alert != Alert.Searching) SetAlert(Alert.Searching);
                    else { SetGoalNear(where); searchLeft = 14f; arrivedScan = false; firstScan = true; }
                    break;
                case NoiseKind.Alarm:                               // il fischietto dell'usciere: chi non caccia gia' corre sul posto
                    if (alert == Alert.Hunting) break;
                    lastKnown = where;
                    awareness = Mathf.Max(awareness, 0.6f);
                    if (alert != Alert.Searching) SetAlert(Alert.Searching);
                    else { SetGoalNear(where); searchLeft = 14f; arrivedScan = false; firstScan = true; }
                    break;
                case NoiseKind.Shout:
                    if (alert == Alert.Hunting) break;
                    stimulus = where;
                    awareness = Mathf.Max(awareness, 0.4f);
                    if (alert == Alert.Patrol) SetAlert(Alert.Suspicious);
                    break;
                default:                                           // passi e il resto: si sommano finche' non insospettiscono
                    if (alert == Alert.Hunting) { if (!seeing) lastKnown = where; break; }
                    awareness = Mathf.Min(0.95f, awareness + 0.1f + 0.28f * loud);
                    stimulus = where;
                    if (alert == Alert.Patrol && awareness >= 0.32f) SetAlert(Alert.Suspicious);
                    else if (alert == Alert.Suspicious) arrivedScan = false;
                    else if (alert == Alert.Searching) SetGoalNear(where);
                    break;
            }
        }

        // ------------------------------------------------------------------ vista
        /// <summary>Vede il giocatore adesso? Entro la portata del momento, nel cono davanti a se' (piu' largo se e' gia'
        /// in allerta) e con la linea libera verso la testa o il petto. A due passi ti vede comunque, al buio come alla luce.
        /// 'gain' e' la velocita' con cui sale la consapevolezza (al secondo): poco al limite della portata, tanto da vicino.</summary>
        bool EvalVision(Vector3 gp, ITarget pl, out float gain)
        {
            gain = 0f;
            Vector3 eyeP = gp + Vector3.up * EyeHeight;
            Vector3 head = pl.LeanedPoint(1.58f), chest = pl.LeanedPoint(1.2f);
            Vector3 to = head - eyeP;
            float d = to.magnitude;
            bool hunting = alert == Alert.Hunting;
            float range = pl.SightRange(this) * (hunting ? 1.35f : 1f);
            Vector3 fwd = transform.forward, to2 = to;
            if (Archetype != EnemyType.Occhio) { fwd.y = 0f; to2.y = 0f; }
            // a due passi ti nota comunque; ma chi cammina non ti sente alle spalle oltre il metro: senza questo la presa col
            // coltello (Knife.cs) sarebbe impossibile, ti noterebbe prima che tu sia a portata
            float nearR = !hunting && Archetype != EnemyType.Occhio && Vector3.Angle(fwd, to2) > 105f ? 1.0f : 2.2f;
            bool near = d < nearR;
            if (d > range && !near) return false;
            float half = FovHalf * (alert == Alert.Patrol ? 1f : 1.5f);
            if (!near && Vector3.Angle(fwd, to2) > half) return false;
            if (!(HotelNav.ClearLine(eyeP, head) || HotelNav.ClearLine(eyeP, chest))) return false;
            float k = 1f - Mathf.Clamp01(d / Mathf.Max(range, 0.01f));
            gain = 0.26f + 1.5f * k;
            if (near) gain = Mathf.Max(gain, 3f);
            // il fascio della torcia dritto in faccia lo allerta in fretta
            var bt = pl.BeamT;
            if (bt != null && d < 16f)
            {
                if (Vector3.Angle(bt.forward, eyeP - bt.position) < 24f) gain *= 1.7f;
            }
            return true;
        }

        /// <summary>Guarda tutti i giocatori vivi e dice se ne vede almeno uno; se ne vede piu' d'uno resta su quello che seguiva, altrimenti passa al
        /// piu' visibile. Da solo c'e' un solo giocatore e il risultato e' quello di sempre.</summary>
        bool EvalVisionAll(Vector3 gp, out float gain)
        {
            gain = 0f;
            ITarget best = null;
            bool curSeen = false;
            for (int i = 0, n = Targets.Count; i < n; i++)
            {
                var t = Targets.At(i);
                if (!t.Alive || !EvalVision(gp, t, out float g)) continue;
                if (t == tgt) curSeen = true;
                if (g > gain || best == null) { gain = g; best = t; }
            }
            if (best == null) return false;
            if (!curSeen) SwitchTarget(best);
            return true;
        }

        /// <summary>Cambia bersaglio: l'occhio fissa la visuale del nuovo.</summary>
        void SwitchTarget(ITarget t)
        {
            if (t == null || t == tgt) return;
            tgt = t;
            if (eye != null && eye.lookAt != null) eye.lookAt = t.LookAt;
        }

        void SenseTick(float dt, Vector3 gp)
        {
            if (Omniscient)
            {
                alert = Alert.Hunting; seeing = true; awareness = 1f; lostT = 0f;
                lastKnown = tgt.Feet;
                return;
            }
            if (debugNoSense) { seeing = false; return; }
            if (!Targets.AnyAlive)
            {
                // nessun giocatore e' vivo: tutti perdono la pista
                seeing = false; seenCache = false;
                if (alert != Alert.Patrol || awareness > 0f) Forget();
                return;
            }
            senseT -= dt;
            if (senseT <= 0f)
            {
                senseT = 0.1f + Random.value * 0.05f;
                seenCache = EvalVisionAll(gp, out visGain);
            }
            seeing = seenCache;
            if (seeing)
            {
                lastKnown = tgt.Feet;
                lostT = 0f;
                awareness = Mathf.Min(1f, awareness + visGain * dt);
            }
            else
            {
                lostT += dt;
                if (alert != Alert.Hunting) awareness = Mathf.Max(0f, awareness - 0.16f * dt);
            }
            switch (alert)
            {
                case Alert.Patrol:
                    if (awareness >= 1f) SetAlert(Alert.Hunting);
                    else if (awareness >= 0.32f) { if (seeing) stimulus = lastKnown; SetAlert(Alert.Suspicious); }
                    break;
                case Alert.Suspicious:
                    if (awareness >= 1f) SetAlert(Alert.Hunting);
                    else if (seeing) { stimulus = lastKnown; arrivedScan = false; }
                    break;
                case Alert.Searching:
                    if (awareness >= 1f && seeing) SetAlert(Alert.Hunting);
                    break;
                case Alert.Hunting:
                    if (lostT > LoseTime) { awareness = 0.55f; SetAlert(Alert.Searching); }
                    break;
            }
        }

        void SetAlert(Alert a)
        {
            if (a == alert) return;
            var old = alert;
            alert = a;
            alertT = 0f;
            arrivedScan = false;
            Vector3 p = transform.position + Vector3.up * 1.6f;
            switch (a)
            {
                case Alert.Suspicious:
                    turnT = 0.9f;
                    Sfx.At(SoundBank.Suspect, p, 0.55f, basePitch, 2f, 26f);
                    break;
                case Alert.Hunting:
                    if (old != Alert.Hunting) Stealth.Spotted++;      // per il bonus Fantasma (FloorDirector.OnCandle)
                    Sfx.At(SoundBank.AlertSting, p, 0.9f, basePitch * (Archetype == EnemyType.Abate ? 0.8f : 1f), 3f, 48f);
                    if (old != Alert.Hunting && seeing && tgt is LocalTarget) Sfx.UI(SoundBank.AlertSting, 0.2f, 1.5f);   // il tuo cuore: sei stato visto
                    if (old != Alert.Hunting) Noise.Emit(transform.position, 15f, NoiseKind.Shout);        // il grido richiama gli altri
                    lostT = 0f;
                    if (old != Alert.Hunting)
                    {
                        if (type == EnemyType.Maschera) Whistle();        // l'usciere dà l'allarme
                        else if (type == EnemyType.Primario) CallHelp();  // il primario chiama gli infermieri
                    }
                    if (eye != null) eye.Stare(1.0f);
                    break;
                case Alert.Searching:
                    searchLeft = 14f; firstScan = true;
                    SetGoalNear(lastKnown);
                    break;
                case Alert.Patrol:
                    hasGoal = false; waitT = Random.Range(1f, 3f); awareness = 0f;
                    break;
            }
            if (eye != null)
            {
                eye.lookAt = a == Alert.Patrol || a == Alert.Searching ? null : (tgt ?? Targets.Local).LookAt;
                eye.glowBoost = a == Alert.Hunting ? 1.9f : a == Alert.Suspicious ? 1.35f : 0.85f;
            }
        }

        void SetGoalNear(Vector3 p)
        {
            goal = NavMesh.SamplePosition(p, out NavMeshHit h, 3f, NavMesh.AllAreas) ? h.position : p;
        }

        // ------------------------------------------------------------------ dove andare
        Vector3 PickPatrolGoal(Vector3 gp)
        {
            if (hasHome) return PickHomeGoal(gp);           // una guardia gira nella sua stanza (pianta a griglia)
            var pts = PatrolPoints;
            if (pts.Count > 0)
                for (int tries = 0; tries < 8; tries++)
                {
                    Vector3 p = pts[Random.Range(0, pts.Count)];
                    if (Flat(p - gp).magnitude < 6f) continue;
                    if (NavMesh.SamplePosition(p, out NavMeshHit h, 2.5f, NavMesh.AllAreas)) return h.position;
                }
            return gp + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 6f;
        }

        Vector3 RandomNear(Vector3 c, float r)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector2 o = Random.insideUnitCircle * r;
                if (NavMesh.SamplePosition(c + new Vector3(o.x, 0f, o.y), out NavMeshHit h, 2.5f, NavMesh.AllAreas)) return h.position;
            }
            return c;
        }

        bool Reached(Vector3 gp, Vector3 target) =>
            Flat(target - gp).magnitude < (agent != null ? agent.stoppingDistance + 0.6f : 1.6f);

        void Sweep(float dt, float amp, float rate)
        {
            scanClock += dt * rate;
            scanning = true;
            scanYaw = scanBase + Mathf.Sin(scanClock) * amp;
        }

        /// <summary>Decide dove va, con che fretta e se si ferma a guardarsi intorno. Lo leggono UpdateMonaco/Abate/Occhio.</summary>
        void PlanMove(float dt, Vector3 gp, Vector3 feet)
        {
            moveMul = 1f; moveHold = false; scanning = false;
            moveGoal = gp;
            if (debugFreeze) { moveHold = true; return; }
            if (Omniscient) { moveGoal = feet; return; }
            alertT += dt;
            if (turnT > 0f) turnT -= dt;
            switch (alert)
            {
                case Alert.Hunting:
                {
                    bool close = (feet - gp).sqrMagnitude < 36f;
                    moveGoal = seeing || close ? feet : lastKnown;
                    moveMul = Archetype == EnemyType.Occhio ? 1.45f : Archetype == EnemyType.Abate ? 1.7f : 1.9f;
                    if (seeing) arrivedScan = false;
                    else if (!close && Flat(moveGoal - gp).magnitude < 1.6f)
                    {
                        // e' arrivato dove ti aveva visto l'ultima volta e non ti vede: si ferma e si guarda intorno
                        if (!arrivedScan) { arrivedScan = true; scanBase = transform.eulerAngles.y; }
                        moveHold = true;
                        Sweep(dt, 90f, 1.6f);
                    }
                    break;
                }
                case Alert.Suspicious:
                {
                    if (turnT > 0f) { moveHold = true; scanning = true; scanYaw = YawTo(stimulus - gp); break; }
                    moveMul = 0.85f;
                    moveGoal = stimulus;
                    if (arrivedScan)
                    {
                        scanT -= dt; moveHold = true; Sweep(dt, 70f, 1.0f);
                        if (scanT <= 0f) { awareness = 0f; SetAlert(Alert.Patrol); }
                    }
                    else if (Reached(gp, stimulus) || alertT > 25f) { arrivedScan = true; scanT = 3.5f; scanBase = transform.eulerAngles.y; }
                    break;
                }
                case Alert.Searching:
                {
                    searchLeft -= dt;
                    if (searchLeft <= 0f) { awareness = 0f; SetAlert(Alert.Patrol); break; }
                    moveMul = 1.05f;
                    moveGoal = goal;
                    if (arrivedScan)
                    {
                        scanT -= dt; moveHold = true; Sweep(dt, 150f, 1.3f);
                        if (scanT <= 0f) { arrivedScan = false; goal = RandomNear(lastKnown, 11f); }
                    }
                    else if (Reached(gp, goal)) { arrivedScan = true; scanT = firstScan ? 5f : 2.2f; firstScan = false; scanBase = transform.eulerAngles.y; }
                    break;
                }
                default:
                {
                    moveMul = 0.55f;
                    if (waitT > 0f)
                    {
                        waitT -= dt; moveHold = true; Sweep(dt, 60f, 0.8f);
                        if (waitT <= 0f) hasGoal = false;
                    }
                    else
                    {
                        if (!hasGoal) { goal = PickPatrolGoal(gp); hasGoal = true; stuckT = 0f; }
                        moveGoal = goal;
                        stuckT += dt;
                        if (Reached(gp, goal) || stuckT > 40f) { waitT = Random.Range(2.5f, 6f); scanBase = transform.eulerAngles.y; hasGoal = false; }
                    }
                    break;
                }
            }
        }

        /// <summary>Guarda dove cammina, o dove sta scrutando; mai verso il giocatore finche' non lo ha notato.</summary>
        void FaceCalm(float dt, float turn)
        {
            Vector3 hv = new Vector3(vel.x, 0f, vel.z);
            Vector3 look;
            if (scanning) look = Quaternion.Euler(0f, scanYaw, 0f) * Vector3.forward;
            else if (hv.sqrMagnitude > 0.04f) look = hv;
            else look = transform.forward;
            look.y = 0f;
            if (look.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Mathf.Min(1f, turn * dt));
        }
    }
}

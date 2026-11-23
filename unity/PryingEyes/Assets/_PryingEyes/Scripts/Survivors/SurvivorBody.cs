using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Quello che il corpo di un sopravvissuto deve sapere a ogni fotogramma (lo riempie chi lo possiede: RemotePlayer dalla
    /// rete, SeatAvatar dal giocatore del posto).</summary>
    public struct SurvivorInput
    {
        public Vector3 velocity;            // m/s nel mondo
        public float pitch;                 // radianti, positivo = guarda in basso (come PlayerController)
        public float lean;                  // -1 sinistra .. +1 destra
        public bool crouch, sprint, alive, downed, grounded;
        public int weapon;                  // indice in WeaponDefs.All, -1 = niente in mano
        public float shotAge;               // secondi dall'ultimo sparo
        public float ads;                   // 0..1 in mira
        public bool reloading;
        public float climb;                 // 0..1 mentre si arrampica
        public bool seated;                 // in sella a una moto: bacino sulla sella, piedi sulle pedane, mani sulle manopole (la radice del corpo sta sulla moto, girata come il suo telaio)
        public Vector3 gripLeft, gripRight; // nel mondo: le manopole, a sinistra e a destra di chi guarda avanti (valgono se seated)
        // la sella della moto (valgono se seated; a zero quella di sempre, la moto arrangiata): dove sta il bacino e dove poggiano i piedi (nel sistema della moto, x e' il lato) e quanto si china il busto (gradi)
        public Vector3 seatHips, seatPegs;
        public float seatLean;

        public static SurvivorInput Idle => new SurvivorInput { alive = true, grounded = true, weapon = -1, shotAge = 99f };
    }

    /// <summary>Strati di luce dei corpi: il bit 2 (come arma e mani: le luci del mondo, 3u, li illuminano) piu' un bit per posto
    /// (4u &lt;&lt; posto). La torcia del posto N non include il suo bit: non brucia la propria arma e le proprie mani da pochi
    /// centimetri, ma illumina i corpi degli altri posti.</summary>
    public static class SurvivorLight
    {
        public const int Slots = 4;
        public static uint BodyMask(int slot) => 2u | (4u << Mathf.Clamp(slot, 0, Slots - 1));
        public static uint TorchMask(int slot)
        {
            uint m = 1u;
            for (int s = 0; s < Slots; s++) if (s != slot) m |= 4u << s;
            return m;
        }
        /// <summary>La luce di riempimento della terza persona: solo il bit del posto, cioe' solo quel corpo (non il mondo, non le mani della prima persona).</summary>
        public static uint FillMask(int slot) => 4u << Mathf.Clamp(slot, 0, Slots - 1);
    }

    /// <summary>Il corpo di un sopravvissuto (modelli/personaggi, Resources/Survivors/&lt;id&gt;): scheletro animato interamente da
    /// codice. Le pose si calcolano come rotazioni rispetto alla posa di riposo nel riferimento della radice, quindi non dipendono dagli
    /// assi delle ossa dell'FBX. Gambe e braccia sono a due ossa (IK analitica con polo), il passo e' un ciclo procedurale con i piedi
    /// sul terreno, il busto segue la mira e l'arma (lo stesso prefab dell'arma in prima persona) sta in mano con la presa modellata
    /// per quell'arma (GripPoses: la stessa che usa la mano in prima persona). Chi lo possiede mette la radice (ai piedi, girata come
    /// il corpo) e riempie 'input'; il resto lo fa LateUpdate (o Step, per le prove).</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class SurvivorBody : MonoBehaviour
    {
        public static readonly string[] Ids = { "custode", "dottoressa", "reporter", "cacciatrice" };
        public static string IdOf(int slot) => Ids[((slot % Ids.Length) + Ids.Length) % Ids.Length];

        public SurvivorInput input = SurvivorInput.Idle;
        public bool autoStep = true;
        public float debugPhase = -1f;       // prove: blocca il ciclo del passo

        public int Slot { get; private set; }
        public string Id { get; private set; }
        public Transform Hips => hips;
        public Transform Head => head;
        public Transform RightHand => handR;
        public Transform LeftHand => handL;
        public Transform Model => model;
        /// <summary>Un punto fisso tra gli occhi (figlio della testa): dove guarda e da dove vede.</summary>
        public Transform Eye => eyeT;
        public Transform Muzzle => weaponRig != null && weaponRig.muzzle != null ? weaponRig.muzzle : handR;
        public Transform WeaponRoot => weaponGo != null ? weaponGo.transform : null;
        public bool HasWeapon => weaponGo != null;
        public Vector3 EyeWorld => head != null ? head.TransformPoint(eyeLocal) : transform.position + Vector3.up * 1.6f;
        /// <summary>Per le prove: quanto la posa in sella e' completa (0 a piedi, 1 seduto) e di quanto le due mani (somma) mancano le manopole, in metri: l'IK non arriva se il braccio e' troppo corto.</summary>
        public float SeatAmount => seatAmt;
        public float RideHandsOff { get; private set; }

        Transform model, hips, spine, chest, uchest, neck, head, eyeT;
        Transform thighL, shinL, footL, thighR, shinR, footR;
        Transform uarmL, larmL, handL, uarmR, larmR, handR;
        readonly Transform[] fingL = new Transform[15], fingR = new Transform[15];      // pollice, indice, medio, anulare, mignolo x 3 falangi
        Vector3[] fingDirL, fingDirR;        // direzione di ogni falange a riposo, nel riferimento locale dell'osso
        struct Rest { public Vector3 lpos; public Quaternion lrot; public Quaternion rs; public Vector3 prs; }
        readonly Dictionary<Transform, Rest> rest = new Dictionary<Transform, Rest>();
        Transform[] bones;
        Vector3 modelRestPos; Quaternion modelRestRot;
        Vector3 eyeLocal;
        float legL1, legL2, armL1, armL2;
        float xLeft = -1f;                   // la x (nella radice) del lato sinistro: -1 se la sinistra sta a -x
        Quaternion frameRsL, frameRsR;       // sistema della mano a riposo (dita avanti, dorso su), nella radice
        Vector3 curlAxisL, curlAxisR;        // asse attorno a cui le dita si chiudono (radice)

        // stato liscio
        Vector3 velLocal;
        float phase, gait, crouchAmt, sprintAmt, adsAmt, leanAmt, airAmt, hipDrop, pitchSmooth, reloadT;
        float lieAmt, downAmt;               // 0..1: disteso (morto), seduto a terra (ferito)
        float seatAmt;                       // 0..1: in sella a una moto
        // in sella (sistema della radice = quello della moto: avanti +z, la sella a 0,885 m e z -0,28, le pedane a x 0,20, y 0,36): il bacino sopra la sella, il busto molto in avanti (le manopole
        // stanno a 0,45 m davanti: con meno busto il braccio non ci arriva), le caviglie sulle pedane
        static readonly Vector3 SeatHips = new Vector3(0f, 1.06f, -0.24f);
        const float SeatLean = 52f, PegX = 0.20f, PegY = 0.45f, PegZ = -0.15f;
        readonly float[] footY = new float[2];
        readonly Vector3[] footN = { Vector3.up, Vector3.up };
        readonly Vector3[] legTarget = new Vector3[2];
        readonly float[] legOff = new float[2], legLift = new float[2];

        // arma
        GameObject weaponGo;
        WeaponRig weaponRig;
        WeaponDef weaponDef;
        int weaponIndex = -2;
        Grips grips = Grips.None;

        sealed class Grips
        {
            public static readonly Grips None = new Grips();
            public bool hasR, hasL;
            public Vector3 wristR = new Vector3(0f, -0.04f, -0.02f), wristL = new Vector3(0f, -0.03f, 0.28f);   // nel riferimento dell'arma
            public Quaternion frameR = Quaternion.identity, frameL = Quaternion.identity;
            public Vector3[][] fingR, fingL;        // indice, medio, anulare, mignolo: nocca, PIP, DIP, punta
            public Vector3[] thumbR, thumbL;
            public Vector3[] triggerR;              // PIP, DIP e punta dell'indice destro sul grilletto
        }
        static readonly Dictionary<string, Grips> gripCache = new Dictionary<string, Grips>();
        static readonly float[] TipCurl = { 0f, 22f, 24f, 26f, 26f };   // piegatura dell'ultima falange (genera_personaggi: body.HAND_CURL)

        // ------------------------------------------------------------------ creazione
        /// <summary>Un corpo sotto 'parent' (coordinate locali azzerate), sullo strato 'layer'. Nullo se il prefab manca (build senza i modelli).</summary>
        public static SurvivorBody Create(Transform parent, int slot, int layer, string look = null)
        {
            // 'look': l'aspetto del personaggio (Characters.CharacterLooks); senza, o se non esiste, quello dello slot
            string id = !string.IsNullOrEmpty(look) && System.Array.IndexOf(Ids, look) >= 0 ? look : IdOf(slot);
            var prefab = Resources.Load<GameObject>("Survivors/" + id);
            if (prefab == null) { id = Ids[0]; prefab = Resources.Load<GameObject>("Survivors/" + id); }
            if (prefab == null) return null;
            var go = Instantiate(prefab, parent);
            go.name = "Sopravvissuto " + id;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var b = go.AddComponent<SurvivorBody>();
            b.Slot = slot; b.Id = id;
            if (!b.Setup())
            {
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
                return null;
            }
            b.Retag(layer);
            return b;
        }

        bool Setup()
        {
            foreach (var a in GetComponentsInChildren<Animator>(true)) a.enabled = false;
            model = transform.childCount > 0 ? transform.GetChild(0) : transform;
            modelRestPos = model.localPosition; modelRestRot = model.localRotation;
            var map = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) map[t.name] = t;
            Transform B(string n) { map.TryGetValue(n, out var t); return t; }
            hips = B("Hips"); spine = B("Spine"); chest = B("Chest"); uchest = B("UpperChest"); neck = B("Neck"); head = B("Head");
            thighL = B("Thigh_L"); shinL = B("Shin_L"); footL = B("Foot_L"); thighR = B("Thigh_R"); shinR = B("Shin_R"); footR = B("Foot_R");
            uarmL = B("UpperArm_L"); larmL = B("LowerArm_L"); handL = B("Hand_L"); uarmR = B("UpperArm_R"); larmR = B("LowerArm_R"); handR = B("Hand_R");
            if (hips == null || head == null || thighL == null || thighR == null || handL == null || handR == null || uarmL == null || uarmR == null)
            {
                Debug.LogWarning("[Survivor] scheletro incompleto");
                return false;
            }
            string[] fn = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
            for (int f = 0; f < 5; f++)
                for (int i = 0; i < 3; i++)
                {
                    fingL[f * 3 + i] = B($"{fn[f]}{i + 1}_L");
                    fingR[f * 3 + i] = B($"{fn[f]}{i + 1}_R");
                }
            foreach (var t in fingL) if (t == null) { Debug.LogWarning("[Survivor] dita incomplete"); return false; }
            foreach (var t in fingR) if (t == null) { Debug.LogWarning("[Survivor] dita incomplete"); return false; }
            bones = hips.GetComponentsInChildren<Transform>(true);
            foreach (var t in bones)
                rest[t] = new Rest { lpos = t.localPosition, lrot = t.localRotation, rs = Quaternion.Inverse(transform.rotation) * t.rotation, prs = transform.InverseTransformPoint(t.position) };
            xLeft = Mathf.Sign(rest[uarmL].prs.x);
            legL1 = (rest[shinL].prs - rest[thighL].prs).magnitude; legL2 = (rest[footL].prs - rest[shinL].prs).magnitude;
            armL1 = (rest[larmL].prs - rest[uarmL].prs).magnitude; armL2 = (rest[handL].prs - rest[larmL].prs).magnitude;
            frameRsL = HandFrame(handL, fingL, out curlAxisL, out var palmL);
            frameRsR = HandFrame(handR, fingR, out curlAxisR, out var palmR);
            fingDirL = FingerDirs(fingL, curlAxisL, palmL);
            fingDirR = FingerDirs(fingR, curlAxisR, palmR);
            // l'occhio: dove le due sfere degli occhi stanno nella testa
            eyeLocal = head.InverseTransformPoint(head.position + transform.up * 0.10f + transform.forward * 0.09f);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.name.Contains("eyes")) { eyeLocal = head.InverseTransformPoint(r.bounds.center); break; }
            eyeT = new GameObject("Occhi").transform;          // creato dopo 'bones': ResetPose non lo tocca
            eyeT.SetParent(head, false);
            eyeT.localPosition = eyeLocal;
            return true;
        }

        /// <summary>Sistema della mano a riposo nella radice (dita avanti, dorso su), l'asse che chiude le dita e la direzione del palmo.</summary>
        Quaternion HandFrame(Transform hand, Transform[] fing, out Vector3 curlAxis, out Vector3 palm)
        {
            Vector3 w = rest[hand].prs;
            Vector3 f = (rest[fing[6]].prs - w).normalized;                  // verso la nocca del medio
            Vector3 v = rest[fing[5]].prs - rest[fing[3]].prs;               // indice, dalla nocca alla seconda articolazione: verso il palmo (a riposo le dita sono mezze chiuse)
            Vector3 c = Vector3.ProjectOnPlane(v, f);
            if (c.sqrMagnitude < 1e-8f) c = Vector3.ProjectOnPlane(-Vector3.up, f);
            c.Normalize();
            curlAxis = Vector3.Cross(f, c).normalized;
            // il verso: ruotando f di un angolo positivo attorno all'asse deve avvicinarsi al palmo
            if (Vector3.Dot(Quaternion.AngleAxis(20f, curlAxis) * f, c) < Vector3.Dot(f, c)) curlAxis = -curlAxis;
            palm = c;
            return Quaternion.LookRotation(f, -c);
        }

        /// <summary>La direzione di ogni falange a riposo, nel riferimento locale del suo osso (l'ultima non ha figlio: continua la
        /// precedente piegata come nel modello).</summary>
        Vector3[] FingerDirs(Transform[] fing, Vector3 curlAxis, Vector3 palm)
        {
            var d = new Vector3[15];
            for (int f = 0; f < 5; f++)
                for (int i = 0; i < 3; i++)
                {
                    var b = fing[f * 3 + i];
                    Vector3 dir;
                    if (i < 2) dir = (rest[fing[f * 3 + i + 1]].prs - rest[b].prs).normalized;
                    else
                    {
                        Vector3 prev = (rest[b].prs - rest[fing[f * 3 + i - 1]].prs).normalized;
                        dir = f == 0 ? Vector3.Slerp(prev, palm, 0.28f).normalized : (Quaternion.AngleAxis(TipCurl[f], curlAxis) * prev).normalized;
                    }
                    d[f * 3 + i] = Quaternion.Inverse(rest[b].rs) * dir;
                }
            return d;
        }

        /// <summary>Strato e luci: il corpo sullo strato indicato, illuminato come arma e mani piu' il bit del suo posto.</summary>
        public void Retag(int layer)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                r.renderingLayerMask = SurvivorLight.BodyMask(Slot);
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }

        // ------------------------------------------------------------------ arma
        void SetWeapon(int idx)
        {
            if (idx == weaponIndex) return;
            weaponIndex = idx;
            if (weaponGo != null) { if (Application.isPlaying) Destroy(weaponGo); else DestroyImmediate(weaponGo); }
            weaponGo = null; weaponRig = null; weaponDef = null; grips = Grips.None;
            if (idx < 0 || idx >= WeaponDefs.All.Length) return;
            weaponDef = WeaponDefs.All[idx];
            var prefab = Resources.Load<GameObject>("Armi/" + weaponDef.id);
            if (prefab == null) return;
            weaponGo = new GameObject("Arma " + weaponDef.id);
            weaponGo.transform.SetParent(transform, false);
            var m = Instantiate(prefab, weaponGo.transform);
            m.name = "Modello";
            m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity;
            weaponRig = m.GetComponent<WeaponRig>();
            if (weaponRig != null) weaponRig.Setup();
            grips = GripsFor(weaponDef.id, weaponGo.transform);
            int layer = gameObject.layer;
            foreach (var t in weaponGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in weaponGo.GetComponentsInChildren<Renderer>(true))
            {
                r.renderingLayerMask = SurvivorLight.BodyMask(Slot);
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }

        /// <summary>Le prese della mano destra e della sinistra nel riferimento dell'arma: polso, sistema del palmo e articolazioni delle
        /// dita (presa_destra.json / presa_sinistra.json, le stesse della prima persona), lette sul prefab delle mani una volta per arma.</summary>
        static Grips GripsFor(string id, Transform holder)
        {
            if (gripCache.TryGetValue(id, out var g)) return g;
            g = new Grips();
            var hp = Resources.Load<GameObject>("ManiArmi/" + id);
            if (hp != null)
            {
                var inst = Instantiate(hp, holder);
                inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
                Transform md = null, ms = null;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) { if (t.name == "mano_d") md = t; else if (t.name == "mano_s") ms = t; }
                Vector3[] noTrigger = null;
                if (md != null) g.hasR = ReadSide(id, md, true, holder, ref g.wristR, ref g.frameR, ref g.fingR, ref g.thumbR, ref g.triggerR);
                if (ms != null) g.hasL = ReadSide(id, ms, false, holder, ref g.wristL, ref g.frameL, ref g.fingL, ref g.thumbL, ref noTrigger);
                if (Application.isPlaying) Destroy(inst); else DestroyImmediate(inst);
            }
            gripCache[id] = g;
            return g;
        }

        static bool ReadSide(string id, Transform mano, bool right, Transform holder, ref Vector3 wrist, ref Quaternion frame,
                             ref Vector3[][] fing, ref Vector3[] thumb, ref Vector3[] trig)
        {
            wrist = holder.InverseTransformPoint(mano.position);
            if (!GripPoses.TryGet(id, mano, right, out var gr)) return true;      // solo il punto: la presa generica
            Vector3 P(Vector3 v) => holder.InverseTransformPoint(mano.TransformPoint(v));
            wrist = P(gr.wrist);
            frame = HandRig.PalmFrame(wrist, P(gr.fingers[1][0]), P(gr.dorsal) - P(gr.palm));
            fing = new Vector3[4][];
            for (int f = 0; f < 4; f++)
            {
                fing[f] = new Vector3[4];
                for (int i = 0; i < 4; i++) fing[f][i] = P(gr.fingers[f][i]);
            }
            thumb = new Vector3[4];
            for (int i = 0; i < 4; i++) thumb[i] = P(gr.thumb[i]);
            if (gr.trigger != null) trig = new[] { P(gr.trigger[0]), P(gr.trigger[1]), P(gr.trigger[2]) };
            return true;
        }

        // ------------------------------------------------------------------ fotogramma
        void LateUpdate() { if (autoStep) Step(Time.deltaTime); }

        static float K(float rate, float dt) => dt > 0f ? 1f - Mathf.Exp(-rate * dt) : 1f;
        static float Frac(float x) => x - Mathf.Floor(x);
        Vector3 Right => transform.right;
        Vector3 Out(bool left) => transform.right * (left ? xLeft : -xLeft);       // verso l'esterno del corpo, dal lato dato
        float Inward(bool left) => left ? -xLeft : xLeft;                           // segno della rotazione attorno all'avanti che porta il braccio verso il corpo

        void ResetPose()
        {
            foreach (var t in bones) { var r = rest[t]; t.localPosition = r.lpos; t.localRotation = r.lrot; }
        }

        void Bend(Transform b, float deg, Vector3 axis)
        {
            if (b != null && Mathf.Abs(deg) > 0.001f) b.rotation = Quaternion.AngleAxis(deg, axis) * b.rotation;
        }

        public void Step(float dt)
        {
            if (hips == null) return;
            var inp = input;
            SetWeapon(inp.alive && !inp.downed && (!inp.seated || inp.shotAge < 1.4f) ? inp.weapon : -1);          // in sella le mani sono sul manubrio, finche' la destra non impugna l'arma (appena sparato)
            Vector3 vLoc = Quaternion.Inverse(transform.rotation) * new Vector3(inp.velocity.x, 0f, inp.velocity.z);
            velLocal = Vector3.Lerp(velLocal, vLoc, K(14f, dt));
            float spd = velLocal.magnitude;
            crouchAmt = Mathf.Lerp(crouchAmt, inp.crouch ? 1f : 0f, K(9f, dt));
            sprintAmt = Mathf.Lerp(sprintAmt, inp.sprint && spd > 2.2f ? 1f : 0f, K(8f, dt));
            adsAmt = Mathf.Lerp(adsAmt, Mathf.Clamp01(inp.ads), K(12f, dt));
            leanAmt = Mathf.Lerp(leanAmt, Mathf.Clamp(inp.lean, -1f, 1f), K(9f, dt));
            airAmt = Mathf.Lerp(airAmt, inp.grounded ? 0f : 1f, K(10f, dt));
            lieAmt = Mathf.Lerp(lieAmt, !inp.alive ? 1f : 0f, K(7f, dt));
            downAmt = Mathf.Lerp(downAmt, inp.alive && inp.downed ? 1f : 0f, K(7f, dt));
            seatAmt = Mathf.Lerp(seatAmt, inp.seated && inp.alive && !inp.downed ? 1f : 0f, K(12f, dt));
            pitchSmooth = Mathf.Lerp(pitchSmooth, inp.pitch, K(18f, dt));
            float gaitGoal = inp.grounded && inp.alive && !inp.downed ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.7f, spd)) : 0f;
            gait = Mathf.Lerp(gait, gaitGoal, K(10f, dt));
            float stepLen = (0.60f + 0.16f * Mathf.Min(spd, 6f)) * Mathf.Lerp(1f, 0.62f, crouchAmt);
            phase = Frac(phase + dt * spd / Mathf.Max(0.25f, 2f * stepLen));
            if (debugPhase >= 0f) phase = debugPhase;
            ResetPose();
            // morto: il corpo cade all'indietro intero (rotazione del modello attorno ai piedi)
            if (model != transform)
            {
                model.localRotation = Quaternion.Euler(-88f * lieAmt, 0f, 0f) * modelRestRot;
                model.localPosition = modelRestPos + new Vector3(0f, 0.12f * lieAmt, 0f);
            }
            if (lieAmt > 0.02f) return;
            PoseTorso(inp);
            PoseLegs(inp, spd, stepLen, dt);
            PoseArms(inp, dt);
        }

        // ------------------------------------------------------------------ busto, testa
        void PoseTorso(SurvivorInput inp)
        {
            float cyc = phase * 2f * Mathf.PI;
            float pitch = Mathf.Clamp(pitchSmooth * Mathf.Rad2Deg, -75f, 70f);
            float fwdLean = Mathf.Lerp(Mathf.Lerp(4f * sprintAmt + 14f * crouchAmt + 2f * gait, -34f, downAmt), inp.seatLean > 0f ? inp.seatLean : SeatLean, seatAmt);
            float drop = 0.012f + 0.42f * crouchAmt + 0.05f * airAmt;
            hipDrop = Mathf.Lerp(hipDrop, drop, 0.5f);
            Vector3 stand = rest[hips].prs + new Vector3(Mathf.Sin(cyc) * 0.012f * gait, -hipDrop, -0.09f * crouchAmt);
            Vector3 sit = new Vector3(0f, 0.15f, -0.20f);
            hips.position = transform.TransformPoint(Vector3.Lerp(Vector3.Lerp(stand, sit, downAmt), inp.seatHips.y > 0f ? inp.seatHips : SeatHips, seatAmt));
            Vector3 r = Right, fw = transform.forward;
            Bend(hips, Mathf.Sin(cyc) * 5f * gait, Vector3.up);
            Bend(hips, Mathf.Cos(cyc) * 2.5f * gait, fw);
            float twist = -Mathf.Sin(cyc) * 6f * gait;
            float breath = Mathf.Sin(Time.time * 1.7f + Slot) * 0.6f;
            Bend(spine, fwdLean * 0.4f + pitch * 0.18f, r);
            Bend(spine, twist * 0.5f, Vector3.up);
            Bend(spine, -leanAmt * 6f, fw);
            Bend(chest, fwdLean * 0.3f + pitch * 0.28f + breath, r);
            Bend(chest, twist * 0.5f, Vector3.up);
            Bend(chest, -leanAmt * 5f, fw);
            Bend(uchest, fwdLean * 0.3f + pitch * 0.24f + breath * 0.5f, r);
            Bend(uchest, -leanAmt * 3f, fw);
            // la testa guarda dove guarda il giocatore: compensa il busto
            Bend(neck, pitch * 0.12f - fwdLean * 0.55f, r);
            Bend(head, pitch * 0.18f - fwdLean * 0.45f, r);
            Bend(head, leanAmt * 3f, fw);
        }

        // ------------------------------------------------------------------ gambe
        void PoseLegs(SurvivorInput inp, float spd, float stepLen, float dt)
        {
            Vector3 md = spd > 0.05f ? velLocal / spd : Vector3.forward;
            float amp = Mathf.Min(0.6f * stepLen, 0.46f) * gait;
            float stance = Mathf.Lerp(0.6f, 0.46f, sprintAmt);                  // frazione del ciclo con il piede a terra
            float L = (legL1 + legL2) * 0.985f;                                  // la gamba non si distende mai del tutto
            float lowerBy = 0f;
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                float ph = Frac(phase + (left ? 0f : 0.5f));
                float off, lifted = 0f;
                if (ph < stance) off = Mathf.Lerp(1f, -1f, ph / stance);          // a terra: il piede scorre indietro rispetto al corpo
                else
                {
                    float t = (ph - stance) / (1f - stance);
                    off = Mathf.Lerp(-1f, 1f, Mathf.SmoothStep(0f, 1f, t));
                    lifted = Mathf.Sin(t * Mathf.PI);
                }
                Transform th = left ? thighL : thighR, ft = left ? footL : footR;
                Vector3 target = rest[ft].prs + md * (off * amp);
                target.y += lifted * (0.06f + 0.05f * Mathf.Clamp01(spd / 4f)) * gait;
                target.y += (off < 0f ? -off * 0.02f * gait : 0f) + 0.15f * airAmt * (left ? 0.5f : 1f);
                // seduto: gambe distese in avanti sul pavimento, un po' aperte
                Vector3 sit = new Vector3(rest[ft].prs.x * 1.25f, 0.09f, 0.52f + (left ? 0f : 0.07f));
                target = Vector3.Lerp(target, sit, downAmt);
                // in sella: la caviglia sulla pedana della moto (nel riferimento della radice, che sta sulla moto)
                Vector3 peg = inp.seatPegs.y > 0f ? inp.seatPegs : new Vector3(PegX, PegY, PegZ);
                target = Vector3.Lerp(target, new Vector3(Mathf.Sign(rest[ft].prs.x) * peg.x, peg.y, peg.z), seatAmt);
                // terreno sotto il piede: scale e dislivelli
                float gy = 0f; Vector3 n = Vector3.up;
                if (inp.grounded && seatAmt < 0.5f && Physics.Raycast(transform.TransformPoint(target) + Vector3.up * 0.6f, Vector3.down, out var hit, 1.2f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                {
                    gy = Mathf.Clamp(hit.point.y - transform.position.y, -0.5f, 0.45f);
                    n = hit.normal;
                }
                if (seatAmt > 0.5f) n = transform.up;          // in sella il piede segue la moto, non il terreno
                footY[side] = Mathf.Lerp(footY[side], gy, K(22f, dt));
                footN[side] = Vector3.Slerp(footN[side], n, K(14f, dt));
                target.y += footY[side];
                legTarget[side] = target; legOff[side] = off; legLift[side] = lifted;
                // quanto puo' stare alto il bacino perche' questa gamba arrivi a terra
                Vector3 tw = transform.TransformPoint(target), thW = th.position;
                float dh = new Vector2(tw.x - thW.x, tw.z - thW.z).magnitude;
                float maxY = tw.y + Mathf.Sqrt(Mathf.Max(0f, L * L - dh * dh));
                lowerBy = Mathf.Max(lowerBy, thW.y - maxY);
            }
            if (lowerBy > 0f && seatAmt < 0.5f) hips.position -= Vector3.up * lowerBy;
            SolveLeg(thighL, shinL, footL, 0, true);
            SolveLeg(thighR, shinR, footR, 1, false);
        }

        void SolveLeg(Transform th, Transform sh, Transform ft, int side, bool left)
        {
            Vector3 target = transform.TransformPoint(legTarget[side]);
            Vector3 pole = transform.forward + Out(left) * 0.15f + Vector3.up * 0.25f * downAmt;
            SolveLimb(th, sh, ft, target, pole, legL1, legL2);
            // piede: parallelo al terreno, punta in giu' alla spinta e un po' in su all'appoggio del tallone
            float off = legOff[side], pitch = (off < 0f ? -off * 22f : -off * 8f) * gait - legLift[side] * 8f * gait;
            Vector3 nl = transform.InverseTransformDirection(footN[side]);
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, nl);
            ft.rotation = transform.rotation * tilt * Quaternion.AngleAxis(pitch, Vector3.right) * rest[ft].rs;
        }

        /// <summary>Due ossa a posizione raggiunta: l'estremo (polso, caviglia) va su 'target' (mondo), il gomito o il ginocchio verso 'pole'.</summary>
        void SolveLimb(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, float l1, float l2)
        {
            Vector3 a = upper.position;
            Vector3 d = target - a;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = d.sqrMagnitude > 1e-8f ? d.normalized : -transform.up;
            float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 pp = pole - dir * Vector3.Dot(pole, dir);
            if (pp.sqrMagnitude < 1e-6f) pp = Vector3.Cross(dir, transform.right);
            pp.Normalize();
            Vector3 joint = a + dir * (l1 * cosA) + pp * (l1 * sinA);
            Vector3 tip = a + dir * dist;
            Quaternion rot = transform.rotation;
            Vector3 restUpper = rot * (rest[lower].prs - rest[upper].prs).normalized;
            upper.rotation = Quaternion.FromToRotation(restUpper, joint - a) * (rot * rest[upper].rs);
            Vector3 restLower = rot * (rest[end].prs - rest[lower].prs).normalized;
            lower.rotation = Quaternion.FromToRotation(restLower, tip - joint) * (rot * rest[lower].rs);
        }

        // ------------------------------------------------------------------ braccia
        void PoseArms(SurvivorInput inp, float dt)
        {
            if (inp.downed && downAmt > 0.02f)
            {
                SwingArm(true, 0f, inp); SwingArm(false, 0f, inp);
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    Transform u = left ? uarmL : uarmR, l = left ? larmL : larmR, h = left ? handL : handR;
                    Quaternion u0 = u.rotation, l0 = l.rotation;
                    Vector3 hand = transform.TransformPoint(new Vector3((left ? xLeft : -xLeft) * 0.30f, 0.05f, -0.30f));
                    SolveLimb(u, l, h, hand, Out(left) * 0.5f - transform.forward * 0.6f + Vector3.up * 0.1f, armL1, armL2);
                    u.rotation = Quaternion.Slerp(u0, u.rotation, downAmt);
                    l.rotation = Quaternion.Slerp(l0, l.rotation, downAmt);
                }
                return;
            }
            if (seatAmt > 0.02f && inp.alive) { PoseRideArms(inp); return; }
            if (weaponGo != null && inp.alive) { PoseWeapon(inp, dt); return; }
            float swing = 26f * gait * Mathf.Lerp(1f, 0.5f, crouchAmt) * Mathf.Lerp(1f, 1.35f, sprintAmt);
            // le braccia contro le gambe: la sinistra va avanti quando e' avanti il piede destro
            SwingArm(true, legOff[1] * swing, inp);
            SwingArm(false, legOff[0] * swing, inp);
        }

        /// <summary>In sella: le due mani sulle manopole (che gli passa chi possiede il corpo, in coordinate del mondo), il gomito in fuori e un po' in basso, le dita in un pugno morbido. Dentro e fuori
        /// dalla sella si sfuma con seatAmt (le rotazioni della posa libera sono gia' state date dal ciclo del passo). Se ha appena sparato (c'e' l'arma in mano) la destra impugna l'arma e la sinistra
        /// resta sul manubrio.</summary>
        void PoseRideArms(SurvivorInput inp)
        {
            SwingArm(true, 0f, inp); SwingArm(false, 0f, inp);
            float off = 0f;
            bool gun = weaponGo != null && inp.shotAge < 1.4f;
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                if (gun && !left) continue;                    // la destra sta sull'arma (PoseRideGun)
                Transform u = left ? uarmL : uarmR, l = left ? larmL : larmR, h = left ? handL : handR;
                Quaternion u0 = u.rotation, l0 = l.rotation, h0 = h.rotation;
                // la manopola del lato in cui sta la spalla (nel sistema della radice, che guarda avanti come la moto)
                Vector3 grip = rest[u].prs.x < 0f ? inp.gripLeft : inp.gripRight;
                SolveLimb(u, l, h, grip, Out(left) * 0.8f + Vector3.down * 0.45f - transform.forward * 0.2f, armL1, armL2);
                AimHand(h, transform.rotation * Quaternion.Euler(8f, 0f, 0f), left ? frameRsL : frameRsR);
                u.rotation = Quaternion.Slerp(u0, u.rotation, seatAmt);
                l.rotation = Quaternion.Slerp(l0, l.rotation, seatAmt);
                h.rotation = Quaternion.Slerp(h0, h.rotation, seatAmt);
                off += Vector3.Distance(h.position, grip);
            }
            RideHandsOff = off;
            if (gun) PoseRideGun(inp); else AimFingers(fingR, fingDirR, handR, curlAxisR, null, null, null, null);
            AimFingers(fingL, fingDirL, handL, curlAxisL, null, null, null, null);
        }

        /// <summary>Seduto con l'arma in mano: la destra la impugna davanti a se', un poco in basso, puntata dove punta la moto e alta o bassa come guarda il giocatore (l'imbardata dello sguardo non
        /// arriva agli altri, il beccheggio si'). La sinistra e' sul manubrio e non la tocca.</summary>
        void PoseRideGun(SurvivorInput inp)
        {
            var g = grips;
            float pitchDeg = Mathf.Clamp(pitchSmooth * Mathf.Rad2Deg, -60f, 50f);
            float kick = Mathf.Exp(-Mathf.Max(0f, inp.shotAge) * 13f);
            Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            Quaternion yawQ = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward, Vector3.up);
            Quaternion aimQ = yawQ * Quaternion.Euler(pitchDeg - kick * 4f, 0f, 0f);
            Quaternion bodyQ = yawQ * Quaternion.Euler(pitchDeg * 0.45f, 0f, 0f);
            Vector3 shR = uarmR.position;
            Vector3 hipOff = new Vector3(-0.03f * -xLeft, -0.27f, 0.36f);
            Vector3 wPos = shR + bodyQ * hipOff - aimQ * g.wristR - aimQ * Vector3.forward * (kick * 0.04f);
            Vector3 toWrist = wPos + aimQ * g.wristR - shR;
            float maxReach = (armL1 + armL2) * 0.97f;
            if (toWrist.magnitude > maxReach) wPos -= toWrist.normalized * (toWrist.magnitude - maxReach);
            weaponGo.transform.SetPositionAndRotation(wPos, aimQ);
            Transform wt = weaponGo.transform;
            Quaternion u0 = uarmR.rotation, l0 = larmR.rotation, h0 = handR.rotation;
            SolveLimb(uarmR, larmR, handR, wt.TransformPoint(g.wristR), Out(false) * 0.55f + Vector3.down * 0.75f - transform.forward * 0.1f, armL1, armL2);
            AimHand(handR, wt.rotation * g.frameR, frameRsR);
            uarmR.rotation = Quaternion.Slerp(u0, uarmR.rotation, seatAmt);
            larmR.rotation = Quaternion.Slerp(l0, larmR.rotation, seatAmt);
            handR.rotation = Quaternion.Slerp(h0, handR.rotation, seatAmt);
            bool trig = g.triggerR != null && inp.shotAge < 0.35f;
            AimFingers(fingR, fingDirR, handR, curlAxisR, g.fingR, g.thumbR, trig ? g.triggerR : null, wt);
        }

        /// <summary>Braccio libero lungo il fianco: oscilla avanti/indietro (gradi, avanti positivo), gomito mezzo piegato, piu' chiuso di corsa.</summary>
        void SwingArm(bool left, float forwardDeg, SurvivorInput inp)
        {
            Transform u = left ? uarmL : uarmR, l = left ? larmL : larmR;
            float flex = 12f + Mathf.Max(0f, forwardDeg) * 0.8f + 38f * sprintAmt;
            Bend(u, Inward(left) * 17f, transform.forward);
            Bend(u, -forwardDeg - 6f * sprintAmt, Right);
            Bend(l, -flex, Right);
            if (inp.climb > 0f) { Bend(u, -150f * inp.climb, Right); Bend(l, -20f * inp.climb, Right); }
        }

        // ------------------------------------------------------------------ arma in mano
        void PoseWeapon(SurvivorInput inp, float dt)
        {
            var g = grips;
            float pitchDeg = Mathf.Clamp(pitchSmooth * Mathf.Rad2Deg, -75f, 70f);
            float kick = Mathf.Exp(-Mathf.Max(0f, inp.shotAge) * 13f);              // 1 al colpo, poi cala
            Quaternion yaw = transform.rotation;
            Quaternion aimQ = yaw * Quaternion.Euler(pitchDeg + 34f * sprintAmt - kick * 4f, 0f, -leanAmt * 6f);
            Quaternion bodyQ = yaw * Quaternion.Euler(pitchDeg * 0.45f, 0f, 0f);
            Vector3 shR = uarmR.position, shL = uarmL.position;
            // posizione di marcia (polso destro rispetto alla spalla destra), di corsa piu' bassa e vicina; x positivo = verso la destra
            Vector3 hipOff = Vector3.Lerp(new Vector3(-0.03f, -0.33f, 0.30f), new Vector3(-0.05f, -0.40f, 0.20f), sprintAmt);
            hipOff.x *= -xLeft;
            Vector3 wPosHip = shR + bodyQ * hipOff - aimQ * g.wristR;
            // in mira: il punto di mira dell'arma sulla linea dell'occhio, adsDist davanti
            Vector3 adsLocal = weaponRig != null ? weaponRig.AdsLocal : new Vector3(0f, 0.07f, 0.15f);
            Vector3 wPosAds = EyeWorld + aimQ * Vector3.forward * weaponDef.adsDist - aimQ * adsLocal;
            Vector3 wPos = Vector3.Lerp(wPosHip, wPosAds, adsAmt) - aimQ * Vector3.forward * (kick * 0.04f);
            // il polso destro deve stare a portata di braccio
            Vector3 toWrist = wPos + aimQ * g.wristR - shR;
            float maxReach = (armL1 + armL2) * 0.97f;
            if (toWrist.magnitude > maxReach) wPos -= toWrist.normalized * (toWrist.magnitude - maxReach);
            weaponGo.transform.SetPositionAndRotation(wPos, aimQ);
            Transform wt = weaponGo.transform;

            // mano destra
            Vector3 poleR = Vector3.Lerp(Out(false) * 0.55f + Vector3.down * 0.75f, Out(false) * 0.85f + Vector3.down * 0.45f, adsAmt) - transform.forward * 0.1f;
            SolveLimb(uarmR, larmR, handR, wt.TransformPoint(g.wristR), poleR, armL1, armL2);
            AimHand(handR, wt.rotation * g.frameR, frameRsR);

            // mano sinistra: sull'astina, o (ricarica) al caricatore
            bool two = weaponDef.hand > 0.5f;
            if (two)
            {
                Vector3 wristL = wt.TransformPoint(g.wristL);
                if (inp.reloading) wristL = ReloadWrist(wristL, shL, bodyQ, dt); else reloadT = 0f;
                SolveLimb(uarmL, larmL, handL, wristL, Out(true) * 0.45f + Vector3.down * 0.8f, armL1, armL2);
                AimHand(handL, wt.rotation * g.frameL, frameRsL);
            }
            else SwingArm(true, 0f, inp);
            PoseFingers(inp, g, two, wt);
        }

        /// <summary>Ricarica: la sinistra lascia l'astina, va al marsupio, poi al vano del caricatore e torna.</summary>
        Vector3 ReloadWrist(Vector3 grip, Vector3 shL, Quaternion bodyQ, float dt)
        {
            reloadT += dt;
            float t = Frac(reloadT / 1.5f);
            Vector3 pouch = shL + bodyQ * new Vector3(0.02f * xLeft, -0.50f, 0.14f);
            Vector3 well = weaponRig != null && weaponRig.mag != null ? weaponRig.mag.position : grip;
            if (t < 0.30f) return Vector3.Lerp(grip, pouch, Mathf.SmoothStep(0f, 1f, t / 0.30f));
            if (t < 0.70f) return Vector3.Lerp(pouch, well, Mathf.SmoothStep(0f, 1f, (t - 0.30f) / 0.40f));
            return Vector3.Lerp(well, grip, Mathf.SmoothStep(0f, 1f, (t - 0.70f) / 0.30f));
        }

        void AimHand(Transform hand, Quaternion targetWorld, Quaternion frameRs)
        {
            Quaternion restFrame = transform.rotation * frameRs;
            hand.rotation = targetWorld * Quaternion.Inverse(restFrame) * (transform.rotation * rest[hand].rs);
        }

        // ------------------------------------------------------------------ dita
        void PoseFingers(SurvivorInput inp, Grips g, bool two, Transform wt)
        {
            bool trig = g.triggerR != null && (adsAmt > 0.5f || inp.shotAge < 0.35f);
            AimFingers(fingR, fingDirR, handR, curlAxisR, g.fingR, g.thumbR, trig ? g.triggerR : null, wt);
            if (two) AimFingers(fingL, fingDirL, handL, curlAxisL, g.fingL, g.thumbL, null, wt);
        }

        void AimFingers(Transform[] fing, Vector3[] dirs, Transform hand, Vector3 curlAxisRs, Vector3[][] jf, Vector3[] jt, Vector3[] trigger, Transform wt)
        {
            if (jf == null || jt == null)
            {
                // senza presa registrata: un pugno morbido
                Vector3 axis = hand.rotation * (Quaternion.Inverse(transform.rotation * rest[hand].rs) * (transform.rotation * curlAxisRs));
                for (int f = 0; f < 5; f++)
                    for (int i = 0; i < 3; i++) Bend(fing[f * 3 + i], (f == 0 ? 14f : 34f) + 10f * i, axis);
                return;
            }
            for (int f = 0; f < 5; f++)
            {
                Vector3[] j = f == 0 ? jt : jf[f - 1];
                if (f == 1 && trigger != null) j = new[] { jf[0][0], trigger[0], trigger[1], trigger[2] };
                for (int i = 0; i < 3; i++)
                {
                    var b = fing[f * 3 + i];
                    Vector3 want = wt.TransformPoint(j[i + 1]) - wt.TransformPoint(j[i]);
                    if (want.sqrMagnitude < 1e-10f) continue;
                    Vector3 cur = b.rotation * dirs[f * 3 + i];
                    b.rotation = Quaternion.FromToRotation(cur, want) * b.rotation;
                }
            }
        }
    }
}

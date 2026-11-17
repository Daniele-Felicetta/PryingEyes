using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>La seconda mano che lascia l'arma (2026-11-16): gesti delle magie, sigilli tracciati sull'arma, lanci. Vive con il modello
    /// in prima persona (Weapon.AddHands la costruisce, un cambio d'arma la rifa') e possiede la mano libera articolata (HandRig).
    ///
    /// Come si muove: un'animazione e' una serie di chiavi; ogni chiave dice dove sta un punto di contatto della mano (il polso, il
    /// centro del palmo, il polpastrello dell'indice, del medio o del pollice), in quale spazio (la camera; l'arma, che dondola e
    /// rincula, con le sue parti: bocca, caricatore, finestra di espulsione, mire, astina, castello, canna; la presa a riposo sull'astina;
    /// fuori campo in basso), verso dove puntano le dita e il dorso, la posa della mano (HandPose), il tremito e come stanno gomito e
    /// spalla. Tra le chiavi il polso segue una curva di Catmull-Rom, le rotazioni e le pose si sfumano con l'andamento della chiave.
    /// Altre chiavi muovono l'arma (offerta di lato per lavorarci sopra, abbassata quando la regge una mano sola) ed eventi a tempo
    /// fanno partire la magia o il sigillo. Il braccio lo chiude ArmRig col braccio libero (Free), il polso resta nei suoi gradi.
    ///
    /// Il cambio di mano non si vede: la mano libera si mette esattamente dove sta il guanto avvolto (GripPoses: polso, palmo e le
    /// articolazioni della presa di quell'arma) con la stessa posa delle dita, e solo allora il guanto avvolto si spegne; al ritorno al
    /// contrario. Con le armi a una mano la sinistra entra ed esce dal basso, fuori campo.</summary>
    public sealed class OffHand
    {
        public enum Space { Camera, Weapon, Grip, Below }
        public enum Contact { Wrist, Palm, Index, Middle, Thumb, Pinky }
        /// <summary>Parti dell'arma su cui si mette la mano. MagGrip e' dove la sinistra afferra il caricatore (fianco sinistro, sotto
        /// il pozzetto) e Port la finestra di caricamento sotto il castello: punti fermi dell'arma. Charge, SlideRear, Cover, Breech,
        /// Forend e Cylinder stanno sulle parti che si muovono (leva d'armamento, carrello, coperchio, canne, astina della doppietta,
        /// tamburo): la mano che ci sta sopra le segue (ReloadAnims).</summary>
        public enum Part { None, Muzzle, Mag, Eject, Sight, GripL, Receiver, Barrel, MagBase, LeftSide, MagGrip, Charge, SlideRear, Cover, Breech, Forend, Cylinder, Port, Under }

        public struct Key
        {
            public float t;
            public Space space;
            public Part part;
            public Vector3 pos;                 // punto del contatto (spazio della chiave; con una parte: scarto dalla parte)
            public Vector3 fingers, dorsal;     // verso delle dita e del dorso, stesso spazio
            public HandPose pose;
            public Contact contact;
            public float tremor, free;          // tremito (gradi), braccio libero (0..1, di norma 1)
            public Vector3 pole;                // dove punta il gomito (spazio della camera), zero = naturale
            public Vector3 shoulder;            // spostamento della spalla (camera)
            public int ease;                    // 0 liscio, 1 accelera (scatto), 2 rallenta (arrivo), 3 lineare
            public float barrel;                // Part.Barrel / LeftSide: frazione dal castello (0) alla bocca (1)
            public bool gripPose;               // Space.Grip: le dita nella presa dell'arma (altrimenti 'pose')
        }

        /// <summary>Chiave dell'arma: scarto e rotazione scritti nel gesto; pull (0..1) e' quanto dell'avvicinamento calcolato per
        /// quest'arma (OffHand.Reach) si aggiunge: le armi lunghe vengono portate a portata della spalla.</summary>
        public struct WKey { public float t; public Vector3 pos, rot; public int ease; public float pull; }
        public struct Ev { public float t; public string id; }

        public sealed class Anim
        {
            public string name;
            public float duration = 1f;
            public Key[] keys;
            public WKey[] weapon;
            public Ev[] events;
            /// <summary>Velocita' (Gesto rapido la alza): la durata vera e' duration / speed.</summary>
            public float speed = 1f;
            /// <summary>Spostamento del punto di lavoro rispetto a ReachTarget per questo gesto (camera; solo armi a due mani): un gesto
            /// che lavora sotto l'arma, sul caricatore, la vuole piu' alta perche' la mano resti in vista.</summary>
            public Vector3 reachOffset;
            /// <summary>Ricariche (ReloadAnims; secondi, negativo = non c'e'): quando la mano prende il caricatore dall'arma, lo butta,
            /// torna dalla cintura con quello nuovo, comincia a infilarlo e lo inserisce; la chiave della posa d'inserimento (mano col
            /// caricatore al suo posto) e il verso in cui il caricatore si sfila (spazio dell'arma). Weapon attacca il caricatore al palmo.</summary>
            public float take = -1f, drop = -1f, fetch = -1f, insert = -1f, seat = -1f;
            public int seatKey = -1;
            public Vector3 magAxis = Vector3.down;
        }

        // ------------------------------------------------------------------ stato costruito con il modello
        readonly Transform cam, viewRoot, handL;
        readonly ArmRig armL;
        readonly WeaponRig rig;
        readonly HandRig free;
        readonly Renderer[] wrapped;
        Vector3 handLRestPos;
        Quaternion handLRestRot;
        readonly Vector3 palmRelPos;            // il palmo della mano libera nel riferimento di mano_s
        readonly Quaternion palmRelRot;
        HandPose gripPose;
        readonly bool oneHanded;
        readonly WeaponSurface surface;
        public readonly string WeaponId;

        // ------------------------------------------------------------------ animazione in corso
        Anim anim;
        float time;
        bool external;
        int nextEvent;
        Action<string> onEvent;
        bool freeShown;
        Vector3 curPalmCam;
        Quaternion curPalmRotCam;
        /// <summary>Posa e punto del palmo dell'ultimo fotogramma (per gli effetti che si attaccano alla mano).</summary>
        public HandPose LastPose { get; private set; }

        public bool Busy => anim != null;
        /// <summary>L'animazione in corso segue un altro orologio (la ricarica): Weapon chiama Seek prima di Tick.</summary>
        public bool External => anim != null && external;
        public Anim CurrentAnim => anim;
        /// <summary>Il tempo dell'animazione in corso (secondi).</summary>
        public float Clock => time;
        /// <summary>L'osso del palmo della mano libera (origine al polso): ci si attacca quello che la mano porta.</summary>
        public Transform PalmBone => free.Palm;
        /// <summary>Verso cui il caricatore si sfila dall'arma (spazio dell'arma).</summary>
        public Vector3 MagAxis { get; private set; } = Vector3.down;
        public string Current => anim != null ? anim.name : null;
        public float Progress => anim != null ? Mathf.Clamp01(time / anim.duration) : 0f;
        public HandRig Hand => free;
        public bool OneHanded => oneHanded;
        public WeaponSurface Surface => surface;
        /// <summary>Quanto la mano libera ha preso il posto del guanto avvolto (per le prove).</summary>
        public bool FreeVisible => freeShown;

        OffHand(Transform cam, Transform viewRoot, Transform handL, ArmRig armL, WeaponRig rig, HandRig free, Renderer[] wrapped,
                Vector3 palmRelPos, Quaternion palmRelRot, HandPose gripPose, bool oneHanded, string id)
        {
            this.cam = cam; this.viewRoot = viewRoot; this.handL = handL; this.armL = armL; this.rig = rig; this.free = free;
            this.wrapped = wrapped; this.palmRelPos = palmRelPos; this.palmRelRot = palmRelRot; this.gripPose = gripPose;
            this.oneHanded = oneHanded; WeaponId = id;
            handLRestPos = handL.localPosition;
            handLRestRot = handL.localRotation;
            surface = new WeaponSurface(viewRoot, rig);
            LastPose = gripPose;
            // il palmo della mano libera nel riferimento di mano_s: LookRotation(dita, dorso), quindi x = pollice per la sinistra
            if (armL != null) { armL.ThumbLocal = palmRelRot * Vector3.right; armL.FingersLocal = palmRelRot * Vector3.forward; }
        }

        static GameObject prefab;
        static bool prefabTried;

        static Transform Child(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        /// <summary>Costruisce la seconda mano per il modello appena fatto. Null senza mano libera (Resources/ManoLibera) o senza mano
        /// sinistra su misura: allora le magie non hanno gesti e l'arma si comporta com'era.</summary>
        public static OffHand Build(Transform cam, Transform viewRoot, Transform handsRoot, Transform handL, ArmRig armL, WeaponRig rig,
                                    WeaponDef d, Renderer[] wrappedLeft)
        {
            if (cam == null || viewRoot == null || handL == null || rig == null) return null;
            if (!prefabTried) { prefabTried = true; prefab = Resources.Load<GameObject>("ManoLibera"); }
            if (prefab == null) return null;
            bool oneHanded = d.hand <= 0.5f;
            // la presa di quest'arma nel riferimento di mano_s; senza tabella una presa generica sotto l'astina
            Vector3 wristL, mcpL, dorsalL;
            GripPoses.Grip grip;
            bool haveGrip = GripPoses.TryGet(d.id, handL, out grip);
            if (haveGrip) { wristL = grip.wrist; mcpL = grip.fingers[1][0]; dorsalL = grip.dorsal - grip.palm; }
            else
            {
                var pj = Child(handL, "polso_s");
                wristL = pj != null ? handL.InverseTransformPoint(pj.position) : new Vector3(-0.09f, -0.04f, 0f);
                mcpL = wristL + (Vector3.zero - wristL).normalized * 0.09f;
                dorsalL = handL.InverseTransformDirection(viewRoot.TransformDirection(Vector3.down));
            }
            Quaternion wrapRot = HandRig.PalmFrame(wristL, mcpL, dorsalL);

            var inst = UnityEngine.Object.Instantiate(prefab, handL);
            inst.name = "ManoLibera";
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = viewRoot.gameObject.layer;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            // il riferimento del palmo della mano libera (come lo costruisce HandRig) rispetto alla sua radice
            Transform tw = Child(inst.transform, "L_polso"), tm = Child(inst.transform, "L_medio_1"), tp = Child(inst.transform, "L_palmo"), td = Child(inst.transform, "L_dorso");
            if (tw == null || tm == null || tp == null || td == null) { UnityEngine.Object.Destroy(inst); return null; }
            Vector3 pPR = inst.transform.InverseTransformPoint(tw.position);
            Quaternion qPR = HandRig.PalmFrame(pPR, inst.transform.InverseTransformPoint(tm.position),
                                               inst.transform.InverseTransformPoint(td.position) - inst.transform.InverseTransformPoint(tp.position));
            Quaternion qR = wrapRot * Quaternion.Inverse(qPR);
            inst.transform.localRotation = qR;
            inst.transform.localPosition = wristL - qR * pPR;

            // la mano articolata tiene il suo materiale (Mani_GuantoLibero: texture uniche dipinte sulla sua superficie)
            var hand = HandRig.Build(inst.transform, null, null);
            if (hand == null) { UnityEngine.Object.Destroy(inst); return null; }
            foreach (var r in hand.Renderers) r.renderingLayerMask = Weapon.ViewLayer;

            HandPose gp = hand.RestPose();
            if (haveGrip)
            {
                Quaternion inv = Quaternion.Inverse(wrapRot);
                var dita = new Vector3[4][];
                for (int f = 0; f < 4; f++)
                {
                    dita[f] = new Vector3[4];
                    for (int i = 0; i < 4; i++) dita[f][i] = inv * (grip.fingers[f][i] - wristL);
                }
                var pol = new Vector3[4];
                for (int i = 0; i < 4; i++) pol[i] = inv * (grip.thumb[i] - wristL);
                gp = hand.PoseFromJoints(dita, pol);
            }
            else gp = oneHanded ? HandPoses.Cup : HandPoses.Grip;
            hand.Apply(gp);
            hand.SetVisible(false);
            // il palmo nel riferimento di mano_s (costante): serve a mettere mano_s dove si vuole il palmo
            Vector3 prp = handL.InverseTransformPoint(hand.Palm.position);
            Quaternion prr = Quaternion.Inverse(handL.rotation) * hand.Palm.rotation;
            return new OffHand(cam, viewRoot, handL, armL, rig, hand, wrappedLeft, prp, prr, gp, oneHanded, d.id);
        }

        // ------------------------------------------------------------------ comandi
        /// <summary>Fa partire un'animazione; gli eventi arrivano a 'events' (anche "fine" alla fine, "annullata" se interrotta).</summary>
        public bool Play(Anim a, Action<string> events)
        {
            if (a == null || a.keys == null || a.keys.Length < 2) return false;
            if (anim != null) Cancel();
            anim = a; time = 0f; nextEvent = 0; onEvent = events; external = false;
            reach = RealHold ? RealHoldPresent(a) : ReachFor(a);
            if (armL != null) armL.ClampHand = true;           // nel gesto la mano rispetta i campi articolari
            return true;
        }

        /// <summary>Come Play, ma il tempo lo da' Weapon con Seek (la ricarica segue WeaponState: perk, magie che la accorciano).</summary>
        public bool PlayExternal(Anim a, Action<string> events)
        {
            if (!Play(a, events)) return false;
            external = true;
            return true;
        }

        public void Seek(float t)
        {
            if (anim != null) time = Mathf.Clamp(t, 0f, anim.duration);
        }

        /// <summary>Per le prove: la chiave verso cui va la mano, dove la vuole il palmo e dove sta (spazio della camera, cm di scarto).</summary>
        public string DebugKey()
        {
            if (anim == null) return "ferma";
            var ks = anim.keys;
            int i1 = ks.Length - 1;
            for (int i = 1; i < ks.Length; i++) if (time <= ks[i].t) { i1 = i; break; }
            KeyCam(ks[i1], out Vector3 w, out Quaternion r);
            Vector3 palm = cam.InverseTransformPoint(free.Palm.position);
            Vector3 part = ks[i1].space == Space.Weapon ? viewRoot.localPosition + viewRoot.localRotation * PartLocal(ks[i1].part, ks[i1].barrel) : Vector3.zero;
            return $"chiave {i1} ({ks[i1].space} {ks[i1].part} t {ks[i1].t:F2}) polso voluto ({w.x:F3}, {w.y:F3}, {w.z:F3}) polso vero ({palm.x:F3}, {palm.y:F3}, {palm.z:F3}) " +
                   $"scarto {(palm - w).magnitude * 100f:F1} cm, parte ({part.x:F3}, {part.y:F3}, {part.z:F3})";
        }

        /// <summary>Il punto di una parte nello spazio dell'arma (per le coreografie).</summary>
        public Vector3 PartPoint(Part p, float barrel = 0.5f) => PartLocal(p, barrel);

        /// <summary>Lo scarto dell'arma (spazio della camera, come le chiavi WKey) che porta il suo punto p (spazio dell'arma) in target
        /// (spazio della camera) con la rotazione rot della chiave, partendo dalla posa di adesso; lungo al piu' maxLen. Le ricariche
        /// portano cosi' il caricatore davanti al petto, dove arriva la sinistra e dove si vede (al fianco il caricatore di un fucile sta
        /// sotto il bordo dello schermo e a piu' di un braccio dalla spalla sinistra).</summary>
        public Vector3 OffsetToBring(Vector3 p, Vector3 rot, Vector3 target, float maxLen = 0.22f)
        {
            Vector3 at = viewRoot.localPosition + viewRoot.localRotation * (Quaternion.Euler(rot) * p);
            return Vector3.ClampMagnitude(target - at, maxLen);
        }

        /// <summary>Dove starebbe l'osso del palmo (mondo) se la mano fosse esattamente nella chiave i dell'animazione in corso.</summary>
        public bool KeyPalmWorld(int i, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (anim == null || i < 0 || i >= anim.keys.Length) return false;
            KeyCam(anim.keys[i], out Vector3 w, out Quaternion r);
            pos = cam.TransformPoint(w);
            rot = cam.rotation * r;
            return true;
        }

        /// <summary>Presa realistica (Weapon.RealHold): l'arma e' gia' a distanza umana, quindi offrendola alla sinistra non viene
        /// avvicinata ancora (lo scarto della presentazione la tirerebbe 11 cm verso la faccia): si annulla quella parte.</summary>
        public bool RealHold;

        static Vector3 RealHoldPresent(Anim a)
        {
            if (a.weapon == null) return Vector3.zero;
            foreach (var wk in a.weapon) if (wk.pull > 0f) return new Vector3(0.03f, 0.005f, 0.11f);
            return Vector3.zero;
        }

        /// <summary>Dove il punto di lavoro dell'arma (un terzo della canna) deve stare, nello spazio della camera, mentre la sinistra ci
        /// lavora sopra: 44 cm davanti all'occhio, 11 sotto e quasi al centro (cosi' castello e mano restano in vista). Al fianco un
        /// fucile sta a 62 cm e la sua canna arriva a oltre un metro: un braccio umano (spalla a 19 cm di lato e 24 sotto l'occhio,
        /// 30 + 27 cm) non ci arriva.</summary>
        public static readonly Vector3 ReachTarget = new Vector3(-0.04f, -0.11f, 0.44f);
        Vector3 reach;
        /// <summary>L'avvicinamento dell'arma per il gesto in corso (spazio della camera), pesato dalle chiavi con pull.</summary>
        public Vector3 Reach => reach;

        Vector3 ReachFor(Anim a)
        {
            if (oneHanded || a.weapon == null) return Vector3.zero;
            foreach (var wk in a.weapon)
            {
                if (wk.pull <= 0f) continue;
                // dove finirebbe il punto di lavoro con lo scarto scritto nel gesto (Weapon somma pos e moltiplica la rotazione)
                Vector3 at = viewRoot.localPosition + wk.pos + viewRoot.localRotation * (Quaternion.Euler(wk.rot) * surface.AlongBarrel(0.3f));
                Vector3 r = ReachTarget + a.reachOffset - at;
                r.z = Mathf.Min(0f, r.z);           // solo verso chi guarda: un'arma corta resta dov'e'
                return Vector3.ClampMagnitude(r, 0.45f);
            }
            return Vector3.zero;
        }

        public void Cancel()
        {
            if (anim == null) return;
            var cb = onEvent;
            anim = null; onEvent = null; external = false;
            ShowFree(false);
            RestoreRest();
            cb?.Invoke("annullata");
        }

        void ShowFree(bool on)
        {
            // con la presa di sostegno naturale la mano articolata resta sempre (il guanto modellato per la spalla finta non torna)
            bool show = on || NaturalRest;
            if (show != freeShown)
            {
                freeShown = show;
                free.SetVisible(show);
                if (wrapped != null) foreach (var r in wrapped) if (r != null && r is MeshRenderer) r.enabled = !show && !oneHanded;
            }
            if (armL != null) armL.ClampHand = on;
            // con la mano articolata in vista l'orlo della manica segue l'avambraccio (come la manichetta del guanto), non la mano
            if (armL != null) armL.FreeSleeve = show ? 1f : 0f;
            if (!on && armL != null) { armL.Free = 0f; armL.PoleWeight = 0f; armL.ShoulderOffset = Vector3.zero; armL.PoleHint = armL.RestPole; }
        }

        /// <summary>Dopo armL.Solve (il gomito e' al suo posto e la mano e' stata riportata nei campi articolari): la manichetta del guanto
        /// si allinea all'avambraccio.</summary>
        public void AfterArmSolve()
        {
            if (!freeShown) return;
            free.UpdateCuff(armL != null ? armL.LastForearmDirW : Vector3.zero);
        }

        /// <summary>Presa di sostegno naturale (presa realistica, armi a due mani): il guanto sinistro di ogni arma e' modellato per la
        /// spalla finta (avambraccio che arriva da sinistra, perpendicolare all'arma: mano sotto l'astina col pollice lungo la canna e le
        /// dita di traverso). Con un braccio vero il gomito pende e l'avambraccio sale da sotto: quella mano chiederebbe 120-180 gradi
        /// di rotazione dell'avambraccio (il limite umano e' ~85). Qui la mano articolata prende il suo posto per sempre, come la tiene un
        /// tiratore: palmo sotto l'astina, mano in diagonale avanti a destra (l'astina attraversa il palmo dalla base dell'indice al
        /// tallone), dita che salgono sul lato destro, pollice lungo il lato sinistro, polso dietro e sotto. Diventa la posa di riposo di
        /// mano_s (anche per i gesti, che partono e tornano qui). Restituisce la nuova posizione di riposo di mano_s nello spazio
        /// dell'arma (Weapon la usa come handLRest per le ricariche).</summary>
        public bool NaturalRest { get; private set; }

        public Vector3 SetNaturalRest()
        {
            Transform parent = handL.parent;
            // il palmo della presa modellata, nello spazio dell'arma: si tiene il punto lungo l'astina
            Vector3 oldPalm = viewRoot.InverseTransformPoint(parent.TransformPoint(handLRestPos) + (parent.rotation * handLRestRot) * palmRelPos);
            float z = oldPalm.z;
            // l'astina gira attorno all'asse della canna (la quota della bocca); il palmo modellato le stava a fianco: la sua distanza
            // dall'asse da' il raggio dell'astina piu' lo spessore del palmo. Il fondo dei riquadri qui non serve: sotto l'astina c'e'
            // spesso un pezzo che scende fino al caricatore
            float boreY = rig.MuzzleLocal.y;
            float r = Mathf.Clamp(new Vector2(oldPalm.x, oldPalm.y - boreY).magnitude, 0.02f, 0.06f);
            Vector3 c = new Vector3(0.004f, boreY - r + 0.004f, z);
            Vector3 F = new Vector3(0.62f, 0.18f, 0.76f).normalized;                                   // dita: avanti a destra, un poco in su
            Vector3 D = Vector3.ProjectOnPlane(new Vector3(-0.55f, -0.83f, 0f), F).normalized;         // dorso: in basso a sinistra
            Quaternion rotW = Quaternion.LookRotation(F, D);
            HandPose pose = HandPose.Lerp(HandPoses.Grip, HandPoses.Fist, 0.12f);
            Vector3 wristW = c - rotW * free.ContactInPalm(pose, (int)Contact.Palm);
            Quaternion hq = viewRoot.rotation * rotW * Quaternion.Inverse(palmRelRot);
            handL.rotation = hq;
            handL.position = viewRoot.TransformPoint(wristW) - hq * palmRelPos;
            handLRestPos = handL.localPosition;
            handLRestRot = handL.localRotation;
            gripPose = pose;
            NaturalRest = true;
            free.Tremor = 0f;
            free.Apply(pose);
            ShowFree(false);
            return viewRoot.InverseTransformPoint(handL.position);
        }

        float idleT, regripAt = 3f, drumAt = 11f;

        /// <summary>A riposo (presa di sostegno naturale) la sinistra e' viva: ogni 4-8 secondi si riassesta sull'astina (le dita si
        /// allentano un poco e tornano a stringere in mezzo secondo), a ogni colpo stringe un attimo (shot 0..1, il contraccolpo) e ogni
        /// 10-18 secondi senza sparare tamburella sull'astina: le dita si alzano e ricadono dal mignolo all'indice, due volte.</summary>
        public void Idle(float dt, float shot)
        {
            if (anim != null || !NaturalRest) return;
            idleT += dt;
            float loosen = 0f, ph = idleT - regripAt;
            if (ph >= 0f)
            {
                loosen = Mathf.Sin(Mathf.Clamp01(ph / 0.55f) * Mathf.PI) * 0.32f;
                if (ph > 0.55f) regripAt = idleT + UnityEngine.Random.Range(4f, 8f);
            }
            var p = HandPose.Lerp(gripPose, HandPoses.Relaxed, loosen);
            if (shot > 0.01f)
            {
                p = HandPose.Lerp(p, HandPoses.Fist, 0.12f * Mathf.Clamp01(shot));
                drumAt = Mathf.Max(drumAt, idleT + 6f);
            }
            float dph = idleT - drumAt;
            if (dph >= 0f)
            {
                const float Beat = 0.20f, Lag = 0.075f, Gap = 0.42f;
                if (dph > Gap + 3f * Lag + Beat) drumAt = idleT + UnityEngine.Random.Range(10f, 18f);
                else
                    for (int f = 0; f < 4; f++)
                    {
                        float lift = 0f;
                        for (int beat = 0; beat < 2; beat++)
                        {
                            float x = (dph - beat * Gap - (3 - f) * Lag) / Beat;
                            if (x > 0f && x < 1f) lift = Mathf.Max(lift, Mathf.Sin(x * Mathf.PI));
                        }
                        if (lift > 0f) p.SetFinger(f, p.Spread(f), p.Mcp(f) - 30f * lift, p.Pip(f) - 20f * lift, p.Dip(f) - 8f * lift);
                    }
            }
            free.Apply(p, dt);
        }

        void RestoreRest()
        {
            handL.localPosition = handLRestPos;
            handL.localRotation = handLRestRot;
            free.Tremor = 0f;
            free.Apply(gripPose);
        }

        // ------------------------------------------------------------------ valutazione
        static float EaseK(float u, int e)
        {
            u = Mathf.Clamp01(u);
            switch (e)
            {
                case 1: return u * u * u;                                  // scatto: parte piano e arriva di colpo
                case 2: return 1f - (1f - u) * (1f - u) * (1f - u);        // arrivo morbido
                case 3: return u;
                default: return u * u * (3f - 2f * u);
            }
        }

        // i punti delle parti che si muovono, nel riferimento della parte (InitParts)
        readonly Dictionary<Part, (Transform t, Vector3 local)> anchors = new Dictionary<Part, (Transform, Vector3)>();
        Vector3 magGrip;
        bool hasMagGrip;

        /// <summary>Dopo la costruzione: dove la mano afferra il caricatore (fianco sinistro, poco sotto il pozzetto; con il caricatore
        /// sopra la canna, sul dorso) e i punti delle parti mobili, presi dai riquadri dei loro pezzi a riposo.</summary>
        public void InitParts(bool magUp)
        {
            MagAxis = magUp ? Vector3.up : Vector3.down;
            anchors.Clear();
            if (rig.mag != null && WeaponSurface.PartBounds(viewRoot, rig.mag, out var mb))
            {
                float top = Mathf.Min(mb.max.y, surface.Bottom(mb.center.z));
                // il palmo sul davanti del caricatore (poco sotto il pozzetto), un filo a sinistra del centro: le dita girano sul fianco
                magGrip = magUp ? new Vector3(mb.center.x, mb.max.y + 0.004f, mb.center.z)
                                : new Vector3(mb.center.x - 0.006f, Mathf.Lerp(mb.min.y, Mathf.Max(mb.min.y + 0.02f, top), 0.45f), mb.max.z + 0.004f);
                hasMagGrip = true;
            }
            void Add(Part p, Transform t, Func<Bounds, Vector3> pick)
            {
                if (t == null || !WeaponSurface.PartBounds(viewRoot, t, out var b)) return;
                anchors[p] = (t, t.InverseTransformPoint(viewRoot.TransformPoint(pick(b))));
            }
            Add(Part.Charge, rig.charge, b => new Vector3(b.center.x, b.max.y, b.min.z));
            Add(Part.SlideRear, rig.slide, b => new Vector3(b.center.x, b.max.y, Mathf.Lerp(b.min.z, b.max.z, 0.18f)));
            Add(Part.Cover, rig.cover, b => new Vector3(b.center.x, b.max.y, Mathf.Lerp(b.min.z, b.max.z, 0.15f)));
            Add(Part.Breech, rig.barrels, b => new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.max.y, 0.6f), b.min.z));
            Add(Part.Forend, rig.barrels, b => new Vector3(b.center.x, b.min.y, Mathf.Lerp(b.min.z, b.max.z, 0.28f)));
            Add(Part.Cylinder, rig.cyl, b => b.center);
        }

        Vector3 PartLocal(Part p, float barrel)
        {
            if (anchors.TryGetValue(p, out var an) && an.t != null) return viewRoot.InverseTransformPoint(an.t.TransformPoint(an.local));
            switch (p)
            {
                case Part.MagGrip: return hasMagGrip ? magGrip : surface.Below(0.10f) + Vector3.left * 0.02f;
                case Part.Port: return surface.Below(surface.Receiver.z + 0.03f);
                case Part.Charge: return surface.TopPoint(Mathf.Max(0.0f, surface.Receiver.z - 0.08f));
                case Part.SlideRear: return surface.TopPoint(Mathf.Max(0.0f, surface.Receiver.z - 0.04f));
                case Part.Cover: case Part.Breech: return surface.TopPoint(surface.Receiver.z);
                case Part.Forend: return surface.Below(surface.Receiver.z + 0.18f);
                case Part.Cylinder: return surface.Receiver;
                case Part.Muzzle: return rig.MuzzleLocal;
                case Part.Sight: return rig.AdsLocal;
                case Part.Mag: return rig.mag != null ? viewRoot.InverseTransformPoint(rig.mag.position) : surface.Below(0.10f);
                case Part.MagBase: return surface.MagBase;
                case Part.Eject: return rig.eject != null ? viewRoot.InverseTransformPoint(rig.eject.position) : surface.TopPoint(0.08f);
                case Part.GripL: return rig.gripL != null ? viewRoot.InverseTransformPoint(rig.gripL.position) : surface.Below(0.25f);
                case Part.Receiver: return surface.Receiver;
                case Part.Barrel: return surface.AlongBarrel(barrel);
                case Part.LeftSide: return surface.LeftAlong(barrel);
                case Part.Under: return surface.UnderAlong(barrel);
                default: return Vector3.zero;
            }
        }

        /// <summary>Il palmo a riposo (presa sull'astina) nello spazio della camera.</summary>
        void RestPalmCam(out Vector3 p, out Quaternion q)
        {
            Transform parent = handL.parent;
            Vector3 hp = parent.TransformPoint(handLRestPos);
            Quaternion hq = parent.rotation * handLRestRot;
            Vector3 pw = hp + hq * palmRelPos;
            Quaternion qw = hq * palmRelRot;
            p = cam.InverseTransformPoint(pw);
            q = Quaternion.Inverse(cam.rotation) * qw;
        }

        static readonly Vector3 BelowPos = new Vector3(-0.30f, -0.62f, 0.26f);

        /// <summary>Una chiave nello spazio della camera: polso (origine del palmo) e rotazione del palmo.</summary>
        void KeyCam(in Key k, out Vector3 wrist, out Quaternion rot)
        {
            switch (k.space)
            {
                case Space.Grip:
                    // la presa a riposo, spostata di 'pos' (spazio dell'arma): la mano che si stacca o si riavvicina all'astina
                    RestPalmCam(out wrist, out rot);
                    if (k.pos.sqrMagnitude > 0f) wrist += viewRoot.localRotation * k.pos;
                    return;
                case Space.Below:
                    rot = Quaternion.LookRotation(new Vector3(0.15f, 0.85f, 0.4f).normalized, new Vector3(-0.8f, 0f, -0.6f));
                    wrist = BelowPos;
                    return;
            }
            Vector3 f = k.fingers.sqrMagnitude > 1e-6f ? k.fingers.normalized : Vector3.forward;
            Vector3 u = Vector3.ProjectOnPlane(k.dorsal.sqrMagnitude > 1e-6f ? k.dorsal : Vector3.up, f);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.ProjectOnPlane(Vector3.up, f);
            Quaternion r = Quaternion.LookRotation(f, u.normalized);
            Vector3 c = k.pos;
            if (k.space == Space.Weapon)
            {
                if (k.part != Part.None) c += PartLocal(k.part, k.barrel);
                Quaternion vq = viewRoot.localRotation;
                c = viewRoot.localPosition + vq * c;
                r = vq * r;
            }
            rot = r;
            // dal punto di contatto al polso, con la posa della chiave (cinematica diretta della mano libera)
            Vector3 off = free.ContactInPalm(k.pose, (int)k.contact);
            wrist = c - rot * off;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Scarto dell'arma (spazio della camera) per le chiavi dell'arma; Weapon lo somma alla sua posa.</summary>
        public void WeaponOffset(out Vector3 pos, out Vector3 rot)
        {
            pos = Vector3.zero; rot = Vector3.zero;
            if (anim == null || anim.weapon == null || anim.weapon.Length == 0) return;
            var w = anim.weapon;
            float t = time, pull;
            int i = 0;
            while (i < w.Length && t > w[i].t) i++;
            if (i == 0) { pos = w[0].pos; rot = w[0].rot; pull = w[0].pull; }
            else if (i >= w.Length) { var l = w[w.Length - 1]; pos = l.pos; rot = l.rot; pull = l.pull; }
            else
            {
                float u = EaseK((t - w[i - 1].t) / Mathf.Max(1e-4f, w[i].t - w[i - 1].t), w[i].ease);
                pos = Vector3.LerpUnclamped(w[i - 1].pos, w[i].pos, u);
                rot = Vector3.LerpUnclamped(w[i - 1].rot, w[i].rot, u);
                pull = Mathf.LerpUnclamped(w[i - 1].pull, w[i].pull, u);
            }
            pos += reach * pull;
        }

        /// <summary>Dopo AnimateParts e prima di armL.Solve: avanza l'animazione, mette mano_s (e la mano libera con lei) dove dice la
        /// curva, piega le dita, regola braccio e gomito, lancia gli eventi.</summary>
        public void Tick(float dt)
        {
            if (anim == null) return;
            float speed = Mathf.Max(0.2f, anim.speed);
            if (!external) time += dt * speed;
            var ks = anim.keys;
            if (!freeShown)
            {
                ShowFree(true);
                // il primo fotogramma: la mano libera parte dalla presa (o da fuori campo) con la posa che combacia
                free.Apply(ks[0].gripPose ? gripPose : ks[0].pose);
            }
            float t = Mathf.Min(time, anim.duration);
            int i1 = ks.Length - 1;
            for (int i = 1; i < ks.Length; i++) if (t <= ks[i].t) { i1 = i; break; }
            int i0 = Mathf.Max(0, i1 - 1);
            var k0 = ks[i0]; var k1 = ks[i1];
            float u = EaseK((t - k0.t) / Mathf.Max(1e-4f, k1.t - k0.t), k1.ease);
            KeyCam(k0, out Vector3 w0, out Quaternion r0);
            KeyCam(k1, out Vector3 w1, out Quaternion r1);
            Vector3 wm, wp;
            if (i0 > 0) KeyCam(ks[i0 - 1], out wm, out _); else wm = w0 + (w0 - w1);
            if (i1 < ks.Length - 1) KeyCam(ks[i1 + 1], out wp, out _); else wp = w1 + (w1 - w0);
            Vector3 wrist = CatmullRom(wm, w0, w1, wp, u);
            Quaternion rot = Quaternion.Slerp(r0, r1, u);
            HandPose p0 = k0.gripPose ? gripPose : k0.pose, p1 = k1.gripPose ? gripPose : k1.pose;
            var pose = HandPose.Lerp(p0, p1, u);
            LastPose = pose;
            curPalmCam = wrist; curPalmRotCam = rot;
            // il palmo nel mondo -> mano_s
            Vector3 pw = cam.TransformPoint(wrist);
            Quaternion qw = cam.rotation * rot;
            Quaternion hq = qw * Quaternion.Inverse(palmRelRot);
            handL.rotation = hq;
            handL.position = pw - hq * palmRelPos;
            free.Tremor = Mathf.Lerp(k0.tremor, k1.tremor, u);
            free.Apply(pose, dt);
            if (armL != null)
            {
                float fr = Mathf.Lerp(k0.gripPose ? 0f : Mathf.Max(0.01f, k0.free), k1.gripPose ? 0f : Mathf.Max(0.01f, k1.free), u);
                armL.Free = fr;
                Vector3 pole = Vector3.Lerp(k0.pole, k1.pole, u);
                if (pole.sqrMagnitude > 1e-4f) { armL.PoleHint = pole.normalized; armL.PoleWeight = 1f; }
                else { armL.PoleHint = armL.RestPole; armL.PoleWeight = 0f; }
                armL.ShoulderOffset = Vector3.Lerp(k0.shoulder, k1.shoulder, u);
            }
            // eventi
            if (anim.events != null)
                while (nextEvent < anim.events.Length && anim.events[nextEvent].t <= time)
                {
                    var id = anim.events[nextEvent].id;
                    nextEvent++;
                    onEvent?.Invoke(id);
                    if (anim == null) return;          // l'evento ha annullato
                }
            if (time >= anim.duration)
            {
                var cb = onEvent;
                anim = null; onEvent = null; external = false;
                ShowFree(false);
                RestoreRest();
                cb?.Invoke("fine");
            }
        }

        /// <summary>Punti utili agli effetti, nel mondo: il centro del palmo, la punta di un dito (0..4).</summary>
        public Vector3 PalmWorld => free.PalmPoint;
        public Vector3 TipWorld(int f) => free.Fingertip(f);
        public Vector3 PalmNormal => free.PalmNormal;
        /// <summary>Un punto di una parte dell'arma nel mondo (per gli effetti del sigillo).</summary>
        public Vector3 PartWorld(Part p, float barrel = 0.5f) => viewRoot.TransformPoint(PartLocal(p, barrel));
        public Transform ViewRoot => viewRoot;
    }

    /// <summary>La sagoma dell'arma dai riquadri dei suoi pezzi (le mesh delle armi non sono leggibili, i loro bounds si'): castello,
    /// canna dal castello alla bocca, sopra e sotto. Serve a mettere la mano sull'arma e non dentro o a mezz'aria.</summary>
    public sealed class WeaponSurface
    {
        readonly List<Bounds> boxes = new List<Bounds>();
        readonly Vector3 muzzle, ads;
        public readonly Vector3 Receiver, MagBase;
        readonly float zBack, zFront;

        public WeaponSurface(Transform viewRoot, WeaponRig rig)
        {
            muzzle = rig.MuzzleLocal; ads = rig.AdsLocal;
            if (rig.renderers != null)
                foreach (var r in rig.renderers)
                {
                    if (r == null) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var b = mf.sharedMesh.bounds;
                    var m = viewRoot.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    var bb = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
                    for (int i = 0; i < 8; i++)
                        bb.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    if (bb.size.magnitude < 0.004f || bb.size.magnitude > 2.5f) continue;
                    boxes.Add(bb);
                }
            zBack = 0.02f; zFront = Mathf.Max(0.15f, muzzle.z);
            float rz = Mathf.Clamp(Mathf.Lerp(zBack, zFront, 0.18f), 0.06f, 0.30f);
            Receiver = new Vector3(0f, (Top(rz) + Bottom(rz)) * 0.5f, rz);
            Vector3 mag = rig.mag != null ? viewRoot.InverseTransformPoint(rig.mag.position) : new Vector3(0f, Bottom(0.12f), 0.12f);
            float magLow = float.MaxValue;
            if (rig.mag != null)
                foreach (var r in rig.mag.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var m = viewRoot.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    var b = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                        magLow = Mathf.Min(magLow, m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))).y);
                }
            MagBase = magLow < float.MaxValue ? new Vector3(mag.x, magLow, mag.z) : mag + Vector3.down * 0.10f;
        }

        /// <summary>Il riquadro dei pezzi di una parte dell'arma nello spazio dell'arma (dalle mesh, che non sono leggibili).</summary>
        public static bool PartBounds(Transform viewRoot, Transform part, out Bounds bb)
        {
            bb = default;
            bool any = false;
            if (part == null) return false;
            foreach (var r in part.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                var m = viewRoot.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { bb = new Bounds(c, Vector3.zero); any = true; }
                    else bb.Encapsulate(c);
                }
            }
            return any;
        }

        bool Covers(Bounds b, float z) => z >= b.min.z - 0.005f && z <= b.max.z + 0.005f;
        bool NearBore(Bounds b) => b.max.y > muzzle.y - 0.02f && b.min.y < muzzle.y + 0.03f;

        /// <summary>Quota del dorso dell'arma alla profondita' z (senza le ottiche): dove poggia un palmo che scorre sopra.</summary>
        public float Top(float z)
        {
            float top = float.MinValue;
            foreach (var b in boxes) if (Covers(b, z) && NearBore(b)) top = Mathf.Max(top, b.max.y);
            return top > float.MinValue ? top : muzzle.y + 0.02f;
        }

        public float Bottom(float z)
        {
            float bot = float.MaxValue;
            foreach (var b in boxes) if (Covers(b, z) && NearBore(b)) bot = Mathf.Min(bot, b.min.y);
            return bot < float.MaxValue ? bot : muzzle.y - 0.03f;
        }

        /// <summary>Il fianco sinistro (x minima) alla profondita' z e alla quota y.</summary>
        public float Left(float z)
        {
            float left = float.MaxValue;
            foreach (var b in boxes) if (Covers(b, z) && NearBore(b)) left = Mathf.Min(left, b.min.x);
            return left < float.MaxValue ? left : -0.02f;
        }

        public Vector3 TopPoint(float z) => new Vector3(0f, Top(z), z);
        /// <summary>Un punto del fianco sinistro, a 3/5 dell'altezza: 0 al castello, 1 verso la bocca (fermato come AlongBarrel).</summary>
        public Vector3 LeftAlong(float t)
        {
            float z = Mathf.Lerp(Receiver.z, BarrelEnd, Mathf.Clamp01(t));
            return LeftPoint(z, 0.6f);
        }

        /// <summary>Fin dove la mano scorre lungo la canna: la bocca, ma al massimo 36 cm oltre il castello (e 62 cm dall'origine
        /// dell'arma): la spalla resta dov'e' e piu' in la' il braccio non arriva.</summary>
        float BarrelEnd => Mathf.Min(Mathf.Min(muzzle.z - 0.02f, 0.62f), Receiver.z + 0.36f);
        public Vector3 Below(float z) => new Vector3(0f, Bottom(z), z);
        public Vector3 LeftPoint(float z, float yFrac = 0.5f) => new Vector3(Left(z), Mathf.Lerp(Bottom(z), Top(z), yFrac), z);

        /// <summary>Un punto sul sotto dell'arma davanti al caricatore: 0 poco oltre il pozzetto, 1 verso la bocca (o fin dove arriva il
        /// braccio, BarrelEnd). Dove un palmo rivolto in su scorre sotto l'arma (i sigilli scaldano l'arma dal basso). Sulle armi corte
        /// il tratto si accorcia ma resta di una decina di centimetri.</summary>
        public Vector3 UnderAlong(float t)
        {
            float end = BarrelEnd;
            float start = Mathf.Min(Receiver.z + 0.10f, end - 0.08f);
            float z = Mathf.Lerp(start, end, Mathf.Clamp01(t));
            return new Vector3(0f, Bottom(z), z);
        }

        /// <summary>Un punto sul dorso della canna: 0 sopra il castello, 1 alla bocca (o fin dove arriva il braccio, BarrelEnd).</summary>
        public Vector3 AlongBarrel(float t)
        {
            float z = Mathf.Lerp(Receiver.z, BarrelEnd, Mathf.Clamp01(t));
            return new Vector3(0f, Top(z), z);
        }
    }
}

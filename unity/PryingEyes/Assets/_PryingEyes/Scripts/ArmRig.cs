using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Braccio in prima persona con il gomito vero (modelli/hotel/genera_braccia.py). La manica dell'FBX diventa una
    /// SkinnedMeshRenderer su quattro ossa (mano, avambraccio, torsione dell'avambraccio, braccio) e a ogni fotogramma una cinematica
    /// inversa a due ossa porta il gomito dove deve stare perche' l'avambraccio arrivi al polso. Il guanto resta rigido con la mano
    /// (figlio di mano_X). Senza i giunti polso_X / gomito_X / spalla_X (mani vecchie) Build torna null e la manica resta rigida.
    ///
    /// Biomeccanica (2026-11-16):
    ///  - torsione: la pronazione e la supinazione non avvengono al polso ma lungo l'avambraccio (radio che ruota sull'ulna). Un osso di
    ///    torsione a meta' avambraccio prende meta' del rollio della mano rispetto al gomito e la manica lo distribuisce dal polsino al
    ///    gomito: la mano che si gira col palmo in su non strizza piu' il polsino come una caramella;
    ///  - campi articolari: il gomito si chiude fino a 28 gradi tra braccio e avambraccio (flessione massima ~150) e non va mai in
    ///    iperestensione (176 al massimo);
    ///  - comodita' del polso: con il braccio libero (Free) si cerca, tra le rotazioni del gomito attorno all'asse spalla-polso, quella
    ///    che tiene la mano entro ~32 gradi dall'avambraccio, con una molla perche' il gomito non scatti; un suggerimento (PoleHint)
    ///    dice dove preferirebbe stare (giu' e in fuori di norma, alto quando la mano passa davanti al viso);
    ///  - spalla: col braccio libero la spalla segue il corpo (la camera) anche in profondita' e si sposta di ShoulderOffset (la spalla
    ///    che si protende quando il braccio si allunga).
    /// A riposo (Free = 0) il braccio e' esattamente quello di prima. Funziona anche in edit-mode (ArmiShots).</summary>
    public sealed class ArmRig
    {
        readonly Transform root, hand, handBone, foreBone, upperBone, twistBone;
        /// <summary>L'osso dell'orlo della manica rispetto a quello dell'avambraccio nella posa di legame.</summary>
        readonly Quaternion handBoneRelFore;
        /// <summary>La pelle e' la manica neutra (FreeSleeve sempre 1: l'orlo segue l'avambraccio) e la lunghezza dell'omero con cui e'
        /// legata (la scala dell'osso del braccio si misura su questa).</summary>
        bool neutral;
        float bindUpper;
        public bool NeutralSleeve => neutral;
        readonly Vector3 shoulder, wristInHand, poleRest, normalRest, handRefLocal, handAxisLocal;
        readonly float lu, lf;
        public readonly SkinnedMeshRenderer Skin;
        Transform body;
        Vector3 shoulderBody;

        /// <summary>In mira la spalla scende di tanto (metri, riferimento del corpo).</summary>
        public float AdsDrop;
        /// <summary>0 = braccio dell'arma (com'era), 1 = braccio libero: spalla col corpo, gomito che cerca il polso comodo.</summary>
        public float Free;
        /// <summary>Dove il gomito preferirebbe puntare (riferimento del corpo, cioe' della camera) e quanto conta (0..1).</summary>
        public Vector3 PoleHint = new Vector3(-0.55f, -0.83f, -0.1f);
        /// <summary>Il polo di riposo, a cui i gesti tornano (OffHand): giu' e in fuori per il braccio dell'arma; con la presa realistica
        /// quasi solo giu' (i gomiti pendono).</summary>
        public Vector3 RestPole = new Vector3(-0.55f, -0.83f, -0.1f);
        public float PoleWeight;
        /// <summary>Spostamento della spalla (riferimento del corpo) col braccio libero: avanti quando il braccio si allunga.</summary>
        public Vector3 ShoulderOffset;
        /// <summary>Il corpo che porta la spalla (riferimento del corpo, metri): respiro, passo, contraccolpo (Weapon.UpdatePose). Le
        /// spalle si muovono con il busto e l'arma con le mani: il gomito e il polso vivono di quello che c'e' in mezzo.</summary>
        public Vector3 BodyMotion;
        /// <summary>Limite di comodita' del polso (gradi tra l'asse della mano e quello dell'avambraccio) che la ricerca del gomito rispetta.</summary>
        public float WristLimit = 32f;
        /// <summary>Presa realistica (2026-11-16): il braccio e' sempre un braccio vero, anche sull'arma: spalla del corpo (con la
        /// rotazione del busto, Stance), omero di 30 cm, gomito diretto dall'anatomia (PoleHint) e polso comodo. Senza, a riposo il
        /// braccio dell'arma resta quello virtuale di prima (spalla finta e omero lungo, per le armi tenute lontane).</summary>
        public bool Body;
        /// <summary>Rotazione del busto del tiratore (gradi): la spalla sinistra va avanti e la destra indietro attorno alla spina,
        /// come chi imbraccia un fucile (30-40) o tiene una pistola (5-15).</summary>
        public float Stance;
        public const float MinElbowDeg = 28f, MaxElbowDeg = 176f;
        /// <summary>Il braccio libero e' un braccio vero: omero di 30 cm (quello dell'arma e' virtuale, 0,35-0,75 m, perche' il modello in
        /// prima persona sta piu' avanti di una presa vera) e spalla dove sta una spalla, 19 cm di lato, 24 sotto e 3 dietro l'occhio.
        /// L'osso del braccio della manica si accorcia davvero (scala lungo l'osso): la manica resta attaccata alla spalla vera.</summary>
        public const float UpperReal = 0.30f, ShoulderSide = 0.19f, ShoulderDown = 0.24f, ShoulderBack = 0.03f, ProtractMax = 0.12f;
        readonly float side;
        float swivel, swivelVel;

        /// <summary>Spalla a riposo nel riferimento della radice delle mani (= quello dell'arma).</summary>
        public Vector3 ShoulderLocal => shoulder;
        /// <summary>L'osso di torsione dell'avambraccio: la manichetta della mano libera si appende qui.</summary>
        public Transform ForearmTwist => twistBone;
        /// <summary>L'osso dell'avambraccio (al gomito, guarda il polso).</summary>
        public Transform Forearm => foreBone;
        public float UpperLength => lu;
        public float ForeLength => lf;
        /// <summary>Angolo del gomito all'ultimo Solve (gradi tra braccio e avambraccio) e scarto del polso dall'avambraccio: per le prove.</summary>
        public float LastElbowDeg { get; private set; }
        public float LastWristDeg { get; private set; }
        public float LastRollDeg { get; private set; }
        /// <summary>Spalla, gomito e polso dell'ultima soluzione, nel mondo (per le prove e la vista di lato).</summary>
        /// <summary>Il sistema della mano nel riferimento di 'hand' (pollice e dita), quando e' noto (la mano sinistra, che ha il palmo
        /// della mano libera): allora polso e avambraccio si misurano come in anatomia e guidano la scelta del gomito. Zero = ignoto.</summary>
        public Vector3 ThumbLocal, FingersLocal;
        /// <summary>Misure anatomiche dell'ultima soluzione (gradi), se il sistema della mano e' noto: prono-supinazione dalla stretta di
        /// mano (pollice in su; limite umano ~85), flessione-estensione del polso (~80) e deviazione radiale-ulnare (~30).</summary>
        public float LastProSupDeg { get; private set; }
        public float LastFlexDeg { get; private set; }
        public float LastDevDeg { get; private set; }
        public bool HasHandFrame => ThumbLocal.sqrMagnitude > 0.5f && FingersLocal.sqrMagnitude > 0.5f;
        /// <summary>Durante i gesti della mano libera (OffHand): dopo aver scelto il gomito, la mano viene riportata dentro i campi
        /// articolari umani ruotandola attorno al polso, come fanno i limiti muscolari di un Humanoid di Unity: il gesto chiede una
        /// direzione, il braccio la da' finche' l'anatomia lo permette. Mai un polso girato di 180 gradi.</summary>
        public bool ClampHand;
        public const float ProSupMax = 85f, FlexMax = 75f, DevMax = 30f;
        /// <summary>1 = l'orlo della manica (gli ultimi 3-9 cm, legati all'osso della mano) segue l'avambraccio come nella posa di legame e
        /// la manica non si torce: e' il braccio della mano libera articolata, che ha la sua manichetta sull'avambraccio (HandRig) e si
        /// piega al polso. 0 = l'orlo segue la mano (il guanto modellato, rigido con la mano, ha la manichetta da coprire).</summary>
        public float FreeSleeve;
        public Vector3 LastShoulderW { get; private set; }
        public Vector3 LastElbowW { get; private set; }
        public Vector3 LastWristW { get; private set; }
        /// <summary>Il verso dell'avambraccio (dal gomito al polso) nel mondo: la manichetta del guanto si allinea qui (HandRig.UpdateCuff).</summary>
        public Vector3 LastForearmDirW { get; private set; }

        public void AnchorTo(Transform bodyT, Vector3 shoulderInBody) { body = bodyT; shoulderBody = shoulderInBody; }

        static readonly Dictionary<Mesh, Mesh> skinned = new Dictionary<Mesh, Mesh>();

        ArmRig(Transform root, Transform hand, Transform handBone, Transform fore, Transform upper, Transform twist, Vector3 shoulder, Vector3 wristInHand,
               Vector3 poleRest, Vector3 normalRest, Vector3 handRefLocal, float lu, float lf, SkinnedMeshRenderer skin, float side)
        {
            this.side = side;
            this.root = root; this.hand = hand; this.handBone = handBone; foreBone = fore; upperBone = upper; twistBone = twist; this.shoulder = shoulder;
            handBoneRelFore = Quaternion.Inverse(fore.rotation) * handBone.rotation;
            this.wristInHand = wristInHand; this.poleRest = poleRest; this.normalRest = normalRest; this.handRefLocal = handRefLocal;
            handAxisLocal = (-wristInHand).sqrMagnitude > 1e-8f ? (-wristInHand).normalized : Vector3.forward;
            this.lu = lu; this.lf = lf; Skin = skin;
        }

        static Transform Child(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static GameObject neutralPrefab;
        static bool neutralTried;

        /// <summary>root = radice delle mani (riferimento dell'arma), hand = Empty mano_d / mano_s, side = "d" / "s". neutral: la pelle e'
        /// la manica neutra (Resources/ManicaLibera, genera_braccia.py -- manica), una sola per tutte le armi, modellata con le proporzioni
        /// vere (omero 30 cm, gomito a 140 gradi) e specchiata per la destra: con le braccia vere (Body) si piega poco e resta una manica;
        /// la manica di ogni arma, modellata sul braccio virtuale (omero fino a 75 cm), col braccio vero veniva stirata e girata molto.
        /// I giunti dell'arma (polso, gomito, spalla) restano: il polso e' dove la mano tiene l'arma.</summary>
        public static ArmRig Build(Transform root, Transform hand, string side, bool neutral = false)
        {
            if (root == null || hand == null) return null;
            Transform wj = Child(hand, "polso_" + side), ej = Child(hand, "gomito_" + side), sj = Child(hand, "spalla_" + side);
            if (wj == null || ej == null || sj == null) return null;
            MeshFilter sleeve = null;
            foreach (var mf in hand.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.EndsWith("_manica")) { sleeve = mf; break; }
            if (sleeve == null || sleeve.sharedMesh == null) return null;

            Vector3 W0 = root.InverseTransformPoint(wj.position), E0 = root.InverseTransformPoint(ej.position), S0 = root.InverseTransformPoint(sj.position);
            float lf = (E0 - W0).magnitude, lu = (S0 - E0).magnitude;
            if (lf < 0.05f || lu < 0.05f) return null;
            Vector3 n0 = Vector3.Cross(E0 - S0, W0 - E0);
            if (n0.sqrMagnitude < 1e-10f) n0 = Vector3.Cross(E0 - S0, Vector3.up);
            n0.Normalize();
            Vector3 dir0 = (W0 - S0).normalized;
            Vector3 pole = E0 - (S0 + dir0 * Vector3.Dot(E0 - S0, dir0));
            pole = pole.sqrMagnitude > 1e-10f ? pole.normalized : n0;
            int layer = sleeve.gameObject.layer;

            // la pelle: la manica neutra (se c'e' e se richiesta) o quella dell'arma; giunti di legame Ws, Es, Ss
            MeshFilter skinMf = sleeve;
            GameObject neutralInst = null;
            Vector3 Ws = W0, Es = E0, Ss = S0;
            if (neutral)
            {
                if (!neutralTried) { neutralTried = true; neutralPrefab = Resources.Load<GameObject>("ManicaLibera"); }
                if (neutralPrefab != null)
                {
                    neutralInst = Object.Instantiate(neutralPrefab, root);
                    neutralInst.name = "ManicaNeutra_" + side;
                    foreach (var t in neutralInst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                    neutralInst.transform.localPosition = Vector3.zero;
                    neutralInst.transform.localRotation = Quaternion.identity;
                    neutralInst.transform.localScale = Vector3.one;
                    if (side == "d") HandMirror.Mirror(neutralInst.transform);
                    Transform nw = Child(neutralInst.transform, "polso_s"), ne = Child(neutralInst.transform, "gomito_s"), ns = Child(neutralInst.transform, "spalla_s");
                    var nmf = neutralInst.GetComponentInChildren<MeshFilter>(true);
                    if (nw != null && ne != null && ns != null && nmf != null && nmf.sharedMesh != null && nmf.sharedMesh.isReadable)
                    {
                        skinMf = nmf;
                        Ws = root.InverseTransformPoint(nw.position); Es = root.InverseTransformPoint(ne.position); Ss = root.InverseTransformPoint(ns.position);
                    }
                    else { Object.Destroy(neutralInst); neutralInst = null; }
                }
            }
            bool isNeutral = neutralInst != null;
            var src = skinMf.sharedMesh;
            if (!src.isReadable) { Debug.LogWarning("[PryingEyes] braccio: manica non leggibile, resta rigida: " + src.name); return null; }
            float lfs = (Es - Ws).magnitude, lus = (Ss - Es).magnitude;
            Vector3 ns0 = Vector3.Cross(Es - Ss, Ws - Es);
            if (ns0.sqrMagnitude < 1e-10f) ns0 = Vector3.Cross(Es - Ss, Vector3.up);
            ns0.Normalize();

            var hb = new GameObject("osso_mano_" + side).transform;
            if (isNeutral)
            {
                // legata al polso della manica neutra, nel verso del suo avambraccio; dopo il legame va sul polso dell'arma (sotto)
                hb.SetParent(root, false);
                hb.localPosition = Ws;
                hb.localRotation = Quaternion.LookRotation(Ws - Es, ns0);
            }
            else
            {
                hb.SetParent(hand, false);
                hb.position = wj.position;
                hb.localRotation = Quaternion.identity;
            }
            var fb = new GameObject("osso_avambraccio_" + side).transform;
            fb.SetParent(root, false);
            fb.localPosition = Es;
            fb.localRotation = Quaternion.LookRotation(Ws - Es, ns0);
            var tb = new GameObject("osso_torsione_" + side).transform;
            tb.SetParent(root, false);
            tb.localPosition = Es;
            tb.localRotation = fb.localRotation;
            var ub = new GameObject("osso_braccio_" + side).transform;
            ub.SetParent(root, false);
            ub.localPosition = Ss;
            ub.localRotation = Quaternion.LookRotation(Es - Ss, ns0);
            foreach (var t in new[] { hb, fb, tb, ub }) t.gameObject.layer = layer;

            // la pelle: copia della mesh con pesi e pose di legame, una volta per mesh (cambiare arma non la ricalcola). Messa sulla
            // mesh d'origine nel riferimento della radice, con le matrici (non con lossyScale, che e' un'approssimazione)
            var go = new GameObject(skinMf.name + "_pelle");
            go.layer = layer;
            go.transform.SetParent(root, false);
            PlaceLocal(go.transform, root.worldToLocalMatrix * skinMf.transform.localToWorldMatrix);
            var bones = new[] { hb, fb, ub, tb };
            if (!skinned.TryGetValue(src, out var mesh) || mesh == null)
            {
                mesh = Object.Instantiate(src);
                mesh.name = src.name + "_pelle";
                var toRoot = root.worldToLocalMatrix * skinMf.transform.localToWorldMatrix;
                var vs = src.vertices;
                var bw = new BoneWeight[vs.Length];
                for (int i = 0; i < vs.Length; i++)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(vs[i]);
                    float t1 = Mathf.Clamp01(Vector3.Dot(p - Ws, Es - Ws) / (lfs * lfs));
                    float d1 = (p - (Ws + (Es - Ws) * t1)).sqrMagnitude;
                    float t2 = Mathf.Clamp01(Vector3.Dot(p - Es, Ss - Es) / (lus * lus));
                    float d2 = (p - (Es + (Ss - Es) * t2)).sqrMagnitude;
                    float s = d1 <= d2 ? t1 * lfs : lfs + t2 * lus;
                    // il polsino e le pieghe sopra la manichetta seguono la mano; al gomito avambraccio e braccio si mescolano
                    float wh = 1f - Smooth(0.030f, 0.090f, s);
                    float u = Smooth(lfs - 0.055f, lfs + 0.055f, s);
                    float wu = (1f - wh) * u, wf = (1f - wh) * (1f - u);
                    // la torsione: vicino al polso l'avambraccio ruota con la mano, vicino al gomito no (radio sull'ulna)
                    float ts = 1f - Smooth(0.18f * lfs, 0.92f * lfs, s);
                    float wt = wf * ts;
                    wf -= wt;
                    bw[i] = new BoneWeight { boneIndex0 = 0, weight0 = wh, boneIndex1 = 1, weight1 = wf, boneIndex2 = 2, weight2 = wu, boneIndex3 = 3, weight3 = wt };
                }
                mesh.boneWeights = bw;
                skinned[src] = mesh;
            }
            var bind = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++) bind[b] = bones[b].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = fb;
            smr.quality = SkinQuality.Bone4;
            smr.updateWhenOffscreen = true;
            var mr = sleeve.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                smr.sharedMaterials = mr.sharedMaterials;
                smr.shadowCastingMode = mr.shadowCastingMode;
                smr.receiveShadows = mr.receiveShadows;
                smr.renderingLayerMask = mr.renderingLayerMask;
            }
            else smr.shadowCastingMode = ShadowCastingMode.Off;
            sleeve.gameObject.SetActive(false);
            if (isNeutral)
            {
                // legata: l'osso dell'orlo va sul polso vero (dove la mano tiene l'arma) e ci resta, figlio della mano
                Matrix4x4 rel = hand.worldToLocalMatrix * hb.localToWorldMatrix;
                hb.SetParent(hand, false);
                PlaceLocal(hb, rel);
                hb.localPosition = hand.InverseTransformPoint(wj.position);
                Object.Destroy(neutralInst);
            }
            Vector3 wih = hand.InverseTransformPoint(wj.position);
            // riferimento del rollio: il 'su' dell'avambraccio di legame visto dalla mano (rollio 0 nella posa modellata)
            Vector3 refLocal = hand.InverseTransformDirection(root.TransformDirection(n0));
            var rig = new ArmRig(root, hand, hb, fb, ub, tb, S0, wih, pole, n0, refLocal, lu, lf, smr, side == "s" ? -1f : 1f);
            rig.neutral = isNeutral;
            rig.bindUpper = isNeutral ? lus : lu;
            if (isNeutral) rig.FreeSleeve = 1f;
            return rig;
        }

        static Vector3 ElbowAt(Vector3 S, Vector3 dir, float a, float h, Vector3 pole) => S + dir * a + pole * h;

        /// <summary>Posizione, rotazione e scala locali da una matrice relativa al genitore (senza taglio): esatte anche quando qualche
        /// antenato ha una scala, dove lossyScale approssima.</summary>
        public static void PlaceLocal(Transform t, Matrix4x4 rel)
        {
            Vector3 c0 = rel.GetColumn(0), c1 = rel.GetColumn(1), c2 = rel.GetColumn(2);
            Vector3 sc = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
            if (Vector3.Dot(Vector3.Cross(c0, c1), c2) < 0f) sc.x = -sc.x;
            t.localPosition = rel.GetColumn(3);
            t.localRotation = Quaternion.LookRotation(c2 / Mathf.Max(1e-8f, sc.z), c1 / Mathf.Max(1e-8f, sc.y));
            t.localScale = sc;
        }

        /// <summary>Misure anatomiche di polso e avambraccio per spalla S, gomito E, polso W e il sistema della mano (pollice, dita):
        /// la prono-supinazione e' l'angolo del pollice attorno all'avambraccio rispetto alla stretta di mano (pollice perpendicolare al
        /// piano del gomito, dalla parte opposta all'omero... cioe' in su col gomito in basso); flessione ed estensione sono la rotazione
        /// delle dita attorno all'asse del pollice, la deviazione quella attorno al dorso.</summary>
        public static void Anatomy(Vector3 S, Vector3 E, Vector3 W, Vector3 thumb, Vector3 fingers, float side, out float proSup, out float flex, out float dev)
        {
            proSup = flex = dev = 0f;
            Vector3 fa = W - E, n = Vector3.Cross(E - S, W - E);
            if (fa.sqrMagnitude < 1e-10f || n.sqrMagnitude < 1e-12f || thumb.sqrMagnitude < 1e-8f || fingers.sqrMagnitude < 1e-8f) return;
            fa.Normalize();
            Vector3 medial = side * n.normalized;
            Vector3 neutralThumb = -side * Vector3.Cross(fa, medial);
            Vector3 tp = Vector3.ProjectOnPlane(thumb, fa);
            if (tp.sqrMagnitude > 1e-8f) proSup = Vector3.SignedAngle(neutralThumb, tp, fa);
            Vector3 th = thumb.normalized, dor = Vector3.Cross(fingers, thumb).normalized;
            Vector3 f1 = Vector3.ProjectOnPlane(fingers, th), a1 = Vector3.ProjectOnPlane(fa, th);
            if (f1.sqrMagnitude > 1e-8f && a1.sqrMagnitude > 1e-8f) flex = Vector3.SignedAngle(a1, f1, th);
            Vector3 f2 = Vector3.ProjectOnPlane(fingers, dor), a2 = Vector3.ProjectOnPlane(fa, dor);
            if (f2.sqrMagnitude > 1e-8f && a2.sqrMagnitude > 1e-8f) dev = Vector3.SignedAngle(a2, f2, dor);
        }

        /// <summary>Riporta la mano dentro i campi articolari ruotandola attorno al polso (spazio della radice): prima la
        /// prono-supinazione attorno all'avambraccio, poi la flessione attorno all'asse del pollice, poi la deviazione attorno al dorso.</summary>
        void ClampToJoints(Vector3 S, Vector3 E, Vector3 W)
        {
            Vector3 fa = (W - E).normalized;
            if (fa.sqrMagnitude < 0.5f) return;
            Vector3 th = root.InverseTransformDirection(hand.TransformDirection(ThumbLocal));
            Vector3 fi = root.InverseTransformDirection(hand.TransformDirection(FingersLocal));
            Quaternion r = Quaternion.identity;
            // i tre assi si toccano (correggere flessione o deviazione sposta un po' il pollice attorno all'avambraccio): si ripete
            for (int it = 0; it < 4; it++)
            {
                Anatomy(S, E, W, th, fi, side, out float ps, out float fl, out float dv);
                float e1 = ps - Mathf.Clamp(ps, -ProSupMax, ProSupMax);
                if (Mathf.Abs(e1) > 0.05f) { var q = Quaternion.AngleAxis(-e1, fa); r = q * r; th = q * th; fi = q * fi; }
                Anatomy(S, E, W, th, fi, side, out ps, out fl, out dv);
                float e2 = fl - Mathf.Clamp(fl, -FlexMax, FlexMax);
                if (Mathf.Abs(e2) > 0.05f) { var q = Quaternion.AngleAxis(-e2, th.normalized); r = q * r; th = q * th; fi = q * fi; }
                Anatomy(S, E, W, th, fi, side, out ps, out fl, out dv);
                float e3 = dv - Mathf.Clamp(dv, -DevMax, DevMax);
                if (Mathf.Abs(e3) > 0.05f) { var q = Quaternion.AngleAxis(-e3, Vector3.Cross(fi, th).normalized); r = q * r; th = q * th; fi = q * fi; }
                if (Mathf.Abs(e1) + Mathf.Abs(e2) + Mathf.Abs(e3) < 0.5f) break;
            }
            if (Quaternion.Angle(r, Quaternion.identity) < 0.05f) return;
            Vector3 pivot = root.TransformPoint(W);
            Quaternion rw = root.rotation * r * Quaternion.Inverse(root.rotation);
            hand.rotation = rw * hand.rotation;
            hand.position = pivot + rw * (hand.position - pivot);
        }

        /// <summary>Quanto una soluzione esce dai campi articolari umani (gradi al quadrato, 0 = dentro): prono-supinazione oltre 75,
        /// flessione-estensione oltre 65, deviazione oltre 25 (con un po' di margine sotto i limiti veri, 85/80/30).</summary>
        static float AnatomyCost(float proSup, float flex, float dev)
        {
            float a = Mathf.Max(0f, Mathf.Abs(proSup) - 75f), b = Mathf.Max(0f, Mathf.Abs(flex) - 65f), c = Mathf.Max(0f, Mathf.Abs(dev) - 25f);
            return a * a + b * b + 2f * c * c;
        }

        /// <summary>La spalla del corpo nel riferimento della camera: 19 cm di lato, 24 sotto e 3 dietro l'occhio, ruotata attorno alla
        /// spina (asse verticale 3 cm dietro l'occhio) di 'stance' gradi: la sinistra avanti, la destra indietro.</summary>
        public static Vector3 BodyShoulder(float side, float stance)
        {
            float t = stance * Mathf.Deg2Rad;
            return new Vector3(side * ShoulderSide * Mathf.Cos(t), -ShoulderDown, -ShoulderBack - side * ShoulderSide * Mathf.Sin(t));
        }

        /// <summary>Gomito a due ossa verso il polso attuale (riferimento della radice delle mani). Va chiamata dopo che la mano e'
        /// stata messa al suo posto nel fotogramma (Weapon.AnimateParts). dt serve alla molla del gomito col braccio libero.</summary>
        public void Solve(float ads = 0f, float dt = 0f)
        {
            if (hand == null || foreBone == null) return;
            Vector3 W = root.InverseTransformPoint(hand.TransformPoint(wristInHand));
            Vector3 S = shoulder;
            // presa realistica: spalla, omero e gomito sono sempre quelli del corpo; Free governa solo la ricerca del polso comodo
            float free = Body ? 1f : Mathf.Clamp01(Free);
            // col braccio libero l'omero torna lungo come un omero vero
            float lu = Mathf.Lerp(this.lu, UpperReal, free);
            if (body != null)
            {
                // di lato e in altezza la spalla sta col corpo; in profondita' resta con l'arma: in mira l'arma viene a 30 cm
                // dall'occhio (al fianco sta a 60) e con la spalla ferma il braccio virtuale, lungo per la posa al fianco, si
                // ripiegherebbe col gomito davanti alla mano. Col braccio libero la spalla e' tutta del corpo.
                Vector3 sb = root.InverseTransformPoint(body.TransformPoint(shoulderBody + Vector3.down * (AdsDrop * Mathf.Clamp01(ads))));
                S = new Vector3(sb.x, sb.y, shoulder.z);
                if (free > 0f)
                {
                    Vector3 sb2 = BodyShoulder(side, Stance) + ShoulderOffset + BodyMotion;
                    Vector3 sf = root.InverseTransformPoint(body.TransformPoint(sb2));
                    // la spalla si protende verso la mano quando il braccio non ci arriva (fino a 12 cm, la scapola che scivola)
                    Vector3 toW = W - sf;
                    float over = toW.magnitude - (lu + lf) * 0.985f;
                    if (over > 0f) sf += toW.normalized * Mathf.Min(over, ProtractMax);
                    S = Vector3.Lerp(S, sf, free);
                }
            }
            Vector3 sw = W - S;
            float d = sw.magnitude;
            if (d < 1e-5f) return;
            Vector3 dir = sw / d;
            // campi articolari del gomito: tra 28 e 176 gradi fra braccio e avambraccio; fuori portata la spalla (fuori campo) scivola
            float dmax = Mathf.Sqrt(lu * lu + lf * lf - 2f * lu * lf * Mathf.Cos(MaxElbowDeg * Mathf.Deg2Rad));
            float dmin = Mathf.Sqrt(lu * lu + lf * lf - 2f * lu * lf * Mathf.Cos(MinElbowDeg * Mathf.Deg2Rad));
            if (d > dmax) { S = W - dir * dmax; d = dmax; }
            else if (d < dmin) { S = W - dir * dmin; d = dmin; }
            float a = (lu * lu - lf * lf + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, lu * lu - a * a));
            Vector3 pr = poleRest;
            if (free > 0f && body != null)
            {
                Vector3 hint = root.InverseTransformDirection(body.TransformDirection(PoleHint.normalized));
                // il polo modellato e' quello del braccio virtuale: col corpo vero comanda l'anatomia
                pr = Body ? hint.normalized : Vector3.Slerp(poleRest, hint, free * Mathf.Clamp01(0.5f + PoleWeight)).normalized;
            }
            Vector3 pole = pr - dir * Vector3.Dot(pr, dir);
            pole = pole.sqrMagnitude > 1e-8f ? pole.normalized : normalRest;
            if (free > 0.01f)
            {
                // il gomito ruota attorno all'asse spalla-polso fino a tenere il polso comodo (poca rotazione se non serve)
                Vector3 axisH = root.InverseTransformDirection(hand.TransformDirection(handAxisLocal));
                Vector3 refH = root.InverseTransformDirection(hand.TransformDirection(handRefLocal));
                bool anat = HasHandFrame;
                Vector3 thumbR = anat ? root.InverseTransformDirection(hand.TransformDirection(ThumbLocal)) : Vector3.zero;
                Vector3 fingR = anat ? root.InverseTransformDirection(hand.TransformDirection(FingersLocal)) : Vector3.zero;
                float best = swivel, bestCost = float.MaxValue;
                // presa realistica: sull'arma il gomito ruota poco (20 gradi) e resta giu' anche se il polso si piega un po' (il guanto e'
                // modellato sull'impugnatura: allinearci l'avambraccio alzerebbe il gomito di lato, ad ala di pollo); nei gesti fino a 70
                // a riposo sull'arma 40 gradi (con la mano articolata il costo e' anatomico: il gomito cerca il polso comodo senza
                // alzarsi ad ala di pollo, lo tiene giu' il costo dell'altezza), nei gesti fino a 70
                float range = Body ? Mathf.Lerp(40f, 70f, Mathf.Clamp01(Free)) : 70f;
                // la spalla nel riferimento del corpo (la camera): per tenere giu' il gomito
                Vector3 sBody = BodyShoulder(side, Stance) + ShoulderOffset;
                for (int k = -7; k <= 7; k++)
                {
                    float phi = k * range / 7f;
                    Vector3 pk = Quaternion.AngleAxis(phi, dir) * pole;
                    Vector3 Ek = ElbowAt(S, dir, a, h, pk);
                    Vector3 fk = (W - Ek).normalized;
                    float dev = Vector3.Angle(axisH, fk);
                    float over = Mathf.Max(0f, dev - WristLimit);
                    // torsione dell'avambraccio con questo gomito (il 'su' del piano del gomito contro quello della mano): pronazione e
                    // supinazione arrivano a ~85 gradi, oltre 75 il costo sale come quello del polso
                    Vector3 nk = Vector3.Cross(Ek - S, W - Ek), rk = Vector3.ProjectOnPlane(refH, fk);
                    float tw = nk.sqrMagnitude > 1e-10f && rk.sqrMagnitude > 1e-8f ? Mathf.Max(0f, Mathf.Abs(Vector3.SignedAngle(nk, rk, fk)) - 75f) : 0f;
                    float joints = over * over + 0.6f * tw * tw;
                    if (anat)
                    {
                        // con il sistema della mano noto contano i campi articolari veri, non lo scarto dalla posa modellata
                        Anatomy(S, Ek, W, thumbR, fingR, side, out float ps, out float fl, out float dv);
                        joints = AnatomyCost(ps, fl, dv);
                    }
                    if (body != null)
                    {
                        // il gomito sta giu': piu' alto di 8 cm sotto la spalla, o a meno di 27 cm dall'occhio, costa. Un braccio vero non
                        // alza il gomito davanti alla faccia per girare la mano: gira l'avambraccio finche' puo', il resto lo da' il polso
                        // (ClampToJoints). Prima il gomito saliva all'altezza degli occhi nelle spinte (fulmine, onda) e il braccio
                        // copriva mezza vista
                        Vector3 eb = body.InverseTransformPoint(root.TransformPoint(Ek));
                        float hi = Mathf.Max(0f, eb.y - (sBody.y - 0.08f));
                        float near = Mathf.Max(0f, 0.27f - eb.magnitude);
                        joints += 15000f * hi * hi + 20000f * near * near;
                    }
                    float cost = joints + 0.012f * phi * phi + 0.03f * (phi - swivel) * (phi - swivel);
                    if (cost < bestCost) { bestCost = cost; best = phi; }
                }
                if (dt > 0f)
                {
                    // molla smorzata critica integrata esatta: il gomito arriva in ~0,15 s senza oscillare e resta stabile con qualunque
                    // fotogramma (l'integrazione esplicita di prima esplodeva oltre ~0,13 s: dopo uno scatto il gomito schizzava davanti
                    // agli occhi e la manica si stirava in un telo)
                    const float w = 15.5f;
                    float x0 = swivel - best, tmp = (swivelVel + w * x0) * dt, e = Mathf.Exp(-w * dt);
                    swivelVel = (swivelVel - w * tmp) * e;
                    swivel = best + (x0 + tmp) * e;
                }
                else swivel = best;
                pole = Quaternion.AngleAxis(swivel * free, dir) * pole;
            }
            else { swivel = 0f; swivelVel = 0f; }
            Vector3 E = ElbowAt(S, dir, a, h, pole);
            if (ClampHand && HasHandFrame) ClampToJoints(S, E, W);
            Vector3 n = Vector3.Cross(E - S, W - E);
            n = n.sqrMagnitude > 1e-10f ? n.normalized : normalRest;
            foreBone.localPosition = E;
            foreBone.localRotation = Quaternion.LookRotation(W - E, n);
            upperBone.localPosition = S;
            upperBone.localRotation = Quaternion.LookRotation(E - S, n);
            // la manica del braccio si accorcia con l'omero (la sua pelle e' legata all'omero virtuale dell'arma)
            float uk = Mathf.Max(0.05f, (E - S).magnitude / Mathf.Max(0.05f, bindUpper > 0f ? bindUpper : this.lu));
            upperBone.localScale = Mathf.Abs(uk - 1f) < 0.002f ? Vector3.one : new Vector3(1f, 1f, uk);
            // torsione: il rollio della mano attorno all'avambraccio, rispetto al 'su' del gomito; meta' all'osso di meta' avambraccio
            Vector3 fa = (W - E).normalized;
            Vector3 foreUp = Vector3.ProjectOnPlane(foreBone.localRotation * Vector3.up, fa);
            Vector3 handRef = Vector3.ProjectOnPlane(root.InverseTransformDirection(hand.TransformDirection(handRefLocal)), fa);
            float roll = foreUp.sqrMagnitude > 1e-8f && handRef.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(foreUp, handRef, fa) : 0f;
            LastRollDeg = roll;
            float fs = neutral ? 1f : Mathf.Clamp01(FreeSleeve);
            if (twistBone != null)
            {
                twistBone.localPosition = E;
                twistBone.localRotation = Quaternion.AngleAxis(roll * 0.5f * (1f - fs), fa) * foreBone.localRotation;
            }
            if (handBone != null)
            {
                // l'orlo della manica: con la mano (0) o con l'avambraccio, come nella posa di legame (1)
                if (fs > 0.001f) handBone.rotation = Quaternion.Slerp(hand.rotation, foreBone.rotation * handBoneRelFore, fs);
                else handBone.localRotation = Quaternion.identity;
            }
            LastElbowDeg = Vector3.Angle(S - E, W - E);
            LastShoulderW = root.TransformPoint(S); LastElbowW = root.TransformPoint(E); LastWristW = root.TransformPoint(W);
            LastForearmDirW = root.TransformDirection(fa);
            if (HasHandFrame)
            {
                Anatomy(S, E, W, root.InverseTransformDirection(hand.TransformDirection(ThumbLocal)), root.InverseTransformDirection(hand.TransformDirection(FingersLocal)),
                        side, out float ps2, out float fl2, out float dv2);
                LastProSupDeg = ps2; LastFlexDeg = fl2; LastDevDeg = dv2;
            }
            LastWristDeg = Vector3.Angle(root.InverseTransformDirection(hand.TransformDirection(handAxisLocal)), fa);
        }
    }
}

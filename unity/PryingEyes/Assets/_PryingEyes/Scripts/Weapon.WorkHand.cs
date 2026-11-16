using UnityEngine;

namespace PryingEyes
{
    // La destra che manovra l'arma (2026-11-16, presa realistica). Con l'otturatore girevole, dopo ogni colpo (e a vuoto dopo la
    // ricarica) la destra lascia l'impugnatura, prende il pomello della manetta fra pollice e indice, lo segue mentre alza, tira,
    // spinge e abbassa (la manetta la muove AnimateCycle, coi tempi di qui), poi torna sull'impugnatura e sul grilletto. Con la leva
    // le dita stanno nell'anello: la mano gira attorno al perno della leva con lei (un po' meno: il palmo resta sul collo del calcio)
    // e le dita si aprono un poco. Mano_d si sposta prima di armR.Solve: braccio e gomito la seguono.
    public partial class Weapon
    {
        // tempi della manovra dell'otturatore con la mano (frazioni del ciclo)
        const float BoltReach = 0.16f, BoltUp0 = 0.16f, BoltUp1 = 0.28f, BoltBack0 = 0.28f, BoltBack1 = 0.50f,
                    BoltFwd0 = 0.54f, BoltFwd1 = 0.72f, BoltDown0 = 0.74f, BoltDown1 = 0.84f, BoltLeave = 0.86f;
        // la mano sul pomello (spazio dell'arma): dita a sinistra e in avanti sopra l'otturatore, dorso in su a destra
        static readonly Vector3 KnobF = new Vector3(-0.55f, -0.15f, 0.82f), KnobD = new Vector3(0.35f, 0.92f, 0.12f);

        bool workHand;                      // c'e' una destra articolata da far lavorare (otturatore o leva)
        bool boltHand;                      // otturatore girevole con il pomello trovato
        Vector3 knobLocal;                  // il pomello nel riferimento della manetta
        Vector3 handRRestPos;               // mano_d a riposo (locale)
        Quaternion handRRestRot;
        Vector3 palmRelPosR;                // l'osso del palmo della destra nel riferimento di mano_d
        Quaternion palmRelRotR;
        Vector3 pinchInPalm;                // il pizzico (polpastrelli di pollice e indice) nel riferimento del palmo
        Quaternion leverRestRel;            // la leva a riposo nel riferimento dell'arma
        float workW;                        // quanto la destra sta lavorando (0 sull'impugnatura, 1 sul pomello)
        Vector3 breakHinge;                 // la cerniera della doppietta (spazio dell'arma)
        bool hasBreakHinge;
        float leverK;                       // quanto la leva e' aperta (0..1)

        /// <summary>Alla fine di AddHands: misure della destra e del pomello a riposo.</summary>
        void InitWorkHand(WeaponDef d)
        {
            workHand = boltHand = false;
            workW = leverK = 0f;
            hasBreakHinge = false;
            if (rig != null && viewRoot != null && rig.barrels != null && WeaponSurface.PartBounds(viewRoot, rig.barrels, out var bb))
            {
                breakHinge = new Vector3(bb.center.x, bb.min.y, bb.min.z + 0.015f);
                hasBreakHinge = true;
            }
            if (gripHand == null || handR == null || rig == null || viewRoot == null || !RealHold(d)) return;
            handRRestPos = handR.localPosition;
            handRRestRot = handR.localRotation;
            var palm = gripHand.Hand.Palm;
            palmRelPosR = handR.InverseTransformPoint(palm.position);
            palmRelRotR = Quaternion.Inverse(handR.rotation) * palm.rotation;
            pinchInPalm = (gripHand.Hand.ContactInPalm(HandPoses.Pinch, 2) + gripHand.Hand.ContactInPalm(HandPoses.Pinch, 4)) * 0.5f;
            if (rig.boltHandle != null && WeaponSurface.PartBounds(viewRoot, rig.boltHandle, out var b))
            {
                // il pomello: l'estremo destro della manetta, un poco sotto la meta'
                Vector3 kv = new Vector3(b.max.x - 0.006f, Mathf.Lerp(b.min.y, b.max.y, 0.35f), b.center.z);
                knobLocal = rig.boltHandle.InverseTransformPoint(viewRoot.TransformPoint(kv));
                boltHand = true;
            }
            if (rig.lever != null) leverRestRel = Quaternion.Inverse(viewRoot.rotation) * rig.lever.rotation;
            workHand = boltHand || rig.lever != null;
        }

        bool BoltByHand => boltHand && !OldReload;

        /// <summary>Prima di armR.Solve: mano_d sul pomello o con la leva (dopo AnimateParts, con le parti gia' mosse).</summary>
        void WorkHandTick()
        {
            workW = leverK = 0f;
            if (!workHand || handR == null) return;
            handR.localPosition = handRRestPos;
            handR.localRotation = handRRestRot;
            float c = cycleAnim;
            if (c < 0f) return;
            var m = Mode;
            if (m == FireMode.Bolt && BoltByHand)
            {
                float w = Mathf.Clamp01(Band(c, 0f, BoltReach) - Band(c, BoltLeave, 1f));
                w = Ease(w);
                workW = w;
                if (w <= 0f) return;
                Quaternion palmGrip = handR.rotation * palmRelRotR;
                Vector3 palmPosGrip = handR.TransformPoint(palmRelPosR);
                Quaternion palmKnob = viewRoot.rotation * Quaternion.LookRotation(KnobF, KnobD);
                Vector3 knobW = rig.boltHandle.TransformPoint(knobLocal);
                Vector3 palmPosKnob = knobW - palmKnob * pinchInPalm;
                // la mano sale ad arco dall'impugnatura al pomello
                Vector3 palmPos = Vector3.Lerp(palmPosGrip, palmPosKnob, w) + viewRoot.up * (0.025f * Mathf.Sin(w * Mathf.PI));
                Quaternion palmRot = Quaternion.Slerp(palmGrip, palmKnob, w);
                Quaternion hq = palmRot * Quaternion.Inverse(palmRelRotR);
                handR.rotation = hq;
                handR.position = palmPos - hq * palmRelPosR;
            }
            else if (m == FireMode.Lever && rig.lever != null)
            {
                // la leva gira attorno al suo perno: la mano con lei (60%), le dita nell'anello
                Quaternion now = Quaternion.Inverse(viewRoot.rotation) * rig.lever.rotation;
                Quaternion delta = now * Quaternion.Inverse(leverRestRel);
                delta.ToAngleAxis(out float ang, out Vector3 axis);
                if (ang > 180f) ang -= 360f;
                if (Mathf.Abs(ang) < 0.1f) return;
                leverK = Mathf.Clamp01(Mathf.Abs(ang) / 55f);
                Quaternion part = viewRoot.rotation * Quaternion.AngleAxis(ang * 0.6f, axis) * Quaternion.Inverse(viewRoot.rotation);
                Vector3 pivot = rig.lever.position;
                handR.position = pivot + part * (handR.position - pivot);
                handR.rotation = part * handR.rotation;
            }
        }

        /// <summary>Dopo gripHand.Pose: le dita del lavoro (pizzico sul pomello, dita aperte nell'anello della leva).</summary>
        void WorkHandFingers()
        {
            if (gripHand == null) return;
            if (workW > 0.001f) gripHand.Hand.Apply(HandPose.Lerp(gripHand.Hand.Current, HandPoses.Pinch, workW));
            else if (leverK > 0.001f) gripHand.Hand.Apply(HandPose.Lerp(gripHand.Hand.Current, HandPoses.Relaxed, 0.35f * leverK));
        }
    }
}

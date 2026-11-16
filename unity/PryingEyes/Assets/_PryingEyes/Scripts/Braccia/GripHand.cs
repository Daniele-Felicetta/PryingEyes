using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>La mano destra articolata (2026-11-16): la stessa mano libera della sinistra (HandRig), specchiata, messa esattamente
    /// dove stava il guanto destro modellato arma per arma (GripPoses, presa_destra.json) e con le dita nella stessa presa attorno
    /// all'impugnatura. Il guanto modellato (dita a salsicciotti fuse nel palmo, la paranocche ovale) si spegne: le due mani sono lo
    /// stesso guanto. L'indice va dalla presa (disteso lungo il telaio) alla posa sul grilletto con Pose(verso, scatto), come faceva
    /// TriggerFinger con l'indice modellato; la manichetta segue l'avambraccio (AfterArmSolve). Senza la presa dell'arma nella tabella
    /// Build torna null e resta il guanto modellato.</summary>
    public sealed class GripHand
    {
        readonly HandRig hand;
        readonly ArmRig arm;
        readonly HandPose grip, trigger;
        public HandRig Hand => hand;
        public IReadOnlyList<Renderer> Renderers => hand.Renderers;
        /// <summary>I pezzi del guanto modellato che la mano articolata ha spento.</summary>
        public readonly Renderer[] Replaced;

        GripHand(HandRig hand, ArmRig arm, HandPose grip, HandPose trigger, Renderer[] replaced)
        {
            this.hand = hand; this.arm = arm; this.grip = grip; this.trigger = trigger; Replaced = replaced;
        }

        static GameObject prefab;
        static bool tried;

        static Transform Child(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        public static GripHand Build(Transform handR, ArmRig armR, WeaponDef d, Renderer[] modelled)
        {
            if (handR == null || d == null) return null;
            if (!tried) { tried = true; prefab = Resources.Load<GameObject>("ManoLibera"); }
            if (prefab == null) return null;
            if (!GripPoses.TryGet(d.id, handR, true, out var g)) return null;
            // il riferimento del palmo della presa modellata (in mano_d): polso, nocca del medio, verso del dorso
            Quaternion wrapRot = HandRig.PalmFrame(g.wrist, g.fingers[1][0], g.dorsal - g.palm);
            var inst = Object.Instantiate(prefab, handR);
            inst.name = "ManoDestraArticolata";
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = handR.gameObject.layer;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            // la mano libera e' una sinistra: specchiata e' una destra
            HandMirror.Mirror(inst.transform);
            Transform tw = Child(inst.transform, "L_polso"), tm = Child(inst.transform, "L_medio_1"), tp = Child(inst.transform, "L_palmo"), td = Child(inst.transform, "L_dorso");
            if (tw == null || tm == null || tp == null || td == null) { Object.Destroy(inst); return null; }
            Vector3 pPR = inst.transform.InverseTransformPoint(tw.position);
            Quaternion qPR = HandRig.PalmFrame(pPR, inst.transform.InverseTransformPoint(tm.position),
                                               inst.transform.InverseTransformPoint(td.position) - inst.transform.InverseTransformPoint(tp.position));
            Quaternion qR = wrapRot * Quaternion.Inverse(qPR);
            inst.transform.localRotation = qR;
            inst.transform.localPosition = g.wrist - qR * pPR;
            // i materiali del guanto modellato: la stessa mano
            Material glove = null, guard = null;
            if (modelled != null)
                foreach (var r in modelled)
                {
                    if (r == null || r.transform.IsChildOf(inst.transform)) continue;
                    if (glove == null && r.name.Contains("_guanto")) glove = r.sharedMaterial;
                    if (guard == null && r.name.Contains("_rinforzo")) guard = r.sharedMaterial;
                }
            var hand = HandRig.Build(inst.transform, glove, guard);
            if (hand == null) { Object.Destroy(inst); return null; }
            foreach (var r in hand.Renderers) { r.renderingLayerMask = Weapon.ViewLayer; r.shadowCastingMode = ShadowCastingMode.Off; }
            // la presa e la posa sul grilletto nel riferimento del palmo
            Quaternion inv = Quaternion.Inverse(wrapRot);
            var dita = new Vector3[4][];
            for (int f = 0; f < 4; f++)
            {
                dita[f] = new Vector3[4];
                for (int i = 0; i < 4; i++) dita[f][i] = inv * (g.fingers[f][i] - g.wrist);
            }
            var pol = new Vector3[4];
            for (int i = 0; i < 4; i++) pol[i] = inv * (g.thumb[i] - g.wrist);
            HandPose gp = hand.PoseFromJoints(dita, pol), trig = gp;
            if (g.trigger != null)
            {
                var dt = new Vector3[4][];
                for (int f = 0; f < 4; f++) dt[f] = dita[f];
                dt[0] = new[] { dita[0][0], inv * (g.trigger[0] - g.wrist), inv * (g.trigger[1] - g.wrist), inv * (g.trigger[2] - g.wrist) };
                var full = hand.PoseFromJoints(dt, pol);
                trig.SetFinger(0, full.Spread(0), full.Mcp(0), full.Pip(0), full.Dip(0));
            }
            hand.Apply(gp);
            hand.SetVisible(true);
            // il guanto modellato si spegne (le MeshRenderer; la pelle della manica e' una SkinnedMeshRenderer e resta)
            var replaced = new List<Renderer>();
            if (modelled != null)
                foreach (var r in modelled)
                    if (r != null && r is MeshRenderer && !r.transform.IsChildOf(inst.transform)) { r.enabled = false; replaced.Add(r); }
            // il sistema della mano (pollice, dita) nel riferimento di mano_d: l'anatomia del braccio destro si misura come la sinistra
            if (armR != null)
            {
                Quaternion prr = Quaternion.Inverse(handR.rotation) * hand.Palm.rotation;
                armR.ThumbLocal = prr * (Vector3.right * hand.ThumbSign);
                armR.FingersLocal = prr * Vector3.forward;
                armR.FreeSleeve = 1f;
            }
            return new GripHand(hand, armR, gp, trig, replaced.ToArray());
        }

        /// <summary>L'indice tra la presa (0) e il grilletto (1), piu' lo scatto del colpo (0..1: la falange si chiude ancora un poco) e
        /// la stretta del contraccolpo (0..1: le altre dita serrano l'impugnatura).</summary>
        public void Pose(float toTrigger, float squeeze, float tighten = 0f)
        {
            var p = HandPose.Lerp(grip, trigger, Mathf.Clamp01(toTrigger));
            if (squeeze > 0f) p.SetFinger(0, p.Spread(0), p.Mcp(0) + 5f * squeeze, p.Pip(0) + 12f * squeeze, p.Dip(0) + 7f * squeeze);
            if (tighten > 0.01f)
                for (int f = 1; f < 4; f++) p.SetFinger(f, p.Spread(f), p.Mcp(f) + 4f * tighten, p.Pip(f) + 6f * tighten, p.Dip(f) + 3f * tighten);
            hand.Apply(p);
        }

        /// <summary>Dopo armR.Solve: la manichetta del guanto lungo l'avambraccio.</summary>
        public void AfterArmSolve() => hand.UpdateCuff(arm != null ? arm.LastForearmDirW : Vector3.zero);
    }
}

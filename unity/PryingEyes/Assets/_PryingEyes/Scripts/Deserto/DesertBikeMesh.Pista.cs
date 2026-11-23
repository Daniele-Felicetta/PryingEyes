using PryingEyes.Pista;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le moto della pista nel deserto: il modello di Blender di PistaBikeMesh (la stessa moto, la stessa vernice e gli stessi numeri sulle targhe), senza il pilota della pista, messo nella stessa gerarchia
    /// di pezzi mobili di <see cref="Rig"/> cosi' che DesertBike la muova come la moto di casa (beccheggio, rollio, sterzo, ruote) e che i sopravvissuti ci stiano in sella (SurvivorBody) con le mani sulle sue manopole.
    /// Le braccia in prima persona (i guanti e gli avambracci) sono quelle della moto di casa, rimesse sulle manopole di questo modello.</summary>
    public static partial class DesertBikeMesh
    {
        // un guanto per stile e per lato (le manopole dipendono solo dallo stile): la mesh si fa una volta e si condivide
        static readonly Mesh[] pistaGloves = new Mesh[6];

        static Mesh PistaGlove(int style, int side, Vector3 grip)
        {
            int k = Mathf.Clamp(style, 0, 2) * 2 + side;
            if (pistaGloves[k] != null) return pistaGloves[k];
            // il pugno avvolge la manopola (che corre lungo x, anche nel sistema dello sterzo)
            var fist = new[] { grip + new Vector3(-0.055f, 0f, 0f), grip, grip + new Vector3(0.055f, 0f, 0f) };
            return pistaGloves[k] = DesertLathe.Tube(side == 0 ? "Moto guanto sinistro" : "Moto guanto destro", fist, new[] { 0.028f, 0.036f, 0.027f }, 8);
        }

        /// <summary>Costruisce una moto della pista sotto 'parent' (la radice: un oggetto a terra, girato come la rotta). Il disegno e' ingrandito o ridotto di <see cref="DesertBikeDef.scale"/> attorno alla radice.</summary>
        public static Rig BuildPista(Transform parent, DesertBikeDef def)
        {
            Ensure();
            var holder = new GameObject("Modello pista").transform;
            holder.SetParent(parent, false);
            holder.localScale = Vector3.one * def.scale;
            var pr = PistaBikeMesh.Build(holder, def.style, Livery.For(def.skin), false);
            var rig = new Rig
            {
                root = parent, body = pr.body, steer = pr.steer, steerRest = pr.steerRest, frontPivot = pr.frontPivot, rearPivot = pr.rearPivot,
                tailMat = pr.tailMat, lensMat = pr.lensMat, eye = def.eye,
            };
            foreach (var r in pr.screenParts) if (r != null) rig.screens.Add(r);
            foreach (var m in new[] { pr.paint, pr.paint2, pr.plate, pr.suit, pr.suit2, pr.helmet, pr.tailMat, pr.lensMat }) if (m != null) rig.ownedMats.Add(m);
            rig.ownedMats.AddRange(pr.extraMats);

            // le braccia in prima persona: i guanti seguono lo sterzo, gli avambracci vanno dal gomito (fermo nel corpo, sotto il bordo dello schermo) al pugno (Rig.PoseArms)
            rig.arms = new GameObject("Guanti").transform;
            rig.arms.SetParent(rig.steer, false);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Vector3 g = pr.grip[i];
                rig.glove[i] = Part(rig.arms, i == 0 ? "Guanto sinistro" : "Guanto destro", PistaGlove(def.style, i, g), kit.mGlove, false);
                rig.sleeve[i] = Part(rig.body, i == 0 ? "Avambraccio sinistro" : "Avambraccio destro", kit.sleeve, kit.mCloth, false);
                rig.handPt[i] = g;
                rig.elbowPt[i] = new Vector3(side * 0.36f, def.grip.y - 0.03f, -0.28f);
            }
            rig.ShowArms(false);

            // il faro: lo stesso cono della moto di casa, sul muso
            var lg = new GameObject("Luce del faro");
            lg.transform.SetParent(rig.body, false);
            lg.transform.localPosition = new Vector3(0f, def.style < 2 ? 0.96f : 1.04f, 0.66f);
            lg.transform.localRotation = Quaternion.Euler(3f, 0f, 0f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Spot;
            l.range = 55f; l.spotAngle = 68f; l.innerSpotAngle = 34f;
            l.color = new Color(1f, 0.93f, 0.80f);
            l.intensity = 105f;
            l.shadows = LightShadows.None;
            l.bounceIntensity = 0f;
            l.enabled = false;
            rig.headlight = l;
            return rig;
        }
    }
}

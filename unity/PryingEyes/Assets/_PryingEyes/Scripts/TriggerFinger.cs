using UnityEngine;

namespace PryingEyes
{
    /// <summary>L'indice destro articolato (modelli/hotel/genera_braccia.py): tre falangi con i perni indice_d_1..3 (la punta e'
    /// indice_d_4) e i punti della posa sul grilletto grilletto_d_2..4, misurati sul grilletto vero dell'arma. A riposo il dito sta
    /// disteso lungo il telaio (disciplina di tiro); quando si spara o si mira va sul grilletto, e a ogni colpo lo stringe un filo di
    /// piu'. Ogni falange ruota attorno al suo perno, in catena: la rotazione che porta la direzione di riposo su quella della posa,
    /// interpolata. Senza i perni (braccia vecchie) Build torna null.</summary>
    public sealed class TriggerFinger
    {
        readonly Transform hand;
        readonly Transform[] seg = new Transform[3];
        readonly Quaternion[] rest = new Quaternion[3];     // rotazioni di riposo nel riferimento della mano
        readonly Quaternion[] delta = new Quaternion[3];    // rotazioni (riferimento della mano) che portano ogni falange sulla posa
        public float Blend { get; private set; }

        TriggerFinger(Transform hand) { this.hand = hand; }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        public static TriggerFinger Build(Transform hand)
        {
            if (hand == null) return null;
            var j = new Transform[4];
            var g = new Transform[3];
            for (int i = 0; i < 4; i++) { j[i] = Find(hand, "indice_d_" + (i + 1)); if (j[i] == null) return null; }
            for (int i = 0; i < 3; i++) { g[i] = Find(hand, "grilletto_d_" + (i + 2)); if (g[i] == null) return null; }
            var f = new TriggerFinger(hand);
            Quaternion inv = Quaternion.Inverse(hand.rotation);
            Vector3 P(Transform t) => hand.InverseTransformPoint(t.position);
            var J = new Vector3[4];
            for (int i = 0; i < 4; i++) J[i] = P(j[i]);
            var G = new Vector3[4];
            G[0] = J[0];
            for (int i = 0; i < 3; i++) G[i + 1] = P(g[i]);
            Quaternion acc = Quaternion.identity;
            for (int i = 0; i < 3; i++)
            {
                f.seg[i] = j[i];
                f.rest[i] = inv * j[i].rotation;
                Vector3 from = acc * (J[i + 1] - J[i]), to = G[i + 1] - G[i];
                f.delta[i] = Quaternion.FromToRotation(from, to);
                acc = f.delta[i] * acc;
            }
            return f;
        }

        /// <summary>b = 0 disteso, 1 sul grilletto; 'squeeze' (0..1) stringe un po' oltre la posa (il colpo).</summary>
        public void Pose(float b, float squeeze = 0f)
        {
            if (hand == null || seg[0] == null) return;
            Blend = b;
            float k = Mathf.Clamp01(b) + squeeze * 0.12f;
            Quaternion acc = Quaternion.identity;
            for (int i = 0; i < 3; i++)
            {
                var q = Quaternion.SlerpUnclamped(Quaternion.identity, delta[i], k);
                acc = q * acc;
                seg[i].rotation = hand.rotation * (acc * rest[i]);
            }
        }
    }
}

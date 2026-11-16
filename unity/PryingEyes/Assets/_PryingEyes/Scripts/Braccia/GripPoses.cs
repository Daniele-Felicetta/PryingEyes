using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Le prese delle due mani arma per arma (Resources/ManiArmi/presa_sinistra.json e presa_destra.json, da genera_braccia.py
    /// -- pose): dove stanno polso, palmo, dorso e le articolazioni delle dita della sinistra avvolta all'astina e della destra avvolta
    /// all'impugnatura (piu', per la destra, l'indice nella posa sul grilletto), in coordinate d'autore relative all'Empty mano_s o
    /// mano_d. Servono alla mano libera (HandRig) per mettersi esattamente dove sta il guanto avvolto prima di staccarsi
    /// dall'arma, e per tornarci: cosi' il cambio tra le due mani non si vede.
    /// Gli assi d'autore (Blender) finiscono in quelli di Unity con l'importazione dell'FBX; invece di supporlo, qui si ricavano da
    /// polso, gomito e spalla, che sono anche Empty figli di mano_s nel prefab: tra le 48 permutazioni con segno si sceglie quella che
    /// porta i tre vettori d'autore sui tre di Unity.</summary>
    public static class GripPoses
    {
        [System.Serializable] sealed class Arma { public string id; public float[] polso, gomito, spalla, dita, pollice, palmo, dorso, grilletto; }
        [System.Serializable] sealed class Root { public int versione; public Arma[] armi; }

        /// <summary>Una presa nel riferimento locale di mano_s (Unity).</summary>
        public sealed class Grip
        {
            public Vector3 wrist, palm, dorsal;
            public readonly Vector3[][] fingers = new Vector3[4][];
            public readonly Vector3[] thumb = new Vector3[4];
            /// <summary>Destra: PIP, DIP e punta dell'indice nella posa sul grilletto (null senza).</summary>
            public Vector3[] trigger;
            public float calibrationError;
        }

        static Dictionary<string, Arma> table, tableD;

        static Dictionary<string, Arma> Load(bool right)
        {
            if (right) return tableD ??= Read("presa_destra");
            return table ??= Read("presa_sinistra");
        }

        static Dictionary<string, Arma> Read(string file)
        {
            var table = new Dictionary<string, Arma>();
            var ta = Resources.Load<TextAsset>("ManiArmi/" + file);
            if (ta == null) { Debug.LogWarning($"[PryingEyes] {file}.json mancante: la mano articolata parte da una presa generica"); return table; }
            try
            {
                var root = JsonUtility.FromJson<Root>(ta.text);
                if (root?.armi != null) foreach (var a in root.armi) if (a != null && !string.IsNullOrEmpty(a.id)) table[a.id] = a;
            }
            catch (System.Exception e) { Debug.LogWarning($"[PryingEyes] {file}.json illeggibile: " + e.Message); }
            return table;
        }

        static Vector3 V(float[] a, int i = 0) => a != null && a.Length >= i * 3 + 3 ? new Vector3(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]) : Vector3.zero;

        static Transform Child(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        /// <summary>La permutazione con segno che porta i vettori d'autore su quelli di Unity con l'errore minimo.</summary>
        static Matrix4x4 Calibrate(Vector3[] author, Vector3[] unity, out float err)
        {
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            Matrix4x4 best = Matrix4x4.identity; err = float.MaxValue;
            foreach (var p in perms)
                for (int s = 0; s < 8; s++)
                {
                    var m = Matrix4x4.zero;
                    for (int r = 0; r < 3; r++) m[r, p[r]] = ((s >> r) & 1) == 0 ? 1f : -1f;
                    m[3, 3] = 1f;
                    float e = 0f;
                    for (int i = 0; i < author.Length; i++) e += (m.MultiplyVector(author[i]) - unity[i]).sqrMagnitude;
                    if (e < err) { err = e; best = m; }
                }
            err = Mathf.Sqrt(err / Mathf.Max(1, author.Length));
            return best;
        }

        /// <summary>La presa sinistra dell'arma 'id' nel riferimento locale di manoS.</summary>
        public static bool TryGet(string id, Transform manoS, out Grip g) => TryGet(id, manoS, false, out g);

        /// <summary>La presa dell'arma 'id' (destra o sinistra) nel riferimento locale di mano (mano_d o mano_s). Falso se manca nella
        /// tabella o se la calibrazione non torna (oltre 1 cm: l'FBX e la tabella non vengono dalla stessa versione di genera_braccia).</summary>
        public static bool TryGet(string id, Transform mano, bool right, out Grip g)
        {
            g = null;
            var tab = Load(right);
            if (mano == null || !tab.TryGetValue(id, out var a)) return false;
            string sx = right ? "d" : "s";
            Transform tp = Child(mano, "polso_" + sx), te = Child(mano, "gomito_" + sx), ts = Child(mano, "spalla_" + sx);
            if (tp == null || te == null || ts == null) return false;
            var author = new[] { V(a.polso), V(a.gomito), V(a.spalla) };
            var unity = new[] { mano.InverseTransformPoint(tp.position), mano.InverseTransformPoint(te.position), mano.InverseTransformPoint(ts.position) };
            var m = Calibrate(author, unity, out float err);
            if (err > 0.01f) { Debug.LogWarning($"[PryingEyes] presa {(right ? "destra" : "sinistra")} di {id}: calibrazione fuori di {err * 100f:F1} cm, presa generica"); return false; }
            Vector3 M(Vector3 v) => m.MultiplyVector(v);
            g = new Grip { wrist = M(V(a.polso)), palm = M(V(a.palmo)), dorsal = M(V(a.dorso)), calibrationError = err };
            for (int f = 0; f < 4; f++)
            {
                g.fingers[f] = new Vector3[4];
                for (int i = 0; i < 4; i++) g.fingers[f][i] = M(V(a.dita, f * 4 + i));
            }
            for (int i = 0; i < 4; i++) g.thumb[i] = M(V(a.pollice, i));
            if (a.grilletto != null && a.grilletto.Length >= 9) g.trigger = new[] { M(V(a.grilletto, 0)), M(V(a.grilletto, 1)), M(V(a.grilletto, 2)) };
            return true;
        }
    }
}

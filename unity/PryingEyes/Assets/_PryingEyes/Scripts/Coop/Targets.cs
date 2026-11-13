using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Un giocatore come lo vede un nemico: quello di questa macchina (LocalTarget) oppure un compagno (RemotePlayer, solo sull'ospite
    /// o in una partita da solo con un compagno finto). Tutto quello che l'IA dei nemici sa del giocatore passa da qui, cosi' da solo il
    /// comportamento e' quello di sempre e in cooperativa i nemici scelgono tra piu' bersagli.</summary>
    public interface ITarget
    {
        /// <summary>0 l'ospite (o chi gioca da solo), 1..3 gli ospitati.</summary>
        int Slot { get; }
        /// <summary>Ai piedi.</summary>
        Vector3 Feet { get; }
        /// <summary>Vivo e nel piano di chi lo guarda.</summary>
        bool Alive { get; }
        float Lean { get; }
        float BodyHeight { get; }
        /// <summary>Dove si trova davvero il corpo a quell'altezza dai piedi (col lean la testa sporge).</summary>
        Vector3 LeanedPoint(float h);
        /// <summary>Cosa guarda l'occhio quando ti fissa: la visuale del giocatore.</summary>
        Transform LookAt { get; }
        /// <summary>Il fascio della torcia se e' accesa, altrimenti null.</summary>
        Transform BeamT { get; }
        /// <summary>Entro quanti metri ti nota un prete, ora (luce, torcia, movimento, spari: Stealth.SightRange).</summary>
        float SightRange(Enemy e);
        void Hurt(float dmg, Vector3? origin = null);
        void Sedate(float seconds);
    }

    /// <summary>Il giocatore di questa macchina.</summary>
    public sealed class LocalTarget : ITarget
    {
        static PlayerController P => GameManager.I != null ? GameManager.I.player : null;
        public int Slot => Coop.Slot;
        public Vector3 Feet { get { var p = P; return p != null ? p.transform.position : Vector3.zero; } }
        public bool Alive { get { var p = P; return p != null && p.alive; } }
        public float BodyHeight => P != null ? P.BodyHeight : 1.75f;
        public float Lean { get { var p = P; return p != null ? p.lean : 0f; } }
        public Vector3 LeanedPoint(float h) { var p = P; return p != null ? p.LeanedPoint(h) : Vector3.up * h; }
        public Transform LookAt { get { var p = P; return p != null ? p.cam : null; } }
        public Transform BeamT { get { var t = Torch.I; return t != null && t.Lit && t.Beam != null ? t.Beam.transform : null; } }
        public float SightRange(Enemy e) => Stealth.SightRange(e);
        public void Hurt(float dmg, Vector3? origin = null) { var p = P; if (p != null) p.Hurt(dmg, origin); }
        public void Sedate(float seconds) { var p = P; if (p != null) p.Sedate(seconds); }
    }

    /// <summary>I bersagli dei nemici: il giocatore locale (sempre al posto 0) e i compagni che l'ospite vede.</summary>
    public static class Targets
    {
        public static readonly LocalTarget Local = new LocalTarget();
        static readonly List<ITarget> remotes = new List<ITarget>();

        public static int Count => 1 + remotes.Count;
        public static ITarget At(int i) => i == 0 ? Local : remotes[i - 1];

        public static void Add(ITarget t) { if (t != null && !remotes.Contains(t)) remotes.Add(t); }
        public static void Remove(ITarget t) { remotes.Remove(t); }
        public static void Clear() { remotes.Clear(); }

        /// <summary>Il bersaglio vivo piu' vicino a p; se nessuno e' vivo, quello locale (cosi' chi lo usa trova sempre qualcuno).</summary>
        public static ITarget Nearest(Vector3 p)
        {
            ITarget best = null;
            float bd = float.MaxValue;
            for (int i = 0, n = Count; i < n; i++)
            {
                var t = At(i);
                if (!t.Alive) continue;
                float d = (t.Feet - p).sqrMagnitude;
                if (d < bd) { bd = d; best = t; }
            }
            return best ?? Local;
        }

        public static bool AnyAlive
        {
            get
            {
                for (int i = 0, n = Count; i < n; i++) if (At(i).Alive) return true;
                return false;
            }
        }

        /// <summary>Il bersaglio di quello slot (null se non c'e').</summary>
        public static ITarget OfSlot(int slot)
        {
            for (int i = 0, n = Count; i < n; i++) if (At(i).Slot == slot) return At(i);
            return null;
        }

        /// <summary>Qualche giocatore vivo ha la linea libera, dagli occhi, verso il punto? (un nemico non deve nascere sotto gli occhi di nessuno)</summary>
        public static bool AnySees(Vector3 point)
        {
            for (int i = 0, n = Count; i < n; i++)
            {
                var t = At(i);
                if (t.Alive && HotelNav.ClearLine(t.Feet + Vector3.up * 1.6f, point)) return true;
            }
            return false;
        }

        /// <summary>Distanza dal bersaglio vivo piu' vicino (per scegliere dove far nascere un nemico lontano da tutti).</summary>
        public static float NearestDistance(Vector3 p)
        {
            float bd = float.MaxValue;
            for (int i = 0, n = Count; i < n; i++)
            {
                var t = At(i);
                if (!t.Alive) continue;
                Vector3 d = t.Feet - p;
                float h = new Vector2(d.x, d.z).magnitude;
                if (h < bd) bd = h;
            }
            return bd;
        }
    }
}

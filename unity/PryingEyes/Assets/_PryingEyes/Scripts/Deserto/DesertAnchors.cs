using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>I punti attorno a cui il deserto deve esistere. Da soli (e sull'ospitato) e' uno solo, il giocatore di questa macchina: la sabbia, la navmesh, i luoghi e le cose sulla sabbia
    /// lo seguono. Gli altri punti sono di due specie (0.9.1):
    /// <list type="bullet">
    /// <item><b>i compagni di rete</b>, sull'<b>ospite</b> di una partita in cooperativa: i nemici li fa girare lui, anche attorno a un compagno che e' a un chilometro, e per camminare, inseguire e
    /// non attraversare i muri hanno bisogno di sabbia con il suo collider, di navmesh e di luoghi costruiti dove stanno i compagni. Gli ospitati non simulano niente: per loro i compagni sono
    /// pupazzi e il deserto e' solo attorno a se'. Questi punti si <i>servono</i> ma non si guardano da questa macchina;</item>
    /// <item><b>gli altri posti dello schermo condiviso</b> (una macchina, piu' giocatori, ognuno con la sua camera): servono e <i>si guardano</i>, quindi attorno a ognuno la sabbia va fino
    /// all'orizzonte, le cose sulla sabbia si disegnano, il cielo ha la sua cupola e gli effetti (polvere, sterpaglie, fuochi) nascono attorno a lui.</item>
    /// </list>
    /// Qui c'e' l'elenco degli <i>altri</i> punti (i piedi dei compagni vivi che hanno costruito il deserto; i corpi degli altri posti), una volta a fotogramma. Il punto "primario" e' sempre quello
    /// del posto 0 (il giocatore della finestra del gioco), qualunque posto stia girando in quel momento.</summary>
    public static class DesertAnchors
    {
        public struct Anchor { public Vector3 pos; public float yaw; public int slot; public bool viewer; }

        static readonly List<Anchor> others = new List<Anchor>();
        static readonly List<Vector3> positions = new List<Vector3>();
        static readonly List<Vector3> viewers = new List<Vector3>();
        static int frame = -1;

        static void Refresh()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            others.Clear(); positions.Clear(); viewers.Clear();
            if (!DesertMode.Active) return;
            // lo schermo condiviso: gli altri posti (il posto 0 e' il primario); un posto morto serve ancora, ma e' fermo: rinascera' in cima alla scalinata
            if (Seats.Split)
            {
                for (int i = 1; i < Seats.All.Count; i++)
                {
                    var pl = Seats.All[i].player;
                    if (pl == null) continue;
                    var p = pl.transform.position;
                    others.Add(new Anchor { pos = p, yaw = pl.yaw, slot = i, viewer = true });
                    positions.Add(p); viewers.Add(p);
                }
            }
            var g = Coop.Game;
            if (!Coop.IsHost || g == null) return;
            foreach (var kv in g.Players)
            {
                var rp = kv.Value;
                // chi non ha ancora costruito il deserto (epoca 0) o e' morto non e' un punto: rinascera' in cima alla scalinata, dove tutto c'e' gia'
                if (rp == null || !rp.hasState || !rp.last.alive || rp.last.epoch != g.EpochByte) continue;
                others.Add(new Anchor { pos = rp.last.pos, yaw = rp.last.yaw, slot = kv.Key, viewer = false });
                positions.Add(rp.last.pos);
            }
        }

        /// <summary>Gli altri punti (i compagni di rete e gli altri posti): vuoto da soli e sugli ospitati.</summary>
        public static List<Anchor> Others { get { Refresh(); return others; } }

        /// <summary>Le stesse posizioni, senza altro (per chi deve solo misurare distanze).</summary>
        public static List<Vector3> OtherPositions { get { Refresh(); return positions; } }

        /// <summary>Solo le posizioni degli altri posti dello schermo condiviso (quelli che si guardano da questa macchina): vuoto fuori dallo schermo condiviso.</summary>
        public static List<Vector3> ViewerPositions { get { Refresh(); return viewers; } }

        /// <summary>Dove sta il giocatore di questa macchina (nello schermo condiviso: il posto 0, qualunque posto giri adesso).</summary>
        public static Vector3 Local
        {
            get
            {
                var pl = Seats.Split && Seats.Primary != null && Seats.Primary.player != null ? Seats.Primary.player : GameManager.I != null ? GameManager.I.player : null;
                return pl != null ? pl.transform.position : Vector3.zero;
            }
        }

        /// <summary>La distanza dal luogo al piu' vicino tra il giocatore e gli altri posti dello schermo condiviso (solo quelli che si guardano da questa macchina, non i compagni di rete).</summary>
        public static float NearestViewFootprint(Site s, Vector3 primary)
        {
            float best = FootDist(s, primary);
            var v = ViewerPositions;
            for (int i = 0; i < v.Count; i++) best = Mathf.Min(best, FootDist(s, v[i]));
            return best;
        }

        /// <summary>La distanza orizzontale dal punto al piu' vicino tra il giocatore e gli altri posti dello schermo condiviso.</summary>
        public static float NearestViewFlat(Vector3 at, Vector3 primary)
        {
            float best = Flat2(at, primary);
            var v = ViewerPositions;
            for (int i = 0; i < v.Count; i++) best = Mathf.Min(best, Flat2(at, v[i]));
            return Mathf.Sqrt(best);
        }

        /// <summary>La distanza orizzontale dal punto al piu' vicino tra il giocatore di questa macchina e gli altri punti.</summary>
        public static float NearestFlat(Vector3 at)
        {
            float best = Flat2(at, Local);
            var list = OtherPositions;
            for (int i = 0; i < list.Count; i++) best = Mathf.Min(best, Flat2(at, list[i]));
            return Mathf.Sqrt(best);
        }

        /// <summary>Un altro punto (non il giocatore di questa macchina) e' a meno di 'radius' metri dal punto, nello spazio?</summary>
        public static bool NearOther(Vector3 at, float radius)
        {
            var list = OtherPositions;
            float r2 = radius * radius;
            for (int i = 0; i < list.Count; i++) if ((at - list[i]).sqrMagnitude < r2) return true;
            return false;
        }

        /// <summary>Lo slot (0 l'ospite, o il posto 0) di chi sta piu' vicino al punto, in piano. Da soli e' sempre il proprio.</summary>
        public static int NearestSlot(Vector3 at)
        {
            int slot = Seats.Split ? 0 : Coop.Slot;
            float best = Flat2(at, Local);
            var list = Others;
            for (int i = 0; i < list.Count; i++)
            {
                float d = Flat2(at, list[i].pos);
                if (d < best) { best = d; slot = list[i].slot; }
            }
            return slot;
        }

        /// <summary>Il punto piu' vicino al luogo tra il giocatore e gli altri (e quanto dista in piano dal bordo del luogo).</summary>
        public static float NearestFootprint(Site s, Vector3 primary, out Vector3 which)
        {
            which = primary;
            float best = FootDist(s, primary);
            var list = OtherPositions;
            for (int i = 0; i < list.Count; i++)
            {
                float d = FootDist(s, list[i]);
                if (d < best) { best = d; which = list[i]; }
            }
            return best;
        }

        static float Flat2(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

        /// <summary>La distanza orizzontale dal punto al bordo del luogo (0 dentro l'impronta).</summary>
        public static float FootDist(Site s, Vector3 p)
        {
            float dx = Mathf.Max(Mathf.Max(s.fx0 - p.x, p.x - s.fx1), 0f), dz = Mathf.Max(Mathf.Max(s.fz0 - p.z, p.z - s.fz1), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}

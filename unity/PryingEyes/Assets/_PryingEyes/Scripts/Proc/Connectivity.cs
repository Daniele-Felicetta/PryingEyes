using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Rete di sicurezza: dopo aver costruito la navmesh si controlla che dall'arrivo si raggiunga ogni stanza, ogni candela e
    /// l'uscita. Gli arredi sono casuali e ogni tanto uno sbarramento (banco, divano, panche) chiude una porta o lascia una tasca
    /// chiusa: nelle stanze che non si raggiungono si tolgono gli arredi e si ricostruisce la navmesh. Costa una stanza spoglia
    /// una volta su cento, ma il giocatore non resta mai bloccato.</summary>
    public static class Connectivity
    {
        static readonly NavMeshPath path = new NavMeshPath();
        static readonly Vector3[] offsets =
        {
            Vector3.zero, new Vector3(1.2f, 0, 0), new Vector3(-1.2f, 0, 0), new Vector3(0, 0, 1.2f), new Vector3(0, 0, -1.2f), new Vector3(2.4f, 0, 2.4f), new Vector3(-2.4f, 0, -2.4f),
        };

        public static bool Reach(Vector3 from, Vector3 target, float tol)
        {
            if (!NavMesh.SamplePosition(target, out NavMeshHit th, tol, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(from, th.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        public static bool RoomReach(Vector3 from, Room r)
        {
            var ctr = new Vector3(r.CenterX, 0f, r.CenterZ);
            foreach (var o in offsets) if (Reach(from, ctr + o, 1.0f)) return true;
            return false;
        }

        public static bool StartPoint(FloorInstance fi, out Vector3 start)
        {
            start = Vector3.zero;
            var lift = fi.arrival;
            if (lift == null) return false;
            if (!NavMesh.SamplePosition(lift.frontPoint, out NavMeshHit h, 2.5f, NavMesh.AllAreas)) return false;
            start = h.position;
            return true;
        }

        /// <summary>Stanze non raggiungibili dall'arrivo, piu' le candele e l'uscita che non si raggiungono.</summary>
        public static List<Room> Unreachable(FloorInstance fi, out int candlesBad, out bool exitBad)
        {
            var bad = new List<Room>();
            candlesBad = 0; exitBad = false;
            if (!StartPoint(fi, out Vector3 s)) { exitBad = true; return bad; }
            foreach (var r in fi.plan.rooms)
            {
                if (r.IsLift || r.type == RoomType.Segreta) continue;     // la stanza segreta si raggiunge solo a carponi: la navmesh non c'e'
                if (!RoomReach(s, r)) bad.Add(r);
            }
            foreach (var c in fi.candles) if (c != null && !Reach(s, c.transform.position, 0.9f)) candlesBad++;
            if (fi.exit != null && !Reach(s, fi.exit.frontPoint, 1.5f)) exitBad = true;
            return bad;
        }

        /// <summary>Toglie gli arredi di una stanza (le candele dell'obiettivo restano) e rimette un contenitore vuoto.</summary>
        public static void Strip(FloorInstance fi, Room r)
        {
            var go = fi.roomGo[r.id];
            if (go == null) return;
            var pr = go.transform.Find("Arredi");
            if (pr == null) return;
            foreach (var c in fi.candles) if (c != null && c.transform.IsChildOf(pr)) c.transform.SetParent(go.transform, true);
            fi.propRoots.Remove(pr);
            Util.Kill(pr.gameObject);
        }

        /// <summary>Quanti arredi l'ultima Repair ha tolto dai varchi delle porte (non contano come stanze svuotate).</summary>
        public static int LastCleared { get; private set; }

        /// <summary>Libera il varco di una porta: toglie gli arredi (non i candelabri) che toccano la soglia o il metro e mezzo davanti, da entrambi
        /// i lati. Ritorna quanti ne ha tolti. Lo sbarramento di una porta sta quasi sempre li' (una pala d'altare contro un arco, un armadio
        /// davanti a una porta), e spesso nella stanza che SI raggiunge, non in quella che non si raggiunge: svuotare solo quest'ultima non basta.</summary>
        public static int ClearDoorway(FloorInstance fi, Opening o)
        {
            float ox = o.alongX ? o.center * 0.1f : o.line * 0.1f, oz = o.alongX ? o.line * 0.1f : o.center * 0.1f;
            Vector3 half = o.alongX ? new Vector3(o.width * 0.05f + 0.5f, 1.1f, 1.8f) : new Vector3(1.8f, 1.1f, o.width * 0.05f + 0.5f);
            return RemoveProps(Physics.OverlapBox(new Vector3(ox, 1.1f, oz), half, Quaternion.identity, HotelNav.EnvMask, QueryTriggerInteraction.Ignore));
        }

        /// <summary>Libera l'intorno di una candela che non si raggiunge (chiusa tra colonne, panche e arredi): toglie gli arredi (non i candelabri)
        /// entro 'radius' metri. Meno distruttivo di svuotare la stanza, che in un'arena si porterebbe via tutte le colonne.</summary>
        public static int ClearAround(Vector3 p, float radius) =>
            RemoveProps(Physics.OverlapSphere(p + Vector3.up * 1.0f, radius, HotelNav.EnvMask, QueryTriggerInteraction.Ignore));

        static int RemoveProps(Collider[] cols)
        {
            int n = 0;
            foreach (var col in cols)
            {
                // il pezzo d'arredo e' il figlio diretto di un contenitore "Arredi" (i muri, i pavimenti e le cornici delle porte no)
                Transform t = col.transform, prop = null;
                while (t != null) { if (t.parent != null && t.parent.name == "Arredi") { prop = t; break; } t = t.parent; }
                if (prop == null || !prop.gameObject.activeSelf || prop.GetComponentInChildren<Candelabro>() != null) continue;
                prop.gameObject.SetActive(false);                // fuori dalla fisica e dalla navmesh subito, la distruzione arriva a fine fotogramma
                Util.Kill(prop.gameObject);
                n++;
            }
            return n;
        }

        /// <summary>Controlla e ripara: prima si liberano i varchi delle porte tra una stanza raggiunta e una no (si ricostruisce la navmesh e si
        /// riprova, a catena) e l'intorno delle candele chiuse in una tasca; se non basta, una stanza alla volta: si svuota la stanza non
        /// raggiungibile piu' vicina all'arrivo, si ricostruisce la navmesh e si riprova (le stanze oltre si aprono da sole). Ritorna quante
        /// stanze sono state svuotate.</summary>
        public static int Repair(FloorInstance fi, out int problemsBefore)
        {
            problemsBefore = 0;
            LastCleared = 0;
            int stripped = 0;
            var plan = fi.plan;
            var hops = plan.Hops(plan.start);
            var done = new HashSet<Room>();
            for (int iter = 0; iter < 12; iter++)
            {
                var bad = Unreachable(fi, out int cb, out bool eb);
                int problems = bad.Count + cb + (eb ? 1 : 0);
                if (iter == 0) problemsBefore = problems;
                if (problems == 0) break;
                if (bad.Count > 0)
                {
                    var badIds = new HashSet<int>();
                    foreach (var r in bad) badIds.Add(r.id);
                    int cleared = 0;
                    foreach (var o in plan.openings)
                    {
                        if (o.b < 0 || o.oneSided || o.style == OpenStyle.Ascensore || o.style == OpenStyle.Passaggio) continue;
                        if (badIds.Contains(o.a) != badIds.Contains(o.b)) cleared += ClearDoorway(fi, o);
                    }
                    if (cleared > 0)
                    {
                        LastCleared += cleared;
                        Physics.SyncTransforms();
                        HotelNav.Rebuild(fi.bounds, fi.propRoots);
                        continue;
                    }
                }
                if (bad.Count == 0 && cb > 0 && StartPoint(fi, out Vector3 s0))
                {
                    // le stanze si raggiungono tutte ma una candela e' chiusa in una tasca (tra colonne, panche e arredi): si libera solo
                    // l'intorno della candela. Svuotare la stanza risolverebbe lo stesso, ma in un'arena si porterebbe via anche le colonne.
                    int around = 0;
                    foreach (var cd in fi.candles)
                        if (cd != null && !Reach(s0, cd.transform.position, 0.9f)) around += ClearAround(cd.transform.position, 1.9f);
                    if (around > 0)
                    {
                        LastCleared += around;
                        Physics.SyncTransforms();
                        HotelNav.Rebuild(fi.bounds, fi.propRoots);
                        continue;
                    }
                }
                Room pick = null;
                int bh = int.MaxValue;
                foreach (var r in bad) if (!done.Contains(r) && hops[r.id] < bh) { bh = hops[r.id]; pick = r; }
                if (pick == null)
                {
                    // tutte le stanze si raggiungono ma non una candela o l'uscita: si svuota la stanza della candela / dell'uscita
                    if (StartPoint(fi, out Vector3 s))
                        foreach (var c in fi.candles)
                            if (pick == null && c != null && !Reach(s, c.transform.position, 0.9f)) { var rr = fi.RoomAt(c.transform.position); if (rr != null && !done.Contains(rr)) pick = rr; }
                    if (pick == null && eb)
                    {
                        var le = plan.openings.Find(q => q.style == OpenStyle.Ascensore && (q.a == plan.exit || q.b == plan.exit));
                        if (le != null) { var rr = plan.rooms[le.a == plan.exit ? le.b : le.a]; if (!done.Contains(rr)) pick = rr; }
                    }
                }
                if (pick == null) break;
                done.Add(pick);
                Strip(fi, pick);
                stripped++;
                Physics.SyncTransforms();
                HotelNav.Rebuild(fi.bounds, fi.propRoots);
            }
            return stripped;
        }
    }
}

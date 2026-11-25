#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -annextest [-seed S -floor F]: ogni annesso del piano (torre, cripta, giardino segreto) alla prova
    /// con il CharacterController vero. Chiuso: non ci si entra a piedi (nessuna via laterale). Aperto: scale comprese si arriva all'interno,
    /// al tesoro e alla chiave. I tre modi di aprirlo (leve, tributo, chiave) fanno quello che dicono, e le cose da usare si raggiungono con
    /// F (primo ostacolo del raggio = l'oggetto stesso). I nemici non nascono dentro gli annessi.</summary>
    public sealed class AnnexTest : PlayerTest
    {
        bool walkReached;
        Vector3 walkStopped;
        float walkTime;

        protected override string Suite { get { return "annextest"; } }
        protected override string ExceptionCheck { get { return null; } }       // la prova non ne ha mai avuto uno
        protected override void Finish() { PlayerController.botDir = Vector3.zero; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<AnnexTest>("-annextest"); }

        /// <summary>Cammina dalla posizione attuale fino a 'target' seguendo la navmesh, come un giocatore. Si ferma se non avanza da tre secondi.</summary>
        IEnumerator Walk(Vector3 target, float timeout)
        {
            var player = GameManager.I.player;
            var path = new NavMeshPath();
            walkReached = false; walkTime = 0f;
            if (!NavMesh.SamplePosition(target, out var goal, 1.5f, NavMesh.AllAreas)) { walkStopped = player.transform.position; yield break; }
            float start = Time.time, until = start + timeout, repath = 0f, lastCheck = Time.time;
            Vector3 lastPos = player.transform.position;
            while (Time.time < until)
            {
                Vector3 p = player.transform.position;
                if (Vector3.Distance(p, goal.position) <= 0.7f) { walkReached = true; break; }
                if (Time.time >= repath)
                {
                    repath = Time.time + 0.25f;
                    if (NavMesh.SamplePosition(p, out var o, 1.5f, NavMesh.AllAreas)) NavMesh.CalculatePath(o.position, goal.position, NavMesh.AllAreas, path);
                }
                Vector3 next = goal.position;
                if (path.corners.Length > 1)
                {
                    next = path.corners[1];
                    if (Vector3.Distance(next, p) < 0.45f && path.corners.Length > 2) next = path.corners[2];
                }
                Vector3 d = next - p; d.y = 0f;
                PlayerController.botDir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.zero;
                if (Time.time - lastCheck >= 3f)
                {
                    if (Vector3.Distance(p, lastPos) < 0.3f) break;         // fermo da tre secondi: bloccato
                    lastPos = p; lastCheck = Time.time;
                }
                yield return null;
            }
            PlayerController.botDir = Vector3.zero;
            walkTime = Time.time - start;
            walkStopped = player.transform.position;
        }

        /// <summary>Da almeno uno dei quattro lati, a un metro e mezzo e all'altezza degli occhi, il raggio di F arriva alla cosa e non a un ostacolo.</summary>
        static bool UsableFromSomewhere(Vector3 point, Transform owner)
        {
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f * Mathf.Deg2Rad;
                Vector3 eye = point + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.3f;
                eye.y = point.y + (1.62f - 1.0f) * 0.5f;
                if (Interact.Visible(eye, point, owner)) return true;
            }
            return false;
        }

        protected override IEnumerator Run()
        {
            Check("automatic tests mute audio", AudioListener.volume == 0f);
            float deadline = Time.realtimeSinceStartup + 90f;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            var fi = FloorDirector.I.cur; var gm = GameManager.I; var player = gm.player;
            Enemy.ClearAll(); Enemy.debugFreeze = Enemy.debugNoSense = true;
            player.invulnerable = true; PlayerController.holdLook = false;
            Time.timeScale = 2f;
            Debug.Log($"[PryingEyes] annextest piano {FloorDirector.I.floor}: {fi.plan.rooms.Count} stanze, {fi.annexSites.Count} annessi");
            Check("il piano ha almeno un annesso", fi.annexSites.Count > 0);

            // niente nascite dentro gli annessi: i punti candidati non cadono nelle zone vietate
            int bad = 0;
            foreach (var p in fi.groundSpawns) if (AnnexRuntime.Forbidden(fi, p)) { bad++; Debug.Log($"[PryingEyes] annextest punto di nascita a terra vietato: {p}"); }
            foreach (var p in fi.airSpawns) if (AnnexRuntime.Forbidden(fi, p)) { bad++; Debug.Log($"[PryingEyes] annextest punto di nascita in aria vietato: {p}"); }
            Check($"nessun punto di nascita dentro un annesso ({fi.groundSpawns.Count} a terra, {fi.airSpawns.Count} in aria)", bad == 0);

            // prima i custodi delle chiavi: il giardino si apre solo con la chiave presa in un altro annesso
            // il sacrario del grimorio si prova a parte (MagiaTest, -magiatest): si entra a carponi e custodisce un libro, non uno scrigno
            var order = new List<AnnexSite>(fi.annexSites);
            order.RemoveAll(x => x.data.kind == AnnexKind.Sacrario);
            Check("il piano ha il suo sacrario del grimorio (uno solo)", fi.annexSites.FindAll(x => x.data.kind == AnnexKind.Sacrario).Count == 1);
            order.Sort((a, b) => (a.data.gate == GateKind.Chiave ? 1 : 0).CompareTo(b.data.gate == GateKind.Chiave ? 1 : 0));
            foreach (var s in order)
            {
                var seq = CheckSite(fi, gm, player, s);
                while (true)
                {
                    bool more;
                    try { more = seq.MoveNext(); }
                    catch (Exception e) { Debug.LogException(e); Check($"annesso {s.data}: nessuna eccezione", false); break; }
                    if (!more) break;
                    yield return seq.Current;
                }
            }
        }

        IEnumerator CheckSite(FloorInstance fi, GameManager gm, PlayerController player, AnnexSite s)
        {
            string tag = $"annesso {s.data} ({s.data.gate})";
            bool okOut = NavMesh.SamplePosition(s.outside, out var no, 1.5f, NavMesh.AllAreas);
            bool okIn = NavMesh.SamplePosition(s.inside, out var ni, 1.5f, NavMesh.AllAreas);
            bool okRw = NavMesh.SamplePosition(s.rewardPos, out var nr, 1.5f, NavMesh.AllAreas);
            Check($"{tag}: ingresso, interno e tesoro stanno sulla navmesh", okOut && okIn && okRw);
            if (!(okOut && okIn && okRw)) yield break;
            Check($"{tag}: la pianta conosce l'interno e il tesoro", fi.RoomAt(s.inside) != null && fi.RoomAt(s.rewardPos) != null);
            Check($"{tag}: cancello usabile e chiuso", s.closerCol != null && s.closerCol.enabled && !s.opened && s.GatePrompt() != null);
            Check($"{tag}: il cancello si usa con F da davanti", s.closer != null && Interact.Visible(no.position + Vector3.up * 1.62f, s.gatePoint, s.closer));
            Check($"{tag}: il tesoro c'e' ed e' usabile", s.chest != null && s.chest.Prompt != null);

            // ---- chiuso: nessuna via laterale
            player.Teleport(no.position);
            yield return new WaitForSeconds(0.4f);
            yield return Walk(ni.position, 40f);
            Check($"{tag}: chiuso, a piedi non si entra (fermo a {walkStopped.x:F1},{walkStopped.y:F1},{walkStopped.z:F1})", !walkReached);

            // ---- il modo di aprirlo
            switch (s.data.gate)
            {
                case GateKind.Tributo:
                {
                    gm.score = s.data.price - 1; s.GateUse();
                    Check($"{tag}: con un punto in meno il tributo non apre", !s.opened);
                    gm.score = s.data.price + 120; s.GateUse();
                    Check($"{tag}: con i punti il tributo apre e li toglie", s.opened && gm.score == 120);
                    break;
                }
                case GateKind.Sigilli:
                {
                    var levers = s.props.GetComponentsInChildren<AnnexLever>(true);
                    Check($"{tag}: {levers.Length} leve su {s.data.levers}", levers.Length == s.data.levers);
                    bool seen = true;
                    foreach (var l in levers) seen &= UsableFromSomewhere(l.Point, l.transform);
                    Check($"{tag}: ogni leva si raggiunge con F", seen);
                    if (levers.Length >= 2)
                    {
                        // il limite di tempo: una leva sola, poi si lascia scadere (tempo accelerato) e tutto torna come prima
                        levers[0].Use();
                        Check($"{tag}: con una leva tirata restano {s.Remaining:F0} s", s.pulled == 1 && !s.opened && s.Remaining > 100f);
                        Time.timeScale = 40f;
                        float expire = Time.time + s.ResetAfter + 3f;
                        while (Time.time < expire) yield return null;
                        Time.timeScale = 2f;
                        yield return new WaitForSeconds(0.5f);
                        Check($"{tag}: scaduto il tempo i sigilli si richiudono", s.pulled == 0 && !s.opened);
                    }
                    for (int i = 0; i < levers.Length; i++)
                    {
                        Check($"{tag}: prima della leva {i + 1} il cancello e' chiuso", !s.opened);
                        levers[i].Use();
                    }
                    Check($"{tag}: tirate tutte le leve il cancello si apre", s.opened);
                    break;
                }
                default:
                {
                    s.GateUse();
                    Check($"{tag}: senza chiave non si apre", !s.opened);
                    AnnexSite holder = null;
                    foreach (var o in fi.annexSites) if (o.data.id == s.data.keyHolder) holder = o;
                    var key = holder != null ? holder.props.GetComponentInChildren<AnnexKey>(true) : null;
                    Check($"{tag}: la chiave sta nell'annesso {holder?.data.id}", key != null);
                    if (key != null)
                    {
                        Check($"{tag}: la chiave si raggiunge con F", UsableFromSomewhere(key.Point, key.transform));
                        key.Use();
                    }
                    Check($"{tag}: presa la chiave l'annesso e' suo", AnnexRuntime.HasKeyFor(s.data.id));
                    s.GateUse();
                    Check($"{tag}: con la chiave si apre", s.opened);
                    break;
                }
            }
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.3f);
            Check($"{tag}: aperto, il collider del cancello e' spento", s.closerCol == null || !s.closerCol.enabled);

            // ---- aperto: dentro, al tesoro, e se custodisce una chiave anche li'
            player.Teleport(no.position);
            yield return new WaitForSeconds(0.4f);
            yield return Walk(ni.position, 80f);
            Check($"{tag}: aperto, si arriva all'interno in {walkTime:F1} s", walkReached);
            if (walkReached)
            {
                yield return Walk(nr.position, 40f);
                Check($"{tag}: dall'interno si arriva al tesoro in {walkTime:F1} s", walkReached);
                var key = s.props.GetComponentInChildren<AnnexKey>(true);
                if (key != null && NavMesh.SamplePosition(s.keyPos, out var nk, 1.5f, NavMesh.AllAreas))
                {
                    yield return Walk(nk.position, 40f);
                    Check($"{tag}: si arriva anche alla chiave", walkReached);
                }
                // e si torna fuori
                yield return Walk(no.position, 80f);
                Check($"{tag}: si torna nella sala in {walkTime:F1} s", walkReached);
            }
        }
    }
}
#endif

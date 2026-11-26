#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -powertest [-powershots]: la corrente del piano (Proc/Power.cs). Si parte con le luci
    /// d'emergenza: il quadro elettrico sta appeso al muro dell'area piu' lontana dall'arrivo, con pavimento e navmesh davanti; le
    /// lampade elettriche sono al 40% e le altre luci (candele, luna, finestre, insegne) restano come sono; trappole e altare
    /// rifiutano. F davanti al quadro (lo sceglie Interact) tira la leva: la luce torna a ondata dal quadro (le lampade lontane
    /// aspettano il loro turno), poi tutte piene; la leva e' giu' e le trappole si accendono.</summary>
    public sealed class PowerTest : PlayerTest
    {
        protected override string Suite { get { return "powertest"; } }
        protected override string PhotoFlag { get { return "-powershots"; } }
        protected override string OutFolder { get { return "corrente"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<PowerTest>("-powertest"); }

        static void Face(PlayerController player, Vector3 target)
        {
            Vector3 d = target - player.cam.position;
            player.yaw = Mathf.Atan2(d.x, d.z);
            player.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
        }

        /// <summary>Lampade elettriche accese senza tremolio: (quante, quante alla scala attesa entro il 2%).</summary>
        static (int n, int ok) Electric(float scale, float minDelay = -1f, float maxDelay = 99f)
        {
            int n = 0, ok = 0;
            foreach (var e in LightPool.All)
            {
                if (!e.electric || e.driven != 0 || e.l == null || !e.l.enabled || e.baseI <= 0f) continue;
                if (e.delay < minDelay || e.delay > maxDelay) continue;
                n++;
                if (Mathf.Abs(e.l.intensity - e.baseI * scale) <= e.baseI * 0.02f + 1e-4f) ok++;
            }
            return (n, ok);
        }

        protected override IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll(); Enemy.debugFreeze = false; Enemy.debugNoSense = false;
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            var panel = Power.Panel;
            Check("il piano ha il quadro elettrico", panel != null);
            Check("si parte senza corrente", !Power.On);
            if (Failed > 0) yield break;

            // dove sta
            var access = RoomAccessDoors.I.Access;
            int depth = access.Depth(panel.RoomId), maxDepth = 0;
            foreach (var r in FloorDirector.I.cur.plan.rooms) if (r != null && !r.IsLift) maxDepth = Mathf.Max(maxDepth, access.Depth(r.id));
            Check($"il quadro sta in un'area lontana dall'arrivo ({depth} porte, la piu' lontana a {maxDepth})",
                  panel.RoomId != access.StartRoom && depth >= Mathf.Max(1, maxDepth - 1));
            Transform pt = panel.transform;
            bool wall = Physics.Raycast(pt.position + pt.right * 0.6f + pt.forward * 0.3f, -pt.forward, out var wh, 0.6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)
                        && wh.distance < 0.36f;
            Vector3 front = pt.position + pt.forward * 0.95f;
            bool ground = HotelNav.Ground(front + Vector3.up * 0.3f, out Vector3 fg) && NavMesh.SamplePosition(fg, out _, 0.5f, NavMesh.AllAreas);
            Check("appeso a un muro, con pavimento e navmesh davanti", wall && ground);
            if (Failed > 0) yield break;

            // davanti al quadro: luci d'emergenza
            player.Teleport(fg + Vector3.up * 0.05f); player.hp = 100; player.invulnerable = true;
            yield return null;
            Face(player, panel.Point);
            LightPool.Tick(true);
            yield return null; yield return null;
            var (n0, ok0) = Electric(Power.Emergency);
            int other = 0, otherOk = 0, elecTotal = 0;
            foreach (var e in LightPool.All)
            {
                if (e.l == null) continue;
                if (e.electric) { elecTotal++; continue; }
                if (e.driven != 0 || !e.l.enabled) continue;
                other++;
                if (Mathf.Abs(e.l.intensity - e.baseI) < 1e-3f) otherOk++;
            }
            Debug.Log($"[PryingEyes] powertest lampade: {elecTotal} elettriche, {LightPool.All.Count - elecTotal} altre");
            Check($"senza corrente le lampade elettriche sono al 40% ({ok0}/{n0} accese)", n0 > 0 && ok0 == n0);
            Check($"le altre luci restano come sono ({otherOk}/{other})", otherOk == other);
            Photo("prima");
            Vector3 far = pt.position + pt.forward * 4.5f;
            Vector3 farG = far;
            bool roomView = Photos && HotelNav.Ground(far + Vector3.up * 0.3f, out farG) && HotelNav.ClearLine(farG + Vector3.up * 1.6f, panel.Point);
            if (roomView)
            {
                player.Teleport(farG + Vector3.up * 0.05f);
                yield return null;
                Face(player, panel.Point + Vector3.up * 0.4f);
                yield return new WaitForSeconds(0.2f);
                Photo("prima_sala");
                player.Teleport(fg + Vector3.up * 0.05f);
                yield return null;
            }

            // senza corrente trappole e altare rifiutano
            var all = new List<int>();
            for (int i = 0; i < FloorDirector.I.cur.plan.rooms.Count; i++) all.Add(i);
            RoomAccessDoors.I.ApplySnapshot(all, true);
            yield return null;
            DoorTrap trap = DoorTraps.I != null && DoorTraps.I.Traps.Count > 0 ? DoorTraps.I.Traps[0] : null;
            if (trap != null)
            {
                gm.score = 5000;
                trap.Use();
                yield return null;
                Check($"senza corrente la trappola non si accende ({trap.Levers[0].Prompt})", trap.state == 0 && gm.score == 5000 && trap.Levers[0].Prompt.StartsWith("SENZA CORRENTE"));
            }
            else Debug.Log("[PryingEyes] powertest nessuna trappola su questo piano");
            var altar = FindAnyObjectByType<Altare>();
            if (altar != null && player.weapon.CanConsecrate)
            {
                gm.score = 10000;
                altar.Use();
                Check("senza corrente l'altare non consacra", !player.weapon.Upgraded && gm.score == 10000);
            }

            // F sul quadro
            gm.score = 0;
            player.Teleport(fg + Vector3.up * 0.05f);
            yield return null;
            Face(player, panel.Point);
            yield return null;
            bool selected = Interact.Tick(player.cam);
            Check($"F punta il quadro ({Interact.Current?.Prompt})", selected && ReferenceEquals(Interact.Current, panel));
            Interact.TryUse();
            yield return null;
            Check("la leva ridà la corrente (gratis)", Power.On && gm.score == 0);
            yield return new WaitForSeconds(0.15f);
            var (nf, okf) = Electric(Power.Emergency, 0.55f);
            if (nf > 0) Check($"l'ondata parte dal quadro: le lampade lontane aspettano ({okf}/{nf} ancora d'emergenza)", okf == nf);
            else Debug.Log("[PryingEyes] powertest nessuna lampada accesa abbastanza lontana per vedere l'ondata");
            yield return new WaitForSeconds(0.5f);
            Check($"la leva e' giu' ({panel.Pull:F2})", panel.Pull > 0.95f);
            yield return new WaitForSeconds(Power.CascadeTime + 0.4f);
            var (n1, ok1) = Electric(1f);
            Check($"poi tutte le lampade elettriche sono piene ({ok1}/{n1})", n1 > 0 && ok1 == n1);
            Check("il quadro non propone piu' niente", panel.Prompt == null);
            Photo("dopo");
            if (roomView)
            {
                player.Teleport(farG + Vector3.up * 0.05f);
                yield return null;
                Face(player, panel.Point + Vector3.up * 0.4f);
                yield return new WaitForSeconds(0.2f);
                Photo("dopo_sala");
                player.Teleport(fg + Vector3.up * 0.05f);
                yield return null;
            }
            if (trap != null)
            {
                gm.score = 5000;
                trap.Use();
                yield return null;
                Check("con la corrente la trappola si accende", trap.state == 1 && gm.score == 5000 - DoorTraps.Price);
            }
            player.invulnerable = false;
        }
    }
}
#endif

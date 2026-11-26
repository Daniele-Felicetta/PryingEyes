#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;
using PryingEyes.Testing;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -magtest [-magshots]: il caricatore vuoto che cade (Weapon.MagDrop.cs). Con l'AR-9 a vuoto
    /// la ricarica butta un caricatore che cade a terra davanti ai piedi, e quello vero sparisce mentre la mano va alla cintura, poi
    /// torna; a fine ricarica il caricatore e' pieno e visibile. La ricarica tattica non butta niente; la pistola a vuoto si';
    /// il fucile a pompa (a cartucce) no.</summary>
    public sealed class MagTest : PlayerTest
    {
        protected override string Suite { get { return "magtest"; } }
        protected override string PhotoFlag { get { return "-magshots"; } }
        protected override string OutFolder { get { return "caricatori"; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<MagTest>("-magtest"); }

        static bool MagVisible(Weapon w)
        {
            var rig = w.GetComponentInChildren<WeaponRig>();
            if (rig == null || rig.mag == null) return false;
            foreach (var r in rig.mag.GetComponentsInChildren<Renderer>(true)) if (r.enabled) return true;
            return false;
        }

        protected override IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll();
            var gm = GameManager.I; var player = gm.player; var w = player.weapon;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            Check("c'e' spazio per la prova", MeleeTest.FindArena(out Vector3 arena, out Vector3 forward));
            if (Failed > 0) yield break;
            player.Teleport(arena + Vector3.up * 0.05f); player.invulnerable = true;
            player.yaw = Mathf.Atan2(forward.x, forward.z); player.pitch = 0.35f;
            w.ResetLoadout(); w.Equip(WeaponDefs.IndexOf("ar9_malleus"));
            yield return new WaitForSeconds(1f);
            int before = DroppedMag.Count;

            // ricarica a vuoto
            w.mag = 0; w.reserve = 90;
            w.Reload();
            float t0 = Time.time, dur = WeaponDefs.All[WeaponDefs.IndexOf("ar9_malleus")].reloadEmpty;
            bool sawHidden = false, sawDrop = false;
            while (w.reloading && Time.time - t0 < dur + 1f)
            {
                if (DroppedMag.Count > before) sawDrop = true;
                if (!MagVisible(w)) sawHidden = true;
                if (sawDrop && Photos && Time.time - t0 > dur * 0.3f && Time.time - t0 < dur * 0.3f + 0.05f) Photo("cintura");
                yield return null;
            }
            Check("a vuoto butta il caricatore", sawDrop && DroppedMag.Count == before + 1);
            Check("mentre la mano va alla cintura quello vero sparisce", sawHidden);
            Check($"a fine ricarica e' pieno e visibile ({w.mag})", !w.reloading && w.mag > 0 && MagVisible(w));
            yield return new WaitForSeconds(1.5f);
            var dropped = FindObjectsByType<DroppedMag>(FindObjectsSortMode.None);
            float low = 9f;
            foreach (var m in dropped) low = Mathf.Min(low, m.transform.position.y - arena.y);
            Check($"il vuoto e' caduto a terra ({low:F2} m dal pavimento)", dropped.Length > 0 && low < 0.25f);
            Photo("a_terra");

            // tattica: niente buttato
            int n1 = DroppedMag.Count;
            w.mag = 10; w.reserve = 90;
            w.Reload();
            t0 = Time.time;
            while (w.reloading && Time.time - t0 < 4f) yield return null;
            Check("la ricarica tattica non butta niente", DroppedMag.Count == n1);

            // pistola a vuoto: si'; fucile a pompa: no
            w.ResetLoadout(); w.Equip(WeaponDefs.IndexOf("p9_pieta"));
            yield return new WaitForSeconds(1f);
            int n2 = DroppedMag.Count;
            w.mag = 0; w.reserve = 30;
            w.Reload();
            t0 = Time.time;
            while (w.reloading && Time.time - t0 < 4f) yield return null;
            Check("la pistola a vuoto butta il caricatore", DroppedMag.Count == n2 + 1);
            int sg = WeaponDefs.IndexOf("sg_breacher12");
            if (sg >= 0)
            {
                w.ResetLoadout(); w.Equip(sg);
                yield return new WaitForSeconds(1f);
                int n3 = DroppedMag.Count;
                w.mag = 0; w.reserve = 20;
                w.Reload();
                t0 = Time.time;
                while (w.reloading && Time.time - t0 < 8f) yield return null;
                Check("il fucile a pompa (a cartucce) non butta niente", DroppedMag.Count == n3);
            }
            player.invulnerable = false;
        }
    }
}
#endif

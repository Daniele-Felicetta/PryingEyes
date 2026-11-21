#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly tracce: le tracce delle gomme sulla sabbia (DesertTracks.cs, DesertTrack.shader, DesertBike.Pose). Una moto ferma non ne lascia; guidata per una cinquantina di metri sulla sabbia
    /// ne lascia due nastri (uno per ruota, un pezzo ogni ottanta centimetri circa), posati sul terreno e vicini alla strada fatta, con il pezzo piu' nuovo sotto la moto; il nastro si spezza dove la
    /// ruota non tocca sabbia (in aria, su una pietra) e il vento cancella tutto in un minuto. Con -desertshots le fotografie: la scia vista da dietro e da vicino, a sera (sole radente) e a mezzogiorno.</summary>
    public sealed partial class DesertTest
    {
        /// <summary>Via tutte le guardie che non sono boss: le prove della moto misurano la moto e non il combattimento, e in una catena di fasi quelle svegliate dalle fasi di prima (i luoghi a un paio di
        /// centinaia di metri a sud si caricano ogni volta che si guida fin laggiu') arrivano a contatto con il pilota fermo e lo spingono, e una moto spinta lascia le tracce di una moto che corre.</summary>
        static void ClearHorde(bool quiet = false)
        {
            int n = 0;
            foreach (var g in new List<Enemy>(Enemy.All))
            {
                if (g == null || g.maxHp >= 3000f) continue;
                Enemy.Remove(g); n++;
            }
            if (n > 0 && !quiet) Debug.Log($"[PryingEyes] desertotest: tolgo {n} guardie prima della prova");
        }

        IEnumerator MotoTracks()
        {
            var tr = DesertTracks.I;
            Check("tracce: il sistema c'e' (le tracce delle gomme)", tr != null, "DesertTracks.I e' nullo");
            if (tr == null) yield break;
            var pl = Pl; var sky = DesertSky.I;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            godMode = true; pl.invulnerable = true;
            motoWorst = 0f;
            ClearHorde();
            DesertBikes.ReturnHome(pl);                                     // in una catena di fasi la moto e' dove l'ha lasciata quella di prima: si parte dal suo posto, sulla sabbia piana ai piedi della scalinata
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("tracce: c'e' una moto parcheggiata", bike != null);
            if (bike == null) yield break;
            const float hour = 17.3f;
            if (sky != null) sky.SetHour(hour, true);
            tr.Age(10000f);                                                 // via quelle di prima
            yield return null; yield return null;
            Vector3 home = bike.transform.position;

            // ---- a un nastro che non tocca sabbia non si aggiunge niente: in aria, sulla scalinata di pietra
            var cold = new DesertTracks.Pen(0.15f);
            int s0 = tr.Stamped;
            DesertTracks.Lay(cold, new Vector3(home.x, 4000f, home.z), 1f);
            DesertTracks.Lay(cold, new Vector3(home.x + 2f, 4000f, home.z), 1f);
            DesertTracks.Lay(cold, new Vector3(21.5f, 0.2f, 2.4f), 1f);
            DesertTracks.Lay(cold, new Vector3(21.5f, 0.2f, 4.4f), 1f);
            Check("tracce: in aria o sulla pietra della scalinata una ruota non lascia niente e il nastro non parte", tr.Stamped == s0 && !cold.Down, $"{tr.Stamped - s0} pezzi, nastro partito {cold.Down}");

            // ---- parcheggiata: a terra, nessuna traccia
            yield return GoTo(home.x - 2.0f, home.z + 0.3f, 2f);
            ClearHorde();
            yield return new WaitForSeconds(0.9f);
            s0 = tr.Stamped;
            yield return new WaitForSeconds(0.6f);
            Check("tracce: una moto ferma non lascia tracce", tr.Stamped == s0 && tr.Alive == 0, $"{tr.Stamped - s0} pezzi, {tr.Alive} vivi");
            Face(bike.Point);
            yield return null; yield return null;
            Interact.Tick(pl.cam);
            Interact.TryUse();
            yield return null; yield return null;
            Check("tracce: in sella", pl.Riding && bike.rider == pl);
            if (!pl.Riding) yield break;
            yield return new WaitForSeconds(0.5f);
            s0 = tr.Stamped;
            yield return new WaitForSeconds(0.5f);
            Check("tracce: in sella ma ferma neppure", tr.Stamped == s0, $"{tr.Stamped - s0} pezzi");

            // ---- in corsa: prima via dalla scalinata, poi per una cinquantina di metri di traverso al sole (a luce radente ogni solco ha una parete accesa e una in ombra)
            Vector3 south = new Vector3(0f, 0f, -1f);
            DesertBike.botThrottle = 1f;
            yield return RideUntil(() => pl.transform.position.z < home.z - 20f && pl.RideSpeed > 12f, 15f, south);
            var st = DesertClock.Evaluate(hour);
            var sunH = new Vector2(st.sunX, st.sunZ).normalized;
            Vector3 dir = new Vector3(-sunH.y, 0f, sunH.x);
            if (dir.z > 0f) dir = -dir;                                      // verso il deserto, non verso la cattedrale
            tr.Age(10000f);                                                 // via le tracce della partenza: si misurano solo quelle del tratto di prova
            yield return null; yield return null;
            Vector3 start = pl.transform.position, prev = start;
            float run = 0f;
            int sBegin = tr.Stamped;
            bool Enough() { var p = pl.transform.position; run += Flat(p, prev); prev = p; return run > 55f; }
            yield return RideUntil(Enough, 30f, dir);
            Vector3 atEnd = pl.transform.position;
            int made = tr.Stamped - sBegin;
            // frenata: ferma, e le tracce si fermano con lei
            float tb = Time.time;
            while (pl.Riding && Time.time - tb < 6f && Mathf.Abs(pl.RideSpeed) > 0.5f) { DesertBike.botThrottle = -1f; DesertBike.botSteer = 0f; yield return null; }
            DesertBike.botThrottle = 0f; DesertBike.botSteer = 0f;
            yield return new WaitForSeconds(0.3f);
            Vector3 stop = pl.transform.position;
            int madeAll = tr.Stamped - sBegin;
            Debug.Log($"[PryingEyes] desertotest tracce: {made} pezzi in {run:F0} m di strada ({made / Mathf.Max(1f, run):F1} al metro), {madeAll} fino all'arresto, {tr.Alive} vivi, da {start:F1} a {stop:F1}");
            Check("tracce: guidando sulla sabbia le due ruote lasciano le tracce (almeno 1,2 pezzi per metro di strada, in tutto non piu' di 3)", run > 40f && made >= 1.2f * run && made <= 3.0f * run + 4f, $"{made} pezzi in {run:F0} m ({made / Mathf.Max(1f, run):F2} al metro)");

            var list = new List<Vector3>();
            tr.Centres(list);
            int bad = 0, far = 0; float worstGap = 0f, worstFar = 0f;
            foreach (var c in list)
            {
                if (GroundAt(c.x, c.z, out float gy)) { float gap = Mathf.Abs(c.y - gy); worstGap = Mathf.Max(worstGap, gap); if (gap > 0.3f) bad++; }
                // il pezzo sta sulla strada tra il punto di partenza e quello d'arresto: a non piu' di 6 m dalla corda che li unisce
                var ab = stop - start; ab.y = 0f; float ab2 = ab.sqrMagnitude;
                var ac = c - start; ac.y = 0f;
                float t = ab2 > 0.01f ? Mathf.Clamp01(Vector3.Dot(ac, ab) / ab2) : 0f;
                float off = Flat(c, start + ab * t);
                worstFar = Mathf.Max(worstFar, off);
                if (off > 6f) far++;
            }
            Check("tracce: i pezzi sono posati sul terreno (almeno il 98 % a meno di 30 cm dal suolo; un raggio dall'alto che trova un sasso o un cactus non e' il suolo)", list.Count > 20 && bad * 50 <= list.Count, $"{list.Count} pezzi, {bad} troppo lontani dal suolo, il peggiore a {worstGap:F2} m");
            Check("tracce: stanno sulla strada fatta dalla moto (a meno di 6 m dalla corda tra la partenza e l'arresto)", list.Count > 20 && far == 0, $"{far} fuori, il peggiore a {worstFar:F1} m dalla corda");
            bool got = tr.Newest(out var last, out float width, out float length);
            Check("tracce: l'ultimo pezzo e' sotto la moto, largo come una gomma (20-34 cm) e lungo quanto la strada fatta (0,8-1,6 m)",
                got && Flat(last, stop) < 3.5f && width > 0.2f && width < 0.34f && length > 0.75f && length < 1.7f, got ? $"a {Flat(last, stop):F1} m dalla moto, largo {width:F2}, lungo {length:F2} m" : "nessuno");
            Check("tracce: dall'arresto non se ne fanno altre", tr.Stamped - sBegin == madeAll, $"{tr.Stamped - sBegin - madeAll} in piu'");

            // ---- scesi, le foto: la moto resta ferma in fondo alla scia (dalla sella si vedrebbero il manubrio e il tachimetro, non le tracce)
            if (pl.Riding) { pl.RideRequestDismount(); yield return WaitFor(() => !pl.Riding, 10f); yield return new WaitForSeconds(0.6f); }
            DesertBike.BotReset();
            if (shots && list.Count > 20)
            {
                Vector3 trailDir = stop - start; trailDir.y = 0f; trailDir.Normalize();
                Vector3 side = new Vector3(trailDir.z, 0f, -trailDir.x);
                Vector3 mid = list[list.Count / 2];
                var back = start - trailDir * 2f;
                yield return GoTo(back.x, back.z, 2f);
                yield return Snap("tracce_a_scia", hour, new Vector3(stop.x, DesertWorld.Height(stop.x, stop.z) + 0.6f, stop.z), 1.0f);
                var near = mid + side * 2.6f;
                yield return GoTo(near.x, near.z, 2f);
                var midGround = new Vector3(mid.x, mid.y, mid.z);
                yield return Snap("tracce_b_vicino", hour, midGround, 1.0f);
                yield return Snap("tracce_c_vicino_giorno", 11.5f, midGround, 1.0f);
                var close = mid + side * 1.4f - trailDir * 1.2f;
                yield return GoTo(close.x, close.z, 2f);
                yield return Snap("tracce_d_tassellato", hour, midGround, 1.0f);
            }

            // ---- il vento le cancella
            tr.Age(DesertTracks.Life * 0.5f);
            yield return null; yield return null;
            int half = tr.Alive;
            tr.Age(DesertTracks.Life * 0.5f + 2f);
            yield return null; yield return null;
            Check("tracce: dopo un minuto il vento le ha cancellate tutte", tr.Alive == 0 && half > 0, $"{tr.Alive} ancora vive, {half} a meta' del tempo");

            if (sky != null) sky.Frozen = false;
            godMode = true;
        }
    }
}
#endif

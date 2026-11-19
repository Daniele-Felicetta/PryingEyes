#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly moto: la moto del deserto (DesertBike, PlayerController.Ride.cs) con il controller vero e comandi forzati (DesertBike.bot*): c'e' ai piedi della scalinata e poggia a terra, Usa
    /// la sceglie e il giocatore sale (collider spento, arma nascosta), gas, velocita' massima, turbo, sterzo (la moto si inclina), freno e retromarcia, freno a mano (si deraPa), urto contro il muro
    /// della cattedrale (danno e arresto), discesa da fermi e frenando da soli, volo e atterraggio, fari di notte, un percorso lungo di oltre un chilometro in cui il deserto si costruisce davanti
    /// (nessuna caduta, nessun fotogramma lungo, velocita' media alta), la morte in sella lontano da casa (la moto resta li' e, alla rinascita, torna a casa), la moto di un compagno (puppet) e,
    /// con -desertshots, le fotografie.</summary>
    public sealed partial class DesertTest
    {
        float motoWorst;

        static DesertBike FirstBike()
        {
            foreach (var b in DesertBike.All) if (b != null && !b.puppet) return b;
            return null;
        }

        static bool GroundAt(float x, float z, out float y)
        {
            y = DesertWorld.Height(x, z);
            if (Physics.Raycast(new Vector3(x, y + 40f, z), Vector3.down, out var h, 90f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) { y = h.point.y; return true; }
            return false;
        }

        /// <summary>Quanto la moto sta sopra il terreno (m): positivo se vola, negativo se e' sprofondata. Il collider della moto si spegne un attimo: il raggio non deve trovarlo.</summary>
        static float Gap(DesertBike b)
        {
            var box = b.GetComponent<BoxCollider>();
            bool was = box != null && box.enabled;
            if (box != null) box.enabled = false;
            bool hit = Physics.Raycast(b.transform.position + Vector3.up * 2f, Vector3.down, out var h, 6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
            if (box != null) box.enabled = was;
            return hit ? b.transform.position.y - h.point.y : 99f;
        }

        static float Feel(Vector3 from, Vector3 dir, float dist)
        {
            if (Physics.SphereCast(from + Vector3.up * 0.9f, 0.5f, dir, out var h, dist, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) && h.normal.y < 0.6f) return h.distance;
            return dist;
        }

        /// <summary>Il pilota automatico delle prove: punta verso 'dir' e schiva quello che c'e' davanti (tre antenne a 30 m, un muro o un sasso e' un ostacolo, una duna no).</summary>
        void Autopilot(Vector3 dir)
        {
            var pl = Pl;
            float h = pl.RideHeading;
            Vector3 pos = pl.transform.position, fwd = new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h));
            float diff = Mathf.DeltaAngle(h * Mathf.Rad2Deg, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
            float steer = Mathf.Clamp(diff / 25f, -1f, 1f);
            float dC = Feel(pos, fwd, 30f), dL = Feel(pos, Quaternion.Euler(0f, -28f, 0f) * fwd, 30f), dR = Feel(pos, Quaternion.Euler(0f, 28f, 0f) * fwd, 30f);
            if (dC < 26f) steer = dL > dR ? -1f : 1f;
            else if (dL < 12f) steer = Mathf.Max(steer, 0.6f);
            else if (dR < 12f) steer = Mathf.Min(steer, -0.6f);
            DesertBike.botSteer = steer;
        }

        /// <summary>Guida con il pilota automatico verso 'dir' finche' 'until' e' vero (o scade 'timeout').</summary>
        IEnumerator RideUntil(Func<bool> until, float timeout, Vector3 dir)
        {
            float t0 = Time.time;
            while (Time.time - t0 < timeout && Pl.Riding)
            {
                bool done; try { done = until(); } catch { done = false; }
                if (done) yield break;
                Autopilot(dir);
                motoWorst = Mathf.Max(motoWorst, Time.unscaledDeltaTime);
                yield return null;
            }
        }

        IEnumerator Moto()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            motoWorst = 0f;
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("moto: c'e' una moto parcheggiata", bike != null);
            if (bike == null) yield break;
            Vector3 home = bike.transform.position;
            float toStairs = Flat(home, new Vector3(HubPlan.SpawnX, home.y, -5.8f));
            Check("moto: sta ai piedi della scalinata, verso il deserto (tra 3 e 16 m dal suo piede)", toStairs > 3f && toStairs < 16f, $"{toStairs:F1} m, in {home:F1}");
            Vector3 south = new Vector3(0f, 0f, -1f);

            // ---- parcheggiata: a terra, con il suo collider, e la scelta di Usa
            yield return GoTo(home.x - 2.2f, home.z + 0.3f, 2f);
            yield return new WaitForSeconds(0.9f);
            float gap = Gap(bike);
            Check("moto: poggia sul terreno", bike.Settled && Mathf.Abs(gap) < 0.2f, $"scarto {gap:F2} m, posata {bike.Settled}");
            Check("moto: da parcheggiata ha il collider", bike.ColliderOn);
            Check("moto: i fari sono spenti, il motore e' muto", !bike.HeadlightOn && bike.EngineVolume < 0.02f, $"volume {bike.EngineVolume:F3}");
            if (shots)
            {
                yield return Snap("moto_a_fronte", 10.5f, bike.Point + Vector3.up * 0.05f, 1.0f);
                yield return GoTo(home.x + 3.0f, home.z - 1.2f, 2f);
                yield return Snap("moto_b_lato", 10.5f, bike.Point, 1.0f);
                yield return GoTo(home.x + 0.5f, home.z + 3.4f, 2f);
                yield return Snap("moto_c_dietro", 10.5f, bike.Point, 1.0f);
                // la scalinata dall'alto: la moto laggiu', col deserto dietro
                pl.Teleport(new Vector3(21.5f, 0.2f, 2.4f));
                yield return new WaitForSeconds(0.5f);
                yield return Snap("moto_d_dalla_scalinata", 11.0f, bike.Point, 1.0f);
            }
            yield return GoTo(home.x - 2.0f, home.z + 0.2f, 2f);
            yield return new WaitForSeconds(0.4f);
            Face(bike.Point);
            yield return null; yield return null;
            Interact.Tick(pl.cam);
            var near = Interact.Current as DesertBike;
            Check("moto: guardandola da vicino Usa la sceglie", near == bike && Interact.Current.Prompt == "Sali in moto", near == null ? "niente" : near.name);
            Interact.TryUse();
            yield return null; yield return null;
            Check("moto: Usa mette in sella", pl.Riding && bike.rider == pl && pl.Bike == bike);
            Check("moto: in sella il collider si spegne e l'arma sparisce", !bike.ColliderOn && !pl.weapon.ViewRootT.gameObject.activeSelf);
            Check("moto: il corpo del giocatore sta sulla moto", Flat(pl.transform.position, bike.transform.position) < 0.3f, $"{Flat(pl.transform.position, bike.transform.position):F2} m");
            Check("moto: da ferma non si muove", Mathf.Abs(pl.RideSpeed) < 0.3f);
            pl.yaw = pl.RideHeading; pl.pitch = 0.16f;
            yield return new WaitForSeconds(0.8f);          // il passaggio della camera sull'occhio del pilota
            if (shots) Shot("moto_e_cabina");

            // ---- gas
            Vector3 p0 = pl.transform.position; float t0 = Time.time;
            DesertBike.botThrottle = 1f;
            yield return RideUntil(() => pl.RideSpeed > 14f, 10f, south);
            float tAcc = Time.time - t0;
            Check("moto: col gas a tavoletta arriva a 50 km/h in meno di 4 s", pl.RideSpeed > 14f && tAcc < 4f, $"{pl.RideSpeed * 3.6f:F0} km/h in {tAcc:F1} s");
            Vector3 fwd = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading));
            Vector3 moved = pl.transform.position - p0; moved.y = 0f;
            Check("moto: va nel verso in cui punta", moved.sqrMagnitude > 4f && Vector3.Dot(moved.normalized, fwd) > 0.9f, $"spostata di {moved.magnitude:F1} m, coseno {Vector3.Dot(moved.normalized, fwd):F2}");
            Check("moto: il motore canta e le ruote girano", bike.EngineVolume > 0.12f && bike.EnginePitch > 0.9f && Mathf.Abs(bike.SteerVisual) < 30f, $"volume {bike.EngineVolume:F2}, tono {bike.EnginePitch:F2}");
            yield return RideUntil(() => pl.RideSpeed > 21f, 14f, south);
            float vTop = pl.RideSpeed;
            Check("moto: raggiunge la velocita' massima (21-24,5 m/s)", vTop > 21f && vTop < 24.5f, $"{vTop:F1} m/s = {vTop * 3.6f:F0} km/h");
            if (shots) Shot("moto_f_in_marcia");
            DesertBike.botBoost = true;
            yield return RideUntil(() => pl.RideSpeed > 27f, 14f, south);
            float vBoost = pl.RideSpeed;
            yield return RideUntil(() => false, 2.0f, south);
            Check("moto: col turbo supera i 27 m/s senza arrivare a 32,5", vBoost > 27f && pl.RideSpeed < 32.5f, $"{vBoost:F1} poi {pl.RideSpeed:F1} m/s");
            if (shots) Shot("moto_g_turbo");
            DesertBike.botBoost = false;

            // ---- sterzo: la moto gira e si inclina dalla parte della curva
            float h0 = pl.RideHeading, bestRoll = 0f;
            DesertBike.botSteer = 1f;
            t0 = Time.time;
            while (Time.time - t0 < 4f && Mathf.Abs(Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, pl.RideHeading * Mathf.Rad2Deg)) < 55f)
            {
                bestRoll = Mathf.Min(bestRoll, bike.RollVisual);
                motoWorst = Mathf.Max(motoWorst, Time.unscaledDeltaTime);
                yield return null;
            }
            float turned = Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, pl.RideHeading * Mathf.Rad2Deg);
            Check("moto: sterzando a destra gira a destra e si inclina a destra", turned > 40f && bestRoll < -8f, $"girata di {turned:F0} gradi, rollio {bestRoll:F1}");
            Check("moto: il manubrio e le ruote sono girati", bike.SteerVisual > 10f, $"{bike.SteerVisual:F0} gradi");
            if (shots) Shot("moto_h_in_curva");
            DesertBike.botSteer = 0f;

            // ---- freno e retromarcia
            DesertBike.botThrottle = -1f;
            t0 = Time.time;
            yield return RideUntil(() => pl.RideSpeed < 1f, 8f, new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading)));
            Check("moto: frenando si ferma in meno di 4 s", pl.RideSpeed < 1f && Time.time - t0 < 4f, $"{pl.RideSpeed:F1} m/s dopo {Time.time - t0:F1} s");
            yield return RideUntil(() => pl.RideSpeed < -2f, 5f, new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading)));
            Check("moto: tenendo il freno va in retromarcia", pl.RideSpeed < -2f, $"{pl.RideSpeed:F1} m/s");
            DesertBike.botThrottle = 0f;
            yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 0.5f, 6f, south);
            Check("moto: lasciato il gas si ferma da sola", Mathf.Abs(pl.RideSpeed) < 0.5f, $"{pl.RideSpeed:F2} m/s");

            // ---- freno a mano: la velocita' resta dov'era e la moto si mette di traverso
            Vector3 dirNow = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading));
            DesertBike.botThrottle = 1f;
            yield return RideUntil(() => pl.RideSpeed > 14f, 10f, dirNow);
            float slipMax = 0f; Vector3 last = pl.transform.position;
            DesertBike.botSteer = 1f; DesertBike.botHandbrake = true;
            t0 = Time.time;
            while (Time.time - t0 < 1.6f && pl.Riding)
            {
                yield return null;
                Vector3 now = pl.transform.position, v = now - last; v.y = 0f; last = now;
                if (v.magnitude > 0.002f)
                {
                    float hh = pl.RideHeading;
                    slipMax = Mathf.Max(slipMax, Vector3.Angle(v, new Vector3(Mathf.Sin(hh), 0f, Mathf.Cos(hh))));
                }
            }
            DesertBike.botHandbrake = false; DesertBike.botSteer = 0f; DesertBike.botThrottle = 0f;
            Check("moto: col freno a mano in curva la moto deraPa (la velocita' non segue la rotta)", slipMax > 6f, $"scarto massimo {slipMax:F1} gradi");
            yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 1f, 8f, south);

            // ---- urto contro il muro della cattedrale: danno e arresto
            pl.AbortRide();
            GroundAt(14.2f, 4.0f, out float wy);
            bike.Park(new Vector3(14.2f, wy, 4.0f), 0f);              // verso nord, contro il muro del nartece (z = 17,85)
            godMode = false; pl.invulnerable = false;
            yield return null;
            float hpBefore = pl.hp + pl.shield;
            pl.Mount(bike);
            yield return null;
            DesertBike.botThrottle = 1f;
            yield return WaitFor(() => !pl.Riding || pl.RideCrashCooldown > 0f, 9f);
            DesertBike.botThrottle = 0f;
            yield return new WaitForSeconds(0.4f);
            Check("moto: contro il muro a piu' di 50 km/h c'e' l'urto, che ferisce", pl.RideCrashCooldown > 0f || pl.hp + pl.shield < hpBefore - 3f, $"vita {hpBefore:F0} -> {pl.hp + pl.shield:F0}");
            Check("moto: dopo l'urto la moto e' ferma o quasi", Mathf.Abs(pl.RideSpeed) < 6f, $"{pl.RideSpeed:F1} m/s");
            Check("moto: l'urto non uccide con la vita piena", pl.alive && !pl.Downed, $"vita {pl.hp:F0}");
            godMode = true; pl.invulnerable = true;
            DesertBike.BotReset();
            if (pl.Riding) pl.AbortRide();

            // ---- in aria e atterraggio (a mezz'aria il terreno non c'e': si vola e si ricade)
            yield return GoTo(home.x - 4f, home.z - 6f, 2f);
            bike.Park(pl.transform.position, Mathf.PI);
            pl.Mount(bike);
            yield return new WaitForSeconds(0.5f);
            var up = pl.transform.position + Vector3.up * 7f;
            pl.Teleport(up);
            yield return null; yield return null;
            Check("moto: sollevata nel vuoto stacca le ruote da terra", !pl.RideGrounded);
            yield return WaitFor(() => pl.RideGrounded, 4f);
            Check("moto: ricade e atterra", pl.RideGrounded && Mathf.Abs(Gap(bike)) < 0.6f, $"scarto {Gap(bike):F2} m");
            yield return new WaitForSeconds(0.4f);
            if (pl.Riding) { DesertBike.botThrottle = 0f; yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 0.6f, 4f, south); }

            // ---- fari di notte
            var sky = DesertSky.I;
            if (sky != null)
            {
                sky.SetHour(23f, true);
                yield return new WaitForSeconds(1.2f);
                Check("moto: di notte il faro si accende", bike.HeadlightOn);
                if (shots)
                {
                    DesertBike.botThrottle = 1f;
                    yield return RideUntil(() => pl.RideSpeed > 12f, 8f, south);
                    Shot("moto_i_notte_in_marcia");
                    DesertBike.botThrottle = 0f;
                }
                sky.SetHour(12f, true);
                yield return new WaitForSeconds(1.2f);
                Check("moto: di giorno il faro e' spento", !bike.HeadlightOn);
                sky.Frozen = false;
            }

            // ---- scendere frenando da soli
            if (pl.Riding)
            {
                DesertBike.botThrottle = 1f;
                yield return RideUntil(() => pl.RideSpeed > 12f, 8f, south);
                pl.RideRequestDismount();
                DesertBike.botThrottle = 0f;
                yield return WaitFor(() => !pl.Riding, 10f);
            }
            else
            {
                Check("moto: in sella per la prova della discesa", false);
            }
            Check("moto: scende da sola appena si ferma", !pl.Riding && bike.rider == null);
            yield return null; yield return null;
            float away = Flat(pl.transform.position, bike.transform.position);
            Check("moto: il pilota scende di lato, a un metro o poco piu', in uno spazio libero", away > 0.7f && away < 2.4f && !Physics.CheckCapsule(pl.transform.position + Vector3.up * 0.45f, pl.transform.position + Vector3.up * 1.3f, 0.3f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore), $"{away:F2} m");
            Check("moto: da parcheggiata torna ad avere il collider, l'arma torna in mano", bike.ColliderOn && pl.weapon.ViewRootT.gameObject.activeSelf);
            Check("moto: da scesi si cammina di nuovo (velocita' del corpo zero)", pl.SpeedH < 0.5f);

            // ---- il percorso lungo: il deserto si costruisce davanti, la moto non cade nel vuoto e non si blocca
            yield return GoTo(home.x, home.z - 4f, 2f);
            bike.Park(pl.transform.position, Mathf.PI);
            pl.Mount(bike);
            yield return new WaitForSeconds(0.6f);
            Vector3[] legs = { new Vector3(0f, 0f, -380f), new Vector3(380f, 0f, 0f), new Vector3(0f, 0f, 380f) };
            Vector3 cur = pl.transform.position, start = cur;
            float tRoute = Time.time, maxDrop = 0f, worstBefore = motoWorst; motoWorst = 0f;
            int arrived = 0;
            DesertBike.botThrottle = 1f;
            for (int leg = 0; leg < legs.Length && pl.Riding; leg++)
            {
                Vector3 target = cur + legs[leg];
                DesertBike.botBoost = leg == legs.Length - 1;
                float lt0 = Time.time;
                while (pl.Riding && Time.time - lt0 < 45f)
                {
                    Vector3 pos = pl.transform.position;
                    if (Flat(pos, target) < 25f) { arrived++; break; }
                    Vector3 want = target - pos; want.y = 0f;
                    Autopilot(want.normalized);
                    float ground = DesertWorld.Height(pos.x, pos.z);
                    maxDrop = Mathf.Max(maxDrop, ground - pos.y);
                    motoWorst = Mathf.Max(motoWorst, Time.unscaledDeltaTime);
                    yield return null;
                }
                cur = pl.transform.position;
            }
            float routeSecs = Time.time - tRoute;
            float routeDist = 0f;
            Vector3 d3 = cur - start; routeDist = new Vector2(d3.x, d3.z).magnitude;
            Debug.Log($"[PryingEyes] desertotest moto percorso: {arrived}/{legs.Length} tratti, {routeSecs:F0} s, {DesertTerrain.I?.ChunkCount} blocchi di sabbia, {Dd?.LoadedSites} luoghi caricati, il fotogramma peggiore {motoWorst * 1000f:F0} ms (prima {worstBefore * 1000f:F0})");
            Check("moto: percorre i tre tratti (oltre un chilometro) in sella", arrived == legs.Length && pl.Riding, $"{arrived}/{legs.Length}, a {routeDist:F0} m dal via in linea d'aria, {routeSecs:F0} s");
            Check("moto: lungo il percorso non cade mai sotto la sabbia", maxDrop < 6f, $"{maxDrop:F1} m sotto il terreno al massimo");
            Check("moto: lungo il percorso nessun fotogramma oltre mezzo secondo (i blocchi nascono in tempo)", motoWorst < 0.5f, $"{motoWorst * 1000f:F0} ms");
            DesertBike.botBoost = false; DesertBike.botThrottle = 0f;
            yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 1f, 8f, south);

            // ---- morire in sella lontano da casa: chi va a terra e' sbalzato e la moto resta li' dov'e'; chi rinasce alla scalinata la ritrova dov'era nata
            Vector3 farAt = bike.transform.position;
            float awayHome = Flat(farAt, bike.home);
            if (pl.Riding)
            {
                godMode = false; pl.invulnerable = false;
                pl.Hurt(99999f);
                yield return null; yield return null;
                Check("moto: andando a terra in sella si e' sbalzati e la moto resta dov'e'", pl.Downed && !pl.Riding && bike.rider == null && Flat(bike.transform.position, farAt) < 2f, $"a terra {pl.Downed}, in sella {pl.Riding}, spostata di {Flat(bike.transform.position, farAt):F1} m");
                pl.BleedOutNow();
                yield return null; yield return null;
                yield return WaitFor(() => pl.alive, 12f);
                pl.invulnerable = true;
                yield return new WaitForSeconds(0.3f);
                Check("moto: chi rinasce alla scalinata ritrova la sua moto dov'era nata", awayHome > 100f && Flat(bike.transform.position, bike.home) < 0.6f && bike.rider == null && bike.ColliderOn, $"lasciata a {awayHome:F0} m da casa, ora a {Flat(bike.transform.position, bike.home):F1} m");
                yield return new WaitForSeconds(2.8f);                    // la tregua della rinascita finisce
                pl.invulnerable = true; godMode = true;
            }
            else Check("moto: in sella alla fine del percorso per la prova della morte", false);

            // ---- la moto di un compagno: senza pilota vero, posata dalla rete (cooperativa), non si monta
            yield return GoTo(home.x + 14f, home.z - 22f, 2f);
            Vector3 pp = pl.transform.position + new Vector3(5f, 0f, 0f);
            GroundAt(pp.x, pp.z, out float py); pp.y = py;
            var pup = DesertBike.Create(null, pp, 0f, "Moto di un compagno");
            pup.SetPuppet(true);
            // a second of game time, not a number of frames: without a window a frame lasts a millisecond and the roll (a smoothing in time) would hardly start
            for (float t = 0f; t < 1.2f; t += Mathf.Max(Time.deltaTime, 0.001f)) { pup.Pose(pp, 0f, true, new Vector3(0f, 0f, 18f), 0.5f, 1f, false, false, false, Mathf.Max(Time.deltaTime, 0.001f)); yield return null; }
            Check("moto: quella di un compagno non si monta e non ha collider", pup.puppet && pup.Prompt == null && !pup.ColliderOn);
            Check("moto: quella di un compagno in corsa ha le ruote che girano e si inclina", Mathf.Abs(pup.WheelSpin) > 0.1f && pup.RollVisual < -3f, $"ruota {pup.WheelSpin:F0}, rollio {pup.RollVisual:F1}");
            if (shots)
            {
                if (sky != null) sky.SetHour(17f, true);
                for (float t = 0f; t < 1.8f; t += Mathf.Max(Time.deltaTime, 0.001f))
                {
                    pup.Pose(pp, 0f, true, new Vector3(0f, 0f, 18f), 0.5f, 1f, false, false, false, Mathf.Max(Time.deltaTime, 0.001f));
                    Face(pup.Point);
                    yield return null;
                }
                Shot("moto_l_compagno_in_corsa");
                if (sky != null) sky.Frozen = false;
            }
            pup.SetPuppet(false);
            UnityEngine.Object.Destroy(pup.gameObject);
            yield return null;
            DesertBike.BotReset();
            godMode = true;
        }
    }
}
#endif

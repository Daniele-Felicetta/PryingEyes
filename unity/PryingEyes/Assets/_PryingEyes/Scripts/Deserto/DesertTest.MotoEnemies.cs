#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly motonemici: sparare dalla sella contro nemici veri (la fase motofuoco spara a una lastra che conta i colpi). Con il fucile in sella, un monaco sulla sabbia a sedici metri davanti alla
    /// moto ferma: i colpi lo feriscono (la vita cala, i colpi a segno aumentano) e dopo qualche secondo cade, e la sua morte da' i punti; poi un monaco fermo di lato alla strada, a nove metri, e la moto che
    /// gli passa davanti a piu' di cinquanta chilometri all'ora sparando: almeno un colpo a segno anche in corsa. Il pilota automatico mira il petto del monaco a ogni fotogramma (togliendo il rinculo, che il
    /// bot non tira giu') perche' si prova la strada del colpo fino al nemico e non la mira.</summary>
    public sealed partial class DesertTest
    {
        /// <summary>Punta la visuale del pilota in sella (la guardata e' del mondo: l'angolo con la moto lo limita il codice della guida) sul bersaglio, compensando il rinculo in corso.</summary>
        void AimSaddle(Vector3 target)
        {
            var pl = Pl;
            Vector3 d = target - pl.cam.position;
            pl.yaw = Mathf.Atan2(d.x, d.z);
            pl.pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) + pl.ViewKickPitch * Mathf.Deg2Rad;
        }

        IEnumerator MotoEnemies()
        {
            var pl = Pl;
            var w = pl.weapon;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset(); Weapon.rideBotFire = false;
            ClearHorde();
            DesertBikes.ReturnHome(pl);                                     // la moto e' dove l'ha lasciata la fase di prima (in catena: in cima a una duna a duecento metri): si parte dal suo posto, dove il terreno e' piano e il bersaglio si vede
            // niente potenziamenti a caso: il primo monaco ucciso poteva lasciare cadere lo Sguardo (ogni colpo uccide), il pilota lo raccoglieva e il secondo monaco moriva al primo colpo a segno (vita 200 -> -1)
            // o, con la Campana, senza nessun colpo; le prove dei round e del coltello lo spengono allo stesso modo
            bool dropsWere = Drops.disabled;
            Drops.ResetRun(); Drops.disabled = true;
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("motonemici: c'e' una moto parcheggiata", bike != null);
            if (bike == null) { Drops.disabled = dropsWere; yield break; }
            godMode = true; pl.invulnerable = true;
            Vector3 home = bike.transform.position;
            Vector3 south = new Vector3(0f, 0f, -1f);
            w.Equip(WeaponDefs.IndexOf("ar9_malleus"));
            w.mag = w.magSize; w.reserve = 300;
            yield return GoTo(home.x - 2.2f, home.z + 0.3f, 2f);
            yield return new WaitForSeconds(1.0f);
            Face(bike.Point);
            yield return null; yield return null;
            Interact.Tick(pl.cam);
            Interact.TryUse();
            yield return null; yield return null;
            Check("motonemici: in sella col fucile", pl.Riding && w.RideMode);
            if (!pl.Riding) { Drops.disabled = dropsWere; yield break; }

            // ---- via dalla scalinata, su sabbia aperta, e fermi
            DesertBike.botThrottle = 1f;
            yield return RideUntil(() => pl.transform.position.z < home.z - 30f, 15f, south);
            float tb = Time.time;
            while (pl.Riding && Time.time - tb < 6f && Mathf.Abs(pl.RideSpeed) > 0.4f) { DesertBike.botThrottle = -1f; DesertBike.botSteer = 0f; yield return null; }
            DesertBike.botThrottle = 0f; DesertBike.botSteer = 0f;
            yield return new WaitForSeconds(0.4f);
            Vector3 fwd = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading)), right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 at = pl.transform.position;

            // ---- fermi: un monaco a sedici metri davanti (solo lui: le guardie dei luoghi caricati guidando fin qui si tolgono, i loro colpi a segno falserebbero il conto)
            ClearHorde();
            Vector3 spot = at + fwd * 16f;
            GroundAt(spot.x, spot.z, out float gy); spot.y = gy;
            var mon = Enemy.Spawn(Enemy.EnemyType.Monaco, spot);
            yield return new WaitForSeconds(0.3f);
            Check("motonemici: il monaco c'e', vivo e a sedici metri", mon != null && mon.hp > 0f && Flat(mon.transform.position, at) > 12f && Flat(mon.transform.position, at) < 20f, mon == null ? "nessuno" : $"vita {mon.hp:F0}, a {Flat(mon.transform.position, at):F1} m");
            if (mon == null) { Drops.disabled = dropsWere; yield break; }
            float hp0 = mon.hp, maxHp = mon.maxHp;
            int sc0 = Gm.score, hit0 = GameManager.shotsHit, shot0 = GameManager.shotsFired;
            Weapon.rideBotFire = true;
            float t0 = Time.time, tHurt = -1f;
            while (Time.time - t0 < 9f && mon != null && mon.hp > 0f)
            {
                AimSaddle(mon.AimPoint);
                if (tHurt < 0f && mon.hp < hp0 - 0.5f) tHurt = Time.time - t0;
                yield return null;
            }
            Weapon.rideBotFire = false;
            bool dead = mon == null || mon.hp <= 0f;
            int fired = GameManager.shotsFired - shot0, hits = GameManager.shotsHit - hit0;
            Debug.Log($"[PryingEyes] desertotest motonemici: fermi, {fired} colpi, {hits} a segno, ferito dopo {tHurt:F2} s, {(dead ? "caduto" : "ancora in piedi")} dopo {Time.time - t0:F1} s, vita {hp0:F0} -> {(mon != null ? mon.hp : 0f):F0} di {maxHp:F0}");
            Check("motonemici: sparando dalla sella il monaco e' ferito (la vita cala e i colpi a segno aumentano)", tHurt >= 0f && hits >= 1, $"ferito dopo {tHurt:F2} s, {hits} a segno su {fired}");
            Check("motonemici: sparando dalla sella il monaco cade in meno di nove secondi (e lo spara una raffica, non un colpo)", dead && fired >= 3, $"{(dead ? "caduto" : "in piedi")} dopo {Time.time - t0:F1} s, {fired} colpi, vita {(mon != null ? mon.hp : 0f):F0}");
            yield return new WaitForSeconds(0.5f);
            Check("motonemici: la sua morte da' i punti", Gm.score > sc0, $"punti {sc0} -> {Gm.score}");
            Check("motonemici: il pilota e' ancora in sella, fermo", pl.Riding && Mathf.Abs(pl.RideSpeed) < 1f, $"in sella {pl.Riding}, {pl.RideSpeed:F1} m/s");
            if (mon != null) Enemy.Remove(mon);

            // ---- in corsa: un monaco fermo a nove metri di lato alla strada, la moto gli passa davanti sparando
            yield return new WaitForSeconds(1.6f);                       // l'arma torna giu'
            at = pl.transform.position;
            fwd = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading)); right = new Vector3(fwd.z, 0f, -fwd.x);
            ClearHorde();
            Vector3 spot2 = at + fwd * 60f + right * 9f;
            GroundAt(spot2.x, spot2.z, out float gy2); spot2.y = gy2;
            var mon2 = Enemy.Spawn(Enemy.EnemyType.Monaco, spot2);
            yield return new WaitForSeconds(0.3f);
            if (mon2 == null) { Check("motonemici: il secondo monaco c'e'", false); Drops.disabled = dropsWere; yield break; }
            float hp2 = mon2.hp;
            Vector3 pass = at + fwd * 120f;                               // la moto va dritta, oltre il monaco
            int hit1 = GameManager.shotsHit, shot1 = GameManager.shotsFired;
            DesertBike.botThrottle = 1f;
            float vTop = 0f, kMax = 0f, tEnd = Time.time + 14f;
            bool firing = false;
            while (pl.Riding && Time.time < tEnd && mon2 != null && mon2.hp > 0f)
            {
                float along = Vector3.Dot(mon2.transform.position - pl.transform.position, fwd);      // quanta strada manca alla sua altezza (negativo: gia' passato)
                var wantAt = pass; Vector3 dd = wantAt - pl.transform.position; dd.y = 0f;
                float diff = Mathf.DeltaAngle(pl.RideHeading * Mathf.Rad2Deg, Mathf.Atan2(dd.x, dd.z) * Mathf.Rad2Deg);
                DesertBike.botSteer = Mathf.Clamp(diff / 25f, -1f, 1f);
                vTop = Mathf.Max(vTop, pl.RideSpeed); kMax = Mathf.Max(kMax, w.RideSpreadFactor);
                // si spara nell'ultimo tratto prima del monaco e un po' dopo, mirando a lui
                bool inRange = along < 32f && along > -22f;
                if (inRange != firing) { firing = inRange; Weapon.rideBotFire = firing; }
                if (firing) AimSaddle(mon2.AimPoint);
                else { pl.yaw = pl.RideHeading; pl.pitch = 0f; }
                motoWorst = Mathf.Max(motoWorst, Time.unscaledDeltaTime);
                if (along < -26f) break;
                yield return null;
            }
            Weapon.rideBotFire = false;
            DesertBike.botThrottle = -1f;
            float tStop = Time.time;
            while (pl.Riding && Time.time - tStop < 8f && Mathf.Abs(pl.RideSpeed) > 0.5f) { DesertBike.botSteer = 0f; yield return null; }
            DesertBike.botThrottle = 0f; DesertBike.botSteer = 0f;
            int hits2 = GameManager.shotsHit - hit1, fired2 = GameManager.shotsFired - shot1;
            Debug.Log($"[PryingEyes] desertotest motonemici: in corsa fino a {vTop * 3.6f:F0} km/h, dispersione fino a x{kMax:F2}, {fired2} colpi, {hits2} a segno, vita del monaco {hp2:F0} -> {(mon2 != null ? mon2.hp : 0f):F0}");
            bool down2 = mon2 == null || mon2.hp <= 0f;                   // se il monaco e' gia' caduto il tiro finisce prima (una testa colpita fa piu' danno): allora i cinque colpi non servono
            Check("motonemici: la moto passa davanti al monaco a oltre 50 km/h e spara (cinque colpi, o meno se il monaco e' gia' caduto)", vTop * 3.6f > 50f && (fired2 >= 5 || (down2 && fired2 >= 1)), $"fino a {vTop * 3.6f:F0} km/h, {fired2} colpi");
            Check("motonemici: sparando in corsa almeno un colpo ferisce il monaco", hits2 >= 1 && mon2 != null && mon2.hp < hp2, $"{hits2} a segno su {fired2}, vita {hp2:F0} -> {(mon2 != null ? mon2.hp : 0f):F0}");
            if (mon2 != null) Enemy.Remove(mon2);

            pl.RideRequestDismount();
            yield return WaitFor(() => !pl.Riding, 10f);
            yield return new WaitForSeconds(0.4f);
            DesertBike.BotReset();
            godMode = true;
            Drops.disabled = dropsWere;
        }
    }
}
#endif

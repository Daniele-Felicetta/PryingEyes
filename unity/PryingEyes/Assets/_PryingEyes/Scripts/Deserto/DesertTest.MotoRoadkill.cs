#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly motoinvesti: la moto contro chi cammina. Prima di questa fase un monaco sulla strada era un muro: la capsula del nemico bloccava il corpo del pilota, la frenata contava come urto contro
    /// la roccia (fino a 45 punti di danno al pilota) e il monaco non si faceva niente. Qui la moto parte da ferma sulla sabbia piana e va, con i comandi forzati e il gas che tiene 61 km/h, contro: un monaco
    /// solo (cade, la moto non si ferma e non e' un urto, il pilota non si fa male, il corpo vola avanti, la morte da' i punti), una fila di cinque a tre metri e mezzo uno dall'altro (la fila cade, la moto
    /// rallenta ma corre ancora), un abate (e' grosso: ferito e non morto, e costa piu' velocita') e, a 11 km/h, un monaco solo (a passo d'uomo resta un ostacolo: niente danno, niente investimento). Ogni
    /// passata riparte dallo stesso punto, con la vita piena.</summary>
    public sealed partial class DesertTest
    {
        bool plowCrash;
        float plowVIn, plowVMin, plowLoss;
        readonly float[] plowRing = new float[12];
        int plowRingI;
        readonly Queue<string> plowTrace = new Queue<string>();

        static Enemy SpawnOnGround(Enemy.EnemyType t, Vector3 p)
        {
            GroundAt(p.x, p.z, out float gy); p.y = gy;
            return Enemy.Spawn(t, p);
        }

        /// <summary>Rimette il pilota in sella alla moto, ferma in 'start' e rivolta a 'heading', con la vita piena.</summary>
        IEnumerator PlowStart(DesertBike bike, Vector3 start, float heading)
        {
            var pl = Pl;
            if (pl.Riding) pl.AbortRide();
            ClearHorde();
            GroundAt(start.x, start.z, out float gy); start.y = gy;
            bike.Park(start, heading);
            yield return null;
            pl.Mount(bike);
            yield return null; yield return null;
            DesertBike.BotReset();
            pl.hp = 100f; pl.shield = 50f;
        }

        /// <summary>Guida contro la fila a 'cruise' m/s: il gas tiene la velocita', il manubrio punta al primo ancora in piedi. Finisce dodici metri oltre l'ultimo (dove stava alla partenza), o a moto ferma dopo
        /// il contatto, o allo scadere. Misura: la velocita' al primo contatto (plowVIn), la piu' bassa dopo (plowVMin, 99 se non c'e' stato contatto), se e' stato contato un urto (plowCrash) e quanta vita ha
        /// perso il pilota (plowLoss).</summary>
        IEnumerator PlowRun(List<Enemy> row, float cruise, float timeout)
        {
            var pl = Pl;
            plowCrash = false; plowVIn = 0f; plowVMin = 99f;
            System.Array.Clear(plowRing, 0, plowRing.Length); plowRingI = 0; plowTrace.Clear();
            float life0 = pl.hp + pl.shield;
            Vector3 origin = pl.transform.position;
            Vector3 fwd0 = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading));
            float farthest = 0f;
            foreach (var e in row) if (e != null) farthest = Mathf.Max(farthest, Vector3.Dot(e.transform.position - origin, fwd0));
            float t0 = Time.time, still = 0f;
            bool contact = false;
            while (pl.Riding && Time.time - t0 < timeout)
            {
                Enemy tgt = null; float best = 1e9f;
                foreach (var e in row)
                {
                    if (e == null || e.IsDead) continue;
                    float d = Flat(e.transform.position, pl.transform.position);
                    if (d < best) { best = d; tgt = e; }
                }
                Vector3 aim = tgt != null ? tgt.transform.position : origin + fwd0 * (farthest + 20f);
                Vector3 want = aim - pl.transform.position; want.y = 0f;
                float diff = Mathf.DeltaAngle(pl.RideHeading * Mathf.Rad2Deg, Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg);
                DesertBike.botSteer = Mathf.Clamp(diff / 25f, -1f, 1f);
                float v = pl.RideSpeed;
                DesertBike.botThrottle = v < cruise ? 1f : (v > cruise + 1.5f ? -0.3f : 0f);
                plowRing[plowRingI++ % plowRing.Length] = v;
                plowTrace.Enqueue($"{Time.time - t0:F2}s z{pl.transform.position.z:F1} y{pl.transform.position.y:F2} v{v * 3.6f:F0} {(pl.RideGrounded ? "t" : "ARIA")} n{pl.RidePlowCount} d{(best < 90f ? best : -1f):F1}");
                if (plowTrace.Count > 36) plowTrace.Dequeue();
                if (pl.RideCrashCooldown > 0f && !plowCrash)
                {
                    plowCrash = true;
                    float before = 0f;
                    foreach (var s in plowRing) before = Mathf.Max(before, s);
                    CrashReport(pl, before, v, row);
                    Debug.Log("[PryingEyes] desertotest motoinvesti: ultimi fotogrammi prima dell'urto: " + string.Join(" | ", plowTrace));
                }
                if (!contact && best < 2.2f) { contact = true; plowVIn = v; }
                if (contact) plowVMin = Mathf.Min(plowVMin, v);
                if (Vector3.Dot(pl.transform.position - origin, fwd0) > farthest + 12f) break;
                if (contact && Mathf.Abs(v) < 0.6f) { still += Time.deltaTime; if (still > 0.6f) break; } else still = 0f;
                motoWorst = Mathf.Max(motoWorst, Time.unscaledDeltaTime);
                yield return null;
            }
            plowLoss = life0 - (pl.hp + pl.shield);
            DesertBike.botThrottle = -1f; DesertBike.botSteer = 0f;
            float tb = Time.time;
            while (pl.Riding && Time.time - tb < 8f && Mathf.Abs(pl.RideSpeed) > 0.5f) yield return null;
            DesertBike.botThrottle = 0f;
        }

        /// <summary>Al primo urto di una passata: dove, a che velocita', quali nemici (di tutta la scena, non solo quelli della fila) a meno di quattro metri e che cosa c'e' davanti al pilota (il collider piu' vicino entro
        /// quattro metri nella direzione della moto: terreno, muro, sasso).</summary>
        void CrashReport(PlayerController pl, float before, float speed, List<Enemy> row)
        {
            Vector3 p = pl.transform.position;
            Vector3 fwd = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading));
            var sb = new System.Text.StringBuilder();
            foreach (var e in Enemy.All)
            {
                if (e == null) continue;
                float d = Flat(e.transform.position, p);
                if (d > 4f) continue;
                float ahead = Vector3.Dot(e.transform.position - p, fwd);
                sb.Append($" [{e.type} a {d:F1} m ({ahead:F1} avanti, dy {e.transform.position.y - p.y:F1}), vita {e.hp:F0}, {(row.Contains(e) ? "della fila" : "estraneo")}, investibile {e.Plowable}]");
            }
            var ccol = pl.GetComponent<CharacterController>();
            string what = "niente";
            if (Physics.SphereCast(p + Vector3.up * 0.9f, 0.35f, fwd, out var h, 4f, ~0, QueryTriggerInteraction.Ignore))
                what = Describe(h.collider, ccol) + $" a {h.distance:F1} m, normale y {h.normal.y:F2}";
            // tutto quello che tocca la capsula del pilota (allargata di mezzo metro) in questo momento
            var near = Physics.OverlapSphere(p + Vector3.up * 0.9f, 1.6f, ~0, QueryTriggerInteraction.Ignore);
            var seen = new System.Text.StringBuilder();
            foreach (var c in near) if (c != ccol && c != null) seen.Append($" <{Describe(c, ccol)}>");
            what += "; entro 1,6 m:" + (seen.Length > 0 ? seen.ToString() : " niente");
            Debug.Log($"[PryingEyes] desertotest motoinvesti: PRIMO URTO in {p:F1}, da {before * 3.6f:F0} a {speed * 3.6f:F0} km/h negli ultimi dodici fotogrammi, investiti {pl.RidePlowCount}, nemici entro 4 m:{(sb.Length > 0 ? sb.ToString() : " nessuno")}; davanti: {what}");
        }

        static string Describe(Collider c, CharacterController cc)
        {
            var en = c.GetComponentInParent<Enemy>();
            bool ignored = cc != null && Physics.GetIgnoreCollision(c, cc);
            return $"{c.name} {c.GetType().Name} strato {LayerMask.LayerToName(c.gameObject.layer)}, {(c.attachedRigidbody != null ? "rigido" : "fisso")}, {(en != null ? en.type + (en.IsDead ? " morto" : " vivo") : "non e' un nemico")}, ignorato dal pilota {ignored}";
        }

        /// <summary>Una fotografia di lato (con -desertshots): la camera del giocatore va per un istante a sei metri di lato e un po' dietro al punto, guarda quel punto e torna dov'era (la guida la rimette a posto
        /// al fotogramma dopo comunque).</summary>
        void SideShot(string name, Vector3 target, Vector3 fwd)
        {
            if (!shots) return;
            var cam = Pl.cam;
            Vector3 p0 = cam.position; Quaternion r0 = cam.rotation;
            Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
            cam.position = target + side * 6f - fwd * 3f + Vector3.up * 0.9f;
            cam.rotation = Quaternion.LookRotation(target - cam.position);
            Shot(name);
            cam.position = p0; cam.rotation = r0;
        }

        /// <summary>Dove sta il corpo di chi e' caduto (il centro della pelle, che segue le ossa: la radice non si muove).</summary>
        static Vector3 BodyCentre(Enemy e)
        {
            var smr = e != null ? e.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            return smr != null ? smr.bounds.center : (e != null ? e.transform.position : Vector3.zero);
        }

        IEnumerator MotoRoadkill()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset(); Weapon.rideBotFire = false;
            motoWorst = 0f;
            ClearHorde();
            DesertBikes.ReturnHome(pl);                                     // in catena la moto e' dove l'ha lasciata la fase di prima: si parte dal suo posto, sulla sabbia piana ai piedi della scalinata
            bool dropsWere = Drops.disabled;
            Drops.ResetRun(); Drops.disabled = true;                         // un monaco ucciso poteva lasciare lo Sguardo: il pilota lo raccoglieva e falsava le misure
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("investire: c'e' una moto parcheggiata", bike != null);
            if (bike == null) { Drops.disabled = dropsWere; yield break; }
            Vector3 home = bike.transform.position;
            Vector3 south = new Vector3(0f, 0f, -1f);
            yield return GoTo(home.x - 2.2f, home.z + 0.3f, 2f);
            yield return new WaitForSeconds(1.0f);
            Face(bike.Point);
            yield return null; yield return null;
            Interact.Tick(pl.cam);
            Interact.TryUse();
            yield return null; yield return null;
            Check("investire: in sella", pl.Riding);
            if (!pl.Riding) { godMode = true; Drops.disabled = dropsWere; yield break; }

            // ---- via dalla scalinata, su sabbia aperta: da qui parte ogni passata
            DesertBike.botThrottle = 1f;
            yield return RideUntil(() => pl.transform.position.z < home.z - 30f, 15f, south);
            float tb = Time.time;
            while (pl.Riding && Time.time - tb < 6f && Mathf.Abs(pl.RideSpeed) > 0.4f) { DesertBike.botThrottle = -1f; DesertBike.botSteer = 0f; yield return null; }
            DesertBike.botThrottle = 0f; DesertBike.botSteer = 0f;
            yield return new WaitForSeconds(0.4f);
            Vector3 start = pl.transform.position;
            float heading = pl.RideHeading;
            Vector3 fwd = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
            Vector3 farSpot = start + fwd * 75f;
            yield return WaitFor(() => (DesertTerrain.I == null || DesertTerrain.I.Ready(farSpot, 30f)) && DesertNav.ReadyAround(farSpot, 30f), 25f);
            ClearHorde();
            // si misura anche il danno al pilota, ma solo da qui: prima (a freddo la navmesh davanti puo' farsi aspettare mezzo minuto) sette guardie dei luoghi vicini lo raggiungevano fermo e lo mandavano a terra
            godMode = false; pl.invulnerable = false;
            var cc = pl.GetComponent<CharacterController>();
            Debug.Log($"[PryingEyes] desertotest motoinvesti: partenza in {start:F1}, rotta {heading * Mathf.Rad2Deg:F0} gradi, capsula del pilota raggio {(cc != null ? cc.radius : 0f):F2} m");

            // ---- A: un monaco solo, a 61 km/h
            yield return PlowStart(bike, start, heading);
            var mon = SpawnOnGround(Enemy.EnemyType.Monaco, start + fwd * 55f);
            yield return new WaitForSeconds(0.3f);
            if (mon == null) { Check("investire: il monaco c'e'", false); godMode = true; Drops.disabled = dropsWere; yield break; }
            float hpA = mon.hp, maxA = mon.maxHp;
            int scA = Gm.score;
            Vector3 lastPos = mon.transform.position;
            // il punto dove il monaco viene colpito: l'ultimo che ha avuto da vivo
            var run = PlowRun(new List<Enemy> { mon }, 17f, 16f);
            bool shotPre = false, shotHit = false, shotAfter = false;
            var shotSide = new bool[3];
            float[] sideAt = { 0.12f, 0.35f, 0.75f };
            float tHit = 0f;
            int count0 = pl.RidePlowCount;
            while (run.MoveNext())
            {
                if (mon != null && !mon.IsDead) lastPos = mon.transform.position;
                if (shots && mon != null)
                {
                    // le fotografie (-desertshots): il monaco che arriva, il colpo, tre scatti di lato lungo il volo del corpo (0,12, 0,35 e 0,75 s dopo), il dopo
                    float dm = Flat(mon.transform.position, pl.transform.position);
                    if (!shotPre && !mon.IsDead && dm < 9f) { shotPre = true; Shot("motoinvesti_a_prima"); }
                    if (!shotHit && pl.RidePlowCount > count0) { shotHit = true; tHit = Time.time; Shot("motoinvesti_b_colpo"); }
                    if (shotHit)
                    {
                        for (int k = 0; k < sideAt.Length; k++)
                            if (!shotSide[k] && Time.time - tHit > sideAt[k]) { shotSide[k] = true; SideShot("motoinvesti_c" + (k + 1) + "_laterale", BodyCentre(mon), fwd); }
                        if (!shotAfter && Time.time - tHit > 1.2f) { shotAfter = true; Shot("motoinvesti_d_dopo"); }
                    }
                }
                yield return run.Current;
            }
            bool deadA = mon == null || mon.IsDead;
            float speedA = plowVIn, minA = plowVMin, lossA = plowLoss; bool crashA = plowCrash;
            yield return new WaitForSeconds(1.3f);
            float flew = mon != null ? Vector3.Dot(BodyCentre(mon) - lastPos, fwd) : 0f;
            int scoreA = Gm.score - scA;
            Debug.Log($"[PryingEyes] desertotest motoinvesti: monaco solo a {speedA * 3.6f:F0} km/h: {(plowVMin < 90f ? "contatto" : "NESSUN contatto")}, urto {crashA}, velocita' minima dopo {minA * 3.6f:F0} km/h, vita {hpA:F0} -> {(mon != null ? Mathf.Max(0f, mon.hp) : 0f):F0} di {maxA:F0} ({(deadA ? "caduto" : "in piedi")}), pilota -{lossA:F0}, corpo {flew:F1} m avanti, punti +{scoreA}, ancora in sella {pl.Riding}");
            Check("investire: il monaco e' stato raggiunto a oltre 50 km/h", plowVMin < 90f && speedA * 3.6f > 50f, $"{speedA * 3.6f:F0} km/h, contatto {plowVMin < 90f}");
            Check("investire: a 61 km/h il monaco cade (la vita da 200 a zero)", deadA, $"vita {(mon != null ? mon.hp : 0f):F0} di {maxA:F0}");
            Check("investire: la moto non si ferma (almeno tre quarti della velocita' dopo il colpo)", minA >= 0.75f * speedA, $"da {speedA * 3.6f:F0} a {minA * 3.6f:F0} km/h");
            Check("investire: non e' un urto contro un muro (nessuna frenata da urto)", !crashA, "urto contato");
            Check("investire: il pilota non si fa male", lossA < 1f, $"-{lossA:F0}");
            Check("investire: il corpo vola avanti (almeno tre metri dal punto del colpo, in un secondo e mezzo)", deadA && flew > 3f, $"{flew:F1} m");
            Check("investire: la morte da' i punti (mezza uccisione, e nessun colpo a segno dell'arma)", scoreA >= Punti.Investito && scoreA < Punti.Uccisione + 40, $"+{scoreA}");
            Check("investire: il pilota dice di aver investito almeno un corpo (una guardia dei luoghi vicini che attraversa la strada conta anche lei)", pl.RidePlowCount >= 1, $"{pl.RidePlowCount}");
            Check("investire: il pilota e' ancora in sella", pl.Riding);
            if (mon != null) Enemy.Remove(mon);

            // ---- B: una fila di cinque monaci, tre metri e mezzo uno dall'altro, sempre a 61 km/h
            yield return PlowStart(bike, start, heading);
            var row = new List<Enemy>();
            for (int i = 0; i < 5; i++)
            {
                var m = SpawnOnGround(Enemy.EnemyType.Monaco, start + fwd * (55f + 3.5f * i));
                if (m != null) row.Add(m);
            }
            yield return new WaitForSeconds(0.3f);
            Check("investire: la fila di cinque c'e'", row.Count == 5, $"{row.Count} monaci");
            if (row.Count > 0)
            {
                yield return PlowRun(row, 17f, 18f);
                int deadB = 0, hurtB = 0; float minHp = 999f;
                foreach (var m in row) { if (m == null || m.IsDead) deadB++; else { if (m.hp < m.maxHp - 0.5f) hurtB++; minHp = Mathf.Min(minHp, m.hp); } }
                Debug.Log($"[PryingEyes] desertotest motoinvesti: fila di {row.Count} a {plowVIn * 3.6f:F0} km/h: urto {plowCrash}, velocita' minima dopo {plowVMin * 3.6f:F0} km/h, {deadB} caduti, {hurtB} feriti ancora in piedi, pilota -{plowLoss:F0}");
                Check("investire: nella fila almeno quattro monaci su cinque cadono", deadB >= 4, $"{deadB} caduti, {hurtB} feriti");
                Check("investire: gli altri sono almeno feriti (nessuno e' passato indenne)", deadB + hurtB == row.Count, $"{deadB} caduti + {hurtB} feriti su {row.Count}");
                Check("investire: la moto rallenta (cinque corpi costano velocita') ma corre ancora (oltre il 55% e meno del 98%)", plowVMin >= 0.55f * plowVIn && plowVMin <= 0.98f * plowVIn, $"da {plowVIn * 3.6f:F0} a {plowVMin * 3.6f:F0} km/h");
                Check("investire: la fila non e' un urto e il pilota non si fa male", !plowCrash && plowLoss < 1f, $"urto {plowCrash}, pilota -{plowLoss:F0}");
                Check("investire: i corpi investiti sono almeno i cinque della fila (di piu' se attraversa la strada una guardia dei luoghi vicini)", pl.RidePlowCount >= 5, $"{pl.RidePlowCount}");
            }
            foreach (var m in row) if (m != null) Enemy.Remove(m);

            // ---- C: a 11 km/h un monaco resta un ostacolo: niente danno, niente investimento
            yield return PlowStart(bike, start, heading);
            var slow = SpawnOnGround(Enemy.EnemyType.Monaco, start + fwd * 24f);
            yield return new WaitForSeconds(0.3f);
            if (slow != null)
            {
                float hpC = slow.hp;
                yield return PlowRun(new List<Enemy> { slow }, 3f, 14f);
                Debug.Log($"[PryingEyes] desertotest motoinvesti: monaco a {plowVIn * 3.6f:F0} km/h: contatto {plowVMin < 90f}, urto {plowCrash}, vita {hpC:F0} -> {slow.hp:F0}");
                Check("investire: a passo d'uomo (11 km/h) il monaco e' un ostacolo, non un colpo (la sua vita non cala)", plowVMin < 90f && slow.hp >= hpC - 0.5f && !slow.IsDead, $"contatto {plowVMin < 90f}, vita {hpC:F0} -> {slow.hp:F0}");
                Check("investire: a 11 km/h non c'e' urto e il pilota non ha investito nessuno", !plowCrash && pl.RidePlowCount == 0 && pl.RidePlowedNow == 0, $"urto {plowCrash}, investiti {pl.RidePlowCount}, ignorati {pl.RidePlowedNow}");
                Enemy.Remove(slow);
            }
            else Check("investire: il monaco lento c'e'", false);

            // ---- D: un abate, a 61 km/h: e' grosso, resta in piedi ferito e costa piu' velocita' di un monaco
            yield return PlowStart(bike, start, heading);
            var abb = SpawnOnGround(Enemy.EnemyType.Abate, start + fwd * 55f);
            yield return new WaitForSeconds(0.3f);
            if (abb != null)
            {
                float hpD = abb.hp, maxD = abb.maxHp;
                yield return PlowRun(new List<Enemy> { abb }, 17f, 16f);
                float hpNow = abb != null ? abb.hp : 0f;
                Debug.Log($"[PryingEyes] desertotest motoinvesti: abate a {plowVIn * 3.6f:F0} km/h: contatto {plowVMin < 90f}, urto {plowCrash}, velocita' minima dopo {plowVMin * 3.6f:F0} km/h, vita {hpD:F0} -> {hpNow:F0} di {maxD:F0}, pilota -{plowLoss:F0}");
                Check("investire: l'abate e' ferito (almeno 150 punti vita)", plowVMin < 90f && hpNow <= hpD - 150f, $"vita {hpD:F0} -> {hpNow:F0}");
                Check("investire: l'abate a 61 km/h resta in piedi (e' grosso)", abb != null && !abb.IsDead, $"vita {hpNow:F0}");
                float ratioD = plowVMin / Mathf.Max(0.1f, plowVIn), ratioA = minA / Mathf.Max(0.1f, speedA);
                Check("investire: l'abate costa alla moto piu' di un monaco (almeno 3 punti percentuali in piu') ma non la ferma (oltre il 40% della velocita')", ratioD >= 0.40f && ratioD <= ratioA - 0.03f, $"abate {ratioD * 100f:F0}% della velocita', monaco {ratioA * 100f:F0}%");
                Check("investire: neppure l'abate e' un urto", !plowCrash);
                if (abb != null) Enemy.Remove(abb);
            }
            else Check("investire: l'abate c'e'", false);

            // ---- E: un fotogramma lungo non e' un urto. Su sabbia libera a 61 km/h, senza nessuno davanti, il thread principale si ferma per 160 ms (come alla prima medaglia o alla nascita di un blocco di sabbia):
            // la guida limita il passo a 50 ms e la velocita' letta dal controller, divisa per il tempo vero, scendeva a un terzo e contava come un urto (fino a 45 punti di danno) contro il niente
            yield return PlowStart(bike, start, heading);
            {
                float tE = Time.time, tSlept = 0f, vBefore = 0f, vAfter = 99f;
                bool slept = false, crashE = false;
                float lifeE = pl.hp + pl.shield;
                while (pl.Riding && Time.time - tE < 7f)
                {
                    float v = pl.RideSpeed;
                    Autopilot(fwd);
                    DesertBike.botThrottle = v < 17f ? 1f : (v > 18.5f ? -0.3f : 0f);
                    if (pl.RideCrashCooldown > 0f) crashE = true;
                    if (!slept && v > 16f && Time.time - tE > 2f) { vBefore = v; System.Threading.Thread.Sleep(160); slept = true; tSlept = Time.time; }
                    else if (slept)
                    {
                        vAfter = Mathf.Min(vAfter, v);
                        if (Time.time - tSlept > 0.8f) break;
                    }
                    yield return null;
                }
                float lossE = lifeE - (pl.hp + pl.shield);
                DesertBike.botThrottle = -1f; DesertBike.botSteer = 0f;
                float tbE = Time.time;
                while (pl.Riding && Time.time - tbE < 8f && Mathf.Abs(pl.RideSpeed) > 0.5f) yield return null;
                DesertBike.botThrottle = 0f;
                Debug.Log($"[PryingEyes] desertotest motoinvesti: fotogramma lungo (160 ms) a {vBefore * 3.6f:F0} km/h: urto {crashE}, velocita' minima dopo {vAfter * 3.6f:F0} km/h, pilota -{lossE:F0}");
                Check("investire: il fotogramma lungo e' avvenuto (a oltre 55 km/h)", slept && vBefore * 3.6f > 55f, $"{vBefore * 3.6f:F0} km/h, pausa fatta {slept}");
                Check("investire: un fotogramma lungo non e' un urto e non toglie vita al pilota", !crashE && lossE < 1f, $"urto {crashE}, pilota -{lossE:F0}");
                Check("investire: dopo il fotogramma lungo la moto corre ancora (almeno il 90% della velocita')", vAfter >= 0.9f * vBefore, $"da {vBefore * 3.6f:F0} a {vAfter * 3.6f:F0} km/h");
            }

            // ---- fine
            pl.RideRequestDismount();
            yield return WaitFor(() => !pl.Riding, 10f);
            yield return new WaitForSeconds(0.4f);
            Check("investire: sceso di sella nessun corpo resta senza capsula per il pilota", pl.RidePlowedNow == 0, $"{pl.RidePlowedNow}");
            ClearHorde();
            DesertBike.BotReset();
            pl.hp = 100f; pl.shield = 50f;
            godMode = true;
            Drops.disabled = dropsWere;
        }
    }
}
#endif

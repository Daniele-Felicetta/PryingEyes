using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Prove della fase 2 (punti, coltello, potenziamenti, Notte delle Falene, bonus Fantasma), con lo schema di -roundtest:
    ///   PryingEyes.exe -batchmode [-nographics] -autotest -stealthtest -knifetest -seed N -floor N    esito in hotel_build/furtivo/coltello.txt
    ///   PryingEyes.exe -batchmode [-nographics] -autotest -stealthtest -droptest  -seed N -floor N    esito in hotel_build/furtivo/drop.txt
    /// Uscita 0 ok, 1 fallito, 2 piano senza lo spiazzo che serve (cambia seme). Senza -nographics fanno anche le foto del coltello
    /// (le pose a mezz'aria, con le mani) e dei quattro potenziamenti.</summary>
    public partial class StealthTest
    {
        // ------------------------------------------------------------------ scena
        /// <summary>Uno spiazzo libero per le scene di prova: dove sta il bersaglio e in che direzione guarda 'dir'. Dietro di lui, a 1,9 m,
        /// sta il giocatore, e a 3,2 m dal giocatore (di fianco) un testimone. Tutto sulla navmesh e senza muri o mobili in mezzo.</summary>
        static bool FindOpenSpot(FloorInstance fi, out Vector3 spot, out Vector3 dir, float back = 1.9f)
        {
            spot = Vector3.zero; dir = Vector3.forward;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            Vector3 up1 = Vector3.up * 1.0f;
            bool Free(Vector3 q) => NavMesh.SamplePosition(q, out NavMeshHit fh, 0.3f, NavMesh.AllAreas)
                                    && !Physics.CheckSphere(fh.position + up1, 0.55f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
            foreach (var r in fi.plan.rooms)
            {
                if (r.IsLift || r.type == RoomType.Segreta) continue;      // la stanza segreta e' un posto a parte (si entra a carponi)
                float x0 = r.x0 * 0.1f + 1.3f, x1 = r.x1 * 0.1f - 1.3f, z0 = r.z0 * 0.1f + 1.3f, z1 = r.z1 * 0.1f - 1.3f;
                for (float x = x0; x <= x1; x += 0.8f)
                    for (float z = z0; z <= z1; z += 0.8f)
                        foreach (var d in dirs)
                        {
                            var m = new Vector3(x, 0f, z);
                            var pp = m - d * back;
                            var ww = pp + Vector3.Cross(Vector3.up, d) * 3.2f;
                            if (!Free(m) || !Free(pp) || !Free(ww)) continue;
                            if (!HotelNav.ClearLine(m + up1, pp + up1) || !HotelNav.ClearLine(pp + up1, ww + up1)) continue;
                            // il testimone sente senza muri in mezzo (un muro dimezza la portata dei rumori): linee libere ad altezza d'orecchio
                            if (!HotelNav.ClearLine(ww + Vector3.up * 1.4f, m + Vector3.up * 1.2f) || !HotelNav.ClearLine(ww + Vector3.up * 1.4f, pp + Vector3.up * 2.0f)) continue;
                            if (Physics.Raycast(m + up1, d, 1.2f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                            spot = m; dir = d;
                            return true;
                        }
            }
            return false;
        }

        /// <summary>Un punto della navmesh fuori dalla zona della candela: accesa da li' non fa partire nessun round.</summary>
        static Vector3 OutsideZone(FloorInstance fi, RoundDirector rd, Candelabro c)
        {
            var reg = rd != null ? rd.RegionOf(c) : null;
            foreach (var r in fi.plan.rooms)
            {
                if (r.IsLift || (reg != null && reg.Has(r.id))) continue;
                if (NavMesh.SamplePosition(new Vector3(r.CenterX, 0f, r.CenterZ), out NavMeshHit oh, 2f, NavMesh.AllAreas)) return oh.position;
            }
            return NavPoint(c.transform.position);
        }

        /// <summary>Il giocatore dentro la stanza della candela, a un metro e mezzo, che la guarda.</summary>
        static void PlaceBeside(PlayerController pl, FloorInstance fi, Candelabro c)
        {
            Vector3 cp = c.transform.position;
            var room = fi.RoomAt(cp);
            Vector3 toCenter = room != null ? new Vector3(room.CenterX - cp.x, 0f, room.CenterZ - cp.z) : Vector3.zero;
            Vector3 pos = NavPoint(cp + (toCenter.sqrMagnitude > 0.5f ? toCenter.normalized : Vector3.right) * 1.3f);
            Vector3 look = cp - pos;
            pl.Teleport(pos);
            pl.yaw = Mathf.Atan2(look.x, look.z);
            pl.pitch = 0f;
        }

        // ------------------------------------------------------------------ coltello (-knifetest)
        /// <summary>Prova del coltello: la presa alle spalle (silenziosa, 130 punti, anche all'abate), il fendente in faccia (rumoroso,
        /// chi sopravvive ti caccia), il colpo a vuoto, i casi in cui la presa non c'e' (chi ti caccia, chi nasce in un round, chi ti
        /// guarda, l'occhio) e, con la grafica, le foto delle pose.</summary>
        IEnumerator CheckKnife()
        {
            var gm = GameManager.I;
            var fd = FloorDirector.I;
            var pl = gm.player;
            bool passed = true;
            void Check(string what, bool ok, string detail = "")
            {
                Log((ok ? "ok       " : "FALLITO  ") + what + (detail.Length > 0 ? "   [" + detail + "]" : ""));
                if (!ok) passed = false;
            }
            bool shots = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            yield return new WaitForSeconds(2.0f);
            var kn = Knife.I;
            Check("il coltello c'e' (StealthRig)", kn != null);
            if (kn == null) { Application.Quit(1); yield break; }
            var wp = pl.weapon;
            Drops.disabled = true;
            Enemy.ClearAll();
            PlayerController.holdLook = true;
            pl.invulnerable = true;
            Enemy.debugFreeze = true;
            Enemy.debugNoSense = false;
            if (!FindOpenSpot(fd.cur, out Vector3 spot, out Vector3 dir)) { Log("nessuno spiazzo libero su questo piano: prova non applicabile (cambia seme)"); Application.Quit(2); yield break; }
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Log($"piano {fd.floor} seme {fd.runSeed}: spiazzo in ({spot.x:F1},{spot.z:F1}), il bersaglio guarda ({dir.x:F0},{dir.z:F0})");

            // il bersaglio guarda 'dir' (di spalle al giocatore) o verso il giocatore, che gli sta dietro a 'gap' metri; il testimone di fianco
            Enemy monk = null, witness = null;
            void Stage(Enemy.EnemyType type, bool faceMe, float gap, bool withWitness)
            {
                Enemy.ClearAll();
                monk = Enemy.Spawn(type, spot);
                monk.transform.rotation = Quaternion.LookRotation(faceMe ? -dir : dir);
                Vector3 me = NavPoint(monk.transform.position - dir * gap);
                witness = null;
                if (withWitness)
                {
                    witness = Enemy.Spawn(Enemy.EnemyType.Monaco, NavPoint(monk.transform.position - dir * 1.9f + side * 3.2f));      // il posto che FindOpenSpot ha provato libero
                    witness.transform.rotation = Quaternion.LookRotation(side);             // di spalle al giocatore
                }
                pl.Teleport(me);
                pl.yaw = Mathf.Atan2(dir.x, dir.z);
                pl.pitch = 0f;
            }
            // G, e si aspetta la fine dell'animazione (e che l'arma risalga)
            IEnumerator Hit()
            {
                kn.DebugAttack();
                float tw = Time.time;
                yield return null;
                while (kn.Playing && Time.time - tw < 3f) yield return null;
                yield return new WaitForSeconds(0.35f);
            }
            int s0, sp0, tk0, st0, wf0;

            // ----- 1) la presa: un monaco che cammina e non ti ha visto
            Log("--- presa alle spalle (monaco)");
            Stage(Enemy.EnemyType.Monaco, false, 1.9f, true);
            yield return new WaitForSeconds(1.5f);
            Check("alle spalle, a 1,9 m, non ti nota (nemmeno stando fermo li')", monk.alert == Alert.Patrol && monk.awareness < 0.05f, $"{monk.alert} aw={monk.awareness:F2}");
            Check("la presa e' possibile", monk.CanTakedown(pl.cam.position), $"a {Vector3.Distance(monk.transform.position, pl.transform.position):F2} m");
            gm.hud.SetPrompt("");
            kn.PromptTick();
            Check("prompt PRESA ALLE SPALLE", gm.hud.CurrentPrompt.Contains("PRESA ALLE SPALLE"), "'" + gm.hud.CurrentPrompt + "'");
            s0 = gm.score; tk0 = Knife.statTakedowns; sp0 = Stealth.Spotted;
            kn.DebugAttack();
            yield return new WaitForSeconds(0.2f);
            Check("durante il colpo l'arma si ritrae", kn.Playing && wp.KnifeBusy);
            float tw1 = Time.time;
            while (kn.Playing && Time.time - tw1 < 3f) yield return null;
            yield return new WaitForSeconds(0.4f);
            Check("la presa riesce: il bersaglio muore e il colpo conta come presa", monk.IsDead && Knife.statTakedowns == tk0 + 1 && Knife.lastSilent, $"morto {monk.IsDead}, prese {Knife.statTakedowns - tk0}, in silenzio {Knife.lastSilent}");
            Check("la presa vale 130 punti", gm.score - s0 == Punti.Coltello, $"+{gm.score - s0}");
            Check("la presa non fa rumore: il testimone a 3 m non si accorge di niente", witness.alert == Alert.Patrol && witness.awareness < 0.02f, $"{witness.alert} aw={witness.awareness:F2}");
            Check("nessun avvistamento", Stealth.Spotted == sp0);
            Check("a fine colpo l'arma torna", !wp.KnifeBusy && !kn.Playing);

            // ----- 2) a meno di un metro, anche alle spalle, ti nota ancora
            Log("--- vicinissimo alle spalle");
            Stage(Enemy.EnemyType.Monaco, false, 0.8f, false);
            yield return new WaitForSeconds(1.2f);
            Check("a 0,8 m alle spalle ti nota", monk.alert != Alert.Patrol || monk.awareness >= 0.32f, $"{monk.alert} aw={monk.awareness:F2}");

            // ----- 3) l'abate: la presa lo uccide anche con 320 PV
            Log("--- presa all'abate");
            Stage(Enemy.EnemyType.Abate, false, 2.0f, false);
            yield return new WaitForSeconds(0.8f);
            Check("abate: la presa e' possibile", monk.CanTakedown(pl.cam.position), $"{monk.alert} aw={monk.awareness:F2}");
            s0 = gm.score; tk0 = Knife.statTakedowns;
            yield return Hit();
            Check("abate: una presa lo uccide", monk.IsDead && Knife.statTakedowns == tk0 + 1, $"hp {monk.hp:F0}");
            Check("abate: 130 punti", gm.score - s0 == Punti.Coltello, $"+{gm.score - s0}");

            // ----- 4) il fendente in faccia: rumoroso. Percezione spenta (debugNoSense): lo stato lo decido io.
            Log("--- fendente in faccia che uccide");
            Enemy.debugNoSense = true;
            Stage(Enemy.EnemyType.Monaco, true, 1.3f, true);
            monk.hp = 100f;                                  // un fendente (120) basta
            yield return new WaitForSeconds(0.4f);
            Check("in faccia la presa non c'e'", !monk.CanTakedown(pl.cam.position));
            gm.hud.SetPrompt("");
            kn.PromptTick();
            Check("in faccia nessun prompt di presa", gm.hud.CurrentPrompt.Length == 0, "'" + gm.hud.CurrentPrompt + "'");
            s0 = gm.score; st0 = Knife.statStabs; sp0 = Stealth.Spotted;
            yield return Hit();
            Check("il fendente uccide e conta come colpo rumoroso", monk.IsDead && Knife.statStabs == st0 + 1 && !Knife.lastSilent, $"morto {monk.IsDead}, colpi {Knife.statStabs - st0}, in silenzio {Knife.lastSilent}");
            Check("l'uccisione col coltello vale 130", gm.score - s0 == Punti.Coltello, $"+{gm.score - s0}");
            {
                Vector3 wEar = witness.transform.position + Vector3.up * 1.4f, target = monk.AimPoint + Vector3.up * 0.4f;
                string blocker = Physics.Linecast(wEar, target, out RaycastHit bh, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)
                    ? $"{bh.collider.name} (strato {bh.collider.gameObject.layer}) a {bh.distance:F1} m" : "nessuno";
                Check("il fendente si sente: il testimone se ne accorge", witness.awareness >= 0.2f,
                      $"{witness.alert} aw={witness.awareness:F2}, a {Vector3.Distance(witness.transform.position, monk.transform.position):F1} m dal colpo, ostacolo sulla linea: {blocker}");
            }
            Check("ucciso sul colpo: nessun avvistamento", Stealth.Spotted == sp0);

            Log("--- fendente che non uccide");
            Stage(Enemy.EnemyType.Monaco, true, 1.3f, true);
            yield return new WaitForSeconds(0.4f);
            s0 = gm.score; st0 = Knife.statStabs; sp0 = Stealth.Spotted;
            yield return Hit();
            Check("sopravvive con 80 PV", !monk.IsDead && Mathf.Abs(monk.hp - (200f - Knife.StabDamage)) < 0.5f, $"hp {monk.hp:F0}");
            Check("chi sopravvive ti caccia", monk.alert == Alert.Hunting, monk.alert.ToString());
            Check("un colpo che non uccide paga 10", gm.score - s0 == Punti.Colpo, $"+{gm.score - s0}");
            Check("l'avvistamento e' contato (serve al bonus Fantasma)", Stealth.Spotted == sp0 + 1, $"{Stealth.Spotted - sp0}");
            Check("il grido sveglia il testimone", witness.alert != Alert.Patrol, witness.alert.ToString());
            s0 = gm.score;
            yield return Hit();
            Check("il secondo fendente lo uccide: 130", monk.IsDead && gm.score - s0 == Punti.Coltello, $"morto {monk.IsDead}, +{gm.score - s0}");

            // ----- 5) a vuoto
            Log("--- fendente a vuoto");
            Enemy.ClearAll();
            yield return null;
            wf0 = Knife.statWhiffs; sp0 = Stealth.Spotted;
            yield return Hit();
            Check("a vuoto: contato come mancato", Knife.statWhiffs == wf0 + 1 && !wp.KnifeBusy, $"mancati {Knife.statWhiffs - wf0}");
            Check("a vuoto: nessun avvistamento", Stealth.Spotted == sp0);

            // ----- 6) quando la presa non si puo' fare
            Log("--- casi in cui la presa non c'e'");
            Stage(Enemy.EnemyType.Monaco, false, 1.9f, false);
            Vector3 eye = pl.cam.position;
            Check("controllo: monaco di spalle che pattuglia -> presa", monk.CanTakedown(eye));
            monk.DebugSetAlert(Alert.Hunting);
            Check("chi ti caccia non si prende alle spalle", !monk.CanTakedown(eye));
            monk.Forget();
            Check("tornato a pattugliare, si", monk.CanTakedown(eye));
            monk.DebugSetAlert(Alert.Suspicious);
            Check("sospettoso ma di spalle, si", monk.CanTakedown(eye));
            monk.MakeRelentless();
            Check("chi nasce in un round non si prende", !monk.CanTakedown(eye));
            Stage(Enemy.EnemyType.Monaco, true, 1.9f, false);
            Check("di faccia niente presa", !monk.CanTakedown(pl.cam.position));
            Enemy.ClearAll();
            var eyeE = Enemy.Spawn(Enemy.EnemyType.Occhio, spot + Vector3.up * 1.5f);
            eyeE.transform.rotation = Quaternion.LookRotation(dir);
            Check("l'occhio non ha una schiena", !eyeE.CanTakedown(spot - dir * 1.9f + Vector3.up * 1.6f));

            // ----- 7) foto: il prompt e le pose del coltello (mani comprese)
            if (shots)
            {
                Log("--- foto");
                Enemy.debugNoSense = false;
                var torch = Torch.I;                                    // senza torcia il corridoio e' nero e non si vede nemmeno il guanto
                if (torch != null) { torch.charge = 1f; if (!torch.on) torch.Toggle(); }
                Stage(Enemy.EnemyType.Monaco, false, 1.9f, false);
                yield return new WaitForSeconds(0.8f);
                gm.hud.SetPrompt("");
                kn.PromptTick();
                Shot("coltello_presa_prompt");
                var poses = new[]
                {
                    (name: "presa_alzata", kind: 2, t: 0.16f), (name: "presa_colpo", kind: 2, t: 0.36f), (name: "presa_dopo", kind: 2, t: 0.56f),
                    (name: "taglio_salita", kind: 0, t: 0.10f), (name: "taglio_colpo", kind: 0, t: 0.19f), (name: "taglio_fine", kind: 0, t: 0.29f),
                    (name: "affondo_colpo", kind: 1, t: 0.17f), (name: "affondo_fine", kind: 1, t: 0.26f),
                };
                foreach (var p in poses)
                {
                    kn.DebugShow(p.kind, p.t);
                    yield return new WaitForSeconds(0.45f);                // l'arma si e' abbassata del tutto
                    GameManager.CamShot(Dir + "/coltello_" + p.name + ".png", null, false);
                }
                kn.DebugHide();
                if (torch != null && torch.on) torch.Toggle();
            }

            Log(passed ? "ESITO: ok" : "ESITO: FALLITO");
            Application.Quit(passed ? 0 : 1);
        }

        // ------------------------------------------------------------------ potenziamenti, falene, Fantasma (-droptest)
        /// <summary>Prova dei potenziamenti: il bonus Fantasma (con e senza avvistamenti), la caduta dall'uccisione e il tetto di quattro per
        /// piano, i quattro effetti (Reliquiario: munizioni; Campana: +400, uccide entro 22 m, il rintocco richiama chi resta; Sguardo e Offerta:
        /// 30 s), la scadenza di quelli lasciati a terra, la Notte delle Falene (ogni quinto round: solo falene, piu' numerose, un Reliquiario di
        /// premio) e, con la grafica, la foto dei quattro modelli.</summary>
        IEnumerator CheckDrops()
        {
            var gm = GameManager.I;
            var fd = FloorDirector.I;
            var rd = RoundDirector.I;
            var pl = gm.player;
            var wp = pl.weapon;
            bool passed = true;
            void Check(string what, bool ok, string detail = "")
            {
                Log((ok ? "ok       " : "FALLITO  ") + what + (detail.Length > 0 ? "   [" + detail + "]" : ""));
                if (!ok) passed = false;
            }
            bool shots = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            yield return new WaitForSeconds(2.0f);
            Log($"piano {fd.floor} seme {fd.runSeed}: stanze={fd.cur.plan.rooms.Count} candele={fd.cur.candles.Count} zone={(rd != null ? rd.ZoneCount : -1)}");
            Drops.disabled = true;
            Enemy.ClearAll();
            PlayerController.holdLook = true;
            pl.invulnerable = true;
            Enemy.debugFreeze = true;
            Enemy.debugNoSense = false;
            if (!FindOpenSpot(fd.cur, out Vector3 spot, out Vector3 dir)) { Log("nessuno spiazzo libero su questo piano: prova non applicabile (cambia seme)"); Application.Quit(2); yield break; }
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 home = NavPoint(spot - dir * 1.9f);                // dove sta il giocatore a riposo
            void Home() { pl.Teleport(home); pl.yaw = Mathf.Atan2(dir.x, dir.z); pl.pitch = 0f; }
            Home();

            // punti della navmesh a varie distanze dal giocatore: i centri delle stanze
            var ring = new List<Vector3>();
            foreach (var r in fd.cur.plan.rooms)
            {
                if (r.IsLift) continue;
                if (NavMesh.SamplePosition(new Vector3(r.CenterX, 0f, r.CenterZ), out NavMeshHit rh, 2f, NavMesh.AllAreas)) ring.Add(rh.position);
            }
            bool PickAt(float dmin, float dmax, out Vector3 found)
            {
                found = Vector3.zero;
                float bd = float.MaxValue;
                foreach (var q in ring)
                {
                    float d = Vector3.Distance(q, home);
                    if (d < dmin || d > dmax || d >= bd) continue;
                    bd = d; found = q;
                }
                return bd < float.MaxValue;
            }
            int s, sp, n0, f0, pk0, e0;

            // ----- 1) bonus Fantasma: ogni candela vale 60; se nessun prete ti ha visto dall'ultima, anche 200
            Log("--- Fantasma");
            var all = fd.cur.candles.FindAll(c => c != null && c.objective && !c.lit);
            all.Sort((a, b) => (rd != null && rd.HasZone(a) ? 1 : 0).CompareTo(rd != null && rd.HasZone(b) ? 1 : 0));      // prima quelle senza zona
            Candelabro reserved = null;                           // una candela con zona resta spenta per la Notte delle Falene
            for (int i = all.Count - 1; i >= 0; i--)
                if (rd != null && rd.HasZone(all[i])) { reserved = all[i]; all.RemoveAt(i); break; }
            Log($"candele libere per il Fantasma: {all.Count}; una con zona riservata alle falene: {(reserved != null ? "si" : "no")}");
            int delta = 0;
            bool expectGhost = false;
            // si accende da fuori della zona (niente round); senza i preti richiamati dalla candela, che a distanza di secondi nascono e ti vedono
            IEnumerator Light(Candelabro c)
            {
                pl.Teleport(OutsideZone(fd.cur, rd, c));
                Enemy.ClearAll();
                yield return new WaitForSeconds(0.7f);               // Stealth.Tick azzera i cacciatori
                expectGhost = Stealth.Spotted == fd.GhostMark && Stealth.Hunters == 0;
                int before = gm.score;
                c.Use();
                delta = gm.score - before;
                fd.StopAllCoroutines();
                Enemy.ClearAll();
                yield return new WaitForSeconds(0.3f);
            }
            if (all.Count >= 2)
            {
                yield return Light(all[0]);
                Check("candela: 60 punti, 200 in piu' se nessuno ti ha visto dall'ultima", delta == 60 + (expectGhost ? Punti.Fantasma : 0), $"+{delta}, Fantasma atteso {expectGhost}");
                yield return Light(all[1]);
                Check("candela senza avvistamenti dall'ultima: Fantasma (+200)", expectGhost && delta == 60 + Punti.Fantasma, $"+{delta}, atteso {60 + Punti.Fantasma}");
                if (all.Count >= 3)
                {
                    // un prete ti vede: fino alla candela dopo, niente Fantasma
                    Home();
                    var spotter = Enemy.Spawn(Enemy.EnemyType.Monaco, spot);
                    spotter.transform.rotation = Quaternion.LookRotation(-dir);
                    pl.Teleport(NavPoint(spotter.transform.position - dir * 1.5f));
                    pl.yaw = Mathf.Atan2(dir.x, dir.z);
                    sp = Stealth.Spotted;
                    float tw = Time.time;
                    while (spotter.alert != Alert.Hunting && Time.time - tw < 5f) yield return null;
                    Check("un prete ti vede e ti caccia", spotter.alert == Alert.Hunting && Stealth.Spotted == sp + 1, $"{spotter.alert}, avvistamenti {Stealth.Spotted - sp}");
                    yield return Light(all[2]);
                    Check("candela dopo un avvistamento: niente Fantasma", !expectGhost && delta == 60, $"+{delta}");
                    if (all.Count >= 4)
                    {
                        yield return Light(all[3]);
                        Check("dopo la candela il conto riparte: di nuovo Fantasma", expectGhost && delta == 60 + Punti.Fantasma, $"+{delta}");
                    }
                }
            }
            else Log("meno di due candele libere: Fantasma non provato");
            Enemy.ClearAll();
            Home();
            yield return new WaitForSeconds(0.3f);

            // ----- 2) la caduta dall'uccisione (forzata), la raccolta, il tetto di quattro per piano
            Log("--- caduta dall'uccisione");
            Drops.disabled = false;
            n0 = Drops.statSpawned; f0 = Drops.OnFloor; pk0 = Drops.statPicked;
            Drops.forceNext = true;
            var victim = Enemy.Spawn(Enemy.EnemyType.Monaco, NavPoint(home + dir * 6f));
            victim.Damage(100000f, victim.AimPoint, false, true);
            yield return new WaitForSeconds(0.2f);
            var drop = Drops.Live.Count > 0 ? Drops.Live[0] : null;
            Check("l'uccisione forzata lascia un potenziamento", Drops.statSpawned == n0 + 1 && Drops.OnFloor == f0 + 1 && drop != null && !Drops.forceNext, $"caduti {Drops.statSpawned - n0}, sul piano {Drops.OnFloor}");
            Check("cade dove e' morto", drop != null && Vector3.Distance(drop.transform.position, Drops.lastKillPos) < 2.5f, drop != null ? $"a {Vector3.Distance(drop.transform.position, Drops.lastKillPos):F1} m" : "");
            yield return new WaitForSeconds(0.8f);
            Check("a sei metri dal giocatore non si prende da solo", Drops.statPicked == pk0 && Drops.Live.Count == 1);
            if (drop != null)
            {
                var kind = drop.kind;
                pl.Teleport(NavPoint(drop.transform.position));
                yield return new WaitForSeconds(1.0f);
                Check("passandoci vicino lo si prende", Drops.statPicked == pk0 + 1 && Drops.Live.Count == 0, kind.ToString());
            }
            Drops.OnPlayerDied();                                   // spegne i timer di quello che e' stato preso
            Home();
            // tetto: arrivo a quattro con regali lontani dal giocatore, poi uccido a ripetizione
            e0 = Drops.statExpired;
            int nFar = 0;
            Vector3 farSpot = PickAt(9f, 80f, out Vector3 fp) ? fp : NavPoint(home + dir * 9f);
            while (Drops.OnFloor < Drops.PerFloor) { Drops.Spawn(DropKind.Ammo, farSpot + side * (nFar * 0.6f), true); nFar++; }
            n0 = Drops.statSpawned;
            for (int i = 0; i < 24; i++)
            {
                var v = Enemy.Spawn(Enemy.EnemyType.Monaco, NavPoint(home + dir * 6f));
                v.Damage(100000f, v.AimPoint, false, true, DamageSource.Takedown);          // la presa e' la piu' generosa (18% a uccisione)
                if (i % 6 == 5) yield return null;
            }
            Check("raggiunti i quattro del piano non cade piu' niente", Drops.statSpawned == n0 && Drops.OnFloor == Drops.PerFloor, $"caduti {Drops.statSpawned - n0}, sul piano {Drops.OnFloor}/{Drops.PerFloor}");
            Drops.disabled = true;
            Enemy.ClearAll();

            // ----- 3) Reliquiario
            Log("--- Reliquiario");
            wp.mag = 0; wp.reserve = 0;
            pk0 = Drops.statPicked;
            Drops.Spawn(DropKind.Ammo, pl.transform.position, false);
            yield return new WaitForSeconds(1.0f);
            Check("Reliquiario: caricatore e riserva al massimo", Drops.statPicked == pk0 + 1 && wp.mag == wp.magSize && wp.reserve == wp.reserveMax, $"{wp.mag}/{wp.magSize}, riserva {wp.reserve}/{wp.reserveMax}");
            if (wp.WeaponInSlot(1) >= 0)
                Check("Reliquiario: anche l'altra arma", wp.ReserveOf(1) == WeaponDefs.All[wp.WeaponInSlot(1)].reserveMax);

            // ----- 4) Campana
            Log("--- Campana");
            Enemy.ClearAll();
            Home();
            yield return null;
            var near = Enemy.Spawn(Enemy.EnemyType.Monaco, NavPoint(home + dir * 4f));
            Enemy mid = null, far = null;
            if (PickAt(10f, 19f, out Vector3 qMid)) mid = Enemy.Spawn(Enemy.EnemyType.Monaco, qMid);
            Vector3 qFar = Vector3.zero;
            if (PickAt(23f, 27f, out qFar) || PickAt(23f, 300f, out qFar)) far = Enemy.Spawn(Enemy.EnemyType.Monaco, qFar);
            float dMid = mid != null ? Vector3.Distance(mid.transform.position, pl.transform.position) : 0f;
            float dFar = far != null ? Vector3.Distance(far.transform.position, pl.transform.position) : 0f;
            Log($"bersagli: vicino {Vector3.Distance(near.transform.position, pl.transform.position):F1} m, medio {(mid != null ? dMid.ToString("F1") : "-")} m, lontano {(far != null ? dFar.ToString("F1") : "-")} m");
            s = gm.score; n0 = Drops.statSpawned; int nk0 = Drops.statNuked;
            Drops.Apply(DropKind.Nuke);
            yield return new WaitForSeconds(0.3f);
            Check("Campana: uccide chi e' vicino e chi e' entro 22 m", near.IsDead && (mid == null || mid.IsDead), $"vicino {near.IsDead}, medio {(mid != null ? mid.IsDead.ToString() : "-")}");
            if (far != null && dFar > Drops.NukeRadius + 0.5f) Check("Campana: chi e' oltre 22 m sopravvive", !far.IsDead, $"a {dFar:F0} m");
            Check("Campana: +400 punti in tutto, nessun punto a testa", gm.score - s == Drops.NukePoints, $"+{gm.score - s}");
            Check("Campana: conta le uccisioni", Drops.statNuked - nk0 == 1 + (mid != null ? 1 : 0), $"{Drops.statNuked - nk0}");
            Check("Campana: le sue uccisioni non fanno cadere altro", Drops.statSpawned == n0);
            if (far != null && !far.IsDead)
            {
                Log($"info: il sopravvissuto a {dFar:F0} m, dopo il rintocco: {far.alert} aw={far.awareness:F2}");
                if (dFar <= 26.5f) Check("Campana: il rintocco richiama chi sopravvive (entro 27 m)", far.alert != Alert.Patrol, $"{far.alert}");
            }
            Enemy.ClearAll();

            // ----- 5) Sguardo e Offerta, e la scadenza di quelli lasciati a terra
            Log("--- Sguardo e Offerta");
            Drops.OnPlayerDied();
            Home();
            pk0 = Drops.statPicked;
            Drops.Spawn(DropKind.Insta, pl.transform.position, false);
            Drops.Spawn(DropKind.Double, pl.transform.position, false);
            yield return new WaitForSeconds(1.0f);
            float tBoth = Time.time - 1.0f;                         // quando sono stati presi (circa)
            Check("Sguardo e Offerta presi", Drops.statPicked == pk0 + 2, $"{Drops.statPicked - pk0}");
            Check("Sguardo: ogni colpo uccide, per 30 s", Drops.InstaKill && Drops.InstaLeft > 28f && Drops.InstaLeft <= Drops.Duration, $"{Drops.InstaLeft:F1} s");
            Check("Offerta: i punti valgono il doppio, per 30 s", Drops.DoublePoints && Drops.DoubleLeft > 28f && Drops.DoubleLeft <= Drops.Duration, $"{Drops.DoubleLeft:F1} s");
            s = gm.score;
            gm.AddPoints(10);
            Check("Offerta: 10 punti ne valgono 20", gm.score - s == 20, $"+{gm.score - s}");
            var vi = Enemy.Spawn(Enemy.EnemyType.Monaco, NavPoint(home + dir * 6f));
            s = gm.score;
            vi.Damage(1f, vi.AimPoint, false, true);
            Check("Sguardo: un colpo da 1 uccide un monaco da 200 PV", vi.IsDead, $"hp {vi.hp:F0}");
            Check("Offerta: l'uccisione vale il doppio (120)", gm.score - s == 2 * Punti.Uccisione, $"+{gm.score - s}");
            float tw2 = Time.time;
            while (Time.time - tw2 < 40f && (Drops.InstaKill || Drops.DoublePoints || Drops.Live.Count > 0)) yield return new WaitForSeconds(0.5f);
            float lived = Time.time - tBoth;
            Check("Sguardo e Offerta finiscono dopo 30 s", !Drops.InstaKill && !Drops.DoublePoints && lived > 29f && lived < 33f, $"{lived:F1} s");
            Check("i potenziamenti non presi spariscono dopo 30 s", Drops.Live.Count == 0 && Drops.statExpired - e0 == nFar, $"scaduti {Drops.statExpired - e0}/{nFar}");

            // ----- 6) Notte delle Falene: il quinto round
            Log("--- Notte delle Falene");
            Drops.OnPlayerDied();
            Drops.disabled = true;
            Enemy.debugFreeze = false;
            if (rd == null || reserved == null) Log("nessuna candela con zona rimasta: Notte delle Falene non provata");
            else
            {
                var reg = rd.RegionOf(reserved);
                int cap = RoundRules.AliveCap(reg.area) + RoundRules.MothAliveBonus;
                Enemy.ClearAll();
                pl.invulnerable = true;
                PlaceBeside(pl, fd.cur, reserved);
                yield return new WaitForSeconds(0.8f);
                RoundDirector.Round = RoundRules.MothEvery - 1;
                sp = Drops.statSpawned; pk0 = Drops.statPicked; f0 = Drops.OnFloor;
                int falls0 = FloorDirector.statFalls;
                reserved.Use();
                yield return null;
                float mulM = rd.Mood == RoundMood.Calm ? RoundRules.CalmMul : rd.Mood == RoundMood.Alarm ? RoundRules.AlarmMul : 1f;
                int adopted = rd.Alive;
                int expect = Mathf.Max(2, Mathf.RoundToInt(RoundRules.Count(RoundDirector.Round, fd.floor) * mulM * RoundRules.MothCountMul) - adopted);
                Check("il quinto round e' la Notte delle Falene", rd.Fighting && rd.Moth && RoundDirector.Round == RoundRules.MothEvery, $"round {RoundDirector.Round}, falene {rd.Moth}");
                Check("falene: il numero dei nemici segue la formula (x1,6)", rd.Target == expect, $"{rd.Target} nemici, attesi {expect} (tono {rd.Mood}, {adopted} gia' dentro)");
                float tm = Time.time;
                while (rd.Spawned < Mathf.Min(3, rd.Target) && Time.time - tm < 40f) yield return null;
                int moths = 0, others = 0;
                foreach (var e in Enemy.All)
                    if (e != null && !e.IsDead && e.relentless) { if (e.type == Enemy.EnemyType.Falena) moths++; else others++; }
                Check("nascono solo falene", moths > 0 && others == 0, $"{moths} falene, {others} altri");
                var run = new RoundRun();
                yield return DriveRound(rd, cap, null, new List<Vector3>(), pl, run);
                Check("il round delle falene finisce, serrande su", !rd.Sealed, $"{run.took:F0} s, nati {rd.Spawned}/{run.seenTarget}");
                Check("tetto dei vivi: quello della zona piu' quattro, tutti dentro", run.maxAlive <= cap && run.allInside, $"massimo {run.maxAlive}, tetto {cap}");
                // le falene si stringono attorno al giocatore: se i loro collider spingono il controller, prima o poi lo fanno uscire dal muro
                Check("le falene non spingono il giocatore fuori dal piano (nessuna CADUTA)", FloorDirector.statFalls == falls0, $"cadute {FloorDirector.statFalls - falls0}");
                bool gift = (Drops.Live.Count == 1 && Drops.Live[0].kind == DropKind.Ammo) || Drops.statPicked == pk0 + 1;
                Check("a fine round cade sempre un Reliquiario, fuori dal tetto del piano", Drops.statSpawned == sp + 1 && gift && Drops.OnFloor == f0,
                      $"caduti {Drops.statSpawned - sp}, a terra {Drops.Live.Count}, presi {Drops.statPicked - pk0}, sul piano {Drops.OnFloor}");
            }

            // ----- 7) foto dei quattro modelli
            if (shots)
            {
                Log("--- foto");
                Enemy.ClearAll();
                foreach (var pk in new List<DropPickup>(Drops.Live)) if (pk != null) Destroy(pk.gameObject);
                Drops.NewFloor();
                Home();
                yield return new WaitForSeconds(0.3f);
                var kinds = new[] { DropKind.Ammo, DropKind.Insta, DropKind.Double, DropKind.Nuke };
                for (int i = 0; i < 4; i++) Drops.Spawn(kinds[i], home + dir * 3.6f + side * ((i - 1.5f) * 1.1f), false);
                yield return new WaitForSeconds(1.2f);
                Shot("drop_quattro");
                foreach (var pk in new List<DropPickup>(Drops.Live))
                {
                    Vector3 from = NavPoint(pk.transform.position - dir * 2.05f);          // appena fuori portata di raccolta
                    pl.Teleport(from);
                    Vector3 look = pk.Center - (from + Vector3.up * 1.6f);
                    pl.yaw = Mathf.Atan2(look.x, look.z);
                    pl.pitch = Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude);
                    yield return new WaitForSeconds(0.25f);
                    GameManager.CamShot(Dir + "/drop_" + pk.kind + ".png", null, false);
                }
            }

            Log(passed ? "ESITO: ok" : "ESITO: FALLITO");
            Application.Quit(passed ? 0 : 1);
        }

        // ------------------------------------------------------------------ i volanti non spingono il giocatore (-pushtest)
        /// <summary>Un giocatore fermo (nessun comando) con dieci falene all'attacco addosso: i loro collider non devono spostarlo ne' farlo
        /// uscire dal pavimento. Con -bodiesclash (il vecchio comportamento) la prova serve a vedere il difetto; esito in
        /// hotel_build/furtivo/spinta.txt, uscita 0 ok, 1 fallito, 2 nessuno spiazzo libero.
        ///   PryingEyes.exe -batchmode -nographics -autotest -stealthtest -pushtest -seed N -floor N [-bodiesclash]</summary>
        IEnumerator CheckPush()
        {
            var gm = GameManager.I;
            var fd = FloorDirector.I;
            var pl = gm.player;
            bool passed = true;
            void Check(string what, bool ok, string detail = "")
            {
                Log((ok ? "ok       " : "FALLITO  ") + what + (detail.Length > 0 ? "   [" + detail + "]" : ""));
                if (!ok) passed = false;
            }
            yield return new WaitForSeconds(2.0f);
            Log($"piano {fd.floor} seme {fd.runSeed}: stanze={fd.cur.plan.rooms.Count}; vecchio comportamento (-bodiesclash): {System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bodiesclash") >= 0}");
            Drops.disabled = true;
            Enemy.ClearAll();
            PlayerController.holdLook = true;
            pl.invulnerable = true;
            Enemy.debugFreeze = false;
            if (!FindOpenSpot(fd.cur, out Vector3 spot, out Vector3 dir)) { Log("nessuno spiazzo libero su questo piano: prova non applicabile (cambia seme)"); Application.Quit(2); yield break; }
            Vector3 home = NavPoint(spot - dir * 1.9f);
            pl.Teleport(home);
            pl.yaw = Mathf.Atan2(dir.x, dir.z);
            pl.pitch = 0f;
            yield return new WaitForSeconds(0.6f);                       // il controller si posa a terra
            Vector3 start = pl.transform.position;
            int falls0 = FloorDirector.statFalls;
            const int N = 10;
            for (int i = 0; i < N; i++)
            {
                Vector3 p = start + Quaternion.Euler(0f, i * 36f, 0f) * Vector3.forward * (2.4f + (i % 3) * 0.6f);
                p.y = 1.8f;
                var e = Enemy.Spawn(Enemy.EnemyType.Falena, p);
                e.MakeRelentless();
            }
            float t0 = Time.time, maxMove = 0f, minY = 99f, nearest = 99f;
            while (Time.time - t0 < 14f)
            {
                Vector3 pp = pl.transform.position;
                maxMove = Mathf.Max(maxMove, new Vector2(pp.x - start.x, pp.z - start.z).magnitude);
                minY = Mathf.Min(minY, pp.y);
                foreach (var e in Enemy.All)
                {
                    if (e == null || e.IsDead) continue;
                    Vector3 q = e.transform.position;
                    if (q.y < 0.3f || q.y > 2.2f) continue;               // all'altezza del corpo
                    nearest = Mathf.Min(nearest, new Vector2(q.x - pp.x, q.z - pp.z).magnitude);
                }
                yield return null;
            }
            Log($"misure: spostamento massimo {maxMove:F2} m, quota minima {minY:F2}, falena piu' vicina all'asse del giocatore {nearest:F2} m, cadute {FloorDirector.statFalls - falls0}");
            Check("le falene non spostano un giocatore fermo", maxMove < 0.1f, $"{maxMove:F2} m in 14 s");
            Check("e non lo fanno uscire dal pavimento", minY > -0.5f && FloorDirector.statFalls == falls0, $"quota minima {minY:F2}, cadute {FloorDirector.statFalls - falls0}");
            Enemy.ClearAll();
            Log(passed ? "ESITO: ok" : "ESITO: FALLITO");
            Application.Quit(passed ? 0 : 1);
        }
    }
}

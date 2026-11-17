#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using Production.Economy;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Cattedrale
{
    /// <summary>-autotest -cattedraletest [-seed N] [-cattedraleshots]: le due zone da soli, con il controller e le interazioni vere. Parte dalla
    /// cattedrale (profilo di prova azzerato), compra al mercato, costruisce due opere (la pianta si rifa': fonte al suo posto, la navata con il
    /// tetto), scende nell'Ossario, raccoglie il bottino di un nemico, versa nella cassetta delle elemosine, risale dalla scala (zaino nel
    /// magazzino, punti in oboli), muore in una seconda discesa (meta' delle comuni salve, il resto nelle spoglie), riprende le spoglie nella
    /// terza, poi gioca il terzo livello: tre ondate, il Cardinale come boss del ramo, il trofeo e la risalita. Non tocca il salvataggio di chi gioca.</summary>
    public sealed class CattedraleTest : MonoBehaviour
    {
        int checks, failed;
        bool shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-cattedraletest") < 0) return;
            Fabbrica.Azzera();               // profilo di prova: si parte da una cattedrale in rovina e da un magazzino vuoto
            new GameObject("CattedraleTest").AddComponent<CattedraleTest>();
        }

        void Check(string name, bool ok, string detail = "")
        {
            checks++; if (!ok) failed++;
            Debug.Log("[PryingEyes] cattedraletest " + (ok ? "ok " : "FALLITO ") + name + (detail.Length > 0 ? " (" + detail + ")" : ""));
        }

        IEnumerator Start()
        {
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-cattedraleshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more;
                try { more = sequence.MoveNext(); }
                catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false, e.Message); break; }
                if (!more) break;
                yield return sequence.Current;
            }
            Time.timeScale = 1f;
            Debug.Log($"[PryingEyes] cattedraletest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        static FloorDirector Fd => FloorDirector.I;
        static GameManager Gm => GameManager.I;

        IEnumerator WaitHub(float timeout = 30f)
        {
            float t0 = Time.time;
            while ((Fd == null || !Fd.InHub || Fd.transitioning || Fd.cur == null) && Time.time - t0 < timeout) yield return null;
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator WaitLevel(int floor, float timeout = 40f)
        {
            float t0 = Time.time;
            while ((Fd == null || Fd.InHub || Fd.transitioning || Fd.floor != floor || Fd.cur == null) && Time.time - t0 < timeout) yield return null;
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Porta il giocatore davanti a una cosa e la guarda, poi lascia lavorare l'interazione vera (Interact.Tick nel fotogramma).</summary>
        IEnumerator LookAt(IInteract it, float distance = 1.3f)
        {
            var pl = Gm.player;
            Vector3 target = it.Point;
            Vector3 from = target - new Vector3(target.x - pl.transform.position.x, 0f, target.z - pl.transform.position.z).normalized * distance;
            if (it is Component comp)
            {
                // davanti alla cosa: dal lato da cui la si usa (il davanti del suo oggetto, se ce l'ha)
                var f = comp.transform.forward; f.y = 0f;
                if (f.sqrMagnitude > 0.1f) from = target + f.normalized * distance;
            }
            from.y = 0.06f;
            pl.Teleport(from);
            yield return null;
            Vector3 eye = pl.cam.position;
            Vector3 d = target - eye;
            pl.yaw = Mathf.Atan2(d.x, d.z);
            pl.pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude);
            yield return new WaitForSeconds(0.15f);
        }

        static T Find<T>(Func<T, bool> pick) where T : UnityEngine.Object
        {
            foreach (var x in FindObjectsByType<T>(FindObjectsSortMode.None)) if (pick(x)) return x;
            return null;
        }

        IEnumerator Use(IInteract it, string what)
        {
            yield return LookAt(it);
            bool selected = Interact.Tick(Gm.player.cam) && Interact.Current == it;     // nelle prove il giocatore non lo chiama da solo
            Check(what + ": si guarda e si usa con F", selected, selected ? "" : $"F userebbe {Interact.Current?.GetType().Name ?? "niente"}");
            if (selected) Interact.TryUse(); else it.Use();
            yield return new WaitForSeconds(0.2f);
        }

        void Shot(string name)
        {
            if (!shots) return;
            System.IO.Directory.CreateDirectory(TestPaths.Out("cattedrale"));
            GameManager.CamShot(TestPaths.Out("cattedrale/" + name + ".png"), null, true, 1600, 900);
        }

        IEnumerator Run()
        {
            yield return WaitHub(40f);
            var fd = Fd; var gm = Gm;
            Check("si parte dalla cattedrale", fd != null && fd.InHub && fd.cur != null && fd.cur.plan.hub, fd != null ? $"InHub {fd.InHub}" : "nessun regista");
            if (fd == null || fd.cur == null) yield break;
            Check("la pianta della cattedrale e' coerente", HubPlan.Validate(fd.cur.plan).Count == 0 && fd.cur.plan.rooms.Count == 10, string.Join("; ", HubPlan.Validate(fd.cur.plan)));
            Check("si nasce sul sagrato", fd.cur.RoomAt(gm.player.transform.position)?.kind == RoomKind.Sagrato, fd.cur.RoomAt(gm.player.transform.position)?.label ?? "fuori");
            Check("la navata senza le volte non ha tetto", fd.cur.plan.rooms[HubPlan.Navata].ruined);
            int stalls = FindObjectsByType<BancoOfferta>(FindObjectsSortMode.None).Length, gates = FindObjectsByType<CancelloRamo>(FindObjectsSortMode.None).Length;
            int sites = FindObjectsByType<CantiereOpera>(FindObjectsSortMode.None).Length;
            Check("il mercato ha tutte le offerte sui banchi", stalls == Listino.Offerte.Length, $"{stalls} di {Listino.Offerte.Length}");
            Check("la discesa ha i cinque cancelli", gates == 5, gates.ToString());
            Check("i cantieri delle prime opere ci sono", sites >= 4 && Find<CantiereOpera>(s => s.opera == "fonte") != null && Find<CantiereOpera>(s => s.opera == "volte") != null, sites + " cantieri");
            Check("c'e' il registro della Fabbrica", Find<RegistroFabbrica>(_ => true) != null);
            Shot("01_sagrato");
            yield return new WaitForSeconds(9f);
            Check("nella cattedrale non ci sono ondate ne' nemici", Enemy.All.Count == 0 && (RoundDirector.I == null || !RoundDirector.I.Fighting), $"{Enemy.All.Count} nemici");

            // ---- il mercato: una ampolla d'acqua santa per 30 oboli
            Fabbrica.Banca.Deposit(Risorse.Oboli, 200);
            var ampolla = Find<BancoOfferta>(b => b.offerta == "rif_acquasanta");
            Check("la speziale vende l'acqua santa", ampolla != null && ampolla.Prompt != null && ampolla.Prompt.Contains("30 OBOLI"), ampolla?.Prompt ?? "");
            if (ampolla != null) yield return Use(ampolla, "banco della speziale");
            Check("l'acquisto toglie gli oboli e mette il rifornimento", Fabbrica.Banca.Get(Risorse.Oboli) == 170 && Fabbrica.Banca.Get(Rifornimenti.Acquasanta) == 1,
                $"oboli {Fabbrica.Banca.Get(Risorse.Oboli)}, ampolle {Fabbrica.Banca.Get(Rifornimenti.Acquasanta)}");

            // ---- la prima opera: la fonte battesimale
            Fabbrica.Banca.Deposit(Bundle.Of((Risorse.Ossa, 15), (Risorse.Acciaio, 15), (Risorse.Sangue, 2)));
            var fonte = Find<CantiereOpera>(s => s.opera == "fonte");
            Check("il cantiere della fonte dice che si puo' costruire", fonte != null && fonte.Prompt != null && fonte.Prompt.StartsWith("COSTRUISCI"), fonte?.Prompt ?? "");
            if (fonte != null) yield return Use(fonte, "cantiere della fonte");
            yield return new WaitForSeconds(0.2f);
            yield return WaitHub();
            Check("la fonte e' costruita e pagata esattamente", Fabbrica.Costruita("fonte") && Fabbrica.Banca.Get(Risorse.Ossa) == 0 && Fabbrica.Banca.Get(Risorse.Sangue) == 0,
                Fabbrica.Banca.Snapshot().ToString());
            Check("la cattedrale si e' rifatta con la fonte al suo posto", Find<CantiereOpera>(s => s.opera == "fonte") == null && GameObject.Find("chi_fonte") != null);

            // ---- le volte: la navata riprende il tetto
            Fabbrica.Banca.Deposit(Bundle.Of((Risorse.Legno, 40), (Risorse.Acciaio, 20)));
            var volte = Find<CantiereOpera>(s => s.opera == "volte");
            if (volte != null) yield return Use(volte, "cantiere delle volte");
            yield return new WaitForSeconds(0.2f);
            yield return WaitHub();
            Check("le volte sono fatte e la navata ha il tetto", Fabbrica.Costruita("volte") && !fd.cur.plan.rooms[HubPlan.Navata].ruined && fd.cur.plan.rooms[HubPlan.Navata].ceil == CeilKind.BarrelPointed);
            Check("le volte aprono i cantieri che reggono (altare, organo)", Find<CantiereOpera>(s => s.opera == "altare") != null && Find<CantiereOpera>(s => s.opera == "organo") != null);
            Shot("02_navata");

            // ---- prima discesa: l'Ossario
            var gate = Find<CancelloRamo>(g => g.ramo == 0);
            var locked = Find<CancelloRamo>(g => g.ramo == 1);
            Check("il cancello del Teatro e' murato finche' non c'e' la sua opera", locked != null && !Fabbrica.RamoAperto(1) && locked.Prompt.Contains("TEATRO"), locked?.Prompt ?? "");
            Check("il cancello dell'Ossario e' aperto", gate != null && gate.Prompt != null && gate.Prompt.StartsWith("SCENDI"), gate?.Prompt ?? "");
            if (gate != null) yield return Use(gate, "cancello dell'Ossario");
            yield return WaitLevel(1);
            Check("si scende al primo livello dell'Ossario", !fd.InHub && fd.floor == 1 && fd.cur.plan.catacombe && fd.cur.plan.theme == Theme.Albergo, $"piano {fd.floor}, tema {fd.cur.plan.theme}");
            Check("niente ascensori: cancello e scala di pietra", fd.cur.arrival != null && fd.cur.arrival.grate != null && fd.cur.exit != null && fd.cur.exit.grate != null);
            Check("i punti di partenza contano le volte", gm.score == 750, gm.score.ToString());
            Check("l'acqua santa: base, ampolla comprata, fonte", Equipment.I != null && Equipment.I.grenades >= 3, Equipment.I != null ? Equipment.I.grenades.ToString() : "nessuna");
            Check("l'ampolla e' stata consumata", Fabbrica.Banca.Get(Rifornimenti.Acquasanta) == 0);
            var stair = Find<ScalaRisalita>(_ => true);
            var alms = Find<CassettaElemosine>(_ => true);
            Check("ci sono la scala per risalire e la cassetta delle elemosine", stair != null && alms != null);
            Shot("03_livello1");
            if (shots && fd.cur.arrival != null && fd.cur.exit != null)
            {
                // i cancelli visti dalla stanza: quello da cui si arriva (aperto) e quello d'uscita (ancora chiuso)
                yield return ShotFrom(fd.cur.arrival.frontPoint + fd.cur.arrival.lookOut * 2.6f, fd.cur.arrival.doorPoint + Vector3.up * 1.3f, "03b_cancello_arrivo");
                yield return ShotFrom(fd.cur.exit.frontPoint + fd.cur.exit.lookOut * 2.2f, fd.cur.exit.doorPoint + Vector3.up * 1.2f, "03c_cancello_uscita");
                gm.player.Teleport(fd.cur.arrival.spawnPoint);
                yield return new WaitForSeconds(0.2f);
            }

            // ---- un nemico ucciso lascia risorse, che si raccolgono passandoci vicino
            int spawned0 = RisorsaPickup.statSpawned, taken0 = RisorsaPickup.statTaken;
            yield return KillOneFor();
            Check("il nemico ucciso lascia risorse a terra", RisorsaPickup.statSpawned > spawned0, $"{RisorsaPickup.statSpawned - spawned0} mucchi");
            yield return Collect();
            var carried = Fabbrica.Spedizione.Carried.Snapshot();
            Check("le risorse finiscono nello zaino, non nel magazzino", RisorsaPickup.statTaken > taken0 && !carried.IsEmpty, carried.ToString());

            // ---- la cassetta delle elemosine
            gm.AddPoints(600);
            int scoreBefore = gm.score, oboliBefore = Fabbrica.Banca.Get(Risorse.Oboli);
            if (alms != null) yield return Use(alms, "cassetta delle elemosine");
            Check("la cassetta toglie 500 punti e mette 33 oboli al sicuro", gm.score == scoreBefore - 500 && Fabbrica.Banca.Get(Risorse.Oboli) == oboliBefore + 33,
                $"punti {scoreBefore}->{gm.score}, oboli {oboliBefore}->{Fabbrica.Banca.Get(Risorse.Oboli)}");

            // ---- la risalita: lo zaino nel magazzino, i punti in oboli
            var expectCarried = Fabbrica.Spedizione.Carried.Snapshot();
            int expectOboli = Fabbrica.Banca.Get(Risorse.Oboli) + Listino.OboliDaPunti(gm.score, Fabbrica.OboliOgni);
            int bankOssa = Fabbrica.Banca.Get(Risorse.Ossa);
            if (stair != null) yield return Use(stair, "scala della risalita");
            yield return WaitHub();
            Check("si risale alla cattedrale", fd.InHub && Fabbrica.D.estrazioni == 1);
            Check("lo zaino e' nel magazzino e i punti sono oboli", Fabbrica.Banca.Get(Risorse.Oboli) == expectOboli && Fabbrica.Banca.Get(Risorse.Ossa) == bankOssa + expectCarried[Risorse.Ossa],
                $"oboli {Fabbrica.Banca.Get(Risorse.Oboli)} (attesi {expectOboli}), zaino [{expectCarried}]");
            Check("tornati su si riparte dalla pistola e senza perk", Perks.Count == 0 && gm.player.weapon.Def.grade == 1, gm.player.weapon.Def.name);

            // ---- seconda discesa: la morte
            yield return Descend(0);
            Catacombe.forceNext = true;
            yield return KillOneFor(Enemy.EnemyType.Statua);
            yield return Collect();
            var before = Fabbrica.Spedizione.Carried.Snapshot();
            var bankBefore = Fabbrica.Banca.Snapshot();
            gm.player.Hurt(99999f);
            yield return new WaitForSeconds(0.3f);
            gm.player.BleedOutNow();
            yield return new WaitForSeconds(0.5f);
            yield return WaitHub(20f);
            Check("morire riporta alla cattedrale", fd.InHub && Fabbrica.D.morti == 1);
            int keptOssa = Fabbrica.Banca.Get(Risorse.Ossa) - bankBefore[Risorse.Ossa];
            var spoglie = Fabbrica.Spoglie(0);
            Check("si salva la meta' delle comuni, il resto va nelle spoglie", keptOssa == before[Risorse.Ossa] / 2 && spoglie[Risorse.Ossa] == before[Risorse.Ossa] - before[Risorse.Ossa] / 2 &&
                Fabbrica.D.spoglieRamo == 0, $"zaino [{before}], salvate {keptOssa} ossa, spoglie [{spoglie}]");

            // ---- terza discesa: le spoglie sono nel primo livello
            yield return Descend(0);
            var remains = Find<RisorsaPickup>(p => p.spoglie);
            Check("le spoglie sono nel primo livello del ramo", remains != null);
            if (remains != null)
            {
                gm.player.Teleport(remains.transform.position + Vector3.up * 0.1f);
                yield return new WaitForSeconds(1.2f);
            }
            Check("riprese, le spoglie tornano nello zaino", Fabbrica.D.spoglieRamo == -1 && Fabbrica.Spedizione.Carried.Get(Risorse.Ossa) >= spoglie[Risorse.Ossa], Fabbrica.Spedizione.Carried.Snapshot().ToString());

            // ---- il terzo livello: tre ondate, il boss del ramo, il trofeo, la risalita
            fd.DebugGoTo(3);
            yield return WaitLevel(3);
            Check("il terzo livello e' quello del boss", Rami.LivelloDelBoss(fd.floor) && fd.cur.exit.lockedText().Contains("CARDINALE"), fd.cur.exit.lockedText());
            SurvivalMode.Hold(false);
            RoundDirector.I.SurvivalNextWaveIn(0.5f);
            bool bossSeen = false; float t0 = Time.time;
            while (!fd.ExitOpen && Time.time - t0 < 360f)
            {
                if (RoundDirector.I.BranchBossFighting && Enemy.Boss != null && !Enemy.Boss.IsDead)
                {
                    if (!bossSeen) Check("il Cardinale scende come boss del ramo", Enemy.BossTitle == "Il Cardinale" && Enemy.Boss.type == Enemy.EnemyType.Cardinale && Enemy.Boss.maxHp >= 3200f,
                        $"{Enemy.BossTitle}, {Enemy.Boss.type}, {Enemy.Boss.maxHp:F0} pv");
                    bossSeen = true;
                }
                foreach (var e in new List<Enemy>(Enemy.All)) if (e != null && !e.IsDead) e.Damage(999999f, e.transform.position + Vector3.up, false, false);
                if (!RoundDirector.I.Fighting) RoundDirector.I.SurvivalNextWaveIn(0.3f);
                yield return new WaitForSeconds(0.4f);
            }
            Check("dopo tre ondate arriva il boss", bossSeen);
            Check("caduto il boss, l'uscita si apre", fd.ExitOpen, $"ondate del livello {RoundDirector.I.FloorWaves}");
            Check("il trofeo del Cardinale e' gia' nel magazzino", Fabbrica.Banca.Get(Risorse.Reliquie) == 3 && Fabbrica.BossVinto(0), $"reliquie {Fabbrica.Banca.Get(Risorse.Reliquie)}");
            yield return new WaitForSeconds(1.6f);         // il cancello finisce di alzarsi
            Check("l'uscita del boss risale alla cattedrale", fd.cur.exit.Prompt != null && fd.cur.exit.Prompt.StartsWith("RISALI"), fd.cur.exit.Prompt ?? "");
            Shot("04_boss_caduto");
            fd.cur.exit.Use();
            yield return WaitHub();
            Check("risalita dal boss: spedizione compiuta", fd.InHub && Fabbrica.D.estrazioni == 2 && !Fabbrica.Spedizione.Active);

            // ---- il salvataggio: ricaricato da capo, dice le stesse cose
            var bank = Fabbrica.Banca.Snapshot(); var built = string.Join(",", Fabbrica.Albero.BuiltIds());
            Fabbrica.Carica(true);
            Check("il salvataggio ricaricato e' identico", Fabbrica.Banca.Snapshot().Equals(bank) && string.Join(",", Fabbrica.Albero.BuiltIds()) == built, $"[{Fabbrica.Banca.Snapshot()}] {built}");
            Check("il cancello del Teatro si puo' costruire con le reliquie", Fabbrica.Albero.Unlocked("cancello_teatro"));
            Shot("05_ritorno");
        }

        IEnumerator ShotFrom(Vector3 from, Vector3 target, string name)
        {
            var pl = Gm.player;
            from.y = 0.06f;
            pl.Teleport(from);
            yield return null;
            Vector3 d = target - pl.cam.position;
            pl.yaw = Mathf.Atan2(d.x, d.z);
            pl.pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude);
            yield return new WaitForSeconds(0.4f);
            Shot(name);
        }

        IEnumerator Descend(int ramo)
        {
            yield return WaitHub();
            var gate = Find<CancelloRamo>(g => g.ramo == ramo);
            if (gate == null) { Check("cancello del ramo " + ramo, false); yield break; }
            gate.Use();
            yield return WaitLevel(Rami.Piano(ramo, 1));
        }

        /// <summary>Un nemico vicino al giocatore, ucciso dalla prova (il bottino lo tira a sorte il gioco; forceNext lo garantisce).</summary>
        IEnumerator KillOneFor(Enemy.EnemyType type = Enemy.EnemyType.Monaco)
        {
            var pl = Gm.player;
            Vector3 at = pl.transform.position + pl.transform.forward * 2.5f;
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            Catacombe.forceNext = true;
            var e = Enemy.Spawn(type, at);
            yield return new WaitForSeconds(0.3f);
            if (e != null) e.Damage(999999f, e.transform.position + Vector3.up, false, true);
            yield return new WaitForSeconds(0.6f);
        }

        IEnumerator Collect()
        {
            foreach (var p in new List<RisorsaPickup>(RisorsaPickup.Live))
            {
                if (p == null || p.spoglie) continue;
                Gm.player.Teleport(p.transform.position + Vector3.up * 0.1f);
                yield return new WaitForSeconds(0.9f);
            }
        }
    }
}
#endif

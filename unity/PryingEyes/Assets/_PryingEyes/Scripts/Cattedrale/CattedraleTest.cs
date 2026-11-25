#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Production.Economy;
using PryingEyes.Characters;
using PryingEyes.Proc;
using PryingEyes.Testing;
using UnityEngine;

namespace PryingEyes.Cattedrale
{
    /// <summary>-autotest -cattedraletest [-seed N] [-cattedraleshots]: le due zone da soli, con il controller e le interazioni vere. Parte dalla
    /// cattedrale (profilo di prova azzerato), compra al mercato, costruisce due opere (la pianta si rifa': fonte al suo posto, la navata con il
    /// tetto), scende nell'Ossario, raccoglie il bottino di un nemico, versa nella cassetta delle elemosine, risale dalla scala (zaino nel
    /// magazzino, punti in oboli), muore in una seconda discesa (meta' delle comuni salve, il resto nelle spoglie), riprende le spoglie nella
    /// terza, poi gioca il terzo livello: tre ondate, il Cardinale come boss del ramo, il trofeo e la risalita. Non tocca il salvataggio di chi gioca.</summary>
    public sealed class CattedraleTest : PlayerTest
    {
        protected override string Suite { get { return "cattedraletest"; } }
        protected override string PhotoFlag { get { return "-cattedraleshots"; } }
        protected override string ExceptionCheck { get { return null; } }          // this suite has never had a closing check on the exceptions
        protected override bool HoldFire { get { return true; } }                  // the bot of the automatic run does not shoot by itself (-cattedraletest was in the no-fire flags of GameManager)
        protected override void Finish() { Time.timeScale = 1f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!TestRun.Has("-cattedraletest")) return;
            Fabbrica.Azzera();               // profilo di prova: si parte da una cattedrale in rovina e da un magazzino vuoto
            InstallWhen<CattedraleTest>("-cattedraletest");
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
            if (!Photos) return;
            System.IO.Directory.CreateDirectory(TestPaths.Out("cattedrale"));
            GameManager.CamShot(TestPaths.Out("cattedrale/" + name + ".png"), null, true, 1600, 900);
        }

        protected override IEnumerator Run()
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
            if (Photos && fd.cur.arrival != null && fd.cur.exit != null)
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

            // ---- la spedizione sospesa: si salva, si riprende nello stesso posto e si ritrova tutto (zaino, punti, armi, perk, magie, lanci)
            yield return RunSaveChecks(fd, gm);
            stair = Find<ScalaRisalita>(_ => true);          // il livello e' stato ricostruito: le cose di prima non ci sono piu'
            alms = Find<CassettaElemosine>(_ => true);

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

            // ---- una spedizione sospesa che non si riprende vale una morte
            yield return DiscardChecks();

            // ---- il salvataggio: ricaricato da capo, dice le stesse cose
            var bank = Fabbrica.Banca.Snapshot(); var built = string.Join(",", Fabbrica.Albero.BuiltIds());
            Fabbrica.Carica(true);
            Check("il salvataggio ricaricato e' identico", Fabbrica.Banca.Snapshot().Equals(bank) && string.Join(",", Fabbrica.Albero.BuiltIds()) == built, $"[{Fabbrica.Banca.Snapshot()}] {built}");
            Check("il cancello del Teatro si puo' costruire con le reliquie", Fabbrica.Albero.Unlocked("cancello_teatro"));
            Shot("05_ritorno");
        }

        /// <summary>Salva a meta' del primo livello con un equipaggiamento riconoscibile, azzera tutto come in una sessione nuova e riprende: ogni cosa
        /// deve tornare com'era, il file si consuma e la ripresa non conta un livello sceso ne' una spedizione in piu'.</summary>
        IEnumerator RunSaveChecks(FloorDirector fd, GameManager gm)
        {
            var owner = Fabbrica.Proprietario;
            Check("la spedizione ha un personaggio a cui salvarsi", owner != null);
            if (owner == null) yield break;
            var pl = gm.player;
            int ar = WeaponDefs.IndexOf("ar9_malleus"), pistol = WeaponDefs.IndexOf("p9_pieta");
            pl.PerkState.Add(PerkId.DoppioColpo);
            pl.weapon.Equip(ar);
            pl.weapon.mag = 5; pl.weapon.reserve = 42;
            Equipment.Ensure(pl);
            Equipment.I.grenades = 3; Equipment.I.lures = 2; Equipment.I.mines = 0;
            pl.Arcana.Witch = 1u << 12; pl.Arcana.SelectedSpell = 12; pl.Arcana.Essence = 77f;
            int score = gm.score, seed = fd.runSeed, floor = fd.floor, branch = Fabbrica.Ramo;
            int descended = Fabbrica.D.livelliScesi, trips = Fabbrica.D.spedizioni, deaths = Fabbrica.D.morti;
            var pack = Fabbrica.Spedizione.Carried.Snapshot();

            // un'area comprata in questo livello: le porte pagate non devono richiudersi con la ripresa (i punti spesi non tornano)
            var doors = RoomAccessDoors.I;
            int bought = -1, openBefore = 0;
            if (doors != null)
            {
                foreach (var room in fd.cur.plan.rooms) if (bought < 0 && doors.Access.CanBuy(doors.Access.StartRoom, room.id)) bought = room.id;
                if (bought >= 0) doors.ApplySnapshot(new List<int>(doors.Access.OpenRooms) { bought }, true);
                openBefore = doors.Access.OpenRooms.Count;
            }
            Check("un'area di questo livello e' comprata (le sue porte sono su)", doors != null && bought >= 0 && doors.Access.IsOpen(bought) && openBefore >= 2, $"area {bought}, {openBefore} aperte");

            Check("tra un'ondata e l'altra si puo' salvare", fd.CanSuspend, fd.SuspendProblem ?? "");
            bool saved = fd.SuspendExpedition(out string problem);
            Check("la spedizione si salva nel file del personaggio", saved && Roster.HasRun(owner), problem ?? "");
            Check("salvare non e' morire: nessuna spedizione aperta, nessuna morte, nessuna risalita", !Fabbrica.Spedizione.Active && Fabbrica.D.morti == deaths && Fabbrica.D.estrazioni == 0);
            var run = Roster.SuspendedRun(owner);
            Check("il file dice dove, con cosa e con quanti punti", run != null && run.floor == floor && run.seed == seed && run.branch == branch && run.score == score &&
                RunSave.ToBundle(run.carried).Equals(pack), run != null ? $"livello {run.floor}, seme {run.seed}, {run.score} punti, zaino [{RunSave.ToBundle(run.carried)}]" : "nessun file");
            Check("armi, perk, magie e lanci sono nel file", run != null && run.state.Has("weapons") && run.state.Has("perks") && run.state.Has("magic") && run.state.Has("equipment"));
            var savedAreas = run != null ? run.state.Get("areas") : null;
            Check("le aree comprese sono nel file", savedAreas != null && savedAreas.Get("open") != null && savedAreas.Get("open").Count == openBefore && savedAreas.Int("rooms") == fd.cur.plan.rooms.Count,
                savedAreas != null && savedAreas.Get("open") != null ? $"{savedAreas.Get("open").Count} aperte su {savedAreas.Int("rooms")} stanze" : "nessuna chiave");
            if (run == null) yield break;

            // come in una sessione nuova: pistola di partenza, niente perk ne' magie, niente punti, niente lanci
            pl.weapon.ResetLoadout(); pl.PerkState.ResetRun(); pl.Arcana.ResetRun(); Equipment.I.ResetRun(0, 0, 0); gm.score = 0;
            Check("prima di riprendere e' tutto da capo", pl.weapon.CurrentIndex == pistol && !pl.weapon.Owns(ar) && Perks.Count == 0 && !pl.Arcana.HasW(12) && gm.score == 0);
            Check("la ripresa parte", fd.ResumeExpedition(run));
            yield return new WaitForSeconds(0.2f);
            yield return WaitLevel(floor);

            Check("si e' allo stesso livello, con lo stesso seme", fd.floor == floor && fd.runSeed == seed && !fd.InHub && fd.cur != null, $"livello {fd.floor}, seme {fd.runSeed}");
            Check("lo zaino e' quello di prima e la spedizione e' aperta", Fabbrica.Spedizione.Active && Fabbrica.Spedizione.Carried.Snapshot().Equals(pack) && Fabbrica.Ramo == branch,
                Fabbrica.Spedizione.Carried.Snapshot().ToString());
            Check("i punti sono quelli di prima", gm.score == score, $"{gm.score} invece di {score}");
            Check("il perk e' tornato", Perks.Has(PerkId.DoppioColpo));
            Check("le due armi sono tornate, con le munizioni dell'arma in mano", pl.weapon.Owns(ar) && pl.weapon.Owns(pistol) && pl.weapon.CurrentIndex == ar && pl.weapon.mag == 5 && pl.weapon.reserve == 42,
                $"in mano {pl.weapon.Def.id}, {pl.weapon.mag}/{pl.weapon.reserve}");
            Check("i lanci sono contati come prima", Equipment.I.grenades == 3 && Equipment.I.lures == 2 && Equipment.I.mines == 0, $"{Equipment.I.grenades}/{Equipment.I.lures}/{Equipment.I.mines}");
            Check("la magia scelta e l'essenza sono tornate", pl.Arcana.HasW(12) && pl.Arcana.SelectedSpell == 12 && pl.Arcana.Essence > 70f, $"essenza {pl.Arcana.Essence:F1}");
            var after = RoomAccessDoors.I;
            bool doorsUp = after != null;
            if (after != null) foreach (var d in after.Doors) if (after.Access.IsOpen(d.Opening.a) && after.Access.IsOpen(d.Opening.b) && !d.Shutter.IsUp) doorsUp = false;
            Check("l'area comprata e' ancora aperta, con le sue porte su", after != null && bought >= 0 && after.Access.IsOpen(bought) && after.Access.OpenRooms.Count == openBefore && doorsUp,
                $"{(after != null ? after.Access.OpenRooms.Count : -1)} aperte (erano {openBefore}), area {bought}, porte su {doorsUp}");
            Check("il file e' stato consumato: si riprende una volta sola", !Roster.HasRun(owner));
            Check("riprendere non conta una discesa ne' una spedizione in piu'", Fabbrica.D.livelliScesi == descended && Fabbrica.D.spedizioni == trips, $"scese {Fabbrica.D.livelliScesi} (erano {descended}), spedizioni {Fabbrica.D.spedizioni} (erano {trips})");
        }

        /// <summary>"Ricomincia dalla cattedrale" (RunSave.Discard): lo zaino sospeso conta come una morte, il file non si distrugge.</summary>
        IEnumerator DiscardChecks()
        {
            var owner = Fabbrica.Proprietario;
            if (owner == null) { Check("c'e' un personaggio a cui abbandonare la spedizione", false); yield break; }
            var made = new RunRecord { branch = 0, floor = 2, score = 900, carried = RunSave.FromBundle(Bundle.Of((Risorse.Ossa, 10), (Risorse.Legno, 4))) };
            Check("una spedizione sospesa a mano si scrive", Roster.SaveRun(owner, made) && Roster.HasRun(owner));
            int deaths = Fabbrica.D.morti, ossa = Fabbrica.Banca.Get(Risorse.Ossa);
            RunSave.Discard(owner, Roster.SuspendedRun(owner));
            var lost = Fabbrica.Spoglie(0);
            Check("abbandonarla vale una morte: meta' delle comuni salve, il resto nelle spoglie", Fabbrica.D.morti == deaths + 1 && Fabbrica.Banca.Get(Risorse.Ossa) == ossa + 5 && lost[Risorse.Ossa] == 5 && !Fabbrica.Spedizione.Active,
                $"morti {deaths}->{Fabbrica.D.morti}, ossa in magazzino {ossa}->{Fabbrica.Banca.Get(Risorse.Ossa)}, spoglie [{lost}]");
            Check("il file non c'e' piu' e ne resta una copia in consumed", !Roster.HasRun(owner) && File.Exists(Path.Combine(Roster.Folder, "runs", "consumed", owner.id + ".json")));
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

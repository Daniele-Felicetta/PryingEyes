using System.Collections;
using System.Collections.Generic;
using Production.Economy;
using Production.Saves;
using PryingEyes.Cattedrale;
using PryingEyes.Characters;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>"Salva e torna al menu": la spedizione da soli si sospende in un file del personaggio (Characters/RunSave.cs, Roster.SaveRun) e
    /// "Gioca" la riprende. Si riparte dall'inizio del livello in cui si era (stesso seme, quindi stessa pianta), con lo zaino, i punti, le
    /// armi, i perk, le magie, i lanci e le aree gia' comprese in quel livello; le ondate di quel livello ricominciano. Quello che non si salva
    /// (la carica delle torce, i nemici e i punti-ondata del livello, le trappole delle porte, i libri comprati nella Biblioteca di quel livello)
    /// e' scritto in docs/game/saves-and-characters.md.
    ///
    /// Regole: solo da soli (in rete e a schermo condiviso il bottino va gia' diretto nel magazzino), tra un'ondata e l'altra (come la scala
    /// della risalita: la pausa non e' una via di fuga) e non a livello finito (si rigiocherebbe tutto il livello, boss compreso). Il file si
    /// consuma alla ripresa: una spedizione sospesa si riprende una volta sola.</summary>
    public partial class FloorDirector
    {
        static RunRecord pendingResume;
        bool resumedLevel;      // il livello che si ricostruisce era gia' contato tra quelli scesi (Fabbrica.LivelloSceso)

        /// <summary>Perche' non si puo' salvare adesso, a parole per il menu (null: si puo').</summary>
        public string SuspendProblem
        {
            get
            {
                if (!Catacombe.Hub) return "Il salvataggio della spedizione c'è solo giocando da soli.";
                if (InHub || floor < 1 || !Fabbrica.Spedizione.Active) return "Non c'è nessuna spedizione da salvare.";
                if (transitioning) return "Aspetta che il livello sia pronto.";
                if (gm == null || gm.player == null || !gm.player.alive) return "Da morto non si salva.";
                if (RoundDirector.I != null && RoundDirector.I.Fighting) return "Si salva tra un'ondata e l'altra.";
                if (exitOpen) return "Il livello è finito: scendi (o risali) prima di salvare, altrimenti dovresti rigiocarlo.";
                if (Fabbrica.Proprietario == null) return "Nessun personaggio a cui salvarla.";
                return null;
            }
        }

        public bool CanSuspend => SuspendProblem == null;

        /// <summary>Scrive la spedizione nel file del personaggio e la chiude senza conseguenze (non e' una morte). Dopo, chi chiama torna al menu.
        /// Se il file non si scrive la partita resta com'e'.</summary>
        public bool SuspendExpedition(out string problem)
        {
            problem = SuspendProblem;
            if (problem != null) return false;
            var owner = Fabbrica.Proprietario;
            var run = new RunRecord
            {
                branch = Fabbrica.Ramo, floor = floor, seed = runSeed, score = gm.score,
                bellUsed = Fabbrica.CampanellaUsata, revealMaps = revealMaps,
                carried = RunSave.FromBundle(Fabbrica.Spedizione.Carried.Snapshot()),
                state = RunSave.CaptureKit(gm.player),
            };
            run.state.Set("areas", CaptureAreas());
            if (!Roster.SaveRun(owner, run)) { problem = "Non riesco a scrivere il file (controlla il disco)."; return false; }
            Fabbrica.Sospendi();
            Debug.Log($"[PryingEyes] spedizioni: {owner.name} sospesa nel ramo {run.branch} al livello {floor}, {run.score} punti, zaino [{Fabbrica.Spedizione.Carried.Snapshot()}]");
            return true;
        }

        /// <summary>Il menu ha scelto "Riprendi": la prossima <see cref="StartRun"/> parte da questa spedizione invece che dalla cattedrale.</summary>
        public static void ResumeNext(RunRecord run) => pendingResume = run;

        static bool TakeResume(out RunRecord run)
        {
            run = pendingResume;
            pendingResume = null;
            return run != null && run.Playable && Catacombe.Hub;
        }

        /// <summary>Riprende una spedizione sospesa subito, anche a partita in corso (per le prove). Falso se non si puo'.</summary>
        public bool ResumeExpedition(RunRecord run)
        {
            if (run == null || !run.Playable || transitioning || !Catacombe.Hub) return false;
            StartCoroutine(ResumeCo(run));
            return true;
        }

        IEnumerator ResumeCo(RunRecord run)
        {
            transitioning = true;
            gm = GameManager.I;
            SetInvulnerable(true);
            FadeAll(1f);
            Fabbrica.Carica();
            var owner = Fabbrica.Proprietario;
            Fabbrica.Riprendi(run.branch, RunSave.ToBundle(run.carried), run.bellUsed);
            runSeed = run.seed != 0 ? run.seed : Random.Range(1, 1 << 28);
            ResetForExpedition(Bundle.Empty);                      // la base di ogni spedizione: prezzi delle opere, perk delle cappelle, vita, scudo
            RunSave.ApplyKit(gm.player, run.state);                // ...e sopra quello che si aveva
            gm.score = Mathf.Max(0, run.score);
            revealMaps = run.revealMaps;
            Seats.ForHuds(h => { h.SetScoreLabel("PUNTI"); h.SetScore(gm.score); });
            resumedLevel = true;
            InHub = false;
            yield return GoTo(run.floor, true);
            resumedLevel = false;
            if (cur == null)
            {
                // il livello non si e' costruito: lo zaino torna solo nel file (nessuna morte), la ripresa si puo' riprovare
                Debug.LogWarning("[PryingEyes] spedizioni: il livello " + run.floor + " non si e' costruito, la spedizione resta nel file");
                Fabbrica.Sospendi();
                transitioning = false;
                yield break;
            }
            RestoreAreas(run.state.Get("areas"));                  // le porte pagate restano aperte: i punti spesi per comprarle non tornano
            if (owner != null) Roster.ClearRun(owner);             // la spedizione e' in gioco: il file non serve piu' (e non deve poterla duplicare)
            Debug.Log($"[PryingEyes] spedizioni: ripresa al livello {run.floor} (ramo {run.branch}), {gm.score} punti, {(RoomAccessDoors.I != null ? RoomAccessDoors.I.Access.OpenRooms.Count : 0)} aree aperte, zaino [{Fabbrica.Spedizione.Carried.Snapshot()}]");
        }

        /// <summary>Le aree comprese su questo livello (<c>state["areas"]</c>): i numeri delle stanze aperte e quante stanze ha il piano (un piano che
        /// una versione futura generasse diverso non riceverebbe numeri che non gli appartengono).</summary>
        JsonItem CaptureAreas()
        {
            var areas = JsonItem.Object();
            var doors = RoomAccessDoors.I;
            if (doors == null || doors.Access == null || cur == null) return areas;
            var open = JsonItem.Array();
            foreach (int room in doors.Access.OpenRooms) open.Add(room);
            return areas.Set("rooms", cur.plan.rooms.Count).Set("open", open);
        }

        /// <summary>Riapre le aree salvate (le porte salgono subito). Un salvataggio senza questa chiave (una spedizione sospesa da un gioco che non la
        /// scriveva) o di un piano con un altro numero di stanze lascia il livello com'e' appena costruito.</summary>
        void RestoreAreas(JsonItem areas)
        {
            var doors = RoomAccessDoors.I;
            if (doors == null || doors.Access == null || cur == null || areas == null || !areas.IsObject) return;
            var open = areas.Get("open");
            if (open == null || !open.IsArray || areas.Int("rooms") != cur.plan.rooms.Count) return;
            var ids = new List<int>();
            foreach (var item in open.Items) if (item != null && item.IsNumber) ids.Add(item.AsInt(-1));
            try
            {
                doors.ApplySnapshot(ids, true);
                RefreshObjectiveNow();
            }
            catch (System.ArgumentException e) { Debug.LogWarning("[PryingEyes] spedizioni: le aree salvate non valgono (" + e.Message + "), il livello resta com'e'"); }
        }
    }
}

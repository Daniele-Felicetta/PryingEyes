#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.IO;
using Production.Economy;
using Production.Saves;
using PryingEyes.Cattedrale;
using UnityEngine;
using PryingEyes.Testing;

namespace PryingEyes.Characters
{
    /// <summary>-autotest -chartest: i personaggi nel gioco vero, sul profilo di prova (cartella characters-prova, mai quella di chi gioca).
    /// Crea un personaggio, rifiuta i nomi doppi o vuoti, controlla che la cattedrale di ognuno sia sua (costruire e depositare per uno non
    /// tocca l'altro), che l'esperienza faccia salire di livello una volta sola, che tutto torni uguale dopo aver riletto i file, che un campo
    /// e un'opera che questa versione non conosce sopravvivano a un salvataggio, e che eliminare un personaggio lo metta in "deleted" senza
    /// perderlo. Le regole dei formati sono provate fuori da Unity (tools/saveslab); qui c'e' il collegamento col gioco.</summary>
    public sealed class CharacterTest : PlayerTest
    {
        protected override string Suite { get { return "chartest"; } }
        protected override string ExceptionCheck { get { return null; } }       // la prova non ne ha mai avuto uno

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() { InstallWhen<CharacterTest>("-chartest"); }

        protected override IEnumerator Run()
        {
            yield return null;                  // il primo fotogramma: il menu c'e', l'elenco e' stato letto
            Sequence();
        }

        static string PathOf(CharacterRecord r) => Path.Combine(Roster.Folder, r.id + ".json");

        void Sequence()
        {
            string folder = Roster.Folder;
            if (!Roster.IsTest || !folder.Contains("characters-")) throw new InvalidOperationException("la prova gira solo sul profilo di prova: " + folder);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);      // cartella di prova: si riparte da zero
            Roster.Load(true);
            Fabbrica.Carica(true);

            // ---- il personaggio di prova e uno nuovo
            Check("c'e' il personaggio di prova, livello 1", Roster.All.Count == 1 && Roster.Local != null && Roster.Local.origin == CharacterOrigin.Test && Roster.Local.Level == 1, Roster.Local?.name);
            var prova = Roster.Local;
            var ada = Roster.Create("  Ada  ", "dottoressa", out var problem);
            Check("Ada nasce con il nome ripulito e il suo file", ada != null && problem == NameProblem.None && ada.name == "Ada" && ada.look == "dottoressa" && File.Exists(PathOf(ada)));
            Check("lo stesso nome (anche con altre maiuscole) e' rifiutato", Roster.Create("ADA", "custode", out problem) == null && problem == NameProblem.Taken);
            Check("un nome vuoto o senza lettere e' rifiutato", Roster.Create("   ", "custode", out problem) == null && problem == NameProblem.Empty && Roster.Create("???", "custode", out problem) == null && problem == NameProblem.NoLetters);
            Check("due personaggi nell'elenco", Roster.All.Count == 2);

            // ---- ognuno ha la sua cattedrale
            Fabbrica.Seleziona(prova);
            Fabbrica.Banca.Deposit(Bundle.Of((Risorse.Ossa, 15), (Risorse.Acciaio, 15), (Risorse.Sangue, 2)));
            var built = Fabbrica.Costruisci("fonte");
            Check("Prova costruisce la fonte", built == WorkTree.Check.Ok && Fabbrica.Costruita("fonte"), built.ToString());
            Fabbrica.Seleziona(ada);
            Check("la cattedrale di Ada e' vuota", !Fabbrica.Costruita("fonte") && Fabbrica.Banca.Get(Risorse.Ossa) == 0 && Fabbrica.Proprietario == ada);
            Fabbrica.Banca.Deposit(Risorse.Oboli, 7);
            Fabbrica.Salva(true);
            Fabbrica.Seleziona(prova);
            Check("Prova ha ancora la sua fonte e nessun obolo", Fabbrica.Costruita("fonte") && Fabbrica.Banca.Get(Risorse.Oboli) == 0 && Fabbrica.Proprietario == prova);

            // ---- l'esperienza
            int raised = 0;
            Action<CharacterRecord> onLevel = c => raised++;
            Progress.LevelledUp += onLevel;
            Progress.Award(ada, Leveling.XpToReach(2));
            Check("Ada sale al livello 2, una volta sola", ada.Level == 2 && raised == 1, $"livello {ada.Level}, salite {raised}");
            Progress.Award(ada, 1);
            Check("un punto in piu' non e' un altro livello", ada.Level == 2 && raised == 1);
            Progress.LevelledUp -= onLevel;
            long before = prova.xp;
            Progress.Kill(true, false);
            Check("un'uccisione alla testa vale " + Leveling.HeadshotXp + " e si conta", prova.xp == before + Leveling.HeadshotXp && prova.stats.kills == 1 && prova.stats.headshots == 1, $"xp {before} -> {prova.xp}");
            Check("a livello 1 non c'e' bonus; nelle prove non ce n'e' mai", Fabbrica.Livello == 1 && Fabbrica.PuntiIniziali == 500);

            // ---- tutto torna uguale dopo aver riletto i file
            Fabbrica.Carica(true);              // scrive quello che c'e' in sospeso e rilegge dai file
            var prova2 = Roster.Local;
            var ada2 = Roster.Find(ada.id);
            Check("i file sono stati riletti (oggetti nuovi)", prova2 != prova && ada2 != ada && prova2.id == prova.id);
            Check("Prova: uccisione e fonte ci sono ancora", prova2.stats.kills == 1 && prova2.xp == prova.xp && Fabbrica.Costruita("fonte"));
            string text = File.ReadAllText(PathOf(prova2));
            Check("il file porta formato e versione", text.Contains("\"format\": \"" + CharacterSchema.Kind + "\"") && text.Contains("\"version\": " + CharacterSchema.Version), text.Substring(0, Math.Min(80, text.Length)).Replace("\n", " "));
            Fabbrica.Seleziona(ada2);
            Check("Ada: nome, aspetto, livello e oboli ci sono ancora", ada2.name == "Ada" && ada2.look == "dottoressa" && ada2.Level == 2 && Fabbrica.Banca.Get(Risorse.Oboli) == 7);

            // ---- un campo e un'opera che questa versione non conosce sopravvivono a un salvataggio
            var doc = JsonItem.Parse(File.ReadAllText(PathOf(ada2)));
            doc.ObjectAt("cathedral").Set("fieldOfTomorrow", JsonItem.Object().Set("keep", true));
            doc.ObjectAt("cathedral").ArrayAt("opere").Add("opera_di_domani");
            doc.ObjectAt("extras").ObjectAt("future").Set("x", 1);
            File.WriteAllText(PathOf(ada2), doc.ToJson(true));
            Fabbrica.Carica(true);
            var ada3 = Roster.Find(ada.id);
            Fabbrica.Seleziona(ada3);
            Fabbrica.Banca.Deposit(Risorse.Oboli, 1);
            Fabbrica.Salva(true);
            var saved = JsonItem.Parse(File.ReadAllText(PathOf(ada3)));
            bool keptWork = false;
            foreach (var w in saved.Path("cathedral", "opere").Items) if (w.AsString() == "opera_di_domani") keptWork = true;
            Check("il campo sconosciuto della cattedrale resta nel file", saved.Path("cathedral", "fieldOfTomorrow", "keep") != null && saved.Path("cathedral", "fieldOfTomorrow", "keep").AsBool());
            Check("l'opera sconosciuta resta nel file e non conta fra quelle costruite", keptWork && !Fabbrica.Costruita("opera_di_domani"));
            Check("gli extras di un sistema futuro restano", saved.Path("extras", "future", "x") != null && saved.Path("extras", "future", "x").AsInt() == 1);
            Check("e il salvataggio nuovo c'e' (8 oboli)", saved.Path("cathedral", "banca", "0", "n") != null && Fabbrica.Banca.Get(Risorse.Oboli) == 8);

            // ---- eliminare mette il file da parte
            string adaFile = PathOf(ada3);
            Fabbrica.Seleziona(Roster.Find(prova.id));
            Check("Ada si elimina", Roster.Delete(ada3) && Roster.Find(ada.id) == null && Roster.All.Count == 1);
            string deleted = Path.Combine(folder, SaveStore.DeletedFolder);
            Check("il file non c'e' piu' fra i personaggi ma e' in deleted", !File.Exists(adaFile) && Directory.Exists(deleted) && Directory.GetFiles(deleted).Length > 0);
            Check("il personaggio locale e' ancora Prova", Roster.Local != null && Roster.Local.id == prova.id);
        }
    }
}
#endif

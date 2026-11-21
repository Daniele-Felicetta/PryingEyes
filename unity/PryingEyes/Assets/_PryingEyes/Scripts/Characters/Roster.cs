using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Production.Saves;
using UnityEngine;

namespace PryingEyes.Characters
{
    /// <summary>L'elenco dei personaggi di questa macchina, su disco: una cartella <c>characters</c> in persistentDataPath con un file per
    /// personaggio (formato e migrazioni: <see cref="CharacterSchema"/>, regole di scrittura e di recupero: Production.Saves.SaveStore).
    /// Le prove automatiche e <c>-profilo NOME</c> usano una cartella a parte (<c>characters-prova</c>...) e mai quella di chi gioca.
    ///
    /// Chi gioca ha un personaggio "locale" (l'ultimo scelto, si ricorda fra una sessione e l'altra): quello della partita da soli, della
    /// cooperativa in rete e del posto 1 dello schermo condiviso. Gli altri posti dello schermo condiviso ne scelgono uno ciascuno (Seat.character).
    /// I file si scrivono con un piccolo ritardo (<see cref="MarkDirty"/>, <see cref="CharacterAutosave"/>) tranne i momenti che contano
    /// (<see cref="SaveNow"/>): una pila di raccolte non fa una pila di scritture.</summary>
    public static class Roster
    {
        const string LastKey = "lastCharacter";
        const string LegacyMarker = "legacy-imported.txt";

        static SaveStore store;
        static string profile;
        static readonly List<CharacterRecord> all = new List<CharacterRecord>();
        static readonly List<string> problems = new List<string>();
        static readonly HashSet<string> dirty = new HashSet<string>();
        static bool loaded;
        static CharacterRecord local;

        /// <summary>Cambiato l'elenco o il personaggio locale: i menu si ridisegnano.</summary>
        public static event Action Changed;

        // ------------------------------------------------------------------ dove stanno
        /// <summary>"" per chi gioca; un nome per le prove automatiche ("prova") e per <c>-profilo NOME</c>.</summary>
        public static string Profile
        {
            get
            {
                if (profile != null) return profile;
                string arg = Arg("-profilo");
                if (arg != null) return profile = Tidy(arg).Length > 0 ? Tidy(arg) : "prova";
                // le prove automatiche e ogni esecuzione senza finestra (-batchmode: le revisioni del menu, le build) non toccano i personaggi di chi gioca
                bool automated = Has("-autotest") || Has("-guntest") || VideoSettings.Automated || Application.isBatchMode;
                return profile = automated ? "prova" : "";
            }
        }

        /// <summary>Una prova automatica o un profilo a parte: niente preferenze, niente importazione del vecchio file, niente bonus di livello.</summary>
        public static bool IsTest => Profile.Length > 0;

        static string Arg(string flag)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == flag) return a[i + 1];
            return null;
        }
        static bool Has(string flag) => Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;

        static string Tidy(string s)
        {
            var chars = new List<char>();
            foreach (char c in s ?? "") if ((char.IsLetterOrDigit(c) && c < 128) || c == '-' || c == '_') chars.Add(c);
            return new string(chars.ToArray());
        }

        public static string Folder => Store.Folder;

        static SaveStore Store
        {
            get
            {
                if (store != null) return store;
                string name = Profile.Length == 0 ? "characters" : "characters-" + Profile;
                store = new SaveStore(Path.Combine(Application.persistentDataPath, name), CharacterSchema.Format) { Log = line => Debug.Log("[PryingEyes] personaggi: " + line) };
                return store;
            }
        }

        static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ lettura
        public static IReadOnlyList<CharacterRecord> All { get { Load(); return all; } }
        public static CharacterRecord Local { get { Load(); return local; } }

        /// <summary>Le cose che non si sono potute leggere (file di una versione piu' nuova, rovinati...), a parole, per i menu.</summary>
        public static IReadOnlyList<string> Problems { get { Load(); return problems; } }

        public static int IndexOf(CharacterRecord r) { Load(); return all.IndexOf(r); }

        public static CharacterRecord Find(string id)
        {
            Load();
            foreach (var r in all) if (r.id == id) return r;
            return null;
        }

        public static IEnumerable<string> Names()
        {
            Load();
            foreach (var r in all) yield return r.name;
        }

        /// <summary>Legge la cartella (una volta per processo; con 'force' da capo). I file vecchi si portano alla versione di oggi da soli,
        /// con una copia del vecchio in <c>backup</c> (SaveStore); quelli che non si leggono restano dove sono e finiscono in <see cref="Problems"/>.</summary>
        public static void Load(bool force = false)
        {
            if (loaded && !force) return;
            loaded = true;
            all.Clear(); problems.Clear();
            try
            {
                foreach (var item in Store.LoadAll())
                {
                    if (item.Loadable)
                    {
                        var rec = CharacterRecord.FromJson(item.Document);
                        rec.id = item.Id;                    // il nome del file e' l'identita'
                        all.Add(rec);
                        continue;
                    }
                    string why = item.Result == null ? "?" : item.Result.Status == LoadStatus.TooNew ? "e' di una versione piu' nuova del gioco (non lo tocco)" : item.Result.ToString();
                    problems.Add(item.Id + ": " + why);
                    Debug.LogWarning($"[PryingEyes] personaggi: {item.Id} non si legge: {why}");
                }
            }
            catch (Exception e) { problems.Add("cartella illeggibile: " + e.Message); Debug.LogWarning("[PryingEyes] personaggi: " + e.Message); }
            if (!IsTest) ImportLegacy();
            all.Sort((a, b) => string.CompareOrdinal(a.createdAt + a.id, b.createdAt + b.id));
            ChooseLocal();
            CharacterAutosave.Ensure();
            Debug.Log($"[PryingEyes] personaggi: {all.Count} in {Store.Folder}" + (local != null ? $", locale {local.name} (livello {local.Level})" : ""));
        }

        static void ChooseLocal()
        {
            local = null;
            if (IsTest) { local = EnsureTestCharacter(); return; }
            string last = PlayerPrefs.GetString(LastKey, "");
            foreach (var r in all) if (r.id == last) local = r;
            if (local == null)
                foreach (var r in all) if (local == null || string.CompareOrdinal(r.savedAt, local.savedAt) > 0) local = r;
        }

        // ------------------------------------------------------------------ il vecchio salvataggio
        /// <summary>Chi aveva gia' giocato ha una cattedrale in <c>cattedrale.json</c> (un magazzino per tutto il PC): al primo avvio diventa il primo
        /// personaggio, "Il Custode", con l'esperienza che la sua storia vale. Il vecchio file resta dov'e' (una versione precedente del gioco lo
        /// legge ancora); un segno nella cartella dice che l'importazione e' fatta, cosi' non si ripete se poi si cancella il personaggio.</summary>
        static void ImportLegacy()
        {
            string marker = Path.Combine(Store.Folder, LegacyMarker), legacy = Path.Combine(Application.persistentDataPath, LegacyCathedral.FileName);
            try
            {
                if (all.Count > 0 || File.Exists(marker) || !File.Exists(legacy)) return;
                if (!LegacyCathedral.TryRead(File.ReadAllText(legacy), out var cathedral, out long xp, out string error))
                {
                    problems.Add(LegacyCathedral.FileName + " non si legge: " + error);
                    Debug.LogWarning($"[PryingEyes] personaggi: {LegacyCathedral.FileName} non si legge ({error}): resta com'e'");
                    return;
                }
                var rec = LegacyCathedral.ToCharacter(cathedral, xp, NewUnusedId(), Now(), Application.version);
                if (!Write(rec)) return;
                all.Add(rec);
                File.WriteAllText(marker, "Importato da " + LegacyCathedral.FileName + " il " + Now() + " come " + rec.id + ". Il vecchio file e' rimasto dov'era.\n");
                Debug.Log($"[PryingEyes] personaggi: {LegacyCathedral.FileName} importato come {rec.name} ({rec.id}): livello {rec.Level} ({rec.xp} xp)");
            }
            catch (Exception e)
            {
                problems.Add(LegacyCathedral.FileName + ": " + e.Message);
                Debug.LogWarning($"[PryingEyes] personaggi: importazione di {LegacyCathedral.FileName} non riuscita: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ scelta e creazione
        /// <summary>Il personaggio di questa macchina per la partita (da soli, in rete, posto 1). Si ricorda per la prossima volta.</summary>
        public static void SetLocal(CharacterRecord r)
        {
            Load();
            if (r == null || !all.Contains(r)) return;
            local = r;
            if (!IsTest) PlayerPrefs.SetString(LastKey, r.id);
            Changed?.Invoke();
        }

        static string NewUnusedId()
        {
            string id;
            do id = CharacterSchema.NewId(); while (Store.Exists(id) || all.Exists(r => r.id == id));
            return id;
        }

        /// <summary>Un personaggio nuovo con quel nome e quell'aspetto. Restituisce null e dice perche' se il nome non va.</summary>
        public static CharacterRecord Create(string rawName, string look, out NameProblem problem)
        {
            Load();
            problem = CharacterNames.Check(rawName, Names(), out string clean);
            if (problem != NameProblem.None) return null;
            return Add(CharacterRecord.Create(NewUnusedId(), clean, look, Now(), Application.version));
        }

        /// <summary>Un personaggio per un posto che non ne ha uno (lo schermo condiviso con piu' giocatori che personaggi): nome libero
        /// "Giocatore N", aspetto non ancora preso. Si puo' ritrovare e gestire poi dal menu dei personaggi.</summary>
        public static CharacterRecord CreateGuest(int seatIndex, ICollection<string> takenLooks)
        {
            Load();
            string name = CharacterNames.Unique("Giocatore " + (seatIndex + 1), Names());
            return Add(CharacterRecord.Create(NewUnusedId(), name, CharacterLooks.Resolve(CharacterLooks.Ids[seatIndex % CharacterLooks.Count], takenLooks), Now(), Application.version));
        }

        static CharacterRecord Add(CharacterRecord rec)
        {
            if (IsTest) rec.origin = CharacterOrigin.Test;
            if (!Write(rec)) return null;
            all.Add(rec);
            if (local == null) { local = rec; if (!IsTest) PlayerPrefs.SetString(LastKey, rec.id); }
            Changed?.Invoke();
            return rec;
        }

        /// <summary>Toglie il personaggio dall'elenco: il file va nella cartella <c>deleted</c> (SaveStore), da dove si puo' rimettere a mano.</summary>
        public static bool Delete(CharacterRecord r)
        {
            Load();
            if (r == null || !all.Contains(r)) return false;
            dirty.Remove(r.id);
            ClearRun(r);
            try { Store.Delete(r.id); }
            catch (Exception e) { Debug.LogWarning($"[PryingEyes] personaggi: {r.name} non eliminato: {e.Message}"); return false; }
            all.Remove(r);
            if (local == r) ChooseLocal();
            Changed?.Invoke();
            Debug.Log($"[PryingEyes] personaggi: {r.name} ({r.id}) eliminato (il file e' in {Path.Combine(Store.Folder, SaveStore.DeletedFolder)})");
            return true;
        }

        // ------------------------------------------------------------------ la spedizione sospesa
        static SaveStore runStore;
        static SaveStore Runs => runStore ?? (runStore = new SaveStore(Path.Combine(Store.Folder, "runs"), RunSchema.Format) { Log = line => Debug.Log("[PryingEyes] spedizioni: " + line) });

        /// <summary>La spedizione sospesa di questo personaggio, se c'e' e si legge; altrimenti null.</summary>
        public static RunRecord SuspendedRun(CharacterRecord c)
        {
            if (c == null || !SaveStore.ValidId(c.id)) return null;
            try
            {
                if (!Runs.Exists(c.id)) return null;
                var item = Runs.Load(c.id);
                if (!item.Loadable) { Debug.LogWarning($"[PryingEyes] spedizioni: la spedizione sospesa di {c.name} non si legge ({(item.Result != null ? item.Result.Status.ToString() : "?")})"); return null; }
                var run = RunRecord.FromJson(item.Document);
                return run.Playable ? run : null;
            }
            catch (Exception e) { Debug.LogWarning($"[PryingEyes] spedizioni: {e.Message}"); return null; }
        }

        public static bool HasRun(CharacterRecord c) => SuspendedRun(c) != null;

        /// <summary>Scrive la spedizione sospesa (una sola per personaggio: la nuova prende il posto della vecchia).</summary>
        public static bool SaveRun(CharacterRecord c, RunRecord run)
        {
            if (c == null || run == null || !SaveStore.ValidId(c.id)) return false;
            run.characterId = c.id; run.savedAt = Now(); run.savedBy = Application.version;
            try { Runs.Save(c.id, run.ToJson()); return true; }
            catch (Exception e) { Debug.LogWarning($"[PryingEyes] spedizioni: {c.name} non salvata ({e.Message})"); return false; }
        }

        /// <summary>Toglie la spedizione sospesa: quando si riprende (si consuma: due copie dello stesso zaino non devono esistere), quando si
        /// abbandona e quando il personaggio viene eliminato. Non si distrugge: l'ultima va in <c>runs/consumed/ID.json</c> (una sola per
        /// personaggio, la nuova prende il posto della vecchia), cosi' una ripresa che non parte si rimette a mano al suo posto.</summary>
        public static void ClearRun(CharacterRecord c)
        {
            if (c == null || !SaveStore.ValidId(c.id)) return;
            string path = Runs.PathOf(c.id);
            try
            {
                if (File.Exists(path))
                {
                    string keep = Path.Combine(Runs.Folder, "consumed");
                    Directory.CreateDirectory(keep);
                    string to = Path.Combine(keep, c.id + ".json");
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(path, to);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PryingEyes] spedizioni: {c.name}: la copia in consumed non e' riuscita ({e.Message}), il file si toglie lo stesso: lo zaino e' gia' in gioco");
                try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
            }
            foreach (string f in new[] { path + ".bak", path + ".tmp" })
                try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ prove
        /// <summary>Il nome del personaggio di prova: quello dato con <c>-coopname</c> (le prove a piu' processi si chiamano per nome) o "Prova".</summary>
        static string TestName() { string n = Arg("-coopname"); return string.IsNullOrWhiteSpace(n) ? "Prova" : n; }
        static string TestId() { string t = Tidy(Arg("-coopname") ?? ""); return t.Length > 0 ? "prova-" + t : "prova"; }

        static CharacterRecord EnsureTestCharacter()
        {
            string id = TestId();
            foreach (var r in all) if (r.id == id) return r;
            var rec = CharacterRecord.Create(id, TestName(), CharacterLooks.Fallback, Now(), Application.version);
            rec.origin = CharacterOrigin.Test;
            Write(rec);
            all.Add(rec);
            return rec;
        }

        /// <summary>Per le prove: il personaggio di prova riparte da zero (mai quello di chi gioca).</summary>
        public static void ResetTestCharacter()
        {
            if (!IsTest) throw new InvalidOperationException("il profilo di chi gioca non si azzera");
            Load();
            string id = TestId();
            dirty.Remove(id);
            ClearRun(Find(id));                    // una spedizione sospesa da una prova di prima non deve rispuntare
            string path = Store.PathOf(id);
            foreach (string f in new[] { path, path + ".bak", path + ".tmp" })
                try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { }
            Load(true);
        }

        // ------------------------------------------------------------------ scrittura
        /// <summary>Il personaggio ha qualcosa da scrivere: lo fa <see cref="CharacterAutosave"/> fra un attimo.</summary>
        public static void MarkDirty(CharacterRecord r)
        {
            if (r == null || !all.Contains(r)) return;
            dirty.Add(r.id);
            CharacterAutosave.Ensure();
        }

        public static bool AnyDirty => dirty.Count > 0;

        /// <summary>Scrive subito (un'opera costruita, una spedizione che comincia o finisce).</summary>
        public static bool SaveNow(CharacterRecord r)
        {
            if (r == null || !all.Contains(r)) return false;
            bool ok = Write(r);
            if (ok) dirty.Remove(r.id); else dirty.Add(r.id);
            return ok;
        }

        /// <summary>Scrive tutti quelli che hanno qualcosa in sospeso.</summary>
        public static void Flush()
        {
            if (dirty.Count == 0) return;
            foreach (string id in new List<string>(dirty))
            {
                var r = all.Find(x => x.id == id);
                if (r == null || Write(r)) dirty.Remove(id);
            }
        }

        static float failLogAt = -99f;

        static bool Write(CharacterRecord r)
        {
            r.savedAt = Now(); r.savedBy = Application.version;
            try { Store.Save(r.id, r.ToJson()); return true; }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup - failLogAt > 10f) { failLogAt = Time.realtimeSinceStartup; Debug.LogWarning($"[PryingEyes] personaggi: {r.name} non salvato ({e.Message}): riprovo fra poco"); }
                return false;
            }
        }
    }
}

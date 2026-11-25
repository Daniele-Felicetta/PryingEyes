using System;
using System.Collections.Generic;

namespace PryingEyes.Characters
{
    // Il personaggio di un giocatore: nome, aspetto, livello e tutto quello che accumula. I file senza il motore (CharacterLooks, Leveling,
    // CharacterRecord, CathedralJson, RunRecord) li compila anche tools/saveslab, che prova i formati dei salvataggi e le migrazioni senza
    // aprire Unity; quelli che usano Unity (Roster, Progress, CharacterAutosave, RunSave, CharacterTest) ci stanno sopra.

    /// <summary>Gli aspetti tra cui si sceglie: i quattro sopravvissuti (modelli in Resources/Survivors, vedi docs/game/characters.md). Un
    /// personaggio ne porta uno per sempre; in una partita con piu' giocatori due personaggi non hanno mai lo stesso aspetto (Resolve).</summary>
    public static class CharacterLooks
    {
        /// <summary>Gli id dei modelli. L'ordine e' quello degli slot di una volta: lo slot 0 era il Custode, il 1 la Dottoressa...</summary>
        public static readonly string[] Ids = { "custode", "dottoressa", "reporter", "cacciatrice" };
        public static readonly string[] Names = { "Il Custode", "La Dottoressa", "Il Reporter", "La Cacciatrice" };
        public static readonly string[] Blurbs =
        {
            "Robusto, capelli grigi e baffi, giacca di tela olivastra, sciarpa rossa e coppola di tweed.",
            "Esile, caschetto castano, cappotto crema e sciarpa verde acqua.",
            "Biondo scuro con la frangia di lato, giacca senape, pantaloni blu e sciarpa grigia.",
            "Gambe forti, capelli ramati in una lunga treccia sotto il cappello scuro, giacca bordeaux.",
        };

        public static int Count => Ids.Length;
        public static string Fallback => Ids[0];

        public static int IndexOf(string id)
        {
            for (int i = 0; i < Ids.Length; i++) if (string.Equals(Ids[i], id, StringComparison.Ordinal)) return i;
            return -1;
        }

        public static bool IsValid(string id) => IndexOf(id) >= 0;

        /// <summary>L'id valido piu' vicino: quello dato se esiste, altrimenti il primo.</summary>
        public static string Valid(string id) => IsValid(id) ? id : Fallback;

        public static string NameOf(string id) { int i = IndexOf(id); return i >= 0 ? Names[i] : Names[0]; }
        public static string BlurbOf(string id) { int i = IndexOf(id); return i >= 0 ? Blurbs[i] : Blurbs[0]; }

        /// <summary>Quello che si vedra' davvero in una partita in cui altri hanno gia' preso certi aspetti: il preferito se e' libero,
        /// altrimenti il primo libero (nell'ordine di Ids). Il personaggio non cambia: vale per quella partita.</summary>
        public static string Resolve(string preferred, ICollection<string> taken)
        {
            string want = Valid(preferred);
            if (taken == null || !taken.Contains(want)) return want;
            foreach (string id in Ids) if (!taken.Contains(id)) return id;
            return want;                                 // piu' giocatori che aspetti: non succede (i posti sono quattro)
        }
    }
}

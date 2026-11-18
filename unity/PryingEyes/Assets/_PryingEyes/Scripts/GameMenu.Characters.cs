using System;
using System.Collections.Generic;
using System.Globalization;
using PryingEyes.Characters;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>I personaggi nel menu: l'elenco (si sceglie con un clic, e' quello con cui si gioca da soli, in rete e al posto 1 dello schermo
    /// condiviso), la creazione di uno nuovo (nome e aspetto), l'eliminazione (il file va in <c>deleted</c>) e, per lo schermo condiviso, la
    /// scelta di chi gioca in ogni posto. La logica sta in Characters.Roster; qui ci sono solo i pannelli.</summary>
    public partial class GameMenu
    {
        static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
        const int CharRows = 5;

        RectTransform charPanel, charCreatePanel, charDeletePanel, castPanel;
        RectTransform charFrom;                                    // da dove si e' arrivati (principale, cooperativa): "Indietro" torna li'
        Text charName, charBody, charXp, charNote, charPageLabel, mainWho;
        Image charBar;
        Button[] charRowButtons;
        Button charNewButton, charDeleteButton, charPrev, charNext, coopCharacterButton;
        int charPage;
        CharacterRecord charShown;

        InputField createName;
        Button createLookButton, createButton;
        Text createBlurb, createError, createTitle;
        int createLook;
        bool createAfterNeed;

        Text deleteText;

        Text castSubtitle, castStatus;
        Button[] castRows;
        Button castGo;
        int castCount;
        readonly CharacterRecord[] cast = new CharacterRecord[Seats.MaxSeats];     // null = un personaggio nuovo, creato quando si comincia

        // ------------------------------------------------------------------ costruzione
        void BuildCharacters()
        {
            // --- l'elenco
            charPanel = Stretch(backdrop, "Personaggi");
            Label(charPanel, "Titolo", new Vector2(0, 232), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Personaggi", Ink, FontStyle.Normal);
            Label(charPanel, "Sottotitolo", new Vector2(0, 186), new Vector2(1100, 28), 19, TextAnchor.MiddleCenter,
                "Ogni personaggio ha il suo nome, il suo livello e la sua cattedrale: si sceglie con un clic.", Dim, FontStyle.Italic);
            charRowButtons = new Button[CharRows];
            for (int i = 0; i < CharRows; i++)
            {
                int row = i;
                var b = MakeButton(charPanel, "", new Vector2(-300, 128 - i * 56), () => PickCharacterRow(row));
                ResizeButton(b, new Vector2(480, 50), 22);
                charRowButtons[i] = b;
            }
            charPrev = MakeButton(charPanel, "‹  prima", new Vector2(-440, -158), () => { charPage--; RefreshCharacters(); });
            ResizeButton(charPrev, new Vector2(190, 38), 18);
            charNext = MakeButton(charPanel, "dopo  ›", new Vector2(-160, -158), () => { charPage++; RefreshCharacters(); });
            ResizeButton(charNext, new Vector2(190, 38), 18);
            charPageLabel = Label(charPanel, "Pagina", new Vector2(-300, -158), new Vector2(110, 24), 15, TextAnchor.MiddleCenter, "", Dim, FontStyle.Normal);

            charName = Label(charPanel, "NomeScelto", new Vector2(250, 126), new Vector2(480, 50), 36, TextAnchor.MiddleLeft, "", Ink, FontStyle.Normal);
            Plate(charPanel, "Barra fondo", new Vector2(250, 92), new Vector2(440, 6), new Color(0.69f, 0.53f, 0.30f, 0.25f));
            charBar = Plate(charPanel, "Barra esperienza", new Vector2(30, 92), new Vector2(0, 6), new Color(0.78f, 0.62f, 0.36f, 0.95f));
            var barRect = charBar.rectTransform; barRect.pivot = new Vector2(0f, 0.5f);      // cresce da sinistra
            charXp = Label(charPanel, "Esperienza", new Vector2(250, 70), new Vector2(480, 24), 16, TextAnchor.MiddleLeft, "", Brass, FontStyle.Normal);
            charBody = Label(charPanel, "Dettagli", new Vector2(250, -58), new Vector2(480, 220), 17, TextAnchor.UpperLeft, "", Dim, FontStyle.Normal);

            charNewButton = MakeButton(charPanel, "Nuovo personaggio", new Vector2(-190, -206), () => OpenCharacterCreate(charPanel, false));
            charDeleteButton = MakeButton(charPanel, "Elimina", new Vector2(190, -206), OpenCharacterDelete);
            charNote = Label(charPanel, "Nota", new Vector2(0, -246), new Vector2(1100, 26), 15, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            MakeButton(charPanel, "Indietro", new Vector2(0, -278), CharactersBackNow);

            // --- il personaggio nuovo
            charCreatePanel = Stretch(backdrop, "NuovoPersonaggio");
            createTitle = Label(charCreatePanel, "Titolo", new Vector2(0, 214), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Nuovo personaggio", Ink, FontStyle.Normal);
            Label(charCreatePanel, "EtichettaNome", new Vector2(-250, 130), new Vector2(200, 28), 20, TextAnchor.MiddleLeft, "Il nome", Dim, FontStyle.Normal);
            createName = MakeInput(charCreatePanel, new Vector2(60, 130), new Vector2(340, 40), "Un nome");
            createName.characterLimit = CharacterNames.MaxLength;
            createName.onValueChanged.AddListener(_ => { if (createError != null) createError.text = ""; });
            createLookButton = MakeButton(charCreatePanel, "", new Vector2(0, 66), () => { createLook = (createLook + 1) % CharacterLooks.Count; RefreshCreateLook(); });
            ResizeButton(createLookButton, new Vector2(440, 50), 22);
            createBlurb = Label(charCreatePanel, "Aspetto", new Vector2(0, 8), new Vector2(760, 56), 18, TextAnchor.UpperCenter, "", Dim, FontStyle.Italic);
            createError = Label(charCreatePanel, "Errore", new Vector2(0, -52), new Vector2(1000, 30), 20, TextAnchor.MiddleCenter, "", new Color(1f, 0.55f, 0.45f), FontStyle.Normal);
            createButton = MakeButton(charCreatePanel, "Crea", new Vector2(0, -110), CreateCharacterNow);
            MakeButton(charCreatePanel, "Annulla", new Vector2(0, -250), CharacterCreateBack);

            // --- l'eliminazione
            charDeletePanel = Stretch(backdrop, "EliminaPersonaggio");
            Label(charDeletePanel, "Titolo", new Vector2(0, 150), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Eliminare il personaggio?", Ink, FontStyle.Normal);
            deleteText = Label(charDeletePanel, "Conseguenze", new Vector2(0, 45), new Vector2(900, 120), 20, TextAnchor.MiddleCenter, "", Dim, FontStyle.Normal);
            MakeButton(charDeletePanel, "Annulla", new Vector2(0, -60), () => Show(charPanel));
            MakeButton(charDeletePanel, "Elimina", new Vector2(0, -130), DeleteCharacterNow);

            // --- chi gioca in ogni posto (schermo condiviso)
            castPanel = Stretch(backdrop, "ChiGioca");
            Label(castPanel, "Titolo", new Vector2(0, 232), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Chi gioca?", Ink, FontStyle.Normal);
            castSubtitle = Label(castPanel, "Sottotitolo", new Vector2(0, 184), new Vector2(1100, 28), 19, TextAnchor.MiddleCenter,
                "Ognuno sceglie il suo personaggio: premi sul posto per passare al successivo.", Dim, FontStyle.Italic);
            castRows = new Button[Seats.MaxSeats];
            for (int i = 0; i < castRows.Length; i++)
            {
                int seat = i;
                var b = MakeButton(castPanel, "", new Vector2(0, 126 - i * 58), () => CastNext(seat));
                ResizeButton(b, new Vector2(760, 50), 22);
                castRows[i] = b;
            }
            castStatus = Label(castPanel, "Stato", new Vector2(0, -126), new Vector2(1100, 30), 18, TextAnchor.MiddleCenter, "", Ink, FontStyle.Italic);
            castGo = MakeButton(castPanel, "Comincia", new Vector2(0, -170), CastStart);
            MakeButton(castPanel, "Indietro", new Vector2(0, -250), () => Show(splitPanel));

            foreach (var p in new[] { charPanel, charCreatePanel, charDeletePanel, castPanel }) p.gameObject.AddComponent<MenuPanelFade>();

            // --- i riferimenti nei pannelli che c'erano: il personaggio sotto "Gioca" e nella cooperativa
            var play = mainPanel.Find("Pulsante Gioca");
            if (play != null)
            {
                mainWho = Label(play, "Personaggio", new Vector2(40, 0), new Vector2(250, 30), 15, TextAnchor.MiddleRight, "", Brass, FontStyle.Normal);
                mainWho.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            Label(coopPanel, "EtichettaPersonaggio", new Vector2(-250, 98), new Vector2(200, 28), 20, TextAnchor.MiddleLeft, "Il tuo personaggio", Dim, FontStyle.Normal);
            coopCharacterButton = MakeButton(coopPanel, "", new Vector2(60, 98), () => OpenCharacters(coopPanel));
            ResizeButton(coopCharacterButton, new Vector2(340, 40), 20);
            coopCharacterButton.GetComponentInChildren<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        // ------------------------------------------------------------------ riga del personaggio in uso
        static string Short(CharacterRecord c) => c == null ? "nessun personaggio" : c.name + " · liv. " + c.Level;

        /// <summary>Il personaggio con cui si gioca, sotto "Gioca" e nella cooperativa (si ridisegna a ogni ritorno a quei pannelli).</summary>
        void RefreshCharacterLines()
        {
            var local = Roster.Local;
            if (mainWho != null) mainWho.text = local != null ? Short(local) : "crea il tuo personaggio";
            if (coopCharacterButton != null)
            {
                var label = coopCharacterButton.GetComponentInChildren<Text>();
                if (label != null) label.text = local != null ? Short(local) + "   ›" : "Crea il tuo personaggio   ›";
            }
        }

        /// <summary>Chi sta per giocare deve avere un personaggio: se non c'e' si apre la creazione (da 'from' si torna a creazione fatta).</summary>
        bool NeedCharacter(RectTransform from)
        {
            if (Roster.Local != null) return false;
            OpenCharacterCreate(from, true);
            return true;
        }

        // ------------------------------------------------------------------ l'elenco
        void OpenCharacters(RectTransform from)
        {
            charFrom = from;
            int at = Roster.IndexOf(Roster.Local);
            charPage = at >= 0 ? at / CharRows : 0;
            charShown = Roster.Local;
            RefreshCharacters();
            Show(charPanel);
        }

        void PickCharacterRow(int row)
        {
            var list = Roster.All;
            int index = charPage * CharRows + row;
            if (index < 0 || index >= list.Count) return;
            var pick = list[index];
            Roster.SetLocal(pick);
            Cattedrale.Fabbrica.Seleziona(pick);
            charShown = pick;
            RefreshCharacters();
        }

        void RefreshCharacters()
        {
            var list = Roster.All;
            int pages = Mathf.Max(1, (list.Count + CharRows - 1) / CharRows);
            charPage = Mathf.Clamp(charPage, 0, pages - 1);
            var local = Roster.Local;
            for (int i = 0; i < CharRows; i++)
            {
                int index = charPage * CharRows + i;
                var b = charRowButtons[i];
                b.gameObject.SetActive(index < list.Count);
                if (index >= list.Count) continue;
                var c = list[index];
                bool inUse = c == local;
                var text = b.GetComponentInChildren<Text>();
                text.text = (inUse ? "◆  " : "     ") + c.name + "   ·   liv. " + c.Level;
                text.alignment = TextAnchor.MiddleLeft;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.rectTransform.sizeDelta = new Vector2(450, 50);
                b.GetComponent<Image>().color = inUse ? new Color(0.29f, 0.23f, 0.13f) : BtnColor;
            }
            charPrev.gameObject.SetActive(pages > 1 && charPage > 0);
            charNext.gameObject.SetActive(pages > 1 && charPage < pages - 1);
            charPageLabel.text = pages > 1 ? (charPage + 1) + " / " + pages : "";
            if (charShown == null || Roster.IndexOf(charShown) < 0) charShown = local;
            ShowCharacterDetail(charShown);
            charDeleteButton.interactable = charShown != null;
            var problems = Roster.Problems;
            string note = problems.Count > 0 ? problems.Count + (problems.Count == 1 ? " salvataggio non si legge (resta nella cartella): " : " salvataggi non si leggono (restano nella cartella): ") + problems[0] : "";
            charNote.text = note.Length > 130 ? note.Substring(0, 127) + "..." : note;
            RefreshCharacterLines();
        }

        void ShowCharacterDetail(CharacterRecord c)
        {
            if (c == null)
            {
                charName.text = "Nessun personaggio";
                charXp.text = "";
                charBody.text = "Crea il tuo primo personaggio: il nome, l'aspetto e quello che accumuli giocando restano con lui.";
                SetBar(0f);
                return;
            }
            charName.text = c.name;
            long next = Leveling.ToNext(c.xp);
            charXp.text = "LIVELLO " + c.Level + "  ·  " + c.xp.ToString("N0", Italian) + " xp" + (next > 0 ? "  ·  ne mancano " + next.ToString("N0", Italian) : "");
            SetBar(Leveling.Progress(c.xp));
            int works = c.cathedral != null && c.cathedral.Get("opere") != null ? c.cathedral.Get("opere").Count : 0;
            var s = c.stats;
            string when = Date(c.createdAt);
            var run = Roster.SuspendedRun(c);
            charBody.text =
                "<color=#e6dac2>" + c.LookName + "</color> — " + CharacterLooks.BlurbOf(c.look) + "\n\n" +
                "Uccisioni  " + s.kills.ToString("N0", Italian) + "  ·  alla testa  " + s.headshots.ToString("N0", Italian) + "\n" +
                "Partite  " + s.games + "  ·  in rete  " + s.coopGames + "  ·  schermo condiviso  " + s.splitGames + "\n" +
                "Tempo di gioco  " + Progress.Duration(s.playSeconds) + "\n" +
                "Cattedrale  " + works + " opere  ·  " + (c.cathedral != null ? c.cathedral.Int("estrazioni") : 0) + " risalite" + (when.Length > 0 ? "\nCreato il " + when : "") +
                (run != null ? "\n<color=#e6dac2>Spedizione sospesa</color>  " + Cattedrale.Rami.NomeLivello(run.floor) : "");
        }

        void SetBar(float k)
        {
            if (charBar == null) return;
            charBar.rectTransform.sizeDelta = new Vector2(440f * Mathf.Clamp01(k), 6f);
            charBar.rectTransform.anchoredPosition = new Vector2(250f - 220f, 92f);
        }

        static string Date(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t.ToLocalTime().ToString("d MMMM yyyy", Italian) : "";
        }

        void CharactersBackNow()
        {
            RefreshCharacterLines();
            Show(charFrom != null ? charFrom : mainPanel);
        }

        // ------------------------------------------------------------------ il personaggio nuovo
        void OpenCharacterCreate(RectTransform from, bool needed)
        {
            if (from != charPanel) charFrom = from;
            createAfterNeed = needed;
            createName.text = "";
            createLook = 0;
            var used = new HashSet<string>();
            foreach (var c in Roster.All) used.Add(c.look);
            for (int i = 0; i < CharacterLooks.Count; i++) if (!used.Contains(CharacterLooks.Ids[i])) { createLook = i; break; }      // il primo aspetto che nessuno ha ancora
            createError.text = needed ? "Per giocare serve un personaggio: dagli un nome." : "";
            RefreshCreateLook();
            Show(charCreatePanel);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(createName.gameObject);
        }

        void RefreshCreateLook()
        {
            string id = CharacterLooks.Ids[createLook];
            var label = createLookButton.GetComponentInChildren<Text>();
            if (label != null) label.text = "Aspetto:  " + CharacterLooks.NameOf(id) + "   ›";
            createBlurb.text = CharacterLooks.BlurbOf(id);
        }

        void CreateCharacterNow()
        {
            var rec = Roster.Create(createName.text, CharacterLooks.Ids[createLook], out var problem);
            if (rec == null)
            {
                createError.text = problem == NameProblem.Empty ? "Scrivi un nome."
                    : problem == NameProblem.TooLong ? "Al massimo " + CharacterNames.MaxLength + " caratteri."
                    : problem == NameProblem.NoLetters ? "Il nome ha bisogno di almeno una lettera o una cifra."
                    : problem == NameProblem.Taken ? "C'e' gia' un personaggio con questo nome."
                    : "Non sono riuscito a salvare il personaggio (controlla il disco).";
                return;
            }
            Roster.SetLocal(rec);
            Cattedrale.Fabbrica.Seleziona(rec);
            charShown = rec;
            Debug.Log($"[PryingEyes] personaggi: creato {rec.name} ({rec.LookName})");
            RefreshCharacterLines();
            if (createAfterNeed && charFrom != null) { Show(charFrom); return; }
            RefreshCharacters();
            Show(charPanel);
        }

        void CharacterCreateBack()
        {
            if (createAfterNeed && charFrom != null) Show(charFrom); else Show(charPanel);
        }

        // ------------------------------------------------------------------ l'eliminazione
        void OpenCharacterDelete()
        {
            var c = charShown;
            if (c == null) return;
            deleteText.text = c.name + " (livello " + c.Level + ") esce dall'elenco con la sua cattedrale.\nIl file va nella cartella \"deleted\" dei salvataggi: si puo' rimettere a mano.";
            Show(charDeletePanel);
        }

        void DeleteCharacterNow()
        {
            var c = charShown;
            if (c != null && Roster.Delete(c))
            {
                Cattedrale.Fabbrica.Dimentica(c);
                var next = Roster.Local;
                if (next != null) Cattedrale.Fabbrica.Seleziona(next);
                charShown = next;
            }
            RefreshCharacters();
            Show(charPanel);
        }

        // ------------------------------------------------------------------ chi gioca in ogni posto
        void OpenCast(int count)
        {
            castCount = Mathf.Clamp(count, SplitScreen.MinPlayers, SplitScreen.MaxPlayers);
            var taken = new List<CharacterRecord>();
            for (int i = 0; i < Seats.MaxSeats; i++)
            {
                CharacterRecord pick = null;
                if (i < castCount)
                {
                    if (i == 0) pick = Roster.Local;
                    if (pick == null)
                        foreach (var r in Roster.All)
                            if (!taken.Contains(r) && (pick == null || string.CompareOrdinal(r.savedAt, pick.savedAt) > 0)) pick = r;
                }
                cast[i] = pick;
                if (pick != null) taken.Add(pick);
            }
            RefreshCast();
            Show(castPanel);
        }

        /// <summary>Il posto passa al personaggio dopo nell'elenco fra quelli non presi dagli altri posti; dopo l'ultimo viene "personaggio nuovo", poi da capo.</summary>
        void CastNext(int seat)
        {
            var options = new List<CharacterRecord>();
            foreach (var r in Roster.All)
            {
                bool usedElsewhere = false;
                for (int i = 0; i < castCount; i++) if (i != seat && cast[i] == r) usedElsewhere = true;
                if (!usedElsewhere) options.Add(r);
            }
            options.Add(null);                                      // un personaggio nuovo
            int at = options.IndexOf(cast[seat]);
            cast[seat] = options[(at + 1) % options.Count];
            RefreshCast();
        }

        void RefreshCast()
        {
            for (int i = 0; i < castRows.Length; i++)
            {
                castRows[i].gameObject.SetActive(i < castCount);
                if (i >= castCount) continue;
                var c = cast[i];
                var text = castRows[i].GetComponentInChildren<Text>();
                text.text = "Giocatore " + (i + 1) + ":   " + (c != null ? c.name + "   ·   liv. " + c.Level + "   ·   " + c.LookName : "nuovo personaggio");
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.rectTransform.sizeDelta = new Vector2(720, 50);
            }
            var looks = new HashSet<string>(); bool doubled = false;
            for (int i = 0; i < castCount; i++) if (cast[i] != null && !looks.Add(cast[i].look)) doubled = true;
            castStatus.text = doubled ? "Due personaggi hanno lo stesso aspetto: in partita uno prende il primo libero (il personaggio non cambia)." : "";
        }

        void CastStart()
        {
            var chosen = new List<CharacterRecord>();
            var takenLooks = new HashSet<string>();
            for (int i = 0; i < castCount; i++)
            {
                var c = cast[i] ?? Roster.CreateGuest(i, takenLooks);
                if (c == null) { castStatus.text = "Non sono riuscito a creare il giocatore " + (i + 1) + " (controlla il disco)."; return; }
                chosen.Add(c);
                takenLooks.Add(c.look);
            }
            SplitBegin(castCount, chosen);
        }

        // ------------------------------------------------------------------ Esc e B
        /// <summary>Indietro (Esc, B) dai pannelli dei personaggi. Vero se ne ha gestito uno.</summary>
        bool CharactersBack()
        {
            if (charDeletePanel != null && charDeletePanel.gameObject.activeSelf) { MenuSound.Back(); Show(charPanel); return true; }
            if (charCreatePanel != null && charCreatePanel.gameObject.activeSelf) { MenuSound.Back(); CharacterCreateBack(); return true; }
            if (charPanel != null && charPanel.gameObject.activeSelf) { MenuSound.Back(); CharactersBackNow(); return true; }
            if (castPanel != null && castPanel.gameObject.activeSelf) { MenuSound.Back(); Show(splitPanel); return true; }
            return false;
        }

        /// <summary>Show() accende solo il pannello scelto: questo accende o spegne i quattro dei personaggi.</summary>
        void CharactersShow(RectTransform p)
        {
            if (charPanel == null) return;
            charPanel.gameObject.SetActive(p == charPanel);
            charCreatePanel.gameObject.SetActive(p == charCreatePanel);
            charDeletePanel.gameObject.SetActive(p == charDeletePanel);
            castPanel.gameObject.SetActive(p == castPanel);
            if (p == mainPanel || p == coopPanel) RefreshCharacterLines();
        }
    }
}

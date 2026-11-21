using PryingEyes.Pista;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Il campionato (Scripts/Pista/Championship.cs): si apre dal pulsante "Campionato" della preparazione della gara. Tre coppe, una per classe di moto (125 GP, Supersport, Superbike), quattro gare ciascuna sui circuiti
    /// del catalogo, con gli stessi undici avversari per tutta la stagione. Ogni coppa e' un pulsante con la stagione a cui si e' arrivati e la coppa migliore vinta; Invio o un clic corrono la gara successiva (la prima di una
    /// stagione nuova, se l'ultima e' finita). Sotto, per la coppa scelta, il calendario e la classifica (prima del via, gli avversari). "Ricomincia" butta la stagione e ne comincia un'altra col livello di adesso, dopo una
    /// conferma. Il registro, PistaCups, e' nelle preferenze del giocatore.</summary>
    public partial class GameMenu
    {
        RectTransform cupPanel;
        Button cupOpenButton, cupResetButton, cupBackButton;
        readonly Button[] cupCells = new Button[Cups.Count];
        readonly Text[] cupName = new Text[Cups.Count], cupClass = new Text[Cups.Count], cupState = new Text[Cups.Count], cupTrophy = new Text[Cups.Count];
        Text cupSubtitle, cupSummary, cupCalendarHead, cupTableHead, cupNote, cupEnter, cupResetLabel;
        readonly Text[] cupRoundName = new Text[CupMaxRounds], cupRoundMark = new Text[CupMaxRounds];
        readonly Text[] cupRowName = new Text[Cups.Riders], cupRowValue = new Text[Cups.Riders];
        int cupFocused;
        bool cupResetArmed;

        const int CupMaxRounds = 4;
        const float CupCellW = 376f, CupCellH = 124f, CupGap = 12f, CupCellY = 96f;
        static readonly Color CupAmber = new Color(1f, 0.74f, 0.18f);

        void BuildCup()
        {
            cupPanel = Stretch(backdrop, "Campionato");
            Label(cupPanel, "Titolo", new Vector2(0, 262), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Campionato", Ink, FontStyle.Normal);
            cupSubtitle = Label(cupPanel, "Sottotitolo", new Vector2(0, 214), new Vector2(1180, 30), 19, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            cupSummary = Label(cupPanel, "Riepilogo", new Vector2(0, 190), new Vector2(1180, 24), 17, TextAnchor.MiddleCenter, "", Brass, FontStyle.Normal);

            for (int i = 0; i < Cups.Count; i++) cupCells[i] = MakeCupCell(i, new Vector2((i - (Cups.Count - 1) * 0.5f) * (CupCellW + CupGap), CupCellY));

            // sotto le coppe, per quella scelta: a sinistra il calendario, a destra la classifica (prima del via, gli avversari)
            cupCalendarHead = Label(cupPanel, "Intestazione calendario", new Vector2(-328f, 10f), new Vector2(496, 26), 18, TextAnchor.MiddleLeft, "", Brass, FontStyle.Normal);
            for (int r = 0; r < CupMaxRounds; r++)
            {
                float y = -18f - r * 28f;
                cupRoundName[r] = Label(cupPanel, "Gara " + (r + 1), new Vector2(-383f, y), new Vector2(386, 26), 17, TextAnchor.MiddleLeft, "", Ink, FontStyle.Normal);
                cupRoundMark[r] = Label(cupPanel, "Gara " + (r + 1) + " stato", new Vector2(-135f, y), new Vector2(110, 26), 16, TextAnchor.MiddleRight, "", Dim, FontStyle.Italic);
            }
            cupNote = Label(cupPanel, "Nota", new Vector2(-328f, -160f), new Vector2(496, 56), 16, TextAnchor.UpperLeft, "", Dim, FontStyle.Italic);
            cupTableHead = Label(cupPanel, "Intestazione classifica", new Vector2(266f, 10f), new Vector2(612, 26), 18, TextAnchor.MiddleLeft, "", Brass, FontStyle.Normal);
            for (int i = 0; i < Cups.Riders; i++)
            {
                float left = i < 6 ? -40f : 276f, y = -18f - (i % 6) * 28f;
                cupRowName[i] = Label(cupPanel, "Pilota " + (i + 1), new Vector2(left + 90f, y), new Vector2(180, 26), 17, TextAnchor.MiddleLeft, "", Ink, FontStyle.Normal);
                cupRowValue[i] = Label(cupPanel, "Pilota " + (i + 1) + " valore", new Vector2(left + 241f, y), new Vector2(110, 26), 16, TextAnchor.MiddleRight, "", Dim, FontStyle.Normal);
            }
            cupEnter = Label(cupPanel, "Invio", new Vector2(0, -196f), new Vector2(1180, 28), 19, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);

            cupResetButton = MakeButton(cupPanel, "Ricomincia", new Vector2(-190, -262), CupReset);
            cupResetLabel = cupResetButton.GetComponentInChildren<Text>();
            cupBackButton = MakeButton(cupPanel, "Indietro", new Vector2(190, -262), CupBack);
            AddFeedback(cupResetButton, null);
            AddFeedback(cupBackButton, null);
            CupWire();
            cupPanel.gameObject.AddComponent<MenuPanelFade>();
        }

        /// <summary>Una coppa: un pulsante largo con il nome, la classe, la stagione a cui si e' arrivati e la coppa migliore vinta.</summary>
        Button MakeCupCell(int index, Vector2 pos)
        {
            var rt = Rect(cupPanel, "Coppa " + (index + 1), new Vector2(CupCellW, CupCellH), pos);
            var plate = rt.gameObject.AddComponent<Image>();
            plate.color = BtnColor;
            var edge = rt.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.64f, 0.49f, 0.28f, 0.50f); edge.effectDistance = new Vector2(1f, -1f);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            button.colors = ButtonColors();
            button.onClick.AddListener(() => { if (!PistaMode.CupAvailable(Cups.All[index])) { MenuSound.Back(); return; } MenuSound.Confirm(); CupStart(index); });          // a career without a bike of the class cannot start it
            cupName[index] = Label(rt, "Nome", new Vector2(0f, 40f), new Vector2(344, 28), 23, TextAnchor.MiddleLeft, "", Ink, FontStyle.Normal);
            cupClass[index] = Label(rt, "Classe", new Vector2(0f, 12f), new Vector2(344, 24), 15, TextAnchor.MiddleLeft, "", Dim, FontStyle.Normal);
            cupState[index] = Label(rt, "Stagione", new Vector2(0f, -14f), new Vector2(344, 24), 17, TextAnchor.MiddleLeft, "", Ink, FontStyle.Normal);
            cupTrophy[index] = Label(rt, "Premio", new Vector2(0f, -40f), new Vector2(344, 24), 16, TextAnchor.MiddleLeft, "", Dim, FontStyle.Normal);
            AddFeedback(button, null);
            rt.gameObject.AddComponent<LicenceCellFocus>().focus = () => CupFocus(index);          // the same hook as the licences: the cell chosen, or under the pointer
            return button;
        }

        /// <summary>The bike of the cup: the one of the preparation if it is of the class, else the first of the class (what PistaMode.BeginCup takes).</summary>
        static int CupBikeOf(int index)
        {
            var cup = Cups.All[index];
            int bike = PistaMode.CupBike(cup);
            return bike >= 0 ? bike : Cups.BikesOf(cup)[0];
        }

        /// <summary>The cup to have chosen when the panel opens: the season in progress, else the first cup never finished, else the first.</summary>
        static int CupPick()
        {
            var book = PistaCups.Book;
            for (int i = 0; i < Cups.Count; i++) { var s = book.Season(i); if (s != null && s.Started && !s.Finished) return i; }
            for (int i = 0; i < Cups.Count; i++) if (book.SeasonsDone(i) == 0) return i;
            return 0;
        }

        /// <summary>The state of every cup, the trophies and the summary; then the detail of the cup chosen.</summary>
        void RefreshCup()
        {
            var book = PistaCups.Book;
            for (int i = 0; i < Cups.Count; i++)
            {
                var cup = Cups.All[i];
                var season = book.Season(i);
                cupName[i].text = cup.name;
                cupClass[i].text = cup.klass + "  -  " + cup.Rounds + " gare da " + cup.laps + " giri";
                cupState[i].text = season == null || !season.Started ? "Stagione da cominciare"
                    : season.Finished ? "Finita  -  " + season.Position(0) + "° con " + season.points[0] + " punti"
                    : "Gara " + (season.round + 1) + " di " + cup.Rounds + "  -  " + season.Position(0) + "° con " + season.points[0] + " punti";
                Medal best = book.BestTrophy(i);
                cupTrophy[i].text = best != Medal.None ? "Migliore: " + PistaDirector.TrophyName(best) : "Nessuna coppa vinta";
                cupTrophy[i].color = PistaDirector.LicenceColor(best) ?? Dim;
            }
            cupSummary.text = "Punti " + string.Join(" ", Cups.PointsTable) + "   -   Coppe: oro " + book.Trophies(Medal.Gold) + "  argento " + (book.Trophies(Medal.Silver) - book.Trophies(Medal.Gold))
                + "  bronzo " + (book.Trophies(Medal.Bronze) - book.Trophies(Medal.Silver));
            CupDetail(cupFocused);
        }

        /// <summary>A cup is chosen (keyboard, pad) or the pointer is over it: the detail below follows.</summary>
        void CupFocus(int index)
        {
            if (cupPanel == null || index < 0 || index >= Cups.Count) return;
            if (index != cupFocused) CupDisarm();
            cupFocused = index;
            CupDetail(index);
        }

        /// <summary>Under the cups: the calendar, the standings (the riders of the field before the first round), what the bike and the level are and what Invio does.</summary>
        void CupDetail(int index)
        {
            var cup = Cups.All[index];
            var season = PistaCups.Book.Season(index);
            bool started = season != null && season.Started, finished = season != null && season.Finished;
            cupSubtitle.text = cup.tagline;

            cupCalendarHead.text = "Calendario  -  " + cup.klass;
            int next = season != null ? season.NextRound : 0;
            for (int r = 0; r < CupMaxRounds; r++)
            {
                if (r >= cup.Rounds) { cupRoundName[r].text = ""; cupRoundMark[r].text = ""; continue; }
                int named = cup.circuits[r];
                cupRoundName[r].text = (r + 1) + "  " + PistaCircuitName(named) + "  -  " + (Catalogue.All[named].length / 1000f).ToString("0.0", PistaIt) + " km";
                bool done = started && r < season.round, upNext = !finished && r == next;
                cupRoundMark[r].text = done ? "fatta" : upNext ? "prossima" : "";
                cupRoundMark[r].color = upNext ? CupAmber : Dim;
            }

            string you = PistaDirector.CupPlayerName();
            int bike = CupBikeOf(index);
            var field = CupField.Make(cup, season != null ? season.rank : PistaMode.Rank, season != null ? season.freeSkin : PistaMode.SkinFor(bike), PistaDirector.Names, Livery.Count);
            if (started)
            {
                cupTableHead.text = "Classifica dopo " + season.round + (season.round == 1 ? " gara" : " gare") + (finished ? "  -  stagione finita" : "");
                var order = season.Order();
                for (int i = 0; i < Cups.Riders; i++)
                {
                    int rider = order[i];
                    cupRowName[i].text = (i + 1) + "  " + (rider == 0 ? you : field[rider - 1].name);
                    cupRowValue[i].text = season.points[rider] + " pt";
                    cupRowName[i].color = cupRowValue[i].color = rider == 0 ? CupAmber : i < 3 ? Ink : Dim;
                }
            }
            else
            {
                cupTableHead.text = "Gli avversari  -  undici, gli stessi per tutta la stagione";
                for (int i = 0; i < Cups.Riders; i++)
                {
                    bool mine = i == Cups.Riders - 1;
                    cupRowName[i].text = mine ? you : field[i].name;
                    cupRowValue[i].text = Bikes.Get(mine ? bike : field[i].bike).name;
                    cupRowName[i].color = mine ? CupAmber : Ink;
                    cupRowValue[i].color = mine ? CupAmber : Dim;
                }
            }

            bool frozen = started && !finished;
            bool available = PistaMode.CupAvailable(cup);
            cupNote.text = (available ? "In sella: " + Bikes.Get(bike).name + (Cups.Allows(cup, PistaMode.Bike) ? "" : "  -  la tua moto non e' di questa classe")
                : "Serve una moto della classe " + cup.klass + ": comprane una nel garage (Carriera)") + "\n"
                + "Avversari: livello " + PistaMode.RankNames[season != null && !finished ? season.rank : PistaMode.Rank].ToLowerInvariant()
                + (frozen ? ", fissato all'inizio della stagione" : ", come nella preparazione");
            int round = season != null && !finished ? season.round : 0;
            cupEnter.text = !available ? "Non si parte: nella carriera ti serve una moto di questa classe"
                : (finished ? "Invio: nuova stagione, " : "Invio: ") + "gara " + (round + 1) + " di " + cup.Rounds + " a " + PistaCircuitName(cup.circuits[round]) + "  -  " + cup.laps + " giri";

            cupResetButton.gameObject.SetActive(frozen);
            CupWire();
        }

        /// <summary>The cross: left and right from cup to cup, down to "Ricomincia" (when the season has points to throw away) and "Indietro".</summary>
        void CupWire()
        {
            bool reset = cupResetButton.gameObject.activeSelf;
            Selectable down = reset ? (Selectable)cupResetButton : cupBackButton;
            for (int i = 0; i < cupCells.Length; i++)
                cupCells[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? cupCells[i - 1] : null,
                    selectOnRight = i < cupCells.Length - 1 ? cupCells[i + 1] : null,
                    selectOnDown = down,
                };
            var cell = cupCells[Mathf.Clamp(cupFocused, 0, cupCells.Length - 1)];
            cupResetButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = cell, selectOnDown = cupBackButton, selectOnRight = cupBackButton };
            cupBackButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = reset ? (Selectable)cupResetButton : cell, selectOnLeft = reset ? cupResetButton : null };
        }

        /// <summary>"Campionato" dalla preparazione: e' gia' scelta la coppa in corso (o la prima mai finita), cosi' Invio parte da dove si e' rimasti.</summary>
        void CupOpen()
        {
            int pick = CupPick();
            cupFocused = pick;
            CupDisarm();
            RefreshCup();
            Show(cupPanel);                 // it selects the first cup, and the cup's callback moves the detail there: the pick is made again below
            var system = EventSystem.current;
            if (system != null) system.SetSelectedGameObject(cupCells[pick].gameObject);
            CupFocus(pick);
        }

        /// <summary>Una coppa scelta: la partita parte con la gara successiva della stagione (una stagione nuova, al livello e nei colori di adesso, se non ce n'e' una o e' finita). La moto, gli aiuti e l'ora sono quelli
        /// della preparazione; i giri, gli avversari e il circuito sono quelli della coppa (PistaMode.BeginCup), da soli.</summary>
        void CupStart(int index)
        {
            modeForCoop = false;
            PistaMode.Requested = true;
            PistaMode.BeginCup(index);
            Play();
        }

        /// <summary>"Ricomincia": la stagione della coppa scelta si butta (i punti e le gare fatte) e ne comincia un'altra col livello di adesso. Chiede una seconda pressione.</summary>
        void CupReset()
        {
            var season = PistaCups.Book.Season(cupFocused);
            if (season == null || !season.Started || season.Finished) return;
            if (!cupResetArmed)
            {
                cupResetArmed = true;
                cupResetLabel.text = "Sicuro? Perdi i punti";
                return;
            }
            CupDisarm();
            PistaCups.Begin(cupFocused, PistaMode.Rank, PistaMode.SkinFor(CupBikeOf(cupFocused)));
            RefreshCup();
            var system = EventSystem.current;
            if (system != null) system.SetSelectedGameObject(cupCells[cupFocused].gameObject);          // the button is gone: back to the cup
        }

        void CupDisarm()
        {
            cupResetArmed = false;
            if (cupResetLabel != null) cupResetLabel.text = "Ricomincia";
        }

        void CupShow(RectTransform p)
        {
            if (cupPanel != null) cupPanel.gameObject.SetActive(p == cupPanel);
        }

        /// <summary>Indietro dal campionato: alla preparazione, con il pulsante "Campionato" scelto.</summary>
        void CupBack()
        {
            CupDisarm();
            Show(pistaPanel);
            var system = EventSystem.current;
            if (system != null && cupOpenButton != null) system.SetSelectedGameObject(cupOpenButton.gameObject);
        }

        /// <summary>Esc o B dal campionato. Vero se ne ha gestito uno.</summary>
        bool CupBackKey()
        {
            if (cupPanel == null || !cupPanel.gameObject.activeSelf) return false;
            MenuSound.Back();
            CupBack();
            return true;
        }
    }
}

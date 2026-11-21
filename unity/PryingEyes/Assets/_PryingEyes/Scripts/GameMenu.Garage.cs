using System;
using PryingEyes.Pista;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Il garage (Scripts/Pista/BikeSheet.cs, GarageStage.cs): si apre dal pulsante "Garage" della preparazione della gara. Una vetrina con la moto e il suo pilota che girano su un piatto, la scelta della moto e
    /// dei colori, della tuta e del casco del pilota e sette barre con le cifre che la moto fa davvero sul modello di guida (potenza, coppia, velocita' massima, ripresa, frenata, peso, maneggevolezza). Quello che si sceglie qui e' quello che si corre: e' la stessa
    /// scelta della riga "Moto" della preparazione.</summary>
    public partial class GameMenu
    {
        RectTransform garagePanel;
        Button garageOpenButton, garageBackButton;
        MenuChoice garageBikeRow, garageColourRow, garageOutfitRow;
        RawImage garagePicture;
        Text garageName, garageBlurb;
        Image garageSwatchMain, garageSwatchAccent, garageSwatchSuit, garageSwatchSuit2, garageSwatchHelmet;
        readonly Text[] garageBarValue = new Text[BikeSheet.BarCount];
        readonly Image[] garageBarFill = new Image[BikeSheet.BarCount];
        GarageStage garageStage;
        // the career: the button that turns it on and shows the money, the button that buys the bike on the plate and the line that says how it went
        Button garageCareerButton, garageBuyButton, garageTuneButton;
        Text garageCareerLabel, garageBuyLabel, garageTuneLabel, garageNote;
        /// <summary>The bike on the plate. Without the career it is the bike chosen (PistaMode.Bike); in a career the plate shows every bike of the roster, owned or not, and PistaMode.Bike follows it only while it is owned.</summary>
        int garageView;

        const float GarageBarW = 250f, GarageBarH = 12f, GarageBarX = 330f;

        void BuildGarage()
        {
            garagePanel = Stretch(backdrop, "Garage");
            Label(garagePanel, "Titolo", new Vector2(0, 262), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Garage", Ink, FontStyle.Normal);
            Label(garagePanel, "Sottotitolo", new Vector2(0, 214), new Vector2(1180, 30), 19, TextAnchor.MiddleCenter,
                "Guarda la moto girare, scegli quale correre e con che colori. Le cifre sono quelle che la moto fa davvero sul modello di guida.", Dim, FontStyle.Italic);

            // la vetrina
            var frame = Rect(garagePanel, "Cornice vetrina", new Vector2(560, 372), new Vector2(-318, 0));
            var frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.color = new Color(0.03f, 0.05f, 0.05f, 0.96f); frameImage.raycastTarget = false;
            var edge = frame.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.64f, 0.49f, 0.28f, 0.50f); edge.effectDistance = new Vector2(1f, -1f);
            garagePicture = Rect(frame, "Vetrina", new Vector2(546, 364), Vector2.zero).gameObject.AddComponent<RawImage>();
            garagePicture.raycastTarget = false;

            garageBikeRow = GarageRow("Moto", 170f, () => Bikes.Get(garageView).name, GarageBikeStep);
            garageColourRow = GarageRow("Colori", 128f, GarageColourName, d => PistaMode.Skin = Wrap(PistaMode.Skin + 1 + d, Livery.Count + 1) - 1);
            garageSwatchMain = Swatch("Colore principale", new Vector2(198f, 128f));
            garageSwatchAccent = Swatch("Colore secondario", new Vector2(226f, 128f));
            garageOutfitRow = GarageRow("Pilota", 86f, GarageOutfitName, d => PistaMode.Outfit = Wrap(PistaMode.Outfit + 1 + d, Livery.Count + 1) - 1);
            garageSwatchSuit = Swatch("Colore tuta", new Vector2(170f, 86f));
            garageSwatchSuit2 = Swatch("Colore tuta secondario", new Vector2(198f, 86f));
            garageSwatchHelmet = Swatch("Colore casco", new Vector2(226f, 86f));

            garageName = Label(garagePanel, "Nome moto", new Vector2(300, 40), new Vector2(620, 36), 26, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            garageBlurb = Label(garagePanel, "Descrizione moto", new Vector2(300, -15), new Vector2(620, 66), 16, TextAnchor.UpperCenter, "", Dim, FontStyle.Italic);
            for (int i = 0; i < BikeSheet.BarCount; i++)
            {
                float y = -62f - i * 25f;
                Label(garagePanel, "Barra " + BikeSheet.BikeBarNames[i], new Vector2(105f, y), new Vector2(170, 24), 17, TextAnchor.MiddleLeft, BikeSheet.BikeBarNames[i], Dim, FontStyle.Normal);
                var track = Rect(garagePanel, "Binario " + BikeSheet.BikeBarNames[i], new Vector2(GarageBarW, GarageBarH), new Vector2(GarageBarX, y)).gameObject.AddComponent<Image>();
                track.color = new Color(0f, 0f, 0f, 0.55f); track.raycastTarget = false;
                var fill = Rect(garagePanel, "Riempimento " + BikeSheet.BikeBarNames[i], new Vector2(GarageBarW, GarageBarH), new Vector2(GarageBarX, y)).gameObject.AddComponent<Image>();
                fill.color = new Color(0.78f, 0.62f, 0.36f); fill.raycastTarget = false;
                garageBarFill[i] = fill;
                garageBarValue[i] = Label(garagePanel, "Cifra " + BikeSheet.BikeBarNames[i], new Vector2(548f, y), new Vector2(150, 24), 17, TextAnchor.MiddleLeft, "", Ink, FontStyle.Normal);
            }

            garageBackButton = MakeButton(garagePanel, "Fatto", new Vector2(0, -262), GarageBack);
            AddFeedback(garageBackButton, null);
            // the career: "Carriera" turns it on and off (and says the money), "Compra" buys the bike on the plate when it is not owned, the line above it says how it went
            garageCareerButton = MakeButton(garagePanel, "Carriera", new Vector2(468, 262), GarageCareerToggle);
            ResizeButton(garageCareerButton, new Vector2(270, 42), 20);
            garageCareerLabel = garageCareerButton.GetComponentInChildren<Text>();
            AddFeedback(garageCareerButton, null);
            garageBuyButton = MakeButton(garagePanel, "Compra", new Vector2(-325, -262), GarageBuy);
            ResizeButton(garageBuyButton, new Vector2(260, 40), 19);
            garageBuyLabel = garageBuyButton.GetComponentInChildren<Text>();
            AddFeedback(garageBuyButton, null);
            // "Potenzia" takes the place of "Compra" for a bike that is owned: one level of tuning at a time (Tuning.cs), for the price of the workshop
            garageTuneButton = MakeButton(garagePanel, "Potenzia", new Vector2(-325, -262), GarageTune);
            ResizeButton(garageTuneButton, new Vector2(260, 40), 19);
            garageTuneLabel = garageTuneButton.GetComponentInChildren<Text>();
            AddFeedback(garageTuneButton, null);
            garageNote = Label(garagePanel, "Nota carriera", new Vector2(-318, -214), new Vector2(560, 24), 16, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            GarageWire();
            garagePanel.gameObject.AddComponent<MenuPanelFade>();
            garagePanel.gameObject.AddComponent<GarageTick>().menu = this;
        }

        /// <summary>Una riga a scelta (‹ valore ›) del garage, nella colonna di destra.</summary>
        MenuChoice GarageRow(string title, float y, Func<string> text, Action<int> step)
        {
            var rowLabel = Label(garagePanel, title, new Vector2(120f, y), new Vector2(200, 30), 20, TextAnchor.MiddleLeft, title, Ink, FontStyle.Normal);
            var rt = Rect(garagePanel, "Scelta " + title, new Vector2(300f, 36f), new Vector2(400f, y));
            var plate = rt.gameObject.AddComponent<Image>();
            plate.color = BtnColor;
            var edge = rt.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.64f, 0.49f, 0.28f, 0.50f); edge.effectDistance = new Vector2(1f, -1f);
            var choice = rt.gameObject.AddComponent<MenuChoice>();
            choice.targetGraphic = plate;
            choice.colors = ButtonColors();
            choice.rowLabel = rowLabel;
            choice.textOn = Ink;
            choice.valueLabel = Label(rt, title + " valore", Vector2.zero, new Vector2(200f, 32), 19, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            Label(rt, "Freccia sinistra", new Vector2(-124f, 0), new Vector2(40, 32), 26, TextAnchor.MiddleCenter, "‹", Brass, FontStyle.Normal);
            Label(rt, "Freccia destra", new Vector2(124f, 0), new Vector2(40, 32), 26, TextAnchor.MiddleCenter, "›", Brass, FontStyle.Normal);
            choice.onStep = d => { step(d); RefreshGarage(); };
            AddFeedback(choice, null);
            return choice;
        }

        Image Swatch(string name, Vector2 pos)
        {
            var img = Rect(garagePanel, name, new Vector2(22, 22), pos).gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            var edge = img.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.64f, 0.49f, 0.28f, 0.60f); edge.effectDistance = new Vector2(1f, -1f);
            return img;
        }

        static string GarageColourName()
        {
            int skin = PistaMode.Skin;
            return skin < 0 ? "Di serie" : Livery.For(skin).name;
        }

        /// <summary>La tuta e il casco del pilota: quelli della moto, o quelli di una delle dodici livree.</summary>
        static string GarageOutfitName()
        {
            int outfit = PistaMode.Outfit;
            return outfit < 0 ? "Come la moto" : Livery.For(outfit).name;
        }

        /// <summary>I valori delle righe, il nome e la frase della moto, le barre con le loro cifre e la moto sul piatto.</summary>
        void RefreshGarage()
        {
            if (garagePanel == null) return;
            if (!PistaMode.CareerApplies) garageView = PistaMode.Bike;     // without the career the plate shows the bike chosen, whoever changed it
            int index = garageView, skin = PistaMode.Skin, outfit = PistaMode.Outfit;
            bool career = PistaMode.CareerApplies;
            var book = PistaCareer.Book;
            bool owned = !career || book.Owns(index);
            int level = career && owned ? book.Level(index) : 0;          // the tuning of a bike that is owned (a bike that is not has none to show)
            var spec = Bikes.Get(index);
            garageBikeRow.Value = spec.name;
            garageColourRow.Value = GarageColourName();
            garageOutfitRow.Value = GarageOutfitName();
            var liv = Livery.For(skin >= 0 ? skin : spec.skin);
            garageSwatchMain.color = liv.main; garageSwatchAccent.color = liv.accent;
            var rider = outfit >= 0 ? Livery.For(outfit) : liv;
            garageSwatchSuit.color = rider.suit; garageSwatchSuit2.color = rider.suit2; garageSwatchHelmet.color = rider.helmet;
            garageName.text = spec.name + "  -  " + spec.klass + (!career ? "" : owned ? "  -  tua" + (level > 0 ? "  -  " + Tuning.Label(level) : "") : "  -  " + Economy.Format(Economy.Price(index)) + " cr");
            garageBlurb.text = BikeSheet.Blurb(index) + (level > 0 ? "\nMessa a punto " + level + " di " + Tuning.MaxLevel + ":   " + Tuning.Describe(level) : "");

            var f = BikeSheet.Of(index, level);                          // the bars and the figures are those of the bike as it is tuned
            for (int i = 0; i < BikeSheet.BarCount; i++)
            {
                float v = BikeSheet.Value(f, (BikeBar)i);
                var fill = garageBarFill[i].rectTransform;
                fill.sizeDelta = new Vector2(Mathf.Max(2f, GarageBarW * v), GarageBarH);
                fill.anchoredPosition = new Vector2(GarageBarX - GarageBarW * 0.5f + fill.sizeDelta.x * 0.5f, fill.anchoredPosition.y);
            }
            garageBarValue[(int)BikeBar.Power].text = Mathf.RoundToInt(f.hp / 5f) * 5 + " CV";
            garageBarValue[(int)BikeBar.Torque].text = Mathf.RoundToInt(f.torque) + " Nm";
            garageBarValue[(int)BikeBar.Speed].text = Mathf.RoundToInt(f.topKmh) + " km/h";
            garageBarValue[(int)BikeBar.Pickup].text = f.t100.ToString("0.0", PistaIt) + " s  0-100";
            garageBarValue[(int)BikeBar.Brakes].text = f.brake100.ToString("0.0", PistaIt) + " m  100-0";
            garageBarValue[(int)BikeBar.Lightness].text = Mathf.RoundToInt(f.kg) + " kg";
            garageBarValue[(int)BikeBar.Handling].text = BikeSheet.HandlingWord(f);
            // the career: the money on its button, and "Compra" only for a bike that is not owned (grey when there is not enough money: it still answers, with what is missing)
            garageCareerLabel.text = career ? "Carriera: " + Economy.Format(book.Money) + " cr" : "Carriera: spenta";
            garageCareerButton.gameObject.SetActive(!PistaMode.ForCoop);          // a cooperative game has no career: the host picks any bike for the table
            garageBuyButton.gameObject.SetActive(career && !owned);
            if (career && !owned)
            {
                garageBuyLabel.text = "Compra  -  " + Economy.Format(Economy.Price(index)) + " cr";
                garageBuyLabel.color = book.Missing(index) > 0 ? Dim : Ink;
            }
            // the workshop: "Potenzia" for a bike that is owned and not yet at the top, with the price of the next level; when nothing else is said the line above it says what that level gives
            bool tunable = career && owned && level < Tuning.MaxLevel;
            garageTuneButton.gameObject.SetActive(tunable);
            if (tunable)
            {
                garageTuneLabel.text = "Potenzia  -  " + Economy.Format(Economy.TunePrice(index, level + 1)) + " cr";
                garageTuneLabel.color = book.TuneMissing(index) > 0 ? Dim : Ink;
                if (string.IsNullOrEmpty(garageNote.text)) garageNote.text = "Livello " + (level + 1) + ":   " + Tuning.Describe(level + 1);
            }
            GarageWire();
            if (garageStage != null) garageStage.Show(index, skin, outfit);
        }

        /// <summary>The row "Moto": the plate goes to the next bike of the roster; the bike chosen follows it if it is owned (always, without the career).</summary>
        void GarageBikeStep(int d)
        {
            garageNote.text = "";
            garageView = Wrap(garageView + d, Bikes.Count);
            if (!PistaMode.CareerApplies || PistaCareer.Book.Owns(garageView)) PistaMode.Bike = garageView;
        }

        /// <summary>"Carriera": on or off. The bike chosen is read again (in a career it is one that is owned).</summary>
        void GarageCareerToggle()
        {
            MenuSound.Confirm();
            PistaMode.CareerOn = !PistaMode.CareerOn;
            garageView = PistaMode.Bike;
            garageNote.text = PistaMode.CareerOn ? "Carriera accesa: premi sul conto, moto da comprare" : "Carriera spenta: tutte le moto sono tue";
            RefreshGarage();
        }

        /// <summary>"Compra": the bike on the plate, if there is the money. The answer is on the line above the button; after a purchase the cross goes back to the row of the bike (the button is gone).</summary>
        void GarageBuy()
        {
            int bike = garageView;
            var book = PistaCareer.Book;
            if (!PistaMode.CareerApplies || book.Owns(bike)) return;
            string name = Bikes.Get(bike).name;
            if (PistaCareer.Buy(bike))
            {
                MenuSound.Confirm();
                PistaMode.Bike = bike;
                garageNote.text = "Comprata: " + name + "  -  conto " + Economy.Format(book.Money) + " cr";
                RefreshGarage();
                var system = EventSystem.current;
                if (system != null) system.SetSelectedGameObject(garageBikeRow.gameObject);
            }
            else
            {
                MenuSound.Back();
                garageNote.text = "Ti mancano " + Economy.Format(book.Missing(bike)) + " cr per la " + name;
            }
        }

        /// <summary>"Potenzia": the bike on the plate goes up one level of tuning, if there is the money. The answer is on the line above the button; at the top level the button is gone and the cross goes back to the row of the bike.</summary>
        void GarageTune()
        {
            int bike = garageView;
            var book = PistaCareer.Book;
            if (!PistaMode.CareerApplies || !book.Owns(bike) || book.Level(bike) >= Tuning.MaxLevel) return;
            string name = Bikes.Get(bike).name;
            int next = book.Level(bike) + 1;
            if (PistaCareer.Tune(bike))
            {
                MenuSound.Confirm();
                garageNote.text = "Potenziata: " + name + ", " + Tuning.Label(next) + "  -  conto " + Economy.Format(book.Money) + " cr";
                RefreshGarage();
                var system = EventSystem.current;
                if (system != null && !garageTuneButton.gameObject.activeSelf) system.SetSelectedGameObject(garageBikeRow.gameObject);
            }
            else
            {
                MenuSound.Back();
                garageNote.text = "Ti mancano " + Economy.Format(book.TuneMissing(bike)) + " cr per il " + Tuning.Label(next) + " della " + name;
            }
        }

        /// <summary>The level of tuning a bike has in a race now, as a tag for a line of text (" liv. 2"), or nothing.</summary>
        static string TuneTag(int bike) { int l = PistaMode.TuneOf(bike); return l > 0 ? " liv. " + l : ""; }

        /// <summary>The cross of the garage: from the career button down through the three rows to the button of the career under the plate ("Compra" or "Potenzia", only while there is something to buy or tune) and "Fatto".</summary>
        void GarageWire()
        {
            if (garageBuyButton == null) return;
            Button deal = garageBuyButton.gameObject.activeSelf ? garageBuyButton : garageTuneButton.gameObject.activeSelf ? garageTuneButton : null;
            bool buy = deal != null;
            garageCareerButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = garageBikeRow };
            garageBikeRow.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = garageCareerButton.gameObject.activeSelf ? (Selectable)garageCareerButton : null, selectOnDown = garageColourRow };
            garageColourRow.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = garageBikeRow, selectOnDown = garageOutfitRow };
            garageOutfitRow.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = garageColourRow, selectOnDown = buy ? (Selectable)deal : garageBackButton };
            garageBuyButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = garageOutfitRow, selectOnDown = garageBackButton, selectOnRight = garageBackButton };
            garageTuneButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = garageOutfitRow, selectOnDown = garageBackButton, selectOnRight = garageBackButton };
            garageBackButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = buy ? (Selectable)deal : garageOutfitRow, selectOnLeft = buy ? deal : null };
        }

        /// <summary>"Garage" dalla preparazione: la vetrina si accende sulla moto scelta e la riga della moto e' gia' scelta.</summary>
        void GarageOpen()
        {
            if (garageStage == null)
            {
                garageStage = GarageStage.Create();
                garagePicture.texture = garageStage.Picture;
            }
            garageView = PistaMode.Bike;
            garageNote.text = "";
            RefreshGarage();
            Show(garagePanel);
            var system = EventSystem.current;
            if (system != null) system.SetSelectedGameObject(garageBikeRow.gameObject);
        }

        void GarageShow(RectTransform p)
        {
            if (garagePanel != null) garagePanel.gameObject.SetActive(p == garagePanel);
        }

        /// <summary>Fatto (Esc, B) dal garage: alla preparazione, con la moto e i colori che si sono scelti.</summary>
        void GarageBack()
        {
            Show(pistaPanel);
            RefreshPista(false);
            var system = EventSystem.current;
            if (system != null && garageOpenButton != null) system.SetSelectedGameObject(garageOpenButton.gameObject);
        }

        bool GarageBackKey()
        {
            if (garagePanel == null || !garagePanel.gameObject.activeSelf) return false;
            MenuSound.Back();
            GarageBack();
            return true;
        }

        /// <summary>Per le prove: la vetrina e i testi del garage.</summary>
        internal GarageStage GarageStageForTest => garageStage;
    }

    /// <summary>Il garage e' aperto: la vetrina gira e la sua camera disegna; chiuso, la vetrina si spegne.</summary>
    public sealed class GarageTick : MonoBehaviour
    {
        public GameMenu menu;
        void OnEnable() { var s = menu != null ? menu.GarageStageForTest : null; if (s != null) s.SetActive(true); }
        void OnDisable() { var s = menu != null ? menu.GarageStageForTest : null; if (s != null) s.SetActive(false); }
        void Update() { var s = menu != null ? menu.GarageStageForTest : null; if (s != null) s.Tick(Time.unscaledDeltaTime); }
    }
}

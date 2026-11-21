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

            garageBikeRow = GarageRow("Moto", 170f, () => Bikes.Get(PistaMode.Bike).name, d => PistaMode.Bike = Wrap(PistaMode.Bike + d, Bikes.Count));
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
            garageBikeRow.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = garageColourRow };
            garageColourRow.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = garageBikeRow, selectOnDown = garageOutfitRow };
            garageOutfitRow.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = garageColourRow, selectOnDown = garageBackButton };
            garageBackButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = garageOutfitRow };
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
            int index = PistaMode.Bike, skin = PistaMode.Skin, outfit = PistaMode.Outfit;
            var spec = Bikes.Get(index);
            garageBikeRow.Value = spec.name;
            garageColourRow.Value = GarageColourName();
            garageOutfitRow.Value = GarageOutfitName();
            var liv = Livery.For(skin >= 0 ? skin : spec.skin);
            garageSwatchMain.color = liv.main; garageSwatchAccent.color = liv.accent;
            var rider = outfit >= 0 ? Livery.For(outfit) : liv;
            garageSwatchSuit.color = rider.suit; garageSwatchSuit2.color = rider.suit2; garageSwatchHelmet.color = rider.helmet;
            garageName.text = spec.name + "  -  " + spec.klass;
            garageBlurb.text = BikeSheet.Blurb(index);

            var f = BikeSheet.Of(index);
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
            if (garageStage != null) garageStage.Show(index, skin, outfit);
        }

        /// <summary>"Garage" dalla preparazione: la vetrina si accende sulla moto scelta e la riga della moto e' gia' scelta.</summary>
        void GarageOpen()
        {
            if (garageStage == null)
            {
                garageStage = GarageStage.Create();
                garagePicture.texture = garageStage.Picture;
            }
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

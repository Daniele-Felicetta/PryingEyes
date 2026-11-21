using PryingEyes.Pista;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>Le patenti (Scripts/Pista/Licences.cs): si aprono dal pulsante "Patenti" della preparazione della gara. Nove prove in tre patenti (A1, A2, A), una moto e un circuito per prova, tre giri da soli:
    /// conta il giro lanciato migliore senza cadute. Ogni prova e' un pulsante con la sua medaglia e il suo tempo migliore (il registro, PistaLicences, e' nelle preferenze del giocatore); Invio o un clic
    /// avviano la prova, la croce passa da una all'altra e sotto si legge cosa chiede (il circuito, i tempi di oro, argento e bronzo, gli aiuti e l'ora). Una patente e' ottenuta con una medaglia su ognuna delle tre prove.</summary>
    public partial class GameMenu
    {
        RectTransform licencePanel;
        Button licenceOpenButton, licenceBackButton;
        readonly Button[] licenceCells = new Button[Licences.Count];
        readonly Text[] licenceName = new Text[Licences.Count], licenceBike = new Text[Licences.Count], licenceMedal = new Text[Licences.Count], licenceTime = new Text[Licences.Count];
        readonly Text[] licenceHead = new Text[Licences.Names.Length], licenceState = new Text[Licences.Names.Length];
        Text licenceDetail1, licenceDetail2, licenceSummary;
        int licenceFocused;

        const float LicenceCellW = 376f, LicenceCellH = 64f, LicenceGap = 12f;

        void BuildLicence()
        {
            licencePanel = Stretch(backdrop, "Patenti");
            Label(licencePanel, "Titolo", new Vector2(0, 262), new Vector2(1000, 70), 48, TextAnchor.MiddleCenter, "Patenti", Ink, FontStyle.Normal);
            Label(licencePanel, "Sottotitolo", new Vector2(0, 214), new Vector2(1180, 30), 19, TextAnchor.MiddleCenter,
                "Tre giri da soli: conta il giro lanciato piu' veloce, senza cadute. Una medaglia su ogni prova e la patente e' tua.", Dim, FontStyle.Italic);
            licenceSummary = Label(licencePanel, "Riepilogo", new Vector2(0, 190), new Vector2(1180, 24), 17, TextAnchor.MiddleCenter, "", Brass, FontStyle.Normal);

            for (int l = 0; l < Licences.Names.Length; l++)
            {
                float headY = 150f - l * 108f, cellY = 102f - l * 108f;
                licenceHead[l] = Label(licencePanel, "Intestazione " + (l + 1), new Vector2(-580f + 90f, headY), new Vector2(180, 30), 24, TextAnchor.MiddleLeft, Licences.Names[l], Ink, FontStyle.Normal);
                Label(licencePanel, "Descrizione " + (l + 1), new Vector2(-400f + 390f, headY), new Vector2(780, 28), 16, TextAnchor.MiddleLeft, Licences.Taglines[l], Dim, FontStyle.Italic);
                licenceState[l] = Label(licencePanel, "Stato " + (l + 1), new Vector2(580f - 100f, headY), new Vector2(200, 28), 18, TextAnchor.MiddleRight, "", Dim, FontStyle.Normal);
                for (int k = 0; k < Licences.PerLicence; k++)
                {
                    int i = l * Licences.PerLicence + k;
                    licenceCells[i] = MakeLicenceCell(i, k, new Vector2((k - 1) * (LicenceCellW + LicenceGap), cellY));
                }
            }
            licenceDetail1 = Label(licencePanel, "Dettaglio", new Vector2(0, -180), new Vector2(1180, 28), 19, TextAnchor.MiddleCenter, "", Ink, FontStyle.Normal);
            licenceDetail2 = Label(licencePanel, "Dettaglio tempi", new Vector2(0, -208), new Vector2(1180, 26), 16, TextAnchor.MiddleCenter, "", Dim, FontStyle.Italic);
            licenceBackButton = MakeButton(licencePanel, "Indietro", new Vector2(0, -262), () => Show(pistaPanel));
            AddFeedback(licenceBackButton, null);

            // la croce: su e giu' passano da una patente all'altra (e all'"Indietro"), sinistra e destra da una prova all'altra della stessa patente
            for (int i = 0; i < Licences.Count; i++)
            {
                int l = i / Licences.PerLicence, k = i % Licences.PerLicence;
                var nav = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = l > 0 ? licenceCells[i - Licences.PerLicence] : null,
                    selectOnDown = l < Licences.Names.Length - 1 ? licenceCells[i + Licences.PerLicence] : licenceBackButton,
                    selectOnLeft = k > 0 ? licenceCells[i - 1] : null,
                    selectOnRight = k < Licences.PerLicence - 1 ? licenceCells[i + 1] : null,
                };
                licenceCells[i].navigation = nav;
            }
            licenceBackButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = licenceCells[Licences.Count - 2] };
            licencePanel.gameObject.AddComponent<MenuPanelFade>();
        }

        /// <summary>Una prova: un pulsante largo con il nome, la moto e, a destra, la medaglia e il tempo migliore.</summary>
        Button MakeLicenceCell(int index, int column, Vector2 pos)
        {
            var rt = Rect(licencePanel, "Prova " + (index + 1), new Vector2(LicenceCellW, LicenceCellH), pos);
            var plate = rt.gameObject.AddComponent<Image>();
            plate.color = BtnColor;
            var edge = rt.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.64f, 0.49f, 0.28f, 0.50f); edge.effectDistance = new Vector2(1f, -1f);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            button.colors = ButtonColors();
            button.onClick.AddListener(() => { MenuSound.Confirm(); LicenceStart(index); });
            licenceName[index] = Label(rt, "Nome", new Vector2(-41f, 13f), new Vector2(270, 28), 21, TextAnchor.MiddleLeft, "", Ink, FontStyle.Normal);
            licenceBike[index] = Label(rt, "Moto", new Vector2(-41f, -13f), new Vector2(270, 24), 15, TextAnchor.MiddleLeft, "", Dim, FontStyle.Normal);
            licenceMedal[index] = Label(rt, "Medaglia", new Vector2(126f, 13f), new Vector2(100, 28), 20, TextAnchor.MiddleRight, "", Dim, FontStyle.Normal);
            licenceTime[index] = Label(rt, "Tempo", new Vector2(126f, -13f), new Vector2(100, 24), 15, TextAnchor.MiddleRight, "", Dim, FontStyle.Normal);
            AddFeedback(button, null);
            rt.gameObject.AddComponent<LicenceCellFocus>().focus = () => LicenceFocus(index);
            return button;
        }

        /// <summary>I nomi, le moto, le medaglie e i tempi di tutte le prove, lo stato di ogni patente e il riepilogo.</summary>
        void RefreshLicences()
        {
            var book = PistaLicences.Book;
            for (int i = 0; i < Licences.Count; i++)
            {
                var t = Licences.All[i];
                Medal medal = book.MedalOf(i);
                float best = book.BestOf(i);
                licenceName[i].text = (i % Licences.PerLicence + 1) + "  " + t.name;
                licenceBike[i].text = Bikes.Get(t.bike).name;
                licenceMedal[i].text = Licences.MedalName(medal);
                licenceMedal[i].color = Pista.PistaDirector.LicenceColor(medal) ?? Dim;
                licenceTime[i].text = best > 0f ? PistaHud.Clock(best) : "da fare";
            }
            for (int l = 0; l < Licences.Names.Length; l++)
            {
                int got = 0;
                for (int k = 0; k < Licences.PerLicence; k++) if (book.MedalOf(l * Licences.PerLicence + k) != Medal.None) got++;
                bool obtained = book.Obtained(l);
                licenceState[l].text = obtained ? "OTTENUTA" : got + " su " + Licences.PerLicence;
                licenceState[l].color = obtained ? new Color(1f, 0.74f, 0.18f) : Dim;
                licenceHead[l].color = obtained ? new Color(1f, 0.74f, 0.18f) : Ink;
            }
            int licences = 0;
            for (int l = 0; l < Licences.Names.Length; l++) if (book.Obtained(l)) licences++;
            licenceSummary.text = "Patenti " + licences + "/" + Licences.Names.Length + "   -   Oro " + book.Count(Medal.Gold) + "   Argento " + (book.Count(Medal.Silver) - book.Count(Medal.Gold)) + "   Bronzo " + (book.Count(Medal.Bronze) - book.Count(Medal.Silver));
        }

        /// <summary>Sotto le prove: cosa chiede quella scelta.</summary>
        void LicenceFocus(int index)
        {
            if (licenceDetail1 == null || index < 0 || index >= Licences.Count) return;
            licenceFocused = index;
            var t = Licences.All[index];
            licenceDetail1.text = t.brief;
            licenceDetail2.text = PistaCircuitName(t.circuit) + "  -  " + (Catalogue.All[t.circuit].length / 1000f).ToString("0.0", PistaIt) + " km   -   Oro " + PistaHud.Clock(t.gold) + "   Argento " + PistaHud.Clock(t.silver)
                + "   Bronzo " + PistaHud.Clock(t.bronze) + "   -   aiuti " + PistaMode.AidNames[t.aids].ToLowerInvariant() + ", " + PistaMode.HourNames[t.hour].ToLowerInvariant();
        }

        /// <summary>"Patenti" dalla preparazione: la prima prova ancora senza medaglia e' gia' scelta, cosi' Invio parte da dove si e' rimasti.</summary>
        void LicenceOpen()
        {
            RefreshLicences();
            Show(licencePanel);
            var book = PistaLicences.Book;
            int pick = 0;
            for (int i = 0; i < Licences.Count; i++) if (book.MedalOf(i) == Medal.None) { pick = i; break; }
            var system = EventSystem.current;
            if (system != null) system.SetSelectedGameObject(licenceCells[pick].gameObject);
            LicenceFocus(pick);
        }

        /// <summary>Una prova scelta: la partita parte con la moto, il circuito, gli aiuti, l'ora e i giri della prova (PistaMode.BeginLicence), da soli.</summary>
        void LicenceStart(int index)
        {
            modeForCoop = false;
            PistaMode.Requested = true;
            PistaMode.BeginLicence(index);
            Play();
        }

        void LicenceShow(RectTransform p)
        {
            if (licencePanel != null) licencePanel.gameObject.SetActive(p == licencePanel);
        }

        /// <summary>Indietro (Esc, B) dalle patenti: alla preparazione. Vero se ne ha gestito uno.</summary>
        bool LicenceBack()
        {
            if (licencePanel == null || !licencePanel.gameObject.activeSelf) return false;
            MenuSound.Back();
            Show(pistaPanel);
            return true;
        }
    }

    /// <summary>Su una prova delle patenti: quando e' scelta (tastiera, controller) o il puntatore ci passa sopra, il menu scrive cosa chiede.</summary>
    public sealed class LicenceCellFocus : MonoBehaviour, ISelectHandler, IPointerEnterHandler
    {
        public System.Action focus;
        public void OnSelect(BaseEventData eventData) { focus?.Invoke(); }
        public void OnPointerEnter(PointerEventData eventData) { focus?.Invoke(); }
    }
}

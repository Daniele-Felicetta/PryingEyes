using UnityEngine;

namespace PryingEyes
{
    public partial class PlayerController
    {
        // lo stato delle due scelte "a interruttore" (Impostazioni > Comandi): sono del giocatore, non della partita
        bool crouchToggled, sprintToggled;

        /// <summary>Corsa e abbassarsi con la tastiera: "tieni premuto" (com'e' sempre stato) oppure "a interruttore", un tocco accende e uno spegne.
        /// A interruttore si comporta come col controller (PadControls.Mapper): saltare rialza, correre rialza, la corsa si spegne da sola se ci
        /// si ferma, si prende la mira o si spara. Con un menu aperto la corsa a interruttore si spegne e l'abbassarsi resta com'era.</summary>
        void ToggleKeys(bool inputEnabled, float forward, ref bool crouchKey, ref bool sprintKey)
        {
            if (!inputEnabled) { sprintToggled = false; return; }
            if (GameSettings.CrouchToggle)
            {
                if (SeatInput.GetKeyDown(KeyCode.C) || SeatInput.GetKeyDown(KeyCode.LeftControl)) crouchToggled = !crouchToggled;
                if (SeatInput.GetKeyDown(KeyCode.Space)) crouchToggled = false;
                crouchKey = crouchToggled;
            }
            else crouchToggled = false;
            if (GameSettings.SprintToggle)
            {
                if (SeatInput.GetKeyDown(KeyCode.LeftShift) || SeatInput.GetKeyDown(KeyCode.RightShift))
                {
                    sprintToggled = !sprintToggled;
                    if (sprintToggled) crouchToggled = false;
                }
                if (forward <= 0f || SeatInput.GetMouseButtonDown(0) || SeatInput.GetMouseButtonDown(1)) sprintToggled = false;
                sprintKey = sprintToggled;
            }
            else sprintToggled = false;
        }
    }
}

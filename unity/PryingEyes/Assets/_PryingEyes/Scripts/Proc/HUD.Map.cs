using UnityEngine;

namespace PryingEyes
{
    /// <summary>La mappa nell'HUD di ogni giocatore: la minimappa in alto a destra e, con M, il foglio grande (Proc/Map/MapView.cs). Cosa si sa del
    /// piano e come si dipinge sta in Proc/Map/FloorMap.cs, ed e' di tutti; qui c'e' soltanto la vista di questo giocatore, che nello schermo
    /// condiviso ha la sua mappa nella sua porzione di schermo.</summary>
    public partial class HUD
    {
        Proc.MapView mapView;
        int mapFaults;

        void BuildMapUI()
        {
            // la mappa e' un di piu': se non si costruisce l'HUD deve restare in piedi lo stesso
            try { mapView = new Proc.MapView(this, transform); }
            catch (System.Exception e) { Debug.LogException(e); mapView = null; }
        }

        void UpdateMapUI()
        {
            if (mapView == null) return;
            // il tempo vero: la mappa si apre e si chiude anche durante i rallentatori
            try { mapView.Tick(Time.unscaledDeltaTime); }
            catch (System.Exception e)
            {
                // la mappa e' un di piu': se inciampa, il resto dell'HUD (che viene dopo di lei in Update) deve andare avanti. Il primo errore e'
                // nel log (e -maptest lo conta); alla terza volta la vista si spegne per sempre invece di intasare il log a ogni fotogramma.
                Debug.LogException(e);
                if (++mapFaults >= 3) { mapView.Hide(); mapView = null; }
            }
        }

        /// <summary>La vista della mappa di questo HUD (per le prove).</summary>
        public Proc.MapView Map => mapView;
    }
}

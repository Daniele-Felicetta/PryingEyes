using System.Collections;
using PryingEyes.Pista;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>La partita su pista (Scripts/Pista): scelta nel menu (o -pista), da soli o in cooperativa fino a quattro. Niente piani, niente stealth, niente ondate: il giocatore a piedi e il suo HUD si
    /// spengono per tutta la partita e la gara la porta PistaDirector, con la sua camera, il suo cielo e la sua interfaccia. Il menu di pausa e il ritorno al menu restano quelli di sempre
    /// (GameManager.ReturnToMenu ricarica la scena).</summary>
    public partial class FloorDirector
    {
        /// <summary>Il regista della gara in corso (null fuori dalla pista).</summary>
        public PistaDirector PistaRace { get; private set; }

        /// <summary>Questa partita e' su pista: scelto nel menu (o -pista), da soli o come ospite. Un ospitato non sceglie: parte dove gioca l'ospite (StartRun con coopPista).</summary>
        static bool PistaWanted => PistaMode.Requested && (!Coop.Active || Coop.IsHost);

        IEnumerator StartPistaCo(int seed)
        {
            transitioning = true;
            gm = GameManager.I;
            PistaMode.Begin(seed);
            Coop.OnFloorStart(0);          // cooperativa: l'ospite annuncia la partita (la prima epoca), l'ospitato si prepara a dire che ha costruito la sua
            // il giocatore a piedi sta fermo e invisibile (la sua camera, la sua arma e i suoi comandi non servono); l'HUD standard si spegne in GameMenu.Refresh
            if (gm != null && gm.player != null) gm.player.gameObject.SetActive(false);
            Debug.Log($"[PryingEyes] pista: partita con seme {seed}, {PistaMode.Laps} giri, {PistaMode.Rivals} avversari");
            PistaRace = PistaDirector.Create(seed);
            PistaRace.Begin();
            if (gm != null && gm.hud != null) { var c = gm.hud.GetComponent<Canvas>(); if (c != null) c.enabled = false; }
            yield return null;
            transitioning = false;
            // in cooperativa la pista e' "costruita" quando il regista ha finito (e non dopo il primo fotogramma): Coop.OnFloorBuilt lo chiama lui (PistaDirector.Setup.cs)
        }

        /// <summary>Chi esce (menu, fine della partita) lascia la pista: la scena si ricarica, ma lo stato statico va rimesso.</summary>
        void StopPista()
        {
            if (PistaRace != null) Destroy(PistaRace.gameObject);
            PistaRace = null;
            PistaMode.End();
        }
    }
}

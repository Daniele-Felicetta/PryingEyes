using UnityEngine;

namespace PryingEyes
{
    /// <summary>Sparare dalla moto. In sella l'arma sta riposta (nascosta e abbassata) finche' non serve: il giocatore la alza premendo fuoco, mira, ricarica o cambia arma (PlayerController.RideGunTick)
    /// e dopo un secondo e mezzo senza usarla torna giu'. Si tiene con la sola destra: la sinistra resta sul manubrio, quindi la mano libera dell'arma (guanto, manica e mano articolata) non si vede e
    /// la ricarica non usa la mano che prende il caricatore (l'arma si inclina e basta, come nelle prime ricariche). La mira con l'ottica e' bloccata e la dispersione cresce con la velocita' (fino a 1,9
    /// volte a 112 km/h). Il resto e' l'arma di sempre: cadenza, rinculo della visuale, rumore, munizioni, il cambio con 1, 2 e X. Il coltello, le magie, l'equipaggiamento e la mina restano spenti
    /// finche' si e' in sella (lo dicono Knife, MagicCaster ed Equipment, che guardano PlayerController.Riding).</summary>
    public partial class Weapon
    {
        bool rideMode, rideWantUp;
        float rideStow = 1f, rideSpeedK, ridePendingT;
        /// <summary>Per le prove: in sella spara da solo (il bot di -autotest, che spara a ogni nemico, in sella resta zitto come prima).</summary>
        public static bool rideBotFire;

        public bool RideMode => rideMode;
        /// <summary>0 arma in mano, 1 riposta (nascosta). Fuori dalla sella e' sempre 0.</summary>
        public float RideStow => rideMode ? rideStow : 0f;
        /// <summary>In sella con l'arma alzata: la destra ha lasciato il manubrio.</summary>
        public bool RideGunUp => rideMode && rideStow < 0.5f;
        /// <summary>Quante volte la dispersione di sempre vale adesso (1 a piedi e da fermi in sella).</summary>
        public float RideSpreadFactor => RideSpreadMult;
        /// <summary>Per le prove: la mano libera dell'arma si vede (il guanto, la manica o la mano articolata). In sella no.</summary>
        public bool LeftHandShown
        {
            get
            {
                if (handLRend != null) foreach (var r in handLRend) if (r != null && r.enabled) return true;
                return offHand != null && offHand.FreeVisible;
            }
        }

        /// <summary>Si sale in moto (arma riposta) o si scende (arma in mano come sempre).</summary>
        public void SetRideMode(bool on)
        {
            if (rideMode == on) return;
            rideMode = on;
            ridePendingT = 0f; rideWantUp = false; rideSpeedK = 0f;
            if (on)
            {
                rideStow = 1f;
                if (reloading) CancelReload();
                offHand?.Cancel();
                inspectT = 0f; adsRaw = 0f; ads = 0f; adsToggled = false; burstLeft = 0;
                if (flashA != null && flashA.activeSelf) { flashA.SetActive(false); flashB.SetActive(false); flashLight.intensity = 0f; }
            }
            else
            {
                rideStow = 0f;
                offHand?.RideHidden(false);
                if (viewRoot != null && !viewRoot.gameObject.activeSelf && knifeBlend < 0.999f) viewRoot.gameObject.SetActive(true);
            }
        }

        /// <summary>Ogni fotogramma in sella: l'arma deve essere in mano (up) e quanto corre la moto (0..1, allarga la dispersione).</summary>
        public void RideUp(bool up, float speedK) { rideWantUp = up; rideSpeedK = Mathf.Clamp01(speedK); }

        float RideSpreadMult => rideMode ? 1f + 0.9f * rideSpeedK : 1f;

        void RideStowTick(float dt)
        {
            if (!rideMode) { rideStow = 0f; return; }
            rideStow = Mathf.MoveTowards(rideStow, rideWantUp ? 0f : 1f, dt / (rideWantUp ? 0.12f : 0.22f));
        }

        /// <summary>Un clic arrivato con l'arma ancora giu' non si perde: parte appena e' su.</summary>
        bool RidePendingPress(bool press, float dt)
        {
            if (press) ridePendingT = 0.4f; else ridePendingT = Mathf.Max(0f, ridePendingT - dt);
            if (rideStow > 0.35f) return false;
            if (ridePendingT > 0f) { ridePendingT = 0f; return true; }
            return press;
        }

        /// <summary>La sinistra e' sul manubrio: la mano dell'arma non si vede. Dopo AnimateParts, che le riaccende secondo la ricarica.</summary>
        void RideHideLeftHand()
        {
            if (!rideMode) return;
            if (handLRend != null) foreach (var r in handLRend) if (r != null) r.enabled = false;
            if (wrappedL != null) foreach (var r in wrappedL) if (r != null) r.enabled = false;
            offHand?.RideHidden(true);
        }

        /// <summary>L'arma riposta e' abbassata e girata verso il fianco; quella tenuta con una mano sola sta un poco piu' bassa e a destra (il manubrio resta libero alla vista).</summary>
        void RidePose(ref Vector3 pos, ref Quaternion rot)
        {
            if (!rideMode) return;
            pos += new Vector3(0.045f, -0.03f, -0.03f);
            if (rideStow > 0f)
            {
                float k = Ease(rideStow);
                pos += new Vector3(0.03f, -0.20f, -0.06f) * k;
                rot *= Quaternion.Euler(32f * k, 0f, 11f * k);
            }
        }
    }
}

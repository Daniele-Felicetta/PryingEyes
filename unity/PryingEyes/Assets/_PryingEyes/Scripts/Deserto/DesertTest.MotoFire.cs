#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly motofuoco: sparare dalla moto (Weapon.Ride.cs, PlayerController.RideGunTick). Con un fucile a due mani in sella: l'arma sta riposta (nascosta, mirino spento) e non spara da sola; premuto fuoco
    /// si alza in meno di un quarto di secondo, la mano sinistra non si vede (e' sul manubrio) e neppure il braccio destro della moto (la destra e' sull'arma); spara, i colpi vanno dove punta la camera (una lastra
    /// a quattordici metri, che li conta come i barili), la dispersione cresce con la velocita'; la ricarica gira senza la mano sinistra; lasciato fuoco, un secondo e mezzo dopo l'arma torna giu'; scesi, tutto
    /// com'era (arma in mano, mano sinistra). Con -desertshots le foto dell'arma in mano, riposta e in corsa.</summary>
    public sealed partial class DesertTest
    {
        /// <summary>Un bersaglio per i colpi sparati in sella: una lastra che conta i colpi che la raggiungono (IShootable, come i barili).</summary>
        sealed class MotoTarget : MonoBehaviour, IShootable
        {
            public int hits;
            public void Shot(float dmg, Vector3 point, Vector3 dir) { hits++; }
        }

        IEnumerator MotoFire()
        {
            var pl = Pl;
            var w = pl.weapon;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset(); Weapon.rideBotFire = false;
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("motofuoco: c'e' una moto parcheggiata", bike != null);
            if (bike == null) yield break;
            godMode = true; pl.invulnerable = true;
            Vector3 home = bike.transform.position;
            Vector3 south = new Vector3(0f, 0f, -1f);

            // ---- a piedi col fucile: la mano sinistra si vede e il mirino c'e'
            int rifle = WeaponDefs.IndexOf("ar9_malleus");
            w.Equip(rifle);
            w.mag = w.magSize; w.reserve = 120;
            yield return GoTo(home.x - 2.2f, home.z + 0.3f, 2f);
            yield return new WaitForSeconds(1.2f);
            Check("motofuoco: a piedi col fucile la mano sinistra dell'arma si vede", w.LeftHandShown && !w.RideMode && w.RideStow == 0f, $"mano {w.LeftHandShown}, in sella {w.RideMode}");

            // ---- in sella l'arma e' riposta, e fermi senza premere fuoco non spara
            Face(bike.Point);
            yield return null; yield return null;
            Interact.Tick(pl.cam);
            Interact.TryUse();
            yield return null; yield return null;
            Check("motofuoco: in sella", pl.Riding && w.RideMode);
            pl.yaw = pl.RideHeading; pl.pitch = 0f;
            yield return new WaitForSeconds(0.9f);
            Check("motofuoco: in sella l'arma e' riposta: nascosta e il mirino spento", w.RideStow > 0.99f && !w.ViewRootT.gameObject.activeSelf && !w.RideGunUp, $"riposta {w.RideStow:F2}, visibile {w.ViewRootT.gameObject.activeSelf}");
            int shots0 = GameManager.shotsFired;
            yield return new WaitForSeconds(1.0f);
            Check("motofuoco: senza premere fuoco in sella non spara da sola", GameManager.shotsFired == shots0, $"{GameManager.shotsFired - shots0} colpi");

            // ---- la lastra a 14 m davanti, all'altezza dell'occhio
            Vector3 fwd = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading));
            Vector3 at = bike.EyeWorld + fwd * 14f;
            var go = new GameObject("MotoBersaglio");
            go.transform.position = at;
            go.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(9f, 5f, 0.4f);
            var target = go.AddComponent<MotoTarget>();

            // ---- fuoco: l'arma si alza in fretta
            int mag0 = w.mag;
            shots0 = GameManager.shotsFired;
            float tFire = Time.time;
            Weapon.rideBotFire = true;
            yield return WaitFor(() => w.RideStow < 0.05f, 2f);
            float tUp = Time.time - tFire;
            Check("motofuoco: premuto fuoco l'arma si alza in meno di mezzo secondo", w.RideStow < 0.05f && tUp < 0.5f, $"{tUp:F2} s, riposta {w.RideStow:F2}");
            Check("motofuoco: alzata, l'arma si vede, la mano sinistra no (e' sul manubrio) e neppure il braccio destro della moto", w.ViewRootT.gameObject.activeSelf && w.RideGunUp && !w.LeftHandShown && !bike.RightArmShown,
                $"visibile {w.ViewRootT.gameObject.activeSelf}, mano sinistra {w.LeftHandShown}, braccio destro sulla moto {bike.RightArmShown}");
            if (shots) { yield return new WaitForSeconds(0.25f); Shot("motofuoco_a_fucile_alzato"); }
            // il rinculo e' quello dell'arma a piedi e il bot non tira giu': la visuale sale a ogni colpo, quindi i primi colpi di una raffica vanno sulla lastra e gli ultimi di una raffica lunga la scavalcano
            // (13 su 20 in una prova, 14 su 17 in un'altra: la stessa arma, un numero diverso di colpi in 1,4 s). Si contano percio' i primi otto.
            int hitsEarly = -1, firedEarly = 0;
            float maxKick = 0f;
            for (float t = 0f; t < 1.4f; t += Time.deltaTime)
            {
                if (hitsEarly < 0 && GameManager.shotsFired - shots0 >= 8) { hitsEarly = target.hits; firedEarly = GameManager.shotsFired - shots0; }
                maxKick = Mathf.Max(maxKick, pl.ViewKickPitch);
                yield return null;
            }
            Weapon.rideBotFire = false;
            int fired = GameManager.shotsFired - shots0;
            Check("motofuoco: spara (i colpi aumentano e il caricatore cala)", fired >= 3 && w.mag < mag0, $"{fired} colpi, caricatore {mag0} -> {w.mag}");
            Check("motofuoco: da fermo i primi colpi vanno dove punta la camera (almeno sette degli otto primi centrano la lastra a 14 m)", firedEarly >= 8 && hitsEarly >= firedEarly - 1,
                $"{hitsEarly} dei primi {firedEarly}; in tutto {target.hits} su {fired}, il rinculo ha alzato la visuale fino a {maxKick:F1} gradi");
            Check("motofuoco: da fermo la dispersione e' quella di sempre", w.RideSpreadFactor < 1.05f, $"x{w.RideSpreadFactor:F2}");

            // ---- lasciato fuoco, un secondo e mezzo dopo l'arma torna giu'
            yield return new WaitForSeconds(1.0f);
            Check("motofuoco: un secondo dopo l'ultimo colpo l'arma e' ancora in mano", w.RideGunUp);
            yield return new WaitForSeconds(1.2f);
            Check("motofuoco: dopo un secondo e mezzo senza usarla l'arma torna giu': nascosta, e il braccio destro della moto torna", w.RideStow > 0.99f && !w.ViewRootT.gameObject.activeSelf && (bike.RightArmShown || !bike.ArmsShown),
                $"riposta {w.RideStow:F2}, braccio destro {bike.RightArmShown}, braccia {bike.ArmsShown}");

            // ---- in corsa: la dispersione cresce con la velocita', e si spara lo stesso
            DesertBike.botThrottle = 1f;
            yield return RideUntil(() => pl.RideSpeed > 14f, 10f, south);
            Weapon.rideBotFire = true;
            yield return WaitFor(() => w.RideStow < 0.05f, 2f);
            shots0 = GameManager.shotsFired;
            yield return RideUntil(() => false, 1.4f, south);
            float k = w.RideSpreadFactor;
            Check("motofuoco: in corsa a oltre 50 km/h la dispersione e' piu' larga (almeno 1,3 volte) e non oltre il doppio", k > 1.3f && k < 2.01f, $"x{k:F2} a {pl.RideSpeed * 3.6f:F0} km/h");
            Check("motofuoco: in corsa si spara lo stesso", GameManager.shotsFired - shots0 >= 3, $"{GameManager.shotsFired - shots0} colpi in 1,4 s");
            if (shots) Shot("motofuoco_c_in_corsa");
            Weapon.rideBotFire = false;
            DesertBike.botThrottle = 0f;
            yield return RideUntil(() => Mathf.Abs(pl.RideSpeed) < 1f, 8f, south);
            UnityEngine.Object.Destroy(go);

            // ---- la ricarica in sella: l'arma si inclina e la sinistra non si vede
            w.mag = 2; w.reserve = 60;
            Weapon.rideBotFire = true;
            yield return WaitFor(() => w.reloading, 6f);
            bool reloadStarted = w.reloading;
            bool leftSeen = false, leftAtStart = w.LeftHandShown;
            float tRel = Time.time;
            while (w.reloading && Time.time - tRel < 8f) { leftSeen |= w.LeftHandShown; yield return null; }
            Weapon.rideBotFire = false;
            Check("motofuoco: finito il caricatore parte la ricarica, in sella, e finisce", reloadStarted && !w.reloading && w.mag > 2, $"partita {reloadStarted}, in corso {w.reloading}, caricatore {w.mag}");
            Check("motofuoco: durante la ricarica la mano sinistra non si vede (il caricatore non lo prende lei)", !leftSeen && !leftAtStart, $"vista {leftSeen}");
            yield return new WaitForSeconds(2.0f);

            // ---- scesi, tutto com'era
            pl.RideRequestDismount();
            yield return WaitFor(() => !pl.Riding, 10f);
            yield return new WaitForSeconds(0.8f);
            Check("motofuoco: scesi, l'arma e' in mano, la sinistra si vede di nuovo e il mirino c'e'", !pl.Riding && !w.RideMode && w.RideStow == 0f && w.ViewRootT.gameObject.activeSelf && w.LeftHandShown && w.RideSpreadFactor == 1f,
                $"in sella {pl.Riding}, modo {w.RideMode}, riposta {w.RideStow:F2}, visibile {w.ViewRootT.gameObject.activeSelf}, mano {w.LeftHandShown}, dispersione x{w.RideSpreadFactor:F2}");

            // ---- con la pistola (una mano sola): in sella si alza e spara lo stesso
            w.Equip(WeaponDefs.IndexOf("p9_pieta"));
            w.mag = w.magSize; w.reserve = 60;
            yield return new WaitForSeconds(0.8f);
            Face(bike.Point);
            yield return null; yield return null;
            Interact.Tick(pl.cam);
            Interact.TryUse();
            yield return null; yield return null;
            pl.yaw = pl.RideHeading; pl.pitch = 0f;
            yield return new WaitForSeconds(0.9f);
            shots0 = GameManager.shotsFired;
            Weapon.rideBotFire = true;
            yield return WaitFor(() => w.RideStow < 0.05f, 2f);
            yield return new WaitForSeconds(0.6f);
            if (shots) Shot("motofuoco_b_pistola");
            yield return new WaitForSeconds(0.6f);
            Weapon.rideBotFire = false;
            Check("motofuoco: con la pistola si alza e spara anche in sella", GameManager.shotsFired - shots0 >= 3, $"{GameManager.shotsFired - shots0} colpi");
            pl.RideRequestDismount();
            yield return WaitFor(() => !pl.Riding, 10f);
            yield return new WaitForSeconds(0.4f);
            Weapon.rideBotFire = false;
            DesertBike.BotReset();
            godMode = true;
        }
    }
}
#endif

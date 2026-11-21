using System.Collections;
using PryingEyes.Cattedrale;
using PryingEyes.Deserto;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>La partita nel deserto (Scripts/Deserto): da soli, scelta nel menu. La cattedrale resta la zona sicura (e' lo stesso piano della
    /// modalita' standard, GoToHub con la scalinata sulla sabbia); il deserto intorno lo fa DesertDirector. InHub resta vero per tutta la partita:
    /// niente ondate, niente mappa dei piani, e il bottino va diretto nel magazzino come nel gioco diretto (Fabbrica.Inizia con diretta = true).
    /// La morte costa quello che costa nei piani (meta' dei punti e l'ultimo perk) e si rinasce in cima alla scalinata.</summary>
    public partial class FloorDirector
    {
        /// <summary>Il regista del deserto in corso (null fuori dal deserto).</summary>
        public DesertDirector Desert { get; private set; }

        /// <summary>Questa partita deve essere nel deserto: scelto nel menu (o -deserto), da soli, come ospite (0.9.1: in cooperativa fino a quattro) o a schermo condiviso (0.9.1: da due a
        /// quattro posti, anche su piu' monitor) e con le due zone attive. Un ospitato non sceglie: parte dove gioca l'ospite (StartRun con coopDesert).</summary>
        static bool DesertWanted => DesertMode.Requested && (!Coop.Active || Coop.IsHost) && (Catacombe.Attive || HasArg("-deserto") || HasArg("-desertotest"));

        IEnumerator StartDesertCo()
        {
            transitioning = true;
            gm = GameManager.I;
            SetInvulnerable(true);
            FadeAll(1f);
            DesertMode.Begin(runSeed);
            Coop.OnFloorStart(0);          // cooperativa: l'ospite annuncia il deserto (la prima epoca), l'ospitato si prepara a dire che ha costruito il suo
            // il bottino va subito nel magazzino (la spedizione resta aperta finche' non si torna al menu) e non ci sono rifornimenti da portare;
            // nello schermo condiviso ogni posto comincia la sua, con la cattedrale e il magazzino del suo personaggio
            if (Seats.Split) BeginSeatExpeditions();
            else ResetForExpedition(Fabbrica.Inizia(0, true));
            Debug.Log($"[PryingEyes] deserto: seme {DesertMode.Seed}, si parte alle {DesertMode.Clock.hour:F1}" + (Seats.Split ? $", schermo condiviso a {Seats.Count} posti" : ""));
            var terrain = DesertTerrain.Create(gm.player.transform);
            var sky = DesertSky.Create(Camera.main);
            for (int i = 1; i < Seats.All.Count; i++)
                if (Seats.All[i].camera != null) sky.AttachExtra(Seats.All[i].camera);      // ogni posto ha il suo sfondo e la sua cupola (DesertSky)
            Desert = DesertDirector.Create(gm.player.transform, terrain);
            yield return null;
            yield return GoToHub(null, true, null, 0f, "IL DESERTO", "Scendi la scalinata: là fuori il giorno è appena cominciato.");
        }

        /// <summary>Tiene il nero finche' la sabbia e la navmesh attorno all'arrivo non sono pronte (si cadrebbe nel vuoto): al massimo 25 secondi.</summary>
        IEnumerator DesertWaitReady()
        {
            float t = 0f;
            var here = new Vector3(18f, 0f, -12f);
            while (t < 25f)
            {
                bool ground = DesertTerrain.I != null && DesertTerrain.I.Ready(here, 45f);
                bool nav = DesertNav.ReadyAround(here, 40f);
                if (ground && nav) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Debug.Log($"[PryingEyes] deserto: arrivo pronto dopo {t:F1} s, {DesertTerrain.I?.ChunkCount} blocchi di sabbia, {DesertNav.BuiltTiles} tasselli di navmesh");
        }

        void ShowDesertHud()
        {
            void One(HUD h)
            {
                h.SetWaveLabel("giorno");
                h.SetWave(DesertMode.Clock.day + 1);
                h.SetRound(0, false);
                h.SetScoreLabel("PUNTI");
                h.SetScore(gm.score);       // nello schermo condiviso si chiama a nome di ogni posto: sono i suoi punti
            }
            if (Seats.Split) Seats.ForEach(_ => { if (gm.hud != null) One(gm.hud); });
            else Seats.ForHuds(One);
        }

        /// <summary>Nello schermo condiviso ogni posto comincia la sua spedizione, a nome suo: la cattedrale e il magazzino del personaggio, i punti di partenza, i perk delle cappelle e
        /// l'equipaggiamento (le ondate e i premi sono di tutti e si azzerano una volta sola).</summary>
        void BeginSeatExpeditions()
        {
            ResetRunCommon();
            Seats.ForEach(_ => ResetSeatForExpedition(Fabbrica.Inizia(0, true)));
        }

        /// <summary>L'obiettivo nel deserto, su due righe (la scritta e' larga una riga: quello che conta sta prima, il resto a capo): l'ora, il luogo piu' vicino e la
        /// strada di casa; poi che cosa si cerca. Si rinfresca ogni mezzo secondo (Update).</summary>
        internal string DesertObjective()
        {
            var c = DesertMode.Clock;
            int h = (int)c.hour, m = (int)((c.hour - h) * 60f);
            string phase = c.IsNight ? "notte" : DesertClock.Evaluate(c.hour).twilight > 0.45f ? (c.hour < 12f ? "alba" : "tramonto") : "giorno";
            string where = Desert != null ? Desert.WhereText() : "";
            return $"{h:00}:{m:00}  ·  {phase}{where}\n<color=#b8ae9c>Torri e catacombe custodiscono i boss; i libri e le armi buone stanno nella cattedrale</color>";
        }

        /// <summary>La rete di sicurezza del deserto: la sabbia e le catacombe arrivano molto sotto lo zero dei piani, quindi la quota limite dipende dal luogo.</summary>
        void DesertFallGuard(float dt)
        {
            int si = Seats.SeatIndex;
            var pl = gm.player;
            Vector3 p = pl.transform.position;
            safeTA[si] -= dt;
            if (safeTA[si] <= 0f && pl.GetComponent<CharacterController>().isGrounded)
            {
                lastSafeA[si] = p; haveSafeA[si] = true; safeTA[si] = 0.5f;
            }
            float limit = DesertWorld.Height(p.x, p.z) - 32f;
            if (p.y > limit) return;
            Vector3 back = haveSafeA[si] ? lastSafeA[si] + Vector3.up * 0.1f : new Vector3(18f, 0.1f, 2.2f);
            statFalls++;
            Debug.LogWarning($"[PryingEyes] CADUTA: giocatore a y={p.y:F0} in ({p.x:F1},{p.z:F1}), lo riporto a ({back.x:F1},{back.y:F1},{back.z:F1})");
            pl.Teleport(back);
        }

        /// <summary>Il deserto non conserva niente di una partita a meta': chi esce (menu, morte definitiva) lascia tutto, le opere costruite restano.</summary>
        void StopDesert()
        {
            if (Desert != null) Destroy(Desert.gameObject);
            Desert = null;
            DesertNav.Clear();
            if (DesertTerrain.I != null) Destroy(DesertTerrain.I.gameObject);
            if (DesertSky.I != null) Destroy(DesertSky.I.gameObject);
            DesertMode.End();
        }
    }
}

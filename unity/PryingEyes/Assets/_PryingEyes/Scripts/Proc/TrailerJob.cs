#if !PE_DISTRIBUTION_BUILD
using System;

namespace PryingEyes.Proc
{
    /// <summary>Copione di una sessione di riprese: un JSON letto a runtime da <c>-trailer file.json</c> (FloorDirector.Trailer.cs).
    /// Serve a girare il trailer con la camera guidata da codice dentro la build vera, senza ricompilare per ogni ritocco di
    /// inquadratura. Tutti i campi hanno un valore, JsonUtility non esegue gli inizializzatori dentro gli array: zero o nullo vuol dire
    /// "predefinito". I file li scrive tools/trailer/make_shots.py; le coordinate sono del mondo, in metri.</summary>
    [Serializable]
    public class TrailerJob
    {
        /// <summary>"themes" (tema e stanze di una serie di semi, senza costruire niente), "scout" (foto di ogni stanza e dati della pianta),
        /// "preview" (tre foto per ripresa: inizio, meta', fine) o "film" (i filmati veri).</summary>
        public string mode;
        public string outDir;
        public int fps, width, height, crf, msaa;
        /// <summary>Solo le riprese il cui nome contiene una di queste parole (separate da virgola); vuoto = tutte.</summary>
        public string only;
        public int seedFrom, seedTo;
        public TrailerFloor[] floors;
        public TrailerShot[] shots;
    }

    [Serializable]
    public class TrailerFloor { public int seed, floor; }

    /// <summary>Un punto fermo della camera: tempo (s), posizione, punto guardato, campo visivo e rollio (gradi).</summary>
    [Serializable]
    public class TrailerKey { public float t; public float[] p, look; public float fov, roll; }

    [Serializable]
    public class TrailerEnemy
    {
        public string type;            // nome di Enemy.EnemyType
        public float[] pos;
        public float yaw, hp, speed;   // hp 0 = praticamente immortale; speed 0 = quella del gioco
        public float delay;            // secondi dall'inizio della ripresa in cui compare
        public bool still;             // fermo come una statua (niente intelligenza artificiale)
    }

    /// <summary>Un evento a un certo istante: fire, burst (v secondi a i colpi al secondo), reload, ads (v 1/0), equip (i), torch (v 1/0),
    /// kill (i = n-esimo nemico, da 0), slow (v = scala del tempo del mondo, 1 = normale), cue (solo un marcatore per il montaggio audio).</summary>
    [Serializable]
    public class TrailerEvent { public float t; public string kind; public float v; public int i; }

    [Serializable]
    public class TrailerShot
    {
        public string name;
        public bool skip, rebuild;
        public int seed, floor;                     // seme della pianta (quello scritto nel log: runSeed + piano*1013) e piano
        public float dur, warm;                     // durata e secondi simulati prima di girare
        public bool fps;                            // vista del giocatore con l'arma in mano; altrimenti camera libera senza arma
        public int ease;                            // 0 lineare, 1 dolce in entrata e uscita, 2 frena, 3 accelera
        public float sway;                          // mano tremante (1 = lieve)
        public int equip;                           // 0 = lascia com'e'; n = arma di indice n-1
        public bool torch, lift;                    // torcia accesa; richiude e riapre l'ascensore di arrivo all'inizio
        public float ev, bloom, fog;                // esposizione in piu' (EV); moltiplicatori di bagliore e nebbia (0 = invariati)
        public float dof, focus, focal;             // profondita' di campo: apertura (f, 0 = spenta); fuoco in metri (0 = sul nemico guardato); lunghezza focale (mm, 0 = 50)
        public string aim;                          // "nearest": in fps la visuale insegue il nemico piu' vicino
        public int lookEnemy;                       // 0 = no; n = la camera guarda l'n-esimo nemico (da 1) dopo lookFrom secondi
        public float lookFrom;
        public float[] lookOff;
        public TrailerKey[] keys;
        public TrailerEnemy[] enemies;
        public TrailerEvent[] events;
        public string note;
    }

    // ---------------------------------------------------------------- uscita della ricognizione (scout)
    [Serializable]
    public class ScoutRoom
    {
        public int id, wing;
        public string kind, type, ceil;
        public bool church, lift, gallery;
        public float x0, z0, x1, z1, wallH, ridgeH;
    }

    [Serializable]
    public class ScoutOpening
    {
        public int a, b;
        public bool alongX;
        public float line, center, width, sill, top;
        public string style;
    }

    [Serializable]
    public class ScoutFloor
    {
        public int seed, floor;
        public string theme;
        public bool grid;
        public ScoutRoom[] rooms;
        public ScoutOpening[] openings;
        public float[] liftSpawn, liftLook, liftFront, liftDoor;
        public float[] exitSpawn;
        public float[] walk;                        // terne x, y, z dei punti della navmesh (passo 0,8 m)
        public string[] stills;
    }
}
#endif

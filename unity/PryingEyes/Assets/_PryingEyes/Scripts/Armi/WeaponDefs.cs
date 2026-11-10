using UnityEngine;

namespace PryingEyes
{
    public enum FireMode { Auto, Semi, Burst, Pump, Bolt, Double, Lever }
    public enum ReloadStyle { Mag, Pistol, Tube, Revolver, Break, Belt }

    /// <summary>Scheda di un'arma: bilanciamento, maneggevolezza, mirino. I numeri sono in gradi e secondi.
    /// Il modello 3D e' il prefab Resources/Armi/&lt;id&gt; (modelli/armi/genera_armi.py).</summary>
    public class WeaponDef
    {
        public string id, name, cls;
        public int grade = 1;                 // 1 ordinaria .. 5 reliquia
        public int price;
        public FireMode[] modes = { FireMode.Auto };

        // danno
        public float damage = 25f;            // per proiettile (o pallino) a bruciapelo
        public float headMult = 2f;
        public float rangeNear = 15f, rangeFar = 60f, farMult = 0.6f;   // pieno fino a rangeNear, poi cala fino a farMult*danno a rangeFar
        public int pellets = 1;
        public float cone = 0f;               // semiapertura del cono dei pallini (gradi)
        public int pierce = 0;                // nemici attraversati in piu'
        public float pierceMult = 0.65f;      // il danno si moltiplica per questo a ogni nemico attraversato

        // cadenza
        public float rpm = 600f;
        public int burst = 3;                 // colpi per raffica (FireMode.Burst)
        public float burstGap = 0.12f;        // pausa dopo la raffica (s)
        public float cycle = 0f;              // ciclo manuale (otturatore, pompa, leva): s tra un colpo e il successivo

        // munizioni
        public int mag = 30, reserve = 120, reserveMax = 240;
        public float reload = 1.8f, reloadEmpty = 2.2f;
        public ReloadStyle style = ReloadStyle.Mag;
        public float shellTime = 0.5f;        // ricarica a canna: tempo per cartuccia

        // precisione (semiapertura in gradi)
        public float hip = 2f, ads = 0.6f;
        public float moveSpread = 1.0f;       // moltiplicatore massimo con la corsa (1 + moveSpread)
        public float bloomShot = 0.35f;       // gradi aggiunti a ogni colpo
        public float bloomMax = 3.0f;
        public float bloomRecover = 6f;       // gradi/s

        // rinculo
        public float recV = 1f, recH = 0.3f;  // rinculo della visuale a ogni colpo (gradi, verticale / orizzontale casuale)
        public float recRecover = 8f;         // ritorno della visuale (gradi/s)
        public float kick = 1f;               // scala del contraccolpo del modello 3D

        // maneggevolezza
        public float adsTime = 0.16f;
        public float drawTime = 0.35f;
        public float moveMult = 1f;           // velocita' di cammino
        public float adsMoveMult = 0.65f;

        // mirino
        public string sight = "iron";         // iron | peep | reflex | holo | ring | acog | scope
        public float zoom = 1f;               // ingrandimento dell'ottica (PiP se > 1.4)
        public float adsFov = 62f;
        public float adsDist = 0.34f;         // distanza del punto di mira dalla camera in ADS
        public int pattern = 0;               // reticolo: 0 punto, 1 anello+punto, 2 anello, 3 chevron, 4 croce
        public float dotSize = 1f;

        // resa
        public string snd = "rifle";          // set di suoni di sparo
        public float flash = 1f;
        public bool suppressed;
        public string casing = "rifle";       // pistol | rifle | big | shell | none
        public float tracerEvery = 3f;
        public Color tracerColor = new Color(1f, 0.82f, 0.5f);
        public float tracerWidth = 0.010f;
        public int tracerPellets = 2;         // quanti pallini di uno sparo mostrano la traccia
        public bool beam;                     // raggio istantaneo al posto della traccia che viaggia
        public string fx = "";                // "flame" = getto di fiamme
        public int soundEvery = 1;            // il suono parte ogni n colpi (fiamme: un ruggito continuo)
        public Color flashColor = new Color(1f, 0.62f, 0.28f);
        public Color? reticle;                // colore del reticolo collimato (default rosso)
        public float hand = 1f;               // 1 = due mani, 0 = la sinistra non tiene l'arma
        public float handDrop;                // abbassa la mano destra (m): le pistole hanno la canna piu' bassa dei fucili per cui sono fatte le mani
        public bool magUp;                    // il caricatore si sfila verso l'alto (PDW con caricatore sopra la canna)

        public string GradeName => GradeNames[Mathf.Clamp(grade, 1, 5) - 1];
        public Color GradeColor => GradeColors[Mathf.Clamp(grade, 1, 5) - 1];
        public string ModeLabel(FireMode m)
        {
            switch (m)
            {
                case FireMode.Auto: return "AUTO";
                case FireMode.Semi: return "SEMI";
                case FireMode.Burst: return "RAFFICA";
                case FireMode.Pump: return "POMPA";
                case FireMode.Bolt: return "OTTURATORE";
                case FireMode.Double: return "DOPPIETTA";
                default: return "LEVA";
            }
        }

        public static readonly string[] GradeNames = { "Ordinaria", "Scelta", "Rara", "Epica", "Reliquia" };
        public static readonly Color[] GradeColors =
        {
            new Color(0.72f, 0.72f, 0.70f), new Color(0.45f, 0.85f, 0.45f), new Color(0.38f, 0.70f, 1.00f),
            new Color(0.72f, 0.50f, 1.00f), new Color(1.00f, 0.78f, 0.28f),
        };
    }

    /// <summary>L'arsenale: 25 armi in cinque gradi di potenza.</summary>
    public static class WeaponDefs
    {
        static readonly FireMode[] A = { FireMode.Auto }, S = { FireMode.Semi }, AS = { FireMode.Auto, FireMode.Semi },
            B = { FireMode.Burst }, SA = { FireMode.Semi, FireMode.Auto };
        // gli array dei modi di fuoco stanno sopra: gli inizializzatori statici girano nell'ordine del testo, e Build() li usa
        public static readonly WeaponDef[] All = Build();

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return -1;
        }

        static WeaponDef[] Build()
        {
            return new[]
            {
                // ---------------- grado I: ordinarie
                new WeaponDef { id = "p9_pieta", name = "PIETÀ P-9", cls = "PISTOLA", grade = 1, price = 0, modes = S,
                    damage = 25, headMult = 2f, rangeNear = 14, rangeFar = 45, farMult = 0.55f, rpm = 460,
                    mag = 15, reserve = 90, reserveMax = 150, reload = 1.45f, reloadEmpty = 1.85f, style = ReloadStyle.Pistol,
                    hip = 1.5f, ads = 0.35f, bloomShot = 0.45f, bloomMax = 2.2f, bloomRecover = 7f, recV = 0.9f, recH = 0.25f, recRecover = 10f, kick = 0.8f,
                    adsTime = 0.12f, drawTime = 0.28f, moveMult = 1.0f, adsMoveMult = 0.8f, sight = "iron", adsFov = 64f, adsDist = 0.30f,
                    snd = "pistol", hand = 0f, handDrop = 0.028f, flash = 0.8f, casing = "pistol", tracerEvery = 1f },
                new WeaponDef { id = "m45_vigilia", name = "VIGILIA .45", cls = "PISTOLA", grade = 1, price = 450, modes = S,
                    damage = 38, headMult = 2.1f, rangeNear = 12, rangeFar = 40, farMult = 0.5f, rpm = 380,
                    mag = 8, reserve = 56, reserveMax = 96, reload = 1.6f, reloadEmpty = 2.05f, style = ReloadStyle.Pistol,
                    hip = 1.7f, ads = 0.30f, bloomShot = 0.6f, bloomMax = 2.4f, bloomRecover = 6f, recV = 1.6f, recH = 0.3f, recRecover = 9f, kick = 1.15f,
                    adsTime = 0.13f, drawTime = 0.30f, moveMult = 1.0f, adsMoveMult = 0.8f, sight = "iron", adsFov = 64f, adsDist = 0.30f,
                    snd = "pistol45", hand = 0f, handDrop = 0.028f, flash = 0.9f, casing = "pistol", tracerEvery = 1f },
                new WeaponDef { id = "smg_tarantola", name = "TARANTOLA", cls = "MICRO-MITRA", grade = 1, price = 500, modes = A,
                    damage = 13, headMult = 1.8f, rangeNear = 7, rangeFar = 24, farMult = 0.5f, rpm = 1150,
                    mag = 32, reserve = 192, reserveMax = 288, reload = 1.55f, reloadEmpty = 1.9f, style = ReloadStyle.Mag,
                    hip = 3.4f, ads = 1.5f, moveSpread = 0.6f, bloomShot = 0.22f, bloomMax = 3.2f, bloomRecover = 8f, recV = 0.5f, recH = 0.45f, recRecover = 12f, kick = 0.7f,
                    adsTime = 0.11f, drawTime = 0.25f, moveMult = 1.07f, adsMoveMult = 0.85f, sight = "iron", adsFov = 62f, adsDist = 0.32f,
                    snd = "smg", flash = 0.9f, casing = "pistol", tracerEvery = 4f },

                // ---------------- grado II: scelte
                new WeaponDef { id = "rv_cardinale", name = "CARDINALE .357", cls = "REVOLVER", grade = 2, price = 1000, modes = S,
                    damage = 70, headMult = 2.3f, rangeNear = 18, rangeFar = 60, farMult = 0.6f, pierce = 1, pierceMult = 0.6f, rpm = 190,
                    mag = 6, reserve = 42, reserveMax = 72, reload = 2.6f, reloadEmpty = 2.6f, style = ReloadStyle.Revolver,
                    hip = 2.2f, ads = 0.25f, bloomShot = 0.9f, bloomMax = 3f, bloomRecover = 5f, recV = 3.4f, recH = 0.6f, recRecover = 8f, kick = 1.6f,
                    adsTime = 0.14f, drawTime = 0.40f, moveMult = 1.0f, adsMoveMult = 0.8f, sight = "iron", adsFov = 64f, adsDist = 0.31f,
                    snd = "revolver", hand = 0f, handDrop = 0.028f, flash = 1.15f, casing = "none", tracerEvery = 1f },
                new WeaponDef { id = "mp_serafino", name = "SERAFINO-18", cls = "PISTOLA MITRAGLIATRICE", grade = 2, price = 950, modes = A,
                    damage = 16, headMult = 1.8f, rangeNear = 8, rangeFar = 28, farMult = 0.55f, rpm = 1000,
                    mag = 20, reserve = 120, reserveMax = 200, reload = 1.5f, reloadEmpty = 1.9f, style = ReloadStyle.Pistol,
                    hip = 2.8f, ads = 1.1f, moveSpread = 0.7f, bloomShot = 0.30f, bloomMax = 3.2f, bloomRecover = 7f, recV = 0.85f, recH = 0.5f, recRecover = 11f, kick = 0.8f,
                    adsTime = 0.12f, drawTime = 0.28f, moveMult = 1.04f, adsMoveMult = 0.85f, sight = "reflex", adsFov = 58f, adsDist = 0.30f, pattern = 0, dotSize = 1.3f,
                    snd = "smg", hand = 0f, handDrop = 0.028f, flash = 0.95f, casing = "pistol", tracerEvery = 3f },
                new WeaponDef { id = "smg_vesper9", name = "VESPER-9", cls = "MITRA", grade = 2, price = 1100, modes = A,
                    damage = 20, headMult = 2f, rangeNear = 12, rangeFar = 40, farMult = 0.55f, rpm = 850,
                    mag = 40, reserve = 200, reserveMax = 320, reload = 1.7f, reloadEmpty = 2.1f, style = ReloadStyle.Mag,
                    hip = 2.0f, ads = 0.8f, moveSpread = 0.7f, bloomShot = 0.25f, bloomMax = 2.6f, bloomRecover = 8f, recV = 0.65f, recH = 0.3f, recRecover = 11f, kick = 0.8f,
                    adsTime = 0.15f, drawTime = 0.30f, moveMult = 1.03f, adsMoveMult = 0.75f, sight = "reflex", adsFov = 58f, adsDist = 0.32f, pattern = 0, dotSize = 1.2f,
                    snd = "smg", flash = 1f, casing = "pistol", tracerEvery = 3f },
                new WeaponDef { id = "smg_cimbalo", name = "CIMBALO-45", cls = "MITRA", grade = 2, price = 1300, modes = AS,
                    damage = 29, headMult = 2f, rangeNear = 13, rangeFar = 42, farMult = 0.55f, rpm = 620,
                    mag = 25, reserve = 150, reserveMax = 250, reload = 1.9f, reloadEmpty = 2.3f, style = ReloadStyle.Mag,
                    hip = 2.2f, ads = 0.7f, moveSpread = 0.8f, bloomShot = 0.30f, bloomMax = 2.6f, bloomRecover = 7f, recV = 0.95f, recH = 0.25f, recRecover = 10f, kick = 0.95f,
                    adsTime = 0.16f, drawTime = 0.32f, moveMult = 1.0f, adsMoveMult = 0.72f, sight = "holo", adsFov = 58f, adsDist = 0.31f, pattern = 1,
                    snd = "smg45", flash = 1f, casing = "pistol", tracerEvery = 3f },
                new WeaponDef { id = "ar_trinita", name = "TRINITÀ", cls = "FUCILE A RAFFICA", grade = 2, price = 1250, modes = B,
                    damage = 36, headMult = 2.1f, rangeNear = 28, rangeFar = 85, farMult = 0.65f, rpm = 900, burst = 3, burstGap = 0.15f,
                    mag = 30, reserve = 150, reserveMax = 240, reload = 1.95f, reloadEmpty = 2.4f, style = ReloadStyle.Mag,
                    hip = 1.7f, ads = 0.45f, bloomShot = 0.35f, bloomMax = 2.4f, bloomRecover = 7f, recV = 1.15f, recH = 0.2f, recRecover = 10f, kick = 1.0f,
                    adsTime = 0.18f, drawTime = 0.36f, moveMult = 0.98f, adsMoveMult = 0.68f, sight = "peep", adsFov = 60f, adsDist = 0.34f,
                    snd = "rifle", flash = 1f, casing = "rifle", tracerEvery = 2f },
                new WeaponDef { id = "ar9_malleus", name = "AR-9 MALLEUS", cls = "FUCILE D'ASSALTO", grade = 2, price = 1400, modes = AS,
                    damage = 30, headMult = 2f, rangeNear = 28, rangeFar = 90, farMult = 0.65f, rpm = 700,
                    mag = 30, reserve = 120, reserveMax = 240, reload = 1.9f, reloadEmpty = 2.3f, style = ReloadStyle.Mag,
                    hip = 1.9f, ads = 0.6f, bloomShot = 0.30f, bloomMax = 2.6f, bloomRecover = 7f, recV = 1.0f, recH = 0.35f, recRecover = 9f, kick = 1.0f,
                    adsTime = 0.18f, drawTime = 0.36f, moveMult = 0.98f, adsMoveMult = 0.66f, sight = "reflex", adsFov = 58f, adsDist = 0.33f, pattern = 0, dotSize = 1.2f,
                    snd = "rifle", flash = 1f, casing = "rifle", tracerEvery = 3f },
                new WeaponDef { id = "lv_predicatore", name = "PREDICATORE", cls = "CARABINA A LEVA", grade = 2, price = 1150, modes = new[] { FireMode.Lever },
                    damage = 66, headMult = 2.4f, rangeNear = 30, rangeFar = 95, farMult = 0.65f, pierce = 1, pierceMult = 0.6f, rpm = 85, cycle = 0.62f,
                    mag = 8, reserve = 48, reserveMax = 72, reload = 0.9f, reloadEmpty = 0.9f, style = ReloadStyle.Tube, shellTime = 0.5f,
                    hip = 2.2f, ads = 0.3f, bloomShot = 0.9f, bloomMax = 2.8f, bloomRecover = 5f, recV = 3.0f, recH = 0.5f, recRecover = 7f, kick = 1.5f,
                    adsTime = 0.17f, drawTime = 0.40f, moveMult = 0.97f, adsMoveMult = 0.7f, sight = "iron", adsFov = 62f, adsDist = 0.33f,
                    snd = "lever", flash = 1.1f, casing = "rifle", tracerEvery = 1f },
                new WeaponDef { id = "bolt_rosario", name = "ROSARIO", cls = "FUCILE A OTTURATORE", grade = 2, price = 1600, modes = new[] { FireMode.Bolt },
                    damage = 118, headMult = 3f, rangeNear = 45, rangeFar = 140, farMult = 0.7f, pierce = 2, pierceMult = 0.7f, rpm = 55, cycle = 1.1f,
                    mag = 5, reserve = 40, reserveMax = 60, reload = 2.9f, reloadEmpty = 3.2f, style = ReloadStyle.Mag,
                    hip = 3.5f, ads = 0.08f, bloomShot = 1.4f, bloomMax = 3.5f, bloomRecover = 4f, recV = 4.5f, recH = 0.4f, recRecover = 6f, kick = 1.8f,
                    adsTime = 0.22f, drawTime = 0.45f, moveMult = 0.95f, adsMoveMult = 0.6f, sight = "peep", adsFov = 52f, adsDist = 0.30f,
                    snd = "sniper", flash = 1.3f, casing = "rifle", tracerEvery = 1f },
                new WeaponDef { id = "sg_cappellano", name = "CAPPELLANO", cls = "DOPPIETTA", grade = 2, price = 950, modes = new[] { FireMode.Double },
                    damage = 14, headMult = 1.5f, rangeNear = 6, rangeFar = 22, farMult = 0.2f, pellets = 10, cone = 4.4f, rpm = 170,
                    mag = 2, reserve = 24, reserveMax = 40, reload = 2.3f, reloadEmpty = 2.3f, style = ReloadStyle.Break,
                    hip = 0.3f, ads = 0.1f, bloomShot = 0.8f, bloomMax = 1.5f, bloomRecover = 5f, recV = 4.2f, recH = 0.7f, recRecover = 6f, kick = 2.0f,
                    adsTime = 0.17f, drawTime = 0.38f, moveMult = 0.98f, adsMoveMult = 0.75f, sight = "iron", adsFov = 62f, adsDist = 0.34f,
                    snd = "shotgun", flash = 1.4f, casing = "shell", tracerEvery = 99f },
                new WeaponDef { id = "sg_breacher12", name = "BREACHER-12", cls = "FUCILE A POMPA", grade = 2, price = 1300, modes = new[] { FireMode.Pump },
                    damage = 15, headMult = 1.6f, rangeNear = 8, rangeFar = 26, farMult = 0.25f, pellets = 8, cone = 3.0f, rpm = 80, cycle = 0.75f,
                    mag = 8, reserve = 48, reserveMax = 80, reload = 0.9f, reloadEmpty = 0.9f, style = ReloadStyle.Tube, shellTime = 0.5f,
                    hip = 0.3f, ads = 0.1f, bloomShot = 0.8f, bloomMax = 1.5f, bloomRecover = 5f, recV = 3.4f, recH = 0.5f, recRecover = 7f, kick = 1.7f,
                    adsTime = 0.17f, drawTime = 0.40f, moveMult = 0.97f, adsMoveMult = 0.7f, sight = "peep", adsFov = 62f, adsDist = 0.34f,
                    snd = "shotgun", flash = 1.4f, casing = "shell", tracerEvery = 99f },

                // ---------------- grado III: rare
                new WeaponDef { id = "pdw_bordone", name = "BORDONE", cls = "ARMA PER LA DIFESA PERSONALE", grade = 3, price = 1900, modes = A,
                    damage = 23, headMult = 2f, rangeNear = 20, rangeFar = 60, farMult = 0.6f, pierce = 1, pierceMult = 0.6f, rpm = 900,
                    mag = 50, reserve = 200, reserveMax = 300, reload = 2.6f, reloadEmpty = 3.0f, style = ReloadStyle.Mag,
                    hip = 1.9f, ads = 0.55f, moveSpread = 0.6f, bloomShot = 0.2f, bloomMax = 2.0f, bloomRecover = 9f, recV = 0.55f, recH = 0.25f, recRecover = 12f, kick = 0.7f,
                    adsTime = 0.15f, drawTime = 0.30f, moveMult = 1.03f, adsMoveMult = 0.8f, sight = "ring", adsFov = 58f, adsDist = 0.30f, pattern = 2,
                    snd = "smg", magUp = true, flash = 0.9f, casing = "pistol", tracerEvery = 3f },
                new WeaponDef { id = "ak_arcangelo", name = "ARCANGELO-47", cls = "FUCILE D'ASSALTO", grade = 3, price = 2000, modes = AS,
                    damage = 42, headMult = 2f, rangeNear = 28, rangeFar = 90, farMult = 0.65f, pierce = 1, pierceMult = 0.6f, rpm = 600,
                    mag = 30, reserve = 120, reserveMax = 210, reload = 2.2f, reloadEmpty = 2.7f, style = ReloadStyle.Mag,
                    hip = 2.6f, ads = 0.9f, bloomShot = 0.45f, bloomMax = 3.2f, bloomRecover = 6f, recV = 1.9f, recH = 0.6f, recRecover = 8f, kick = 1.3f,
                    adsTime = 0.19f, drawTime = 0.40f, moveMult = 0.96f, adsMoveMult = 0.65f, sight = "iron", adsFov = 62f, adsDist = 0.34f,
                    snd = "ak", flash = 1.15f, casing = "rifle", tracerEvery = 3f },
                new WeaponDef { id = "bp_orifiamma", name = "ORIFIAMMA", cls = "FUCILE BULLPUP", grade = 3, price = 2300, modes = AS,
                    damage = 32, headMult = 2f, rangeNear = 30, rangeFar = 95, farMult = 0.65f, rpm = 680,
                    mag = 30, reserve = 150, reserveMax = 240, reload = 2.3f, reloadEmpty = 2.9f, style = ReloadStyle.Mag,
                    hip = 1.9f, ads = 0.4f, bloomShot = 0.28f, bloomMax = 2.2f, bloomRecover = 8f, recV = 0.85f, recH = 0.25f, recRecover = 10f, kick = 0.85f,
                    adsTime = 0.17f, drawTime = 0.36f, moveMult = 1.0f, adsMoveMult = 0.7f, sight = "acog", zoom = 1.5f, adsFov = 56f, adsDist = 0.20f, pattern = 3,
                    snd = "rifle", flash = 1f, casing = "rifle", tracerEvery = 3f },
                new WeaponDef { id = "dmr_lance7", name = "LANCE-7", cls = "FUCILE DI PRECISIONE", grade = 3, price = 2100, modes = S,
                    damage = 72, headMult = 2.5f, rangeNear = 40, rangeFar = 130, farMult = 0.75f, pierce = 1, pierceMult = 0.65f, rpm = 280,
                    mag = 12, reserve = 72, reserveMax = 120, reload = 2.1f, reloadEmpty = 2.6f, style = ReloadStyle.Mag,
                    hip = 2.8f, ads = 0.12f, bloomShot = 0.9f, bloomMax = 3f, bloomRecover = 5f, recV = 2.2f, recH = 0.2f, recRecover = 7f, kick = 1.4f,
                    adsTime = 0.22f, drawTime = 0.45f, moveMult = 0.94f, adsMoveMult = 0.6f, sight = "scope", zoom = 4f, adsFov = 52f, adsDist = 0.11f, pattern = 4,
                    snd = "br", flash = 1.2f, casing = "rifle", tracerEvery = 1f },
                new WeaponDef { id = "sg_martello12", name = "MARTELLO-12", cls = "FUCILE SEMIAUTOMATICO", grade = 3, price = 2300, modes = S,
                    damage = 13, headMult = 1.6f, rangeNear = 9, rangeFar = 28, farMult = 0.25f, pellets = 8, cone = 3.2f, rpm = 190,
                    mag = 8, reserve = 56, reserveMax = 88, reload = 2.8f, reloadEmpty = 3.3f, style = ReloadStyle.Mag,
                    hip = 0.3f, ads = 0.1f, bloomShot = 0.9f, bloomMax = 1.6f, bloomRecover = 5f, recV = 3.0f, recH = 0.4f, recRecover = 8f, kick = 1.5f,
                    adsTime = 0.18f, drawTime = 0.40f, moveMult = 0.96f, adsMoveMult = 0.7f, sight = "reflex", adsFov = 58f, adsDist = 0.31f, pattern = 0, dotSize = 1.3f,
                    snd = "shotgun", flash = 1.4f, casing = "shell", tracerEvery = 99f },
                new WeaponDef { id = "lmg_salterio", name = "SALTERIO", cls = "MITRAGLIATRICE A TAMBURO", grade = 3, price = 2400, modes = A,
                    damage = 36, headMult = 1.9f, rangeNear = 28, rangeFar = 90, farMult = 0.6f, pierce = 1, pierceMult = 0.6f, rpm = 640,
                    mag = 75, reserve = 225, reserveMax = 300, reload = 4.4f, reloadEmpty = 5.2f, style = ReloadStyle.Mag,
                    hip = 3.2f, ads = 1.2f, bloomShot = 0.42f, bloomMax = 3.6f, bloomRecover = 5f, recV = 1.4f, recH = 0.45f, recRecover = 7f, kick = 1.2f,
                    adsTime = 0.24f, drawTime = 0.50f, moveMult = 0.90f, adsMoveMult = 0.55f, sight = "iron", adsFov = 62f, adsDist = 0.34f,
                    snd = "lmg", flash = 1.2f, casing = "rifle", tracerEvery = 2f },

                // ---------------- grado IV: epiche
                new WeaponDef { id = "de_eremita", name = "EREMITA .50", cls = "PISTOLA MAGNUM", grade = 4, price = 2900, modes = S,
                    damage = 125, headMult = 2.4f, rangeNear = 22, rangeFar = 70, farMult = 0.6f, pierce = 2, pierceMult = 0.6f, rpm = 150,
                    mag = 7, reserve = 35, reserveMax = 56, reload = 2.1f, reloadEmpty = 2.5f, style = ReloadStyle.Pistol,
                    hip = 2.4f, ads = 0.3f, bloomShot = 1.2f, bloomMax = 3.2f, bloomRecover = 4f, recV = 5.8f, recH = 0.8f, recRecover = 7f, kick = 2.2f,
                    adsTime = 0.16f, drawTime = 0.42f, moveMult = 0.98f, adsMoveMult = 0.75f, sight = "iron", adsFov = 62f, adsDist = 0.31f,
                    snd = "magnum", hand = 0f, handDrop = 0.028f, flash = 1.6f, casing = "big", tracerEvery = 1f },
                new WeaponDef { id = "br_gloria", name = "GLORIA", cls = "FUCILE DA BATTAGLIA", grade = 4, price = 3000, modes = AS,
                    damage = 55, headMult = 2.1f, rangeNear = 35, rangeFar = 110, farMult = 0.7f, pierce = 1, pierceMult = 0.65f, rpm = 520,
                    mag = 20, reserve = 100, reserveMax = 160, reload = 2.5f, reloadEmpty = 3.0f, style = ReloadStyle.Mag,
                    hip = 2.4f, ads = 0.5f, bloomShot = 0.5f, bloomMax = 3.2f, bloomRecover = 6f, recV = 2.8f, recH = 0.5f, recRecover = 7f, kick = 1.5f,
                    adsTime = 0.22f, drawTime = 0.45f, moveMult = 0.93f, adsMoveMult = 0.6f, sight = "acog", zoom = 4f, adsFov = 54f, adsDist = 0.17f, pattern = 3,
                    snd = "br", flash = 1.25f, casing = "rifle", tracerEvery = 2f },
                new WeaponDef { id = "snp_sentinella", name = "SENTINELLA", cls = "FUCILE DA CECCHINO", grade = 4, price = 3300, modes = new[] { FireMode.Bolt },
                    damage = 190, headMult = 2.5f, rangeNear = 60, rangeFar = 180, farMult = 0.8f, pierce = 3, pierceMult = 0.75f, rpm = 50, cycle = 1.2f,
                    mag = 5, reserve = 30, reserveMax = 45, reload = 2.9f, reloadEmpty = 3.2f, style = ReloadStyle.Mag,
                    hip = 4.5f, ads = 0.05f, bloomShot = 1.6f, bloomMax = 4f, bloomRecover = 3.5f, recV = 5.5f, recH = 0.4f, recRecover = 5f, kick = 2.0f,
                    adsTime = 0.32f, drawTime = 0.55f, moveMult = 0.90f, adsMoveMult = 0.5f, sight = "scope", zoom = 8f, adsFov = 50f, adsDist = 0.11f, pattern = 4,
                    snd = "sniper", flash = 1.4f, casing = "big", tracerEvery = 1f },
                new WeaponDef { id = "sg_apocalisse12", name = "APOCALISSE-12", cls = "FUCILE AUTOMATICO A TAMBURO", grade = 4, price = 3500, modes = A,
                    damage = 11, headMult = 1.6f, rangeNear = 9, rangeFar = 28, farMult = 0.25f, pellets = 8, cone = 3.6f, rpm = 300,
                    mag = 20, reserve = 80, reserveMax = 120, reload = 4.4f, reloadEmpty = 5.0f, style = ReloadStyle.Mag,
                    hip = 0.3f, ads = 0.1f, bloomShot = 0.5f, bloomMax = 1.8f, bloomRecover = 5f, recV = 2.8f, recH = 0.7f, recRecover = 7f, kick = 1.4f,
                    adsTime = 0.22f, drawTime = 0.48f, moveMult = 0.90f, adsMoveMult = 0.6f, sight = "holo", adsFov = 58f, adsDist = 0.32f, pattern = 1,
                    snd = "shotgun", flash = 1.5f, casing = "shell", tracerEvery = 99f },
                new WeaponDef { id = "lmg_campana249", name = "CAMPANA-249", cls = "MITRAGLIATRICE LEGGERA", grade = 4, price = 3400, modes = A,
                    damage = 40, headMult = 1.9f, rangeNear = 30, rangeFar = 100, farMult = 0.6f, pierce = 1, pierceMult = 0.6f, rpm = 780,
                    mag = 100, reserve = 300, reserveMax = 400, reload = 5.2f, reloadEmpty = 6.0f, style = ReloadStyle.Belt,
                    hip = 3.5f, ads = 1.1f, bloomShot = 0.40f, bloomMax = 3.6f, bloomRecover = 5f, recV = 1.3f, recH = 0.5f, recRecover = 7f, kick = 1.3f,
                    adsTime = 0.28f, drawTime = 0.55f, moveMult = 0.84f, adsMoveMult = 0.5f, sight = "acog", zoom = 3f, adsFov = 55f, adsDist = 0.17f, pattern = 3,
                    snd = "lmg", flash = 1.3f, casing = "rifle", tracerEvery = 2f },

                // ---------------- grado V: reliquia
                // ---------------- armi speciali e aliene del culto dell'Occhio
                new WeaponDef { id = "tu_turibolo", name = "TURIBOLO", cls = "LANCIAFIAMME SACRO", grade = 3, price = 2200, modes = A,
                    damage = 9, headMult = 1.2f, rangeNear = 3, rangeFar = 11, farMult = 0.35f, pierce = 4, pierceMult = 0.92f, rpm = 1100,
                    mag = 120, reserve = 360, reserveMax = 480, reload = 3.2f, reloadEmpty = 3.6f, style = ReloadStyle.Mag,
                    hip = 3.5f, ads = 2.5f, moveSpread = 0.2f, bloomShot = 0.02f, bloomMax = 0.3f, bloomRecover = 8f, recV = 0.12f, recH = 0.08f, recRecover = 14f, kick = 0.25f,
                    adsTime = 0.20f, drawTime = 0.50f, moveMult = 0.95f, adsMoveMult = 0.7f, sight = "iron", adsFov = 62f, adsDist = 0.34f,
                    snd = "flame", soundEvery = 2, fx = "flame", flash = 0.6f, flashColor = new Color(1f, 0.55f, 0.15f), casing = "none", tracerEvery = 999f },
                new WeaponDef { id = "sp_spora", name = "SPORA", cls = "LANCIASPINE ALIENO", grade = 4, price = 3200, modes = S,
                    damage = 21, headMult = 1.5f, rangeNear = 14, rangeFar = 45, farMult = 0.35f, pellets = 7, cone = 5.0f, pierce = 1, pierceMult = 0.7f, rpm = 140,
                    mag = 6, reserve = 36, reserveMax = 60, reload = 2.6f, reloadEmpty = 2.6f, style = ReloadStyle.Mag,
                    hip = 0.6f, ads = 0.2f, bloomShot = 0.8f, bloomMax = 1.6f, bloomRecover = 5f, recV = 2.6f, recH = 0.4f, recRecover = 8f, kick = 1.3f,
                    adsTime = 0.18f, drawTime = 0.40f, moveMult = 0.97f, adsMoveMult = 0.7f, sight = "iron", adsFov = 62f, adsDist = 0.34f,
                    snd = "spine", flash = 0.9f, flashColor = new Color(0.55f, 1f, 0.35f), casing = "none", tracerEvery = 1f,
                    tracerColor = new Color(0.55f, 1f, 0.35f), tracerWidth = 0.012f, tracerPellets = 7 },
                new WeaponDef { id = "pu_pupilla", name = "PUPILLA", cls = "FUCILE-OCCHIO", grade = 5, price = 6500, modes = S,
                    damage = 90, headMult = 2.2f, rangeNear = 200, rangeFar = 400, farMult = 1f, pierce = 99, pierceMult = 0.92f, rpm = 210,
                    mag = 14, reserve = 70, reserveMax = 98, reload = 2.8f, reloadEmpty = 2.8f, style = ReloadStyle.Mag,
                    hip = 2.2f, ads = 0.04f, bloomShot = 0.5f, bloomMax = 2.4f, bloomRecover = 7f, recV = 1.2f, recH = 0.15f, recRecover = 12f, kick = 0.9f,
                    adsTime = 0.20f, drawTime = 0.45f, moveMult = 1.0f, adsMoveMult = 0.7f, sight = "ring", adsFov = 56f, adsDist = 0.30f, pattern = 2,
                    snd = "beam", flash = 1.3f, flashColor = new Color(1f, 0.62f, 0.15f), casing = "none", tracerEvery = 1f, beam = true,
                    tracerColor = new Color(1f, 0.62f, 0.12f), tracerWidth = 0.034f, reticle = new Color(1f, 0.075f, 0.006f, 1f) },   // il reticolo e' lineare x 5,5: ambra vera = verde basso

                new WeaponDef { id = "amr_altare50", name = "ALTARE-50", cls = "FUCILE ANTIMATERIALE", grade = 5, price = 5500, modes = S,
                    damage = 420, headMult = 2f, rangeNear = 80, rangeFar = 220, farMult = 0.85f, pierce = 5, pierceMult = 0.85f, rpm = 80,
                    mag = 5, reserve = 20, reserveMax = 30, reload = 3.6f, reloadEmpty = 4.2f, style = ReloadStyle.Mag,
                    hip = 5.0f, ads = 0.10f, bloomShot = 2.2f, bloomMax = 5f, bloomRecover = 3f, recV = 8f, recH = 1.0f, recRecover = 4.5f, kick = 2.6f,
                    adsTime = 0.38f, drawTime = 0.70f, moveMult = 0.80f, adsMoveMult = 0.45f, sight = "scope", zoom = 10f, adsFov = 48f, adsDist = 0.11f, pattern = 4,
                    snd = "amr", flash = 2.0f, casing = "big", tracerEvery = 1f },
            };
        }
    }
}

using UnityEngine;

namespace PryingEyes
{
    public enum FireMode { Auto, Semi, Burst, Pump, Bolt, Double, Lever }
    public enum ReloadStyle { Mag, Pistol, Tube, Revolver, Break, Belt }

    /// <summary>Scheda di un'arma: bilanciamento, maneggevolezza, mirino. I numeri sono in gradi e secondi.
    /// Il modello 3D e' il prefab Resources/Armi/&lt;id&gt; (modelli/armi/genera_armi.py).</summary>
    [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.Fields)]
    public class WeaponDef
    {
        public string id, name, cls;
        public int grade = 1;                 // 1 ordinaria .. 5 reliquia
        public int price;
        public bool cityOnly;
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

    /// <summary>Catalogo autorevole in Resources/Production/weapons.json; gli indici sono append-only.</summary>
    public static class WeaponDefs
    {
        public static WeaponCatalog Catalog { get; private set; }
        public static string ContentHash { get; private set; }
        public static readonly WeaponDef[] All = Build();

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return -1;
        }

        static WeaponDef[] Build()
        {
            var asset = Resources.Load<TextAsset>("Production/weapons");
            if (asset == null) throw new System.InvalidOperationException("WEAPON_CATALOG_MISSING: Production/weapons");
            Catalog = WeaponCatalog.Parse(asset.text);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                ContentHash = System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(
                    Catalog.ToJson().Replace("\r\n", "\n")))).Replace("-", "").ToLowerInvariant().Substring(0, 12);
            var result = new WeaponDef[Catalog.weapons.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Catalog.weapons[i].definition;
            return result;
        }
    }
}

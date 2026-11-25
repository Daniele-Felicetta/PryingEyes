using System.Collections.Generic;
using PryingEyes.Pista;
using UnityEngine;
using Production.Racing;

namespace PryingEyes.Deserto
{
    /// <summary>Una moto che si puo' avere nel deserto: la moto arrangiata di sempre (gratis, la prima del garage) e le nove della pista (BikeSpec, con il loro modello di Blender, la loro livrea e il loro
    /// motore), ognuna con i numeri della guida arcade del deserto (PlayerController.Ride.cs): velocita' massima e col turbo, ripresa, frenata, sterzo. I numeri non sono inventati uno per uno: nascono dalla
    /// scheda della moto della pista (potenza, peso, resistenza dell'aria, raggio di sterzata) con la stessa formula per tutte, cosi' una moto nuova della pista ha gia' i suoi numeri nel deserto. Le misure
    /// di posa (bacino, piedi, occhio, manopole) sono in metri nel sistema della moto (x a destra, y in alto, z avanti, origine a terra a meta' dell'interasse).</summary>
    public sealed class DesertBikeDef
    {
        public string id, name, klass, tagline;
        /// <summary>Quale moto della pista e' (indice in <see cref="Bikes"/>), -1 per la moto arrangiata, disegnata a codice (DesertBikeMesh).</summary>
        public int pista = -1;
        /// <summary>Lo stile del modello (0 superbike, 1 supersport, 2 naked) e la livrea (indice in <see cref="Livery"/>), come nella pista.</summary>
        public int style, skin;
        /// <summary>La grandezza del disegno rispetto alle altre (una 125 e' piccola, un'ipersportiva grande).</summary>
        public float scale = 1f;
        public EngineKind engine = EngineKind.Inline4;
        /// <summary>Il prezzo in punti (0: gratis, la moto con cui si comincia).</summary>
        public int price;
        /// <summary>Velocita' massima, col turbo e in retromarcia (m/s); moltiplicatori di ripresa, frenata e sterzo (1 = la moto arrangiata).</summary>
        public float top = 23f, boost = 31f, reverse = 5f, accel = 1f, brake = 1f, turn = 1f;
        /// <summary>Giri al minimo e alla linea rossa del motore: il suono sale da uno all'altro (la pista suona a rpm/4800).</summary>
        public float idleRpm = 1300f, redlineRpm = 12000f;
        /// <summary>Cavalli e chili (con il pilota) per la scheda del negozio: 0 se non si sanno (la moto arrangiata).</summary>
        public float hp, kg;

        // ---- la posa in sella (vedi SurvivorBody.PoseTorso/PoseLegs e PistaBikeMesh)
        /// <summary>Dove sta il bacino del pilota seduto, dove poggia i piedi (x e' il lato: si specchia) e dov'e' l'occhio; quanto si china in avanti (gradi). I valori della moto arrangiata sono quelli di sempre.</summary>
        public Vector3 seatHips = new Vector3(0f, 1.06f, -0.24f), pegs = new Vector3(0.20f, 0.45f, -0.15f), eye = new Vector3(0f, 1.40f, -0.28f);
        public float seatLean = 52f;
        /// <summary>La manopola destra nel sistema della moto (la sinistra e' lo specchio), per le braccia in prima persona delle moto della pista (quella arrangiata ha le sue in DesertBikeMesh).</summary>
        public Vector3 grip;

        public bool IsPista => pista >= 0;
        public bool Free => price <= 0;
        public bool Starter => pista < 0;
        public override string ToString() => name;

        /// <summary>Una barra della scheda del negozio, da 0 a 1 su una scala fissa (non contro le altre moto: una moto nuova non sposta le vecchie): velocita', ripresa, maneggevolezza, frenata.</summary>
        public float Bar(int which)
        {
            switch (which)
            {
                case 0: return Mathf.InverseLerp(21f, 34f, top);
                case 1: return Mathf.InverseLerp(0.95f, 1.45f, accel);
                case 2: return Mathf.InverseLerp(0.88f, 1.18f, turn);
                default: return Mathf.InverseLerp(0.90f, 1.25f, brake);
            }
        }

        public static readonly string[] BarNames = { "Velocita'", "Ripresa", "Maneggevolezza", "Frenata" };
    }

    /// <summary>Il catalogo delle moto del deserto, nell'ordine del negozio (la moto di casa, poi dalla piu' economica alla piu' cara). L'ordine non e' quello dei salvataggi: ogni moto si ricorda per il suo
    /// <see cref="DesertBikeDef.id"/>, cosi' se ne possono aggiungere o spostare senza toccare i garage dei giocatori.</summary>
    public static class DesertBikeDefs
    {
        /// <summary>La moto arrangiata di sempre: la prima del garage di ognuno, gratis.</summary>
        public const string StarterId = "ruggine";
        /// <summary>I punti che costa una moto: un quarto di quello che costa nella pista (i punti si fanno ammazzando, non girando), arrotondato a 25.</summary>
        public const float PriceShare = 0.25f;

        static List<DesertBikeDef> all;

        public static IReadOnlyList<DesertBikeDef> All { get { Ensure(); return all; } }
        public static int Count { get { Ensure(); return all.Count; } }
        public static DesertBikeDef Starter { get { Ensure(); return all[0]; } }

        public static DesertBikeDef Get(int i) { Ensure(); return all[Mathf.Clamp(i, 0, all.Count - 1)]; }

        /// <summary>La moto con quell'id, o null.</summary>
        public static DesertBikeDef ById(string id)
        {
            Ensure();
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in all) if (d.id == id) return d;
            return null;
        }

        public static int IndexOf(DesertBikeDef d) { Ensure(); return d == null ? -1 : all.IndexOf(d); }

        /// <summary>Il numero col quale la moto va in rete (0 e' la moto arrangiata): e' l'indice della moto della pista piu' uno, cosi' resta uguale anche se il negozio cambia ordine.</summary>
        public static int NetCode(DesertBikeDef d) => d == null || d.pista < 0 ? 0 : d.pista + 1;

        public static DesertBikeDef FromNet(int code) => code <= 0 ? Starter : ByPista(code - 1) ?? Starter;

        public static DesertBikeDef ByPista(int bike)
        {
            Ensure();
            foreach (var d in all) if (d.pista == bike) return d;
            return null;
        }

        static void Ensure()
        {
            if (all != null) return;
            var l = new List<DesertBikeDef> { Rust() };
            var sold = new List<DesertBikeDef>();
            for (int i = 0; i < Bikes.Count; i++) sold.Add(FromPista(i));
            sold.Sort((a, b) => a.price != b.price ? a.price.CompareTo(b.price) : a.pista.CompareTo(b.pista));
            l.AddRange(sold);
            all = l;
        }

        /// <summary>La moto di casa: i numeri della guida di sempre (DesertBike.TopSpeed e compagni), il disegno a codice.</summary>
        static DesertBikeDef Rust()
        {
            return new DesertBikeDef
            {
                id = StarterId, name = "Ruggine", klass = "Moto di fortuna", pista = -1, price = 0,
                tagline = "Telaio saldato a mano, due ruote e tanta sabbia nel carburatore: parte sempre. Quasi.",
                top = DesertBike.TopSpeed, boost = DesertBike.BoostSpeed, reverse = DesertBike.ReverseSpeed, accel = 1f, brake = 1f, turn = 1f,
                idleRpm = 1500f, redlineRpm = 8000f,
            };
        }

        // nell'ordine di Bikes (la stessa degli appunti del garage della pista, BikeSheet.Blurb)
        static readonly string[] Taglines =
        {
            "Il modello da cui tutte le altre hanno copiato. Quattro cilindri, tutta la strada davanti.",
            "Leggera, pronta, un po' timida in rettilineo. Si guida con le dita.",
            "Senza carena e senza vergogna: coppia da vendere ai medi e il vento in faccia.",
            "Due tempi da Gran Premio in formato tascabile: pesa come chi la guida e canta come una zanzara arrabbiata.",
            "Tre cilindri, un solo ululato. Agile e perdonante, la preferita di chi sbaglia spesso.",
            "Un bicilindrico a V che batte come un cuore: coppia da subito, impenna se la guardi male.",
            "La piu' veloce e la piu' pesante: lunga come un autobus, dritta come un fuso.",
            "Due tempi V4 da Gran Premio: tutta la potenza arriva insieme, e la moto non ti avvisa.",
            "Il motore di una superbike senza vestiti addosso: curve e rabbia, aria in faccia.",
        };

        /// <summary>Una moto della pista nel deserto. Tutto quello che e' un numero di gioco viene dalla sua scheda (BikeSpec): la velocita' massima da potenza e resistenza dell'aria (la formula della
        /// velocita' di un veicolo in piano), la ripresa dalla potenza per chilo, lo sterzo dal raggio di sterzata, la frenata dai freni.</summary>
        static DesertBikeDef FromPista(int bike)
        {
            var s = Bikes.Get(bike);
            float kw = s.PeakPowerKw(out _);
            // la velocita' massima della moto vera, in piano, rannicchiata dietro il cupolino (rendimento 0,9, aria a 1,2 kg/m3): da 61 m/s a quasi 90
            float vReal = Mathf.Pow(2f * kw * 1000f * 0.9f / (1.2f * Mathf.Max(0.2f, s.cdATuck)), 1f / 3f);
            float t = Mathf.Clamp01(Mathf.InverseLerp(55f, 87f, vReal));
            float pw = kw / s.Mass;
            // nel deserto la moto piu' lenta del negozio va un poco piu' forte di quella di casa (non si compra per andare piano) e la piu' veloce arriva a 119 km/h, 137 col turbo:
            // oltre, il terreno che si costruisce davanti non ce la farebbe a stare avanti (DesertTerrain.Ready)
            float top = Mathf.Lerp(24.5f, 33f, t);
            var d = new DesertBikeDef
            {
                id = s.id, name = s.name, klass = s.klass, pista = bike, style = s.meshStyle, skin = s.skin, scale = s.meshScale, engine = s.engine,
                tagline = Taglines[Mathf.Clamp(bike, 0, Taglines.Length - 1)],
                top = Round(top), boost = Round(Mathf.Min(top * 1.30f, 38f)), reverse = 5f,
                accel = Mathf.Lerp(1.0f, 1.45f, Mathf.InverseLerp(0.22f, 0.72f, pw)),
                brake = Mathf.Lerp(0.95f, 1.25f, Mathf.InverseLerp(700f, 1800f, s.brakeFront)),
                turn = Mathf.Lerp(1.18f, 0.88f, Mathf.InverseLerp(4.0f, 5.4f, s.minTurnRadius)),
                idleRpm = s.idleRpm, redlineRpm = s.redlineRpm,
                hp = kw * 1.36f, kg = s.Mass,
                price = Mathf.RoundToInt(Economy.Price(bike) * PriceShare / 25f) * 25,
            };
            // la posa in sella: con le semimanubri (superbike e supersport) il pilota e' piu' basso e piu' chino, sulla naked piu' dritto
            if (s.meshStyle < 2)
            {
                d.grip = new Vector3(0.30f, 0.905f, 0.40f);
                d.seatHips = new Vector3(0f, 0.98f, -0.27f); d.seatLean = 60f;
                d.pegs = new Vector3(0.20f, 0.42f, -0.18f);
                d.eye = new Vector3(0f, 1.34f, -0.10f);
            }
            else
            {
                d.grip = new Vector3(0.30f, 1.00f, 0.34f);
                d.seatHips = new Vector3(0f, 1.00f, -0.25f); d.seatLean = 42f;
                d.pegs = new Vector3(0.20f, 0.45f, -0.13f);
                d.eye = new Vector3(0f, 1.44f, -0.18f);
            }
            return d;
        }

        static float Round(float v) => Mathf.Round(v * 2f) * 0.5f;

        /// <summary>Che motore e' a parole, per la scheda del negozio.</summary>
        public static string EngineWords(EngineKind k)
        {
            switch (k)
            {
                case EngineKind.V2: return "bicilindrico a V";
                case EngineKind.Triple: return "tre cilindri";
                case EngineKind.TwoStroke125: return "monocilindrico a due tempi";
                case EngineKind.TwoStroke500: return "V4 a due tempi";
                default: return "quattro cilindri in linea";
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace PryingEyes.Proc.MapArt
{
    /// <summary>Quanto di una stanza e' noto a chi guarda la mappa.</summary>
    public enum RoomSeen : byte
    {
        /// <summary>Non si sa che esiste: sulla carta non c'e' niente.</summary>
        Hidden,
        /// <summary>Si sa che c'e' (e' oltre una porta di una stanza visitata) ma non si e' mai entrati: una macchia con un punto interrogativo.</summary>
        Rumor,
        /// <summary>Ci si e' entrati: pianta, arredi, nome.</summary>
        Seen,
    }

    /// <summary>Come sta una porta: aperta (area comprata o ciclo senza porte a pagamento), chiusa con una stanza gia' aperta da una parte (si puo'
    /// comprare), chiusa da tutte e due le parti.</summary>
    public enum DoorState : byte { Open, Sealed, SealedFar }

    public enum PoiKind : byte
    {
        Weapon, Biblioteca, Scrigno, Altare, Quadro, Candela, ArrivalLift, ExitLift, Drop, Lever, Barrel, Annex,
    }

    public enum PropClass : byte { Generic, Pew, Bed, Table, Chair, Column, Shelf, Sarcophagus, Altar, Statue, Cabinet, Piano, Machine, Tub }

    /// <summary>Lo stato dei sigilli che la mappa vuole mostrare: cosa c'e' sul piano, cosa si sa e cosa e' successo. E' riempito dal gioco
    /// (FloorMap) o, per le anteprime, da planlab.</summary>
    public sealed class MapScene
    {
        public Plan plan;
        public RoomSeen[] seen;
        public bool[] open;
        public int[] price;
        public DoorState[] door;
        public int[] doorPrice;
        public readonly List<MapProp> props = new List<MapProp>();
        public readonly List<MapPoi> pois = new List<MapPoi>();
        public readonly List<MapGallery> galleries = new List<MapGallery>();
        public readonly List<MapBlood> blood = new List<MapBlood>();
        /// <summary>Il ciclo a ondate (porte a pagamento): senza, ogni porta e' aperta e nessuna stanza ha un prezzo.</summary>
        public bool paidDoors;
        public bool powerOn = true;
        public int seed;

        public MapScene(Plan plan)
        {
            this.plan = plan;
            int n = plan.rooms.Count, m = plan.openings.Count;
            seen = new RoomSeen[n]; open = new bool[n]; price = new int[n];
            door = new DoorState[m]; doorPrice = new int[m];
            for (int i = 0; i < n; i++) open[i] = true;
            seed = plan.seed;
        }

        public bool IsSeen(int room) => room >= 0 && room < seen.Length && seen[room] == RoomSeen.Seen;
        public bool IsKnown(int room) => room >= 0 && room < seen.Length && seen[room] != RoomSeen.Hidden;
    }

    /// <summary>L'impronta a terra di un arredo solido (centro, misure, rotazione): l'ostacolo che il giocatore ha davanti.</summary>
    public struct MapProp
    {
        public float x, z, w, d, yaw;       // metri; yaw in radianti dall'asse x verso nord (la larghezza w e' lungo yaw)
        public PropClass cls;
        public int room;
    }

    public struct MapPoi
    {
        public PoiKind kind;
        public float x, z;
        public string label;                // nome dell'arma, "Biblioteca"...
        public int price;                   // 0 = niente prezzo
        public byte state;                  // dipende dal tipo: 0/1 (spento/acceso, sigillato/aperto, acquistato...)
        public int room;
        public byte tint;                   // per i potenziamenti: il tipo; per le armi: la classe
    }

    /// <summary>Il percorso di una balconata (la passerella al piano di sopra).</summary>
    public struct MapGallery
    {
        public int room;
        public V2[] points;
        public float height;
    }

    /// <summary>Una macchia di sangue sul pavimento: dove sono morti i nemici.</summary>
    public struct MapBlood
    {
        public float x, z, size;            // metri; size 0..1
        public int room;
    }

    public enum LabelKind : byte { Room, Role, Price, Poi, Note, Question }

    /// <summary>Un occhio che guarda dalla macchia di una stanza mai visitata: la vista lo disegna ogni fotogramma (sbatte le palpebre e segue il giocatore).</summary>
    public struct MapEye
    {
        public V2 pos;
        public float size;                  // larghezza in metri
        public int owner;
        public float phase;                 // sfasamento del battito di palpebre
    }

    public struct MapLabel
    {
        public V2 pos;
        public string text;
        public LabelKind kind;
        public float rot;                   // radianti
        public Col color;
        public int owner;                   // stanza (o -1): per far comparire l'etichetta con la sua stanza
    }

    /// <summary>Quello che il pittore ha disegnato per una parte della mappa: tre mesh (una per texture) e le etichette di testo.</summary>
    public sealed class Art
    {
        /// <summary>Acquerello: texture di macchia che si ripete (uv = metri x MapPainter.WashUv).</summary>
        public readonly Mesh2D wash = new Mesh2D();
        /// <summary>Inchiostro: la texture dei nastri (uv della penna).</summary>
        public readonly Mesh2D ink = new Mesh2D();
        /// <summary>Macchie: l'atlante di sangue, inchiostro e graffi.</summary>
        public readonly Mesh2D stain = new Mesh2D();
        public readonly List<MapLabel> labels = new List<MapLabel>();
        public readonly List<MapEye> eyes = new List<MapEye>();
        public void Clear() { wash.Clear(); ink.Clear(); stain.Clear(); labels.Clear(); eyes.Clear(); }
        public int Vertices => wash.Count + ink.Count + stain.Count;
    }

    /// <summary>I nomi delle stanze, in italiano (i RoomKind scritti per esteso, con gli spazi).</summary>
    public static class RoomNames
    {
        public static string Of(Room r)
        {
            switch (r.kind)
            {
                case RoomKind.SalaBallo: return "Sala da ballo";
                case RoomKind.SalaOperatoria: return "Sala operatoria";
                case RoomKind.SalaMedici: return "Sala dei medici";
                case RoomKind.GiardinoInverno: return "Giardino d'inverno";
                case RoomKind.SalaArmadi: return "Sala degli armadi";
                case RoomKind.Palcoscenico: return "Palcoscenico";
                case RoomKind.Ascensore: return "Ascensore";
                default: return r.kind.ToString();
            }
        }

        /// <summary>Il ruolo (RoomType) scritto per esteso, o "" per le stanze normali.</summary>
        public static string Role(RoomType t)
        {
            switch (t)
            {
                case RoomType.Partenza: return "Arrivo";
                case RoomType.Guardia: return "";
                case RoomType.Candela: return "Arena";
                case RoomType.Negozio: return "Negozio";
                case RoomType.Biblioteca: return "Biblioteca";
                case RoomType.Segreta: return "Segreta";
                case RoomType.Boss: return "Guardiano";
                default: return "";
            }
        }
    }
}

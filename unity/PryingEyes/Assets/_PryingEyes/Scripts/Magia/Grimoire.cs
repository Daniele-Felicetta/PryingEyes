using System;

namespace PryingEyes
{
    /// <summary>I due grimori della stanza segreta (2026-11-16, dalle due tavole dell'utente). Ognuno e' un dodecaedro di 19 nodi:
    /// dodici esterni (virtu' o sigilli minori, sempre acquistabili), sei interni (le magie della seconda mano o i sigilli che si
    /// tracciano sull'arma: ognuno chiede almeno uno dei due nodi esterni che gli stanno accanto) e il supremo al centro (chiede quattro
    /// dei sei interni). Indici: 0..11 esterni (in giro), 12..17 interni (l'interno k tocca gli esterni 2k e 2k+1), 18 il supremo.
    ///  - Libro della Stregoneria, "Perk del Dodecaedro Arcano Maledetto - Le 19 Virtu' della Stregoneria": gli interni sono le sei magie
    ///    della mano sinistra, il supremo e' l'Essenza Maledetta;
    ///  - Libro dell'Arsenale, "Potenziamenti delle Armi - I 19 Sigilli dell'Arsenale Maledetto": gli esterni migliorano tutte le armi,
    ///    gli interni sono i sigilli elementali (la mano sinistra li traccia sull'arma), il supremo e' l'Arma Maledetta.
    /// Pura logica: niente Unity, si prova da sola.</summary>
    public static class Grimoire
    {
        public enum Book { Stregoneria = 0, Arsenale = 1 }
        public enum Tier { Esterno, Interno, Supremo }

        public const int Count = 19, Outer = 12, Inner = 6, Supreme = 18, FirstInner = 12;
        /// <summary>Quanti interni servono per il supremo.</summary>
        public const int InnerForSupreme = 4;

        public sealed class Node
        {
            public int index;
            public string name, effect, lore;
            public Tier tier;
            public int icon;           // GrimoireIcons
        }

        static Node N(int i, string name, string effect, string lore, int icon) =>
            new Node { index = i, name = name, effect = effect, lore = lore, icon = icon, tier = i < 12 ? Tier.Esterno : i < 18 ? Tier.Interno : Tier.Supremo };

        // icone: vedi GrimoireIcons.Draw (un numero per disegno)
        public static readonly Node[] Witch =
        {
            N(0, "Lingua di Serpe", "le magie fanno il 25% di danno in più", "La serpe parla col veleno di chi l'ha nutrita.", 1),
            N(1, "Occhio del Corvo", "gittata e raggio delle magie +35%", "Il corvo vede la carcassa prima che cada.", 2),
            N(2, "Clessidra Rotta", "i tempi di recupero delle magie -30%", "Il tempo che hai rubato non torna indietro.", 3),
            N(3, "Gesto Rapido", "la mano compie i gesti il 25% più in fretta", "Le dita ricordano il rito prima della mente.", 4),
            N(4, "Luna Calante", "chi è colpito da una magia rallenta del 30% per 2 s", "La luna che cala trascina le maree e i passi.", 5),
            N(5, "Radici Cave", "fermo o accovacciato, l'essenza torna il doppio più in fretta", "L'albero cavo beve dal buio sotto la Torre.", 6),
            N(6, "Patto di Sangue", "se l'essenza non basta, paghi il resto con la vita", "Ogni goccia è una firma.", 7),
            N(7, "Mietitura", "ogni uccisione ti rende 5 di essenza in più", "La falce non chiede, raccoglie.", 8),
            N(8, "Eco del Rito", "una magia su cinque non costa essenza", "Il rito si ripete da solo, nelle pareti.", 9),
            N(9, "Pelle di Runa", "dopo una magia subisci il 35% di danno in meno per 2,5 s", "Le rune si accendono sotto la pelle.", 10),
            N(10, "Pozzo Profondo", "essenza massima +40", "Il pozzo non ha fondo, solo altre voci.", 11),
            N(11, "Sorgente Nera", "l'essenza torna il 60% più in fretta", "Dall'ampolla nera sgorga ciò che non dovrebbe.", 12),
            N(12, "Fiamma Sacrilega", "una sfera di fuoco esplode e incendia (30 di essenza)", "Il fuoco che non scalda: divora.", 20),
            N(13, "Catena del Fulmine", "un fulmine salta da un nemico all'altro e li stordisce (35)", "Il cielo della Torre è sotto il pavimento.", 21),
            N(14, "Morsa di Gelo", "un cono di gelo rallenta e congela (30)", "Il freddo delle cripte ha mani.", 22),
            N(15, "Sigillo di Sangue", "un filo di sangue prosciuga il nemico e ti cura (25)", "Ciò che scorre in loro ora scorre in te.", 23),
            N(16, "Passo d'Ombra", "uno schiocco e sei otto metri più avanti (30)", "L'ombra arriva prima del corpo.", 24),
            N(17, "Onda del Dodecaedro", "un'onda scaglia indietro chi ti sta davanti (35)", "Dodici facce, una sola spinta.", 25),
            N(18, "Essenza Maledetta", "per 12 s le magie non costano e non aspettano, +60% danno, ti rigeneri e bruci chi ti tocca", "Il nucleo del dodecaedro: ciò che resta quando la preghiera finisce.", 26),
        };

        public static readonly Node[] Arsenal =
        {
            N(0, "Mira del Corvo", "dispersione dei colpi -30%", "Il corvo non manca l'occhio.", 30),
            N(1, "Colpo di Grazia", "+40% di danno a chi ha meno di un terzo della vita", "Pietà è finire in fretta.", 31),
            N(2, "Perforazione", "i colpi attraversano un nemico in più", "Il piombo benedetto non si ferma alla carne.", 32),
            N(3, "Forza d'Impatto", "+15% di danno e i colpi spingono di più", "Ogni colpo è una porta sfondata.", 33),
            N(4, "Raffica Mortale", "cadenza di fuoco +12%", "Il rosario si sgrana più in fretta.", 34),
            N(5, "Detonazione", "un colpo su sedici esplode", "Il bossolo ricorda di essere stato polvere.", 35),
            N(6, "Munizioni Fantasma", "un colpo su otto non consuma munizioni", "Alcuni proiettili tornano nel caricatore.", 36),
            N(7, "Caricatore Esteso", "caricatori più capienti del 25%", "La fame dell'arma cresce con lei.", 37),
            N(8, "Maneggevolezza", "mira e ricarica più rapide del 20%", "L'arma diventa un'estensione del braccio.", 38),
            N(9, "Ricarica Arcana", "l'arma nella fondina si ricarica da sola", "Mani che non vedi riempiono il caricatore.", 39),
            N(10, "Stabilità Oscura", "rinculo -30%", "Qualcosa tiene ferma la canna insieme a te.", 40),
            N(11, "Clessidra di Piombo", "ogni uccisione allunga il sigillo sull'arma di 1,5 s", "La sabbia della clessidra è polvere da sparo.", 41),
            N(12, "Danni Critici", "un colpo su quattro è critico: danno x2,5", "L'occhio tracciato sull'arma sceglie dove ferire.", 50),
            N(13, "Sanguinamento", "i colpi aprono ferite che sanguinano", "Il sangue chiama sangue.", 51),
            N(14, "Incendiario", "i colpi incendiano", "La canna ricorda la forgia.", 52),
            N(15, "Corrosivo", "i colpi corrodono: chi è colpito subisce il 30% in più da tutto", "L'icore verde mangia il ferro e la pelle.", 53),
            N(16, "Elettrificato", "i colpi stordiscono e saltano a due nemici vicini", "Il caricatore ronza come un alveare.", 54),
            N(17, "Gelido", "i colpi rallentano; quattro colpi congelano", "La brina corre lungo la canna.", 55),
            N(18, "Arma Maledetta", "per 15 s danno doppio, tutti gli effetti insieme e munizioni infinite", "Il vero nome dell'arma, pronunciato una volta sola.", 56),
        };

        public static Node[] Of(Book b) => b == Book.Stregoneria ? Witch : Arsenal;

        public static string Title(Book b) => b == Book.Stregoneria ? "Perk del Dodecaedro Arcano Maledetto" : "Potenziamenti delle Armi";
        public static string Subtitle(Book b) => b == Book.Stregoneria ? "Le 19 Virtù della Stregoneria" : "I 19 Sigilli dell'Arsenale Maledetto";
        public static string TierName(Book b, Tier t)
        {
            if (b == Book.Stregoneria) return t == Tier.Esterno ? "Perk Esterni" : t == Tier.Interno ? "Perk Interni" : "Perk Supremo";
            return t == Tier.Esterno ? "Potenziamenti Esterni" : t == Tier.Interno ? "Potenziamenti Interni" : "Perk Supremo";
        }
        public static string TierText(Book b, Tier t)
        {
            if (b == Book.Stregoneria)
                return t == Tier.Esterno ? "Dodici virtù minori poste ai vertici del Dodecaedro. Potenziano e specializzano il tuo potere."
                     : t == Tier.Interno ? "Sei virtù maggiori, più profonde e trasformative: le magie della mano sinistra."
                     : "L'Essenza Maledetta, il nucleo definitivo del Dodecaedro, fonte del potere assoluto.";
            return t == Tier.Esterno ? "Dodici sigilli minori: valgono per ogni arma che impugni."
                 : t == Tier.Interno ? "Sei sigilli elementali: la mano sinistra li traccia sull'arma, che li porta per un tempo."
                 : "Arma Maledetta: sblocca il vero potere dell'arma per un tempo limitato.";
        }

        public static Tier TierOf(int i) => i < 12 ? Tier.Esterno : i < 18 ? Tier.Interno : Tier.Supremo;

        /// <summary>I nodi che toccano il nodo i nel disegno (anello esterno, raggi verso gli interni, interni verso il supremo).</summary>
        public static int[] Neighbors(int i)
        {
            if (i < 12)
            {
                int inner = 12 + i / 2;
                return new[] { (i + 11) % 12, (i + 1) % 12, inner };
            }
            if (i < 18) { int k = i - 12; return new[] { 2 * k, 2 * k + 1, 12 + (k + 5) % 6, 12 + (k + 1) % 6, Supreme }; }
            return new[] { 12, 13, 14, 15, 16, 17 };
        }

        public static bool Has(uint mask, int i) => i >= 0 && i < Count && (mask & (1u << i)) != 0;
        public static int CountTier(uint mask, Tier t)
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (Has(mask, i) && TierOf(i) == t) n++;
            return n;
        }

        /// <summary>Prezzo del nodo i con quel che hai gia' (punti): esterni 1250 + 250 per esterno gia' preso, interni 3000 + 500 per
        /// interno gia' preso, il supremo 9000.</summary>
        public static int Price(uint mask, int i)
        {
            var t = TierOf(i);
            if (t == Tier.Esterno) return 1250 + 250 * CountTier(mask, Tier.Esterno);
            if (t == Tier.Interno) return 3000 + 500 * CountTier(mask, Tier.Interno);
            return 9000;
        }

        public enum Lock { Libero, Posseduto, ServeEsterno, ServonoInterni }

        /// <summary>Si puo' comprare? Esterni sempre; un interno chiede uno dei due esterni accanto; il supremo quattro interni.</summary>
        public static Lock Status(uint mask, int i)
        {
            if (Has(mask, i)) return Lock.Posseduto;
            if (i < 12) return Lock.Libero;
            if (i < 18) { int k = i - 12; return Has(mask, 2 * k) || Has(mask, 2 * k + 1) ? Lock.Libero : Lock.ServeEsterno; }
            return CountTier(mask, Tier.Interno) >= InnerForSupreme ? Lock.Libero : Lock.ServonoInterni;
        }

        public static string LockText(Book b, uint mask, int i, Lock l)
        {
            switch (l)
            {
                case Lock.Posseduto: return "già tuo";
                case Lock.ServeEsterno:
                    int k = i - 12; var ns = Of(b);
                    return $"serve prima «{ns[2 * k].name}» o «{ns[2 * k + 1].name}»";
                case Lock.ServonoInterni: return $"servono quattro nodi interni (ne hai {CountTier(mask, Tier.Interno)})";
                default: return "";
            }
        }

        /// <summary>Il libro che esce in un piano: a caso ma uguale per tutti i giocatori (dal seme del piano).</summary>
        public static Book ForFloor(int planSeed, int floor)
        {
            unchecked
            {
                uint h = (uint)planSeed * 2654435761u ^ (uint)(floor * 40503) ^ 0x9E3779B9u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 1u) == 0 ? Book.Stregoneria : Book.Arsenale;
            }
        }
    }
}

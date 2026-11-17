using System;
using System.Collections.Generic;
using Production.Economy;

namespace PryingEyes.Cattedrale
{
    // Il catalogo della cattedrale e delle catacombe: risorse, rami, opere, mercato, bottino. Niente UnityEngine: lo compila anche
    // tools/economylab, che controlla i numeri (ogni risorsa si trova, ogni opera si raggiunge, quante spedizioni servono).

    public enum Grado { Comune, Rara, Boss, Moneta, Rifornimento }

    public sealed class RisorsaDef
    {
        public string id, nome, uno;      // "Ossa" e "osso": il nome di una quantita' e quello di un pezzo
        public Grado grado;
        public float r, g, b;             // il colore dell'icona, del bagliore a terra e della scritta
        public string descrizione;
    }

    /// <summary>Le risorse. Comuni: ossa, legno, acciaio. Rare: brace, cuori, sangue. Dei boss (una per ramo): reliquie, cristalli d'oscurita',
    /// cristalli di fede, gemme. Gli oboli sono la moneta del mercato (i punti che riporti); i rifornimenti sono quello che compri per la
    /// prossima spedizione.</summary>
    public static class Risorse
    {
        public const string Ossa = "ossa", Legno = "legno", Acciaio = "acciaio";
        public const string Brace = "brace", Cuori = "cuori", Sangue = "sangue";
        public const string Reliquie = "reliquie", Oscurita = "cristalli_oscurita", Fede = "cristalli_fede", Gemme = "gemme";
        public const string Oboli = "oboli";

        public static readonly RisorsaDef[] Tutte =
        {
            new RisorsaDef { id = Ossa, nome = "Ossa", uno = "osso", grado = Grado.Comune, r = 0.93f, g = 0.89f, b = 0.78f, descrizione = "Le ossa dei frati e dei pellegrini: la calce delle volte, l'ossario." },
            new RisorsaDef { id = Legno, nome = "Legno", uno = "legno", grado = Grado.Comune, r = 0.62f, g = 0.40f, b = 0.22f, descrizione = "Bastoni, rosari, schegge di banchi: travi, organo, confessionali." },
            new RisorsaDef { id = Acciaio, nome = "Acciaio", uno = "acciaio", grado = Grado.Comune, r = 0.70f, g = 0.75f, b = 0.82f, descrizione = "Chiavi, aghi, catene: cancelli, forgia, campane." },
            new RisorsaDef { id = Brace, nome = "Brace", uno = "brace", grado = Grado.Rara, r = 1.00f, g = 0.46f, b = 0.10f, descrizione = "Il fuoco che resta nelle Veglie: accende la forgia e le canne d'organo." },
            new RisorsaDef { id = Cuori, nome = "Cuori", uno = "cuore", grado = Grado.Rara, r = 0.92f, g = 0.14f, b = 0.22f, descrizione = "Cuori che battono ancora: gli altari li vogliono." },
            new RisorsaDef { id = Sangue, nome = "Sangue", uno = "sangue", grado = Grado.Rara, r = 0.62f, g = 0.03f, b = 0.06f, descrizione = "Sangue in ampolle: l'acqua della fonte, l'inchiostro dei voti." },
            new RisorsaDef { id = Reliquie, nome = "Reliquie", uno = "reliquia", grado = Grado.Boss, r = 1.00f, g = 0.76f, b = 0.30f, descrizione = "Del Cardinale, signore dell'Ossario." },
            new RisorsaDef { id = Oscurita, nome = "Cristalli d'oscurità", uno = "cristallo d'oscurità", grado = Grado.Boss, r = 0.52f, g = 0.22f, b = 1.00f, descrizione = "Intrisi del buio del Teatro sepolto: li porta il Direttore." },
            new RisorsaDef { id = Fede, nome = "Cristalli di fede", uno = "cristallo di fede", grado = Grado.Boss, r = 1.00f, g = 0.95f, b = 0.70f, descrizione = "Le preghiere dei malati, cristallizzate: le custodisce il Primario." },
            new RisorsaDef { id = Gemme, nome = "Gemme", uno = "gemma", grado = Grado.Boss, r = 0.30f, g = 1.00f, b = 0.78f, descrizione = "Il tesoro del Conte, in fondo al suo Guardaroba." },
            new RisorsaDef { id = Oboli, nome = "Oboli", uno = "obolo", grado = Grado.Moneta, r = 0.86f, g = 0.72f, b = 0.40f, descrizione = "I punti che riporti dalle catacombe. Si spendono al mercato." },
        };

        static readonly Dictionary<string, RisorsaDef> byId = Index();
        static Dictionary<string, RisorsaDef> Index()
        {
            var d = new Dictionary<string, RisorsaDef>(StringComparer.Ordinal);
            foreach (var r in Tutte) d[r.id] = r;
            foreach (var s in Rifornimenti.Tutti) d[s.id] = new RisorsaDef { id = s.id, nome = s.nome, uno = s.nome, grado = Grado.Rifornimento, r = 0.80f, g = 0.86f, b = 0.95f, descrizione = s.effetto };
            return d;
        }

        public static RisorsaDef Of(string id) => id != null && byId.TryGetValue(id, out var r) ? r : null;
        public static string Nome(string id) => Of(id)?.nome ?? id;
        public static bool IsMaterial(string id) { var r = Of(id); return r != null && r.grado <= Grado.Boss; }

        /// <summary>Quanta parte di una risorsa portata si salva se muori: meta' delle comuni (tutte con l'Ossario), niente delle rare (meta'
        /// con l'Ossario). Le risorse dei boss non si portano: vanno subito nella cattedrale.</summary>
        public static double Tenuta(string id, bool ossario)
        {
            var r = Of(id);
            if (r == null) return 0;
            switch (r.grado)
            {
                case Grado.Comune: return ossario ? 1.0 : 0.5;
                case Grado.Rara: return ossario ? 0.5 : 0.0;
                default: return 1.0;
            }
        }

        /// <summary>"12 Ossa, 3 Brace" per i messaggi.</summary>
        public static string Testo(Bundle b, string vuoto = "niente")
        {
            if (b == null || b.IsEmpty) return vuoto;
            var parts = new List<string>();
            foreach (var r in Tutte) if (b[r.id] > 0) parts.Add(b[r.id] + " " + r.nome);
            foreach (var e in b) if (Of(e.Key) == null || Of(e.Key).grado == Grado.Rifornimento) parts.Add(e.Value + " " + Nome(e.Key));
            return string.Join(", ", parts);
        }
    }

    // ---------------------------------------------------------------- i rami delle catacombe
    public sealed class RamoDef
    {
        public int indice;
        public string nome, sottotitolo;
        public int tema;                  // il valore di PryingEyes.Proc.Theme (0 albergo e chiesa, 1 teatro, 2 sanatorio, 3 guardaroba; -1 a caso)
        public string bossTipo;           // il nome di Enemy.EnemyType del boss
        public string bossTitolo;
        public string esclusiva;          // la risorsa del boss
        public string cancello;           // l'opera che apre il ramo (null: aperto dall'inizio)
    }

    /// <summary>Le catacombe sotto la cattedrale: quattro rami, ognuno di tre livelli, l'ultimo con il suo boss; l'Abisso (senza fondo) si apre
    /// quando la cattedrale e' consacrata. Il piano 'globale' (quello che scala la difficolta' del gioco) e' ramo * 3 + livello: l'Ossario
    /// tiene i piani 1-3, il Teatro 4-6, il Sanatorio 7-9, il Guardaroba 10-12, l'Abisso dal 13.</summary>
    public static class Rami
    {
        public const int LivelliPerRamo = 3;
        public const int Abisso = 4;

        public static readonly RamoDef[] Tutti =
        {
            new RamoDef { indice = 0, nome = "L'Ossario", sottotitolo = "L'albergo e la chiesa sepolti sotto la cattedrale", tema = 0, bossTipo = "Cardinale", bossTitolo = "Il Cardinale", esclusiva = Risorse.Reliquie },
            new RamoDef { indice = 1, nome = "Il Teatro sepolto", sottotitolo = "La sala, il palcoscenico e le quinte, sotto terra", tema = 1, bossTipo = "Direttore", bossTitolo = "Il Direttore", esclusiva = Risorse.Oscurita, cancello = "cancello_teatro" },
            new RamoDef { indice = 2, nome = "Il Sanatorio", sottotitolo = "Le corsie dove nessuno guarisce", tema = 2, bossTipo = "Primario", bossTitolo = "Il Primario", esclusiva = Risorse.Fede, cancello = "cancello_sanatorio" },
            new RamoDef { indice = 3, nome = "Il Guardaroba del Conte", sottotitolo = "Armadi, bare e lava: il castello sotto il castello", tema = 3, bossTipo = "Cardinale", bossTitolo = "Il Conte", esclusiva = Risorse.Gemme, cancello = "cancello_conte" },
            new RamoDef { indice = 4, nome = "L'Abisso", sottotitolo = "Le catacombe senza fondo", tema = -1, bossTipo = "Cardinale", bossTitolo = "Il Custode dell'Abisso", esclusiva = null, cancello = "rosone" },
        };

        public static int Piano(int ramo, int livello) => ramo * LivelliPerRamo + livello;
        /// <summary>Il ramo di un piano globale (l'Abisso tiene tutto cio' che sta sotto il dodicesimo).</summary>
        public static int RamoDi(int piano) => piano < 1 ? 0 : Math.Min(Abisso, (piano - 1) / LivelliPerRamo);
        public static int LivelloDi(int piano) => piano < 1 ? 1 : piano - RamoDi(piano) * LivelliPerRamo;
        /// <summary>Ogni terzo livello c'e' il boss (anche nell'Abisso, ogni tre).</summary>
        public static bool LivelloDelBoss(int piano) => piano >= 1 && piano % LivelliPerRamo == 0;

        /// <summary>Il boss dell'Abisso cambia ogni tre livelli: i quattro a turno.</summary>
        public static RamoDef BossDi(int piano)
        {
            int r = RamoDi(piano);
            if (r < Abisso) return Tutti[r];
            return Tutti[((piano - 1) / LivelliPerRamo) % 4];
        }

        /// <summary>La risorsa del boss di un piano (nell'Abisso, quella del boss di turno).</summary>
        public static string EsclusivaDi(int piano) => BossDi(piano).esclusiva;

        public static bool Aperto(int ramo, Func<string, bool> costruita) => Tutti[ramo].cancello == null || costruita(Tutti[ramo].cancello);

        public static string NomeLivello(int piano)
        {
            int r = RamoDi(piano);
            string[] num = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            int l = LivelloDi(piano);
            return Tutti[r].nome + " · livello " + (l <= num.Length ? num[l - 1] : l.ToString());
        }
    }

    // ---------------------------------------------------------------- le opere della cattedrale
    public sealed class OperaDef
    {
        public string id, nome, luogo, effetto;
        public Bundle costo;
        public string[] richiede = new string[0];
    }

    /// <summary>Le venti opere. Ognuna ha un posto nella cattedrale (un cantiere finche' non c'e'), un costo e un effetto sulle spedizioni.</summary>
    public static class Opere
    {
        static Bundle B(params (string, int)[] items) => Bundle.Of(items);

        public static readonly OperaDef[] Tutte =
        {
            // le fondamenta: solo risorse comuni
            new OperaDef { id = "volte", nome = "Le Volte della navata", luogo = "Navata", costo = B((Risorse.Legno, 40), (Risorse.Acciaio, 20)),
                effetto = "La navata torna ad avere un tetto e i fedeli tornano: +250 punti all'inizio di ogni spedizione." },
            new OperaDef { id = "fonte", nome = "La Fonte battesimale", luogo = "Nartece", costo = B((Risorse.Ossa, 15), (Risorse.Acciaio, 15), (Risorse.Sangue, 2)),
                effetto = "Un'acqua santa in più: a ogni livello delle catacombe ne hai almeno tre." },
            new OperaDef { id = "sacrestia", nome = "La Sacrestia", luogo = "Sacrestia", costo = B((Risorse.Legno, 30), (Risorse.Acciaio, 25)),
                effetto = "La rastrelliera della sacrestia: prima di scendere scegli un'arma migliore della pistola." },
            new OperaDef { id = "ossario", nome = "L'Ossario", luogo = "Cripta", costo = B((Risorse.Ossa, 60), (Risorse.Legno, 10)),
                effetto = "Le ossa ricordano: se muori conservi tutte le risorse comuni e metà delle rare." },
            // il cuore della chiesa: servono le risorse rare
            new OperaDef { id = "altare", nome = "L'Altare maggiore", luogo = "Presbiterio", costo = B((Risorse.Ossa, 30), (Risorse.Legno, 20), (Risorse.Cuori, 3)), richiede = new[] { "volte" },
                effetto = "Prima di ogni spedizione accendi uno dei tre ceri dell'altare: una benedizione per tutta la discesa." },
            new OperaDef { id = "organo", nome = "L'Organo", luogo = "Navata", costo = B((Risorse.Legno, 50), (Risorse.Acciaio, 30), (Risorse.Brace, 4)), richiede = new[] { "volte" },
                effetto = "L'inno: ogni ondata superata ti cura del tutto e porta lo scudo a 100." },
            new OperaDef { id = "confessionale", nome = "Il Confessionale", luogo = "Navata", costo = B((Risorse.Legno, 25), (Risorse.Sangue, 4)), richiede = new[] { "volte" },
                effetto = "Il perdono: se muori, i punti che avevi valgono comunque un obolo ogni 20." },
            new OperaDef { id = "forgia", nome = "La Forgia", luogo = "Sacrestia", costo = B((Risorse.Acciaio, 50), (Risorse.Legno, 15), (Risorse.Brace, 6)), richiede = new[] { "sacrestia" },
                effetto = "La tempra: +15% di danno con tutte le armi." },
            new OperaDef { id = "tesoro", nome = "Il Tesoro", luogo = "Sacrestia", costo = B((Risorse.Acciaio, 30), (Risorse.Ossa, 30), (Risorse.Cuori, 2)), richiede = new[] { "sacrestia" },
                effetto = "I punti che riporti valgono di più: un obolo ogni 6 punti invece che ogni 10." },
            // i rami: ognuno chiede il trofeo del boss di quello prima
            new OperaDef { id = "cancello_teatro", nome = "Il Cancello del Teatro", luogo = "Cripta", costo = B((Risorse.Acciaio, 30), (Risorse.Ossa, 30), (Risorse.Reliquie, 1)),
                effetto = "Apre il secondo ramo delle catacombe: il Teatro sepolto e il suo Direttore." },
            new OperaDef { id = "cancello_sanatorio", nome = "Il Cancello del Sanatorio", luogo = "Cripta", costo = B((Risorse.Acciaio, 45), (Risorse.Legno, 30), (Risorse.Brace, 3), (Risorse.Oscurita, 1)), richiede = new[] { "cancello_teatro" },
                effetto = "Apre il terzo ramo: il Sanatorio e il suo Primario." },
            new OperaDef { id = "cancello_conte", nome = "Il Cancello del Conte", luogo = "Cripta", costo = B((Risorse.Acciaio, 60), (Risorse.Sangue, 5), (Risorse.Fede, 1)), richiede = new[] { "cancello_sanatorio" },
                effetto = "Apre il quarto ramo: il Guardaroba del Conte." },
            new OperaDef { id = "campanile", nome = "Il Campanile", luogo = "Campanile", costo = B((Risorse.Acciaio, 40), (Risorse.Legno, 30), (Risorse.Brace, 5), (Risorse.Reliquie, 1)), richiede = new[] { "volte" },
                effetto = "La campanella del ritorno: una volta per spedizione, dal menu di pausa, torni alla cattedrale con tutto il bottino." },
            new OperaDef { id = "cappella_pietra", nome = "La Cappella della Pietra", luogo = "Navata", costo = B((Risorse.Ossa, 40), (Risorse.Cuori, 4), (Risorse.Reliquie, 2)), richiede = new[] { "altare" },
                effetto = "Scendi già con Pelle di pietra (+50% di vita); occupa uno dei posti dei perk." },
            new OperaDef { id = "cappella_mani", nome = "La Cappella delle Mani", luogo = "Navata", costo = B((Risorse.Legno, 40), (Risorse.Brace, 4), (Risorse.Oscurita, 2)), richiede = new[] { "altare" },
                effetto = "Scendi già con Mani svelte (ricarica più rapida); occupa uno dei posti dei perk." },
            // le vetrate: il trofeo di ogni boss, alla luce
            new OperaDef { id = "vetrata_cardinale", nome = "La Vetrata del Cardinale", luogo = "Navata", costo = B((Risorse.Reliquie, 3), (Risorse.Legno, 20)), richiede = new[] { "volte" },
                effetto = "+25% di danno ai boss." },
            new OperaDef { id = "vetrata_direttore", nome = "La Vetrata del Direttore", luogo = "Navata", costo = B((Risorse.Oscurita, 3), (Risorse.Acciaio, 20)), richiede = new[] { "volte" },
                effetto = "Le porte delle aree costano il 25% in meno." },
            new OperaDef { id = "vetrata_primario", nome = "La Vetrata del Primario", luogo = "Navata", costo = B((Risorse.Fede, 3), (Risorse.Ossa, 20)), richiede = new[] { "volte" },
                effetto = "+25 di vita massima." },
            new OperaDef { id = "vetrata_conte", nome = "La Vetrata del Conte", luogo = "Navata", costo = B((Risorse.Gemme, 3), (Risorse.Sangue, 5)), richiede = new[] { "volte" },
                effetto = "I libri dei perk costano il 40% in meno." },
            new OperaDef { id = "rosone", nome = "Il Rosone", luogo = "Facciata",
                costo = B((Risorse.Reliquie, 3), (Risorse.Oscurita, 3), (Risorse.Fede, 3), (Risorse.Gemme, 3), (Risorse.Ossa, 100), (Risorse.Legno, 100), (Risorse.Acciaio, 100)),
                richiede = new[] { "vetrata_cardinale", "vetrata_direttore", "vetrata_primario", "vetrata_conte" },
                effetto = "La cattedrale è consacrata. Sotto la cripta si apre l'Abisso: le catacombe senza fondo." },
        };

        static readonly Dictionary<string, OperaDef> byId = Index();
        static Dictionary<string, OperaDef> Index()
        {
            var d = new Dictionary<string, OperaDef>(StringComparer.Ordinal);
            foreach (var o in Tutte) d[o.id] = o;
            return d;
        }
        public static OperaDef Of(string id) => id != null && byId.TryGetValue(id, out var o) ? o : null;

        public static WorkTree Albero()
        {
            var list = new List<WorkTree.Work>();
            foreach (var o in Tutte) list.Add(new WorkTree.Work(o.id, o.costo, o.richiede));
            return new WorkTree(list);
        }
    }

    // ---------------------------------------------------------------- benedizioni dell'altare
    public sealed class BenedizioneDef { public string id, nome, effetto; }

    public static class Benedizioni
    {
        public static readonly BenedizioneDef[] Tutte =
        {
            new BenedizioneDef { id = "furia", nome = "Furia", effetto = "+20% di danno" },
            new BenedizioneDef { id = "grazia", nome = "Grazia", effetto = "+30 di vita massima" },
            new BenedizioneDef { id = "raccolto", nome = "Raccolto", effetto = "il bottino dei nemici vale una volta e mezza" },
            new BenedizioneDef { id = "provvidenza", nome = "Provvidenza", effetto = "i potenziamenti cadono il doppio" },
            new BenedizioneDef { id = "decima", nome = "Decima", effetto = "+25% di punti dalle uccisioni" },
            new BenedizioneDef { id = "porte", nome = "Porte d'oro", effetto = "le porte costano il 30% in meno" },
        };
        public static BenedizioneDef Of(string id) { foreach (var b in Tutte) if (b.id == id) return b; return null; }

        /// <summary>Tre benedizioni diverse, scelte da 'uniform' (deterministico con lo stesso seme).</summary>
        public static string[] TreA(Func<double> uniform)
        {
            var ids = new List<string>(); foreach (var b in Tutte) ids.Add(b.id);
            for (int i = ids.Count - 1; i > 0; i--) { int j = (int)(uniform() * (i + 1)); if (j > i) j = i; (ids[i], ids[j]) = (ids[j], ids[i]); }
            return new[] { ids[0], ids[1], ids[2] };
        }
    }

    // ---------------------------------------------------------------- rifornimenti e mercato
    public sealed class RifornimentoDef { public string id, nome, effetto; }

    public static class Rifornimenti
    {
        public const string Acquasanta = "rif_acquasanta", Carillon = "rif_carillon", Mina = "rif_mina", Lettera = "rif_lettera", Cero = "rif_cero", Mappa = "rif_mappa";
        public static readonly RifornimentoDef[] Tutti =
        {
            new RifornimentoDef { id = Acquasanta, nome = "Ampolla d'acqua santa", effetto = "un'acqua santa in più alla partenza" },
            new RifornimentoDef { id = Carillon, nome = "Carillon", effetto = "un carillon in più alla partenza" },
            new RifornimentoDef { id = Mina, nome = "Mina del reliquiario", effetto = "una mina in più alla partenza" },
            new RifornimentoDef { id = Lettera, nome = "Lettera di credito", effetto = "+500 punti alla partenza" },
            new RifornimentoDef { id = Cero, nome = "Cero votivo", effetto = "scendi con il libro della Rinascita" },
            new RifornimentoDef { id = Mappa, nome = "Mappa del cartografo", effetto = "le piante di tutti i livelli della spedizione" },
        };
        public static bool Is(string id) => id != null && id.StartsWith("rif_", StringComparison.Ordinal);
    }

    public sealed class BancoDef { public string id, nome, mercante; public string[] offerte; }

    /// <summary>Il listino del mercato sul sagrato: quattro banchi. Si paga in oboli; le scorte tornano piene a ogni ritorno dalle catacombe. I mercanti
    /// comprano e vendono le risorse comuni e rare; quelle dei boss no.</summary>
    public static class Listino
    {
        static Market.Offer Compra(string id, int oboli, string cosa, int quanti, int scorta) =>
            new Market.Offer(id, Bundle.One(Risorse.Oboli, oboli), Bundle.One(cosa, quanti), scorta);
        static Market.Offer Vendi(string id, string cosa, int quanti, int oboli) =>
            new Market.Offer(id, Bundle.One(cosa, quanti), Bundle.One(Risorse.Oboli, oboli));

        public static readonly Market.Offer[] Offerte =
        {
            Compra("rif_acquasanta", 30, Rifornimenti.Acquasanta, 1, 3),
            Compra("rif_carillon", 25, Rifornimenti.Carillon, 1, 2),
            Compra("rif_mina", 45, Rifornimenti.Mina, 1, 2),
            Compra("rif_lettera", 60, Rifornimenti.Lettera, 1, 2),
            Compra("rif_cero", 120, Rifornimenti.Cero, 1, 1),
            Compra("rif_mappa", 20, Rifornimenti.Mappa, 1, 1),
            Compra("compra_ossa", 25, Risorse.Ossa, 10, 3),
            Compra("compra_legno", 30, Risorse.Legno, 10, 3),
            Compra("compra_acciaio", 40, Risorse.Acciaio, 10, 3),
            Compra("compra_brace", 35, Risorse.Brace, 1, 2),
            Compra("compra_cuori", 40, Risorse.Cuori, 1, 2),
            Compra("compra_sangue", 30, Risorse.Sangue, 1, 2),
            Vendi("vendi_ossa", Risorse.Ossa, 10, 8),
            Vendi("vendi_legno", Risorse.Legno, 10, 10),
            Vendi("vendi_acciaio", Risorse.Acciaio, 10, 12),
            Vendi("vendi_brace", Risorse.Brace, 1, 10),
            Vendi("vendi_cuori", Risorse.Cuori, 1, 12),
            Vendi("vendi_sangue", Risorse.Sangue, 1, 9),
        };

        public static readonly BancoDef[] Banchi =
        {
            new BancoDef { id = "speziale", nome = "La bottega della speziale", mercante = "La speziale", offerte = new[] { "rif_acquasanta", "rif_cero", "rif_mappa" } },
            new BancoDef { id = "armaiolo", nome = "Il banco dell'armaiolo", mercante = "L'armaiolo", offerte = new[] { "rif_mina", "rif_carillon", "rif_lettera" } },
            new BancoDef { id = "ossaio", nome = "Il carro dell'ossaio", mercante = "L'ossaio", offerte = new[] { "compra_ossa", "compra_legno", "compra_acciaio", "vendi_ossa", "vendi_legno", "vendi_acciaio" } },
            new BancoDef { id = "cerusico", nome = "La tenda del cerusico", mercante = "Il cerusico", offerte = new[] { "compra_brace", "compra_cuori", "compra_sangue", "vendi_brace", "vendi_cuori", "vendi_sangue" } },
        };

        public static Market Nuovo() => new Market(Offerte);

        /// <summary>Gli oboli che valgono i punti riportati: uno ogni 10 (ogni 6 con il Tesoro). Morendo, uno ogni 20 con il Confessionale.</summary>
        public static int OboliDaPunti(int punti, int ogni) => ogni <= 0 || punti <= 0 ? 0 : punti / ogni;

        /// <summary>La cassetta delle elemosine nei livelli: versi 500 punti e diventano subito oboli, uno ogni 15 (al sicuro anche se muori).</summary>
        public const int Elemosina = 500, ElemosinaOgni = 15;
    }

    // ---------------------------------------------------------------- bottino dei nemici
    /// <summary>Cosa lascia ogni nemico: una tabella delle comuni e una delle rare, per nome del tipo (Enemy.EnemyType). Piu' in fondo
    /// (ramo piu' alto) le quantita' crescono del 15% a ramo; il coltello raddoppia la probabilita' delle comuni (il raccolto attento).</summary>
    public static class Bottino
    {
        static LootTable.Entry E(string id, double w, int min = 1, int max = 1) => new LootTable.Entry(id, w, min, max);
        static LootTable T(double chance, params LootTable.Entry[] e) => new LootTable(chance, 1, e);

        sealed class Coppia { public LootTable comuni, rare; }

        static readonly Dictionary<string, Coppia> tabelle = new Dictionary<string, Coppia>(StringComparer.Ordinal)
        {
            ["Monaco"] = new Coppia { comuni = T(0.22, E(Risorse.Ossa, 6, 1, 2), E(Risorse.Legno, 4, 1, 2)), rare = T(0.025, E(Risorse.Sangue, 5), E(Risorse.Cuori, 3), E(Risorse.Brace, 2)) },
            ["Abate"] = new Coppia { comuni = T(0.50, E(Risorse.Ossa, 5, 2, 3), E(Risorse.Acciaio, 5, 2, 3)), rare = T(0.09, E(Risorse.Cuori, 6), E(Risorse.Sangue, 4)) },
            ["Occhio"] = new Coppia { comuni = T(0.15, E(Risorse.Ossa, 1, 1, 2)), rare = T(0.04, E(Risorse.Sangue, 7), E(Risorse.Cuori, 3)) },
            ["Veglia"] = new Coppia { comuni = T(0.16, E(Risorse.Legno, 7, 1, 2), E(Risorse.Ossa, 3)), rare = T(0.05, E(Risorse.Brace, 1)) },
            ["Medusa"] = new Coppia { comuni = T(0.18, E(Risorse.Ossa, 1, 1, 2)), rare = T(0.06, E(Risorse.Sangue, 1)) },
            ["Portachiavi"] = new Coppia { comuni = T(0.55, E(Risorse.Acciaio, 1, 2, 3)), rare = T(0.04, E(Risorse.Brace, 1)) },
            ["Maschera"] = new Coppia { comuni = T(0.24, E(Risorse.Legno, 6, 1, 2), E(Risorse.Ossa, 4)), rare = T(0.03, E(Risorse.Brace, 6), E(Risorse.Sangue, 4)) },
            ["Direttore"] = new Coppia { comuni = T(0.50, E(Risorse.Acciaio, 6, 2, 3), E(Risorse.Legno, 4, 2, 3)), rare = T(0.09, E(Risorse.Cuori, 6), E(Risorse.Brace, 4)) },
            ["Infermiere"] = new Coppia { comuni = T(0.24, E(Risorse.Acciaio, 5, 1, 2), E(Risorse.Ossa, 5)), rare = T(0.04, E(Risorse.Sangue, 1)) },
            ["Primario"] = new Coppia { comuni = T(0.50, E(Risorse.Acciaio, 6, 2, 3), E(Risorse.Ossa, 4, 2, 3)), rare = T(0.09, E(Risorse.Cuori, 6), E(Risorse.Sangue, 4)) },
            ["Appestato"] = new Coppia { comuni = T(0.26, E(Risorse.Ossa, 1, 1, 2)), rare = T(0.06, E(Risorse.Sangue, 7), E(Risorse.Cuori, 3)) },
            ["Flagellante"] = new Coppia { comuni = T(0.26, E(Risorse.Legno, 5, 1, 2), E(Risorse.Ossa, 5)), rare = T(0.06, E(Risorse.Sangue, 1)) },
            ["Statua"] = new Coppia { comuni = T(0.75, E(Risorse.Acciaio, 5, 3, 5), E(Risorse.Ossa, 5, 3, 5)), rare = T(0.15, E(Risorse.Brace, 5), E(Risorse.Cuori, 5)) },
            ["Cardinale"] = new Coppia { comuni = T(1.0, E(Risorse.Ossa, 3, 6, 10), E(Risorse.Legno, 3, 6, 10), E(Risorse.Acciaio, 3, 6, 10)), rare = T(1.0, E(Risorse.Brace, 1, 1, 2), E(Risorse.Cuori, 1, 1, 2), E(Risorse.Sangue, 1, 1, 2)) },
        };
        static readonly Coppia nessuno = new Coppia { comuni = T(0.12, E(Risorse.Ossa, 1)), rare = T(0.02, E(Risorse.Sangue, 1)) };

        public static bool Conosciuto(string tipo) => tipo != null && tabelle.ContainsKey(tipo);
        public static IEnumerable<string> Tipi => tabelle.Keys;

        /// <summary>Il bottino di un'uccisione. 'ramo' 0..4 (le quantita' crescono in fondo), 'coltello' raddoppia la probabilita' delle comuni,
        /// 'mul' moltiplica le quantita' (la benedizione del Raccolto).</summary>
        public static Bundle Uccisione(string tipo, int ramo, bool coltello, double mul, Func<double> uniform)
        {
            var c = tipo != null && tabelle.TryGetValue(tipo, out var found) ? found : nessuno;
            double qty = (1 + 0.15 * Math.Max(0, Math.Min(Rami.Abisso, ramo))) * Math.Max(0, mul);
            var a = c.comuni.Roll(uniform, coltello ? 2 : 1, qty);
            var b = c.rare.Roll(uniform, 1, qty);
            return a.Plus(b);
        }

        /// <summary>Il trofeo del boss di un ramo: la sua risorsa (tre la prima volta, poi due) piu' comuni e rare, sempre.</summary>
        public static Bundle Boss(int piano, bool primaVolta, Func<double> uniform)
        {
            var ramo = Rami.BossDi(piano);
            int r = Rami.RamoDi(piano);
            double qty = 1 + 0.15 * r;
            var comuni = new LootTable(1, 3, new[] { E(Risorse.Ossa, 1, 4, 6), E(Risorse.Legno, 1, 4, 6), E(Risorse.Acciaio, 1, 4, 6) }).Roll(uniform, 1, qty);
            var rare = new LootTable(1, 3, new[] { E(Risorse.Brace, 1), E(Risorse.Cuori, 1), E(Risorse.Sangue, 1) }).Roll(uniform, 1, qty);
            var esclusiva = ramo.esclusiva == null ? Bundle.Empty : Bundle.One(ramo.esclusiva, primaVolta ? 3 : 2);
            return comuni.Plus(rare).Plus(esclusiva);
        }

        /// <summary>La media di una risorsa per uccisione di un tipo (per il laboratorio e per tarare).</summary>
        public static double Atteso(string tipo, string risorsa)
        {
            var c = tipo != null && tabelle.TryGetValue(tipo, out var found) ? found : nessuno;
            return c.comuni.Expected(risorsa) + c.rare.Expected(risorsa);
        }
    }
}

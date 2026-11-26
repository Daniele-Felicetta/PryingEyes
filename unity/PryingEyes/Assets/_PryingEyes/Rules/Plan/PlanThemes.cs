using System;
using System.Collections.Generic;

namespace PryingEyes.Proc
{
    /// <summary>I temi oltre l'albergo e la chiesa (che restano in PlanGen.cs). Teatro: la sala (platea con palchi e foyer) e il
    /// palcoscenico con i camerini, uniti dall'arco scenico. Sanatorio: l'istituto (accettazione, corsie, celle e servizi) con la
    /// cappella dell'istituto, come l'albergo con la chiesa. Ogni tema compone le sue ali con la stessa tecnica del piano.</summary>
    public static partial class PlanGen
    {
        // ------------------------------------------------------------------ tema per piano
        /// <summary>Forza il tema di tutti i piani a pianta a griglia (prove, menu, -theme): null = estrazione normale. Il generatore
        /// vecchio (-oldplan) non sa comporre il Guardaroba e lo legge come albergo.</summary>
        public static Theme? ForceTheme;
        /// <summary>Il tema del ramo delle catacombe (lo imposta FloorDirector a ogni livello; null nell'Abisso e nella Torre di prima):
        /// conta dopo ForceTheme e prima delle estrazioni, che restano le stesse (la pianta del seme non cambia, solo il tema).</summary>
        public static Theme? RamoTheme;

        /// <summary>Su cento piani dal terzo in su, quanti sono il Guardaroba. 0 (il predefinito) lo spegne: i piani sono quelli di prima, sullo stesso
        /// seme, e la cooperativa non cambia. Si accende con <c>-guardaroba N</c> (ThemeFlag.cs; con la cooperativa lo stesso N su ogni
        /// macchina: il piano si ricava dal seme) o si forza con <c>-theme guardaroba</c>. Il tema e' fatto ma non e' ancora stato giocato.</summary>
        public static int GuardarobaPercent = 0;

        /// <summary>Piani 1 e 2: solo albergo e chiesa, per imparare il gioco. Dal terzo piano il tema viene estratto a pari peso tra
        /// albergo, teatro e sanatorio con lo stesso Rng di sempre (cosi' il resto del piano non cambia); il Guardaroba e' una seconda
        /// estrazione, indipendente, che sostituisce quel tema in GuardarobaPercent piani su cento (nessuno, di default).</summary>
        static Theme PickTheme(int floor, Rng r, int seed = 0, bool grid = false)
        {
            Theme t = floor <= 2 ? Theme.Albergo : (Theme)r.Range(0, 3);
            if (!grid) return t;
            if (ForceTheme.HasValue) return ForceTheme.Value;
            if (RamoTheme.HasValue) return RamoTheme.Value;
            if (floor >= 3 && GuardarobaPercent > 0 && new Rng(unchecked(seed * 211 + floor * 7919 + GuardarobaSalt)).Range(0, 100) < GuardarobaPercent)
                return Theme.Guardaroba;
            return t;
        }

        /// <summary>Il sale della seconda estrazione: scelto con 'planlab themes scan' perche' i semi delle prove storiche (tutti dal terzo
        /// piano in su) mantengano il tema che avevano.</summary>
        public static int GuardarobaSalt = 66;

        /// <summary>Il tema che il piano (seme, piano) avra' con le impostazioni correnti: per i laboratori e le prove.</summary>
        public static Theme ThemeFor(int seed, int floor) => PickTheme(floor, new Rng(unchecked(seed * 73 + floor * 1009 + 5)), seed, UseGrid);

        // ------------------------------------------------------------------ funzioni delle stanze di un'ala d'albergo
        /// <summary>Un'ala d'albergo ha la stessa pianta nell'albergo e nell'istituto: cambiano le funzioni delle stanze.</summary>
        sealed class HotelKinds
        {
            public RoomKind Atrium, Guest, Suite, Big, BigAlt, Kitchen;
            public int SuiteDepth;
            public bool Institute;

            /// <summary>Stanza di servizio: un locale vuoto nell'albergo; nell'istituto una cella, un archivio, un obitorio o un ufficio.</summary>
            public RoomKind PickService(Rng r)
            {
                if (!Institute) return RoomKind.Servizio;
                switch (r.Weighted(new double[] { 0.5, 0.2, 0.15, 0.15 }))
                {
                    case 0: return RoomKind.Cella;
                    case 1: return RoomKind.Archivio;
                    case 2: return RoomKind.Obitorio;
                    default: return RoomKind.SalaMedici;
                }
            }
        }

        static readonly HotelKinds KindsAlbergo = new HotelKinds
        {
            Atrium = RoomKind.Atrio, Guest = RoomKind.Camera, Suite = RoomKind.Suite, Big = RoomKind.Ristorante,
            BigAlt = RoomKind.SalaBallo, Kitchen = RoomKind.Cucina, SuiteDepth = Grow(60),
        };

        static readonly HotelKinds KindsIstituto = new HotelKinds
        {
            Atrium = RoomKind.Accettazione, Guest = RoomKind.Degenza, Suite = RoomKind.Corsia, Big = RoomKind.Refettorio,
            BigAlt = RoomKind.SalaOperatoria, Kitchen = RoomKind.Lavanderia, SuiteDepth = Grow(84), Institute = true,
        };

        // ------------------------------------------------------------------ come si uniscono le due ali
        /// <summary>Le stanze candidate al portale delle due ali (lato e peso), la larghezza e lo stile dell'apertura che le collega.</summary>
        sealed class Joint
        {
            public List<(RoomKind k, Side s, double wt)> A, B;
            public int Width;
            public OpenStyle Style;

            static List<(RoomKind k, Side s, double wt)> Church() => new List<(RoomKind k, Side s, double wt)>
            {
                (RoomKind.Nartece, Side.W, 3.0), (RoomKind.Nartece, Side.N, 1.0), (RoomKind.Nartece, Side.S, 1.0),
                (RoomKind.Sacrestia, Side.E, 1.0), (RoomKind.Sacrestia, Side.N, 1.0),
            };

            public static Joint Albergo() => new Joint
            {
                A = new List<(RoomKind k, Side s, double wt)> { (RoomKind.Corridoio, Side.E, 3.0), (RoomKind.Atrio, Side.N, 1.0), (RoomKind.Atrio, Side.S, 1.0) },
                B = Church(), Width = 18, Style = OpenStyle.Arco,
            };

            public static Joint Istituto() => new Joint
            {
                A = new List<(RoomKind k, Side s, double wt)> { (RoomKind.Corridoio, Side.E, 3.0), (RoomKind.Accettazione, Side.N, 1.0), (RoomKind.Accettazione, Side.S, 1.0) },
                B = Church(), Width = 18, Style = OpenStyle.Arco,
            };

            /// <summary>L'arco scenico: la sala guarda il palcoscenico da un'apertura larga, come in un teatro vero.</summary>
            public static Joint Teatro() => new Joint
            {
                A = new List<(RoomKind k, Side s, double wt)> { (RoomKind.Platea, Side.W, 1.0) },
                B = new List<(RoomKind k, Side s, double wt)> { (RoomKind.Palcoscenico, Side.E, 1.0) },
                Width = 80, Style = OpenStyle.Arco,
            };
        }

        // ------------------------------------------------------------------ ali del teatro
        /// <summary>La sala: platea con i palchi sui lati lunghi, e il foyer all'estremo opposto al palcoscenico.</summary>
        static Wing TheatreWing(Rng r, bool simple)
        {
            var w = new Wing(false, 0);
            int hw = Grow(r.Pick(90, 100, 110));                        // mezza larghezza della sala
            int Lp = simple ? Grow(180) : Grow(r.Pick(180, 200, 220));  // dal palcoscenico al foyer
            var platea = w.Add(RoomKind.Platea, 0, -hw, Lp, hw, r);
            int fh = Grow(55);                                          // mezza larghezza del foyer
            var foyer = w.Add(RoomKind.Foyer, Lp, -fh, Lp + Grow(r.Pick(90, 110)), fh, r);
            w.Connect(platea, foyer, OpenStyle.PortaLarga, Grow(40), int.MinValue, 3);
            // ridotto: un bar piccolo sui lati del foyer, con la sua porta sul foyer
            foreach (int side in new[] { 1, -1 })
            {
                if (simple || !r.Chance(0.7)) continue;
                int rx0 = Lp + Grow(20);
                int rz0 = side > 0 ? fh : -fh - Grow(40);
                var ridotto = w.Add(RoomKind.Ridotto, rx0, rz0, rx0 + Grow(r.Pick(44, 50)), rz0 + Grow(40), r);
                w.Connect(ridotto, foyer, OpenStyle.PortaLarga, Grow(30), int.MinValue, 3);
            }
            // palchi: alcove sui due lati lunghi della sala, ciascuna con la sua porta sulla platea
            foreach (int side in new[] { 1, -1 })
            {
                int want = simple ? 1 : r.Range(1, 4);
                int x0 = Grow(30), made = 0;
                while (made < want)
                {
                    int pw = Grow(r.Pick(40, 50, 60));
                    if (x0 + pw > Lp - Grow(20)) break;
                    int z0 = side > 0 ? hw : -hw - Grow(40);
                    var palco = w.Add(RoomKind.Palco, x0, z0, x0 + pw, z0 + Grow(40), r);
                    w.Connect(palco, platea, OpenStyle.PortaLarga, Grow(30), int.MinValue, 3);
                    made++;
                    x0 += pw + Grow(r.Pick(20, 30));
                }
            }
            return w;
        }

        /// <summary>Il palcoscenico con il corridoio delle quinte e i camerini e il deposito lungo il lato lontano del corridoio.
        /// Il lato x = 0 e' quello che guarda la sala: li' sta l'arco scenico.</summary>
        static Wing StageWing(Rng r, bool simple)
        {
            var w = new Wing(false, 1);
            int hp = Grow(r.Pick(90, 100, 110));                          // mezza larghezza del palcoscenico
            int Ls = simple ? Grow(160) : Grow(r.Pick(150, 170, 190));    // profondita' del palcoscenico
            var stage = w.Add(RoomKind.Palcoscenico, -Ls, -hp, 0, hp, r);
            int cw = Grow(r.Pick(24, 26, 28));
            var back = w.Add(RoomKind.Corridoio, -Ls - cw, -hp, -Ls, hp, r);
            w.Connect(back, stage, OpenStyle.PortaLarga, Grow(40), int.MinValue, 3);
            int z = -hp;
            while (z < hp)
            {
                int wd = Grow(r.Pick(40, 50, 60));
                if (hp - (z + wd) < Grow(30)) wd = hp - z;                 // l'ultimo prende il resto
                int depth = Grow(r.Pick(50, 60));
                var kind = r.Chance(0.7) ? RoomKind.Camerino : RoomKind.Deposito;
                var room = w.Add(kind, -Ls - cw - depth, z, -Ls - cw, z + wd, r);
                w.Connect(room, back, OpenStyle.Porta, 14, int.MinValue, 3);
                z += wd;
            }
            return w;
        }

        // ------------------------------------------------------------------ regole dei temi
        /// <summary>Altezze e volte delle stanze dei temi nuovi (le altre le fissa SetKindDefaults). False per albergo e chiesa.</summary>
        static bool ThemeKindDefaults(Room r)
        {
            switch (r.kind)
            {
                case RoomKind.Platea: r.wallH = 9.5f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Palco: r.wallH = 3.6f; break;
                case RoomKind.Foyer: r.wallH = 6.0f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Palcoscenico: r.wallH = 11.0f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Camerino: r.wallH = 3.0f; break;
                case RoomKind.Ridotto: r.wallH = 3.8f; break;
                case RoomKind.Deposito: r.wallH = 4.0f; break;
                case RoomKind.Accettazione: r.wallH = 4.0f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Degenza: r.wallH = 3.2f; break;
                case RoomKind.Corsia: r.wallH = 4.2f; break;
                case RoomKind.Cella: r.wallH = 2.8f; break;
                case RoomKind.Refettorio: r.wallH = 4.2f; r.ceil = CeilKind.Beams; break;
                case RoomKind.SalaOperatoria: r.wallH = 4.5f; break;
                case RoomKind.Lavanderia: r.wallH = 3.4f; break;
                case RoomKind.Archivio: r.wallH = 3.8f; break;
                case RoomKind.Obitorio: r.wallH = 2.8f; break;
                case RoomKind.SalaMedici: r.wallH = 3.2f; break;
                // Guardaroba: gli armadi arrivano al soffitto, la cripta ha la volta a botte, la terrazza non ha tetto (le sale a balconata
                // del ciclo a ondate si alzano da sole, vedi BuildGrid; la terrazza no: niente balconata, i muri verso le altre stanze salgono col vicino)
                case RoomKind.SalaArmadi: r.wallH = 4.8f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Sartoria: r.wallH = 3.8f; break;
                case RoomKind.Spogliatoio: r.wallH = 3.4f; break;
                case RoomKind.Sepolcreto: r.wallH = 3.6f; r.ridgeH = 5.6f; r.ceil = CeilKind.BarrelRound; r.ridgeAlongX = true; break;
                case RoomKind.Forgia: r.wallH = 4.2f; r.ceil = CeilKind.Beams; break;
                case RoomKind.Terrazza: r.wallH = 6.0f; r.ceil = CeilKind.Open; break;
                default: return false;
            }
            if (r.ceil == CeilKind.Flat || r.ceil == CeilKind.Beams || r.ceil == CeilKind.Open) r.ridgeH = r.wallH;
            return true;
        }

        /// <summary>Dove puo' stare l'ascensore di arrivo: nella prima ala del tema, o nella seconda (chiesa, palcoscenico).</summary>
        static RoomKind[] StartKinds(Theme t, bool secondWing)
        {
            switch (t)
            {
                case Theme.Teatro: return secondWing ? new[] { RoomKind.Camerino, RoomKind.Deposito } : new[] { RoomKind.Foyer };
                case Theme.Sanatorio: return secondWing ? new[] { RoomKind.Sacrestia, RoomKind.Cripta, RoomKind.Cappella } : new[] { RoomKind.Accettazione };
                default: return secondWing ? new[] { RoomKind.Sacrestia, RoomKind.Cripta, RoomKind.Cappella } : new[] { RoomKind.Atrio };
            }
        }

        /// <summary>Le stanze di servizio e di culto si preferiscono come uscita: lì c'e' il lavoro e il rito del piano.</summary>
        static bool IsPreferredExit(RoomKind k) =>
            k == RoomKind.Servizio || k == RoomKind.Cucina || k == RoomKind.Atrio || k == RoomKind.SalaBallo || k == RoomKind.Ristorante
            || k == RoomKind.Cripta || k == RoomKind.Sacrestia || k == RoomKind.Cappella || k == RoomKind.Campanile || k == RoomKind.Presbiterio
            || k == RoomKind.Platea || k == RoomKind.Palcoscenico || k == RoomKind.Foyer || k == RoomKind.Accettazione
            || k == RoomKind.Refettorio || k == RoomKind.SalaOperatoria || k == RoomKind.Lavanderia || k == RoomKind.Archivio
            || k == RoomKind.Obitorio || k == RoomKind.SalaMedici;

        // ------------------------------------------------------------------ finestre dei temi nuovi
        /// <summary>Finestre delle stanze dei temi nuovi, sui tratti di muro che danno sul vuoto. La sala e il palcoscenico restano al buio.</summary>
        static void AddThemeWindows(Plan p, Rng r)
        {
            int n = p.rooms.Count;
            for (int i = 0; i < n; i++)
            {
                var room = p.rooms[i];
                if (room.IsLift) continue;
                foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                {
                    bool ax = Plan.AlongX(s);
                    int line = Plan.Line(room, s);
                    foreach (var seg in p.Segments(room, s))
                    {
                        if (seg.other >= 0) continue;
                        int len = seg.a1 - seg.a0;
                        switch (room.kind)
                        {
                            case RoomKind.Foyer:
                            case RoomKind.Accettazione:
                                for (int c = seg.a0 + 20; c <= seg.a1 - 20; c += 40) Win(p, room, ax, line, c, 16, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Camerino:
                            case RoomKind.Degenza:
                                if (len >= 30) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 12, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Corsia:
                                for (int c = seg.a0 + 18; c <= seg.a1 - 18; c += 30) Win(p, room, ax, line, c, 14, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Refettorio:
                            case RoomKind.SalaOperatoria:
                                for (int c = seg.a0 + 22; c <= seg.a1 - 22; c += 44) Win(p, room, ax, line, c, 16, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Lavanderia:
                                if (len >= 30) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 12, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Cella:
                            case RoomKind.SalaMedici:
                                if (len >= 20) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 8, OpenStyle.Feritoia, r);
                                break;
                            // Guardaroba: finestre alte nella sala degli armadi, larghe nella sartoria (serve luce per cucire), spioncini
                            // nello spogliatoio, feritoie nella cripta e nella forgia (i vampiri non amano il sole, il fabbro il vento)
                            case RoomKind.SalaArmadi:
                                for (int c = seg.a0 + 22; c <= seg.a1 - 22; c += 44) Win(p, room, ax, line, c, 14, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Sartoria:
                                for (int c = seg.a0 + 18; c <= seg.a1 - 18; c += 30) Win(p, room, ax, line, c, 14, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Spogliatoio:
                                if (len >= 30) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 12, OpenStyle.Finestra, r);
                                break;
                            case RoomKind.Sepolcreto:
                            case RoomKind.Forgia:
                                if (len >= 20) Win(p, room, ax, line, (seg.a0 + seg.a1) / 2, 8, OpenStyle.Feritoia, r);
                                break;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ sottotitoli
        static readonly string[] TeatroNames = { "Il sipario non si alza", "La platea vuota", "Il palco riservato", "Le luci di sala", "Il foyer dei ritardatari", "Dietro le quinte" };
        static readonly string[] SanatorioNames = { "Istituto Santa Lucia", "Reparto Silenzio", "Corsia Otto", "La cappella dell'istituto", "Il padiglione delle celle", "Il turno di notte" };

        static readonly string[] GuardarobaNames = { "Il guardaroba del conte", "L'ultimo armadio", "Taglia e cuci", "Il sarto e la lava", "Appendi il cappotto", "La terrazza del conte", "Le forbici non si fermano" };

        /// <summary>Il sottotitolo del piano: come prima per albergo e chiesa, con nomi suoi per il teatro, il sanatorio e il Guardaroba.</summary>
        static string Epithet(Plan p, Rng r)
        {
            switch (p.theme)
            {
                case Theme.Teatro: return r.Pick(TeatroNames);
                case Theme.Sanatorio: return r.Pick(SanatorioNames);
                case Theme.Guardaroba: return r.Pick(GuardarobaNames);
                default:
                    return r.Chance(0.7) ? (p.startInChurch ? r.Pick(ChurchNames) : r.Pick(HotelNames)) : (p.startInChurch ? r.Pick(HotelNames) : r.Pick(ChurchNames));
            }
        }
    }
}

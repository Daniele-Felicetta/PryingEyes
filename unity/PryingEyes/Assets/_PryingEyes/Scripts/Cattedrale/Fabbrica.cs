using System;
using System.Collections.Generic;
using Production.Economy;
using Production.Saves;
using PryingEyes.Characters;
using UnityEngine;

namespace PryingEyes.Cattedrale
{
    /// <summary>La Fabbrica della cattedrale (come la Fabbrica del Duomo): il magazzino delle risorse, le opere costruite, il mercato e la
    /// spedizione in corso. Dalla 0.9 e' di un personaggio (Characters.Roster): ognuno ha la sua cattedrale, salvata nel suo file sotto la
    /// chiave "cathedral" (le prove automatiche usano un personaggio di prova, mai quello di chi gioca). Le regole stanno nel pacchetto
    /// Production.Economy e i numeri in Catalogo.cs; qui c'e' lo stato del gioco e i bonus che le opere danno.
    ///
    /// La classe e' statica come sempre: tutto il gioco legge "la Fabbrica" di chi gira. Quello che cambia da un giocatore all'altro sta in
    /// <see cref="Stato"/> (uno per personaggio), e <see cref="Usa"/> mette al posto delle letture quello di un altro: nello schermo condiviso
    /// lo fa Seats a ogni cambio di posto, esattamente come per i punti e le armi.</summary>
    public static class Fabbrica
    {
        [Serializable] public sealed class Voce { public string id; public int n; }

        [Serializable]
        public sealed class Dati
        {
            public int versione = 1;
            public List<Voce> banca = new List<Voce>();
            public List<string> opere = new List<string>();
            public List<Voce> scorte = new List<Voce>();
            public int bossVinti;                    // un bit per ramo: il boss e' stato battuto almeno una volta
            public int spoglieRamo = -1;             // dove sono rimaste le tue spoglie (-1: da nessuna parte)
            public List<Voce> spoglie = new List<Voce>();
            public string benedizione = "";          // quella accesa per la prossima spedizione
            public List<string> ceri = new List<string>();   // le tre offerte dall'altare
            public int spedizioni, estrazioni, morti, bossUccisi, livelliScesi;
            public int ramoMassimo;                  // il ramo piu' profondo raggiunto
        }

        /// <summary>La cattedrale di UN personaggio in memoria: i dati salvati, il magazzino, le opere, il mercato e la spedizione in corso.</summary>
        public sealed class Stato
        {
            /// <summary>Di chi e' (null: di nessuno, non si salva da nessuna parte).</summary>
            public readonly CharacterRecord pg;
            public Dati d;
            public Ledger banca;
            public WorkTree albero;
            public Market mercato;
            public Expedition spedizione;
            /// <summary>Il ramo della spedizione in corso.</summary>
            public int ramo;
            /// <summary>Vero se il bottino va subito nel magazzino (cooperativa e schermo condiviso, dove non si torna alla cattedrale).</summary>
            public bool diretta;
            public bool campanellaUsata;
            /// <summary>Opere che il file nomina e questa versione non conosce (le ha aggiunte una versione piu' nuova): si rimettono nel file a ogni salvataggio.</summary>
            readonly List<string> sconosciute = new List<string>();

            public Stato(CharacterRecord pg) { this.pg = pg; Leggi(pg != null ? pg.cathedral : null); }

            /// <summary>Ricostruisce tutto da una cattedrale salvata (quella del personaggio).</summary>
            void Leggi(JsonItem salvata)
            {
                d = new Dati();
                try { if (salvata != null) d = JsonUtility.FromJson<Dati>(salvata.ToJson(false)) ?? new Dati(); }
                catch (Exception ex) { Debug.LogWarning($"[PryingEyes] cattedrale: dati illeggibili ({ex.Message}), si riparte da zero"); d = new Dati(); }
                banca = new Ledger();
                banca.Restore(DaVoci(d.banca));
                albero = Opere.Albero();
                albero.Restore(d.opere ?? new List<string>());
                sconosciute.Clear();
                // fuori dall'albero di questa modalita' (una versione piu' nuova, o le opere del deserto in una partita senza deserto): restano nel file
                foreach (string id in d.opere ?? new List<string>()) if (!albero.Exists(id)) sconosciute.Add(id);
                mercato = Listino.Nuovo();
                foreach (var s in d.scorte ?? new List<Voce>()) if (mercato.Exists(s.id)) mercato.RestoreLeft(s.id, s.n);
                spedizione = new Expedition(banca);
                banca.Changed += _ => { if (st == this) Cambiata?.Invoke(); };
                if (sconosciute.Count > 0) Debug.Log($"[PryingEyes] cattedrale: {sconosciute.Count} opere di una versione piu' nuova ({string.Join(", ", sconosciute)}) restano nel file");
            }

            /// <summary>L'albero si rifa' con le opere della modalita' di adesso (il deserto le aggiunge) senza perdere quelle costruite.</summary>
            public void RifaiAlbero()
            {
                var built = albero.BuiltIds();
                foreach (string id in sconosciute) if (!built.Contains(id)) built.Add(id);
                albero = Opere.Albero();
                albero.Restore(built);
                sconosciute.Clear();
                foreach (string id in built) if (!albero.Exists(id)) sconosciute.Add(id);
            }

            /// <summary>Mette lo stato nella cattedrale del personaggio (senza scrivere il file: lo fa Roster). I campi che questa versione
            /// non conosce restano come sono.</summary>
            public void Scrivi()
            {
                d.banca = AVoci(banca.Snapshot());
                d.opere = albero.BuiltIds();
                foreach (string id in sconosciute) if (!d.opere.Contains(id)) d.opere.Add(id);
                d.scorte = new List<Voce>();
                foreach (var o in mercato.All) if (o.Stock >= 0) d.scorte.Add(new Voce { id = o.Id, n = mercato.Left(o.Id) });
                if (pg == null) return;
                try { CathedralJson.Overlay(pg.cathedral, JsonItem.Parse(JsonUtility.ToJson(d))); }
                catch (Exception ex) { Debug.LogWarning($"[PryingEyes] cattedrale: non riesco a scrivere i dati di {pg.name}: {ex.Message}"); }
            }
        }

        static Stato st;                                        // quella installata (null finche' nessuno la chiede)
        static readonly Stato nessuno = new Stato(null);        // per chi guarda prima che ci sia un personaggio: vuota e mai salvata
        static readonly Dictionary<string, Stato> aperti = new Dictionary<string, Stato>();
        static Stato S => st ?? nessuno;

        public static Ledger Banca => S.banca;
        public static WorkTree Albero => S.albero;
        public static Market Mercato => S.mercato;
        public static Expedition Spedizione => S.spedizione;
        public static Dati D => S.d;
        public static bool Caricata => st != null;
        /// <summary>Il ramo della spedizione in corso.</summary>
        public static int Ramo => S.ramo;
        /// <summary>Vero se il bottino va subito nel magazzino (cooperativa e schermo condiviso, dove non si torna alla cattedrale).</summary>
        public static bool Diretta => S.diretta;
        /// <summary>Le risorse cambiate (bottino, acquisti, opere) nella cattedrale di chi gira: l'HUD si ridisegna.</summary>
        public static event Action Cambiata;

        // ---------------------------------------------------------------- personaggi
        /// <summary>La cattedrale che gira adesso (per Seats: ogni posto la ricorda e la rimette).</summary>
        public static Stato Corrente => st;
        public static CharacterRecord Proprietario => S.pg;

        /// <summary>Lo stato di un personaggio (uno solo per personaggio, anche se lo chiedono in due).</summary>
        public static Stato Apri(CharacterRecord pg)
        {
            if (pg == null) return null;
            if (aperti.TryGetValue(pg.id, out var s) && s.pg == pg) return s;
            s = new Stato(pg);
            aperti[pg.id] = s;
            Debug.Log($"[PryingEyes] cattedrale di {pg.name}: {s.albero.BuiltCount}/{s.albero.All.Count} opere, magazzino [{s.banca.Snapshot()}]");
            return s;
        }

        /// <summary>Da qui legge e scrive questa cattedrale.</summary>
        public static void Usa(Stato s) { if (s != null) st = s; }

        /// <summary>Le opere della modalita' sono cambiate (Opere.IncludeDesert): l'albero di ogni cattedrale aperta si rifa' (le opere costruite restano).</summary>
        public static void AggiornaOpere()
        {
            foreach (var s in aperti.Values) s.RifaiAlbero();
            nessuno.RifaiAlbero();
            if (st != null && !aperti.ContainsValue(st) && st != nessuno) st.RifaiAlbero();
        }

        /// <summary>La cattedrale di questo personaggio, da qui in avanti (il menu: "Gioca" con un altro personaggio).</summary>
        public static void Seleziona(CharacterRecord pg) { var s = Apri(pg); if (s != null) st = s; }

        /// <summary>Il personaggio e' stato eliminato: la sua cattedrale non si usa piu'.</summary>
        public static void Dimentica(CharacterRecord pg)
        {
            if (pg == null || !aperti.TryGetValue(pg.id, out var s)) return;
            aperti.Remove(pg.id);
            if (st == s) st = null;
        }

        public static string Profilo => Roster.Profile;

        /// <summary>Mette in uso la cattedrale del personaggio locale se nessuna e' ancora in uso (una volta per processo). Con 'forza' scrive
        /// quello che c'e' in sospeso e rilegge tutto dai file: per le prove ("il salvataggio ricaricato e' identico").</summary>
        public static void Carica(bool forza = false)
        {
            if (st != null && !forza) return;
            if (forza)
            {
                Roster.Flush();
                aperti.Clear();
                Roster.Load(true);
            }
            st = Apri(Roster.Local) ?? new Stato(null);
        }

        /// <summary>Scrive la cattedrale nel personaggio: subito (un'opera, una spedizione che comincia o finisce) o fra un attimo (un bottino).</summary>
        public static void Salva(bool subito = false)
        {
            var s = st;
            if (s == null) return;
            s.Scrivi();
            if (s.pg == null) return;
            if (subito) Roster.SaveNow(s.pg); else Roster.MarkDirty(s.pg);
        }

        /// <summary>Per le prove: il personaggio di prova riparte da zero (solo su un profilo di prova, mai quello di chi gioca).</summary>
        public static void Azzera()
        {
            if (!Roster.IsTest) throw new InvalidOperationException("il profilo di chi gioca non si azzera");
            Roster.ResetTestCharacter();
            aperti.Clear();
            st = null;
            Carica();
        }

        static Bundle DaVoci(List<Voce> v)
        {
            var list = new List<KeyValuePair<string, int>>();
            if (v != null) foreach (var x in v) if (!string.IsNullOrWhiteSpace(x.id) && x.n > 0) list.Add(new KeyValuePair<string, int>(x.id, x.n));
            return new Bundle(list);
        }
        static List<Voce> AVoci(Bundle b)
        {
            var list = new List<Voce>();
            foreach (var e in b) list.Add(new Voce { id = e.Key, n = e.Value });
            return list;
        }

        // ---------------------------------------------------------------- opere e bonus
        public static bool Costruita(string id) => S.albero.IsBuilt(id);
        public static bool BossVinto(int ramo) => (S.d.bossVinti & (1 << ramo)) != 0;
        public static bool RamoAperto(int ramo) => ramo >= 0 && ramo < Rami.Tutti.Length && Rami.Aperto(ramo, Costruita);
        public static string Benedizione => InGioco && !Diretta ? S.d.benedizione : "";
        static bool InGioco => S.spedizione.Active;
        static bool Ha(string benedizione) => Benedizione == benedizione;

        /// <summary>Il livello di chi gioca, per i bonus: nelle prove automatiche vale sempre 1 (i numeri delle prove non cambiano con l'esperienza).</summary>
        public static int Livello => S.pg == null || Roster.IsTest ? 1 : S.pg.Level;

        public static int PuntiIniziali => 500 + (Costruita("volte") ? 250 : 0) + Leveling.StartPointsBonus(Livello);
        public static int OboliOgni => Costruita("tesoro") ? 6 : 10;
        public static float DannoMul => (Costruita("forgia") ? 1.15f : 1f) * (Ha("furia") ? 1.2f : 1f);
        public static float DannoBossMul => Costruita("vetrata_cardinale") ? 1.25f : 1f;
        public static float VitaBonus => (Costruita("vetrata_primario") ? 25f : 0f) + (Ha("grazia") ? 30f : 0f) + Leveling.HpBonus(Livello);
        public static float PorteMul => (Costruita("vetrata_direttore") ? 0.75f : 1f) * (Ha("porte") ? 0.7f : 1f);
        public static float PerkMul => Costruita("vetrata_conte") ? 0.6f : 1f;
        public static float BottinoMul => Ha("raccolto") ? 1.5f : 1f;
        public static float CadutaMul => Ha("provvidenza") ? 2f : 1f;
        public static float PuntiUccisioneMul => Ha("decima") ? 1.25f : 1f;
        public static bool Inno => Costruita("organo");
        public static int AcquasantaMinima => Costruita("fonte") ? 3 : 0;
        public static bool CampanellaPronta => Costruita("campanile") && InGioco && !Diretta && !S.campanellaUsata;
        public static bool CampanellaUsata => S.campanellaUsata;
        public static void UsaCampanella() => S.campanellaUsata = true;

        public static WorkTree.Check Costruisci(string id)
        {
            Carica();
            var check = Albero.TryBuild(id, Banca);
            if (check == WorkTree.Check.Ok) { Salva(true); Cambiata?.Invoke(); Debug.Log($"[PryingEyes] cattedrale: costruita {id}, magazzino [{Banca.Snapshot()}]"); }
            return check;
        }

        public static Market.Check Compra(string offerta)
        {
            Carica();
            var check = Mercato.TryBuy(offerta, Banca);
            if (check == Market.Check.Ok) { Salva(true); Cambiata?.Invoke(); }
            return check;
        }

        /// <summary>Accende uno dei ceri dell'altare: la benedizione della prossima spedizione.</summary>
        public static void Benedici(string id) { Carica(); S.d.benedizione = Benedizioni.Of(id) != null ? id : ""; Salva(true); Cambiata?.Invoke(); }

        /// <summary>I tre ceri dell'altare: nuovi a ogni ritorno (finche' non ne accendi uno restano quelli).</summary>
        public static string[] Ceri()
        {
            Carica();
            var d = S.d;
            if (d.ceri == null || d.ceri.Count != 3)
            {
                var rng = new System.Random(d.spedizioni * 7919 + 17);
                d.ceri = new List<string>(Benedizioni.TreA(rng.NextDouble));
                Salva();
            }
            return d.ceri.ToArray();
        }

        // ---------------------------------------------------------------- spedizioni
        /// <summary>Si scende nelle catacombe dal ramo 'ramo'. 'diretta': il bottino va subito nel magazzino (nessun ritorno alla cattedrale).
        /// Restituisce i rifornimenti comprati al mercato, tolti dal magazzino: li applica chi chiama (punti, acqua santa, carillon...).</summary>
        public static Bundle Inizia(int ramo, bool diretta)
        {
            Carica();
            var s = S;
            if (s.spedizione.Active) s.spedizione.Extract();
            s.ramo = Mathf.Clamp(ramo, 0, Rami.Abisso);
            s.diretta = diretta;
            s.campanellaUsata = false;
            s.spedizione.Begin();
            // i rifornimenti del mercato si usano solo nelle spedizioni dalla cattedrale (nel gioco diretto restano in magazzino per la prossima)
            var rif = new List<KeyValuePair<string, int>>();
            if (!diretta) foreach (var e in s.banca.Snapshot()) if (Rifornimenti.Is(e.Key)) rif.Add(e);
            var forniti = new Bundle(rif);
            s.banca.TryPay(forniti);
            s.d.spedizioni++;
            s.d.ramoMassimo = Mathf.Max(s.d.ramoMassimo, s.ramo);
            if (s.pg != null)
            {
                s.pg.stats.games++;
                if (diretta) { if (Seats.Split) s.pg.stats.splitGames++; else if (Coop.Active) s.pg.stats.coopGames++; }
            }
            Salva(true);
            Debug.Log($"[PryingEyes] cattedrale: spedizione {s.d.spedizioni} nel ramo {s.ramo} ({Rami.Tutti[s.ramo].nome}), rifornimenti [{forniti}], benedizione '{s.d.benedizione}'" + (diretta ? ", bottino diretto" : ""));
            return forniti;
        }

        /// <summary>Una spedizione sospesa (RunSave) riprende: come <see cref="Inizia"/> ma senza contarla di nuovo e con lo zaino che c'era.
        /// I rifornimenti del mercato sono gia' stati usati alla partenza.</summary>
        public static void Riprendi(int ramo, Bundle zaino, bool campanellaUsata)
        {
            Carica();
            var s = S;
            if (s.spedizione.Active) s.spedizione.Extract();
            s.ramo = Mathf.Clamp(ramo, 0, Rami.Abisso);
            s.diretta = false;
            s.campanellaUsata = campanellaUsata;
            s.spedizione.Begin();
            if (zaino != null && !zaino.IsEmpty) s.spedizione.Pick(zaino);
            Salva(true);
            Cambiata?.Invoke();
            Debug.Log($"[PryingEyes] cattedrale: spedizione ripresa nel ramo {s.ramo} ({Rami.Tutti[s.ramo].nome}), zaino [{s.spedizione.Carried.Snapshot()}]");
        }

        /// <summary>La spedizione si sospende: lo zaino e' nel file della spedizione (RunSave) e qui non resta niente di aperto, ne' un'uscita
        /// a meta': non succede niente alla cattedrale e non conta come morte.</summary>
        public static void Sospendi()
        {
            var s = S;
            s.spedizione = new Expedition(s.banca);
            s.diretta = false;
            Salva(true);
            Cambiata?.Invoke();
        }

        /// <summary>Il bottino di un nemico: nello zaino (a rischio) o, nel gioco diretto, subito nel magazzino.</summary>
        public static void Raccogli(Bundle loot)
        {
            if (loot == null || loot.IsEmpty) return;
            Carica();
            if (!Spedizione.Active) { Banca.Deposit(loot); Salva(); return; }
            if (Diretta) { Spedizione.Secure(loot); Salva(); }
            else Spedizione.Pick(loot);
            Cambiata?.Invoke();
        }

        /// <summary>Il trofeo del boss di un piano: va subito nel magazzino (anche se poi muori). Restituisce cosa e' stato messo al sicuro.</summary>
        public static Bundle Trofeo(int piano)
        {
            Carica();
            var s = S;
            int ramo = Rami.BossDi(piano).indice;
            bool prima = !BossVinto(ramo);
            var rng = new System.Random(piano * 31 + s.d.bossUccisi * 977 + 5);
            var loot = Bottino.Boss(piano, prima, rng.NextDouble);
            if (s.spedizione.Active) s.spedizione.Secure(loot); else s.banca.Deposit(loot);
            s.d.bossVinti |= 1 << ramo;
            s.d.bossUccisi++;
            Salva(true);
            Progress.Award(s.pg, Leveling.BossXp);
            Cambiata?.Invoke();
            return loot;
        }

        /// <summary>La cassetta delle elemosine: 'punti' diventano oboli al sicuro (uno ogni 15).</summary>
        public static int Elemosina(int punti)
        {
            Carica();
            int oboli = Listino.OboliDaPunti(punti, Listino.ElemosinaOgni);
            if (oboli <= 0) return 0;
            if (Spedizione.Active) Spedizione.Secure(Bundle.One(Risorse.Oboli, oboli)); else Banca.Deposit(Risorse.Oboli, oboli);
            Salva();
            Cambiata?.Invoke();
            return oboli;
        }

        public sealed class Esito
        {
            public bool riuscita;
            public Bundle salvato = Bundle.Empty, perso = Bundle.Empty;
            public int oboli, punti;
            public string Titolo => riuscita ? "Spedizione compiuta" : "Spedizione fallita";
            public string Riga()
            {
                var parts = new List<string>();
                if (!salvato.IsEmpty) parts.Add("Riporti " + Risorse.Testo(salvato.Minus(Bundle.One(Risorse.Oboli, oboli))));
                if (oboli > 0) parts.Add($"{punti} punti diventano {oboli} oboli");
                if (!perso.IsEmpty) parts.Add("Perdi " + Risorse.Testo(perso) + " (restano nelle tue spoglie)");
                return parts.Count == 0 ? (riuscita ? "Torni a mani vuote." : "Non riporti niente.") : string.Join(" · ", parts);
            }
        }

        /// <summary>Sei tornato su vivo: tutto lo zaino va nel magazzino e i punti rimasti diventano oboli.</summary>
        public static Esito Rientra(int punti)
        {
            Carica();
            var s = S;
            var e = new Esito { riuscita = true, punti = Mathf.Max(0, punti) };
            if (!s.spedizione.Active) return e;
            e.oboli = Listino.OboliDaPunti(e.punti, OboliOgni);
            if (e.oboli > 0) s.spedizione.Pick(Bundle.One(Risorse.Oboli, e.oboli));
            s.spedizione.Extract();
            e.salvato = s.spedizione.Banked;
            s.d.estrazioni++;
            if (s.d.benedizione != "") { s.d.benedizione = ""; s.d.ceri.Clear(); }
            s.mercato.Restock();
            Salva(true);
            Progress.Award(s.pg, Leveling.ReturnXp);
            Cambiata?.Invoke();
            Debug.Log($"[PryingEyes] cattedrale: rientro con [{e.salvato}], magazzino [{s.banca.Snapshot()}]");
            return e;
        }

        /// <summary>Sei morto: si salva solo una parte dello zaino (Risorse.Tenuta); il resto diventa le tue spoglie nel primo livello del
        /// ramo, da riprendere alla prossima discesa (le spoglie vecchie, se non le avevi riprese, sono perse). Con il Confessionale i punti
        /// valgono un obolo ogni 20.</summary>
        public static Esito Fallisce(int punti)
        {
            Carica();
            var s = S;
            var e = new Esito { riuscita = false, punti = Mathf.Max(0, punti) };
            if (!s.spedizione.Active) return e;
            bool ossario = Costruita("ossario");
            if (Costruita("confessionale")) e.oboli = Listino.OboliDaPunti(e.punti, 20);
            var kept = s.spedizione.Fail(id => Risorse.Tenuta(id, ossario), out var lost);
            if (e.oboli > 0) s.banca.Deposit(Risorse.Oboli, e.oboli);
            e.salvato = s.spedizione.Banked.Plus(e.oboli > 0 ? Bundle.One(Risorse.Oboli, e.oboli) : Bundle.Empty);
            e.perso = lost;
            s.d.spoglieRamo = lost.IsEmpty ? -1 : s.ramo;
            s.d.spoglie = AVoci(lost);
            s.d.morti++;
            if (s.d.benedizione != "") { s.d.benedizione = ""; s.d.ceri.Clear(); }
            s.mercato.Restock();
            Salva(true);
            Progress.Award(s.pg, Leveling.DeathXp);
            Cambiata?.Invoke();
            Debug.Log($"[PryingEyes] cattedrale: spedizione fallita, salvato [{kept}], perso [{lost}]");
            return e;
        }

        /// <summary>Le spoglie da mettere nel primo livello del ramo (vuoto se non ce ne sono). Riprese, tornano nello zaino.</summary>
        public static Bundle Spoglie(int ramo) => S.d.spoglieRamo == ramo ? DaVoci(S.d.spoglie) : Bundle.Empty;
        public static void SpoglieRiprese()
        {
            var s = S;
            var b = DaVoci(s.d.spoglie);
            s.d.spoglieRamo = -1; s.d.spoglie.Clear();
            Raccogli(b);
            Salva();
        }

        /// <summary>Un livello delle catacombe raggiunto scendendo: conta nei libri e vale esperienza.</summary>
        public static void LivelloSceso()
        {
            var s = S;
            s.d.livelliScesi++;
            Progress.Award(s.pg, Leveling.DescentXp);
        }

        /// <summary>Si torna al menu: una spedizione da soli ancora aperta e' persa come una morte (si salva solo quello che la morte salva);
        /// nel gioco diretto (cooperativa, schermo condiviso) il bottino e' gia' nel magazzino e la spedizione si chiude e basta.</summary>
        public static void Chiudi(int punti)
        {
            if (!Caricata || !Spedizione.Active) return;
            if (Diretta) { Spedizione.Extract(); Salva(true); return; }
            Fallisce(punti);
        }

        /// <summary>Quanto di una risorsa c'e' tra magazzino e zaino (quello che l'HUD mostra).</summary>
        public static int Totale(string id) => Banca.Get(id) + (Spedizione.Active ? Spedizione.Carried.Get(id) : 0);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Production.Economy;
using UnityEngine;

namespace PryingEyes.Cattedrale
{
    /// <summary>La Fabbrica della cattedrale (come la Fabbrica del Duomo): il magazzino delle risorse, le opere costruite, il mercato e la
    /// spedizione in corso, salvati in un file della macchina (persistentDataPath/cattedrale.json; con -profilo NOME un file a parte, e le
    /// prove automatiche senza -profilo usano sempre un profilo di prova, mai quello di chi gioca). Le regole stanno nel pacchetto
    /// Production.Economy e i numeri in Catalogo.cs; qui c'e' solo lo stato del gioco e i bonus che le opere danno.</summary>
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

        public static Ledger Banca { get; private set; } = new Ledger();
        public static WorkTree Albero { get; private set; } = Opere.Albero();
        public static Market Mercato { get; private set; } = Listino.Nuovo();
        public static Expedition Spedizione { get; private set; } = new Expedition(Banca);
        public static Dati D { get; private set; } = new Dati();
        public static bool Caricata { get; private set; }
        /// <summary>Il ramo della spedizione in corso.</summary>
        public static int Ramo { get; private set; }
        /// <summary>Vero se il bottino va subito nel magazzino (cooperativa e schermo condiviso, dove non si torna alla cattedrale).</summary>
        public static bool Diretta { get; private set; }
        /// <summary>Le risorse cambiate (bottino, acquisti, opere): l'HUD si ridisegna.</summary>
        public static event Action Cambiata;
        static bool campanellaUsata;

        // ---------------------------------------------------------------- file
        public static string Profilo
        {
            get
            {
                var a = Environment.GetCommandLineArgs();
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-profilo") return Pulito(a[i + 1]);
                return GameManager.autoTest || VideoSettings.Automated ? "prova" : "";
            }
        }
        static string Pulito(string s)
        {
            var chars = new List<char>();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c) || c == '-' || c == '_') chars.Add(c);
            return new string(chars.ToArray());
        }
        public static string Percorso
        {
            get
            {
                string p = Profilo;
                return Path.Combine(Application.persistentDataPath, string.IsNullOrEmpty(p) ? "cattedrale.json" : "cattedrale-" + p + ".json");
            }
        }

        /// <summary>Carica il salvataggio (una volta per processo; Ricarica per forzare). Un file illeggibile non ferma il gioco: si
        /// riparte da zero e il file vecchio resta accanto con l'estensione .rotto.</summary>
        public static void Carica(bool forza = false)
        {
            if (Caricata && !forza) return;
            Caricata = true;
            D = new Dati();
            try
            {
                if (File.Exists(Percorso)) D = JsonUtility.FromJson<Dati>(File.ReadAllText(Percorso)) ?? new Dati();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PryingEyes] cattedrale: salvataggio illeggibile ({ex.Message}), si riparte da zero");
                try { File.Copy(Percorso, Percorso + ".rotto", true); } catch (Exception) { }
                D = new Dati();
            }
            Banca = new Ledger();
            Banca.Restore(DaVoci(D.banca));
            Albero = Opere.Albero();
            int skipped = Albero.Restore(D.opere ?? new List<string>());
            Mercato = Listino.Nuovo();
            foreach (var s in D.scorte ?? new List<Voce>()) if (Mercato.Exists(s.id)) Mercato.RestoreLeft(s.id, s.n);
            Spedizione = new Expedition(Banca);
            Banca.Changed += _ => Cambiata?.Invoke();
            Debug.Log($"[PryingEyes] cattedrale: {Percorso}: {Albero.BuiltCount}/{Albero.All.Count} opere, magazzino [{Banca.Snapshot()}]" + (skipped > 0 ? $", {skipped} opere sconosciute saltate" : ""));
        }

        public static void Salva()
        {
            if (!Caricata) return;
            D.banca = AVoci(Banca.Snapshot());
            D.opere = Albero.BuiltIds();
            D.scorte = new List<Voce>();
            foreach (var o in Mercato.All) if (o.Stock >= 0) D.scorte.Add(new Voce { id = o.Id, n = Mercato.Left(o.Id) });
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Percorso));
                string tmp = Percorso + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(D, true));
                if (File.Exists(Percorso)) File.Replace(tmp, Percorso, null); else File.Move(tmp, Percorso);
            }
            catch (Exception ex) { Debug.LogWarning($"[PryingEyes] cattedrale: salvataggio non scritto: {ex.Message}"); }
        }

        /// <summary>Per le prove: cancella il profilo e riparte da zero (solo su un profilo di prova, mai su quello di chi gioca).</summary>
        public static void Azzera()
        {
            if (string.IsNullOrEmpty(Profilo)) throw new InvalidOperationException("il profilo di chi gioca non si azzera");
            try { if (File.Exists(Percorso)) File.Delete(Percorso); } catch (Exception) { }
            Caricata = false;
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
        public static bool Costruita(string id) => Albero.IsBuilt(id);
        public static bool BossVinto(int ramo) => (D.bossVinti & (1 << ramo)) != 0;
        public static bool RamoAperto(int ramo) => ramo >= 0 && ramo < Rami.Tutti.Length && Rami.Aperto(ramo, Costruita);
        public static string Benedizione => InGioco && !Diretta ? D.benedizione : "";
        static bool InGioco => Spedizione.Active;
        static bool Ha(string benedizione) => Benedizione == benedizione;

        public static int PuntiIniziali => 500 + (Costruita("volte") ? 250 : 0);
        public static int OboliOgni => Costruita("tesoro") ? 6 : 10;
        public static float DannoMul => (Costruita("forgia") ? 1.15f : 1f) * (Ha("furia") ? 1.2f : 1f);
        public static float DannoBossMul => Costruita("vetrata_cardinale") ? 1.25f : 1f;
        public static float VitaBonus => (Costruita("vetrata_primario") ? 25f : 0f) + (Ha("grazia") ? 30f : 0f);
        public static float PorteMul => (Costruita("vetrata_direttore") ? 0.75f : 1f) * (Ha("porte") ? 0.7f : 1f);
        public static float PerkMul => Costruita("vetrata_conte") ? 0.6f : 1f;
        public static float BottinoMul => Ha("raccolto") ? 1.5f : 1f;
        public static float CadutaMul => Ha("provvidenza") ? 2f : 1f;
        public static float PuntiUccisioneMul => Ha("decima") ? 1.25f : 1f;
        public static bool Inno => Costruita("organo");
        public static int AcquasantaMinima => Costruita("fonte") ? 3 : 0;
        public static bool CampanellaPronta => Costruita("campanile") && InGioco && !Diretta && !campanellaUsata;
        public static void UsaCampanella() => campanellaUsata = true;

        public static WorkTree.Check Costruisci(string id)
        {
            Carica();
            var check = Albero.TryBuild(id, Banca);
            if (check == WorkTree.Check.Ok) { Salva(); Cambiata?.Invoke(); Debug.Log($"[PryingEyes] cattedrale: costruita {id}, magazzino [{Banca.Snapshot()}]"); }
            return check;
        }

        public static Market.Check Compra(string offerta)
        {
            Carica();
            var check = Mercato.TryBuy(offerta, Banca);
            if (check == Market.Check.Ok) { Salva(); Cambiata?.Invoke(); }
            return check;
        }

        /// <summary>Accende uno dei ceri dell'altare: la benedizione della prossima spedizione.</summary>
        public static void Benedici(string id) { D.benedizione = Benedizioni.Of(id) != null ? id : ""; Salva(); Cambiata?.Invoke(); }

        /// <summary>I tre ceri dell'altare: nuovi a ogni ritorno (finche' non ne accendi uno restano quelli).</summary>
        public static string[] Ceri()
        {
            if (D.ceri == null || D.ceri.Count != 3)
            {
                var rng = new System.Random(D.spedizioni * 7919 + 17);
                D.ceri = new List<string>(Benedizioni.TreA(rng.NextDouble));
                Salva();
            }
            return D.ceri.ToArray();
        }

        // ---------------------------------------------------------------- spedizioni
        /// <summary>Si scende nelle catacombe dal ramo 'ramo'. 'diretta': il bottino va subito nel magazzino (nessun ritorno alla cattedrale).
        /// Restituisce i rifornimenti comprati al mercato, tolti dal magazzino: li applica chi chiama (punti, acqua santa, carillon...).</summary>
        public static Bundle Inizia(int ramo, bool diretta)
        {
            Carica();
            if (Spedizione.Active) Spedizione.Extract();
            Ramo = Mathf.Clamp(ramo, 0, Rami.Abisso);
            Diretta = diretta;
            campanellaUsata = false;
            Spedizione.Begin();
            // i rifornimenti del mercato si usano solo nelle spedizioni dalla cattedrale (nel gioco diretto restano in magazzino per la prossima)
            var rif = new List<KeyValuePair<string, int>>();
            if (!diretta) foreach (var e in Banca.Snapshot()) if (Rifornimenti.Is(e.Key)) rif.Add(e);
            var forniti = new Bundle(rif);
            Banca.TryPay(forniti);
            D.spedizioni++;
            D.ramoMassimo = Mathf.Max(D.ramoMassimo, Ramo);
            Salva();
            Debug.Log($"[PryingEyes] cattedrale: spedizione {D.spedizioni} nel ramo {Ramo} ({Rami.Tutti[Ramo].nome}), rifornimenti [{forniti}], benedizione '{D.benedizione}'" + (diretta ? ", bottino diretto" : ""));
            return forniti;
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
            int ramo = Rami.BossDi(piano).indice;
            bool prima = !BossVinto(ramo);
            var rng = new System.Random(piano * 31 + D.bossUccisi * 977 + 5);
            var loot = Bottino.Boss(piano, prima, rng.NextDouble);
            if (Spedizione.Active) Spedizione.Secure(loot); else Banca.Deposit(loot);
            D.bossVinti |= 1 << ramo;
            D.bossUccisi++;
            Salva();
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
            var e = new Esito { riuscita = true, punti = Mathf.Max(0, punti) };
            if (!Spedizione.Active) return e;
            e.oboli = Listino.OboliDaPunti(e.punti, OboliOgni);
            if (e.oboli > 0) Spedizione.Pick(Bundle.One(Risorse.Oboli, e.oboli));
            Spedizione.Extract();
            e.salvato = Spedizione.Banked;
            D.estrazioni++;
            if (D.benedizione != "") { D.benedizione = ""; D.ceri.Clear(); }
            Mercato.Restock();
            Salva();
            Cambiata?.Invoke();
            Debug.Log($"[PryingEyes] cattedrale: rientro con [{e.salvato}], magazzino [{Banca.Snapshot()}]");
            return e;
        }

        /// <summary>Sei morto: si salva solo una parte dello zaino (Risorse.Tenuta); il resto diventa le tue spoglie nel primo livello del
        /// ramo, da riprendere alla prossima discesa (le spoglie vecchie, se non le avevi riprese, sono perse). Con il Confessionale i punti
        /// valgono un obolo ogni 20.</summary>
        public static Esito Fallisce(int punti)
        {
            Carica();
            var e = new Esito { riuscita = false, punti = Mathf.Max(0, punti) };
            if (!Spedizione.Active) return e;
            bool ossario = Costruita("ossario");
            if (Costruita("confessionale")) e.oboli = Listino.OboliDaPunti(e.punti, 20);
            var kept = Spedizione.Fail(id => Risorse.Tenuta(id, ossario), out var lost);
            if (e.oboli > 0) Banca.Deposit(Risorse.Oboli, e.oboli);
            e.salvato = Spedizione.Banked.Plus(e.oboli > 0 ? Bundle.One(Risorse.Oboli, e.oboli) : Bundle.Empty);
            e.perso = lost;
            D.spoglieRamo = lost.IsEmpty ? -1 : Ramo;
            D.spoglie = AVoci(lost);
            D.morti++;
            if (D.benedizione != "") { D.benedizione = ""; D.ceri.Clear(); }
            Mercato.Restock();
            Salva();
            Cambiata?.Invoke();
            Debug.Log($"[PryingEyes] cattedrale: spedizione fallita, salvato [{kept}], perso [{lost}]");
            return e;
        }

        /// <summary>Le spoglie da mettere nel primo livello del ramo (vuoto se non ce ne sono). Riprese, tornano nello zaino.</summary>
        public static Bundle Spoglie(int ramo) => D.spoglieRamo == ramo ? DaVoci(D.spoglie) : Bundle.Empty;
        public static void SpoglieRiprese()
        {
            var b = DaVoci(D.spoglie);
            D.spoglieRamo = -1; D.spoglie.Clear();
            Raccogli(b);
            Salva();
        }

        public static void LivelloSceso() { D.livelliScesi++; }

        /// <summary>Si torna al menu: una spedizione da soli ancora aperta e' persa come una morte (si salva solo quello che la morte salva);
        /// nel gioco diretto (cooperativa, schermo condiviso) il bottino e' gia' nel magazzino e la spedizione si chiude e basta.</summary>
        public static void Chiudi(int punti)
        {
            if (!Caricata || !Spedizione.Active) return;
            if (Diretta) { Spedizione.Extract(); Salva(); return; }
            Fallisce(punti);
        }

        /// <summary>Quanto di una risorsa c'e' tra magazzino e zaino (quello che l'HUD mostra).</summary>
        public static int Totale(string id) => Banca.Get(id) + (Spedizione.Active ? Spedizione.Carried.Get(id) : 0);
    }
}

using System.Collections.Generic;
using System.Text;
using Production.Economy;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Cattedrale
{
    /// <summary>Le cose della cattedrale con cui si interagisce con F (come le armi a muro): i cantieri delle opere, gli oggetti sui banchi
    /// del mercato, i ceri dell'altare, i cancelli dei rami, il registro della Fabbrica. Le crea RoomKits.Hub.cs mentre arreda.</summary>
    public abstract class HubCosa : MonoBehaviour, IInteract
    {
        public Vector3 point;
        public float radius = 2.2f;
        public Vector3 Point => point;
        public virtual float Radius => radius;
        public abstract string Prompt { get; }
        public abstract void Use();
        protected virtual void OnEnable() { Interact.Add(this); }
        protected virtual void OnDisable() { Interact.Remove(this); }
        protected static HUD Hud => GameManager.I != null ? GameManager.I.hud : null;

        /// <summary>"Legno 32/50 · Acciaio 30/30": quello che hai (magazzino) contro quello che serve.</summary>
        public static string Costo(Bundle cost)
        {
            var sb = new StringBuilder();
            foreach (var r in Risorse.Tutte)
            {
                int need = cost[r.id];
                if (need <= 0) continue;
                if (sb.Length > 0) sb.Append("  ·  ");
                int have = Fabbrica.Banca.Get(r.id);
                sb.Append(r.nome).Append(' ').Append(Mathf.Min(have, need)).Append('/').Append(need);
            }
            return sb.ToString();
        }
    }

    /// <summary>Il cantiere di un'opera: un'impalcatura con il cartello. F costruisce se il magazzino basta (e le opere richieste ci sono);
    /// la cattedrale si ricostruisce con l'opera nuova al suo posto.</summary>
    public sealed class CantiereOpera : HubCosa
    {
        public string opera;
        OperaDef Def => Opere.Of(opera);

        public override string Prompt
        {
            get
            {
                var d = Def;
                if (d == null || Fabbrica.Costruita(opera)) return null;
                string nome = d.nome.ToUpperInvariant();
                switch (Fabbrica.Albero.CanBuild(opera, Fabbrica.Banca))
                {
                    case WorkTree.Check.Locked:
                        var serve = new List<string>();
                        foreach (var r in d.richiede) if (!Fabbrica.Costruita(r)) serve.Add(Opere.Of(r).nome);
                        return $"{nome} — prima: {string.Join(", ", serve)}";
                    case WorkTree.Check.Ok:
                        return $"COSTRUISCI {nome} — {Costo(d.costo)}\n{d.effetto}";
                    default:
                        return $"{nome} — {Costo(d.costo)}\n{d.effetto}";
                }
            }
        }

        public override void Use()
        {
            var d = Def;
            if (d == null) return;
            var check = Fabbrica.Costruisci(opera);
            if (check == WorkTree.Check.Ok)
            {
                Sfx.UI(SoundBank.Toll, 0.7f, 0.85f);
                FloorDirector.I?.HubBuilt(d);
                return;
            }
            Sfx.UI(SoundBank.BookNo, 0.6f);
            if (check == WorkTree.Check.Unaffordable) Hud?.Toast("Mancano " + Risorse.Testo(Fabbrica.Banca.Missing(d.costo)) + ".");
            else if (check == WorkTree.Check.Locked) Hud?.Toast("Prima servono le opere che la reggono.");
        }
    }

    /// <summary>Un oggetto sul banco di un mercante: F compra (o vende) l'offerta. Le scorte tornano piene a ogni ritorno dalle catacombe.</summary>
    public sealed class BancoOfferta : HubCosa
    {
        public string offerta;
        public override float Radius => 1.9f;

        public override string Prompt
        {
            get
            {
                var m = Fabbrica.Mercato;
                if (!m.Exists(offerta)) return null;
                var o = m.Get(offerta);
                bool vendi = o.Goods[Risorse.Oboli] > 0;
                int left = m.Left(offerta);
                string scorta = left < 0 ? "" : left == 0 ? "  ·  esaurito fino al prossimo ritorno" : $"  ·  ne restano {left}";
                if (vendi)
                {
                    string cosa = Risorse.Testo(o.Price);
                    int have = 0; foreach (var e in o.Price) have = Fabbrica.Banca.Get(e.Key);
                    return $"VENDI {cosa.ToUpperInvariant()} — {o.Goods[Risorse.Oboli]} OBOLI  (ne hai {have})";
                }
                string merce = Risorse.Testo(o.Goods);
                string effetto = "";
                foreach (var e in o.Goods) { var r = Risorse.Of(e.Key); if (r != null && r.grado == Grado.Rifornimento) effetto = "\n" + r.descrizione + " (per la prossima spedizione)"; }
                return $"COMPRA {merce.ToUpperInvariant()} — {o.Price[Risorse.Oboli]} OBOLI{scorta}{effetto}";
            }
        }

        public override void Use()
        {
            var check = Fabbrica.Compra(offerta);
            var o = Fabbrica.Mercato.Get(offerta);
            switch (check)
            {
                case Market.Check.Ok:
                    Sfx.UI(SoundBank.PickCoins, 0.5f, 1.1f);
                    Hud?.Toast(o.Goods[Risorse.Oboli] > 0 ? $"Venduto: {Risorse.Testo(o.Price)}." : $"Comprato: {Risorse.Testo(o.Goods)}.");
                    GameManager.I?.hud?.SetScore(Fabbrica.Banca.Get(Risorse.Oboli));
                    break;
                case Market.Check.SoldOut: Sfx.UI(SoundBank.BookNo, 0.6f); Hud?.Toast("Esaurito: il mercante torna a rifornirsi mentre sei sotto."); break;
                case Market.Check.Unaffordable: Sfx.UI(SoundBank.BookNo, 0.6f); Hud?.Toast("Mancano " + Risorse.Testo(Fabbrica.Banca.Missing(o.Price)) + "."); break;
            }
        }
    }

    /// <summary>Uno dei tre ceri davanti all'altare maggiore: acceso, da' la sua benedizione alla prossima spedizione.</summary>
    public sealed class CeroBenedizione : HubCosa
    {
        public string benedizione;
        public Light flame;
        public Renderer flameMesh;

        public override string Prompt
        {
            get
            {
                var b = Benedizioni.Of(benedizione);
                if (b == null) return null;
                bool acceso = Fabbrica.D.benedizione == benedizione;
                return acceso ? $"IL CERO DELLA {b.nome.ToUpperInvariant()} È ACCESO — {b.effetto}, per la prossima discesa"
                              : $"ACCENDI IL CERO DELLA {b.nome.ToUpperInvariant()} — {b.effetto}";
            }
        }

        public override void Use()
        {
            Fabbrica.Benedici(benedizione);
            Sfx.UI(SoundBank.Ignite, 0.7f);
            foreach (var c in FindObjectsByType<CeroBenedizione>(FindObjectsSortMode.None)) c.Refresh();
            var b = Benedizioni.Of(benedizione);
            Hud?.Banner(b.nome, b.effetto + ". La benedizione ti accompagna nella prossima discesa.");
        }

        public void Refresh()
        {
            bool on = Fabbrica.D.benedizione == benedizione;
            if (flame != null) flame.enabled = on;
            if (flameMesh != null) flameMesh.enabled = on;
        }

        void Start() { Refresh(); }
    }

    /// <summary>Il cancello di un ramo nella discesa alle catacombe. Aperto: F scende al primo livello. Chiuso: e' il cantiere della sua opera.</summary>
    public sealed class CancelloRamo : HubCosa
    {
        public int ramo;
        public override float Radius => 2.6f;

        public override string Prompt
        {
            get
            {
                var r = Rami.Tutti[ramo];
                if (Fabbrica.RamoAperto(ramo))
                {
                    string boss = ramo == Rami.Abisso ? "senza fondo, un boss ogni tre livelli" : $"3 livelli, in fondo {r.bossTitolo}";
                    string spoglie = Fabbrica.D.spoglieRamo == ramo ? "  ·  le tue spoglie sono laggiù" : "";
                    string bene = Fabbrica.D.benedizione != "" ? $"  ·  benedizione: {Benedizioni.Of(Fabbrica.D.benedizione)?.nome}" : "";
                    return $"SCENDI: {r.nome.ToUpperInvariant()} — {boss}{spoglie}{bene}";
                }
                var d = Opere.Of(r.cancello);
                if (d == null) return null;
                bool can = Fabbrica.Albero.CanBuild(d.id, Fabbrica.Banca) == WorkTree.Check.Ok;
                if (!Fabbrica.Albero.Unlocked(d.id))
                {
                    var serve = new List<string>();
                    foreach (var q in d.richiede) if (!Fabbrica.Costruita(q)) serve.Add(Opere.Of(q).nome);
                    return $"{r.nome.ToUpperInvariant()} — il cancello è murato. Prima: {string.Join(", ", serve)}";
                }
                return $"{(can ? "COSTRUISCI " : "")}{d.nome.ToUpperInvariant()} — {Costo(d.costo)}\n{d.effetto}";
            }
        }

        public override void Use()
        {
            if (Fabbrica.RamoAperto(ramo)) { FloorDirector.I?.BeginExpedition(ramo); return; }
            var d = Opere.Of(Rami.Tutti[ramo].cancello);
            if (d == null) return;
            var check = Fabbrica.Costruisci(d.id);
            if (check == WorkTree.Check.Ok) { Sfx.UI(SoundBank.Clang, 0.8f, 0.8f); FloorDirector.I?.HubBuilt(d); return; }
            Sfx.UI(SoundBank.BookNo, 0.6f);
            if (check == WorkTree.Check.Unaffordable) Hud?.Toast("Mancano " + Risorse.Testo(Fabbrica.Banca.Missing(d.costo)) + ".");
            else Hud?.Toast("Prima servono le opere che lo reggono.");
        }
    }

    /// <summary>Il registro della Fabbrica, sul leggio del nartece: F apre e chiude l'elenco di tutte le opere (fatte, da fare, bloccate), con
    /// i costi e gli effetti, e il magazzino. Si chiude anche allontanandosi.</summary>
    public sealed class RegistroFabbrica : HubCosa
    {
        public static bool Aperto;
        static GUIStyle head, row, small;

        public override string Prompt => Aperto ? "CHIUDI IL REGISTRO DELLA FABBRICA" : "LEGGI IL REGISTRO DELLA FABBRICA — le opere, i costi, gli effetti";
        public override void Use() { Aperto = !Aperto; Sfx.UI(SoundBank.BookOpen, 0.5f); }

        protected override void OnDisable() { base.OnDisable(); Aperto = false; }

        void Update()
        {
            if (!Aperto) return;
            var gm = GameManager.I;
            if (gm == null || gm.player == null || Vector3.Distance(gm.player.transform.position, point) > 4.5f) Aperto = false;
        }

        void OnGUI()
        {
            if (!Aperto || GameMenu.Blocked) return;
            float s = Screen.height / 720f;
            if (head == null)
            {
                head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
                row = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
                small = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            }
            head.fontSize = Mathf.RoundToInt(17 * s); row.fontSize = Mathf.RoundToInt(12 * s); small.fontSize = Mathf.RoundToInt(11 * s);
            var area = new Rect(Screen.width * 0.12f, Screen.height * 0.08f, Screen.width * 0.76f, Screen.height * 0.84f);
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(area.x + 18 * s, area.y + 12 * s, area.width - 36 * s, area.height - 24 * s));
            GUILayout.Label($"Il registro della Fabbrica — {Fabbrica.Albero.BuiltCount}/{Fabbrica.Albero.All.Count} opere", head);
            GUILayout.Label("<color=#c8b48a>Magazzino:</color> " + Risorse.Testo(Fabbrica.Banca.Snapshot(), "vuoto"), small);
            GUILayout.Space(6 * s);
            foreach (var d in Opere.Elenco)
            {
                bool fatta = Fabbrica.Costruita(d.id);
                bool sbloccata = Fabbrica.Albero.Unlocked(d.id);
                bool puoi = !fatta && Fabbrica.Albero.CanBuild(d.id, Fabbrica.Banca) == WorkTree.Check.Ok;
                string stato = fatta ? "<color=#8fd18f>fatta</color>" : puoi ? "<color=#ffd36b>si può costruire</color>" : sbloccata ? "<color=#bbbbbb>da fare</color>" : "<color=#777777>bloccata</color>";
                string costo = fatta ? "" : "  —  " + Costo(d.costo);
                GUILayout.Label($"<b>{d.nome}</b> ({d.luogo}) · {stato}{costo}\n<color=#a9a9a9>{d.effetto}</color>", row);
            }
            GUILayout.EndArea();
        }
    }
}

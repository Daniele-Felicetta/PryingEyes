using System;
using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Un giocatore dello schermo condiviso: tutto quello che nel gioco e' "del giocatore" (corpo, camera, HUD, punti, armi, furtivita',
    /// medaglie, controller) sta qui. Il posto 0 e' il giocatore di sempre: i suoi oggetti sono quelli della scena e restano sempre al loro posto.</summary>
    public sealed class Seat
    {
        public readonly int index;
        /// <summary>Riceve tastiera e mouse (uno solo alla volta: il posto 0).</summary>
        public bool keyboard;
        public PlayerController player;
        public HUD hud;
        public Camera camera;
        /// <summary>Contenitore degli oggetti del posto (null per il posto 0): tutto quello che vi sta sotto "e' suo".</summary>
        public Transform root;
        /// <summary>La sua porzione dello schermo, 0..1 (y dal basso, come Camera.rect). E' dentro il SUO monitor (display).</summary>
        public Rect view = new Rect(0f, 0f, 1f, 1f);
        /// <summary>Il monitor su cui sta: 0 e' la finestra del gioco, 1 e oltre i monitor accesi con SplitScreen.ActivateDisplays (camera e HUD ci
        /// vanno con Camera.targetDisplay e il canvas del monitor). Con un monitor solo e' sempre 0.</summary>
        public int display;
        /// <summary>Lo strato di Unity di arma e mani di questo giocatore: le camere degli altri non lo disegnano.</summary>
        public int viewLayer;
        // quello che il resto del gioco legge come globale (GameManager.I.score, Equipment.I...): Seats.Install lo mette al suo posto
        public int score = 500, kills, shotsFired, shotsHit, damageTaken, headshots, deaths;
        public Equipment equipment; public Knife knife; public Torch torch; public Guide guide; public StealthRig rig; public Prove prove;
        public Stealth.State stealth = new Stealth.State();
        public Medals.State medals = new Medals.State();
        public Proc.Interact.State interact = new Proc.Interact.State();
        public WallBuy.State wallBuy = new WallBuy.State();
        public PadControls.Channel pad;
        /// <summary>Come lo vedono i nemici (il posto 0 e' Targets.Local, gli altri un SeatTarget).</summary>
        public ITarget target;

        public Seat(int index) { this.index = index; viewLayer = Seats.FirstViewLayer + index; }
        /// <summary>Il corpo di questo posto come lo vedono gli altri (0.7.1).</summary>
        public SeatAvatar avatar;
        public string Name => "Giocatore " + (index + 1);
        public Transform Eye => player != null ? player.cam : null;
        /// <summary>La torcia di questo posto (se il posto gira adesso, quella installata).</summary>
        public Torch TorchNow => Seats.Current == this ? Torch.I : torch;
    }

    /// <summary>Schermo condiviso, un solo gioco e una sola finestra: da due a quattro giocatori nello stesso mondo, ognuno nella sua porzione dello
    /// schermo. Ogni giocatore e' un "posto" (Seat) con il suo corpo, la sua camera, il suo HUD e il suo stato (punti, armi, furtivita', medaglie,
    /// controller). Il resto del gioco, scritto per un solo giocatore, legge sempre le stesse cose (GameManager.I.player, .hud, .score,
    /// Equipment.I, Stealth.Level...): quando gira il codice di un posto, Enter mette al posto di quelle globali quelle del posto, e al termine
    /// rimette a posto il posto 0. Cosi' ogni sistema continua a funzionare come per un giocatore solo, e vale per chi sta girando.
    /// A riposo (fuori da ogni Enter) sono installati i dati del posto 0: nel giocatore singolo non cambia niente e non costa niente.
    ///
    /// Regole: i componenti dei posti 1..3 avvolgono i loro punti d'ingresso di Unity (Update, LateUpdate...) in "using (Seats.Enter(this))";
    /// il codice del mondo che parla di "tutti i giocatori" passa da Targets (ogni posto e' un ITarget) o scorre All; il codice che ha
    /// un giocatore specifico (un bersaglio colpito, chi ha lanciato una granata) entra nel posto giusto.</summary>
    public static partial class Seats
    {
        public const int MaxSeats = 4;
        /// <summary>Strati di Unity per arma e mani dei giocatori: 24 + indice del posto. Fuori dallo split screen l'arma sta sullo strato 2, come sempre.</summary>
        public const int FirstViewLayer = 24;
        /// <summary>Strati del corpo di ogni posto visto dagli altri (SeatAvatar, 20..23): la camera di un posto disegna quelli degli altri e
        /// non il suo.</summary>
        public const int FirstAvatarLayer = 20;

        public static readonly List<Seat> All = new List<Seat>();
        public static bool Split => All.Count > 1;
        public static int Count => All.Count;
        public static Seat Primary => All.Count > 0 ? All[0] : null;
        static Seat cur;
        /// <summary>Il posto i cui dati sono installati adesso (null: giocatore singolo).</summary>
        public static Seat Current => cur;
        /// <summary>Chi gira riceve tastiera e mouse (sempre vero fuori dallo split screen).</summary>
        public static bool Keyboard => cur == null || cur.keyboard;
        /// <summary>Lo strato dell'arma e delle mani di chi gira.</summary>
        public static int ViewLayer => cur != null ? cur.viewLayer : 2;
        /// <summary>Tutti gli strati di arma e mani dei posti.</summary>
        public static int AllViewLayersMask { get { int m = 0; for (int i = 0; i < MaxSeats; i++) m |= 1 << (FirstViewLayer + i); return m; } }
        public static int AllAvatarLayersMask { get { int m = 0; for (int i = 0; i < MaxSeats; i++) m |= 1 << (FirstAvatarLayer + i); return m; } }
        public static int SeatIndex => cur != null ? cur.index : 0;
        /// <summary>Lo "slot" di chi gira, come lo conoscono candele, premi e bottino: in cooperativa quello di rete, nello schermo condiviso il suo posto.</summary>
        public static int Slot => Split ? SeatIndex : Coop.Slot;
        /// <summary>Il posto con quello slot (null fuori dallo schermo condiviso o se non c'e').</summary>
        public static Seat OfSlot(int slot)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].index == slot) return All[i];
            return null;
        }

        static readonly Dictionary<Transform, Seat> byRoot = new Dictionary<Transform, Seat>();

        // ------------------------------------------------------------------ cambio di posto
        public readonly struct Scope : IDisposable
        {
            readonly Seat back;
            internal Scope(Seat back) { this.back = back; }
            public void Dispose() { if (back != null) Install(back); }
        }

        /// <summary>Da qui al Dispose valgono i dati del posto (using (Seats.Enter(s)) ...). Con null, o se il posto gira gia', non fa niente.</summary>
        public static Scope Enter(Seat s)
        {
            if (s == null || s == cur || cur == null) return default;
            var back = cur;
            Install(s);
            return new Scope(back);
        }

        /// <summary>Per i punti d'ingresso di Unity di un componente: il posto a cui appartiene (null per quelli del posto 0 e fuori dallo split screen).</summary>
        public static Scope Enter(Component c)
        {
            if (byRoot.Count == 0 || c == null) return default;
            return Enter(OwnerOf(c.transform));
        }

        public static Seat OwnerOf(Transform t)
        {
            if (byRoot.Count == 0 || t == null) return null;
            return byRoot.TryGetValue(t.root, out var s) ? s : null;
        }

        /// <summary>Il posto di un giocatore (PlayerController), o null.</summary>
        public static Seat OfPlayer(PlayerController p)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].player == p) return All[i];
            return null;
        }

        /// <summary>Il bersaglio da dare al corpo di quel giocatore (PlayerAvoidance): quello del suo posto, o il locale.</summary>
        public static ITarget TargetFor(Component c)
        {
            var s = OwnerOf(c.transform);
            return s != null && s.target != null ? s.target : Targets.Local;
        }

        /// <summary>Un oggetto che appartiene a chi gira ma nasce alla radice della scena (la rete della furtivita'): lo appende al contenitore del posto.</summary>
        public static void Adopt(GameObject go)
        {
            if (cur != null && cur.root != null && go != null) go.transform.SetParent(cur.root, false);
        }

        internal static void Install(Seat s)
        {
            if (s == null || s == cur) return;
            var gm = GameManager.I;
            if (cur != null) Save(cur, gm);
            Load(s, gm);
            cur = s;
        }

        static void Save(Seat s, GameManager gm)
        {
            if (gm != null) { s.score = gm.score; s.kills = gm.kills; }
            s.shotsFired = GameManager.shotsFired; s.shotsHit = GameManager.shotsHit; s.damageTaken = GameManager.damageTaken;
            s.headshots = GameManager.headshots; s.deaths = GameManager.deaths;
            s.equipment = Equipment.I; s.knife = Knife.I; s.torch = Torch.I; s.guide = Guide.I; s.rig = StealthRig.I; s.prove = Prove.I;
        }

        static void Load(Seat s, GameManager gm)
        {
            if (gm != null) { gm.player = s.player; gm.hud = s.hud; gm.score = s.score; gm.kills = s.kills; }
            GameManager.shotsFired = s.shotsFired; GameManager.shotsHit = s.shotsHit; GameManager.damageTaken = s.damageTaken;
            GameManager.headshots = s.headshots; GameManager.deaths = s.deaths;
            Equipment.I = s.equipment; Knife.I = s.knife; Torch.I = s.torch; Guide.I = s.guide; StealthRig.I = s.rig; Prove.I = s.prove;
            Stealth.Use(s.stealth); Medals.Use(s.medals); Proc.Interact.Use(s.interact); WallBuy.Use(s.wallBuy);
            PadControls.Use(s.pad);
        }

        /// <summary>Rimette nei globali i dati del posto che gira (dopo averne cambiato il giocatore o l'HUD a mano).</summary>
        static void Reload() { if (cur != null) Load(cur, GameManager.I); }

        /// <summary>Esegue a nome di ogni posto, uno alla volta (azzeramenti, premi per tutti...).</summary>
        public static void ForEach(Action<Seat> action)
        {
            if (All.Count == 0) return;
            var snapshot = All.ToArray();
            foreach (var s in snapshot) using (Enter(s)) action(s);
        }

        /// <summary>Come ForEach, ma fuori dallo split screen esegue una volta sola (con null): per quello che vale per "ogni giocatore", anche
        /// quando e' uno.</summary>
        public static void EachOrOnce(Action<Seat> action)
        {
            if (All.Count == 0) action(null); else ForEach(action);
        }

        /// <summary>Un avviso del mondo (ondata, piano, ascensore): lo vedono tutti, ognuno sul proprio HUD.</summary>
        public static void ForHuds(Action<HUD> action)
        {
            if (All.Count > 1) { foreach (var s in All) if (s.hud != null) action(s.hud); return; }
            var h = GameManager.I != null ? GameManager.I.hud : null;
            if (h != null) action(h);
        }

        /// <summary>Quanti nemici restano, su ogni HUD (senza closure: si chiama a ogni nascita e morte).</summary>
        public static void EnemiesLeft(int n)
        {
            if (All.Count > 1) { for (int i = 0; i < All.Count; i++) if (All[i].hud != null) All[i].hud.SetEnemiesLeft(n); return; }
            var h = GameManager.I != null ? GameManager.I.hud : null;
            if (h != null) h.SetEnemiesLeft(n);
        }

        /// <summary>Il bersaglio di chi gira (quello del suo posto; fuori dallo split screen, il giocatore locale).</summary>
        public static ITarget MyTarget => cur != null && cur.target != null ? cur.target : Targets.Local;

        /// <summary>Il posto di un bersaglio dei nemici (null se non e' di un posto).</summary>
        public static Seat SeatOf(ITarget t)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].target == t) return All[i];
            return null;
        }

        /// <summary>Un colpo subito da un giocatore (per il tabellone): conta nel posto di chi l'ha preso, anche se non e' quello che gira.</summary>
        public static void CountDamageTaken(ITarget victim)
        {
            var s = SeatOf(victim);
            if (s == null) { if (victim is LocalTarget) GameManager.damageTaken++; return; }
            if (s == cur) GameManager.damageTaken++; else s.damageTaken++;
        }

        // ------------------------------------------------------------------ occhi e giocatori (per i sistemi del mondo)
        static readonly List<Transform> eyes = new List<Transform>();

        /// <summary>Gli occhi (camere) di tutti i giocatori; senza split screen quello dato o, in mancanza, la camera principale.
        /// La lista e' riusata: va letta subito.</summary>
        public static List<Transform> Eyes(Transform fallback = null)
        {
            eyes.Clear();
            if (All.Count > 1) { foreach (var s in All) { var e = s.Eye; if (e != null) eyes.Add(e); } }
            else
            {
                var t = fallback != null ? fallback : (Camera.main != null ? Camera.main.transform : null);
                if (t != null) eyes.Add(t);
            }
            return eyes;
        }

        /// <summary>L'occhio (camera) piu' vicino a p: fuori dallo split screen la camera principale. Null se non c'e' nessuna camera.</summary>
        public static Transform NearestEye(Vector3 p)
        {
            var list = Eyes();
            Transform best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                float d = (list[i].position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = list[i]; }
            }
            return best;
        }

        /// <summary>Distanza al quadrato dall'occhio piu' vicino a p (infinito se non c'e' nessuna camera).</summary>
        public static float EyeDistanceSqr(Vector3 p)
        {
            var e = NearestEye(p);
            return e != null ? (e.position - p).sqrMagnitude : float.PositiveInfinity;
        }

        /// <summary>Il giocatore vivo piu' vicino a p (o, se sono tutti morti, il piu' vicino): per i sistemi del mondo che pensano a "il giocatore".
        /// Senza split screen e' GameManager.I.player.</summary>
        public static PlayerController NearestPlayer(Vector3 p, bool aliveOnly = true)
        {
            if (All.Count <= 1) return GameManager.I != null ? GameManager.I.player : null;
            PlayerController best = null, bestAny = null;
            float bd = float.MaxValue, bda = float.MaxValue;
            foreach (var s in All)
            {
                var pl = s.player;
                if (pl == null) continue;
                float d = (pl.transform.position - p).sqrMagnitude;
                if (d < bda) { bda = d; bestAny = pl; }
                if (aliveOnly && !pl.alive) continue;
                if (d < bd) { bd = d; best = pl; }
            }
            return best ?? bestAny;
        }

        /// <summary>Tutti i corpi dei giocatori (per i sistemi del mondo): in split screen quelli dei posti, altrimenti il solo giocatore.</summary>
        public static void Players(List<PlayerController> into)
        {
            into.Clear();
            if (All.Count > 1) { foreach (var s in All) if (s.player != null) into.Add(s.player); }
            else if (GameManager.I != null && GameManager.I.player != null) into.Add(GameManager.I.player);
        }

        /// <summary>Qualche giocatore (vivo o no) e' dentro l'area?</summary>
        public static bool AnyPlayerWithin(Vector3 p, float radius)
        {
            float r2 = radius * radius;
            if (All.Count <= 1)
            {
                var pl = GameManager.I != null ? GameManager.I.player : null;
                return pl != null && (pl.transform.position - p).sqrMagnitude <= r2;
            }
            foreach (var s in All) if (s.player != null && (s.player.transform.position - p).sqrMagnitude <= r2) return true;
            return false;
        }

        /// <summary>Scuote la visuale di ogni giocatore in base alla distanza (un'esplosione si sente su tutti gli schermi).</summary>
        public static void ShakeAll(Vector3 at, float strength = 1f, float radius = 12f)
        {
            if (All.Count > 1) { foreach (var s in All) if (s.player != null) s.player.Shake(at, strength, radius); return; }
            var p = GameManager.I != null ? GameManager.I.player : null;
            if (p != null) p.Shake(at, strength, radius);
        }

        /// <summary>Il numero piu' alto di minaccia (Stealth.Level) fra i posti: il cuore batte per chiunque sia in pericolo.</summary>
        public static float MaxThreat()
        {
            if (All.Count <= 1) return Stealth.Level;
            float m = 0f;
            foreach (var s in All) m = Mathf.Max(m, s == cur ? Stealth.Level : s.stealth.Level);
            return m;
        }
    }
}

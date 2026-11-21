using System.Collections.Generic;
using PryingEyes.Cattedrale;
using PryingEyes.Proc;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Deserto
{
    /// <summary>Un luogo del deserto caricato: un edificio (una sola unita') o una torre / catacomba (un'unita' per livello). Si costruisce a pezzi, un
    /// pezzo per passo (DesertDirector ne da' pochi millisecondi a fotogramma): prima il guscio di ogni unita' (muri, pavimenti, soffitti, le rampe),
    /// poi, solo se il giocatore e' vicino, gli arredi e le luci stanza per stanza, il bottino e le guardie. Quando si allontana gli arredi si
    /// tolgono (il guscio resta, lontano si vede), e quando e' lontano davvero tutto il luogo sparisce.
    /// Gli arredi dei piani stanno in coordinate assolute: ogni unita' si costruisce con la radice alla quota zero e poi la radice sale alla quota del
    /// luogo (un edificio al suo padY, un livello a LevelY); per arredare dopo la radice torna un attimo a zero.</summary>
    public sealed class SiteRt
    {
        struct Guard { public Vector3 pos; public Vector2 hmin, hmax; public Enemy.EnemyType type; public bool boss; public float hpMul, speedMul; }

        public readonly Site site;
        public Transform root;
        public readonly FloorInstance[] fis;
        public readonly bool[] want;
        readonly Dictionary<int, int>[] marks;          // stanza -> quanti figli aveva prima di arredarla (e quindi: arredata)
        readonly List<GameObject>[] loot;
        readonly List<Enemy>[] guards;
        readonly List<Guard>[] pending;
        readonly bool[] populated;
        public Enemy boss;
        public float dist, hdist;
        /// <summary>Il giocatore piu' vicino al luogo (da soli il suo; sull'ospite di una partita in cooperativa puo' essere un compagno): dalla sua posizione si decide cosa arredare e dove nascono le guardie.</summary>
        public Vector3 anchor;
        bool navMarked;
        public bool dressedAny;
        /// <summary>Che pezzo di lavoro ha fatto l'ultimo Step e su quale livello: il direttore li scrive nel registro quando un passo dura troppo.</summary>
        public string lastKind = "";
        public int lastUnit;

        /// <summary>Quante stanze sono arredate adesso, e quante se ne possono arredare in tutto (le stanze delle scale no): per le prove e per l'attesa.</summary>
        public int DressedRooms { get { int n = 0; foreach (var m in marks) n += m.Count; return n; } }
        public int DressableRooms { get { int n = 0; for (int u = 0; u < Units; u++) foreach (var r in PlanOf(u).rooms) if (Dressable(r)) n++; return n; } }

        /// <summary>Il livello u ha il guscio e tutte le sue stanze arredabili sono arredate (con gli arredi, le luci e, dopo, il bottino e le guardie): le fotografie e le prove aspettano questo.</summary>
        public bool UnitDressed(int u)
        {
            if (u < 0 || u >= Units || fis[u] == null) return false;
            foreach (var r in PlanOf(u).rooms) if (Dressable(r) && !marks[u].ContainsKey(r.id)) return false;
            return true;
        }

        public int Units => site.IsStack ? site.LevelCount : 1;

        /// <summary>Il guscio di tutto cio' che sta sopra la sabbia c'e' (di una catacomba basta l'ingresso): la pelle di chi sta lontano (SiteFacade) puo' spegnersi.</summary>
        public bool ShellComplete
        {
            get
            {
                if (site.IsCatacomb) return fis[site.entrance] != null;
                for (int u = 0; u < Units; u++) if (fis[u] == null) return false;
                return true;
            }
        }
        public Plan PlanOf(int u) => site.IsStack ? site.levels[u] : site.plan;
        public float YOf(int u) => site.IsStack ? site.LevelY(u) : site.padY;
        public Bounds Area => new Bounds(new Vector3(site.CentreX, site.padY + (site.IsTower ? site.Height * 0.5f : 3f), site.CentreZ),
            new Vector3(site.fx1 - site.fx0 + 8f, site.IsTower ? site.Height : 10f, site.fz1 - site.fz0 + 8f));

        public SiteRt(Site s)
        {
            site = s;
            int n = Units;
            fis = new FloorInstance[n];
            want = new bool[n];
            marks = new Dictionary<int, int>[n]; loot = new List<GameObject>[n]; guards = new List<Enemy>[n]; pending = new List<Guard>[n];
            populated = new bool[n];
            for (int i = 0; i < n; i++) { marks[i] = new Dictionary<int, int>(); loot[i] = new List<GameObject>(); guards[i] = new List<Enemy>(); pending[i] = new List<Guard>(); }
        }

        // ------------------------------------------------------------------ quale unita' interessa
        /// <summary>L'unita' (livello) da curare per prima: quella del giocatore se e' dentro la torre, altrimenti la porta d'ingresso.</summary>
        public int PreferredUnit(Vector3 p) => PreferredUnit(p, hdist);

        /// <summary>Come <see cref="PreferredUnit(Vector3)"/> per un giocatore in 'p' che sta a 'h' metri dal bordo del luogo.</summary>
        int PreferredUnit(Vector3 p, float h)
        {
            if (!site.IsStack) return 0;
            bool inside = h < 6f;
            if (!inside) return site.entrance;
            return Mathf.Clamp(Mathf.RoundToInt((p.y - site.LevelY(0)) / PlanGen.LevelHeight), 0, Units - 1);
        }

        /// <summary>Per le prove: -legacydress riporta l'arredo di come era fino al 20 novembre, deciso dal solo giocatore piu' vicino al luogo (-splitdesertotest, il passo della torre).</summary>
        public static readonly bool LegacyDress = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-legacydress") >= 0;

        /// <summary>Quali unita' devono avere gli arredi adesso. 'dressIn' e 'dressOut' sono le distanze (m) per cominciare e per smettere: l'isteresi
        /// evita che il confine faccia arredare e svuotare di continuo. Vale l'unione di quello che vuole ogni giocatore (il primo e gli altri punti: i posti dello schermo condiviso, sull'ospite i compagni
        /// di rete): in una torre o in una catacomba due giocatori a piani diversi vogliono piani diversi, e prima, con un punto solo (il piu' vicino, a pari distanza il primo), i livelli dell'altro si
        /// svuotavano di arredi, bottino e guardie.</summary>
        public void UpdateWant(Vector3 primary, List<Vector3> others, float dressIn, float dressOut)
        {
            for (int u = 0; u < Units; u++) want[u] = false;
            float limit = dressedAny ? dressOut : dressIn;
            if (LegacyDress) { WantFor(anchor, hdist, limit); return; }
            WantFor(primary, DesertAnchors.FootDist(site, primary), limit);
            for (int i = 0; i < others.Count; i++) WantFor(others[i], DesertAnchors.FootDist(site, others[i]), limit);
        }

        void WantFor(Vector3 q, float h, float limit)
        {
            if (h > limit) return;
            if (!site.IsStack) { want[0] = true; return; }
            int pu = PreferredUnit(q, h);
            if (h < 6f) { for (int u = pu - 1; u <= pu + 1; u++) if (u >= 0 && u < Units) want[u] = true; }
            else
            {
                want[site.entrance] = true;
                int adj = site.IsTower ? 1 : site.entrance - 1;
                if (adj >= 0 && adj < Units) want[adj] = true;
            }
        }

        // ------------------------------------------------------------------ un passo
        /// <summary>Un pezzo di lavoro (un guscio, una stanza arredata, il bottino di un'unita', due guardie, uno svuotamento). Falso se non c'e' niente da fare.</summary>
        public bool Step(Vector3 p)
        {
            int prefer = PreferredUnit(p);
            // 1. i gusci, dal piu' vicino alla scelta
            int best = -1;
            for (int u = 0; u < Units; u++)
                if (fis[u] == null && (best < 0 || Mathf.Abs(u - prefer) < Mathf.Abs(best - prefer))) best = u;
            if (best >= 0) { lastKind = "guscio"; lastUnit = best; BuildShell(best); return true; }
            if (!navMarked) { navMarked = true; DesertNav.Dirty(Area); }
            // 2. arredi e bottino delle unita' volute (dalla piu' vicina), svuotamento delle altre
            for (int k = 0; k < Units; k++)
            {
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    int u = prefer + sgn * k;
                    if (k == 0 && sgn == 1) continue;
                    if (u < 0 || u >= Units) continue;
                    lastUnit = u;
                    if (want[u])
                    {
                        if (DressNext(u)) { lastKind = "arredo"; return true; }
                        if (!populated[u]) { lastKind = "bottino"; Populate(u); return true; }
                        if (SpawnPending(u, p)) { lastKind = "guardia"; return true; }
                    }
                    else if (marks[u].Count > 0 || populated[u]) { lastKind = "svuotamento"; Undress(u); return true; }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ il guscio
        void BuildShell(int u)
        {
            if (root == null)
            {
                root = new GameObject($"Luogo_{site.cx}_{site.cz}_{site.kind}").transform;
                root.position = Vector3.zero;
            }
            var plan = PlanOf(u);
            var fi = BuildShellOf(site, u, root);
            fis[u] = fi;
            if (site.IsStack) DesertStairs.Build(fi.root.transform, plan, u, Units, site.church);
            fi.root.transform.position = new Vector3(0f, YOf(u), 0f);
            Physics.SyncTransforms();
        }

        /// <summary>Il guscio di un livello di un luogo, con le impostazioni dell'esterno di quel luogo (la tavolozza, i tetti, i merli, le torrette, la sabbia alla base). Lo usano il direttore e la
        /// prova dei muri (DesertCensus), che costruisce gli stessi muri senza caricare il luogo: un posto solo per le impostazioni, cosi' la prova guarda quello che si gioca.</summary>
        internal static FloorInstance BuildShellOf(Site site, int u, Transform root)
        {
            var plan = site.IsStack ? site.levels[u] : site.plan;
            int units = site.IsStack ? site.LevelCount : 1;
            FloorBuilder.SkipDress = true;
            FloorBuilder.FacadePalette = site.palette;
            // l'esterno: i tetti sull'ultimo livello (la cima di una torre, l'ingresso di una catacomba) e i merli sul parapetto di chi e' alto; i livelli sotto la sabbia non ne hanno
            bool topLevel = !site.IsStack || (site.IsTower ? u == units - 1 : u == site.entrance);
            FloorBuilder.FacadeRoofs = topLevel;
            FloorBuilder.FacadeCrown = site.IsStack && topLevel;
            FloorBuilder.FacadeTurrets = site.IsTower && topLevel;          // le torrette d'angolo sul tetto della torre
            FloorBuilder.SkipFacade = site.IsCatacomb && u != site.entrance;
            FloorBuilder.FacadeDrift = !site.IsStack || u == site.entrance;          // la sabbia accumulata alla base dei muri: solo dove i muri toccano il suolo
            try { return FloorBuilder.Build(plan, root); }
            finally
            {
                FloorBuilder.SkipDress = false; FloorBuilder.SkipFacade = false;
                FloorBuilder.FacadePalette = 0; FloorBuilder.FacadeRoofs = true; FloorBuilder.FacadeCrown = false; FloorBuilder.FacadeTurrets = false; FloorBuilder.FacadeDrift = false;
            }
        }

        // ------------------------------------------------------------------ arredi
        bool Dressable(Room r) => !r.noFloor && !r.noCeil;

        bool DressNext(int u)
        {
            var fi = fis[u];
            if (fi == null) return false;
            var plan = PlanOf(u);
            Room next = null;
            foreach (var r in plan.rooms) if (Dressable(r) && !marks[u].ContainsKey(r.id)) { next = r; break; }
            if (next == null) return false;
            var tr = fi.root.transform;
            Vector3 saved = tr.position;
            tr.position = Vector3.zero;                  // gli arredi sono in coordinate assolute: si arreda con la radice dov'era alla costruzione
            int before = fi.roomGo[next.id].transform.childCount;
            try { FloorBuilder.DressRoom(fi, next); }
            finally { tr.position = saved; }
            marks[u][next.id] = before;
            dressedAny = true;
            bool done = true;
            foreach (var r in plan.rooms) if (Dressable(r) && !marks[u].ContainsKey(r.id)) { done = false; break; }
            if (done) { Physics.SyncTransforms(); DesertNav.Dirty(Area); }
            return true;
        }

        void Undress(int u)
        {
            var fi = fis[u];
            if (fi != null)
                foreach (var kv in marks[u])
                {
                    var go = fi.roomGo[kv.Key];
                    if (go == null) continue;
                    var t = go.transform;
                    for (int i = t.childCount - 1; i >= kv.Value; i--) Object.Destroy(t.GetChild(i).gameObject);
                }
            marks[u].Clear();
            foreach (var g in loot[u]) if (g != null) Object.Destroy(g);
            loot[u].Clear();
            foreach (var e in guards[u]) if (e != null) { if (e == boss) boss = null; Enemy.Remove(e); }
            guards[u].Clear();
            pending[u].Clear();
            populated[u] = false;
            bool any = false;
            for (int i = 0; i < Units; i++) if (marks[i].Count > 0) any = true;
            dressedAny = any;
            if (fi != null) fi.propRoots.RemoveAll(x => x == null);
            DesertNav.Dirty(Area);
        }

        public void Unload()
        {
            for (int u = 0; u < Units; u++)
                foreach (var e in guards[u]) if (e != null) Enemy.Remove(e);
            boss = null;
            if (root != null) Object.Destroy(root.gameObject);
            DesertNav.Dirty(Area);
        }

        /// <summary>Le radici degli arredi (ostacoli per la navmesh) delle unita' arredate.</summary>
        public void CollectBlockers(List<Transform> into)
        {
            for (int u = 0; u < Units; u++)
            {
                var fi = fis[u];
                if (fi == null || marks[u].Count == 0) continue;
                foreach (var t in fi.propRoots) if (t != null) into.Add(t);
            }
        }

        // ------------------------------------------------------------------ bottino e guardie
        static readonly double[] KindWeights = { 36, 26, 16, 22 };      // punti, munizioni, medikit, essenza

        void Populate(int u)
        {
            populated[u] = true;
            var fi = fis[u];
            var plan = PlanOf(u);
            float y = YOf(u);
            float danger = DesertMode.Danger(site.x, site.z);
            var rng = new Rng(site.seed * 31 + u * 131 + 17);
            var rooms = new List<Room>();
            foreach (var r in plan.rooms) if (Dressable(r)) rooms.Add(r);
            if (rooms.Count == 0) return;
            bool bossUnit = site.IsStack && u == site.boss;
            Room hall = site.IsStack ? plan.rooms[1] : null;

            // le armi: poche e comuni, un pannello libero sul muro di una stanza (al boss uno un po' migliore)
            double wChance = site.IsStack ? (bossUnit ? 1.0 : site.IsCatacomb ? 0.22 : 0.16) : 0.45;
            if (rng.Chance(wChance))
            {
                int grade = bossUnit ? 2 : 1;
                var pool = new List<int>();
                int start = WeaponDefs.IndexOf("p9_pieta");
                for (int i = 0; i < WeaponDefs.All.Length; i++)
                {
                    var d = WeaponDefs.All[i];
                    if (i != start && !d.cityOnly && d.grade <= grade) pool.Add(i);
                }
                if (pool.Count > 0)
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        var room = rooms[rng.Range(0, rooms.Count)];
                        if (!TryWallSpot(plan, room, rng, y, out Vector3 pos, out Quaternion rot)) continue;
                        int wi = pool[rng.Range(0, pool.Count)];
                        var wb = WallBuy.Place(wi, 0, pos, rot, WeaponDefs.All[wi].GradeColor, true);
                        wb.transform.SetParent(fi.root.transform, true);
                        loot[u].Add(wb.gameObject);
                        break;
                    }
            }

            // punti, munizioni, medikit ed essenza sparsi nelle stanze
            int n = site.IsStack ? 3 + (bossUnit ? 3 : 0) : Mathf.Clamp(Mathf.RoundToInt(rooms.Count * 0.55f), 2, 9);
            for (int i = 0; i < n; i++)
            {
                var room = (bossUnit && i < 3 && hall != null) ? hall : rooms[rng.Range(0, rooms.Count)];
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    var q = new Vector3(rng.Range(room.x0 * 0.1f + 1.2f, room.x1 * 0.1f - 1.2f), y + 0.05f, rng.Range(room.z0 * 0.1f + 1.2f, room.z1 * 0.1f - 1.2f));
                    if (Physics.CheckSphere(q + Vector3.up * 0.55f, 0.4f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    var kind = (PickKind)rng.Weighted(KindWeights);
                    int amount = kind == PickKind.Points ? Mathf.RoundToInt(rng.Range(40f, 110f) * danger / 10f) * 10 : kind == PickKind.Medikit ? 50 : kind == PickKind.Essence ? 30 : 0;
                    var pk = DesertPickup.Spawn(fi.root.transform, q, kind, amount, DesertMode.Pack(site, u, i));
                    if (pk != null) loot[u].Add(pk.gameObject);
                    break;
                }
            }

            // le guardie, e il boss: nascono quando la navmesh c'e' (SpawnPending). Le fa chi simula (da soli o l'ospite): sull'ospitato i nemici sono i pupazzi che l'ospite manda
            if (!Coop.Sim) return;
            var grng = new Rng(site.seed * 17 + u * 7919 + (int)(Time.realtimeSinceStartup * 10f));
            int guardsN = site.IsStack ? 3 + Mathf.RoundToInt(danger) + (bossUnit ? 1 : 0) : Mathf.Clamp(Mathf.RoundToInt(rooms.Count * 0.4f * Mathf.Sqrt(danger)), 1, 9);
            for (int i = 0; i < guardsN; i++)
            {
                var room = rooms[grng.Range(0, rooms.Count)];
                pending[u].Add(MakeGuard(grng, room, y, danger, false));
            }
            if (bossUnit && hall != null && !DesertMode.BossesDown.Contains(DesertMode.Pack(site, u, 0)))
                pending[u].Add(MakeGuard(grng, hall, y, danger, true));
        }

        Guard MakeGuard(Rng rng, Room room, float y, float danger, bool isBoss)
        {
            float x0 = room.x0 * 0.1f + 1.0f, x1 = room.x1 * 0.1f - 1.0f, z0 = room.z0 * 0.1f + 1.0f, z1 = room.z1 * 0.1f - 1.0f;
            var g = new Guard
            {
                pos = new Vector3(isBoss ? (x0 + x1) * 0.5f : rng.Range(x0, x1), y + 0.1f, isBoss ? (z0 + z1) * 0.5f : rng.Range(z0, z1)),
                hmin = new Vector2(x0, z0), hmax = new Vector2(x1, z1),
                type = isBoss ? BossType() : PickType(rng, site.theme, danger),
                boss = isBoss,
                hpMul = 0.85f + 0.30f * danger,
                speedMul = 1f + 0.06f * (danger - 1f),
            };
            return g;
        }

        Enemy.EnemyType BossType()
        {
            var def = Rami.Tutti[DesertMode.BranchOf(site)];
            return System.Enum.TryParse(def.bossTipo, out Enemy.EnemyType t) ? t : Enemy.EnemyType.Cardinale;
        }

        /// <summary>Che nemico: monaci soprattutto, e piu' lontano dalla cattedrale piu' varieta' (abati, occhi, flagellanti, appestati, statue);
        /// nei teatri e nei sanatori spesso quelli del tema.</summary>
        public static Enemy.EnemyType PickType(Rng rng, Theme theme, float danger)
        {
            if (theme == Theme.Teatro && rng.Chance(0.45)) return Enemy.EnemyType.Maschera;
            if (theme == Theme.Sanatorio && rng.Chance(0.45)) return Enemy.EnemyType.Infermiere;
            var w = new[]
            {
                60.0,
                danger >= 1.3f ? 12.0 * (danger - 1f) : 0.0,       // abate
                danger >= 1.2f ? 13.0 : 0.0,                       // occhio
                danger >= 1.5f ? 9.0 : 0.0,                        // flagellante
                danger >= 1.8f ? 7.0 : 0.0,                        // appestato
                danger >= 2.3f ? 4.0 : 0.0,                        // statua
            };
            switch (rng.Weighted(w))
            {
                case 1: return Enemy.EnemyType.Abate;
                case 2: return Enemy.EnemyType.Occhio;
                case 3: return Enemy.EnemyType.Flagellante;
                case 4: return Enemy.EnemyType.Appestato;
                case 5: return Enemy.EnemyType.Statua;
                default: return Enemy.EnemyType.Monaco;
            }
        }

        bool SpawnPending(int u, Vector3 player)
        {
            var list = pending[u];
            if (list.Count == 0) return false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var g = list[i];
                bool fly = Enemy.IsFlying(g.type);
                Vector3 pos = g.pos;
                if (!fly)
                {
                    if (!DesertNav.Covers(g.pos)) continue;
                    if (!NavMesh.SamplePosition(g.pos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) { list.RemoveAt(i); continue; }
                    pos = hit.position;
                }
                else pos = g.pos + Vector3.up * 1.7f;
                if ((player - pos).sqrMagnitude < 25f || DesertAnchors.NearOther(pos, 5f)) continue;            // non a due passi da un giocatore appena entrato
                list.RemoveAt(i);
                var e = Enemy.Spawn(g.type, pos);
                if (e == null) return true;
                e.maxHp *= g.hpMul; e.hp = e.maxHp; e.speed *= g.speedMul;
                e.Doze();                       // riposa nella sua stanza finche' il giocatore non e' vicino (DesertDirector.WakeTick)
                if (!fly) e.SetHome(g.hmin, g.hmax);
                guards[u].Add(e);
                if (g.boss)
                {
                    boss = e;
                    int ramo = DesertMode.BranchOf(site);
                    var def = Rami.Tutti[ramo];
                    float danger = DesertMode.Danger(site.x, site.z);
                    float hp = (3200f + 700f * (danger - 1f)) * (1f + 0.25f * ramo);
                    Enemy.MakeBranchBoss(e, def.bossTitolo, hp, ramo == 3);
                    // il signore del luogo si annuncia a chi gli e' piu' vicino: a un compagno lontano dall'ospite lo dice l'ospite (e non a tutti: il boss e' laggiu')
                    int heard = DesertAnchors.NearestSlot(pos);
                    if (heard == Coop.Slot || !Coop.Active)
                    {
                        Seats.ForHuds(h => h.Banner(def.bossTitolo.ToUpperInvariant(), "Il signore di questo luogo ti ha sentito arrivare."));
                        Sfx.UI(SoundBank.BossRoar, 0.8f, 0.85f);
                    }
                    else Coop.Game?.Announce(heard, 1, def.bossTitolo.ToUpperInvariant(), "Il signore di questo luogo ti ha sentito arrivare.");
                }
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ un posto libero a un muro (per un pannello d'arma)
        bool TryWallSpot(Plan plan, Room r, Rng rng, float yBase, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (r.kind == RoomKind.Corridoio || r.wallH < 2.2f) return false;
            float T = FloorBuilder.TH;
            var sides = new List<Side> { Side.S, Side.N, Side.W, Side.E };
            for (int i = sides.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); var t = sides[i]; sides[i] = sides[j]; sides[j] = t; }
            foreach (var s in sides)
            {
                bool ax = Plan.AlongX(s);
                float inset = Mathf.Max(T + 1.2f, r.cornerRadius > 0f ? r.cornerRadius + 0.85f : 0f);
                float lo = (ax ? r.x0 : r.z0) * 0.1f + inset, hi = (ax ? r.x1 : r.z1) * 0.1f - inset;
                if (hi - lo < 0.5f) continue;
                Vector3 inward = s == Side.S ? Vector3.forward : s == Side.N ? Vector3.back : s == Side.W ? Vector3.right : Vector3.left;
                float face = s == Side.S ? r.z0 * 0.1f + T : s == Side.N ? r.z1 * 0.1f - T : s == Side.W ? r.x0 * 0.1f + T : r.x1 * 0.1f - T;
                var ops = plan.OpeningsOn(r, s);
                for (int tries = 0; tries < 24; tries++)
                {
                    float c = rng.Range(lo, hi);
                    bool blocked = false;
                    foreach (var o in ops) if (Mathf.Abs(c - o.center * 0.1f) < o.width * 0.05f + 1.05f) { blocked = true; break; }
                    if (blocked) continue;
                    Vector3 wp = ax ? new Vector3(c, yBase + 1.55f, face) : new Vector3(face, yBase + 1.55f, c);
                    Vector3 mid = wp + inward * 0.55f;
                    if (Physics.CheckBox(mid, new Vector3(0.7f, 0.55f, 0.5f), Quaternion.LookRotation(inward), HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    pos = wp + inward * 0.03f;
                    rot = Quaternion.LookRotation(inward);
                    return true;
                }
            }
            return false;
        }
    }
}

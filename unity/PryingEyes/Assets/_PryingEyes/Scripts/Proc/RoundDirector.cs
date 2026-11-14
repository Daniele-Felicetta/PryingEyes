using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Come ti sei presentato al round: nessun prete sapeva di te (calmo: meno nemici, ci metti piu' a partire e c'e' un premio),
    /// qualcuno ti cercava gia' (in allarme: piu' nemici, subito) o una via di mezzo.</summary>
    public enum RoundMood { Calm, Normal, Alarm }

    /// <summary>Le regole dei round in un posto solo, per tararle senza cercarle in giro (e per le prove).</summary>
    public static class RoundRules
    {
        public const float CalmMul = 0.75f, AlarmMul = 1.25f;
        public const float DelayCalm = 6f, DelayNormal = 2.5f, DelayAlarm = 1.5f;     // secondi dalla chiusura alla prima nascita
        public const int CalmBonus = 150;
        public static float Failsafe = 240f;                                          // oltre, il round finisce da solo (senza premio); non const per le prove

        /// <summary>Quanti nemici in tutto: cresce con il round e, un po', con il piano.</summary>
        public static int Count(int round, int floor) => Mathf.Clamp(Mathf.RoundToInt(12f + 2.5f * round + 1.5f * floor), 16, 72);
        public static int CountForArea(int round,int floor,float areaM2) => Count(round,floor)+Mathf.Clamp(Mathf.RoundToInt((areaM2-240)/90),0,14);
        /// <summary>Salute: +6% a round fino al 16, poi esponenziale (come i Black Ops Zombies, dove dal 10 in poi si fa piu' fatica).</summary>
        public static float HpMul(int round) => round <= 16 ? 1f + 0.06f * (round - 1) : 1.9f * Mathf.Pow(1.035f, round - 16);
        public static float SpeedMul(int round) => Mathf.Min(1.8f, 1f + 0.025f * (round - 1));
        /// <summary>Quanti vivi insieme: dipende da quanto e' grande la zona.</summary>
        public static int AliveCap(float areaM2) => Mathf.Clamp(Mathf.RoundToInt(areaM2 / 28f), 8, 24);
        public static float SpawnGap(int round) => Mathf.Lerp(0.75f, 0.35f, Mathf.Clamp01((round - 1) / 20f));
        public static int Reward(int round) => 100 + 20 * round;

        /// <summary>Ogni quinto round e' la Processione dei Lumi (come i round dei cani nei Black Ops Zombies): solo Veglie, una volta e mezzo
        /// i nemici, piu' vivi insieme, e a fine round un Reliquiario di regalo. Le falene volano svelte e hanno poca salute.</summary>
        public const int MothEvery = 5;
        public const float MothCountMul = 1.6f;
        public const int MothAliveBonus = 4;
        public static bool IsMoth(int round) => round > 0 && round % MothEvery == 0;
    }

    /// <summary>Serranda d'acciaio su un'apertura: scende dall'architrave (la parte alta resta dentro il muro, non si vede) con una
    /// striscia rossa luminosa in fondo e una luce che pulsa. Il collider e' sullo strato delle porte: ferma il giocatore, la vista
    /// e i colpi. I nemici a piedi li ferma la navmesh (area della soglia tolta dalla maschera), quelli che volano anche il collider.</summary>
    public sealed class Shutter : MonoBehaviour
    {
        const float FallTime = 0.55f, RiseTime = 1.6f;
        public Opening o;
        public bool alongX;
        public float inward;                // +1 / -1: da che parte dell'asse perpendicolare al muro sta l'interno della zona
        public float width, height;         // m (di poco piu' larghe e alte dell'apertura: mordono gli stipiti)
        public Vector3 center;              // sul muro, a terra
        Transform slab;
        PoolLight pool;
        float t;                            // 0 = tutta su (dentro il muro), 1 = tutta giu'
        bool falling, slammed;

        public bool IsUp => !falling && t <= 0f;
        public bool IsDown => falling && t >= 1f;
        public float Progress => falling ? t * t : t * t * (3f - 2f * t);

        /// <summary>Posizione lungo il muro rispetto al centro dell'apertura (m).</summary>
        public float Along(Vector3 p) { Vector3 d = p - center; return alongX ? d.x : d.z; }
        /// <summary>Distanza dal muro, positiva verso l'interno della zona (m).</summary>
        public float Across(Vector3 p) { Vector3 d = p - center; return (alongX ? d.z : d.x) * inward; }

        static Material redMat;
        static Material Red()
        {
            if (redMat == null) redMat = EnemyMats.Glow(new Color(1f, 0.07f, 0.04f), 2.4f);
            return redMat;
        }

        public static Shutter Make(Transform parent, Opening op, float inwardSign)
        {
            bool ax = op.alongX;
            float line = op.line * 0.1f, c = op.center * 0.1f;
            var go = new GameObject("Serranda");
            go.transform.SetParent(parent, false);
            go.transform.position = ax ? new Vector3(c, 0f, line) : new Vector3(line, 0f, c);
            go.transform.rotation = Quaternion.Euler(0f, ax ? 0f : 90f, 0f);     // l'asse locale X corre lungo il muro, Z lo attraversa
            var s = go.AddComponent<Shutter>();
            s.o = op; s.alongX = ax; s.inward = inwardSign;
            s.width = op.width * 0.1f + 0.16f;
            s.height = Mathf.Max(1.2f, op.top) + 0.12f;
            s.center = go.transform.position;

            float W = s.width, H = s.height;
            var steel = ProcKit.Mat("acciaio");
            var iron = ProcKit.Mat("ferro");
            var mb = new MeshBuilder();
            mb.Box(steel, new Vector3(0f, H * 0.5f, 0f), new Vector3(W, H, 0.05f));                          // il telo
            for (float y = 0.12f; y < H - 0.05f; y += 0.12f)                                                  // le doghe
                mb.Box(iron, new Vector3(0f, y, 0f), new Vector3(W - 0.04f, 0.04f, 0.085f));
            mb.Box(iron, new Vector3(-W * 0.5f + 0.035f, H * 0.5f, 0f), new Vector3(0.07f, H, 0.14f));        // le guide laterali
            mb.Box(iron, new Vector3(W * 0.5f - 0.035f, H * 0.5f, 0f), new Vector3(0.07f, H, 0.14f));
            mb.Box(Red(), new Vector3(0f, 0.075f, 0f), new Vector3(W - 0.12f, 0.12f, 0.12f));                 // la barra rossa in fondo
            var mesh = mb.ToMesh("Serranda", out Material[] mats);

            var slabGo = new GameObject("Telo");
            slabGo.transform.SetParent(go.transform, false);
            slabGo.layer = HotelNav.DoorLayer;
            slabGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            slabGo.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var col = slabGo.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, H * 0.5f, 0f);
            col.size = new Vector3(W, H, 0.2f);
            s.slab = slabGo.transform;

            var lg = new GameObject("LuceSerranda");
            lg.transform.SetParent(go.transform, false);
            lg.transform.localPosition = new Vector3(0f, Mathf.Min(2.1f, H - 0.25f), 0.6f * inwardSign);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.10f, 0.06f);
            l.intensity = 1.6f;
            l.range = 5.5f;
            l.shadows = LightShadows.None;
            s.pool = LightPool.Register(l, false, false);
            var pulse = lg.AddComponent<LightPulse>();
            pulse.l = l; pulse.baseI = 1.6f; pulse.amp = 0.45f; pulse.speed = 4.5f;

            s.Apply();
            return s;
        }

        void Apply()
        {
            float p = Progress;
            slab.localPosition = new Vector3(0f, height * (1f - p), 0f);
            if (pool != null) pool.wanted = p > 0.15f;
        }

        public void Drop() { falling = true; slammed = false; }

        public void Release()
        {
            if (!falling && t <= 0f) return;
            falling = false;
            Sfx.At(SoundBank.Doors, center + Vector3.up * 1.2f, 0.8f, 0.75f, 3f, 40f);
        }

        public void SnapUp() { falling = false; t = 0f; Apply(); }
        public void SnapDown() { falling = true; t = 1f; slammed = true; Apply(); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (falling)
            {
                if (t >= 1f) return;
                t = Mathf.Min(1f, t + dt / FallTime);
                Apply();
                if (t >= 1f) Slam();
            }
            else if (t > 0f)
            {
                t = Mathf.Max(0f, t - dt / RiseTime);
                Apply();
            }
        }

        void Slam()
        {
            if (slammed) return;
            slammed = true;
            Sfx.At(SoundBank.Clang, center + Vector3.up * 1.2f, 1f, Random.Range(0.9f, 1.05f), 3f, 48f);
            var gm = GameManager.I;
            if (gm != null && gm.player != null && (gm.player.transform.position - center).sqrMagnitude < 100f)
                gm.player.Kick(1.8f, Random.Range(-1f, 1f), 9f);
        }
    }

    /// <summary>Ingresso breve in cenere e pulviscolo, senza anello a terra o colonna luminosa.</summary>
    public static class RoundFlare
    {
        public static void At(Vector3 position)
        {
            Fx.EnemyImpact(position + Vector3.up * 0.4f, Vector3.up, new Color(0.42f, 0.36f, 0.28f));
            Sfx.At(SoundBank.Whisper, position + Vector3.up, 0.3f, Random.Range(0.8f, 1f), 2f, 18f);
        }
    }

    /// <summary>Round stile Black Ops Zombies dentro una zona sigillabile del piano: quando si accende la candela di una zona (vedi
    /// RoundRegions) le serrande calano su tutte le aperture che portano fuori, dentro nascono i nemici del round (sempre all'attacco,
    /// piu' numerosi, forti e veloci a ogni round) e, quando sono tutti a terra, le serrande salgono, la candela conta e si prende il
    /// premio. Il numero del round e' unico per la partita e non riparte da zero ai piani successivi. Se il giocatore muore il round
    /// salta: serrande su, nemici via, candela di nuovo spenta da accendere.</summary>
    public partial class RoundDirector : MonoBehaviour
    {
        public static RoundDirector I;
        /// <summary>L'ultimo round cominciato (quello in corso, o l'ultimo finito).</summary>
        public static int Round;

        static int enabledState;
        public static bool Enabled
        {
            get
            {
                if (enabledState == 0)
                {
                    bool off = HasArg("-norounds") || (GameManager.autoTest && HasArg("-procbot") && !HasArg("-rounds"));
                    enabledState = off ? 2 : 1;
                }
                return enabledState == 1;
            }
        }

        static bool HasArg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs()) if (s == a) return true;
            return false;
        }

        enum Phase { Idle, Fighting, Opening }

        sealed class Zone
        {
            public RoundRegion reg;
            public Candelabro candle;
            public bool done;
            public readonly List<Shutter> shutters = new List<Shutter>();
        }

        // le soglie del piano in corso: apertura -> area della navmesh (le riempie PrepareNav prima di costruire la navmesh)
        static readonly Dictionary<Opening, int> areaOf = new Dictionary<Opening, int>();
        static int doorBits;

        readonly List<Zone> zones = new List<Zone>();
        readonly List<Enemy> mine = new List<Enemy>();
        FloorInstance fi;
        Zone cur;
        Phase phase;
        RoundMood mood;
        int target, spawned, cap, fails;
        float clock, delay, spawnT, endT, gap;

        bool moth;

        /// <summary>Il round in corso (o l'ultimo) e' una Processione dei Lumi.</summary>
        public bool Moth => moth;
        /// <summary>Il nemico e' di questo round (nato o adottato dalle serrande). Lo usa la Campana.</summary>
        public bool Owns(Enemy e) => mine.Contains(e);

        public bool Fighting => phase == Phase.Fighting;
        public bool Sealed => SurvivalMode.Enabled ? fi != null : phase != Phase.Idle;
        public int Target => target;
        public int Spawned => spawned;
        public int Alive => mine.Count;
        public RoundMood Mood => mood;
        public float Clock => clock;
        public IReadOnlyList<Shutter> Shutters => cur != null ? (IReadOnlyList<Shutter>)cur.shutters : new List<Shutter>();
        public int ZoneCount => zones.Count;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        /// <summary>Nuova partita: il numero del round riparte da zero.</summary>
        public static void ResetRun()
        {
            Round = 0;
            if (I != null) I.Disarm();
            var gm = GameManager.I;
            if (gm != null && gm.hud != null) gm.hud.SetRound(0);
        }

        // ------------------------------------------------------------------ piano
        /// <summary>Prima di costruire la navmesh: trova le zone sigillabili del piano e prepara le sue soglie, ognuna con la sua area.</summary>
        public static List<RoundRegion> PrepareNav(Plan plan)
        {
            HotelNav.DoorBoxes.Clear();
            areaOf.Clear();
            doorBits = 0;
            HotelNav.Unseal();
            if (SurvivalMode.Enabled) { RoomAccessDoors.PrepareNav(plan); return null; }
            if (!Enabled) return null;
            var found = RoundRegions.Find(plan);
            var kept = new List<RoundRegion>();
            int next = HotelNav.FirstDoorArea;
            foreach (var r in found)
            {
                int fresh = 0;
                foreach (var o in r.cuts) if (!areaOf.ContainsKey(o)) fresh++;
                if (next + fresh - 1 > HotelNav.LastDoorArea) continue;       // le aree sono 29: oltre, la zona resta com'e'
                foreach (var o in r.cuts)
                {
                    if (areaOf.ContainsKey(o)) continue;
                    areaOf[o] = next;
                    doorBits |= 1 << next;
                    HotelNav.DoorBoxes.Add(Band(o, next));
                    next++;
                }
                kept.Add(r);
            }
            return kept;
        }

        /// <summary>La fascia di navmesh da marcare sotto un'apertura: larga come l'apertura (piu' un margine) e profonda 1,4 m, a cavallo del muro.</summary>
        internal static HotelNav.DoorBox Band(Opening o, int area)
        {
            float w = o.width * 0.1f + 0.3f;
            bool ax = o.alongX;
            return new HotelNav.DoorBox
            {
                center = ax ? new Vector3(o.center * 0.1f, 0.9f, o.line * 0.1f) : new Vector3(o.line * 0.1f, 0.9f, o.center * 0.1f),
                size = ax ? new Vector3(w, 1.8f, 1.4f) : new Vector3(1.4f, 1.8f, w),
                yaw = 0f,
                area = area,
            };
        }

        /// <summary>Dopo aver costruito il piano e messo le candele: abbina ogni zona al suo candelabro.</summary>
        public void Arm(FloorInstance f, List<RoundRegion> regions)
        {
            Disarm();
            fi = f;
            if (SurvivalMode.Enabled && f != null) { ArmSurvival(); return; }
            if (regions == null || f == null) return;
            foreach (var r in regions)
            {
                Candelabro c = null;
                foreach (var cd in f.candles)
                {
                    if (cd == null) continue;
                    var room = f.RoomAt(cd.transform.position);
                    if (room != null && room.id == r.candleRoom) { c = cd; break; }
                }
                if (c == null) continue;
                zones.Add(new Zone { reg = r, candle = c });
                if (GameManager.autoTest) Debug.Log($"[PryingEyes] round: zona {r}");
            }
            Debug.Log($"[PryingEyes] round: {zones.Count} zone sigillabili su {f.candles.Count} candele");
        }

        /// <summary>Si cambia piano (o si riparte): niente round in corso, niente zone. Le serrande sono figlie del piano e vanno via con lui.</summary>
        public void Disarm()
        {
            mine.Clear();
            moth = false;
            zones.Clear();
            cur = null;
            fi = null;
            phase = Phase.Idle;
            HotelNav.Unseal();
        }

        /// <summary>Area della navmesh sotto un'apertura che taglia una zona (-1 se non e' tra quelle sigillabili). Per le prove.</summary>
        public static int AreaOf(Opening o) => areaOf.TryGetValue(o, out int a) ? a : -1;

        public bool HasZone(Candelabro c) => zones.Exists(z => z.candle == c);
        public RoundRegion RegionOf(Candelabro c) => zones.Find(z => z.candle == c)?.reg;

        bool Inside(Zone z, Vector3 p)
        {
            var room = fi != null ? fi.RoomAt(p) : null;
            return room != null && z.reg.Has(room.id);
        }

        /// <summary>Vero se il punto e' dentro la zona sigillata adesso (i richiami da fuori non nascono la').</summary>
        public bool InsideSealed(Vector3 p) => phase != Phase.Idle && cur != null && Inside(cur, p);

        // ------------------------------------------------------------------ inizio
        /// <summary>Chiamato quando si accende una candela. Se e' di una zona sigillabile e il giocatore e' dentro, chiude le serrande e
        /// parte il round (vero); altrimenti non fa nulla (falso) e la candela vale come sempre.</summary>
        public bool TryStart(Candelabro c)
        {
            if (!Enabled || phase != Phase.Idle || fi == null || c == null) return false;
            var fd = FloorDirector.I;
            var gm = GameManager.I;
            if (fd == null || gm == null || gm.player == null || fd.transitioning) return false;
            var by = Targets.OfSlot(c.litBy) ?? Targets.Local;       // chi l'ha accesa (in cooperativa puo' essere un compagno)
            if (!by.Alive) return false;
            var z = zones.Find(q => q.candle == c);
            if (z == null || z.done) return false;
            var pl = gm.player;
            var here = fi.RoomAt(by.Feet);
            if (here == null || !z.reg.Has(here.id)) return false;     // la candela l'ha accesa da fuori: niente round

            cur = z;
            mood = Stealth.Hunters > 0 ? RoundMood.Alarm : Stealth.Aware == 0 ? RoundMood.Calm : RoundMood.Normal;
            Round++;
            moth = RoundRules.IsMoth(Round);
            clock = 0f; spawned = 0; endT = 0f; spawnT = 0f; fails = 0;
            delay = mood == RoundMood.Calm ? RoundRules.DelayCalm : mood == RoundMood.Alarm ? RoundRules.DelayAlarm : RoundRules.DelayNormal;
            cap = RoundRules.AliveCap(z.reg.area) + (moth ? RoundRules.MothAliveBonus : 0);
            gap = RoundRules.SpawnGap(Round);
            mine.Clear();

            // le serrande: la navmesh chiude subito, il metallo cala in mezzo secondo
            BuildShutters(z);
            var seal = new List<int>();
            foreach (var o in z.reg.cuts) if (areaOf.TryGetValue(o, out int a)) seal.Add(a);
            HotelNav.SealAreas(seal);
            ClearBands(z);
            foreach (var s in z.shutters) s.Drop();
            Physics.SyncTransforms();

            // chi era gia' dentro la zona entra nel round (e conta nel totale)
            int adopted = 0;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.IsDead || !Inside(z, e.transform.position)) continue;
                Scale(e);
                e.MakeRelentless();
                mine.Add(e);
                adopted++;
            }
            float mul = (mood == RoundMood.Calm ? RoundRules.CalmMul : mood == RoundMood.Alarm ? RoundRules.AlarmMul : 1f) * (moth ? RoundRules.MothCountMul : 1f);
            target = Mathf.Max(2, Mathf.RoundToInt(RoundRules.CountForArea(Round, fd.floor,z.reg.area) * mul) - adopted);
            phase = Phase.Fighting;

            gm.hud?.SetRound(Round);
            if (moth) gm.hud?.Banner("Processione dei Lumi", "Round " + Round + "  ·  Le candele hanno lasciato i loro altari.");
            else gm.hud?.Banner("Round " + Round, mood == RoundMood.Calm ? "Nessuno sa che sei qui." : "Le serrande sono calate.");
            Sfx.UI(SoundBank.Toll, 0.55f, 0.55f);
            if (moth) Sfx.UI(SoundBank.LumiProcession, 0.7f);
            Debug.Log($"[PryingEyes] round {Round} (piano {fd.floor}, {mood}): {target} nemici in {z.reg.area:F0} m2, massimo {cap} insieme, {adopted} gia' dentro, {z.shutters.Count} serrande");
            statusT = 0f;
            Coop.Game?.RoundStarted(zones.IndexOf(z), Round, moth, (int)mood);     // cooperativa: l'ospite lo racconta agli altri
            return true;
        }

        void BuildShutters(Zone z)
        {
            z.shutters.Clear();
            foreach (var o in z.reg.cuts)
            {
                int inRoom = z.reg.Has(o.a) ? o.a : o.b;
                var room = fi.plan.rooms[inRoom];
                float sign = o.alongX ? (room.CenterZ > o.line * 0.1f ? 1f : -1f) : (room.CenterX > o.line * 0.1f ? 1f : -1f);
                z.shutters.Add(Shutter.Make(fi.root.transform, o, sign));
            }
        }

        /// <summary>Chi sta sulla soglia quando la serranda cala viene portato da una parte o dall'altra (il giocatore e chi e' dentro:
        /// dentro; chi e' fuori: fuori). Gli agenti sulla soglia sigillata non avrebbero piu' un percorso.</summary>
        void ClearBands(Zone z)
        {
            foreach (var s in z.shutters)
            {
                for (int i = 0, n = Targets.Count; i < n; i++)
                {
                    var t = Targets.At(i);
                    if (!t.Alive) continue;
                    Vector3 pp = t.Feet;
                    if (!InBand(s, pp)) continue;
                    Vector3 q = SidePoint(s, pp, true);
                    var dest = new Vector3(q.x, pp.y, q.z);
                    if (t is LocalTarget) GameManager.I.player.Teleport(dest);
                    else Coop.Game?.TeleportRemote(t.Slot, dest);          // un compagno sulla soglia: lo sposta il suo gioco
                }
                foreach (var e in Enemy.All)
                {
                    if (e == null || e.IsDead) continue;
                    Vector3 ep = e.transform.position;
                    if (!InBand(s, ep)) continue;
                    Vector3 q = SidePoint(s, ep, s.Across(ep) > 0f);
                    if (!Enemy.IsFlying(e.type) && NavMesh.SamplePosition(q, out NavMeshHit h, 1.5f, HotelNav.EnemyMask)) q = h.position;
                    else q.y = ep.y;
                    e.Nudge(q);
                }
            }
        }

        static bool InBand(Shutter s, Vector3 p) => Mathf.Abs(s.Along(p)) < s.width * 0.5f + 0.45f && Mathf.Abs(s.Across(p)) < 0.8f;

        /// <summary>Punto a un metro dal muro, dalla parte interna o esterna, alla stessa posizione lungo il muro (dentro l'apertura).</summary>
        static Vector3 SidePoint(Shutter s, Vector3 p, bool inside)
        {
            float half = Mathf.Max(0f, s.width * 0.5f - 0.5f);
            float along = Mathf.Clamp(s.Along(p), -half, half);
            float across = (inside ? 1f : -1f) * 1.0f * s.inward;
            return s.alongX ? new Vector3(s.center.x + along, p.y, s.center.z + across) : new Vector3(s.center.x + across, p.y, s.center.z + along);
        }

        // ------------------------------------------------------------------ nemici
        static void Scale(Enemy e)
        {
            e.maxHp *= RoundRules.HpMul(Round);
            e.hp = e.maxHp;
            // Survival applica già alla nascita la curva completa, con un tetto alla decima ondata.
            if (!SurvivalMode.Enabled) e.speed *= RoundRules.SpeedMul(Round);
        }

        Room PickRoom(Zone z)
        {
            float total = 0f;
            foreach (int id in z.reg.rooms) total += fi.plan.rooms[id].W * fi.plan.rooms[id].D;
            float r = Random.value * total;
            foreach (int id in z.reg.rooms)
            {
                var room = fi.plan.rooms[id];
                r -= room.W * room.D;
                if (r <= 0f) return room;
            }
            return fi.plan.rooms[z.reg.rooms[0]];
        }

        bool SpawnOne()
        {
            var fd = FloorDirector.I;
            int spawnMask = NavMesh.AllAreas & ~doorBits;
            for (int pass = 0; pass < 3; pass++)
            {
                float dmin = pass == 0 ? 6.5f : pass == 1 ? 4.5f : 2.5f;
                for (int tries = 0; tries < 24; tries++)
                {
                    var room = PickRoom(cur);
                    float x0 = room.x0 * 0.1f + 1.0f, x1 = room.x1 * 0.1f - 1.0f, z0 = room.z0 * 0.1f + 1.0f, z1 = room.z1 * 0.1f - 1.0f;
                    var q = new Vector3(x1 > x0 ? Random.Range(x0, x1) : room.CenterX, 0f, z1 > z0 ? Random.Range(z0, z1) : room.CenterZ);
                    if (room.upperGallery && Random.value < 0.3f)
                    {
                        var routes=fi.galleries.FindAll(g=>g.room==room);
                        if(routes.Count>0)
                        {
                            var route=routes[Random.Range(0,routes.Count)];
                            for(int attempt=0;attempt<4;attempt++)
                            {
                                var upper=route.points[Random.Range(1,route.points.Length-1)];
                                if(upper.y>2.5f){q=upper;break;}
                            }
                        }
                    }
                    if (Targets.NearestDistance(q) < dmin) continue;
                    var t = moth ? Enemy.EnemyType.Veglia : fd.PickRoundType(room);
                    bool fly = Enemy.IsFlying(t);
                    if (fly)
                    {
                        q.y = Mathf.Min(q.y + 2.2f, room.wallH - 0.7f);
                        if (Physics.CheckSphere(q, 0.45f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    }
                    else
                    {
                        if (!NavMesh.SamplePosition(q, out NavMeshHit nh, 1.5f, spawnMask)) continue;
                        if (new Vector2(nh.position.x - q.x, nh.position.z - q.z).magnitude > 1.2f || !Inside(cur, nh.position)) continue;
                        q = nh.position;
                    }
                    if (pass == 0 && Targets.AnySees(q + Vector3.up * 1.2f)) continue;               // si vede nascere: meglio altrove
                    var e = Enemy.Spawn(t, q);
                    if (e == null) continue;
                    Scale(e);
                    e.MakeRelentless();
                    mine.Add(e);
                    RoundFlare.At(q);
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ svolgimento
        void Update()
        {
            if (SurvivalMode.Enabled) { TickSurvival(Time.deltaTime); return; }
            if (phase == Phase.Idle) return;
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            float dt = Time.deltaTime;
            if (Coop.IsClient) { ClientTick(dt); return; }        // sugli ospitati il round lo gioca l'ospite: qui si vede soltanto
            if (!Targets.AnyAlive) { Abort(); return; }           // da solo: il giocatore e' morto; in cooperativa: sono morti tutti
            if (phase == Phase.Fighting)
            {
                statusT -= dt;
                if (statusT <= 0f) { statusT = 0.5f; Coop.Game?.RoundStatus(target, spawned, mine.Count); }
            }
            if (phase == Phase.Opening)
            {
                bool up = true;
                foreach (var s in cur.shutters) if (s != null && !s.IsUp) up = false;
                if (up) Finish();
                return;
            }
            clock += dt;
            mine.RemoveAll(e => e == null || e.IsDead);
            if (clock >= delay && spawned < target)
            {
                spawnT -= dt;
                if (spawnT <= 0f && mine.Count < cap)
                {
                    int batch = Mathf.Min(spawned == 0 ? 4 : mine.Count < cap / 2 ? 2 : 1, Mathf.Min(cap - mine.Count, target - spawned));
                    bool added = false;
                    for (int i = 0; i < batch; i++)
                    {
                        if (!SpawnOne()) break;
                        spawned++; added = true;
                    }
                    if (added) { fails = 0; spawnT = gap * Random.Range(0.8f, 1.25f); }
                    else
                    {
                        spawnT = 0.6f;
                        if (++fails > 40) { Debug.LogWarning($"[PryingEyes] round {Round}: nessun punto di nascita libero, il round finisce con {spawned}/{target}"); target = spawned; }
                    }
                }
            }
            if (spawned >= target && mine.Count == 0)
            {
                endT += dt;
                if (endT >= 1.3f) Complete(true);
            }
            else endT = 0f;
            if (phase == Phase.Fighting && clock > RoundRules.Failsafe) Timeout();      // (non dopo Complete: nello stesso fotogramma sarebbe un doppio finale)
        }

        /// <summary>Testo per la riga dell'obiettivo mentre il round e' in corso.</summary>
        public string Status()
        {
            if (SurvivalMode.Enabled) return SurvivalStatus();
            if (cur == null) return "";
            string head = moth ? $"PROCESSIONE DEI LUMI {Round}" : $"ROUND {Round}";
            if (clock < delay) return $"{head}  ·  le serrande sono calate";
            int left = Coop.IsClient ? Mathf.Max(0, netTarget - netSpawned) + netAlive : Mathf.Max(0, target - spawned) + mine.Count;
            return $"{head}  ·  {(left == 1 ? "una presenza" : left + " presenze")} ancora";
        }

        // ------------------------------------------------------------------ cooperativa: gli ospitati vedono il round, lo gioca l'ospite
        int netTarget, netSpawned, netAlive;
        float statusT;

        /// <summary>Indice della zona del round in corso tra quelle del piano (-1 se non c'e'): lo stesso su ogni macchina.</summary>
        public int CurrentZone => SurvivalMode.Enabled && fi != null ? 0 : cur != null ? zones.IndexOf(cur) : -1;

        /// <summary>Ospitato: l'ospite ha cominciato un round. Serrande giu', striscione e rintocco, come sull'ospite (senza nemici: arrivano dalla rete).</summary>
        public void CoopStart(int zoneIdx, int round, bool isMoth, int moodIdx)
        {
            if (SurvivalMode.Enabled) { Round = round; moth = isMoth; phase = Phase.Fighting; clock = 0; ShowSurvivalWave(); return; }
            if (fi == null || zoneIdx < 0 || zoneIdx >= zones.Count) return;
            if (phase != Phase.Idle) Finish();
            var z = zones[zoneIdx];
            cur = z; Round = round; moth = isMoth; mood = (RoundMood)Mathf.Clamp(moodIdx, 0, 2);
            clock = 0f; endT = 0f;
            delay = mood == RoundMood.Calm ? RoundRules.DelayCalm : mood == RoundMood.Alarm ? RoundRules.DelayAlarm : RoundRules.DelayNormal;
            netTarget = netSpawned = netAlive = 0;
            BuildShutters(z);
            foreach (var s in z.shutters) s.Drop();
            Physics.SyncTransforms();
            phase = Phase.Fighting;
            var gm = GameManager.I;
            gm.hud?.SetRound(Round);
            if (moth) gm.hud?.Banner("Processione dei Lumi", "Round " + Round + "  ·  Le candele hanno lasciato i loro altari.");
            else gm.hud?.Banner("Round " + Round, mood == RoundMood.Calm ? "Nessuno sa che sei qui." : "Le serrande sono calate.");
            Sfx.UI(SoundBank.Toll, 0.55f, 0.55f);
            if (moth) Sfx.UI(SoundBank.LumiProcession, 0.7f);
            FloorDirector.I?.RefreshObjectiveNow();
        }

        public void CoopStatus(int t, int s, int alive) { netTarget = t; netSpawned = s; netAlive = alive; }

        /// <summary>Ospitato: il round e' finito, premio a tutti.</summary>
        public void CoopComplete(bool reward, int pts)
        {
            if (SurvivalMode.Enabled) { FinishSurvivalWave(reward, pts); return; }
            if (cur == null) return;
            var gm = GameManager.I;
            var pl = gm.player;
            if (reward)
            {
                gm.AddPoints(pts);
                pl.shield = 50f;
                pl.weapon.AddAmmoFraction(0.25f);
            }
            foreach (var s in cur.shutters) if (s != null) s.Release();
            Sfx.UI(SoundBank.Toll, 0.7f);
            gm.hud?.SetRound(Round);
            gm.hud?.Banner((moth ? "Processione dei Lumi " : "Round ") + Round + " superato",
                reward ? $"+{pts} punti  ·  scudo e munizioni" : "Le presenze si sono dissolte.");
            cur.done = true;
            phase = Phase.Opening;
        }

        /// <summary>Ospitato: il round e' saltato (sono morti tutti): serrande su, candela da riaccendere.</summary>
        public void CoopAbort()
        {
            if (SurvivalMode.Enabled)
            {
                if (phase == Phase.Fighting) Round = Mathf.Max(0, Round - 1);
                phase = Phase.Idle; nextWave = FirstWaveDelay;
                GameManager.I.hud?.SetRound(Round, false);
                return;
            }
            if (cur == null) { phase = Phase.Idle; return; }
            var z = cur;
            foreach (var s in z.shutters) if (s != null) s.SnapUp();
            if (phase != Phase.Opening)
            {
                z.candle.Setup(false);
                Round = Mathf.Max(0, Round - 1);
                var gm = GameManager.I;
                gm.hud?.SetRound(Round, false);
                FloorDirector.I?.OnRoundAborted(z.candle);
            }
            Finish();
        }

        void ClientTick(float dt)
        {
            if (phase == Phase.Fighting) { clock += dt; return; }
            if (phase == Phase.Opening && cur != null)
            {
                foreach (var s in cur.shutters) if (s != null && !s.IsUp) return;
                Finish();
            }
        }

        void Complete(bool reward)
        {
            var gm = GameManager.I;
            var pl = gm.player;
            int pts = 0;
            if (reward)
            {
                pts = RoundRules.Reward(Round) + (mood == RoundMood.Calm ? RoundRules.CalmBonus : 0);
                gm.AddPoints(pts);
                pl.shield = 50f;
                pl.weapon.AddAmmoFraction(0.25f);
            }
            Coop.Game?.RoundComplete(reward, pts);        // cooperativa: il premio e' di tutti
            foreach (var s in cur.shutters) if (s != null) s.Release();
            Sfx.UI(SoundBank.Toll, 0.7f);
            gm.hud?.SetRound(Round);
            bool gift = reward && moth;
            if (gift)
            {
                // il regalo della Processione dei Lumi: un Reliquiario dove e' caduta l'ultima (non conta nel tetto del piano)
                Vector3 at = Drops.lastKillPos;
                if (at == Vector3.zero) at = pl.transform.position;
                Drops.Spawn(DropKind.Ammo, at, false);
            }
            gm.hud?.Banner((moth ? "Processione dei Lumi " : "Round ") + Round + " superato",
                reward ? $"+{pts} punti  ·  scudo e munizioni" + (gift ? "  ·  un Reliquiario è caduto" : "") + (mood == RoundMood.Calm ? "  ·  nessuno ti ha sentito" : "") : "Le presenze si sono dissolte.");
            cur.done = true;
            phase = Phase.Opening;
            Debug.Log($"[PryingEyes] round {Round} finito in {clock:F0} s ({(reward ? "+" + pts + " punti" : "senza premio")})");
            var fd = FloorDirector.I;
            if (fd != null) fd.OnRoundEnd(cur.candle);
        }

        void Timeout()
        {
            Debug.LogWarning($"[PryingEyes] round {Round}: tempo scaduto con {mine.Count} nemici vivi, {spawned}/{target} nati");
            foreach (var e in mine.ToArray())
                if (e != null && !e.IsDead) { RoundFlare.At(e.transform.position); Enemy.Remove(e); }
            mine.Clear();
            Complete(false);
        }

        /// <summary>Il giocatore e' morto: il round salta. Serrande su, nemici via, candela da riaccendere, il numero del round non conta.</summary>
        public void OnPlayerDied() { if (phase != Phase.Idle && !Coop.Active) Abort(); }     // in cooperativa il round salta solo se muoiono tutti (Update)

        void Abort()
        {
            if (SurvivalMode.Enabled) { AbortSurvival(); return; }
            if (cur == null) { phase = Phase.Idle; HotelNav.Unseal(); return; }
            var z = cur;
            if (phase == Phase.Opening)
            {
                foreach (var s in z.shutters) if (s != null) s.SnapUp();
                Finish();
                return;
            }
            foreach (var e in mine.ToArray())
                if (e != null && !e.IsDead) { RoundFlare.At(e.transform.position); Enemy.Remove(e); }
            mine.Clear();
            foreach (var s in z.shutters) if (s != null) s.SnapUp();
            z.candle.Setup(false);
            Debug.Log($"[PryingEyes] round {Round} annullato: giocatore morto dopo {clock:F0} s, {spawned}/{target} nati");
            Coop.Game?.RoundAborted();
            Round = Mathf.Max(0, Round - 1);
            var gm = GameManager.I;
            if (gm != null && gm.hud != null) gm.hud.SetRound(Round, false);
            var fd = FloorDirector.I;
            if (fd != null) fd.OnRoundAborted(z.candle);
            Finish();
        }

        void Finish()
        {
            if (cur != null)
            {
                foreach (var s in cur.shutters) if (s != null) Destroy(s.gameObject);
                cur.shutters.Clear();
            }
            HotelNav.Unseal();
            phase = Phase.Idle;
            cur = null;
            mine.Clear();
        }
    }
}

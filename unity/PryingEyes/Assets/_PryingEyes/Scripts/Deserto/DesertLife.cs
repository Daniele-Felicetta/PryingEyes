using System.Collections.Generic;
using PryingEyes.Proc;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    /// <summary>La vita sulla sabbia, che si muove senza fare male a nessuno: sterpaglie che rotolano col vento, lucertole, scorpioni e scarabei che scappano quando ci si avvicina, avvoltoi che
    /// fanno la ruota sopra le rovine di giorno e pipistrelli che le girano attorno di notte. Niente oggetti di scena: tutto e' disegnato con l'istanziamento (DesertScatter.PropMaterial(true),
    /// la luminosita' non dipende dalla posizione perche' queste cose si muovono) e animato a mano (le ali battono, la coda ondeggia). Sta attorno al giocatore: gli animali nascono a 12-58 m
    /// fuori dalla vista e spariscono oltre 75 m; i branchi stanno sui luoghi in rovina e sulle torri entro 700 m (DesertWorld.SitesNearKnown), gli stessi ogni volta.</summary>
    public sealed class DesertLife : MonoBehaviour
    {
        public static DesertLife I { get; private set; }

        const int MaxWeeds = 12, MaxCritters = 20, MaxFlocks = 5, MaxPerFlock = 9;
        // i branchi che si dissolvono restano nella lista finche' non sono spariti, mentre quelli nuovi arrivano: la capienza di quel che si disegna e' tripla di MaxFlocks
        const int DrawCap = MaxFlocks * 3 * MaxPerFlock;

        enum Kind : byte { Lizard, Scorpion, Beetle }
        enum Act : byte { Idle, Walk, Flee }

        struct Weed { public bool on; public Vector3 pos; public float speed, size, phase, checkT; public Quaternion rot; }
        struct Critter { public bool on; public Kind kind; public Act act; public Vector3 pos; public float yaw, speed, t, scale, phase, pitch; }

        sealed class Flock
        {
            public long key; public bool bats; public Vector3 centre;
            public int n; public float radius, alt, dir, phase, omega, fade, target;
            public readonly float[] off = new float[MaxPerFlock], rj = new float[MaxPerFlock], aj = new float[MaxPerFlock], sp = new float[MaxPerFlock], sz = new float[MaxPerFlock];
        }

        Transform player;
        /// <summary>La camera del giocatore attorno a cui vivono (schermo condiviso: una per posto); senza, quella principale.</summary>
        [System.NonSerialized] public Camera view;
        Material mat;
        System.Random rng = new System.Random(20261118);
        readonly List<Site> sites = new List<Site>();
        readonly Weed[] weeds = new Weed[MaxWeeds];
        readonly Critter[] crit = new Critter[MaxCritters];
        readonly List<Flock> flocks = new List<Flock>();
        readonly HashSet<long> wanted = new HashSet<long>();
        readonly Matrix4x4[] mWeed = new Matrix4x4[MaxWeeds], mLizB = new Matrix4x4[MaxCritters], mLizT = new Matrix4x4[MaxCritters], mScorp = new Matrix4x4[MaxCritters], mBeetle = new Matrix4x4[MaxCritters];
        readonly Matrix4x4[] mBirdB = new Matrix4x4[DrawCap], mBirdL = new Matrix4x4[DrawCap], mBirdR = new Matrix4x4[DrawCap];
        readonly Matrix4x4[] mBatB = new Matrix4x4[DrawCap], mBatL = new Matrix4x4[DrawCap], mBatR = new Matrix4x4[DrawCap];
        Bounds bounds;
        float planT, spawnT;
        int spawnCursor;
        readonly Comparison_ byDist = new Comparison_();

        // per le prove
        public int WeedCount { get; private set; }
        public int LizardCount { get; private set; }
        public int ScorpionCount { get; private set; }
        public int BeetleCount { get; private set; }
        public int BirdCount { get; private set; }
        public int BatCount { get; private set; }
        public int FlockCount => flocks.Count;
        public int Fled { get; private set; }
        public bool Hidden { get; private set; }
        /// <summary>Le prove (e le fotografie): gli animali non scappano.</summary>
        public bool Calm;

        /// <summary>L'animale vivo piu' vicino a un punto, di un certo genere (0 lucertola, 1 scorpione, 2 scarabeo, -1 uno qualsiasi). Per le prove.</summary>
        public bool NearestCritter(Vector3 from, int kind, out Vector3 pos)
        {
            pos = default; float best = float.MaxValue; bool found = false;
            for (int i = 0; i < MaxCritters; i++)
            {
                if (!crit[i].on || (kind >= 0 && (int)crit[i].kind != kind)) continue;
                float d = (crit[i].pos - from).sqrMagnitude;
                if (d < best) { best = d; pos = crit[i].pos; found = true; }
            }
            return found;
        }

        /// <summary>Per le prove: mette una sterpaglia in un punto (al posto di una libera, o della piu' lontana dal punto), cosi' una foto non dipende da dove sono andate a finire quelle vere.</summary>
        public void PlaceWeed(Vector3 at, float size)
        {
            int pick = -1; float far = -1f;
            for (int i = 0; i < MaxWeeds; i++)
            {
                if (!weeds[i].on) { pick = i; break; }
                float d = (weeds[i].pos - at).sqrMagnitude;
                if (d > far) { far = d; pick = i; }
            }
            if (pick < 0) return;
            ref var k = ref weeds[pick];
            k.on = true; k.size = size; k.phase = 0f; k.speed = 1f; k.pos = at; k.rot = Quaternion.identity; k.checkT = 0.5f;
        }

        /// <summary>La sterpaglia viva piu' vicina a un punto (per le prove).</summary>
        public bool NearestWeed(Vector3 from, out Vector3 pos)
        {
            pos = default; float best = float.MaxValue; bool found = false;
            for (int i = 0; i < MaxWeeds; i++)
            {
                if (!weeds[i].on) continue;
                float d = (weeds[i].pos - from).sqrMagnitude;
                if (d < best) { best = d; pos = weeds[i].pos; found = true; }
            }
            return found;
        }

        sealed class Comparison_ : IComparer<Site>
        {
            public Vector3 from;
            public int Compare(Site a, Site b)
            {
                float da = (a.CentreX - from.x) * (a.CentreX - from.x) + (a.CentreZ - from.z) * (a.CentreZ - from.z);
                float db = (b.CentreX - from.x) * (b.CentreX - from.x) + (b.CentreZ - from.z) * (b.CentreZ - from.z);
                return da.CompareTo(db);
            }
        }

        public static DesertLife Create(GameObject host, Transform followed)
        {
            var l = host.AddComponent<DesertLife>();
            l.player = followed;
            return l;
        }

        void Awake()
        {
            if (I == null) I = this;          // con piu' posti ce n'e' uno per ognuno: I e' il primo (il posto 0)
            mat = DesertScatter.PropMaterial(true);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (mat != null) Destroy(mat);
        }

        float R() => (float)rng.NextDouble();

        /// <summary>Un altro posto dello schermo condiviso ha il suo: un seme diverso, perche' gli animali dei due non nascano uguali.</summary>
        public void Reseed(int salt) { rng = new System.Random(20261118 + 7919 * salt); }
        float R(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        /// <summary>Il branco di avvoltoi (o di pipistrelli) piu' vicino a un punto, sul piano: il suo centro a terra e la quota a cui gira. Per il suono dei gracchi e per le prove.
        /// Conta solo chi e' gia' piu' che mezzo comparso: un branco che si sta dissolvendo altrove non e' quello che si vede sopra la testa.</summary>
        public bool NearestFlock(Vector3 from, bool bats, out Vector3 centre, out float alt)
        {
            centre = default; alt = 0f; float best = float.MaxValue; bool found = false;
            foreach (var f in flocks)
            {
                if (f.bats != bats || f.fade <= 0.5f) continue;
                float dx = f.centre.x - from.x, dz = f.centre.z - from.z, d = dx * dx + dz * dz;
                if (d < best) { best = d; centre = f.centre; alt = f.alt; found = true; }
            }
            return found;
        }

        void Update()
        {
            if (player == null) return;
            var sky = DesertSky.I;
            var w = sky != null ? sky.Weather : new WeatherState { dirX = 1f, wind = 0.2f };
            float day = sky != null ? sky.State.daylight : 1f;
            Hidden = sky != null && sky.IndoorFor(view) > 0.6f;
            Vector3 p = player.position;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            bounds = new Bounds(p, new Vector3(1800f, 700f, 1800f));
            UpdateWeeds(p, w, dt);
            UpdateCritters(p, w, day, dt);
            UpdateFlocks(p, w, day, dt);
            if (!Hidden) Draw();
        }

        // ------------------------------------------------------------------ le sterpaglie
        bool OnSite(float x, float z, float margin)
        {
            DesertWorld.SitesNearKnown(x, z, margin + 2f, sites);
            foreach (var s in sites) if (x > s.fx0 - margin && x < s.fx1 + margin && z > s.fz0 - margin && z < s.fz1 + margin) return true;
            return false;
        }

        void UpdateWeeds(Vector3 p, WeatherState w, float dt)
        {
            int want = Mathf.RoundToInt(MaxWeeds * Mathf.Clamp01(0.25f + w.wind * 1.3f));
            var wd = new Vector3(w.dirX, 0f, w.dirZ);
            var side = new Vector3(-wd.z, 0f, wd.x);
            float time = Time.time;
            int on = 0;
            for (int i = 0; i < MaxWeeds; i++)
            {
                ref var k = ref weeds[i];
                if (!k.on)
                {
                    if (i >= want) continue;
                    // nasce controvento, fuori dalla vista, dove non ci sono costruzioni
                    float up = R(70f, 130f), sd = R(-90f, 90f);
                    float x = p.x - wd.x * up + side.x * sd, z = p.z - wd.z * up + side.z * sd;
                    if (OnSite(x, z, 3f) || DesertWorld.DistToCathedral(x, z) < 12f) continue;
                    k.on = true; k.size = R(0.85f, 1.5f); k.phase = R(0f, 6.28f); k.speed = R(0.7f, 1.2f); k.pos = new Vector3(x, DesertWorld.Height(x, z), z); k.rot = Random.rotation; k.checkT = R(0f, 0.3f);
                }
                float v = (1.1f + 9.5f * w.wind) * k.speed;
                var step = (wd + side * 0.12f * Mathf.Sin(time * 0.7f + k.phase)).normalized * (v * dt);
                var pos = k.pos + step;
                // non si lascia attraversare dal giocatore
                float dx = pos.x - p.x, dz = pos.z - p.z, d2 = dx * dx + dz * dz;
                if (d2 < 2.2f * 2.2f && d2 > 0.0001f) { float d = Mathf.Sqrt(d2); pos.x += dx / d * (2.2f - d) * 0.6f; pos.z += dz / d * (2.2f - d) * 0.6f; }
                float y = DesertWorld.Height(pos.x, pos.z);
                float hop = Mathf.Abs(Mathf.Sin(time * 2.4f + k.phase)) * 0.30f * Mathf.Clamp01(w.wind * 1.6f);
                k.pos = new Vector3(pos.x, y + hop, pos.z);
                var axis = Vector3.Cross(Vector3.up, wd).normalized;
                k.rot = Quaternion.AngleAxis(v / (0.5f * k.size) * dt * Mathf.Rad2Deg, axis) * k.rot;
                k.checkT -= dt;
                if (k.checkT <= 0f)
                {
                    k.checkT = 0.3f;
                    if ((k.pos - p).sqrMagnitude > 170f * 170f || OnSite(k.pos.x, k.pos.z, 1.5f)) k.on = false;
                }
                if (k.on) { mWeed[on] = Matrix4x4.TRS(k.pos + Vector3.up * (k.size * 0.45f), k.rot, Vector3.one * k.size); on++; }
            }
            WeedCount = on;
        }

        // ------------------------------------------------------------------ gli animali
        static Kind PickKind(Region r, float u)
        {
            switch (r)
            {
                case Region.Salt: return u < 0.75f ? Kind.Beetle : Kind.Scorpion;
                case Region.Badlands: return u < 0.45f ? Kind.Lizard : u < 0.8f ? Kind.Scorpion : Kind.Beetle;
                case Region.DeadTown: return u < 0.4f ? Kind.Beetle : u < 0.7f ? Kind.Scorpion : Kind.Lizard;
                default: return u < 0.6f ? Kind.Lizard : u < 0.8f ? Kind.Beetle : Kind.Scorpion;
            }
        }

        bool PlaceCritter(int i, Vector3 p, float day)
        {
            Vector3 fwd = player.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            for (int tries = 0; tries < 6; tries++)
            {
                float a = R(0f, 6.283f), r = R(12f, 58f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (r < 36f && Vector3.Dot(dir, fwd) > 0.15f) continue;                   // vicino non nascono davanti agli occhi
                float x = p.x + dir.x * r, z = p.z + dir.z * r;
                if (OnSite(x, z, 3f) || DesertWorld.DistToCathedral(x, z) < 16f) continue;
                var kind = PickKind(DesertWorld.RegionAt(x, z), R());
                if (kind == Kind.Lizard && day < 0.15f) kind = R() < 0.5f ? Kind.Scorpion : Kind.Beetle;      // le lucertole dormono di notte
                crit[i] = new Critter
                {
                    on = true, kind = kind, act = Act.Idle, pos = new Vector3(x, DesertWorld.Height(x, z), z), yaw = R(0f, 360f), t = R(0.5f, 4f),
                    scale = kind == Kind.Lizard ? R(0.9f, 1.4f) : kind == Kind.Scorpion ? R(0.9f, 1.35f) : R(1f, 1.6f), phase = R(0f, 6.28f)
                };
                return true;
            }
            return false;
        }

        void UpdateCritters(Vector3 p, WeatherState w, float day, float dt)
        {
            float time = Time.time;
            bool shelter = w.storm > 0.4f;                                               // nella tempesta stanno nelle tane
            int nl = 0, ns = 0, nb = 0;
            int slot = spawnCursor; spawnCursor = (spawnCursor + 1) % MaxCritters;       // un animale nuovo per fotogramma al massimo
            for (int i = 0; i < MaxCritters; i++)
            {
                ref var c = ref crit[i];
                if (!c.on)
                {
                    if (!shelter && i == slot) PlaceCritter(i, p, day);
                    continue;
                }
                float dx = c.pos.x - p.x, dz = c.pos.z - p.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > 75f || shelter || (c.kind == Kind.Lizard && day < 0.12f && d > 20f)) { c.on = false; continue; }
                float scare = c.kind == Kind.Scorpion ? 3.8f : 6.5f;
                if (!Calm && c.act != Act.Flee && d < scare)
                {
                    c.act = Act.Flee; c.t = R(1.4f, 2.6f); Fled++;
                    c.yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg + R(-35f, 35f);          // via dal giocatore
                    c.speed = c.kind == Kind.Lizard ? R(3.2f, 4.2f) : c.kind == Kind.Scorpion ? R(1.4f, 2.0f) : R(0.9f, 1.4f);
                }
                c.t -= dt;
                if (c.t <= 0f)
                {
                    if (c.act == Act.Idle) { c.act = Act.Walk; c.yaw += R(-70f, 70f); c.speed = c.kind == Kind.Lizard ? R(0.5f, 0.9f) : R(0.25f, 0.5f); c.t = R(0.8f, 2.2f); }
                    else { c.act = Act.Idle; c.speed = 0f; c.t = R(1.2f, 5f); }
                }
                if (c.act != Act.Idle && c.speed > 0f)
                {
                    float yr = c.yaw * Mathf.Deg2Rad;
                    var np = c.pos + new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr)) * (c.speed * dt);
                    if (OnSite(np.x, np.z, 1.2f)) { c.yaw += 150f; np = c.pos; }       // non entra nelle case: torna indietro
                    c.pos = new Vector3(np.x, c.pos.y, np.z);
                }
                float yy = DesertWorld.Height(c.pos.x, c.pos.z);
                float yr2 = c.yaw * Mathf.Deg2Rad;
                float ahead = DesertWorld.Height(c.pos.x + Mathf.Sin(yr2) * 0.2f, c.pos.z + Mathf.Cos(yr2) * 0.2f);
                c.pos.y = yy;
                c.pitch = -Mathf.Atan2(ahead - yy, 0.2f) * Mathf.Rad2Deg;
                var body = Matrix4x4.TRS(c.pos, Quaternion.Euler(c.pitch, c.yaw, 0f), Vector3.one * c.scale);
                if (c.kind == Kind.Lizard)
                {
                    float moving = c.act == Act.Idle ? 0.15f : Mathf.Clamp01(c.speed / 3f) + 0.25f;
                    float wig = Mathf.Sin(time * (4f + c.speed * 4f) + c.phase) * (10f + 28f * moving);
                    mLizB[nl] = body;
                    mLizT[nl] = body * Matrix4x4.TRS(new Vector3(0f, 0.05f, -0.13f), Quaternion.Euler(0f, wig, 0f), Vector3.one);
                    nl++;
                }
                else if (c.kind == Kind.Scorpion) { mScorp[ns] = body; ns++; }
                else { mBeetle[nb] = body; nb++; }
            }
            LizardCount = nl; ScorpionCount = ns; BeetleCount = nb;
        }

        // ------------------------------------------------------------------ i branchi
        void UpdateFlocks(Vector3 p, WeatherState w, float day, float dt)
        {
            planT -= dt;
            if (planT <= 0f) { planT = 1.5f; PlanFlocks(p, day, w.storm); }
            float time = Time.time;
            int nb = 0, nt = 0;
            for (int fi = flocks.Count - 1; fi >= 0; fi--)
            {
                var f = flocks[fi];
                f.fade = Mathf.MoveTowards(f.fade, f.target, dt / (f.target > 0f ? 4f : 5f));
                if (f.target <= 0f && f.fade <= 0f) { flocks.RemoveAt(fi); continue; }
                float k0 = Mathf.SmoothStep(0f, 1f, f.fade);
                if (k0 <= 0.01f) continue;
                for (int i = 0; i < f.n; i++)
                {
                    if (f.bats ? nt >= DrawCap : nb >= DrawCap) break;
                    float seed = f.key * 0.37f % 100f + i * 1.7f;
                    float th = f.phase + f.dir * (time * f.omega * f.sp[i]) + f.off[i];
                    float r = f.radius + f.rj[i] + (f.bats ? 3.5f * Mathf.Sin(time * 0.9f + seed) : 0f);
                    float y = f.alt + f.aj[i] + (f.bats ? 4f * Mathf.Sin(time * 1.7f + seed) : 2.5f * Mathf.Sin(time * 0.21f + seed));
                    var pos = f.centre + new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
                    var tan = new Vector3(-Mathf.Sin(th) * f.dir, f.bats ? 0.15f * Mathf.Cos(time * 1.7f + seed) : 0.04f * Mathf.Cos(time * 0.21f + seed), Mathf.Cos(th) * f.dir);
                    float bank = f.bats ? 26f : 14f;
                    var rot = Quaternion.LookRotation(tan.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, f.dir * bank);
                    float s = f.sz[i] * k0;
                    var body = Matrix4x4.TRS(pos, rot, Vector3.one * s);
                    float a;
                    if (f.bats) a = 0.15f + 0.75f * Mathf.Sin(time * 17f + seed);
                    else { float burst = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Sin(time * 0.35f + seed * 1.7f) - 0.5f) * 2.2f)); a = 0.17f + 0.55f * burst * Mathf.Sin(time * 6.2f + seed); }
                    var shoulderR = Matrix4x4.TRS(new Vector3(f.bats ? 0.05f : 0.12f, f.bats ? 0.01f : 0.03f, f.bats ? 0.02f : 0.05f), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg), Vector3.one);
                    var shoulderL = Matrix4x4.TRS(new Vector3(f.bats ? -0.05f : -0.12f, f.bats ? 0.01f : 0.03f, f.bats ? 0.02f : 0.05f), Quaternion.Euler(0f, 0f, -a * Mathf.Rad2Deg), Vector3.one);
                    if (f.bats) { mBatB[nt] = body; mBatR[nt] = body * shoulderR; mBatL[nt] = body * shoulderL; nt++; }
                    else { mBirdB[nb] = body; mBirdR[nb] = body * shoulderR; mBirdL[nb] = body * shoulderL; nb++; }
                }
            }
            BirdCount = nb; BatCount = nt;
        }

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        void PlanFlocks(Vector3 p, float daylight, float storm)
        {
            bool birds = daylight > 0.18f && storm < 0.35f, bats = daylight < 0.22f;
            DesertWorld.SitesNearKnown(p.x, p.z, 700f, sites);
            byDist.from = p;
            sites.Sort(byDist);
            wanted.Clear();
            int count = 0;
            foreach (var s in sites)
            {
                bool ruin = s.IsTower || s.IsCatacomb || s.state != BuildingState.Intact;
                if (!ruin) continue;
                if (count >= MaxFlocks) break;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool bat = pass == 1;
                    if (bat ? !bats : !birds) continue;
                    long key = Key(s.cx, s.cz) * 2 + pass;
                    wanted.Add(key);
                    bool have = false;
                    foreach (var f in flocks) if (f.key == key) { have = true; break; }
                    if (!have) flocks.Add(NewFlock(s, bat, key));
                }
                count++;
            }
            foreach (var f in flocks) f.target = wanted.Contains(f.key) ? 1f : 0f;
        }

        static Flock NewFlock(Site s, bool bat, long key)
        {
            var h = new System.Random(unchecked((int)DesertNoise.Hash(s.cx, s.cz, bat ? 91 : 90)));
            float H() => (float)h.NextDouble();
            var f = new Flock { key = key, bats = bat, centre = new Vector3(s.CentreX, s.padY, s.CentreZ) };
            f.n = bat ? 5 + h.Next(0, 5) : 3 + h.Next(0, 4);
            f.radius = bat ? 11f + H() * 9f : 24f + H() * 24f;
            f.alt = bat ? 5f + H() * 12f + (s.IsTower ? s.Height * 0.6f : 0f) : (s.IsTower ? s.Height + 14f + H() * 20f : 38f + H() * 32f);
            f.dir = H() < 0.5f ? 1f : -1f;
            f.phase = H() * 6.283f;
            f.omega = (bat ? 8f : 9f) / f.radius;                                       // m/s su una circonferenza
            for (int i = 0; i < MaxPerFlock; i++)
            {
                f.off[i] = (float)i / Mathf.Max(1, f.n) * 6.283f * 0.55f + H() * 0.5f;
                f.rj[i] = (H() - 0.5f) * (bat ? 5f : 12f);
                f.aj[i] = (H() - 0.5f) * (bat ? 6f : 10f);
                f.sp[i] = 0.9f + H() * 0.2f;
                f.sz[i] = bat ? 0.8f + H() * 0.35f : 0.9f + H() * 0.3f;
            }
            return f;
        }

        // ------------------------------------------------------------------ il disegno
        void Render(Mesh mesh, Matrix4x4[] arr, int n, bool shadows)
        {
            if (n <= 0 || !DesertScatter.CanDraw) return;
            var rp = new RenderParams(mat) { shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, worldBounds = bounds, layer = 0 };
            Graphics.RenderMeshInstanced(rp, mesh, 0, arr, n);
        }

        void Draw()
        {
            Render(PropMeshes.Life(LifeMesh.Tumbleweed), mWeed, WeedCount, true);
            Render(PropMeshes.Life(LifeMesh.LizardBody), mLizB, LizardCount, true);
            Render(PropMeshes.Life(LifeMesh.LizardTail), mLizT, LizardCount, true);
            Render(PropMeshes.Life(LifeMesh.Scorpion), mScorp, ScorpionCount, true);
            Render(PropMeshes.Life(LifeMesh.Beetle), mBeetle, BeetleCount, false);
            Render(PropMeshes.Life(LifeMesh.BirdBody), mBirdB, BirdCount, true);
            Render(PropMeshes.Life(LifeMesh.BirdWingL), mBirdL, BirdCount, true);
            Render(PropMeshes.Life(LifeMesh.BirdWingR), mBirdR, BirdCount, true);
            Render(PropMeshes.Life(LifeMesh.BatBody), mBatB, BatCount, false);
            Render(PropMeshes.Life(LifeMesh.BatWingL), mBatL, BatCount, false);
            Render(PropMeshes.Life(LifeMesh.BatWingR), mBatR, BatCount, false);
        }
    }
}

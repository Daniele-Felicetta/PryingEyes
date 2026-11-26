using System;
using System.Collections.Generic;

namespace Production.Racing
{
    public enum RacePhase : byte { Grid = 0, Countdown = 1, Racing = 2, Over = 3 }

    /// <summary>One rider in the race: a human on this machine, a computer rider, or a friend on another machine (a remote rider has no bike here, only the pose that arrives).</summary>
    public sealed class Racer
    {
        /// <summary>The id of the rider, the grid slot, the caller's own number for the bike (a place in its roster: the race keeps it for the views and never looks a bike up by it) and the livery.</summary>
        public int id, slot, bikeIndex, skin;
        public string name;
        public bool human, remote;
        /// <summary>A computer rider (on a friend's machine it is 'remote' too: the host rides it and sends the poses).</summary>
        public bool cpu;
        /// <summary>A person on another machine (cooperative game).</summary>
        public bool friend;
        /// <summary>A friend who left the race: last in the standings, and the end of the race does not wait for them.</summary>
        public bool dnf;
        /// <summary>The place in the network game of the person who rides this (0 the host, 1..3 the friends); -1 for a computer rider.</summary>
        public int netSlot = -1;
        /// <summary>The host's word on the place at the end of the race (-1: not given, the standings are worked out here).</summary>
        public int final = -1;
        /// <summary>The bike of this rider as it is in the race (a tuned one included): what the model rides and, for somebody on another machine, whose weight and wheelbase the contacts use.</summary>
        public BikeSpec spec;
        public BikeModel bike;
        public BikeDriver driver;
        /// <summary>The human's input for the next step (set by the game).</summary>
        public BikeInputs input;

        // pose and place on the circuit
        public float x, y, z, heading, speed, lean;
        public bool crashed;
        public CrashKind crashKind;
        public float s, lateral, progress;
        public bool wrongWay;
        /// <summary>0..1: how well this rider is sheltered from the wind by somebody just ahead (the slipstream). Worked out by the race for everybody from the poses.</summary>
        public float draft;
        public int position;

        // timing
        public int lap;
        public float lapStart, lastLap = -1f, bestLap = -1f, finishTime = -1f;
        public bool finished;
        public int finishOrder;
        public int crashes;
        public float crashedFor;
        /// <summary>How many times this rider's bike was touched by another bike harder than a graze.</summary>
        public int bumps;
        public float[] cp;
        internal int cpLast = -1, hint = -1;
        internal float prevS;
        internal bool crashSeen;
        /// <summary>Race time before which other bikes go through this one: after a fall and a respawn a rider has a few seconds to get going (nobody should be hit while standing in the racing line).</summary>
        internal float ghostUntil;

        /// <summary>The lap being ridden (1-based), capped at the race length.</summary>
        public int CurrentLap(int laps) { return Math.Min(lap + 1, laps); }
    }

    /// <summary>The rules of a race, outside the engine: the grid and the lights, the physics of every local bike and the computer riders' driving, distance along the lap with laps and finish,
    /// timing loops for the gaps, standings, crashes and the way back onto the track. Remote riders are fed in as poses and ranked by the same rules, so every machine agrees on the order.</summary>
    public sealed class Race
    {
        public readonly Circuit circuit;
        public readonly int laps;
        public RacePhase phase = RacePhase.Grid;
        /// <summary>Seconds since the lights went out (0 before).</summary>
        public float time;
        /// <summary>Red lights lit (0..5) and seconds since they all went out (for the "GO" flash).</summary>
        public int lightsOn;
        public float sinceGo = 99f;
        public readonly List<Racer> racers = new List<Racer>();
        public readonly List<Racer> standings = new List<Racer>();

        public Action<Racer> OnLap, OnFinish, OnCrash, OnRespawn;
        /// <summary>A touch harder than a graze: the two riders and how fast they came together (m/s).</summary>
        public Action<Racer, Racer, float> OnContact;
        public Action OnGo, OnOver;

        /// <summary>After the first rider finishes the others have this long to finish too.</summary>
        public float overtime = 45f;
        public const float CheckpointSpacing = 50f;

        readonly Dictionary<string, RacingLine> lines = new Dictionary<string, RacingLine>();          // by the key of the bike: a tuned one has its own line
        float countT, hold, firstFinish = -1f;
        int finishCounter, nextId;
        Rng rng;

        /// <summary>On a friend's machine the end of the race is not decided here but by the host (ApplyResults): the local rules never say 'over'.</summary>
        public bool externalEnd;

        /// <summary>Normally the race is over as soon as every person (the player, the friends) has crossed the line: the computer riders still out are ranked by the distance they have ridden. A championship scores the whole
        /// field, so its race waits for every rider (the overtime after the first finish still ends it).</summary>
        public bool waitForField;

        public Race(Circuit circuit, int laps, int seed)
        {
            this.circuit = circuit; this.laps = Math.Max(1, laps);
            rng = new Rng(seed ^ 0x2545F491);
        }

        /// <summary>The line of a bike as it is in the race (the roster's, or a tuned one), kept by the key of its spec.</summary>
        public RacingLine LineFor(BikeSpec spec)
        {
            RacingLine l;
            if (!lines.TryGetValue(spec.id, out l)) { l = new RacingLine(circuit, spec); lines[spec.id] = l; }
            return l;
        }

        // ------------------------------------------------------------------ riders
        Racer NewRacer(string name, BikeSpec spec, int skin, int bikeIndex)
        {
            var r = new Racer { id = nextId++, name = name, spec = spec, bikeIndex = bikeIndex, skin = skin, slot = racers.Count };
            r.cp = new float[(int)(laps * circuit.length / CheckpointSpacing) + 4];
            for (int i = 0; i < r.cp.Length; i++) r.cp[i] = -1f;
            racers.Add(r);
            return r;
        }

        /// <summary>A rider with a bike on this machine; the game fills 'input' every frame. 'spec' is the bike as a workshop made it (a tuned one); 'bikeIndex' is the caller's own number for it.</summary>
        public Racer AddHuman(string name, BikeSpec spec, int skin, int seed, int bikeIndex = 0)
        {
            var r = NewRacer(name, spec, skin, bikeIndex);
            r.human = true;
            r.bike = new BikeModel(spec, seed);
            return r;
        }

        /// <summary>A computer rider. 'pace' is the fraction of the planned speed (0.9 .. 0.99); 'spec' is the bike (a tuned one gets a line of its own, kept by its id); 'bikeIndex' is the caller's own number for it.</summary>
        public Racer AddAi(string name, BikeSpec spec, int skin, int seed, float pace, float mistakes, int bikeIndex = 0)
        {
            var r = NewRacer(name, spec, skin, bikeIndex);
            r.cpu = true;
            r.bike = new BikeModel(spec, seed);
            var line = LineFor(spec);
            r.driver = new BikeDriver(r.bike, circuit, line, seed * 31 + 7) { pace = pace, mistakes = mistakes };
            var rr = new Rng(seed);
            r.driver.bias = rr.Range(-1.3f, 1.3f);
            r.driver.reaction = rr.Range(0.08f, 0.5f);
            return r;
        }

        /// <summary>Somebody on another machine: only poses arrive. A friend (a person) by default; a computer rider ridden by the host, as seen on a friend's machine, with friend = false.</summary>
        public Racer AddRemote(string name, BikeSpec spec, int skin, bool friend = true, int bikeIndex = 0)
        {
            var r = NewRacer(name, spec, skin, bikeIndex);
            r.remote = true; r.friend = friend; r.cpu = !friend;
            return r;
        }

        /// <summary>A friend left (or lost the connection): out of the race, last in the standings, and the end of the race does not wait for them.</summary>
        public void Retire(Racer r) { if (r != null) r.dnf = true; }

        public Racer Find(int id)
        {
            for (int i = 0; i < racers.Count; i++) if (racers[i].id == id) return racers[i];
            return null;
        }

        /// <summary>Put a rider on a grid slot (0 = pole). The slot of a rider also decides the order of the standings before the start.</summary>
        public void PlaceOnGrid(Racer r, int slot)
        {
            float gx, gz, gy, gh;
            circuit.GridSlot(slot, out gx, out gz, out gy, out gh);
            r.slot = slot;
            if (r.bike != null) r.bike.Reset(gx, gz, gh, gy);
            if (r.driver != null) r.driver.Reset();
            r.x = gx; r.z = gz; r.y = gy; r.heading = gh; r.speed = 0f; r.lean = 0f; r.crashed = false; r.crashKind = CrashKind.None;
            r.hint = -1;
            Proj p;
            circuit.Project(gx, gz, ref r.hint, out p);
            r.s = p.s; r.lateral = p.d; r.prevS = p.s;
            r.progress = p.s > circuit.length * 0.5f ? p.s - circuit.length : p.s;
            r.lap = 0; r.lapStart = 0f; r.lastLap = r.bestLap = r.finishTime = -1f; r.finished = false; r.finishOrder = 0; r.crashes = 0; r.crashedFor = 0f; r.crashSeen = false;
            r.cpLast = -1;
            for (int i = 0; i < r.cp.Length; i++) r.cp[i] = -1f;
            r.wrongWay = false; r.bumps = 0; r.ghostUntil = 0f;
        }

        /// <summary>Fill the grid in the order given (the first racer takes pole).</summary>
        public void PlaceAll(IList<Racer> order)
        {
            for (int i = 0; i < order.Count; i++) PlaceOnGrid(order[i], i);
            Standings();
        }

        public void StartCountdown()
        {
            phase = RacePhase.Countdown; countT = 0f; lightsOn = 0; time = 0f; sinceGo = 99f; finishCounter = 0; firstFinish = -1f;
            hold = 0.6f + rng.Value() * 1.6f;
        }

        /// <summary>Everything the lights need: seconds from the first light to the start.</summary>
        public float CountdownLength { get { return 5f + hold; } }

        // ------------------------------------------------------------------ the update
        public void Step(float dt)
        {
            if (dt <= 0f) return;
            if (phase == RacePhase.Grid) { Poses(); Progress(dt); Standings(); return; }
            if (phase == RacePhase.Countdown)
            {
                countT += dt;
                lightsOn = Math.Min(5, (int)countT);
                if (countT >= 5f + hold)
                {
                    phase = RacePhase.Racing; lightsOn = 0; time = 0f; sinceGo = 0f;
                    if (OnGo != null) OnGo();
                }
                Poses(); Progress(dt); Standings();
                return;
            }
            time += dt; sinceGo += dt;
            Drafts(dt);
            for (int i = 0; i < racers.Count; i++)
            {
                var r = racers[i];
                if (r.remote || r.bike == null) continue;
                BikeInputs inp;
                if (r.human) inp = r.input;
                else { Avoid(r, dt); inp = r.driver.Drive(dt); }
                r.bike.Step(dt, circuit, inp);
            }
            if (contacts) Contacts();
            Poses();
            Progress(dt);
            Crashes(dt);
            Standings();
            if (phase == RacePhase.Racing) CheckEnd();
        }

        /// <summary>How far ahead and how wide a rider must be to shelter the one behind: a cone that opens a little with the distance.</summary>
        public const float DraftRange = 36f, DraftWidth = 1.05f, DraftSpread = 0.03f, DraftMinGap = 1.2f;

        /// <summary>The slipstream: a rider close behind another, in line and going the same way, rides in the hole in the air and loses up to a third of the drag. Worked out from the poses (so it is the
        /// same for bikes simulated here and for friends' bikes seen as poses) and eased in and out over a third of a second.</summary>
        void Drafts(float dt)
        {
            for (int i = 0; i < racers.Count; i++)
            {
                var r = racers[i];
                float target = 0f;
                if (!r.crashed && !r.finished && !r.dnf && r.speed > 20f)
                {
                    float sn = (float)Math.Sin(r.heading), cs = (float)Math.Cos(r.heading);
                    for (int j = 0; j < racers.Count; j++)
                    {
                        var o = racers[j];
                        if (o == r || o.crashed || o.dnf || o.speed < 12f) continue;
                        float dx = o.x - r.x, dz = o.z - r.z;
                        float fwd = dx * sn + dz * cs;
                        if (fwd < DraftMinGap || fwd > DraftRange) continue;
                        float side = Math.Abs(dx * cs - dz * sn);
                        float half = DraftWidth + DraftSpread * fwd;
                        if (side > half) continue;
                        if (Math.Cos(r.heading - o.heading) < 0.6) continue;
                        float v = (float)Math.Pow(1f - fwd / DraftRange, 0.7) * (1f - side / half);
                        if (v > target) target = v;
                    }
                }
                r.draft += (target - r.draft) * RacingMath.Ease(3.5f, dt);
                if (r.draft < 0.002f) r.draft = 0f;
                if (r.bike != null) r.bike.draft = r.draft;
            }
        }

        void Poses()
        {
            for (int i = 0; i < racers.Count; i++)
            {
                var r = racers[i];
                if (r.bike == null) continue;
                var b = r.bike;
                r.x = b.x; r.y = b.y; r.z = b.z; r.heading = b.heading; r.speed = RacingMath.Max(b.u, 0f); r.lean = b.lean;
                r.crashed = b.crashed; r.crashKind = b.crashKind;
            }
        }

        /// <summary>Feed in the pose of a remote rider (position, height, heading, speed, lean, fallen).</summary>
        public void SetRemote(Racer r, float x, float y, float z, float heading, float speed, float lean, bool crashed)
        {
            r.x = x; r.y = y; r.z = z; r.heading = heading; r.speed = speed; r.lean = lean; r.crashed = crashed;
        }

        void Progress(float dt)
        {
            float L = circuit.length;
            for (int i = 0; i < racers.Count; i++)
            {
                var r = racers[i];
                Proj p;
                circuit.Project(r.x, r.z, ref r.hint, out p);
                r.s = p.s; r.lateral = p.d;
                float delta = circuit.DeltaS(r.prevS, p.s);
                if (Math.Abs(delta) < 140f) r.progress += delta;
                r.prevS = p.s;
                r.wrongWay = r.speed > 3f && !r.crashed && (Math.Sin(r.heading) * p.tx + Math.Cos(r.heading) * p.tz) < -0.35;
                if (phase != RacePhase.Racing && phase != RacePhase.Over) continue;
                if (r.progress < 0f) continue;
                float sp = Math.Max(r.speed, 4f);
                // timing loops
                int ci = (int)(r.progress / CheckpointSpacing);
                while (r.cpLast < ci && r.cpLast + 1 < r.cp.Length)
                {
                    r.cpLast++;
                    float back = (r.progress - r.cpLast * CheckpointSpacing) / sp;
                    r.cp[r.cpLast] = Math.Max(0f, time - back);
                }
                if (r.finished) continue;
                int done = (int)(r.progress / L);
                while (r.lap < done && !r.finished)
                {
                    r.lap++;
                    float over = r.progress - r.lap * L;
                    float tCross = Math.Max(0f, time - over / sp);
                    float lapTime = tCross - r.lapStart;
                    r.lastLap = lapTime;
                    // the first lap starts from the grid: it only counts as the best lap when it is the only one
                    if (r.lap > 1 || laps == 1) { if (r.bestLap < 0f || lapTime < r.bestLap) r.bestLap = lapTime; }
                    r.lapStart = tCross;
                    if (OnLap != null) OnLap(r);
                    if (r.lap >= laps)
                    {
                        r.finished = true; r.finishTime = tCross; r.finishOrder = ++finishCounter;
                        if (firstFinish < 0f) firstFinish = time;
                        if (OnFinish != null) OnFinish(r);
                    }
                }
            }
        }

        void Crashes(float dt)
        {
            for (int i = 0; i < racers.Count; i++)
            {
                var r = racers[i];
                if (r.bike == null) continue;
                if (r.bike.crashed)
                {
                    if (!r.crashSeen) { r.crashSeen = true; r.crashes++; r.crashedFor = 0f; if (OnCrash != null) OnCrash(r); }
                    r.crashedFor += dt;
                    if (r.bike.CrashOver && r.crashedFor > 1.6f) Respawn(r);
                }
            }
        }

        /// <summary>Put a fallen rider back on the asphalt near where he fell, facing the way of the race, standing still.</summary>
        public void Respawn(Racer r)
        {
            if (r.bike == null) return;
            Proj p;
            int h = -1;
            circuit.Project(r.bike.x, r.bike.z, ref h, out p);
            float hw = circuit.HalfWidthAt(p);
            float lat = RacingMath.Clamp(p.d, -(hw - 2.4f), hw - 2.4f);
            float cx = circuit.Lerp(circuit.px, p.i, p.t), cz = circuit.Lerp(circuit.pz, p.i, p.t), cy = circuit.Lerp(circuit.py, p.i, p.t);
            float x = cx + p.nx * lat, z = cz + p.nz * lat;
            float head = (float)Math.Atan2(p.tx, p.tz);
            r.bike.Reset(x, z, head, cy);
            if (r.driver != null) r.driver.Reset();
            r.crashSeen = false; r.crashedFor = 0f; r.crashed = false;
            r.prevS = p.s; r.hint = -1;
            r.ghostUntil = time + RespawnGrace;
            if (OnRespawn != null) OnRespawn(r);
        }

        // ------------------------------------------------------------------ bikes touching
        /// <summary>A bike is three discs of this radius (the two tires and the middle) that cannot overlap. What a touch does depends on how fast the two come together (m/s along the line between the discs, 'closing'):
        /// up to <see cref="ScrapeSpeed"/> it is a nudge, from there to <see cref="StrikeSpeed"/> a bump that costs speed and shakes the frame, above <see cref="StrikeSpeed"/> the bike that rode into the other falls and
        /// above <see cref="HardSpeed"/> both do. A rider who has just got up (<see cref="RespawnGrace"/> seconds) is not touched. The two speeds are 36 and 61 km/h of closing: a fall is a real hit, not a wheel touched in the slipstream
        /// (they were 22 and 34 km/h, and a rider who was run into too easily was down).</summary>
        public const float ContactRadius = 0.40f, ContactRestitution = 0.15f, ScrapeSpeed = 1.2f, StrikeSpeed = 10f, HardSpeed = 17f, RespawnGrace = 3f;

        /// <summary>Whether bikes touch at all. A bike is touched only by another bike when one of the two is a person's (the player's, or a friend's on another machine), and only the bikes simulated on this machine are moved by it
        /// (each machine moves its own, with the share that the masses give it): two computer riders still go through each other (they steer around each other) and so do the poses of two friends.</summary>
        public bool contacts = true;

        static bool Person(Racer r) { return r.human || r.friend; }

        bool CanTouch(Racer r)
        {
            bool down = r.bike != null ? r.bike.crashed : r.crashed;
            return !down && !r.finished && !r.dnf && time >= r.ghostUntil;
        }

        void Contacts()
        {
            for (int i = 0; i < racers.Count; i++)
            {
                var a = racers[i];
                if (!Person(a) || !CanTouch(a)) continue;
                for (int j = 0; j < racers.Count; j++)
                {
                    if (j == i) continue;
                    if (!CanTouch(a)) break;                          // down after the last touch: the rest of the pack goes by
                    var b = racers[j];
                    if (!CanTouch(b)) continue;
                    if (Person(b) && j < i) continue;                 // two people are looked at once
                    if (a.bike == null && b.bike == null) continue;   // two poses: nothing here to move
                    Touch(a, b);
                }
            }
        }

        /// <summary>Where a rider is and how fast it moves, read from the bike if it is simulated here (it is a step ahead of the pose) and from the pose if it is not.</summary>
        static void Motion(Racer r, out float x, out float z, out float heading, out float vx, out float vz)
        {
            if (r.bike != null) { var b = r.bike; x = b.x; z = b.z; heading = b.heading; vx = b.vxWorld; vz = b.vzWorld; }
            else { x = r.x; z = r.z; heading = r.heading; vx = r.speed * (float)Math.Sin(r.heading); vz = r.speed * (float)Math.Cos(r.heading); }
        }

        /// <summary>The twist a push gives a bike: a push at the front to the right turns the nose to the right (heading grows toward +x).</summary>
        static float Twist(float x, float z, float cx, float cz, float pushX, float pushZ, float closing)
        {
            float rx = cx - x, rz = cz - z;
            return RacingMath.Clamp(0.03f * closing * (pushX * rz - pushZ * rx), -0.06f, 0.06f);
        }

        /// <summary>The bike of a rider as it is in the race (a tuned one weighs less); the one the race was given for somebody on another machine, of whom only the pose is known.</summary>
        static BikeSpec SpecOf(Racer r) { return r.bike != null ? r.bike.spec : r.spec; }

        void Touch(Racer a, Racer b)
        {
            float ax, az, ah, avx, avz, bx, bz, bh, bvx, bvz;
            Motion(a, out ax, out az, out ah, out avx, out avz);
            Motion(b, out bx, out bz, out bh, out bvx, out bvz);
            var sa = SpecOf(a); var sb = SpecOf(b);
            float reach = (sa.wheelbase + sb.wheelbase) * 0.5f + 2f * ContactRadius;
            if ((ax - bx) * (ax - bx) + (az - bz) * (az - bz) > reach * reach) return;
            float sinA = (float)Math.Sin(ah), cosA = (float)Math.Cos(ah), sinB = (float)Math.Sin(bh), cosB = (float)Math.Cos(bh);
            float lim = 2f * ContactRadius, deepest = 0f, nx = 0f, nz = 0f, cx = 0f, cz = 0f;
            for (int ka = -1; ka <= 1; ka++)
            {
                float oa = ka * sa.wheelbase * 0.5f;
                float px = ax + sinA * oa, pz = az + cosA * oa;
                for (int kb = -1; kb <= 1; kb++)
                {
                    float ob = kb * sb.wheelbase * 0.5f;
                    float qx = bx + sinB * ob, qz = bz + cosB * ob;
                    float dx = px - qx, dz = pz - qz, d2 = dx * dx + dz * dz;
                    if (d2 >= lim * lim) continue;
                    float d = (float)Math.Sqrt(d2), pen = lim - d;
                    if (pen <= deepest) continue;
                    deepest = pen;
                    if (d > 1e-4f) { nx = dx / d; nz = dz / d; } else { nx = cosA; nz = -sinA; }          // on top of each other: out to the right of the first
                    cx = (px + qx) * 0.5f; cz = (pz + qz) * 0.5f;
                }
            }
            if (deepest <= 0f) return;
            // (nx, nz) points from b to a; the two come together while a moves along -n relative to b
            float closing = -((avx - bvx) * nx + (avz - bvz) * nz);
            float ma = sa.Mass, mb = sb.Mass, total = ma + mb;
            float shareA = mb / total, shareB = ma / total;                // the lighter bike gives way more
            float kick = closing > 0f ? (1f + ContactRestitution) * closing : 0f;
            bool localA = a.bike != null, localB = b.bike != null;
            if (localA) a.bike.Shove(nx * deepest * shareA, nz * deepest * shareA, nx * kick * shareA, nz * kick * shareA, Twist(ax, az, cx, cz, nx, nz, closing), closing * 0.12f);
            if (localB) b.bike.Shove(-nx * deepest * shareB, -nz * deepest * shareB, -nx * kick * shareB, -nz * kick * shareB, Twist(bx, bz, cx, cz, -nx, -nz, closing), closing * 0.12f);
            if (closing < ScrapeSpeed) return;
            a.bumps++; b.bumps++;
            if (closing >= StrikeSpeed)
            {
                float intoB = -(avx * nx + avz * nz), intoA = bvx * nx + bvz * nz;      // how much of the closing is each bike's own motion
                bool hard = closing >= HardSpeed, aStrikes = intoB >= intoA;
                if (localA && (hard || aStrikes)) a.bike.ContactCrash();
                if (localB && (hard || !aStrikes)) b.bike.ContactCrash();
            }
            if (OnContact != null) OnContact(a, b, closing);
        }

        // ------------------------------------------------------------------ computer riders going around slower bikes
        void Avoid(Racer r, float dt) { Avoid(r, r.driver, dt); }

        /// <summary>The same for a driver that is not the rider's own: the computer driver that stands for the player in the automated runs goes around slower bikes like the computer riders do.</summary>
        public void Avoid(Racer r, BikeDriver d, float dt)
        {
            float hw = circuit.HalfWidth(r.s);
            float lineOff = d.line.OffsetAt(r.s) + d.bias;
            Racer who = null;
            float whoScore = 1e9f, limit = 0f;
            for (int i = 0; i < racers.Count; i++)
            {
                var o = racers[i];
                if (o == r || o.crashed) continue;
                float ds = circuit.DeltaS(r.s, o.s);
                if (ds < -3f || ds > 60f) continue;
                float dl = o.lateral - r.lateral;
                if (Math.Abs(dl) >= 2.6f) continue;
                float closing = r.speed - o.speed;
                if (ds > 3f)
                {
                    if (closing < -1.5f) continue;                       // the one ahead is pulling away
                    float tc = ds / Math.Max(closing, 1f);
                    if (ds > 14f && tc > 3.5f) continue;                 // not yet
                }
                if (Math.Abs(ds) < whoScore) { who = o; whoScore = Math.Abs(ds); }
                if (ds > 0f && ds < 9f && Math.Abs(dl) < 1.2f)
                {
                    float allow = o.speed + (ds - 2.5f) * 0.8f;
                    limit = limit == 0f ? allow : Math.Min(limit, allow);
                }
            }
            if (who != null)
            {
                if (d.commitId != who.id)
                {
                    float dl = who.lateral - r.lateral;
                    float side = Math.Abs(dl) < 0.25f ? (r.id < who.id ? -1f : 1f) : (dl > 0f ? -1f : 1f);
                    if (Math.Abs(who.lateral + side * 2.4f) > hw - 1.0f) side = -side;      // no room that way
                    d.commitId = who.id; d.commitSide = side;
                }
                float tgt = RacingMath.Clamp(who.lateral + d.commitSide * 2.4f, -(hw - 1.0f), hw - 1.0f);
                d.laneShift = RacingMath.Clamp(tgt - lineOff, -5f, 5f);
            }
            else
            {
                d.commitId = -1;
                d.laneShift -= d.laneShift * Math.Min(1f, 1.2f * dt);
            }
            d.speedLimit = limit > 0f ? Math.Max(limit, 4f) : 0f;
        }

        // ------------------------------------------------------------------ order, gaps, end
        void Standings()
        {
            standings.Clear();
            standings.AddRange(racers);
            standings.Sort(Compare);
            for (int i = 0; i < standings.Count; i++) standings[i].position = i + 1;
        }

        static int Compare(Racer a, Racer b)
        {
            if (a.dnf != b.dnf) return a.dnf ? 1 : -1;
            if (a.final >= 0 && b.final >= 0) return a.final.CompareTo(b.final);
            if (a.finished != b.finished) return a.finished ? -1 : 1;
            if (a.finished) return a.finishOrder.CompareTo(b.finishOrder);
            int c = b.progress.CompareTo(a.progress);
            return c != 0 ? c : a.id.CompareTo(b.id);
        }

        /// <summary>Seconds behind the rider ahead in the standings, read from the timing loops (0 for the leader, -1 when unknown).</summary>
        public float GapToAhead(Racer r)
        {
            if (r.position <= 1 || r.cpLast < 0) return r.position <= 1 ? 0f : -1f;
            var a = standings[r.position - 2];
            if (a.finished) return r.finished ? r.finishTime - a.finishTime : -1f;
            int idx = r.cpLast;
            if (idx < a.cp.Length && a.cp[idx] >= 0f && r.cp[idx] >= 0f) return Math.Max(0f, r.cp[idx] - a.cp[idx]);
            return Math.Max(0f, (a.progress - r.progress) / Math.Max(r.speed, 10f));
        }

        void CheckEnd()
        {
            if (externalEnd) return;
            bool anyHuman = false, humansDone = true, allDone = true;
            for (int i = 0; i < racers.Count; i++)
            {
                var r = racers[i];
                if (r.dnf) continue;
                if (!r.finished) allDone = false;
                if (r.human || r.friend) { anyHuman = true; if (!r.finished) humansDone = false; }
            }
            bool timeout = firstFinish >= 0f && time > firstFinish + overtime;
            if (allDone || timeout || (anyHuman && humansDone && !waitForField))
            {
                if (firstFinish >= 0f || racers.Count == 0)
                {
                    phase = RacePhase.Over;
                    if (OnOver != null) OnOver();
                }
            }
        }

        /// <summary>How a rider ended the race, as the host says it (the same for everybody).</summary>
        public struct Result { public int id, finishOrder, crashes, place; public bool finished, dnf; public float finishTime, bestLap; }

        /// <summary>The result of every rider as it was worked out on the host (the machine that decides when the race is over): the order and the times are taken as given and the race is over here too.</summary>
        public void ApplyResults(IList<Result> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                var r = Find(e.id);
                if (r == null) continue;
                r.finished = e.finished; r.dnf = e.dnf; r.finishOrder = e.finishOrder; r.finishTime = e.finishTime; r.bestLap = e.bestLap; r.crashes = e.crashes; r.final = e.place;
            }
            Standings();
            if (phase != RacePhase.Over) { phase = RacePhase.Over; if (OnOver != null) OnOver(); }
        }

        /// <summary>The host's side of ApplyResults: what to send.</summary>
        public void CollectResults(List<Result> into)
        {
            into.Clear();
            for (int i = 0; i < racers.Count; i++)
            {
                var r = racers[i];
                into.Add(new Result { id = r.id, finished = r.finished, dnf = r.dnf, finishOrder = r.finishOrder, finishTime = r.finishTime, bestLap = r.bestLap, crashes = r.crashes, place = r.position });
            }
        }

        /// <summary>The race is over now whatever the rules say (a lost connection: nobody will tell us how it ended).</summary>
        public void ForceOver() { if (phase != RacePhase.Over) { phase = RacePhase.Over; if (OnOver != null) OnOver(); } }
    }
}

using System;
using System.Collections.Generic;

namespace PryingEyes.Proc
{
    /// <summary>What a building of the desert is made of (Deserto/DesertWorld.cs picks one per site).</summary>
    public enum BuildingKind { Casa, Cappella, Locanda, Chiesa, Albergo, Teatro, Sanatorio, Complesso }

    /// <summary>The state a building is found in: whole, a ruin (roofs gone, walls low, breaches), split in two pieces drifted apart, or
    /// collapsing (almost everything open to the sky, rooms missing).</summary>
    public enum BuildingState { Intact, Ruin, Split, Collapse }

    /// <summary>The buildings of the desert, made of the rooms of the floors: the same wings (hotel, church, theatre, institute) that PlanGen
    /// builds for the standard game, kept whole, carved, ruined or pulled apart. Pure integer geometry like every plan, so tools/planlab and
    /// tools/desertlab generate and check them outside Unity. Also the tower and catacomb levels: a stack of identical-footprint plans linked by a
    /// stair room that has no floor or no ceiling (the ramps are built by DesertStairs).</summary>
    public static partial class PlanGen
    {
        /// <summary>Floor to floor in a tower or a catacomb (m): the rooms are exactly this tall, so the facade has no band between levels.</summary>
        public const float LevelHeight = 5.4f;
        /// <summary>The widest a building of the desert may be (dm): the cells of the world are 200 m wide.</summary>
        public const int BuildingSpan = 1100;

        /// <summary>The stair room of a stack level (dm): x 0..StairW, z 0..StairD, its door is on the east wall, at the middle of z.</summary>
        public const int StairW = 66, StairD = 34;

        // ------------------------------------------------------------------ the buildings
        /// <summary>A building of the desert, in its own coordinates (the wing's origin): the caller moves it with <see cref="Place"/>. Null if no
        /// valid plan came out of the attempts (the caller picks another seed).</summary>
        public static Plan Building(int seed, BuildingKind kind, BuildingState state)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var r = new Rng(unchecked(seed * 92821 + attempt * 6151 + 977));
                var p = RawBuilding(r, kind);
                if (p == null || p.rooms.Count == 0) { LastBuildingFailure = "no wing came out"; continue; }
                p.seed = seed; p.floor = 1; p.name = kind + " (" + state + ")";
                foreach (var room in p.rooms) { room.label = room.kind.ToString(); room.variant = r.Range(0, 1000); }
                if (state == BuildingState.Split) p = SplitPlan(p, r);
                else if (state == BuildingState.Collapse && p.rooms.Count > 5)
                {
                    if (r.Chance(0.5)) p = SplitPlan(p, r);
                    p = DropRooms(p, r, 0.22);
                }
                if (p.rooms.Count == 0) { LastBuildingFailure = "every room dropped"; continue; }
                AddWindows(p, r);
                AddThemeWindows(p, r);
                DropWindowsOnDoors(p);
                Decay(p, r, state);
                if (!EnsureEntrance(p, r)) { LastBuildingFailure = "no wall for a door"; continue; }
                p.ComputeBounds();
                if (p.maxX - p.minX > BuildingSpan || p.maxZ - p.minZ > BuildingSpan) { LastBuildingFailure = "wider than the cell"; continue; }
                string fault = BuildingFault(p);
                if (fault != null) { LastBuildingFailure = fault; continue; }
                return p;
            }
            return null;
        }

        /// <summary>Why the last attempt of <see cref="Building"/> was thrown away (for the lab and the log).</summary>
        public static string LastBuildingFailure = "";

        static Plan RawBuilding(Rng r, BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Casa: return House(r);
                case BuildingKind.Locanda: return MiniHotel(r);
                case BuildingKind.Cappella: return Single(ChurchWing(r, true), r.Chance(0.3) ? Theme.Sanatorio : Theme.Albergo);
                case BuildingKind.Chiesa: return Single(ChurchWing(r, false), r.Chance(0.3) ? Theme.Sanatorio : Theme.Albergo);
                case BuildingKind.Albergo:
                    {
                        bool inst = r.Chance(0.3);
                        return Single(HotelWing(r, false, inst ? KindsIstituto : KindsAlbergo), inst ? Theme.Sanatorio : Theme.Albergo);
                    }
                case BuildingKind.Teatro: return WithTheme(OneUnit(r, false, Theme.Teatro), Theme.Teatro);
                case BuildingKind.Sanatorio: return WithTheme(OneUnit(r, false, Theme.Sanatorio), Theme.Sanatorio);
                default: return WithTheme(OneUnit(r, r.Chance(0.4), Theme.Albergo), Theme.Albergo);
            }
        }

        static Plan WithTheme(Plan p, Theme t)
        {
            if (p != null) p.theme = t;
            return p;
        }

        static Plan Single(Wing w, Theme t)
        {
            var p = new Plan { theme = t };
            foreach (var room in w.rooms) { room.id = p.rooms.Count; p.rooms.Add(room); }
            p.openings.AddRange(w.ops);
            p.ComputeBounds();
            return p;
        }

        /// <summary>One to three rooms in a row, with a door between each: a hut.</summary>
        static Plan House(Rng r)
        {
            var w = new Wing(false, 0);
            int n = r.Range(1, 4);
            var kinds = new[] { RoomKind.Camera, RoomKind.Suite, RoomKind.Servizio, RoomKind.Cucina };
            int depth = Grow(r.Pick(54, 60, 66));
            int x = 0;
            Room prev = null;
            for (int i = 0; i < n; i++)
            {
                int wd = Grow(r.Pick(40, 48, 56));
                var k = i == 0 ? (r.Chance(0.5) ? RoomKind.Camera : RoomKind.Suite) : kinds[r.Range(0, kinds.Length)];
                var room = w.Add(k, x, 0, x + wd, depth, r);
                if (prev != null) w.Connect(prev, room, OpenStyle.Porta, 14, int.MinValue, 3);
                prev = room; x += wd;
            }
            return Single(w, Theme.Albergo);
        }

        /// <summary>A short hotel wing: an entrance hall, a corridor and two to four rooms on each side.</summary>
        static Plan MiniHotel(Rng r)
        {
            var w = new Wing(false, 0);
            int cw = Grow(r.Pick(24, 26));
            int per = r.Range(2, 5);
            var widths = new int[per];
            int len = 0;
            for (int i = 0; i < per; i++) { widths[i] = Grow(r.Pick(36, 42, 48)); len += widths[i]; }
            var cor = w.Add(RoomKind.Corridoio, 0, 0, len, cw, r);
            int pad = Grow(24);
            var atr = w.Add(RoomKind.Atrio, -Grow(r.Pick(60, 70)), -pad, 0, cw + pad, r);
            w.Connect(atr, cor, OpenStyle.PortaLarga, cw, margin: 0);
            for (int side = 0; side < 2; side++)
            {
                int x = 0;
                for (int i = 0; i < per; i++)
                {
                    int depth = Grow(r.Pick(54, 60));
                    var room = side == 0 ? w.Add(RoomKind.Camera, x, cw, x + widths[i], cw + depth, r) : w.Add(RoomKind.Camera, x, -depth, x + widths[i], 0, r);
                    w.Connect(room, cor, OpenStyle.Porta, 14, x + widths[i] / 2, 3);
                    x += widths[i];
                }
            }
            return Single(w, Theme.Albergo);
        }

        // ------------------------------------------------------------------ moving and copying plans
        static Room CloneRoom(Room q) => new Room
        {
            id = q.id, kind = q.kind, type = q.type, church = q.church, x0 = q.x0, z0 = q.z0, x1 = q.x1, z1 = q.z1, wallH = q.wallH, ridgeH = q.ridgeH,
            ceil = q.ceil, ridgeAlongX = q.ridgeAlongX, seed = q.seed, label = q.label, variant = q.variant, upperGallery = q.upperGallery,
            cornerRadius = q.cornerRadius, ruined = q.ruined, crumble = q.crumble, noFloor = q.noFloor, noCeil = q.noCeil, wing = q.wing, unit = q.unit,
        };

        static Opening CloneOpening(Opening o) => new Opening
        {
            a = o.a, b = o.b, alongX = o.alongX, line = o.line, center = o.center, width = o.width, style = o.style, sill = o.sill, top = o.top,
            oneSided = o.oneSided, noGlass = o.noGlass, variant = o.variant,
        };

        static Plan CloneHead(Plan p) => new Plan { seed = p.seed, floor = p.floor, name = p.name, epithet = p.epithet, theme = p.theme, units = p.units, catacombe = p.catacombe };

        /// <summary>The plan turned by 'rot' quarter turns (after a mirror, if asked) and moved by (dx, dz) dm: the building lands where its site is.</summary>
        public static Plan Place(Plan p, int rot, bool mirror, int dx, int dz)
        {
            var xf = new Xf { mirror = mirror, rot = rot & 3, tx = dx, tz = dz };
            var q = CloneHead(p);
            foreach (var room in p.rooms)
            {
                xf.Apply(room.x0, room.z0, out int ax, out int az);
                xf.Apply(room.x1, room.z1, out int bx, out int bz);
                var c = CloneRoom(room);
                c.x0 = Math.Min(ax, bx); c.x1 = Math.Max(ax, bx); c.z0 = Math.Min(az, bz); c.z1 = Math.Max(az, bz);
                if ((xf.rot & 1) == 1) c.ridgeAlongX = !room.ridgeAlongX;
                q.rooms.Add(c);
            }
            foreach (var o in p.openings) q.openings.Add(TransformOpening(o, xf));
            q.ComputeBounds();
            return q;
        }

        /// <summary>The plan keeping only the rooms 'keep' says yes to. The openings between a kept room and a removed one stay, turned into open
        /// breaches to the outside (no glass): the wall that faced the lost room now faces the desert.</summary>
        static Plan Subset(Plan p, Func<Room, bool> keep)
        {
            var map = new int[p.rooms.Count];
            var q = CloneHead(p);
            for (int i = 0; i < map.Length; i++)
            {
                var room = p.rooms[i];
                if (keep(room)) { map[i] = q.rooms.Count; var c = CloneRoom(room); c.id = q.rooms.Count; q.rooms.Add(c); }
                else map[i] = -1;
            }
            foreach (var o in p.openings)
            {
                bool ka = map[o.a] >= 0, kb = o.b >= 0 && map[o.b] >= 0;
                if (!ka && !kb) continue;
                var c = CloneOpening(o);
                if (ka && (kb || o.b < 0)) { c.a = map[o.a]; if (kb) c.b = map[o.b]; }
                else if (ka) { c.a = map[o.a]; c.b = -1; c.noGlass = true; }                // the door's other room is gone: a breach
                else { c.a = map[o.b]; c.b = -1; c.noGlass = true; }                         // the owner room is gone, the neighbour keeps the gap
                if (c.b < 0 && c.oneSided && !ka) continue;
                q.openings.Add(c);
            }
            q.ComputeBounds();
            return q;
        }

        // ------------------------------------------------------------------ splitting and ruining
        /// <summary>Cuts the plan along a line no room straddles (a wall every wing has: the church's narthex, the hotel's lobby, an aisle) and moves
        /// the far half away (60 to 130 dm of sand between them, and a little to the side): the doors that joined them become breaches on both
        /// sides. A plan with no such line stays whole.</summary>
        static Plan SplitPlan(Plan p, Rng r)
        {
            p.ComputeBounds();
            var cuts = new List<(bool alongX, int c)>();
            foreach (bool ax in new[] { true, false })
            {
                var edges = new SortedSet<int>();
                foreach (var room in p.rooms) { edges.Add(ax ? room.x0 : room.z0); edges.Add(ax ? room.x1 : room.z1); }
                foreach (int c in edges)
                {
                    int left = 0, right = 0;
                    bool straddle = false;
                    foreach (var room in p.rooms)
                    {
                        int lo = ax ? room.x0 : room.z0, hi = ax ? room.x1 : room.z1;
                        if (lo < c && hi > c) { straddle = true; break; }
                        if (hi <= c) left++; else right++;
                    }
                    if (!straddle && left >= 2 && right >= 2) cuts.Add((ax, c));
                }
            }
            if (cuts.Count == 0) return p;
            var pick = cuts[r.Range(0, cuts.Count)];
            bool alongX = pick.alongX;
            var inB = new bool[p.rooms.Count];
            foreach (var room in p.rooms) inB[room.id] = (alongX ? room.x0 : room.z0) >= pick.c;
            int gap = r.Range(60, 130) & ~1, lateral = r.Range(-30, 31) & ~1;
            int dx = alongX ? gap : lateral, dz = alongX ? lateral : gap;
            var q = CloneHead(p);
            foreach (var room in p.rooms)
            {
                var c = CloneRoom(room);
                if (inB[room.id]) { c.x0 += dx; c.x1 += dx; c.z0 += dz; c.z1 += dz; }
                q.rooms.Add(c);
            }
            Opening Shift(Opening o, bool move)
            {
                var c = CloneOpening(o);
                if (move)
                {
                    if (o.alongX) { c.center += dx; c.line += dz; } else { c.center += dz; c.line += dx; }
                }
                return c;
            }
            foreach (var o in p.openings)
            {
                bool bB = o.b >= 0 && inB[o.b];
                if (o.b >= 0 && inB[o.a] != bB)
                {
                    // the door between the halves: a breach on each side
                    var ca = Shift(o, inB[o.a]); ca.b = -1; ca.noGlass = true; q.openings.Add(ca);
                    var cb = Shift(o, bB); cb.a = o.b; cb.b = -1; cb.noGlass = true; q.openings.Add(cb);
                }
                else q.openings.Add(Shift(o, inB[o.a]));
            }
            q.ComputeBounds();
            return q;
        }

        /// <summary>The wall that faced a lost room now faces the sand and the window generator may put a window where the door (now a breach) is:
        /// windows that touch a door of the same wall are dropped.</summary>
        static void DropWindowsOnDoors(Plan p)
        {
            foreach (var w in p.openings.ToArray())
            {
                if (w.b >= 0 || w.sill < 0.01f) continue;
                foreach (var o in p.openings)
                {
                    if (o == w || o.alongX != w.alongX || o.line != w.line || (o.a != w.a && o.b != w.a)) continue;
                    if (o.b < 0 && o.sill > 0.01f) continue;                    // another window: the generator keeps those apart
                    if (w.Lo < o.Hi + 1 && o.Lo < w.Hi + 1 && w.sill < o.top) { p.openings.Remove(w); break; }
                }
            }
        }

        /// <summary>Removes a fraction of the rooms (never the first two, which hold the entrance in most wings).</summary>
        static Plan DropRooms(Plan p, Rng r, double frac)
        {
            var drop = new HashSet<int>();
            foreach (var room in p.rooms)
                if (room.id >= 2 && r.Chance(frac) && p.rooms.Count - drop.Count > 4) drop.Add(room.id);
            if (drop.Count == 0) return p;
            return Subset(p, room => !drop.Contains(room.id));
        }

        /// <summary>Roofs fall in, walls crumble, windows lose their glass, breaches open in the walls of the roofless rooms.</summary>
        static void Decay(Plan p, Rng r, BuildingState state)
        {
            double pRuin = state == BuildingState.Intact ? 0.05 : state == BuildingState.Ruin ? 0.55 : state == BuildingState.Split ? 0.30 : 0.85;
            foreach (var room in p.rooms)
            {
                if (room.IsLift || room.noFloor || room.noCeil || !r.Chance(pRuin)) continue;
                room.ruined = true;
                if (state == BuildingState.Intact) continue;                                    // only the roof is gone
                room.crumble = true;                                                           // the walls end in teeth and the stones lie at their feet
                float k = state == BuildingState.Collapse ? r.Range(0.34f, 0.8f) : r.Range(0.6f, 1f);
                float rise = Math.Max(0f, room.ridgeH - room.wallH);
                room.wallH = Math.Max(1.5f, room.wallH * k);
                room.ridgeH = room.wallH + rise * k;
            }
            FitOpenings(p);
            foreach (var o in p.openings)
                if (o.b < 0 && o.sill > 0.01f) o.noGlass = state != BuildingState.Intact || r.Chance(0.4);
            if (state == BuildingState.Intact) return;
            double pBreach = state == BuildingState.Ruin ? 0.55 : 0.8;
            foreach (var room in p.rooms.ToArray())
            {
                if (!room.ruined || room.IsLift) continue;
                int made = 0;
                foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                {
                    if (made >= 2) break;
                    bool ax = Plan.AlongX(s);
                    int line = Plan.Line(room, s);
                    foreach (var seg in p.Segments(room, s))
                    {
                        if (seg.other >= 0 || seg.a1 - seg.a0 < 44 || made >= 2 || !r.Chance(pBreach)) continue;
                        int width = r.Pick(24, 30);
                        int centre = r.Range(seg.a0 + 14 + width / 2, seg.a1 - 14 - width / 2 + 1);
                        bool clash = false;
                        foreach (var o in p.openings)
                            if (o.alongX == ax && o.line == line && (o.a == room.id || o.b == room.id) && centre + width / 2 + 4 > o.Lo && centre - width / 2 - 4 < o.Hi) { clash = true; break; }
                        if (clash) continue;
                        var br = new Opening { a = room.id, b = -1, alongX = ax, line = line, center = centre, width = width, style = OpenStyle.PortaLarga, noGlass = true, variant = r.Range(0, 4) };
                        Opening.Defaults(br);
                        br.top = Math.Min(br.top, room.wallH);
                        p.openings.Add(br);
                        made++;
                    }
                }
            }
        }

        /// <summary>After the walls are lowered: an opening taller than the wall of its rooms is cut down, or turned into a plain one, or dropped.</summary>
        static void FitOpenings(Plan p)
        {
            foreach (var o in p.openings.ToArray())
            {
                float hMax = p.rooms[o.a].wallH;
                if (o.b >= 0) hMax = Math.Min(hMax, p.rooms[o.b].wallH);
                if (o.top <= hMax + 0.001f) continue;
                bool window = o.sill > 0.01f;
                if (window && o.sill + 0.5f > hMax) { p.openings.Remove(o); continue; }
                if (o.Pointed)
                {
                    o.style = window ? OpenStyle.Finestra : (o.width >= 20 ? OpenStyle.PortaLarga : OpenStyle.Porta);
                    float sill = o.sill;
                    Opening.Defaults(o);
                    if (window) o.sill = Math.Min(sill, hMax - 0.6f);
                }
                o.top = Math.Min(o.top, hMax);
                if (o.top < o.sill + 0.5f) p.openings.Remove(o);
            }
        }

        /// <summary>Makes sure somebody can walk in: if no opening reaches the ground from outside, a door opens in the best long wall facing
        /// the sand (the windows it touches are removed). False if no wall is long enough.</summary>
        static bool EnsureEntrance(Plan p, Rng r)
        {
            foreach (var o in p.openings)
                if (o.b < 0 && o.sill < 0.01f && !o.oneSided && !p.rooms[o.a].IsLift && !p.rooms[o.a].noFloor) return true;
            Room best = null; Side bestSide = Side.S; Seg bestSeg = null; int bestW = 0;
            foreach (var room in p.rooms)
            {
                if (room.IsLift || room.noFloor || room.noCeil) continue;
                bool big = room.kind == RoomKind.Atrio || room.kind == RoomKind.Nartece || room.kind == RoomKind.Accettazione || room.kind == RoomKind.Foyer || room.kind == RoomKind.Platea;
                foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                    foreach (var seg in p.Segments(room, s))
                    {
                        if (seg.other >= 0) continue;
                        int len = seg.a1 - seg.a0;
                        int weight = len + (big ? 400 : 0);
                        if (len >= 30 && weight > bestW) { best = room; bestSide = s; bestSeg = seg; bestW = weight; }
                    }
            }
            if (best == null) return false;
            bool ax = Plan.AlongX(bestSide);
            int line = Plan.Line(best, bestSide);
            int width = (bestSeg.a1 - bestSeg.a0) >= 60 ? 20 : 14;
            int centre = (bestSeg.a0 + bestSeg.a1) / 2;
            var door = new Opening { a = best.id, b = -1, alongX = ax, line = line, center = centre, width = width, style = width >= 20 ? OpenStyle.PortaLarga : OpenStyle.Porta, noGlass = true };
            Opening.Defaults(door);
            door.top = Math.Min(door.top, best.wallH);
            p.openings.RemoveAll(o => o.alongX == ax && o.line == line && (o.a == best.id || o.b == best.id) && centre + width / 2 + 3 > o.Lo && centre - width / 2 - 3 < o.Hi);
            p.openings.Add(door);
            return true;
        }

        /// <summary>The first thing wrong with a building, or null: overlapping rooms, an opening off its wall or without a neighbour, a window
        /// that does not face the sand, two openings on top of each other. (The checks of Validate, without lifts and objectives.)</summary>
        public static string BuildingFault(Plan p)
        {
            for (int i = 0; i < p.rooms.Count; i++)
                for (int j = i + 1; j < p.rooms.Count; j++)
                    if (Overlap(p.rooms[i], p.rooms[j], 0)) return $"rooms overlap {p.rooms[i]} {p.rooms[j]}";
            foreach (var o in p.openings)
            {
                if (o.a < 0 || o.a >= p.rooms.Count) return "opening without a room";
                var a = p.rooms[o.a];
                Side s = o.alongX ? (Plan.Line(a, Side.N) == o.line ? Side.N : Side.S) : (Plan.Line(a, Side.E) == o.line ? Side.E : Side.W);
                if (Plan.Line(a, s) != o.line) return "opening off its line " + a;
                Plan.Span(a, s, out int lo, out int hi);
                if (o.Lo < lo || o.Hi > hi) return "opening off its wall " + a;
                if (o.b >= 0)
                {
                    if (o.b >= p.rooms.Count) return "opening to a missing room";
                    var b = p.rooms[o.b];
                    Side os = Plan.Opposite(s);
                    if (Plan.Line(b, os) != o.line) return "opening without a neighbour " + a;
                    Plan.Span(b, os, out int blo, out int bhi);
                    if (o.Lo < blo || o.Hi > bhi) return "opening off the neighbour " + a;
                }
                else if (!o.oneSided)
                {
                    bool voidOk = false;
                    foreach (var seg in p.Segments(a, s)) if (seg.other < 0 && o.Lo >= seg.a0 && o.Hi <= seg.a1) voidOk = true;
                    if (!voidOk) return "outside opening not on the sand " + a;
                }
            }
            for (int i = 0; i < p.openings.Count; i++)
                for (int j = i + 1; j < p.openings.Count; j++)
                {
                    var o = p.openings[i]; var q = p.openings[j];
                    if (o.alongX != q.alongX || o.line != q.line) continue;
                    bool shareRoom = o.a == q.a || o.a == q.b || (o.b >= 0 && (o.b == q.a || o.b == q.b));
                    if (!shareRoom || o.sill >= q.top || q.sill >= o.top) continue;
                    if (o.Lo < q.Hi + 1 && q.Lo < o.Hi + 1) return "openings overlap " + p.rooms[o.a];
                }
            return null;
        }

        // ------------------------------------------------------------------ the stacks: towers and catacombs
        /// <summary>The rooms a level of a stack is made of: the stair room, a big hall east of it and five rooms around the hall, about 37 x 40 m.</summary>
        public const int StackRooms = 7;

        /// <summary>One level of a tower or of a catacomb. 'level' counts from the bottom (0 = lowest); the entrance is on the level 'entrance',
        /// the boss hall on 'boss'. Every level has the same footprint, with the stair room at the same place: that room has no floor from the
        /// second level up and no ceiling below the last (DesertStairs builds the ramps in it). 'church' makes stone halls and gothic arches
        /// (the catacombs, a bell tower), otherwise the rooms follow 'theme'. The windows are those of the outside walls.</summary>
        public static Plan StackLevel(int seed, int level, int levels, int entrance, int boss, Theme theme, bool church, bool broken)
        {
            var r = new Rng(unchecked(seed * 31337 + level * 7919 + 5));
            var w = new Wing(church, 0);
            bool isEntrance = level == entrance, isBoss = level == boss;
            var stair = w.Add(church ? RoomKind.Sacrestia : RoomKind.Servizio, 0, 0, StairW, StairD, r);
            stair.noFloor = level > 0;
            stair.noCeil = level < levels - 1;
            RoomKind hallKind, bigKind;
            RoomKind[] around;
            if (church)
            {
                hallKind = isEntrance ? RoomKind.Nartece : RoomKind.Navata;
                bigKind = RoomKind.Cappella;
                around = new[] { RoomKind.Cappella, RoomKind.Sacrestia, RoomKind.Cripta, RoomKind.Navatella };
            }
            else if (theme == Theme.Sanatorio)
            {
                hallKind = isEntrance ? RoomKind.Accettazione : isBoss ? RoomKind.SalaOperatoria : RoomKind.Refettorio;
                bigKind = RoomKind.Corsia;
                around = new[] { RoomKind.Degenza, RoomKind.Cella, RoomKind.Archivio, RoomKind.SalaMedici, RoomKind.Obitorio, RoomKind.Lavanderia };
            }
            else
            {
                hallKind = isEntrance ? RoomKind.Atrio : isBoss ? RoomKind.SalaBallo : (r.Chance(0.5) ? RoomKind.Ristorante : RoomKind.SalaBallo);
                bigKind = RoomKind.Suite;
                around = new[] { RoomKind.Camera, RoomKind.Suite, RoomKind.Servizio, RoomKind.Cucina };
            }
            var hall = w.Add(hallKind, StairW, -83, StairW + 200, 117, r);
            var cells = new[]
            {
                new[] { StairW, 117, StairW + 100, 217 }, new[] { StairW + 100, 117, StairW + 200, 217 },       // north row
                new[] { StairW, -183, StairW + 100, -83 }, new[] { StairW + 100, -183, StairW + 200, -83 },     // south row
                new[] { StairW + 200, -83, StairW + 300, 117 },                                                 // east
            };
            var rooms = new List<Room>();
            for (int i = 0; i < cells.Length; i++)
            {
                var k = i == 4 ? bigKind : around[r.Range(0, around.Length)];
                rooms.Add(w.Add(k, cells[i][0], cells[i][1], cells[i][2], cells[i][3], r));
            }
            OpenStyle door = church ? OpenStyle.Arco : OpenStyle.PortaLarga;
            w.Connect(stair, hall, door, 14, StairD / 2, 3);
            foreach (var room in rooms) w.Connect(hall, room, door, church ? 18 : 16, int.MinValue, 3);
            w.Connect(rooms[0], rooms[1], door, 14, int.MinValue, 3);
            w.Connect(rooms[2], rooms[3], door, 14, int.MinValue, 3);
            foreach (var room in w.rooms)
            {
                room.wallH = LevelHeight; room.ridgeH = LevelHeight;
                room.ceil = room == hall || room == stair ? CeilKind.Beams : CeilKind.Flat;
                room.ridgeAlongX = true;
            }
            var p = Single(w, theme);
            p.seed = seed; p.floor = 1; p.name = (church ? "Level " : "Floor ") + level;
            foreach (var room in p.rooms) { room.label = room.kind.ToString(); room.variant = r.Range(0, 1000); }
            if (!church || level >= entrance) { AddWindows(p, r); AddThemeWindows(p, r); }          // under the sand there are no windows
            foreach (var o in p.openings) if (o.b < 0 && o.sill > 0.01f) o.noGlass = broken || r.Chance(0.35);
            if (isEntrance)
            {
                // the door to the sand: on the west wall of the hall, south of the stair room (the other three walls touch rooms)
                var door0 = new Opening { a = hall.id, b = -1, alongX = false, line = hall.x0, center = -40, width = 24, style = OpenStyle.PortaLarga, noGlass = true };
                Opening.Defaults(door0);
                p.openings.RemoveAll(o => !o.alongX && o.line == hall.x0 && o.b < 0 && o.a == hall.id && door0.center + 15 > o.Lo && door0.center - 15 < o.Hi);
                p.openings.Add(door0);
            }
            if (isBoss) hall.type = RoomType.Boss;
            p.ComputeBounds();
            return p;
        }

        /// <summary>The stair room of a stack level (the first room).</summary>
        public static Room StairRoom(Plan p) => p.rooms[0];
        public static Room HallRoom(Plan p) => p.rooms[1];
    }
}

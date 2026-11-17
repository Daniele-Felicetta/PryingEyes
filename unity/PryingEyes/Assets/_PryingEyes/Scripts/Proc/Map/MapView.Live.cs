using System.Collections.Generic;
using UnityEngine;
using PryingEyes.Proc.MapArt;

namespace PryingEyes.Proc
{
    public sealed partial class MapView
    {
        // ---- quello che si muove: si ridisegna a ogni fotogramma in un canvas a parte (poche centinaia di vertici)
        Mesh2D liveMesh;
        Pen livePen;
        MapGraphic liveGfx;
        float pingT = -1f;
        readonly List<MateNow> mates = new List<MateNow>(4);
        static readonly V2[] arrowPoly = new V2[4], starPoly = new V2[10];
        static readonly Color[] MateCols =
        {
            new Color(0.62f, 0.74f, 0.42f), new Color(0.47f, 0.64f, 0.96f), new Color(0.93f, 0.47f, 0.45f), new Color(0.95f, 0.78f, 0.38f),
        };
        static readonly Col[] DropCols = { Col.F(1.0f, 0.70f, 0.20f), Col.F(0.95f, 0.18f, 0.14f), Col.F(1.0f, 0.86f, 0.30f), Col.F(0.30f, 0.60f, 1.0f) };

        struct MateNow { public int index; public V2 pos; public float yaw; public bool alive, downed, torch; }

        void BuildLive()
        {
            liveMesh = new Mesh2D();
            livePen = new Pen(liveMesh, 7) { wobble = 0f };
            liveGfx = NewGraphic("Vivo", liveRt);
        }

        /// <summary>Un ping: un'onda parte da te e si allarga sulla mappa (la stessa che si vede a schermo).</summary>
        public void Ping() { pingT = 0f; }

        void PaintLive(float dt, float e, V2 player, PlayerController pl)
        {
            var m = liveMesh;
            m.Clear();
            float t = Time.unscaledTime;
            var scene = map.scene;
            if (scene == null) return;
            // gli occhi nelle stanze mai visitate: sbattono le palpebre e seguono chi guarda
            for (int i = 0; i < map.rooms.Length; i++)
            {
                if (scene.seen[i] != RoomSeen.Rumor) continue;
                float ra = RoomAlpha(i);
                foreach (var eye in map.rooms[i].eyes) PaintRumorEye(eye, ra, player, t);
            }
            PaintDrops(t);
            PaintMarks(t);
            PaintEnemies(player, t);
            GatherMates(pl);
            foreach (var mate in mates) PaintMate(mate, t);
            PaintSelf(player, pl, t);
            PaintPing(player, dt);
            liveGfx.Refresh();
        }

        // ------------------------------------------------------------------ gli occhi delle macchie
        void PaintRumorEye(MapEye eye, float alpha, V2 player, float t)
        {
            float cycle = 4.2f + Mathf.Repeat(eye.phase, 3.3f);
            float u = Mathf.Repeat(t + eye.phase, cycle);
            float open = 0.60f;
            if (u < 0.24f) open *= Mathf.Abs(u / 0.24f * 2f - 1f);                     // la palpebra scende e risale in un quarto di secondo
            V2 d = player - eye.pos;
            float dist = d.Len;
            open = Mathf.Min(1f, open + Mathf.Clamp01(1f - dist / 38f) * 0.38f);       // piu' ci si avvicina, piu' si spalancano
            V2 look = dist > 0.1f && dist < 70f ? d * (1f / dist) * (eye.size * 0.2f) : default;
            float a = alpha * Mathf.Clamp01(1.25f - dist / 60f) * 0.95f + alpha * 0.18f;
            MapPainter.Eye(livePen, eye.pos, eye.size, open, look, Col.F(0.84f, 0.77f, 0.60f, a), Col.F(0.60f, 0.07f, 0.05f, a), Col.F(0.03f, 0.02f, 0.02f, a), Col.F(0.07f, 0.05f, 0.04f, a));
        }

        // ------------------------------------------------------------------ i nemici che ti hanno notato (le stesse regole dei segnalini dell'HUD)
        void PaintEnemies(V2 player, float t)
        {
            bool seeAll = Perks.EyeOn;
            float range = seeAll ? 70f : 44f;
            foreach (var en in Enemy.All)
            {
                if (en == null || en.IsDead) continue;
                if (!seeAll && en.alert == Alert.Patrol && en.awareness < 0.07f) continue;
                Vector3 p3 = en.transform.position;
                V2 ep = new V2(p3.x, p3.z);
                V2 d = player - ep;
                float dist = d.Len;
                if (dist > range) continue;
                float open, size = 3.0f;
                Col sclera = Col.F(0.92f, 0.88f, 0.78f), iris;
                switch (en.alert)
                {
                    case Alert.Hunting:
                        open = 1f; iris = Col.F(0.95f, 0.12f, 0.08f); size = 3.6f + 0.5f * Mathf.Abs(Mathf.Sin(t * 9f));
                        break;
                    case Alert.Searching:
                        open = 0.66f; iris = Col.F(0.98f, 0.66f, 0.18f);
                        break;
                    default:
                    {
                        float a = Mathf.Clamp01((en.awareness - 0.05f) / 0.95f);
                        open = Mathf.Clamp(0.18f + a * 0.55f, 0.1f, 0.8f);
                        var c = Perks.Of(PerkId.Occhio).color;
                        iris = a <= 0.02f ? Col.F(c.r, c.g, c.b) : Col.F(0.95f, 0.7f, 0.22f);
                        break;
                    }
                }
                float fade = Mathf.Clamp01(1.3f - dist / range);
                V2 look = dist > 0.1f ? d * (1f / dist) * (size * 0.2f) : default;
                // un alone scuro sotto: l'occhio si legge anche sul tratteggio
                livePen.Disc(ep, size * 0.62f, Col.F(0f, 0f, 0f, 0.38f * fade), 14);
                MapPainter.Eye(livePen, ep, size, open, look, sclera.A(fade), iris.A(fade), Col.F(0.03f, 0.02f, 0.02f, fade), Col.F(0.06f, 0.04f, 0.03f, fade));
            }
        }

        // ------------------------------------------------------------------ potenziamenti a terra e segnali del ping
        void PaintDrops(float t)
        {
            var live = Drops.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var d = live[i];
                if (d == null) continue;
                Vector3 p3 = d.transform.position;
                var room = map.floor.RoomAt(p3);
                if (room == null || map.StateOf(room.id) != RoomSeen.Seen) continue;
                var c = DropCols[Mathf.Clamp((int)d.kind, 0, 3)];
                V2 pos = new V2(p3.x, p3.z);
                float pulse = 1f + 0.16f * Mathf.Sin(t * 5f + i);
                for (int k = 0; k < 10; k++) starPoly[k] = pos + V2.Polar(1.5707963f + k * 0.6283185f, ((k & 1) == 0 ? 1.25f : 0.55f) * pulse);
                livePen.Glow(pos, 3.2f * pulse, c.A(0.45f), 18);
                livePen.Fill(starPoly, c.A(0.98f));
                livePen.Path(starPoly, true, 0.10f, MapStyle.Ink, 0f);
            }
        }

        void PaintMarks(float t)
        {
            var g = Guide.I;
            if (g == null) return;
            foreach (var mk in g.marks)
            {
                if (!mk.shown) continue;
                float age = t > 0f ? Time.time - mk.born : 0f;
                if (age > Guide.MarkLife) continue;
                float fade = Mathf.Clamp01((Guide.MarkLife - age) / 1.4f);
                float ph = Mathf.Repeat(age, 1.1f);
                var col = mk.kind == 0 ? Col.F(0.55f, 0.78f, 1f) : mk.kind == 2 ? Col.F(0.5f, 1f, 0.62f) : Col.F(0.72f, 0.76f, 0.84f);
                V2 pos = new V2(mk.pos.x, mk.pos.z);
                livePen.Ring(pos, 1.2f + ph * 5f, 0.20f, col.A(fade * (1f - ph / 1.1f) * 0.9f), 28, 0f);
                livePen.Disc(pos, 0.65f, col.A(fade), 10);
            }
        }

        void PaintPing(V2 player, float dt)
        {
            if (pingT < 0f) return;
            pingT += dt;
            float r = pingT * Guide.WaveSpeed;
            if (r > 64f) { pingT = -1f; return; }
            float a = Mathf.Clamp01(1f - r / 64f);
            livePen.Ring(player, r, 0.30f, Col.F(0.55f, 0.85f, 1f, 0.55f * a), 64, 0f);
        }

        // ------------------------------------------------------------------ i compagni
        void GatherMates(PlayerController me)
        {
            mates.Clear();
            var game = Coop.Game;
            if (Coop.Active && game != null)
            {
                foreach (var kv in game.Players)
                {
                    var peer = kv.Value;
                    if (peer == null || !peer.hasState || !peer.Shown) continue;
                    var s = peer.last;
                    mates.Add(new MateNow { index = kv.Key, pos = new V2(peer.Feet.x, peer.Feet.z), yaw = s.yaw, alive = s.alive, downed = s.alive && s.downed, torch = s.torch });
                }
            }
            else if (Seats.Split)
            {
                foreach (var st in Seats.All)
                {
                    var p = st.player;
                    if (st.hud == hud || p == null) continue;
                    var torch = st.TorchNow;
                    mates.Add(new MateNow { index = st.index, pos = new V2(p.transform.position.x, p.transform.position.z), yaw = p.yaw, alive = p.alive, downed = p.alive && p.Downed, torch = torch != null && torch.Lit });
                }
            }
        }

        void PaintMate(MateNow m, float t)
        {
            var room = map.floor.RoomAt(new Vector3(m.pos.x, 1f, m.pos.y));
            if (room == null) return;
            var c = MateCols[Mathf.Clamp(m.index, 0, MateCols.Length - 1)];
            var col = Col.F(c.r, c.g, c.b);
            if (!m.alive) { col = Col.F(0.4f, 0.4f, 0.4f, 0.8f); }
            Arrow(m.pos, m.yaw, 1.45f, col, MapStyle.Ink);
            if (m.alive && m.torch) Cone(m.pos, m.yaw, 5.5f, 0.5f, Col.F(1f, 0.88f, 0.55f, 0.28f));
            if (m.downed)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 7f);
                livePen.Ring(m.pos, 2.4f + pulse * 0.8f, 0.24f, Col.F(0.95f, 0.15f, 0.1f, 0.5f + 0.5f * pulse), 20, 0f);
            }
        }

        // ------------------------------------------------------------------ tu
        void PaintSelf(V2 pos, PlayerController pl, float t)
        {
            var torch = Torch.I;
            bool lit = torch != null && torch.Lit;
            float yaw = pl.yaw;
            // il cono di luce della torcia (o il poco che si vede senza)
            if (lit) Cone(pos, yaw, 8.5f, 0.52f, Col.F(1f, 0.86f, 0.52f, 0.52f));
            else Cone(pos, yaw, 3.4f, 0.7f, Col.F(0.75f, 0.85f, 1f, 0.22f));
            // il battito: un anello che si allarga, piu' presto e piu' rosso quando qualcuno ti ha visto
            float threat = Mathf.Clamp01(Stealth.Level);
            float rate = Mathf.Lerp(0.9f, 2.6f, threat);
            float ph = Mathf.Repeat(t * rate, 1f);
            var ring = Col.Mix(Col.F(0.95f, 0.88f, 0.7f), Col.F(1f, 0.12f, 0.08f), threat);
            livePen.Ring(pos, 1.6f + ph * 2.6f, 0.17f, ring.A((1f - ph) * 0.7f), 22, 0f);
            livePen.Disc(pos, 1.5f, MapStyle.Paper.A(0.55f), 14);
            bool down = pl.Downed;
            var body = down ? Col.F(0.9f, 0.1f, 0.08f, 0.6f + 0.4f * Mathf.Sin(t * 8f)) : Col.F(0.93f, 0.16f, 0.12f);
            Arrow(pos, yaw, 1.7f, body, MapStyle.Ink);
        }

        void Arrow(V2 pos, float yaw, float size, Col fill, Col outline)
        {
            V2 dir = new V2(Mathf.Sin(yaw), Mathf.Cos(yaw)), side = dir.Perp;
            arrowPoly[0] = pos + dir * size;
            arrowPoly[1] = pos - dir * size * 0.7f + side * size * 0.72f;
            arrowPoly[2] = pos - dir * size * 0.28f;
            arrowPoly[3] = pos - dir * size * 0.7f - side * size * 0.72f;
            livePen.Fill(arrowPoly, fill);
            livePen.Path(arrowPoly, true, 0.14f, outline, 0f);
        }

        /// <summary>Un settore che sfuma verso l'esterno: la luce davanti a chi la porta.</summary>
        void Cone(V2 pos, float yaw, float radius, float half, Col inner)
        {
            float ang = 1.5707963f - yaw;
            var mesh = liveMesh;
            int hub = mesh.Vert(pos, Pen.Solid, inner);
            int first = mesh.Count;
            const int N = 14;
            var edge = inner.A(0f);
            for (int i = 0; i <= N; i++) mesh.Vert(pos + V2.Polar(ang - half + 2f * half * i / N, radius), Pen.Solid, edge);
            for (int i = 0; i < N; i++) mesh.Tri(hub, first + i, first + i + 1);
        }
    }
}

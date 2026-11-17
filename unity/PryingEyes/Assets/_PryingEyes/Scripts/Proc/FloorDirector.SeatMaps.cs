using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>La mappa e la minimappa di ogni giocatore, per chi sta su un monitor oltre il primo: Unity disegna l'IMGUI (DrawMap) solo sul primo
    /// monitor, quindi i giocatori sugli altri hanno la stessa mappa fatta con uGUI (Coop/SeatMapUi.cs), dagli stessi dati (GridMapItems e le
    /// segnalazioni di DrawMap). Quando la mappa passera' nell'HUD di ogni giocatore questo file non serve piu'.</summary>
    public partial class FloorDirector
    {
        /// <summary>Un elemento della mappa per uGUI: riquadro pieno, bordo (di spessore t) o testo, in coordinate dell'area (origine in alto a sinistra,
        /// y verso il basso: le stesse di DrawMap).</summary>
        public struct SeatMapItem
        {
            public enum Kind { Fill, Box, Text }
            public Kind kind;
            public Rect r;
            public Color c;
            public float t;
            public string text;
            public TextAnchor align;
            public int size;
            public bool wrap;
        }

        /// <summary>Le mappe si vedono ora (la stessa condizione con cui OnGUI le disegna).</summary>
        public bool MapsShown => cur != null && gm != null && gm.player != null && !GameMenu.Blocked && !transitioning && !InHub;

        /// <summary>Il giocatore si' ha la mappa grande aperta.</summary>
        public bool MapOpen(int si) => mapOpenA[si];

        /// <summary>Il giocatore si' ha la minimappa accesa.</summary>
        public bool MiniMapOn(int si) => !miniMapOff[si];

        /// <summary>Solo per le prove: apre o chiude la mappa grande e la minimappa di un giocatore.</summary>
        public void DebugShowMapFor(int si, bool full, bool mini)
        {
            mapOpenA[si] = full; miniMapOff[si] = !mini;
        }

        static readonly Color SeatMapGrid = new Color(0.40f, 0.36f, 0.32f);

        /// <summary>Gli elementi della mappa (grande o minimappa) di un giocatore in un'area di 'size' unita': stanze, segnalini, scritte, come DrawMap.
        /// La minimappa e' centrata sul giocatore, la grande mostra tutta la pianta.</summary>
        public List<SeatMapItem> SeatMapItems(int si, Vector3 player, Vector2 size, bool full)
        {
            var items = new List<SeatMapItem>();
            if (cur == null) return items;
            var plan = cur.plan;
            float span = full ? Mathf.Max((plan.maxX - plan.minX) * 0.1f, (plan.maxZ - plan.minZ) * 0.1f) + 8f : 34f;
            float scale = Mathf.Min(size.x, size.y - (full ? 45f : 0f)) / Mathf.Max(span, 1f);
            float cx = full ? (plan.minX + plan.maxX) * 0.05f : player.x;
            float cz = full ? (plan.minZ + plan.maxZ) * 0.05f : player.z;
            float px(float x) => size.x * 0.5f + (x - cx) * scale;
            float py(float z) => (size.y - (full ? 35f : 0f)) * 0.5f - (z - cz) * scale;
            if (plan.grid)
            {
                foreach (var it in GridMapItems(plan, full, px, py, scale))
                    items.Add(new SeatMapItem
                    {
                        kind = it.kind == MapItem.Kind.Fill ? SeatMapItem.Kind.Fill : it.kind == MapItem.Kind.Box ? SeatMapItem.Kind.Box : SeatMapItem.Kind.Text,
                        r = it.r, c = it.c, t = it.t, text = it.text, align = TextAnchor.MiddleCenter, size = 13,
                    });
            }
            else
                foreach (var r in plan.rooms)
                {
                    if (!explored.Contains(r.id)) continue;
                    items.Add(new SeatMapItem
                    {
                        kind = SeatMapItem.Kind.Fill, c = r.church ? new Color(0.36f, 0.40f, 0.43f) : SeatMapGrid,
                        r = new Rect(px(r.x0 * 0.1f), py(r.z1 * 0.1f), (r.x1 - r.x0) * 0.1f * scale, (r.z1 - r.z0) * 0.1f * scale),
                    });
                }
            void Mark(Vector3 p, Color color, string label)
            {
                float x = px(p.x), y = py(p.z);
                items.Add(new SeatMapItem { kind = SeatMapItem.Kind.Fill, r = new Rect(x - 3, y - 3, 6, 6), c = color });
                if (full) items.Add(new SeatMapItem { kind = SeatMapItem.Kind.Text, r = new Rect(x + 5, y - 10, 100, 20), c = Color.white, text = label, align = TextAnchor.MiddleLeft, size = 13 });
            }
            foreach (var w in foundWeapons) if (w != null) Mark(w.transform.position, Color.cyan, full ? w.displayName : "");
            foreach (var c in foundCandles) if (c != null) Mark(c.transform.position, Color.yellow, c.lit ? "Candela accesa" : "Candela");
            if (foundExit && cur.exit != null) Mark(cur.exit.transform.position, Color.green, "Uscita");
            foreach (int k in foundShrines)
            {
                var sp = ShrinePos(k);
                if (sp.HasValue) Mark(sp.Value, k == 0 ? new Color(0.72f, 0.48f, 1f) : k == 1 ? new Color(1f, 0.58f, 0.16f) : new Color(1f, 0.86f, 0.35f), k == 0 ? "Biblioteca" : k == 1 ? "Scrigno" : "Altare");
            }
            if (Seats.Split)
                foreach (var o in Seats.All)
                    if (o.index != si && o.player != null && o.player.alive) Mark(o.player.transform.position, OtherPlayerColor, o.Name);
            Mark(player, Color.red, "Tu");
            if (full)
                items.Add(new SeatMapItem
                {
                    // piu' alta e piccola di quella dell'IMGUI (che e' 40 alta, da 12): il carattere dell'HUD ha l'interlinea piu' larga e la terza riga
                    // (le tinte delle stanze della pianta a griglia) sarebbe tagliata dal bordo del pannello
                    kind = SeatMapItem.Kind.Text, r = new Rect(12, size.y - 46, size.x - 24, 44), c = Color.white, align = TextAnchor.UpperLeft, size = 11, wrap = true,
                    text = "M: chiudi   N: minimappa   • rosso: tu" + (Seats.Split ? "   • rosa: compagni" : "") + "   • azzurro: armi   • giallo: candele   • verde: uscita\n• viola: biblioteca dei perk   • arancio: scrigno delle armi   • oro: altare della consacrazione" +
                           (plan.grid ? "\nStanze: fuoco arena (candela) · oro negozio · viola biblioteca · cremisi guardiano · turchese segreta · acciaio guardie" : ""),
                });
            return items;
        }
    }
}

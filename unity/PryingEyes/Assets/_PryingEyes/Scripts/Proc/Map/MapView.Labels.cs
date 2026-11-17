using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PryingEyes.Proc.MapArt;

namespace PryingEyes.Proc
{
    public sealed partial class MapView
    {
        // ---- le scritte della carta: nomi delle stanze, ruoli, prezzi, nomi delle armi. Le scrive Unity (il pittore le indica soltanto) e restano a
        // grandezza fissa sullo schermo: l'oggetto e' figlio del contenitore scalato, quindi si scala al contrario.
        sealed class Slot
        {
            public Text text;
            public LabelKind kind;
            public int owner;
        }

        readonly List<Slot> slots = new List<Slot>();
        static Font hand;
        float labelScale = -1f;

        /// <summary>Quante scritte sono accese adesso (per le prove).</summary>
        public int ActiveLabels { get { int n = 0; foreach (var s in slots) if (s.text != null && s.text.gameObject.activeSelf) n++; return n; } }

        static readonly Color PaperHalo = new Color(0.78f, 0.66f, 0.45f, 0.82f);

        /// <summary>Il carattere a mano per i prezzi, le note e i nomi delle armi: il primo che c'e' tra quelli di Windows che sembrano una penna.</summary>
        static Font Hand()
        {
            if (hand == null) hand = Font.CreateDynamicFontFromOSFont(new[] { "Segoe Print", "Ink Free", "Segoe Script", "Lucida Handwriting", "Georgia" }, 32);
            return hand;
        }

        void LabelsReset()
        {
            foreach (var s in slots) if (s.text != null) Object.Destroy(s.text.gameObject);
            slots.Clear();
            labelScale = -1f;
        }

        void LabelsSync()
        {
            int used = 0;
            for (int i = 0; i < map.rooms.Length; i++) foreach (var l in map.rooms[i].labels) Put(ref used, l);
            if (map.doors != null) foreach (var l in map.doors.labels) Put(ref used, l);
            if (map.pois != null) foreach (var l in map.pois.labels) Put(ref used, l);
            for (int i = used; i < slots.Count; i++) slots[i].text.gameObject.SetActive(false);
            labelScale = -1f;
        }

        void Put(ref int used, MapLabel l)
        {
            while (slots.Count <= used) slots.Add(NewSlot());
            var s = slots[used++];
            s.kind = l.kind; s.owner = l.owner;
            var t = s.text;
            t.gameObject.SetActive(true);
            int extraLines = 0;
            switch (l.kind)
            {
                case LabelKind.Room: t.font = HUD.Serif(); t.fontStyle = FontStyle.Italic; extraLines = Fit(t, l.text, RoomWidthPx(l.owner), 17, 9, 0.5f, true); break;
                case LabelKind.Role: t.font = HUD.Serif(); t.fontStyle = FontStyle.Bold; Fit(t, l.text.ToUpperInvariant(), RoomWidthPx(l.owner), 11, 8, 0.7f, false); break;
                case LabelKind.Price: t.font = Hand(); t.fontSize = 16; t.fontStyle = FontStyle.Bold; t.text = l.text; break;
                case LabelKind.Question: t.font = Hand(); t.fontSize = 56; t.fontStyle = FontStyle.Bold; t.text = l.text; break;
                case LabelKind.Note: t.font = Hand(); t.fontSize = 15; t.fontStyle = FontStyle.Normal; t.text = l.text; break;
                default: t.font = Hand(); t.fontSize = 13; t.fontStyle = FontStyle.Bold; t.text = l.text; break;
            }
            t.color = new Color32(l.color.r, l.color.g, l.color.b, l.color.a);
            var rt = t.rectTransform;
            rt.anchoredPosition = new Vector2(l.pos.x, l.pos.y - 1.5f * extraLines);            // un nome a due righe scende, per non toccare il ruolo
            rt.localRotation = Quaternion.Euler(0f, 0f, l.rot * Mathf.Rad2Deg);
            // un'alone del colore della carta li stacca dal tratteggio; il punto interrogativo sta su una macchia scura e non lo vuole
            var halo = t.GetComponent<Outline>();
            halo.enabled = l.kind != LabelKind.Question;
        }

        /// <summary>La larghezza di una stanza in pixel, a 6 px al metro (la scala del foglio grande); 0 se 'owner' non e' una stanza.</summary>
        float RoomWidthPx(int owner)
        {
            if (owner < 0 || map == null || owner >= map.plan.rooms.Count) return 0f;
            var r = map.plan.rooms[owner];
            return (r.x1 - r.x0) * 0.1f * 6f;
        }

        /// <summary>Scrive 'text' nello spazio che ha: se non ci sta in una riga va a capo sullo spazio piu' vicino alla meta' ('wrap'), e se
        /// ancora non ci sta si rimpicciolisce fino a 'min'. 'charW' e' la larghezza media di una lettera, in parti del corpo. Restituisce quante
        /// righe in piu' della prima ha scritto.</summary>
        static int Fit(Text t, string text, float widthPx, int size, int min, float charW, bool wrap)
        {
            t.fontSize = size;
            if (widthPx <= 0f) { t.text = text; return 0; }
            float room = widthPx * 0.92f;
            int extra = 0;
            if (wrap && text.Length * charW * size > room)
            {
                int best = -1;
                for (int i = 1; i < text.Length - 1; i++)
                    if (text[i] == ' ' && (best < 0 || Mathf.Abs(i - text.Length / 2) < Mathf.Abs(best - text.Length / 2))) best = i;
                if (best > 0) { text = text.Substring(0, best) + "\n" + text.Substring(best + 1); extra = 1; }
            }
            int longest = 0, run = 0;
            foreach (char ch in text) { if (ch == '\n') run = 0; else run++; if (run > longest) longest = run; }
            t.fontSize = Mathf.Clamp(Mathf.FloorToInt(room / (Mathf.Max(1, longest) * charW)), min, size);
            t.text = text;
            return extra;
        }

        Slot NewSlot()
        {
            var rt = NewRt("Scritta", labelsRt);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(280f, 64f);
            var t = rt.gameObject.AddComponent<Text>();
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = false;
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = PaperHalo; o.effectDistance = new Vector2(1.3f, -1.3f); o.useGraphicAlpha = true;
            return new Slot { text = t };
        }

        // ------------------------------------------------------------------ ogni fotogramma
        /// <summary>Quanto di una stanza e' gia' comparso (0..1): quando la si vede la carta si inchiostra piano piano.</summary>
        float RoomAlpha(int owner)
        {
            if (owner < 0 || map == null) return 1f;
            if (map.StateOf(owner) == RoomSeen.Hidden) return 0f;
            return 0.30f + 0.70f * Ease((Time.unscaledTime - map.shownAt[owner]) / 1.2f);
        }

        void UpdateLabels(float e, float dt)
        {
            // le stanze affiorano
            for (int i = 0; i < roomGfx.Length; i++)
            {
                float a = RoomAlpha(i);
                var g = roomGfx[i];
                if (g.wash.enabled) g.wash.canvasRenderer.SetAlpha(a);
                if (g.ink.enabled) g.ink.canvasRenderer.SetAlpha(a);
                if (g.stain.enabled) g.stain.canvasRenderer.SetAlpha(a);
            }
            float inv = 1f / Mathf.Max(0.01f, scale);
            bool rescale = Mathf.Abs(scale - labelScale) > 0.002f;
            labelScale = scale;
            float roomAlpha = Ease((e - 0.55f) / 0.40f);
            foreach (var s in slots)
            {
                if (!s.text.gameObject.activeSelf) continue;
                if (rescale) s.text.rectTransform.localScale = new Vector3(inv, inv, 1f);
                float a = RoomAlpha(s.owner);
                switch (s.kind)
                {
                    case LabelKind.Price: case LabelKind.Question: break;
                    default: a *= roomAlpha; break;
                }
                s.text.canvasRenderer.SetAlpha(a);
                SetCull(s.text, a < 0.01f);
            }
        }
    }
}

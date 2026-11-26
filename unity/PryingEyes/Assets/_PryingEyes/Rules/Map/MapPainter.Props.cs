using System;
using System.Collections.Generic;

namespace PryingEyes.Proc.MapArt
{
    public sealed partial class MapPainter
    {
        const float PropW = 0.13f;

        void PaintProps(Room r, Pen ink)
        {
            var list = propsByRoom[r.id];
            if (list == null) return;
            foreach (var p in list) Prop(ink, p);
        }

        static V2 Loc(V2 c, float yaw, float u, float v) => c + V2.Polar(yaw, u) + V2.Polar(yaw + 1.5707963f, v);

        /// <summary>L'impronta di un arredo come l'avrebbe disegnata un geometra: il contorno a penna, un velo d'inchiostro dentro e il segno di
        /// che cos'e' (la chiave del disegno: panca, letto, tavolo, colonna...).</summary>
        void Prop(Pen ink, MapProp p)
        {
            V2 c = new V2(p.x, p.z);
            float w = Math.Max(p.w, 0.45f), d = Math.Max(p.d, 0.45f), yaw = p.yaw;
            var line = MapStyle.Ink.A(0.80f);
            var veil = MapStyle.Ink.A(0.11f);
            switch (p.cls)
            {
                case PropClass.Column:
                {
                    float rr = Math.Max(0.55f, Math.Min(w, d) * 0.5f);
                    ink.Disc(c, rr, MapStyle.Ink.A(0.42f));
                    ink.Ring(c, rr, PropW * 1.2f, line, 12, 0.3f);
                    ink.Line(c + new V2(-rr * 0.55f, 0f), c + new V2(rr * 0.55f, 0f), PropW * 0.8f, line, false);
                    ink.Line(c + new V2(0f, -rr * 0.55f), c + new V2(0f, rr * 0.55f), PropW * 0.8f, line, false);
                    return;
                }
                case PropClass.Statue:
                {
                    float rr = Math.Max(0.5f, Math.Min(w, d) * 0.5f);
                    ink.Ring(c, rr, PropW, line, 10, 0.3f);
                    ink.Disc(c, rr * 0.32f, line, 8);
                    return;
                }
                case PropClass.Tub:
                {
                    var box = Pen.OrientedRect(c, w, d, yaw);
                    ink.Fill(box, veil);
                    ink.Path(box, true, PropW, line, 0.3f);
                    ink.Ring(c, Math.Min(w, d) * 0.32f, PropW * 0.8f, line, 10, 0.2f);
                    return;
                }
            }
            if (p.cls == PropClass.Table && Math.Abs(w - d) < 0.3f * Math.Max(w, d))
            {
                float rr = Math.Max(w, d) * 0.5f;
                ink.Disc(c, rr, veil, 14);
                ink.Ring(c, rr, PropW, line, 14, 0.3f);
                return;
            }
            var rect = Pen.OrientedRect(c, w, d, yaw);
            ink.Fill(rect, veil);
            ink.Path(rect, true, PropW, line, 0.3f);
            float hw = w * 0.5f, hd = d * 0.5f;
            var mark = MapStyle.Ink.A(0.55f);
            switch (p.cls)
            {
                case PropClass.Pew:
                    ink.Line(Loc(c, yaw, -hw * 0.82f, hd * 0.30f), Loc(c, yaw, hw * 0.82f, hd * 0.30f), PropW * 0.8f, mark, false);
                    ink.Line(Loc(c, yaw, -hw * 0.82f, -hd * 0.30f), Loc(c, yaw, hw * 0.82f, -hd * 0.30f), PropW * 0.8f, mark, false);
                    break;
                case PropClass.Bed:
                    ink.Path(Pen.OrientedRect(Loc(c, yaw, 0f, hd * 0.62f), w * 0.78f, d * 0.22f, yaw), true, PropW * 0.8f, mark, 0.2f);
                    ink.Line(Loc(c, yaw, -hw, -hd * 0.1f), Loc(c, yaw, hw, -hd * 0.1f), PropW * 0.7f, mark, false);
                    break;
                case PropClass.Table:
                    ink.Line(Loc(c, yaw, -hw * 0.7f, 0f), Loc(c, yaw, hw * 0.7f, 0f), PropW * 0.7f, mark, false);
                    break;
                case PropClass.Shelf:
                    for (int i = -2; i <= 2; i++) ink.Line(Loc(c, yaw, i * hw * 0.38f, -hd * 0.8f), Loc(c, yaw, i * hw * 0.38f, hd * 0.8f), PropW * 0.7f, mark, false);
                    break;
                case PropClass.Sarcophagus:
                    ink.Line(Loc(c, yaw, -hw * 0.85f, 0f), Loc(c, yaw, hw * 0.85f, 0f), PropW * 0.8f, mark, false);
                    ink.Line(Loc(c, yaw, hw * 0.28f, -hd * 0.6f), Loc(c, yaw, hw * 0.28f, hd * 0.6f), PropW * 0.8f, mark, false);
                    break;
                case PropClass.Altar:
                    ink.Line(Loc(c, yaw, 0f, -hd * 0.7f), Loc(c, yaw, 0f, hd * 0.7f), PropW * 1.2f, line, false);
                    ink.Line(Loc(c, yaw, -hw * 0.30f, hd * 0.2f), Loc(c, yaw, hw * 0.30f, hd * 0.2f), PropW * 1.2f, line, false);
                    break;
                case PropClass.Cabinet:
                    ink.Line(rect[0], rect[2], PropW * 0.7f, mark, false);
                    ink.Line(rect[1], rect[3], PropW * 0.7f, mark, false);
                    break;
                case PropClass.Piano:
                    ink.Line(Loc(c, yaw, -hw, hd * 0.55f), Loc(c, yaw, hw, hd * 0.55f), PropW * 0.8f, mark, false);
                    ink.Line(Loc(c, yaw, hw * 0.3f, -hd), Loc(c, yaw, hw, -hd * 0.1f), PropW * 0.8f, mark, false);
                    break;
                case PropClass.Machine:
                    ink.Ring(c, Math.Min(w, d) * 0.28f, PropW * 0.8f, mark, 8, 0.2f);
                    break;
                case PropClass.Chair:
                    break;
                default:
                    ink.Line(rect[0], rect[2], PropW * 0.6f, mark.A(0.35f), false);
                    break;
            }
        }
    }
}

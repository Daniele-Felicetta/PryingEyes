using UnityEngine;

namespace PryingEyes.Proc
{
    public static partial class RoomKits
    {
        /// <summary>Gli annessi della sala (PlanAnnex.cs) vogliono il loro spazio libero: attorno all'asse di una torre o di una botola (la base
        /// del fusto e il cancello), e davanti alla porta di un giardino segreto, come davanti a una porta qualunque ma piu' largo.</summary>
        static void ReserveAnnexes(Ctx c)
        {
            foreach (var a in c.plan.annexes)
            {
                if (a.host != c.r.id || !a.HasShaft) continue;
                float R = PlanAnnexGen.ShaftOuter * 0.1f + (a.kind == AnnexKind.Torre ? 1.3f : 1.1f);
                c.occ.Add(new Rect(a.AxisX - R, a.AxisZ - R, 2f * R, 2f * R));
            }
            foreach (var o in c.plan.annexDoors)
            {
                if (o.a != c.r.id) continue;
                float w = o.width * 0.1f + 1.6f, depth = 2.8f;
                float line = o.line * 0.1f, ctr = o.center * 0.1f;
                bool low = o.alongX ? o.line == c.r.z0 : o.line == c.r.x0;          // il muro e' il lato S/W: l'interno e' verso le coordinate crescenti
                float inner = line + (low ? FloorBuilder.TH : -FloorBuilder.TH);
                float a0 = low ? inner : inner - depth;
                c.occ.Add(o.alongX ? new Rect(ctr - w * 0.5f, a0, w, depth) : new Rect(a0, ctr - w * 0.5f, depth, w));
            }
        }
    }
}

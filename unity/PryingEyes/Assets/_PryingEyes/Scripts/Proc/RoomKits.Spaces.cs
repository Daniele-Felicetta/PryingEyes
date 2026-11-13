using UnityEngine;

namespace PryingEyes.Proc
{
    public static partial class RoomKits
    {
        static void Rotonda(Ctx c)
        {
            // Monumento centrale basso e quattro isole di seduta: due fasce percorribili, dentro e fuori le sedute.
            Prop(c, "chi_fonte", c.Center, Vector3.forward, 1.3f, false);
            Prop(c, "chi_lampadario", c.Center, Vector3.forward, 1f, false, c.r.wallH - 1.8f);
            float lampHeight = c.r.wallH - 1.1f;
            var pool = AddLight(c, c.Center + Vector3.up * lampHeight, new Color(0.91f, 0.87f, 0.75f),
                45f * Mathf.Pow(lampHeight / 5.1f, 2f), Mathf.Max(c.W, c.D), false, true, null, LightType.Spot);
            pool.l.transform.rotation = Quaternion.Euler(90f, 0, 0);
            pool.l.spotAngle = 140f; pool.l.innerSpotAngle = 100f;
            float orbit = Mathf.Min(c.W, c.D) * 0.28f;
            for (int i = 0; i < 4; i++)
            {
                float angle = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Prop(c, c.r.church ? "chi_panca" : "arr_divano", c.Center + radial * orbit, -radial);
                bool east = i == 0 || i == 3, north = i < 2;
                Vector3 center = new Vector3(east ? c.r.x1 * 0.1f - c.r.cornerRadius : c.r.x0 * 0.1f + c.r.cornerRadius,
                    2.6f, north ? c.r.z1 * 0.1f - c.r.cornerRadius : c.r.z0 * 0.1f + c.r.cornerRadius);
                Vector3 wall = center + radial * (c.r.cornerRadius - FloorBuilder.TH - 0.03f);
                Sconce(c, wall, -radial, true);
            }
        }

        static void GiardinoInverno(Ctx c)
        {
            // Un salotto verde al posto di un'altra camera: gruppi di vasi e sedute, centro e vie d'accesso liberi.
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 position = c.Center + new Vector3(radial.x * c.W * 0.29f, 0, radial.z * c.D * 0.29f);
                Prop(c, "arr_palma", position, -radial, 1.4f);
                if (i % 2 == 0) Prop(c, "arr_poltrona", position - radial * 1.1f, -radial);
            }
            for (int side = -1; side <= 1; side += 2)
                FloorLampProp(c, c.Center + Vector3.right * (side * c.W * 0.27f), -Vector3.right * side, true);
        }
    }
}

#if !PE_DISTRIBUTION_BUILD
using System.Collections;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Deserto
{
    /// <summary>-desertonly motodettagli: il modello della moto (DesertBikeMesh.cs e DesertBikeMesh.Details.cs). Controlla che ogni pezzo ci sia (corpo, sterzo, le due ruote con il loro disco, l'ottone), che tutti i vertici
    /// siano numeri veri, che l'ingombro sia quello di una moto (lunga 2-2,4 m, larga meno di 1,1, alta tra 1 e 1,5) e che i disegni e i triangoli restino pochi (una moto per posto, fino a quattro). Con -desertshots le foto
    /// da fuori (sinistra, destra, tre quarti davanti e dietro, la ruota davanti, gli ammortizzatori) e dal posto del pilota.</summary>
    public sealed partial class DesertTest
    {
        /// <summary>Triangoli, disegni (renderer attivi), vertici non finiti e ingombro nel sistema della moto, di tutte le mesh sotto 'root'.</summary>
        static void BikeStats(Transform root, out int tris, out int renderers, out int badVerts, out Bounds local)
        {
            tris = 0; renderers = 0; badVerts = 0;
            bool first = true; local = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(false))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                renderers++;
                tris += m.triangles.Length / 3;
                foreach (var v in m.vertices)
                {
                    if (float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z)) { badVerts++; continue; }
                    Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(v));          // i vertici veri: il parallelepipedo di una mesh girata (lo sterzo) sovrastima l'ingombro
                    if (first) { local = new Bounds(p, Vector3.zero); first = false; } else local.Encapsulate(p);
                }
            }
        }

        IEnumerator MotoDetails()
        {
            var pl = Pl;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero; DesertBike.BotReset();
            DesertBike bike = null;
            yield return WaitFor(() => { bike = FirstBike(); return bike != null; }, 10f);
            Check("motodettagli: c'e' una moto parcheggiata", bike != null);
            if (bike == null) yield break;
            godMode = true; pl.invulnerable = true;
            Vector3 home = bike.transform.position;
            Transform body = bike.Body, steer = body != null ? body.Find("Sterzo") : null;

            // ---- i pezzi ci sono tutti
            string[] bodyParts = { "Telaio", "Motore", "Scarico", "Vernice", "Sella", "Ottone", "Targa dietro", "Fanalino", "Ruota dietro", "Sterzo" };
            string[] steerParts = { "Forcella", "Manubrio e faro", "Manopole", "Parafango", "Targa", "Ottone sterzo", "Vetri", "Lente", "Ruota davanti" };
            string missing = "";
            foreach (var n in bodyParts) if (body == null || body.Find(n) == null) missing += n + "; ";
            foreach (var n in steerParts) if (steer == null || steer.Find(n) == null) missing += "sterzo/" + n + "; ";
            Check("motodettagli: il corpo e lo sterzo hanno tutti i pezzi (telaio, motore, scarico, vernice, sella, ottone, targa, fanalino, forcella, manubrio, manopole, parafango, vetri, lente, ruote)", missing == "", "mancano: " + missing);
            var front = steer != null ? steer.Find("Ruota davanti") : null; var rear = body != null ? body.Find("Ruota dietro") : null;
            Mesh mf = front != null && front.childCount > 1 ? front.GetChild(1).GetComponent<MeshFilter>().sharedMesh : null, mr = rear != null && rear.childCount > 1 ? rear.GetChild(1).GetComponent<MeshFilter>().sharedMesh : null;
            Check("motodettagli: le due ruote hanno il loro disco: davanti il disco, dietro il disco e la corona (due mesh diverse, la posteriore con piu' triangoli)", mf != null && mr != null && mf != mr && mr.triangles.Length > mf.triangles.Length,
                mf == null || mr == null ? "ruote senza mesh" : $"davanti {mf.name} {mf.triangles.Length / 3}, dietro {mr.name} {mr.triangles.Length / 3} triangoli");
            var tyre = front != null && front.childCount > 0 ? front.GetChild(0).GetComponent<MeshFilter>().sharedMesh : null;
            Check("motodettagli: il pneumatico ha il tassellato (piu' di 1000 triangoli: il solo toro ne ha un centinaio)", tyre != null && tyre.triangles.Length / 3 > 1000, tyre == null ? "niente pneumatico" : $"{tyre.triangles.Length / 3} triangoli");

            // ---- numeri veri, ingombro e costo
            BikeStats(bike.transform, out int tris, out int rends, out int bad, out Bounds lb);
            Check("motodettagli: nessun vertice non finito in tutte le mesh della moto", bad == 0, $"{bad} vertici");
            Check("motodettagli: l'ingombro e' quello di una moto (lunga 2-2,4 m, larga meno di 1,1, alta tra 1 e 1,5)", lb.size.z > 2.0f && lb.size.z < 2.4f && lb.size.x < 1.1f && lb.size.y > 1.0f && lb.size.y < 1.5f,
                $"lunga {lb.size.z:F2}, larga {lb.size.x:F2}, alta {lb.size.y:F2} m");
            Check("motodettagli: i triangoli restano pochi (meno di 30000 per moto) e i disegni anche (meno di 28)", tris < 30000 && rends < 28, $"{tris} triangoli, {rends} disegni");
            Debug.Log($"[PryingEyes] desertotest motodettagli: una moto ha {tris} triangoli e {rends} disegni, ingombro {lb.size:F2}");

            if (shots)
            {
                Vector3 T(float x, float y, float z) => bike.transform.TransformPoint(new Vector3(x, y, z));
                Vector3 pos;
                // da fuori, con la scena di mezzogiorno meno dura (le 10 e mezza dei primi scatti)
                pos = T(-2.3f, 0f, 0f); yield return GoTo(pos.x, pos.z, 2f); yield return Snap("motodett_a_sinistra", 10.5f, T(0f, 0.55f, 0f), 1.0f);
                pos = T(2.3f, 0f, 0f); yield return GoTo(pos.x, pos.z, 2f); yield return Snap("motodett_b_destra", 10.5f, T(0f, 0.55f, 0f), 1.0f);
                pos = T(-1.5f, 0f, 2.0f); yield return GoTo(pos.x, pos.z, 2f); yield return Snap("motodett_c_tre_quarti_davanti", 10.5f, T(0f, 0.6f, 0.1f), 1.0f);
                pos = T(1.5f, 0f, -2.1f); yield return GoTo(pos.x, pos.z, 2f); yield return Snap("motodett_d_tre_quarti_dietro", 10.5f, T(0f, 0.6f, -0.1f), 1.0f);
                pos = T(1.15f, 0f, 1.25f); yield return GoTo(pos.x, pos.z, 2f); yield return Snap("motodett_e_ruota_davanti", 10.5f, T(0.05f, 0.45f, 0.62f), 1.0f);
                pos = T(1.2f, 0f, -1.35f); yield return GoTo(pos.x, pos.z, 2f); yield return Snap("motodett_f_ammortizzatori", 10.5f, T(0.1f, 0.55f, -0.55f), 1.0f);
                pos = T(-1.15f, 0f, -1.3f); yield return GoTo(pos.x, pos.z, 2f); yield return Snap("motodett_g_catena", 10.5f, T(-0.1f, 0.4f, -0.45f), 1.0f);

                // dal posto del pilota: il manubrio con gli specchietti, le leve e il serbatoio con i filetti
                pos = T(-2.0f, 0f, 0.1f); yield return GoTo(pos.x, pos.z, 2f);
                yield return new WaitForSeconds(0.4f);
                Face(bike.Point);
                yield return null; yield return null;
                Interact.Tick(pl.cam);
                Interact.TryUse();
                yield return null; yield return null;
                pl.yaw = pl.RideHeading; pl.pitch = 0.08f;
                yield return new WaitForSeconds(0.8f);
                Vector3 fwd = new Vector3(Mathf.Sin(pl.RideHeading), 0f, Mathf.Cos(pl.RideHeading));
                yield return Snap("motodett_h_cabina", 10.5f, bike.EyeWorld + fwd * 10f - Vector3.up * 1.2f, 0.6f);
                pl.RideRequestDismount();
                yield return WaitFor(() => !pl.Riding, 10f);
                yield return new WaitForSeconds(0.4f);
            }
            DesertBike.BotReset();
        }
    }
}
#endif

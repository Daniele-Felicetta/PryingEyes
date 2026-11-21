#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>-autotest -nofire -noenemies -impacttest [-impactshots]: gli impatti per superficie (Surfaces.cs, Fx.Impact). Le chiavi del
    /// kit finiscono nella famiglia giusta (e le parole trappola come "rosso" e "coro" no); il censimento del piano (raggi da ogni stanza)
    /// riconosce quasi tutto e trova piu' famiglie; per ognuna un impatto vero da' la famiglia, il suo foro e un suono; le schegge cadono e
    /// si fermano sul pavimento; la luce stimata e' piu' forte sotto una lampada che nel punto piu' buio; infine l'arma vera, sparando a un
    /// muro di ogni famiglia, passa dal nuovo impatto. Con -impactshots foto ravvicinate in unity_logs/impatti (in volo e dopo).</summary>
    public sealed class ImpactTest : MonoBehaviour
    {
        int checks, failed, exceptions;
        bool shots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-impacttest") >= 0) new GameObject("ImpactTest").AddComponent<ImpactTest>();
        }

        void Check(string name, bool ok) { checks++; if (!ok) failed++; Debug.Log("[PryingEyes] impacttest " + (ok ? "ok " : "FALLITO ") + name); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions++; }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            shots = Array.IndexOf(Environment.GetCommandLineArgs(), "-impactshots") >= 0;
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            Weapon.debugAds = false;
            PlayerController.holdLook = false;
            Check("nessuna eccezione runtime", exceptions == 0);
            Debug.Log($"[PryingEyes] impacttest RISULTATO {(failed == 0 ? "SUPERATA" : "FALLITA")}: {checks - failed}/{checks}");
            Application.Quit(failed == 0 ? 0 : 1);
        }

        static void Face(PlayerController player, Vector3 target)
        {
            Vector3 d = target - player.cam.position;
            player.yaw = Mathf.Atan2(d.x, d.z);
            player.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
        }

        void Photo(string name)
        {
            if (!shots) return;
            string folder = Path.GetFullPath("unity_logs/impatti"); Directory.CreateDirectory(folder);
            LightPool.Tick(false, Camera.main.transform.position);
            var weapon = GameManager.I.player.weapon;
            weapon.DebugHideView(true);                 // l'arma in primo piano coprirebbe proprio il punto colpito
            GameManager.CamShot(Path.Combine(folder, name + ".png"), null, false);
            weapon.DebugHideView(false);
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while ((FloorDirector.I == null || FloorDirector.I.cur == null || FloorDirector.I.transitioning) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Enemy.ClearAll();
            var gm = GameManager.I; var player = gm.player;
            PlayerController.holdLook = true; PlayerController.botDir = Vector3.zero;
            player.invulnerable = true;

            // le chiavi del kit e i nomi trappola
            var expect = new (string name, Surface s)[]
            {
                ("pav_parquet", Surface.Wood), ("pav_palco", Surface.Wood), ("mur_boiserie", Surface.Wood), ("sof_legno", Surface.Wood), ("cornice_legno", Surface.Wood),
                ("pav_marmo", Surface.Stone), ("pav_marmo_venato", Surface.Stone), ("pav_lastre", Surface.Stone), ("mur_pietra", Surface.Stone), ("colonna_pietra", Surface.Stone), ("sof_volta", Surface.Stone), ("pav_servizio", Surface.Stone),
                ("pav_piastrelle", Surface.Tile), ("mur_piastrelle", Surface.Tile),
                ("pav_moquette", Surface.Fabric), ("mur_velluto", Surface.Fabric), ("tenda_nera", Surface.Fabric), ("pav_linoleo", Surface.Fabric),
                ("mur_intonaco", Surface.Plaster), ("sof_intonaco", Surface.Plaster), ("mur_damasco_verde", Surface.Plaster), ("mur_vernice_verde", Surface.Plaster),
                ("ferro", Surface.Metal), ("ottone", Surface.Metal), ("acciaio", Surface.Metal), ("vetro_buio", Surface.Glass), ("vetrata_chiara", Surface.Glass),
                ("velluto_rosso", Surface.Fabric), ("coro_legno", Surface.Wood), ("Abate_Oro", Surface.Metal), ("VelvetR (Instance)", Surface.Fabric),
                ("pendolo_orologio", Surface.Plaster), ("DesertSand", Surface.Sand), ("Sabbia_3_-4", Surface.Sand),
            };
            var wrong = new List<string>();
            foreach (var (name, s) in expect) if (Surfaces.OfName(name, out _) != s) wrong.Add($"{name}={Surfaces.OfName(name, out _)}");
            Check($"chiavi del kit e nomi trappola nella famiglia giusta ({expect.Length - wrong.Count}/{expect.Length}{(wrong.Count > 0 ? ": " + string.Join(", ", wrong) : "")})", wrong.Count == 0);

            // censimento: raggi da ogni stanza
            var fi = FloorDirector.I.cur;
            var rnd = new System.Random(7);
            var tally = new int[8];
            var unknown = new Dictionary<string, int>();
            var sample = new Dictionary<Surface, List<RaycastHit>>();
            var names = new Dictionary<Surface, Dictionary<string, int>>();
            int total = 0, known = 0;
            foreach (var room in fi.plan.rooms)
            {
                if (room.IsLift) continue;
                Vector3 c = new Vector3((room.x0 + room.x1) * 0.05f, 1.4f, (room.z0 + room.z1) * 0.05f);
                for (int k = 0; k < 32; k++)
                {
                    float az = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float el = k < 24 ? ((float)rnd.NextDouble() - 0.5f) * 1.0f : k < 28 ? 1.2f : -1.2f;
                    Vector3 d = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
                    if (!Physics.Raycast(c, d, out var h, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    if (h.collider.GetComponentInParent<Enemy>() != null || h.collider.GetComponentInParent<PlayerController>() != null) continue;
                    var s = Surfaces.Describe(h, out string label, out bool ok);
                    total++; tally[(int)s]++;
                    if (ok) known++;
                    else { unknown.TryGetValue(label, out int u); unknown[label] = u + 1; }
                    if (!names.TryGetValue(s, out var nm)) names[s] = nm = new Dictionary<string, int>();
                    string full = label + " @" + h.collider.name;
                    nm.TryGetValue(full, out int cnt); nm[full] = cnt + 1;
                    if (!sample.TryGetValue(s, out var list)) sample[s] = list = new List<RaycastHit>();
                    if (list.Count < 40) list.Add(h);
                }
            }
            var parts = new List<string>();
            for (int i = 0; i < 7; i++) if (tally[i] > 0) parts.Add($"{(Surface)i} {100f * tally[i] / Mathf.Max(1, total):F0}%");
            var unk = new List<KeyValuePair<string, int>>(unknown);
            unk.Sort((a, b) => b.Value.CompareTo(a.Value));
            var unkText = new List<string>();
            for (int i = 0; i < Mathf.Min(12, unk.Count); i++) unkText.Add($"{unk[i].Key} x{unk[i].Value}");
            Debug.Log($"[PryingEyes] impacttest censimento: {total} colpi, {string.Join(", ", parts)}; senza regola {total - known}: {string.Join(" | ", unkText)}");
            foreach (var kv in names)
            {
                var top = new List<KeyValuePair<string, int>>(kv.Value);
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                var t = new List<string>();
                for (int i = 0; i < Mathf.Min(6, top.Count); i++) t.Add($"{top[i].Key} x{top[i].Value}");
                Debug.Log($"[PryingEyes] impacttest   {kv.Key}: {string.Join(" | ", t)}");
            }
            Check($"censimento: {total} colpi su {sample.Count} famiglie, {100f * known / Mathf.Max(1, total):F0}% riconosciuti", total > 100 && sample.Count >= 4 && known >= total * 0.85f);

            // un impatto vero per famiglia: famiglia, foro e suono giusti
            int bad = 0; var log = new List<string>();
            Vector3 camPos = Camera.main.transform.position;
            foreach (var kv in sample)
            {
                // il punto della famiglia piu' vicino a chi gioca: il suono parte solo entro 45 m
                var h = kv.Value[0];
                foreach (var c in kv.Value) if ((c.point - camPos).sqrMagnitude < (h.point - camPos).sqrMagnitude) h = c;
                bool near = (h.point - camPos).sqrMagnitude < 45f * 45f;
                int sounds = Fx.ImpactSounds, count = Fx.ImpactCount;
                var got = Fx.Impact(h, 1f, -h.normal);
                bool fam = got == kv.Key && Fx.LastSurface == kv.Key, hole = Fx.LastHoleTexture == Fx.HoleTex(kv.Key);
                bool snd = Fx.ImpactSounds == sounds + (near ? 1 : 0), cnt = Fx.ImpactCount == count + 1;
                if (!(fam && hole && snd && cnt)) bad++;
                log.Add($"{kv.Key}{(fam ? "" : " famiglia!")}{(hole ? "" : " foro!")}{(snd ? "" : " suono!")}{(cnt ? "" : " conto!")}{(near ? "" : " (lontano, muto)")}");
                yield return new WaitForSeconds(0.08f);
            }
            Check($"un impatto per famiglia con il suo foro e il suo suono ({string.Join(", ", log)})", bad == 0);

            // le schegge cadono e si fermano sul pavimento
            int flying = Fx.Count(5);
            yield return new WaitForSeconds(1.0f);
            int resting = Fx.Count(5, true);
            Check($"le schegge cadono e si fermano sul pavimento ({flying} in volo, {resting} ferme dopo 1 s, la piu' alta a {Fx.RestingChipLift() * 1000f:F0} mm)",
                  flying > 0 && resting > 0 && Fx.RestingChipLift() < 0.02f);

            // la luce: sotto una lampada piu' che nel punto piu' buio del censimento
            PoolLight lamp = null;
            foreach (var pl in LightPool.All) if (pl?.l != null && pl.l.isActiveAndEnabled && pl.l.type == LightType.Point && (lamp == null || pl.l.intensity > lamp.l.intensity)) lamp = pl;
            float bright = lamp != null ? Surfaces.LightAt(lamp.l.transform.position + Vector3.down * 0.6f) : 0f;
            float dark = float.MaxValue;
            foreach (var kv in sample) foreach (var h in kv.Value) dark = Mathf.Min(dark, Surfaces.LightAt(h.point + h.normal * 0.05f));
            Check($"la luce stimata: {bright:F2} sotto la lampada piu' forte, {dark:F3} nel punto piu' buio", lamp != null && bright > dark * 3f && bright > 0.2f);

            // il catalogo: i sette fori affiancati sul muro piu' illuminato del censimento, visti da 80 cm (solo con le foto)
            if (shots)
            {
                RaycastHit best = default; float bestLight = -1f; Vector3 bestStand = Vector3.zero;
                foreach (var kv in sample)
                    foreach (var h in kv.Value)
                    {
                        if (Mathf.Abs(h.normal.y) > 0.2f || h.point.y < 0.9f || h.point.y > 2.2f) continue;
                        float lt = Surfaces.LightAt(h.point + h.normal * 0.05f);
                        if (lt <= bestLight) continue;
                        Vector3 tan = Vector3.Cross(h.normal, Vector3.up).normalized;
                        bool flat = true;
                        foreach (float o in new[] { -0.6f, 0.6f })
                            if (!Physics.Raycast(h.point + h.normal * 0.1f + tan * o, -h.normal, out var wh, 0.2f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) || Vector3.Dot(wh.normal, h.normal) < 0.95f) flat = false;
                        Vector3 e = h.point + h.normal * 0.8f;
                        if (!flat || !HotelNav.Ground(new Vector3(e.x, h.point.y + 1f, e.z), out var g)) continue;
                        if (Physics.CheckCapsule(g + Vector3.up * 0.45f, g + Vector3.up * 1.4f, 0.3f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                        best = h; bestLight = lt; bestStand = g;
                    }
                if (bestLight > 0f)
                {
                    player.SetSpawn(bestStand);
                    yield return null;
                    Vector3 tan = Vector3.Cross(best.normal, Vector3.up).normalized;
                    Vector3 mid = new Vector3(best.point.x, player.cam.position.y - 0.05f, best.point.z);
                    for (int i = 0; i < 7; i++)
                    {
                        // il punto vero del muro (non il piano stimato: un muro che piega di un centimetro nasconderebbe il foro)
                        Vector3 q = mid + tan * ((i - 3) * 0.17f);
                        if (Physics.Raycast(q + best.normal * 0.3f, -best.normal, out var wq, 0.6f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore))
                            Fx.Impact(wq.point, wq.normal, 1f, (Surface)i, -wq.normal);
                        yield return new WaitForSeconds(0.06f);
                    }
                    Face(player, mid);
                    yield return null; yield return null;
                    Photo("catalogo_volo");
                    yield return new WaitForSeconds(1.4f);
                    Photo("catalogo");
                    Debug.Log($"[PryingEyes] impacttest catalogo: da sinistra intonaco, pietra, piastrelle, legno, metallo, stoffa, vetro (luce {bestLight:F2})");
                }
            }

            // l'arma vera contro un muro di ogni famiglia (con le foto ravvicinate)
            Weapon.debugAds = true;
            var weapon = player.weapon;
            int fired = 0, matched = 0; var shotLog = new List<string>();
            foreach (var kv in sample)
            {
                RaycastHit wall = default; Vector3 stand = Vector3.zero; bool found = false;
                // prima i punti piu' illuminati (la foto di un muro al buio non dice nulla)
                var cands = new List<RaycastHit>(kv.Value);
                cands.Sort((a, b) => Surfaces.LightAt(b.point + b.normal * 0.05f).CompareTo(Surfaces.LightAt(a.point + a.normal * 0.05f)));
                foreach (var h in cands)
                {
                    // muri (di fronte, da 1,25 m) o pavimenti (da un metro di lato); mai soffitti
                    bool floor = h.normal.y > 0.5f;
                    if (h.normal.y < -0.5f || (floor && kv.Key != Surface.Fabric && kv.Key != Surface.Tile && kv.Key != Surface.Stone && kv.Key != Surface.Wood)) continue;
                    Vector3 eye = h.point + h.normal * 0.85f;
                    Vector3 q = floor ? h.point + new Vector3(0.55f, 0f, 0.25f) : new Vector3(eye.x, h.point.y, eye.z);
                    if (!HotelNav.Ground(q + Vector3.up * 1.0f, out var g)) continue;
                    if (!HotelNav.ClearLine(g + Vector3.up * 1.55f, h.point + h.normal * 0.05f)) continue;
                    if (Physics.CheckCapsule(g + Vector3.up * 0.45f, g + Vector3.up * 1.4f, 0.3f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                    wall = h; stand = g; found = true; break;
                }
                if (!found) { shotLog.Add(kv.Key + " (nessun punto)"); continue; }
                player.SetSpawn(stand);
                yield return null;
                Face(player, wall.point);
                yield return null; yield return null;
                if (weapon.mag <= 0) weapon.RespawnAmmo();
                // tre colpi vicini; per ognuno la superficie attesa e' quella sul raggio esatto della mira, e tra un colpo e l'altro
                // passano 0,45 s: in mira e fermi il primo colpo dopo una pausa e' preciso (Weapon.FireRays)
                RaycastHit aim = default; bool aimed = false; var expected = kv.Key;
                int before = Fx.ImpactCount;
                for (int k = 0; k < 3; k++)
                {
                    Face(player, wall.point + Vector3.Cross(wall.normal, Vector3.up).normalized * ((k - 1) * 0.07f) + Vector3.up * (k == 1 ? 0.05f : 0f));
                    yield return null;
                    aimed = Physics.Raycast(player.cam.position, player.cam.forward, out aim, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    expected = aimed ? Surfaces.Of(aim) : kv.Key;
                    weapon.DebugFire();
                    yield return new WaitForSeconds(0.45f);
                }
                fired++;
                bool ok = Fx.ImpactCount > before && Fx.LastSurface == expected;
                if (ok) matched++;
                string what = aimed ? Surfaces.Describe(aim, out string lab, out _) + " " + lab + " @" + aim.collider.name : "-";
                shotLog.Add($"{kv.Key}->{Fx.LastSurface}{(ok ? "" : "!")} [{what}]");
                Photo(kv.Key + "_volo");
                yield return new WaitForSeconds(1.3f);
                Photo(kv.Key + "_dopo");
            }
            Check($"l'arma vera passa dall'impatto nuovo ({matched}/{fired}: {string.Join(", ", shotLog)})", fired >= 3 && matched == fired);
            player.invulnerable = false;
        }
    }
}
#endif

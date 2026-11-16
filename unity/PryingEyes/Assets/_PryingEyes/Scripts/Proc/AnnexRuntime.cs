using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes.Proc
{
    /// <summary>Gli annessi in gioco (la geometria e' FloorBuilder.Annex.cs / .Garden.cs, il piano e' PlanAnnex.cs). Ogni annesso ha un
    /// cancello che lo chiude (parete finta o grata alla base della torre, sigillo di pietra sulla botola della cripta, porta coperta
    /// d'edera del giardino) e si apre in uno di tre modi: tirando tutte le leve dei sigilli sparse nella sala madre, in basso e sulle
    /// balconate, entro un tempo limite (Sigilli); offrendo molti punti (Tributo); con la chiave che custodisce un altro annesso del
    /// piano (Chiave). Dentro c'e' uno Scrigno antico, gratis e una volta sola, con un'arma di grado alto. In cooperativa cancelli e leve
    /// sono di tutti (dal 2026-11-16: CoopGame.Magic.cs, l'ospite fa da arbitro e racconta a chi entra tardi gli annessi aperti); la
    /// chiave e lo Scrigno restano di ognuno (come lo Scrigno del negozio).</summary>
    public static class AnnexRuntime
    {
        static readonly HashSet<int> keys = new HashSet<int>();

        /// <summary>Il giocatore ha la chiave di questo annesso (di un giardino).</summary>
        public static bool HasKeyFor(int annexId) => keys.Contains(annexId);
        public static void GiveKey(int annexId) { keys.Add(annexId); }
        public static void Clear() { keys.Clear(); }

        /// <summary>Dopo la costruzione del piano e prima della navmesh: i cancelli diventano usabili, i tesori, le chiavi e le leve si mettono al loro posto.</summary>
        public static void Place(FloorInstance fi)
        {
            Clear();
            Physics.SyncTransforms();           // le query di FindSpot devono vedere i collider appena costruiti
            foreach (var s in fi.annexSites) s.Place();
            Physics.SyncTransforms();
        }

        /// <summary>Un punto dove non deve nascere nessun nemico: dentro un annesso (il cilindro del fusto di una torre o di una cripta a ogni
        /// quota, la camera della cripta sotto il pavimento, il giardino). Sono zone sigillate fino all'apertura: chi nascesse li' resterebbe
        /// irraggiungibile e l'ondata non finirebbe mai.</summary>
        public static bool Forbidden(FloorInstance fi, Vector3 p)
        {
            if (fi == null) return false;
            foreach (var s in fi.annexSites)
            {
                var a = s.data;
                if (a.HasShaft)
                {
                    float R = PlanAnnexGen.ShaftOuter * 0.1f + 0.4f, dx = p.x - a.AxisX, dz = p.z - a.AxisZ;
                    if (dx * dx + dz * dz < R * R) return true;
                }
                bool inRect = p.x >= a.x0 * 0.1f && p.x <= a.x1 * 0.1f && p.z >= a.z0 * 0.1f && p.z <= a.z1 * 0.1f;
                if (a.kind == AnnexKind.Cripta && inRect && p.y < -1.2f) return true;
                if (a.kind == AnnexKind.Giardino && inRect) return true;
                if (a.kind == AnnexKind.Sacrario && a.OutsideContains(p.x, p.z)) return true;
            }
            return false;
        }

        /// <summary>Dopo la navmesh: una riga di log per annesso con la raggiungibilita' a piedi dall'arrivo (ingresso e interno).</summary>
        public static void Arm(FloorInstance fi)
        {
            if (fi.annexSites.Count == 0) return;
            var lift = fi.arrival;
            if (lift == null || !NavMesh.SamplePosition(lift.frontPoint, out NavMeshHit fh, 2.5f, NavMesh.AllAreas)) return;
            var path = new NavMeshPath();
            bool Reach(Vector3 p, out string why)
            {
                why = "";
                if (!NavMesh.SamplePosition(p, out NavMeshHit th, 1.2f, NavMesh.AllAreas)) { why = "fuori navmesh"; return false; }
                if (!NavMesh.CalculatePath(fh.position, th.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) { why = "nessun percorso"; return false; }
                return true;
            }
            foreach (var s in fi.annexSites)
            {
                if (s.data.kind == AnnexKind.Sacrario)
                {
                    // il sacrario si raggiunge solo a carponi (cunicolo di 1,3 m): la navmesh non ci arriva, ed e' voluto (i nemici non entrano)
                    bool okS = Reach(s.outside, out string ws);
                    string ls = $"[PryingEyes] ANNESSO {s.data} libro={(s.data.book == 0 ? "Stregoneria" : "Arsenale")}: parete {(okS ? "ok" : ws)}, interno a carponi";
                    if (okS) Debug.Log(ls); else Debug.LogWarning(ls);
                    continue;
                }
                bool okOut = Reach(s.outside, out string w1), okIn = Reach(s.inside, out string w2), okRw = Reach(s.rewardPos, out string w3);
                string line = $"[PryingEyes] ANNESSO {s.data} cancello={s.data.gate}: ingresso {(okOut ? "ok" : w1)}, interno {(okIn ? "ok" : w2)}, tesoro {(okRw ? "ok" : w3)}";
                if (okOut && okIn && okRw) Debug.Log(line); else Debug.LogWarning(line);
            }
        }
    }

    public sealed class AnnexSite : MonoBehaviour
    {
        // ---- dati messi dal costruttore (FloorBuilder.Annex.cs / .Garden.cs)
        public Annex data;
        public FloorInstance fi;
        public Room host;
        public Transform props, closer;
        public Collider closerCol;
        public Material sigilMat;
        public Vector3 gatePoint, outside, inside, rewardPos, keyPos, altarPos;
        /// <summary>Sacrario: verso l'esterno (dalla sala al cunicolo) e lungo il muro, per far rientrare e scorrere la parete finta.</summary>
        public Vector3 secretOut, secretAlong;
        /// <summary>Il grimorio sull'altare (solo il sacrario).</summary>
        public GrimoireAltar altar;
        public float gateRadius = 3f, rewardYaw, altarYaw, cellFloorY;
        /// <summary>Cose che scompaiono con l'apertura oltre al cancello (l'edera del giardino): figli del cancello, affondano con lui.</summary>
        public readonly List<Transform> fade = new List<Transform>();

        // ---- stato
        /// <summary>Vero mentre si applica un cambio arrivato dalla rete (ApplyRemote): un ospitato non lo rimanda indietro.</summary>
        public static bool Remote;
        public bool opened;
        public int pulled;
        public Scrigno chest;
        readonly List<AnnexLever> levers = new List<AnnexLever>();
        AnnexGate gate;
        float firstPull = -1f;
        const float OpenDur = 1.7f;

        /// <summary>Quanti secondi restano per tirare le altre leve (da quando si tira la prima).</summary>
        public float ResetAfter => data.levers >= 3 ? 130f : 160f;
        public float Remaining => firstPull < 0f ? ResetAfter : Mathf.Max(0f, ResetAfter - (Time.time - firstPull));

        string What => data.kind == AnnexKind.Torre ? (data.secret ? "PARETE CAVA" : "GRATA SIGILLATA") : data.kind == AnnexKind.Cripta ? "BOTOLA SIGILLATA"
            : data.kind == AnnexKind.Sacrario ? "UN OCCHIO SCOLPITO NEL MURO" : "EDERA FITTA";

        // ------------------------------------------------------------------ messa in opera
        public void Place()
        {
            ShrineKit.Mats();
            var rng = new Rng(data.seed);
            if (closer != null)
            {
                gate = closer.gameObject.AddComponent<AnnexGate>();
                gate.site = this;
                Interact.Add(gate);
            }
            if (data.kind == AnnexKind.Sacrario)
            {
                // il sacrario custodisce il grimorio, non uno scrigno; si apre trovando la parete (nessuna leva, nessun tributo)
                altar = GrimoireAltar.Place(this, (Grimoire.Book)data.book);
                return;
            }
            int grade = data.kind == AnnexKind.Torre ? 4 : 3;
            chest = Scrigno.PlaceAt(fi, rewardPos, rewardYaw, 0, grade, true);
            var garden = fi.plan.annexes.Find(x => x.kind == AnnexKind.Giardino && x.keyHolder == data.id);
            if (garden != null) AnnexKey.Build(this, garden.id);
            if (data.gate == GateKind.Sigilli) PlaceLevers(rng);
            if (data.gate == GateKind.Tributo && altarPos != Vector3.zero) AnnexAltar.Build(this);
        }

        string Target => data.kind == AnnexKind.Torre ? "la torre" : data.kind == AnnexKind.Cripta ? "la cripta" : data.kind == AnnexKind.Sacrario ? "il sacrario" : "il giardino";

        /// <summary>Il testo sull'altare del tributo (e sul cancello quando e' un tributo).</summary>
        public string TributePrompt(string head)
        {
            if (opened) return null;
            var gm = GameManager.I;
            int have = gm != null ? gm.score : 0;
            return have >= data.price ? $"{head} — offri {data.price} punti: si aprirà {Target}" : $"{head} — tributo {data.price} punti (ti mancano {data.price - have})";
        }

        void PlaceLevers(Rng rng)
        {
            var taken = new List<Vector3>();
            int n = data.levers;
            for (int i = 0; i < n; i++)
            {
                Vector3 pos; float yaw;
                bool ok = false;
                pos = default; yaw = 0f;
                // la prima in basso, le altre sulle balconate (la piu' alta per seconda), poi ripiego in basso
                if (i >= 1) ok = GalleryLeverSpot(i == 1 ? 2 : 1, rng, taken, out pos, out yaw) || GalleryLeverSpot(0, rng, taken, out pos, out yaw);
                if (!ok && ShrineKit.FindSpot(fi.plan, host, rng, new Vector2(0.45f, 0.45f), 0.7f, taken, 6f, out pos))
                {
                    Vector3 to = new Vector3(host.CenterX - pos.x, 0f, host.CenterZ - pos.z);
                    yaw = to.sqrMagnitude < 0.01f ? 0f : Mathf.Round(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg / 90f) * 90f;
                    ok = true;
                }
                if (!ok) { Debug.LogWarning($"[PryingEyes] annesso {data}: leva {i + 1} senza posto"); continue; }
                taken.Add(pos);
                levers.Add(AnnexLever.Build(this, i, pos, yaw));
            }
            // meno leve di quante servivano: l'annesso resta apribile con quelle che ci sono
            if (levers.Count < data.levers) Debug.LogWarning($"[PryingEyes] annesso {data}: {levers.Count}/{data.levers} leve");
        }

        /// <summary>Un punto su una balconata della sala madre: 'rank' 2 la quota piu' alta, 1 la piu' bassa tra le balconate, 0 qualunque.
        /// Il punto sta vicino a un punto del percorso, spostato verso il parapetto, su pavimento libero.</summary>
        bool GalleryLeverSpot(int rank, Rng rng, List<Vector3> taken, out Vector3 pos, out float yaw)
        {
            pos = default; yaw = 0f;
            float maxY = 0f, minY = float.MaxValue;
            foreach (var g in fi.galleries)
                if (g.room == host) foreach (var p in g.points) { if (p.y > 3f) { maxY = Mathf.Max(maxY, p.y); minY = Mathf.Min(minY, p.y); } }
            if (maxY <= 3f) return false;
            float want = rank == 2 ? maxY : rank == 1 ? minY : -1f;
            var cands = new List<Vector3>();
            foreach (var g in fi.galleries)
            {
                if (g.room != host) continue;
                foreach (var p in g.points)
                    if (p.y > 3f && (want < 0f || Mathf.Abs(p.y - want) < 0.3f)) cands.Add(p);
            }
            for (int i = cands.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); var t = cands[i]; cands[i] = cands[j]; cands[j] = t; }
            foreach (var c in cands)
            {
                for (int k = 1; k < 9; k++)
                {
                    Vector3 off = new Vector3(Mathf.Sin(k * 0.785f), 0f, Mathf.Cos(k * 0.785f)) * 0.95f;
                    Vector3 p = c + off;
                    // Leave the walking route clear of the pedestal and its navmesh clearance.
                    bool onRoute = false;
                    foreach (var g in fi.galleries)
                        if (g.room == host)
                            for (int j = 1; j < g.points.Length; j++)
                            {
                                Vector3 a = g.points[j - 1], d = g.points[j] - a;
                                float t = Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(d.sqrMagnitude, 0.001f));
                                if ((a + d * t - p).sqrMagnitude < 0.9f * 0.9f) { onRoute = true; break; }
                            }
                    if (onRoute) continue;
                    if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit fh, 2.2f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(fh.point.y - c.y) > 0.25f) continue;
                    if (Physics.CheckBox(fh.point + Vector3.up * 1.0f, new Vector3(0.4f, 0.8f, 0.4f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    bool bad = false;
                    foreach (var q in taken) if ((q - fh.point).sqrMagnitude < 25f) { bad = true; break; }
                    if (bad) continue;
                    // niente sul bordo della scala: a 1,4 m da una porta o dal fondo di una scala (i punti con y vicino a 0)
                    pos = fh.point;
                    Vector3 to = new Vector3(host.CenterX - pos.x, 0f, host.CenterZ - pos.z);
                    yaw = to.sqrMagnitude < 0.01f ? 0f : Mathf.Round(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg / 90f) * 90f;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ il cancello
        public string GatePrompt()
        {
            if (opened) return null;
            var gm = GameManager.I;
            switch (data.gate)
            {
                case GateKind.Sigilli:
                    if (pulled > 0) return $"{What} — sigilli {pulled}/{Mathf.Max(1, levers.Count)}, restano {Mathf.CeilToInt(Remaining)} s";
                    return $"{What} — la tengono chiusa {Mathf.Max(1, levers.Count)} sigilli, in questa sala";
                case GateKind.Tributo:
                    return TributePrompt(What);
                case GateKind.Segreto:
                    return $"{What} — sussurra il tuo nome: premi l'occhio";
                default:
                    return AnnexRuntime.HasKeyFor(data.id) ? $"{What} — apri con la chiave" : $"{What} — serve una chiave, custodita altrove";
            }
        }

        public void GateUse()
        {
            if (opened) return;
            var gm = GameManager.I;
            if (gm == null) return;
            switch (data.gate)
            {
                case GateKind.Sigilli:
                    gm.hud?.Toast("Tira i sigilli sparsi per la sala: tutti, prima che si richiudano.");
                    Sfx.UI(SoundBank.BookNo, 0.4f, 1.3f);
                    break;
                case GateKind.Tributo:
                    if (gm.score < data.price) { gm.hud?.Toast($"Servono {data.price} punti."); Sfx.UI(SoundBank.BookNo, 0.6f); return; }
                    gm.AddPoints(-data.price);
                    Sfx.UI(SoundBank.PickCoins, 0.7f);
                    Open();
                    break;
                case GateKind.Segreto:
                    Open();
                    break;
                default:
                    if (!AnnexRuntime.HasKeyFor(data.id)) { gm.hud?.Toast("La porta ha una serratura: serve una chiave."); Sfx.UI(SoundBank.BookNo, 0.6f); return; }
                    Open();
                    break;
            }
        }

        /// <summary>Una leva e' stata tirata: se sono tutte, si apre.</summary>
        public void LeverPulled(AnnexLever l)
        {
            pulled++;
            if (firstPull < 0f) firstPull = Time.time;
            var gm = GameManager.I;
            int total = Mathf.Max(1, levers.Count);
            if (pulled >= total) { gm?.hud?.Toast("Tutti i sigilli sono sciolti."); Open(); }
            else gm?.hud?.Toast($"Sigillo {pulled}/{total} — hai {Mathf.CeilToInt(Remaining)} secondi per gli altri.");
        }

        /// <summary>In cooperativa: un annesso aperto (what 255) o la leva 'what' tirata altrove; 'instant' = per chi entra tardi, senza
        /// animazione. Vero se ha cambiato qualcosa.</summary>
        public bool ApplyRemote(int what, bool instant)
        {
            if (opened) return false;
            Remote = true;
            try
            {
                if (what == 255) { if (instant) OpenInstant(); else Open(); return true; }
                return what >= 0 && what < levers.Count && levers[what].PullRemote();
            }
            finally { Remote = false; }
        }

        /// <summary>Per le prove e le foto: l'annesso aperto subito, senza animazione ne' suoni.</summary>
        public void OpenInstant()
        {
            opened = true;
            if (closerCol != null) closerCol.enabled = false;
            if (closer != null) closer.gameObject.SetActive(false);
        }

        public void Open()
        {
            if (opened) return;
            opened = true;
            if (closerCol != null) closerCol.enabled = false;
            var gm = GameManager.I;
            string msg = data.kind == AnnexKind.Torre ? "Qualcosa si muove alla base della torre." : data.kind == AnnexKind.Cripta ? "Il sigillo della botola si dissolve."
                : data.kind == AnnexKind.Sacrario ? "Il muro rientra su un cunicolo basso: chinati (C) e segui la luce rossa." : "L'edera si ritira: c'è una porta.";
            gm?.hud?.Toast(msg);
            if (data.kind == AnnexKind.Sacrario)
            {
                Sfx.At(SoundBank.SegretaApre, gatePoint, 0.9f, 1f, 3f, 40f);
                foreach (var src in closer != null ? closer.GetComponents<AudioSource>() : new AudioSource[0]) src.Stop();
            }
            else
            {
                Sfx.At(SoundBank.Clang, gatePoint, 0.9f, 0.7f, 3f, 55f);
                Sfx.At(SoundBank.Creak, gatePoint, 0.8f, 0.6f, 3f, 40f);
            }
            if (closer != null) StartCoroutine(data.kind == AnnexKind.Sacrario ? SlideAnim() : OpenAnim());
            // in cooperativa l'apertura e' di tutti: l'ospite la racconta sempre (anche quella chiesta da un ospitato), un ospitato solo la sua
            if (!Remote || Coop.IsHost) Coop.Game?.AnnexChanged(this, 255);
        }

        IEnumerator OpenAnim()
        {
            Vector3 p0 = closer.position, s0 = closer.localScale;
            bool shrink = data.kind == AnnexKind.Cripta;
            for (float t = 0f; t < OpenDur; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / OpenDur);
                if (shrink) { closer.localScale = s0 * Mathf.Max(0.001f, 1f - k); closer.position = p0 + Vector3.down * (0.3f * k); }
                else closer.position = p0 + Vector3.down * (2.6f * k);
                if (sigilMat != null) sigilMat.SetColor("_EmissionColor", FloorBuilder.GateColor(data.gate) * Mathf.Lerp(2.6f, 0f, k));
                yield return null;
            }
            closer.gameObject.SetActive(false);
        }

        /// <summary>La parete del sacrario: rientra di 30 cm (si stacca dal muro con un tonfo di polvere), poi scorre di lato dentro il muro.</summary>
        IEnumerator SlideAnim()
        {
            Vector3 p0 = closer.position;
            const float push = 0.55f, slide = 1.25f;
            for (float t = 0f; t < push; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / push);
                closer.position = p0 + secretOut * (0.30f * k) + Random.insideUnitSphere * 0.003f * (1f - k);
                if (sigilMat != null) sigilMat.SetColor("_EmissionColor", FloorBuilder.GateColor(data.gate) * Mathf.Lerp(1.4f, 3.2f, k));
                yield return null;
            }
            for (int i = 0; i < 14; i++)
                Fx.Particle(0, false, gatePoint + secretAlong * Random.Range(-0.6f, 0.6f) + Vector3.down * Random.Range(0.1f, 0.7f), -secretOut * 0.4f + Random.insideUnitSphere * 0.2f, Vector3.down * 0.3f,
                            0.08f, 0.4f, 1.4f, new Color(0.45f, 0.42f, 0.38f), 0.35f);
            Vector3 p1 = closer.position;
            for (float t = 0f; t < slide; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / slide);
                closer.position = p1 + secretAlong * (1.22f * k);
                if (sigilMat != null) sigilMat.SetColor("_EmissionColor", FloorBuilder.GateColor(data.gate) * Mathf.Lerp(3.2f, 0.4f, k));
                yield return null;
            }
            closer.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ ogni fotogramma
        void Update()
        {
            if (opened) return;
            // il sigillo respira, piu' forte quando e' piu' vicino alla sua apertura
            if (sigilMat != null)
            {
                float k = data.gate == GateKind.Sigilli && levers.Count > 0 ? 0.8f + 0.9f * pulled / levers.Count : 1.1f;
                sigilMat.SetColor("_EmissionColor", FloorBuilder.GateColor(data.gate) * (k * (0.75f + 0.25f * Mathf.Sin(Time.time * 2.2f + data.id))));
            }
            // il tempo e' scaduto: le leve tornano su
            if (data.gate == GateKind.Sigilli && pulled > 0 && firstPull >= 0f && Time.time - firstPull > ResetAfter)
            {
                pulled = 0; firstPull = -1f;
                foreach (var l in levers) l.Reset();
                GameManager.I?.hud?.Toast("I sigilli si sono richiusi.");
                Sfx.UI(SoundBank.BookNo, 0.6f, 0.8f);
            }
        }

        void OnDestroy()
        {
            if (gate != null) Interact.Remove(gate);
            foreach (var l in levers) if (l != null) Interact.Remove(l);
            if (sigilMat != null) Destroy(sigilMat);
        }
    }

    /// <summary>Il cancello come cosa da usare (F): sta sull'oggetto che chiude l'annesso, cosi' i raggi che lo mirano lo riconoscono.</summary>
    public sealed class AnnexGate : MonoBehaviour, IInteract
    {
        public AnnexSite site;
        public Vector3 Point => site.gatePoint;
        public float Radius => site.gateRadius;
        public string Prompt => site != null ? site.GatePrompt() : null;
        public void Use() { if (site != null) site.GateUse(); }
    }

    /// <summary>L'altare del tributo (cancelli che chiedono punti): un'ara in pietra con un piatto d'oro che brilla, vicino al cancello. Pagare qui o
    /// sul cancello e' lo stesso.</summary>
    public sealed class AnnexAltar : MonoBehaviour, IInteract
    {
        AnnexSite site;
        Material gold;

        public Vector3 Point => transform.position + Vector3.up * 1.0f;
        public float Radius => 2.6f;
        public string Prompt => site != null ? site.TributePrompt("ALTARE DEL TRIBUTO") : null;
        public void Use() { if (site != null) site.GateUse(); }

        public static AnnexAltar Build(AnnexSite site)
        {
            var go = FloorBuilder.AnnexProp(site.props, "chi_altare", site.altarPos, site.altarYaw, 0.8f);
            var A = go.AddComponent<AnnexAltar>();
            A.site = site;
            A.gold = ShrineKit.EmitOwn(FloorBuilder.GateColor(GateKind.Tributo), 1.6f);
            ShrineKit.Prim(go.transform, PrimitiveType.Cylinder, "Piatto del tributo", A.gold, new Vector3(0f, 1.02f, 0f), new Vector3(0.42f, 0.015f, 0.42f));
            ShrineKit.PointLight(go.transform, "Luce del tributo", new Vector3(0f, 1.55f, 0f), FloorBuilder.GateColor(GateKind.Tributo), 1.4f * LightPool.Dim, 4.6f, 0.2f, 2.2f);
            Interact.Add(A);
            return A;
        }

        void OnDestroy()
        {
            Interact.Remove(this);
            if (gold != null) Destroy(gold);
        }
    }

    /// <summary>Una leva dei sigilli: un piedistallo di pietra con la leva di ferro e una gemma che da rossa diventa verde.</summary>
    public sealed class AnnexLever : MonoBehaviour, IInteract
    {
        AnnexSite site;
        int index;
        bool done, moving;
        Transform arm;
        Material gem;
        Light glow;
        float angle = -50f;

        public Vector3 Point => transform.position + Vector3.up * 0.95f;
        public float Radius => 2.4f;
        public string Prompt => done ? null : (site.pulled > 0 ? $"SIGILLO — tira la leva ({site.pulled}/{Mathf.Max(1, site.data.levers)}, {Mathf.CeilToInt(site.Remaining)} s)" : "SIGILLO — tira la leva");

        static readonly Color Closed = new Color(0.95f, 0.22f, 0.30f), Open = new Color(0.30f, 1.0f, 0.45f);

        public static AnnexLever Build(AnnexSite site, int index, Vector3 pos, float yaw)
        {
            var go = new GameObject("Leva del sigillo " + (index + 1));
            go.transform.SetParent(site.props, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var L = go.AddComponent<AnnexLever>();
            L.site = site; L.index = index;
            var t = go.transform;
            ShrineKit.Prim(t, PrimitiveType.Cube, "Basamento", ShrineKit.Stone, new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.9f, 0.34f), default, true);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Piastra", ShrineKit.Brass, new Vector3(0f, 0.93f, 0f), new Vector3(0.58f, 0.05f, 0.42f));
            var pivot = new GameObject("Perno");
            pivot.transform.SetParent(t, false);
            pivot.transform.localPosition = new Vector3(0f, 0.97f, 0f);
            L.arm = pivot.transform;
            ShrineKit.Prim(L.arm, PrimitiveType.Cylinder, "Asta", ShrineKit.Iron, new Vector3(0f, 0.30f, 0f), new Vector3(0.055f, 0.30f, 0.055f));
            ShrineKit.Prim(L.arm, PrimitiveType.Sphere, "Pomolo", ShrineKit.Brass, new Vector3(0f, 0.62f, 0f), Vector3.one * 0.13f);
            L.gem = ShrineKit.EmitOwn(Closed, 2.2f);
            ShrineKit.Prim(t, PrimitiveType.Sphere, "Gemma", L.gem, new Vector3(0f, 0.62f, 0.19f), Vector3.one * 0.10f);
            L.glow = ShrineKit.PointLight(t, "Luce del sigillo", new Vector3(0f, 1.3f, 0.35f), Closed, 1.0f * LightPool.Dim, 4.2f, 0.18f, 2.0f);
            L.Pose();
            site.fi.propRoots.Add(go.transform);
            Interact.Add(L);
            return L;
        }

        void Pose() { if (arm != null) arm.localRotation = Quaternion.Euler(angle, 0f, 0f); }

        public void Use()
        {
            if (done || moving) return;
            Coop.Game?.AnnexChanged(site, index);          // in cooperativa la leva tirata conta per tutti
            Pulled();
        }

        /// <summary>La leva tirata da un compagno (cooperativa). Vero se era ancora su.</summary>
        public bool PullRemote()
        {
            if (done) return false;
            Pulled();
            return true;
        }

        void Pulled()
        {
            done = true;
            StartCoroutine(Pull(50f));
            Sfx.At(SoundBank.Clang, transform.position, 0.8f, 1.1f, 2f, 40f);
            gem.SetColor("_EmissionColor", Open * 2.2f);
            ShrineKit.SetGlow(glow, 1.4f * LightPool.Dim);
            glow.color = Open;
            site.LeverPulled(this);
        }

        public void Reset()
        {
            if (!done) return;
            done = false;
            StartCoroutine(Pull(-50f));
            gem.SetColor("_EmissionColor", Closed * 2.2f);
            glow.color = Closed;
        }

        IEnumerator Pull(float to)
        {
            moving = true;
            float from = angle;
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                angle = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / 0.4f));
                Pose();
                yield return null;
            }
            angle = to; Pose();
            moving = false;
        }

        void OnDestroy()
        {
            Interact.Remove(this);
            if (gem != null) Destroy(gem);
        }
    }

    /// <summary>La chiave del giardino: un piedistallo con una chiave d'oro che gira e brilla di verde. Prenderla la mette in tasca per questo piano.</summary>
    public sealed class AnnexKey : MonoBehaviour, IInteract
    {
        int forAnnex;
        bool taken;
        Transform spin;
        Material gold;
        Light glow;

        public Vector3 Point => transform.position + Vector3.up * 1.25f;
        public float Radius => 2.4f;
        public string Prompt => taken ? null : "CHIAVE D'ORO — prendila";

        public static AnnexKey Build(AnnexSite site, int forAnnex)
        {
            var go = new GameObject("Chiave del giardino");
            go.transform.SetParent(site.props, false);
            go.transform.position = site.keyPos;
            var K = go.AddComponent<AnnexKey>();
            K.forAnnex = forAnnex;
            var t = go.transform;
            ShrineKit.Prim(t, PrimitiveType.Cube, "Piedistallo", ShrineKit.Stone, new Vector3(0f, 0.5f, 0f), new Vector3(0.42f, 1.0f, 0.42f), default, true);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Piatto", ShrineKit.Brass, new Vector3(0f, 1.02f, 0f), new Vector3(0.52f, 0.05f, 0.52f));
            var root = new GameObject("Chiave");
            root.transform.SetParent(t, false);
            root.transform.localPosition = new Vector3(0f, 1.32f, 0f);
            K.spin = root.transform;
            K.gold = ShrineKit.EmitOwn(new Color(1f, 0.78f, 0.25f), 1.2f);
            var r = root.transform;
            ShrineKit.Prim(r, PrimitiveType.Cylinder, "Anello", K.gold, new Vector3(0f, 0.17f, 0f), new Vector3(0.17f, 0.012f, 0.17f), new Vector3(90f, 0f, 0f));
            ShrineKit.Prim(r, PrimitiveType.Cylinder, "Foro", ShrineKit.Dark, new Vector3(0f, 0.17f, 0f), new Vector3(0.075f, 0.0135f, 0.075f), new Vector3(90f, 0f, 0f));
            ShrineKit.Prim(r, PrimitiveType.Cylinder, "Fusto", K.gold, new Vector3(0f, -0.08f, 0f), new Vector3(0.028f, 0.20f, 0.028f));
            ShrineKit.Prim(r, PrimitiveType.Cube, "Dente 1", K.gold, new Vector3(0.055f, -0.22f, 0f), new Vector3(0.10f, 0.045f, 0.02f));
            ShrineKit.Prim(r, PrimitiveType.Cube, "Dente 2", K.gold, new Vector3(0.045f, -0.14f, 0f), new Vector3(0.08f, 0.04f, 0.02f));
            K.glow = ShrineKit.PointLight(t, "Luce della chiave", new Vector3(0f, 1.5f, 0f), new Color(0.35f, 1f, 0.55f), 1.6f * LightPool.Dim, 5f, 0.2f, 2.4f);
            site.fi.propRoots.Add(go.transform);
            Interact.Add(K);
            return K;
        }

        void Update()
        {
            if (taken || spin == null) return;
            spin.localRotation = Quaternion.Euler(0f, Time.time * 70f, 0f);
            spin.localPosition = new Vector3(0f, 1.32f + 0.05f * Mathf.Sin(Time.time * 2f), 0f);
        }

        public void Use()
        {
            if (taken) return;
            taken = true;
            AnnexRuntime.GiveKey(forAnnex);
            GameManager.I?.hud?.Toast("Hai una chiave d'oro: apre una porta nascosta d'edera.");
            Sfx.UI(SoundBank.PickCoins, 0.7f, 1.2f);
            if (spin != null) spin.gameObject.SetActive(false);
            if (glow != null) glow.enabled = false;
        }

        void OnDestroy()
        {
            Interact.Remove(this);
            if (gold != null) Destroy(gold);
        }
    }
}

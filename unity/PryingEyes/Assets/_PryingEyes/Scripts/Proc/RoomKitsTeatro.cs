using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>Teatro: la sala (platea, palchi, foyer, ridotto) e il palcoscenico con i camerini. Rosso, ottone e legno in sala;
    /// nero, polvere e una luce sola sul palco. Gli arredi sono quelli dell'albergo (sedie, poltrone, banchi) finche' la fase dei
    /// modelli non porta file di poltrone, palchi e sipario veri; le tende sono mesh a pieghe fatte qui, senza collisione.</summary>
    public static partial class RoomKits
    {
        static readonly Color StageWarm = new Color(1f, 0.66f, 0.38f);

        // ---------------------------------------------------------------- aiuti comuni ai temi nuovi
        /// <summary>Il lato di questa stanza con l'apertura piu' larga verso una stanza di tipo 'kind'; falso se non ce n'e'.</summary>
        static bool SideTo(Ctx c, RoomKind kind, out Side side, out Opening op)
        {
            side = Side.S; op = null; int bw = -1;
            foreach (Side s in new[] { Side.S, Side.N, Side.W, Side.E })
                foreach (var o in c.plan.OpeningsOn(c.r, s))
                {
                    if (o.b < 0 || o.oneSided) continue;
                    var other = c.plan.rooms[o.a == c.r.id ? o.b : o.a];
                    if (other.kind != kind || o.width <= bw) continue;
                    bw = o.width; side = s; op = o;
                }
            return op != null;
        }

        /// <summary>Punto sulla faccia interna del muro 's' alla coordinata 't' lungo il muro (x per S e N, z per W e E).</summary>
        static Vector3 WallPoint(Ctx c, Side s, float t)
        {
            switch (s)
            {
                case Side.S: return new Vector3(t, 0f, c.iz0);
                case Side.N: return new Vector3(t, 0f, c.iz1);
                case Side.W: return new Vector3(c.ix0, 0f, t);
                default: return new Vector3(c.ix1, 0f, t);
            }
        }

        /// <summary>Una fila di arredi: 'n' pezzi da 'start' a passi di 'step', col davanti verso 'face'. Ritorna quanti ne ha messi.</summary>
        static int ThemeRow(Ctx c, string name, Vector3 start, Vector3 step, int n, Vector3 face, double skip = 0.0, float yawJit = 0f)
        {
            int placed = 0;
            for (int i = 0; i < n; i++)
            {
                if (skip > 0.0 && c.rng.Chance(skip)) continue;
                var g = Prop(c, name, start + step * i, face, 1f, true, 0f, yawJit > 0f ? c.rng.Range(-yawJit, yawJit) : 0f);
                if (g != null) placed++;
            }
            return placed;
        }

        /// <summary>Tenda a pieghe, senza collisione: striscia verticale tra a e b (conta solo x e z), dal pavimento a topAt(u), con
        /// 'folds' onde profonde 'depth' lungo 'face'. Si vede da tutti e due i lati.</summary>
        static GameObject Drape(Ctx c, string mat, Vector3 a, Vector3 b, float y0, Func<float, float> topAt, float folds, float depth, Vector3 face)
        {
            Vector3 span = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float len = span.magnitude;
            if (len < 0.05f) return null;
            Vector3 dir = span / len;
            var m = ProcKit.Mat(mat);
            var mb = new MeshBuilder();
            int seg = Mathf.Max(6, Mathf.CeilToInt(folds * 6f));
            float k = folds * Mathf.PI * 2f;
            Vector3 P(float u, float y) => new Vector3(a.x, y, a.z) + dir * (u * len) + face * (Mathf.Sin(u * k) * depth * 0.5f);
            Vector3 Nrm(float u) => (face - dir * (Mathf.Cos(u * k) * k * depth * 0.5f / len)).normalized;
            for (int i = 0; i < seg; i++)
            {
                float u0 = (float)i / seg, u1 = (float)(i + 1) / seg;
                float t0 = topAt(u0), t1 = topAt(u1);
                Vector3 n0 = Nrm(u0), n1 = Nrm(u1);
                Vector3 A = P(u0, y0), B = P(u1, y0), C = P(u1, t1), D = P(u0, t0);
                var uA = new Vector2(u0 * len, y0); var uB = new Vector2(u1 * len, y0); var uC = new Vector2(u1 * len, t1); var uD = new Vector2(u0 * len, t0);
                mb.QuadSmooth(m, A, B, C, D, n0, n1, n1, n0, uA, uB, uC, uD);
                mb.QuadSmooth(m, A, B, C, D, -n0, -n1, -n1, -n0, uA, uB, uC, uD);
            }
            var go = new GameObject("Tenda");
            go.transform.SetParent(c.props, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("Tenda", out var mats);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Cartello d'uscita verde sopra una porta: una scatola che brilla e un filo di luce verde.</summary>
        static void ExitSign(Ctx c, Vector3 wallPt, Vector3 inward, float y = 2.75f)
        {
            var g = new GameObject("Uscita");
            g.transform.SetParent(c.props, false);
            g.transform.position = new Vector3(wallPt.x, y, wallPt.z) + inward * 0.06f;
            g.transform.rotation = Quaternion.LookRotation(inward);
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Util.Kill(p.GetComponent<Collider>());
            p.transform.SetParent(g.transform, false);
            p.transform.localScale = new Vector3(0.52f, 0.17f, 0.06f);
            var mr = p.GetComponent<MeshRenderer>();
            mr.sharedMaterial = ProcKit.Mat("uscita");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            AddLight(c, g.transform.position + inward * 0.35f, new Color(0.35f, 1f, 0.5f), 0.9f, 4.5f, false, true, g.transform);
        }

        /// <summary>Il punto sul muro (al centro dello spessore) dove sta un'apertura.</summary>
        static Vector3 OpeningPoint(Opening o) =>
            o.alongX ? new Vector3(o.center * 0.1f, 0f, o.line * 0.1f) : new Vector3(o.line * 0.1f, 0f, o.center * 0.1f);

        // ---------------------------------------------------------------- la sala
        /// <summary>La platea: file di sedie in due blocchi con il corridoio al centro e una corsia lungo ogni muro (li' si aprono i
        /// palchi), tutte rivolte al palcoscenico; manca piu' di un terzo delle sedie. Lampadari a corona sull'asse della sala.</summary>
        static void Platea(Ctx c)
        {
            Vector3 toStage = SideTo(c, RoomKind.Palcoscenico, out Side ss, out var arch) ? -Inward(ss) : Vector3.left;
            var fr = MakeFrame(c, toStage);                        // f: dal fondo (foyer) verso il palcoscenico
            float Lf = fr.Lf, Lr = fr.Lr, mid = Lr * 0.5f;
            // file da quattro poltrone a ribalta (tea_fila, 2,4 m): tre varianti, una con due sedili alzati, una con un sedile senza cuscino
            const float rowStep = 1.15f, centerHalf = 1.1f, sideLane = 2.0f, rowW = 2.4f;
            float f0 = 5.5f, fEnd = Lf - 5.2f;
            int rows = Mathf.Max(0, Mathf.FloorToInt((fEnd - f0) / rowStep) + 1);
            float block = mid - sideLane - centerHalf;                        // larghezza di un blocco di file
            int per = Mathf.FloorToInt(block / (rowW * 0.88f));               // file per blocco (possono stringersi fino al 12%)
            float sx = per > 0 ? Mathf.Clamp(block / (per * rowW), 0.88f, 1.15f) : 1f;
            var variants = new[] { "tea_fila", "tea_fila_b", "tea_fila_c" };
            for (int i = 0; i < rows; i++)
            {
                float f = f0 + i * rowStep;
                foreach (float sg in new[] { -1f, 1f })
                    for (int k = 0; k < per; k++)
                    {
                        if (c.rng.Chance(0.14)) continue;                    // una fila e' stata portata via
                        float rc = mid + sg * (centerHalf + (k + 0.5f) * block / per);
                        string nm = variants[c.rng.Weighted(new[] { 0.55, 0.25, 0.20 })];
                        float yaw = c.rng.Chance(0.08) ? c.rng.Range(-6f, 6f) : c.rng.Range(-1.5f, 1.5f);
                        Prop(c, nm, fr.P(f + c.rng.Range(-0.04f, 0.04f), rc), fr.f, sx, true, 0f, yaw);
                    }
            }
            // lampadari a corona sull'asse: il primo (verso l'ingresso) e' sempre acceso
            for (int i = 0; i < 3; i++)
                Crown(c, fr.P(Lf * (0.26f + 0.24f * i), mid, c.r.wallH - 0.5f), i == 0 || c.rng.Chance(0.55), 4.5f, 24f);
            // applique lungo i muri lunghi, una ogni ~4,5 m: quelle davanti alle porte dei palchi saltano da sole
            int ns = Mathf.Max(2, Mathf.RoundToInt(Lf / 4.5f));
            for (int i = 0; i < ns; i++)
            {
                float f = (i + 0.5f) / ns * Lf;
                foreach (bool left in new[] { true, false })
                {
                    if (!Free(c, fr.P(f, left ? 0.3f : Lr - 0.3f), 0.5f, 0.3f, 0f, 0f)) continue;
                    Sconce(c, fr.P(f, left ? 0.02f : Lr - 0.02f, 2.5f), left ? fr.r : -fr.r, c.rng.Chance(0.55), 5.5f, 10f);
                }
            }
            // lo spiraglio del palcoscenico: un lume caldo basso davanti all'arco
            AddLight(c, fr.P(Lf - 1.2f, mid, 1.6f), StageWarm, 3.2f, 11f, false);
            // uscita verde sopra la porta del foyer
            if (SideTo(c, RoomKind.Foyer, out Side fs, out var door)) ExitSign(c, WallPoint(c, fs, door.center * 0.1f), Inward(fs));
        }

        /// <summary>Il palco: due poltrone di velluto rivolte alla sala, un tavolino in fondo, un paio di applique. Il passaggio resta libero.</summary>
        static void Palco(Ctx c)
        {
            Side ds = MainSide(c, RoomKind.Platea);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            float f = Mathf.Min(2.5f, Lf - 1.4f);
            // alle due estremita' del palco, lasciando in mezzo un varco largo almeno 1,3 m (la navmesh vuole 0,8 m e un po' di margine)
            float seatR = Mathf.Max(0.66f, Lr * 0.18f);
            foreach (float rr in new[] { seatR, Lr - seatR })
                Prop(c, "arr_poltrona", fr.P(f, rr), -fr.f, 1f, true, 0f, c.rng.Range(-8f, 8f));
            if (Lf > 3.4f && c.rng.Chance(0.6)) Prop(c, "arr_tavolino", fr.P(Lf - 0.55f, Lr * 0.5f), -fr.f, Mathf.Min(1f, (Lr - 1.2f) / 1.4f));
            foreach (bool left in new[] { true, false })
                Sconce(c, fr.P(Lf * 0.62f, left ? 0.02f : Lr - 0.02f, 2.0f), left ? fr.r : -fr.r, true, 6.5f, 8f);
        }

        /// <summary>Il foyer: biglietteria con il mobile del guardaroba, un salotto, palme, un orologio; due lampadari e applique.</summary>
        static void Foyer(Ctx c)
        {
            Side ds = MainSide(c, RoomKind.Platea);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            Vector3 toDoor = -fr.f;
            Prop(c, "arr_chiavi", fr.P(Lf - 0.3f, Lr * 0.5f), toDoor, Mathf.Min(1f, (Lr - 1.0f) / 5.1f));
            Prop(c, "arr_banco", fr.P(Lf - 1.75f, Lr * 0.5f), toDoor, Mathf.Min(1f, (Lr - 2.2f) / 4.04f));
            bool left = c.rng.Chance(0.5);
            float rw = left ? 0.6f : Lr - 0.6f;
            Vector3 inL = left ? fr.r : -fr.r;
            float fs = Lf * 0.36f;
            Prop(c, "arr_divano", fr.P(fs, rw + (left ? 0.1f : -0.1f)), inL);
            Prop(c, "arr_tavolino", fr.P(fs, left ? 2.3f : Lr - 2.3f), inL);
            Prop(c, "arr_poltrona", fr.P(fs - 2.0f, left ? 2.2f : Lr - 2.2f), Quaternion.Euler(0f, left ? 40f : -40f, 0f) * -fr.f);
            Prop(c, "arr_palma", fr.P(0.7f, 0.7f), fr.f, 1f, true, 0f, c.rng.Range(0f, 360f));
            Prop(c, "arr_palma", fr.P(0.7f, Lr - 0.7f), fr.f, 1f, true, 0f, c.rng.Range(0f, 360f));
            Prop(c, "arr_orologio", fr.P(Lf * 0.5f, left ? Lr - 0.3f : 0.3f), left ? -fr.r : fr.r);
            for (int i = 0; i < 2; i++)
                Crown(c, fr.P(Lf * (0.3f + 0.4f * i), Lr * 0.5f, c.r.wallH - 0.5f), i == 0 || c.rng.Chance(0.5), 2.2f, 16f);
            int ns = Mathf.Max(2, Mathf.RoundToInt(Lf / 4f));
            for (int i = 0; i < ns; i++)
            {
                float f = (i + 0.5f) / ns * Lf;
                foreach (bool l in new[] { true, false })
                {
                    if (!Free(c, fr.P(f, l ? 0.3f : Lr - 0.3f), 0.5f, 0.3f, 0f, 0f)) continue;
                    Sconce(c, fr.P(f, l ? 0.02f : Lr - 0.02f, 2.4f), l ? fr.r : -fr.r, c.rng.Chance(0.6), 4.2f, 9f);
                }
            }
        }

        /// <summary>Il ridotto: un bar con il suo scaffale di bottiglie, un tavolo con due sedie, una lampadina e un paio di applique.</summary>
        static void Ridotto(Ctx c)
        {
            Side ds = MainSide(c, RoomKind.Foyer);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            Prop(c, "arr_scaffale", fr.P(Lf - 0.25f, Lr * 0.5f), -fr.f, Mathf.Min(1f, (Lr - 0.3f) / 1.2f));
            Prop(c, "arr_banco", fr.P(Lf - 1.35f, Lr * 0.5f), -fr.f, Mathf.Min(1f, (Lr - 1.2f) / 4.04f));
            if (Lf > 4.0f)
            {
                var t = Prop(c, "arr_tavolo", fr.P(2.6f, Lr * 0.3f), fr.f);
                if (t != null)
                    foreach (float s in new[] { -1f, 1f })
                        Prop(c, "arr_sedia", fr.P(2.6f, Lr * 0.3f + s * 0.78f), -fr.r * s, 1f, true, 0f, c.rng.Range(-30f, 30f));
            }
            foreach (bool left in new[] { true, false })
                Sconce(c, fr.P(Lf * 0.5f, left ? 0.02f : Lr - 0.02f, 2.1f), left ? fr.r : -fr.r, c.rng.Chance(0.75), 3.6f, 7f);
            PendantBulb(c, new Vector3(c.Center.x, c.r.wallH - 0.7f, c.Center.z), c.rng.Chance(0.6), 4.6f, 8f);
        }

        // ---------------------------------------------------------------- dietro il sipario
        /// <summary>Il palcoscenico: il sipario di velluto appeso nell'arco e raccolto ai lati, la ribalta di lampadine, una lampada di
        /// servizio accesa al centro con un riflettore dall'alto, le quinte nere, il pianoforte delle prove, qualche sedia e l'Occhio
        /// d'oro sopra la porta del fondo (si vede dalla sala attraverso l'arco).</summary>
        static void Palcoscenico(Ctx c)
        {
            bool hasArch = SideTo(c, RoomKind.Platea, out Side sa, out var arch);
            Vector3 toArch = hasArch ? -Inward(sa) : Vector3.right;
            var fr = MakeFrame(c, toArch);                          // f: dal fondo del palco verso la sala
            float Lf = fr.Lf, Lr = fr.Lr, mid = Lr * 0.5f, H = c.r.wallH;

            if (hasArch)
            {
                Vector3 along = arch.alongX ? Vector3.right : Vector3.forward;
                Vector3 wallC = OpeningPoint(arch);
                float hw = arch.width * 0.05f, panel = Mathf.Min(2.4f, hw * 0.5f);
                // sipario: due meta' appese all'arco, raccolte contro gli stipiti; seguono la testa acuta dell'arco
                foreach (float sg in new[] { -1f, 1f })
                {
                    float hwl = hw;
                    Drape(c, "tenda", wallC + along * (sg * hw), wallC + along * (sg * (hw - panel)), 0.03f,
                          u => arch.Head(Mathf.Lerp(hwl, hwl - panel, u), hwl) - 0.03f, 4f, 0.2f, Inward(sa));
                }
                // ribalta: lampadine a terra lungo il bordo del palco, poche accese, e un lume basso che si vede dalla sala
                int nb = Mathf.Clamp(Mathf.RoundToInt(hw * 1.6f), 5, 12);
                for (int i = 0; i < nb; i++)
                {
                    float t = (i + 0.5f) / nb - 0.5f;
                    Bulb(c, wallC + along * (t * (hw * 2f - 1.0f)) + Inward(sa) * 0.5f + Vector3.up * 0.12f, 0.11f, c.rng.Chance(0.6) ? "lampadina" : "lampadina_spenta");
                }
                AddLight(c, wallC + Inward(sa) * 0.8f + Vector3.up * 0.4f, StageWarm, 7.0f, 12f, false);
            }

            // lampada di servizio ("ghost light") al centro e un riflettore stretto dall'alto
            Vector3 gp = fr.P(Lf * 0.55f, mid);
            var lamp = Prop(c, "arr_lampada", gp, fr.f, 1f, false);
            if (lamp != null)
            {
                Occupy(c, gp, 0.5f, 0.5f, 0f);
                var pl = AddLight(c, gp + Vector3.up * 1.3f, new Color(1f, 0.74f, 0.45f), 14f, 13f, true, true, lamp.transform);
                lamp.AddComponent<LampFlicker>().Init(pl);
            }
            var spot = AddLight(c, new Vector3(gp.x, H - 1.0f, gp.z), new Color(1f, 0.86f, 0.64f), 520f, H + 2f, true, true, null, LightType.Spot);
            spot.l.spotAngle = 50f; spot.l.innerSpotAngle = 22f;
            spot.l.transform.rotation = Quaternion.LookRotation(Vector3.down);

            // quinte: tre paia di tende nere appese ai lati
            foreach (float fk in new[] { 0.28f, 0.5f, 0.72f })
                foreach (bool left in new[] { true, false })
                    Drape(c, "tenda_nera", fr.P(Lf * fk, left ? 0.06f : Lr - 0.06f), fr.P(Lf * fk, left ? 2.6f : Lr - 2.6f), 0.1f, u => H - 0.5f, 3f, 0.14f, fr.f);

            // l'Occhio d'oro sul muro del fondo, sopra la porta del corridoio
            if (hasArch)
            {
                Side back = Plan.Opposite(sa);
                float t = Plan.AlongX(back) ? c.Center.x : c.Center.z;
                foreach (var o in c.plan.OpeningsOn(c.r, back)) if (o.b >= 0 && !o.oneSided) { t = o.center * 0.1f; break; }
                Vector3 eyeAt = WallPoint(c, back, t) + Inward(back) * 0.1f;
                var eye = Prop(c, "chi_occhio", eyeAt, Inward(back), 1f, false, 4.3f);
                if (eye != null)
                {
                    eye.transform.localScale = Vector3.one * 3.2f;
                    // un faro stretto lo illumina dall'alto e dal davanti: dall'arco si vede come un'icona nel buio
                    Vector3 from = eyeAt + Inward(back) * 6.0f + Vector3.up * (H - 1.4f);
                    var eyeSpot = AddLight(c, from, new Color(1f, 0.82f, 0.5f), 150f, 14f, false, true, null, LightType.Spot);
                    eyeSpot.l.spotAngle = 30f; eyeSpot.l.innerSpotAngle = 14f;
                    eyeSpot.l.transform.rotation = Quaternion.LookRotation((eyeAt + Vector3.up * 6.1f) - from);
                }
            }

            // pianoforte delle prove, sedie e un tavolo sparsi, un carrello di scena
            Prop(c, "arr_pianoforte", fr.P(Lf - 4.2f, Lr * 0.22f), Quaternion.Euler(0f, c.rng.Range(-25f, 25f), 0f) * fr.f);
            int ch = c.rng.Range(3, 7);
            for (int i = 0; i < ch; i++)
                Prop(c, "arr_sedia", fr.P(c.rng.Range(Lf * 0.3f, Lf * 0.85f), c.rng.Range(2.8f, Lr - 2.8f)), Quaternion.Euler(0f, c.rng.Range(0f, 360f), 0f) * Vector3.forward);
            if (c.rng.Chance(0.6)) Prop(c, "arr_tavolo", fr.P(Lf * 0.4f, c.rng.Range(3f, Lr - 3f)), fr.f, 1f, true, 0f, c.rng.Range(-40f, 40f));
            if (c.rng.Chance(0.5)) Prop(c, "arr_carrello", fr.P(2.0f, c.rng.Chance(0.5f) ? 3.2f : Lr - 3.2f), fr.r, 1f, true, 0f, c.rng.Range(-30f, 30f));
            // due fari su treppiede ai lati dell'arco, puntati verso il centro del palco, e un manichino dimenticato
            foreach (float rr in new[] { 2.6f, Lr - 2.6f })
            {
                Vector3 p = fr.P(Lf - 3.4f, rr);
                Vector3 toward = (fr.P(Lf * 0.55f, mid) - p); toward.y = 0f;
                Prop(c, "tea_faro", p, toward.normalized, 1f, true, 0f, c.rng.Range(-10f, 10f));
            }
            if (c.rng.Chance(0.45)) Prop(c, "tea_manichino", fr.P(c.rng.Range(Lf * 0.25f, Lf * 0.5f), c.rng.Chance(0.5f) ? 3.4f : Lr - 3.4f), Quaternion.Euler(0f, c.rng.Range(0f, 360f), 0f) * Vector3.forward);
        }

        /// <summary>Il camerino: le toelette lungo il muro in fondo, ognuna con la sua sedia e una lampadina, l'armadio dei costumi.</summary>
        static void Camerino(Ctx c)
        {
            Side ds = MainSide(c, RoomKind.Corridoio);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            int n = Mathf.Clamp(Mathf.FloorToInt(Lr / 1.9f), 1, 3);
            for (int i = 0; i < n; i++)
            {
                float r = (i + 0.5f) * Lr / n;
                // toeletta con lo specchio a lampadine contro il muro in fondo, la sedia davanti; la luce calda si somma alle lampadine
                var t = Prop(c, "tea_toeletta", fr.P(Lf - 0.30f, r), -fr.f);
                if (t == null) continue;
                Prop(c, "arr_sedia", fr.P(Lf - 0.95f, r + c.rng.Range(-0.2f, 0.2f)), fr.f, 1f, true, 0f, c.rng.Range(-35f, 35f));
                if (c.rng.Chance(0.75)) AddLight(c, fr.P(Lf - 0.55f, r, 1.35f), new Color(1f, 0.74f, 0.46f), 3.2f, 4.6f, false, true, t.transform);
            }
            // un manichino da sarta in un angolo, girato verso la porta
            if (c.rng.Chance(0.65)) Prop(c, "tea_manichino", fr.P(c.rng.Range(1.0f, Lf * 0.5f), c.rng.Chance(0.5f) ? 0.6f : Lr - 0.6f), Quaternion.Euler(0f, c.rng.Range(-40f, 40f), 0f) * -fr.f);
            bool left = c.rng.Chance(0.5);
            Prop(c, "arr_armadio", fr.P(2.6f, left ? 0.4f : Lr - 0.4f), left ? fr.r : -fr.r);
            if (c.rng.Chance(0.5)) Prop(c, "arr_poltrona", fr.P(Lf * 0.55f, left ? Lr - 0.9f : 0.9f), left ? -fr.r : fr.r, 1f, true, 0f, c.rng.Range(-20f, 20f));
            PendantBulb(c, new Vector3(c.Center.x, c.r.wallH - 0.5f, c.Center.z), c.rng.Chance(0.65), 6f, 9f);
        }

        /// <summary>Il deposito di scena: scaffali al fondo, un carrello, sedie e tavoli ammucchiati; una lampadina nuda.</summary>
        static void Deposito(Ctx c)
        {
            Side ds = MainSide(c);
            var fr = MakeFrame(c, Inward(ds));
            float Lf = fr.Lf, Lr = fr.Lr;
            int ns = Mathf.Max(1, Mathf.FloorToInt((Lr - 0.4f) / 1.25f));
            ThemeRow(c, "arr_scaffale", fr.P(Lf - 0.25f, 0.2f + 0.6f), fr.r * 1.25f, ns, -fr.f, 0.15);
            if (Lr > 2.6f && Lf > 3f) Prop(c, "arr_carrello", fr.P(Lf * 0.5f, 0.55f), fr.r, 1f, true, 0f, c.rng.Range(-25f, 25f));
            int ch = c.rng.Range(2, 6);
            for (int i = 0; i < ch; i++)
                Prop(c, "arr_sedia", fr.P(c.rng.Range(2.4f, Mathf.Max(2.5f, Lf - 1.2f)), c.rng.Range(0.5f, Lr - 0.5f)), Quaternion.Euler(0f, c.rng.Range(0f, 360f), 0f) * Vector3.forward);
            if (c.rng.Chance(0.5)) Prop(c, "arr_tavolo", fr.P(Lf * 0.6f, Lr - 0.9f), fr.f, 1f, true, 0f, c.rng.Range(-30f, 30f));
            // manichini ammucchiati tra le casse, girati in direzioni diverse
            int dummies = c.rng.Range(1, 3);
            for (int i = 0; i < dummies; i++)
                Prop(c, "tea_manichino", fr.P(c.rng.Range(2.4f, Mathf.Max(2.5f, Lf - 1.2f)), c.rng.Range(0.6f, Lr - 0.6f)), Quaternion.Euler(0f, c.rng.Range(0f, 360f), 0f) * Vector3.forward);
            PendantBulb(c, new Vector3(c.Center.x, c.r.wallH - 0.6f, c.Center.z), c.rng.Chance(0.6), 4.5f, 7f);
        }

        /// <summary>Il corridoio delle quinte: lampadine nude appese sull'asse, qualche carrello di scena e un armadio.</summary>
        static void CorridoioTeatro(Ctx c, Frame fr)
        {
            float Lf = fr.Lf, Lr = fr.Lr;
            int n = Mathf.Max(2, Mathf.RoundToInt(Lf / 5f));
            for (int i = 0; i < n; i++)
                PendantBulb(c, fr.P((i + 0.5f) / n * Lf, Lr * 0.5f, c.r.wallH - 0.45f), c.rng.Chance(0.75), 9f, 10f);
            // i corridoi delle quinte sono larghi 2,6 m (interno 2,3: fascia di 0,5 m): carrello e armadio ci stanno solo in quello da 3 m
            if (c.rng.Chance(0.5)) HugWallAny(c, fr, "arr_carrello", 3f);
            if (c.rng.Chance(0.35)) HugWallAny(c, fr, "arr_armadio");
        }
    }
}

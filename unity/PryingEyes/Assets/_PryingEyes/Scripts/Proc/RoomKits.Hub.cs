using System.Collections.Generic;
using PryingEyes.Cattedrale;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Proc
{
    /// <summary>L'arredo della cattedrale (HubPlan): niente arredi a caso. Ogni opera ha il suo posto: finche' non c'e', un cantiere (impalcatura,
    /// pietre, il cartello con il costo); fatta, il suo arredo (fonte, altare, organo, forgia...). Il sagrato tiene i quattro banchi del mercato,
    /// la discesa alle catacombe i cancelli dei rami. Le luci crescono con le opere: la cattedrale da rudere al buio diventa una chiesa accesa.</summary>
    public static partial class RoomKits
    {
        static readonly Color Warm = new Color(1f, 0.70f, 0.40f), Lantern = new Color(1f, 0.78f, 0.50f), Moon = new Color(0.62f, 0.72f, 1f);

        static Light HubDress(Ctx c)
        {
            ShrineKit.Mats();
            System.Func<string, bool> built = Fabbrica.Costruita;
            switch (c.r.id)
            {
                case HubPlan.Sagrato: HubSagrato(c); break;
                case HubPlan.Battistero:
                    if (built("fonte")) { HubProp(c, "chi_fonte", c.Center, Vector3.forward); HubCandles(c, c.Center, 1.6f, 4); }
                    else HubSite(c, "fonte", c.Center, Vector3.back);
                    HubLight(c, new Vector3(c.Center.x, c.r.wallH - 0.8f, c.Center.z), Warm, built("fonte") ? 3.4f : 1.4f, 7f);
                    break;
                case HubPlan.Nartece:
                    {
                        // il leggio del registro, a sinistra entrando
                        var at = new Vector3(c.ix0 + 1.3f, 0f, c.iz1 - 1.6f);
                        var go = new GameObject("Registro");
                        go.transform.SetParent(c.props, false);
                        go.transform.position = at;
                        ShrineKit.Prim(go.transform, PrimitiveType.Cube, "Colonna", ShrineKit.Wood, new Vector3(0f, 0.55f, 0f), new Vector3(0.16f, 1.1f, 0.16f), default, true);
                        ShrineKit.Prim(go.transform, PrimitiveType.Cube, "Piano", ShrineKit.Wood, new Vector3(0f, 1.14f, 0f), new Vector3(0.62f, 0.05f, 0.46f), new Vector3(-18f, 90f, 0f));
                        ShrineKit.Prim(go.transform, PrimitiveType.Cube, "Libro", ShrineKit.Paper, new Vector3(0f, 1.2f, 0f), new Vector3(0.5f, 0.04f, 0.36f), new Vector3(-18f, 90f, 0f));
                        var reg = go.AddComponent<RegistroFabbrica>();
                        reg.point = at + Vector3.up * 1.2f;
                        HubCandles(c, at + new Vector3(0.6f, 0f, 0.4f), 0f, 1);
                        HubLight(c, new Vector3(c.Center.x, c.r.wallH - 0.6f, c.Center.z), Warm, 2.6f, 9f);
                    }
                    break;
                case HubPlan.Campanile:
                    if (built("campanile")) { HubBell(c, new Vector3(c.Center.x, c.r.wallH - 2.6f, c.Center.z)); HubLight(c, c.Center + Vector3.up * 3.2f, Warm, 2.2f, 8f); }
                    else { HubRubble(c, c.Center, 1.6f, 9); HubSite(c, "campanile", c.Center + new Vector3(0f, 0f, -1.2f), Vector3.back); }
                    break;
                case HubPlan.NavatellaO:
                    if (built("confessionale")) HubProp(c, "chi_confessionale", new Vector3(c.ix0 + 0.7f, 0f, c.iz0 + 7f), Vector3.right);
                    else HubSite(c, "confessionale", new Vector3(c.ix0 + 1.2f, 0f, c.iz0 + 7f), Vector3.right);
                    HubCandles(c, new Vector3(c.ix0 + 0.6f, 0f, c.iz1 - 4f), 0f, 1);
                    break;
                case HubPlan.NavatellaE:
                    HubChapel(c, "cappella_pietra", new Vector3(c.ix1 - 0.9f, 0f, c.iz0 + 8f), new Color(0.75f, 0.85f, 1f));
                    HubChapel(c, "cappella_mani", new Vector3(c.ix1 - 0.9f, 0f, c.iz0 + 20f), new Color(1f, 0.72f, 0.45f));
                    break;
                case HubPlan.Navata: HubNave(c); break;
                case HubPlan.Presbiterio: HubChoir(c); break;
                case HubPlan.Sacrestia: HubSacristy(c); break;
                case HubPlan.Cripta: HubCrypt(c); break;
                case HubPlan.Armeria: HubArmory(c); break;
            }
            var window = WindowLights(c);
            Census(c);
            return window;
        }

        // ---------------------------------------------------------------- pezzi comuni
        static GameObject HubProp(Ctx c, string name, Vector3 pos, Vector3 face, float sx = 1f) => Prop(c, name, pos, face, sx, false);

        static PoolLight HubLight(Ctx c, Vector3 pos, Color col, float intensity, float range, bool shadow = false) =>
            AddLight(c, pos, col, intensity, range, shadow, true, c.parent, LightType.Point, false, false);

        /// <summary>Qualche cero acceso attorno a un punto (raggio 'r', 'n' ceri; r = 0: un candelabro solo).</summary>
        static void HubCandles(Ctx c, Vector3 at, float r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) / n * Mathf.PI * 2f;
                var p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                Candle(c, r > 0f ? "chi_ceri" : "chi_candelabro", p, Vector3.forward, true, false, false);
            }
        }

        /// <summary>Il cantiere di un'opera: due ritti e un traverso d'impalcatura, un tavolato, un mucchio di pietre e il cartello con il costo.</summary>
        static CantiereOpera HubSite(Ctx c, string opera, Vector3 pos, Vector3 face, float w = 1.8f)
        {
            var go = new GameObject("Cantiere_" + opera);
            go.transform.SetParent(c.props, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(face, Vector3.up);
            var t = go.transform;
            foreach (float sx in new[] { -w * 0.5f, w * 0.5f })
                foreach (float sz in new[] { -0.35f, 0.35f })
                    ShrineKit.Prim(t, PrimitiveType.Cube, "Ritto", ShrineKit.Wood, new Vector3(sx, 1.25f, sz), new Vector3(0.08f, 2.5f, 0.08f), default, true);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Tavolato", ShrineKit.Wood, new Vector3(0f, 1.32f, 0f), new Vector3(w + 0.1f, 0.05f, 0.8f), default, true);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Traverso", ShrineKit.Wood, new Vector3(0f, 2.3f, -0.35f), new Vector3(w, 0.07f, 0.07f), new Vector3(0f, 0f, 9f));
            ShrineKit.Prim(t, PrimitiveType.Cube, "Diagonale", ShrineKit.Wood, new Vector3(0f, 0.8f, 0.36f), new Vector3(w * 1.08f, 0.06f, 0.05f), new Vector3(0f, 0f, 28f));
            for (int i = 0; i < 4; i++)
                ShrineKit.Prim(t, PrimitiveType.Cube, "Pietra", ShrineKit.Stone, new Vector3(-w * 0.3f + i * 0.28f, 0.12f + (i % 2) * 0.1f, 0.62f), new Vector3(0.34f, 0.22f, 0.26f), new Vector3(0f, i * 23f, 0f), true);
            // il cartello: un'asse chiara su un palo, davanti all'impalcatura
            ShrineKit.Prim(t, PrimitiveType.Cube, "Palo", ShrineKit.Wood, new Vector3(w * 0.5f + 0.35f, 0.6f, 0.75f), new Vector3(0.06f, 1.2f, 0.06f));
            ShrineKit.Prim(t, PrimitiveType.Cube, "Cartello", ShrineKit.Paper, new Vector3(w * 0.5f + 0.35f, 1.25f, 0.78f), new Vector3(0.5f, 0.34f, 0.03f));
            var site = go.AddComponent<CantiereOpera>();
            site.opera = opera;
            site.point = go.transform.TransformPoint(new Vector3(w * 0.5f + 0.35f, 1.25f, 0.8f));
            site.radius = 2.6f;
            // la lanterna del cantiere: si vede dove si lavora
            HubLight(c, go.transform.TransformPoint(new Vector3(-w * 0.5f, 2.4f, 0.4f)), Lantern, 1.6f, 5.5f);
            return site;
        }

        /// <summary>Il pozzo in mezzo al sagrato: un anello di pietra, l'acqua nera, l'arco di ferro con la carrucola.</summary>
        static void HubWell(Ctx c, Vector3 at)
        {
            var root = new GameObject("Pozzo");
            root.transform.SetParent(c.props, false);
            root.transform.position = at;
            var t = root.transform;
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                ShrineKit.Prim(t, PrimitiveType.Cube, "Pietra", ShrineKit.Stone, new Vector3(Mathf.Cos(a) * 0.8f, 0.42f, Mathf.Sin(a) * 0.8f), new Vector3(0.5f, 0.84f, 0.26f), new Vector3(0f, -a * Mathf.Rad2Deg + 90f, 0f), true);
            }
            ShrineKit.Prim(t, PrimitiveType.Cylinder, "Acqua", ShrineKit.Dark, new Vector3(0f, 0.55f, 0f), new Vector3(1.4f, 0.01f, 1.4f));
            foreach (float s in new[] { -0.85f, 0.85f })
                ShrineKit.Prim(t, PrimitiveType.Cube, "Montante", ShrineKit.Iron, new Vector3(s, 1.3f, 0f), new Vector3(0.06f, 1.7f, 0.06f));
            ShrineKit.Prim(t, PrimitiveType.Cube, "Arco", ShrineKit.Iron, new Vector3(0f, 2.15f, 0f), new Vector3(1.76f, 0.06f, 0.06f));
            ShrineKit.Prim(t, PrimitiveType.Cylinder, "Carrucola", ShrineKit.Wood, new Vector3(0f, 2.0f, 0f), new Vector3(0.22f, 0.04f, 0.22f), new Vector3(90f, 0f, 0f));
        }

        static void HubRubble(Ctx c, Vector3 at, float r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a = c.rng.Range(0f, Mathf.PI * 2f), d = c.rng.Range(0.2f, r);
                var p = at + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                var s = new Vector3(c.rng.Range(0.25f, 0.6f), c.rng.Range(0.15f, 0.4f), c.rng.Range(0.25f, 0.55f));
                ShrineKit.Prim(c.props, PrimitiveType.Cube, "Maceria", ShrineKit.Stone, p + Vector3.up * s.y * 0.5f, s, new Vector3(c.rng.Range(-12f, 12f), c.rng.Range(0f, 90f), c.rng.Range(-12f, 12f)), true);
            }
        }

        /// <summary>Una cappella della navatella est: statua, ceri e inginocchiatoio se fatta (con la luce del suo colore), altrimenti il cantiere.</summary>
        static void HubChapel(Ctx c, string opera, Vector3 at, Color glow)
        {
            if (!Fabbrica.Costruita(opera)) { HubSite(c, opera, at + Vector3.left * 0.6f, Vector3.left); return; }
            HubProp(c, "chi_statua", at, Vector3.left);
            HubProp(c, "chi_inginocchiatoio", at + Vector3.left * 1.4f, Vector3.right);
            HubCandles(c, at + Vector3.left * 0.4f, 0.9f, 3);
            HubLight(c, at + new Vector3(-0.8f, 2.8f, 0f), glow, 4.2f, 6f, true);
        }

        static void HubBell(Ctx c, Vector3 at)
        {
            var bell = ShrineKit.LatheObj(c.props, "Campana", ShrineKit.Brass, ShrineKit.Lathe("CampanaCattedrale",
                new float[] { 0.05f, 1.15f, 0.22f, 1.1f, 0.32f, 0.9f, 0.4f, 0.5f, 0.6f, 0.12f, 0.62f, 0f }));
            bell.transform.position = at;
            bell.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
            ShrineKit.Prim(c.props, PrimitiveType.Cube, "Ceppo", ShrineKit.Wood, at + Vector3.up * 1.75f, new Vector3(2.4f, 0.22f, 0.3f), default, false);
            ShrineKit.Prim(c.props, PrimitiveType.Cylinder, "Corda", ShrineKit.Leather, new Vector3(at.x + 0.3f, at.y * 0.5f, at.z), new Vector3(0.025f, at.y * 0.5f, 0.025f));
        }

        // ---------------------------------------------------------------- il sagrato e il mercato
        static void HubSagrato(Ctx c)
        {
            // Keep the first pair by the market; bring the second pair closer to the portal.
            foreach (int side in new[] { -1, 1 })
            {
                HubLampPost(c, new Vector3(c.Center.x + side * (c.ix1 - c.Center.x - 11f), 0f, c.iz0 + 5f));
                HubLampPost(c, new Vector3(c.Center.x + side * 4.3f, 0f, c.iz1 - 2.9f));
            }
            HubWell(c, new Vector3(c.Center.x, 0f, c.iz0 + 7.5f));
            // la luna dall'alto, come nel giardino segreto (un faro che copre tutto il sagrato), e il cielo stellato attorno a tutta la cattedrale
            // (nel deserto il cielo vero c'e' gia': la cupola finta nasconderebbe la sabbia e il faro della luna darebbe luce al giorno)
            if (!Deserto.DesertMode.Active)
            {
                var moon = AddLight(c, c.Center + Vector3.up * 14f, Moon, 260f, 32f, false, true, c.parent, LightType.Spot, false, false);
                moon.l.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                moon.l.spotAngle = 116f; moon.l.innerSpotAngle = 70f;
                moon.l.name = "Luce di luna";
                var dome = new GameObject("Cielo");
                dome.transform.SetParent(c.parent, false);
                dome.transform.position = new Vector3(18f, -6f, 33f);
                dome.AddComponent<MeshFilter>().sharedMesh = GardenKit.SkyDome(78f);
                var dr = dome.AddComponent<MeshRenderer>();
                dr.sharedMaterial = GardenKit.Sky();
                dr.shadowCastingMode = ShadowCastingMode.Off; dr.receiveShadows = false;
            }
            // i quattro banchi: due a ovest, due a est, il bancone parallelo al lato lungo, i clienti dal centro
            var banchi = Listino.Banchi;
            HubStall(c, banchi[0], new Vector3(c.ix0 + 3.2f, 0f, c.iz0 + 3.0f), Vector3.forward, Vector3.right);
            HubStall(c, banchi[1], new Vector3(c.ix0 + 3.2f, 0f, c.iz0 + 10.2f), Vector3.forward, Vector3.right);
            HubStall(c, banchi[2], new Vector3(c.ix1 - 3.2f, 0f, c.iz0 + 1.6f), Vector3.forward, Vector3.left);
            HubStall(c, banchi[3], new Vector3(c.ix1 - 3.2f, 0f, c.iz0 + 9.4f), Vector3.forward, Vector3.left);
        }

        /// <summary>Un banco del mercato: il bancone lungo 'along' a partire da 'start', la tenda sopra, la lanterna e la merce in fila. Ogni
        /// oggetto e' un'offerta (BancoOfferta), a 0,95 m dall'altro: si sceglie mettendosi davanti, come le armi a muro.</summary>
        static void HubStall(Ctx c, BancoDef banco, Vector3 start, Vector3 along, Vector3 inward)
        {
            int n = banco.offerte.Length;
            float len = n * 0.95f + 0.5f;
            var root = new GameObject("Banco_" + banco.id);
            root.transform.SetParent(c.props, false);
            root.transform.position = start;
            root.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);       // z locale verso i clienti, x locale lungo il bancone
            var t = root.transform;
            float sideSign = Vector3.Dot(t.right, along) >= 0f ? 1f : -1f;
            Vector3 L(float a, float y, float d) => new Vector3(sideSign * a, y, d);
            var cloth = banco.id switch
            {
                "speziale" => new Color(0.25f, 0.32f, 0.20f),
                "armaiolo" => new Color(0.30f, 0.10f, 0.08f),
                "ossaio" => new Color(0.36f, 0.30f, 0.20f),
                _ => new Color(0.18f, 0.12f, 0.26f)
            };
            var counterWood = Deserto.DesertAwning.Wood();
            ShrineKit.Prim(t, PrimitiveType.Cube, "Bancone", counterWood, L(len * 0.5f, 0.47f, 0f), new Vector3(len, 0.94f, 0.72f), default, true);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Piano", counterWood, L(len * 0.5f, 0.96f, 0.02f), new Vector3(len + 0.06f, 0.05f, 0.8f), default, true);
            foreach (float a in new[] { 0.05f, len - 0.05f })
                foreach (float d in new[] { -0.6f, 0.45f })
                    ShrineKit.Prim(t, PrimitiveType.Cube, "Palo", ShrineKit.Wood, L(a, 1.25f, d), new Vector3(0.07f, 2.5f, 0.07f), default, true);
            Deserto.DesertAwning.Build(t, len, sideSign, cloth);
            Deserto.DesertAwning.Drape(t, len, sideSign, cloth);
            HubLight(c, t.TransformPoint(L(len * 0.5f, 2.1f, 0.25f)), Lantern, 2.2f, 6.5f);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Lanterna", ShrineKit.Iron, L(len * 0.5f, 2.2f, 0.3f), new Vector3(0.14f, 0.2f, 0.14f));
            for (int i = 0; i < n; i++)
            {
                string id = banco.offerte[i];
                var offer = Fabbrica.Mercato.Exists(id) ? Fabbrica.Mercato.Get(id) : default;
                if (offer.Id == null) continue;
                bool sell = offer.Goods[Risorse.Oboli] > 0;
                string shown = null;
                foreach (var e in sell ? offer.Price : offer.Goods) { shown = e.Key; break; }
                var item = new GameObject("Offerta_" + id);
                item.transform.SetParent(t, false);
                item.transform.localPosition = L(0.5f + i * 0.95f, 1.0f, 0.1f);
                HubGoods(item.transform, shown, sell);
                var bo = item.AddComponent<BancoOfferta>();
                bo.offerta = id;
                bo.point = item.transform.position + Vector3.up * 0.15f;
            }
        }

        /// <summary>La merce sul banco: un mucchietto della risorsa (o il rifornimento), e per la vendita un piatto della bilancia vuoto.</summary>
        static void HubGoods(Transform t, string id, bool sell)
        {
            var def = Risorse.Of(id);
            Color col = def != null ? new Color(def.r, def.g, def.b) : Color.white;
            var m = def != null && def.grado >= Grado.Rara ? ShrineKit.EmitOwn(col, 1.2f) : ShrineKit.Lit("Merce_" + id, col, id == Risorse.Acciaio ? 0.8f : 0f, 0.4f);
            if (sell)
            {
                ShrineKit.Prim(t, PrimitiveType.Cylinder, "Piatto", ShrineKit.Brass, new Vector3(0f, 0.02f, 0f), new Vector3(0.32f, 0.01f, 0.32f));
                ShrineKit.Prim(t, PrimitiveType.Cube, "Asta", ShrineKit.Brass, new Vector3(0f, 0.22f, -0.14f), new Vector3(0.02f, 0.42f, 0.02f));
            }
            if (Rifornimenti.Is(id))
            {
                switch (id)
                {
                    case Rifornimenti.Acquasanta: ShrineKit.Prim(t, PrimitiveType.Cylinder, "Ampolla", ShrineKit.EmitOwn(new Color(0.55f, 0.75f, 1f), 0.8f), new Vector3(0f, 0.1f, 0f), new Vector3(0.09f, 0.1f, 0.09f)); break;
                    case Rifornimenti.Lettera: ShrineKit.Prim(t, PrimitiveType.Cube, "Lettera", ShrineKit.Paper, new Vector3(0f, 0.02f, 0f), new Vector3(0.24f, 0.01f, 0.17f), new Vector3(0f, 12f, 0f)); break;
                    case Rifornimenti.Cero: ShrineKit.Prim(t, PrimitiveType.Cylinder, "Cero", ShrineKit.Wax, new Vector3(0f, 0.16f, 0f), new Vector3(0.07f, 0.16f, 0.07f)); break;
                    case Rifornimenti.Mappa: ShrineKit.Prim(t, PrimitiveType.Cylinder, "Rotolo", ShrineKit.Paper, new Vector3(0f, 0.05f, 0f), new Vector3(0.06f, 0.16f, 0.06f), new Vector3(0f, 0f, 90f)); break;
                    case Rifornimenti.Mina: ShrineKit.Prim(t, PrimitiveType.Cube, "Mina", ShrineKit.Brass, new Vector3(0f, 0.06f, 0f), new Vector3(0.16f, 0.12f, 0.12f)); break;
                    default: ShrineKit.Prim(t, PrimitiveType.Cube, "Carillon", ShrineKit.Wood, new Vector3(0f, 0.06f, 0f), new Vector3(0.16f, 0.1f, 0.12f)); break;
                }
                return;
            }
            for (int k = 0; k < (sell ? 1 : 3); k++)
                ShrineKit.Prim(t, id == Risorse.Ossa ? PrimitiveType.Capsule : id == Risorse.Brace || id == Risorse.Cuori ? PrimitiveType.Sphere : id == Risorse.Sangue ? PrimitiveType.Cylinder : PrimitiveType.Cube,
                    "Merce", m, new Vector3(-0.1f + k * 0.1f, 0.06f + (sell ? 0.04f : 0f), (k % 2) * 0.06f),
                    id == Risorse.Ossa ? new Vector3(0.05f, 0.1f, 0.05f) : id == Risorse.Legno ? new Vector3(0.06f, 0.06f, 0.22f) : new Vector3(0.09f, 0.07f, 0.09f),
                    id == Risorse.Ossa ? new Vector3(0f, k * 40f, 90f) : new Vector3(0f, k * 35f, 0f));
        }

        // ---------------------------------------------------------------- la navata
        static void HubNave(Ctx c)
        {
            bool volte = Fabbrica.Costruita("volte"), altare = Fabbrica.Costruita("altare");
            if (!volte)
            {
                // senza tetto: il coronamento spezzato dei muri, macerie della volta caduta, l'impalcatura in mezzo
                foreach (var (a, b, alongX, line) in new[] { (c.ix0, c.ix1, true, c.iz0), (c.ix0, c.ix1, true, c.iz1), (c.iz0, c.iz1, false, c.ix0), (c.iz0, c.iz1, false, c.ix1) })
                {
                    int n = Mathf.CeilToInt((b - a) / 1.5f);
                    for (int i = 0; i < n; i++)
                    {
                        float s0 = a + (b - a) * i / n, s1 = a + (b - a) * (i + 1) / n, h = c.rng.Range(0.1f, 0.55f);
                        Vector3 p = alongX ? new Vector3((s0 + s1) * 0.5f, c.r.wallH + h * 0.5f, line) : new Vector3(line, c.r.wallH + h * 0.5f, (s0 + s1) * 0.5f);
                        Vector3 s = alongX ? new Vector3(s1 - s0 + 0.02f, h, 0.62f) : new Vector3(0.62f, h, s1 - s0 + 0.02f);
                        ShrineKit.Prim(c.props, PrimitiveType.Cube, "Coronamento", ShrineKit.Stone, p, s);
                    }
                }
                HubRubble(c, new Vector3(c.Center.x - 3f, 0f, c.Center.z + 6f), 2.2f, 10);
                HubRubble(c, new Vector3(c.Center.x + 3.4f, 0f, c.Center.z - 5f), 1.8f, 8);
                HubSite(c, "volte", c.Center + new Vector3(0f, 0f, -2f), Vector3.back, 3.2f);
            }
            // l'organo, a destra entrando; i banchi: in disordine prima dell'altare, in file ordinate dopo
            if (Fabbrica.Costruita("organo")) HubProp(c, "chi_organo", new Vector3(c.ix0 + 2.2f, 0f, c.iz0 + 1.2f), Vector3.forward);
            else if (volte) HubSite(c, "organo", new Vector3(c.ix0 + 2.4f, 0f, c.iz0 + 2.2f), Vector3.forward);
            if (altare)
            {
                if (!Deserto.DesertMode.Active)         // nel deserto la navata e' libera per i leggii della Biblioteca
                    for (float z = c.iz0 + 6f; z < c.iz1 - 4f; z += 2.2f)
                        foreach (float x in new[] { c.Center.x - 3.1f, c.Center.x + 3.1f })
                            HubProp(c, "chi_panca", new Vector3(x, 0f, z), Vector3.forward, 1f);
                HubProp(c, "chi_lampadario", new Vector3(c.Center.x, 0f, c.Center.z + 5f), Vector3.forward, 2f);
                HubLight(c, new Vector3(c.Center.x, c.r.wallH - 2.8f, c.Center.z + 5f), Warm, 9f, 18f, true);
                HubProp(c, "chi_lampadario", new Vector3(c.Center.x, 0f, c.Center.z - 6f), Vector3.forward, 2f);
                HubLight(c, new Vector3(c.Center.x, c.r.wallH - 2.8f, c.Center.z - 6f), Warm, 7f, 16f);
            }
            else
            {
                for (int i = 0; i < (Deserto.DesertMode.Active ? 0 : 5); i++)
                    HubProp(c, "chi_panca", new Vector3(c.rng.Range(c.ix0 + 1.5f, c.ix1 - 1.5f), 0f, c.rng.Range(c.iz0 + 5f, c.iz1 - 3f)), new Vector3(c.rng.Range(-1f, 1f), 0f, c.rng.Range(-1f, 1f)).normalized + Vector3.forward * 0.01f);
                HubLight(c, new Vector3(c.Center.x, 3.5f, c.Center.z), Warm, 2.0f, 12f);
            }
            // il deserto: la Biblioteca dei Voti si costruisce qui (finita, i leggii li mette DesertStations dopo la costruzione della cattedrale)
            if (Deserto.DesertMode.Active && !Fabbrica.Costruita("biblioteca_voti"))
                HubSite(c, "biblioteca_voti", new Vector3(c.ix1 - 1.4f, 0f, c.Center.z + 2f), Vector3.left, 2.4f);
            // i cartoni del vetraio sotto le vetrate dei boss non ancora fatte (il cleristorio ovest), e il rosone sopra il portale
            for (int i = 0; i < HubPlan.VetrateOpere.Length; i++)
            {
                if (Fabbrica.Costruita(HubPlan.VetrateOpere[i])) continue;
                var at = new Vector3(c.ix0 + 1.1f, 0f, HubPlan.VetrateZ[i] * 0.1f + (i % 2 == 0 ? 1.4f : -1.4f));
                if (volte) HubSite(c, HubPlan.VetrateOpere[i], at, Vector3.right, 1.3f);
            }
            if (!Fabbrica.Costruita("rosone") && volte) HubSite(c, "rosone", new Vector3(c.ix1 - 2.6f, 0f, c.iz0 + 1.6f), Vector3.forward, 1.6f);
            HubCandles(c, new Vector3(c.Center.x - 2.4f, 0f, c.iz0 + 2.2f), 0f, 1);
            HubCandles(c, new Vector3(c.Center.x + 2.4f, 0f, c.iz0 + 2.2f), 0f, 1);
        }

        // ---------------------------------------------------------------- il presbiterio: l'altare e i suoi ceri
        static void HubChoir(Ctx c)
        {
            var altarAt = new Vector3(c.Center.x, 0f, c.iz1 - 2.6f);
            if (Fabbrica.Costruita("altare"))
            {
                HubProp(c, "chi_altare", altarAt, Vector3.back);
                HubProp(c, "chi_pala", new Vector3(c.Center.x, 0f, c.iz1 - 0.6f), Vector3.back);
                HubCandles(c, altarAt + Vector3.back * 0.1f, 1.6f, 2);
                var ceri = Fabbrica.Ceri();
                for (int i = 0; i < 3; i++)
                {
                    var p = altarAt + new Vector3((i - 1) * 1.5f, 0f, -2.2f - (i == 1 ? 0.35f : 0f));
                    var go = new GameObject("Cero_" + ceri[i]);
                    go.transform.SetParent(c.props, false);
                    go.transform.position = p;
                    ShrineKit.Prim(go.transform, PrimitiveType.Cylinder, "Fusto", ShrineKit.Brass, new Vector3(0f, 0.5f, 0f), new Vector3(0.07f, 0.5f, 0.07f), default, true);
                    ShrineKit.Prim(go.transform, PrimitiveType.Cylinder, "Piede", ShrineKit.Brass, new Vector3(0f, 0.03f, 0f), new Vector3(0.36f, 0.03f, 0.36f));
                    ShrineKit.Prim(go.transform, PrimitiveType.Cylinder, "Cero", ShrineKit.Wax, new Vector3(0f, 1.18f, 0f), new Vector3(0.09f, 0.18f, 0.09f));
                    var flame = ShrineKit.Prim(go.transform, PrimitiveType.Sphere, "Fiamma", ProcKit.HasMat("fiamma") ? ProcKit.Mat("fiamma") : ShrineKit.EmitOwn(Warm, 3f), new Vector3(0f, 1.42f, 0f), new Vector3(0.05f, 0.1f, 0.05f));
                    var pl = AddLight(c, p + Vector3.up * 1.5f, Warm, 3.2f, 5.5f, false, true, go.transform, LightType.Point, false, false);
                    var cero = go.AddComponent<CeroBenedizione>();
                    cero.benedizione = ceri[i];
                    cero.point = p + Vector3.up * 1.25f;
                    cero.radius = 1.9f;
                    cero.flame = pl.l; cero.flameMesh = flame.GetComponent<MeshRenderer>();
                }
                HubLight(c, new Vector3(c.Center.x, c.r.wallH - 3f, c.Center.z), Warm, 7f, 16f, true);
                // il deserto: l'altare della consacrazione (le armi si potenziano) si costruisce a sinistra dell'altare maggiore
                if (Deserto.DesertMode.Active && !Fabbrica.Costruita("consacrazione"))
                    HubSite(c, "consacrazione", new Vector3(c.ix0 + 2.2f, 0f, c.Center.z - 1.5f), Vector3.right, 2.2f);
            }
            else
            {
                HubRubble(c, altarAt + Vector3.back * 1.5f, 1.4f, 7);
                HubSite(c, "altare", altarAt + Vector3.back * 1.2f, Vector3.back, 2.2f);
                HubLight(c, new Vector3(c.Center.x, 3.2f, c.Center.z), Warm, 1.6f, 10f);
            }
        }

        // ---------------------------------------------------------------- la sacrestia: rastrelliera, forgia, tesoro
        static void HubSacristy(Ctx c)
        {
            bool sac = Fabbrica.Costruita("sacrestia");
            if (sac)
            {
                HubProp(c, "arr_armadio", new Vector3(c.ix0 + 0.5f, 0f, c.iz1 - 2.2f), Vector3.right);
                HubProp(c, "arr_tavolo", new Vector3(c.Center.x, 0f, c.Center.z + 0.5f), Vector3.forward);
                // la rastrelliera: tre armi gratis sul muro nord, scelte tra le migliori della pistola (grado 2, una di grado 3 con la Forgia)
                var picks = SacristyWeapons(Fabbrica.Costruita("forgia"));
                for (int i = 0; i < picks.Count; i++)
                {
                    var pos = new Vector3(c.ix0 + 2.2f + i * 2.0f, 1.55f, c.iz1 + 0.03f);
                    WallBuy.Place(picks[i], 0, pos, Quaternion.LookRotation(Vector3.back), WeaponDefs.All[picks[i]].GradeColor.ToColor());
                }
            }
            else HubSite(c, "sacrestia", new Vector3(c.Center.x - 1f, 0f, c.iz1 - 1.6f), Vector3.back, 2.4f);
            var forge = new Vector3(c.ix1 - 1.6f, 0f, c.iz0 + 1.8f);
            if (Fabbrica.Costruita("forgia"))
            {
                // l'incudine sul ceppo e il braciere con la brace accesa
                ShrineKit.Prim(c.props, PrimitiveType.Cylinder, "Ceppo", ShrineKit.Wood, forge + new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.3f, 0.5f), default, true);
                ShrineKit.Prim(c.props, PrimitiveType.Cube, "Incudine", ShrineKit.Iron, forge + new Vector3(0f, 0.72f, 0f), new Vector3(0.36f, 0.22f, 0.7f), default, true);
                ShrineKit.Prim(c.props, PrimitiveType.Cube, "Corno", ShrineKit.Iron, forge + new Vector3(0f, 0.76f, 0.45f), new Vector3(0.16f, 0.12f, 0.3f), new Vector3(0f, 0f, 0f));
                var brazier = forge + new Vector3(-0.2f, 0f, 1.7f);
                ShrineKit.Prim(c.props, PrimitiveType.Cylinder, "Braciere", ShrineKit.Iron, brazier + new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.45f, 0.9f), default, true);
                var embers = ShrineKit.Prim(c.props, PrimitiveType.Cylinder, "Brace", ProcKit.HasMat("kit_brace") ? ProcKit.Mat("kit_brace") : ShrineKit.EmitOwn(new Color(1f, 0.4f, 0.1f), 3f), brazier + new Vector3(0f, 0.92f, 0f), new Vector3(0.78f, 0.02f, 0.78f));
                embers.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                HubLight(c, brazier + new Vector3(0f, 1.3f, 0f), new Color(1f, 0.48f, 0.18f), 4.5f, 7f, true);
            }
            else if (sac) HubSite(c, "forgia", forge + Vector3.left * 0.4f, Vector3.left, 1.6f);
            var tesoro = new Vector3(c.ix0 + 1.4f, 0f, c.iz0 + 1.4f);
            if (Fabbrica.Costruita("tesoro"))
            {
                // due forzieri ferrati e lo scaffale con i calici
                foreach (float dz in new[] { 0f, 1.1f })
                {
                    ShrineKit.Prim(c.props, PrimitiveType.Cube, "Forziere", ShrineKit.Wood, tesoro + new Vector3(0f, 0.3f, dz), new Vector3(0.9f, 0.6f, 0.6f), default, true);
                    ShrineKit.Prim(c.props, PrimitiveType.Cube, "Fascia", ShrineKit.Iron, tesoro + new Vector3(0f, 0.3f, dz), new Vector3(0.94f, 0.08f, 0.64f));
                    ShrineKit.Prim(c.props, PrimitiveType.Cube, "Serratura", ShrineKit.Gold, tesoro + new Vector3(0.46f, 0.42f, dz), new Vector3(0.03f, 0.1f, 0.1f));
                }
                HubProp(c, "arr_scaffale", tesoro + new Vector3(0f, 0f, 2.4f), Vector3.right);
                HubLight(c, tesoro + Vector3.up * 1.6f, new Color(1f, 0.82f, 0.4f), 2.6f, 4.5f);
            }
            else if (sac) HubSite(c, "tesoro", tesoro + Vector3.right * 0.6f, Vector3.right, 1.4f);
            HubLight(c, new Vector3(c.Center.x, c.r.wallH - 0.6f, c.Center.z), Warm, sac ? 3.4f : 1.4f, 9f);
        }

        /// <summary>Le tre armi della rastrelliera: le prime di grado 2 del catalogo (mai la pistola di partenza), e con la Forgia la terza e'
        /// la prima di grado 3.</summary>
        static List<int> SacristyWeapons(bool forge)
        {
            var list = new List<int>();
            for (int i = 1; i < WeaponDefs.All.Length && list.Count < 3; i++) if (WeaponDefs.All[i].grade == 2) list.Add(i);
            if (forge)
                for (int i = 1; i < WeaponDefs.All.Length; i++)
                    if (WeaponDefs.All[i].grade == 3) { if (list.Count >= 3) list[2] = i; else list.Add(i); break; }
            return list;
        }

        // ---------------------------------------------------------------- la discesa: i cancelli dei rami e l'ossario
        static void HubCrypt(Ctx c)
        {
            // tre cancelli sul muro nord (Ossario, Teatro, Sanatorio), due su quello ovest (Guardaroba, Abisso)
            // (nel deserto non ci sono: le catacombe si raggiungono dai mausolei sulla sabbia)
            if (!Deserto.DesertMode.Active)
            {
                for (int i = 0; i < 3; i++) HubGate(c, i, new Vector3(c.ix0 + 1.9f + i * 2.7f, 0f, c.iz1), Vector3.back);
                for (int i = 3; i < 5; i++) HubGate(c, i, new Vector3(c.ix0, 0f, c.iz0 + 6.1f - (i - 3) * 2.8f), Vector3.right);
            }
            var oss = new Vector3(c.ix1 - 1.4f, 0f, c.iz0 + 1.2f);
            if (Fabbrica.Costruita("ossario"))
            {
                HubProp(c, "chi_sarcofago", oss, Vector3.left);
                for (int i = 0; i < 14; i++)
                {
                    var p = oss + new Vector3(c.rng.Range(-1.1f, 0.6f), 0f, c.rng.Range(1.2f, 2.6f));
                    var bone = ShrineKit.Prim(c.props, i % 4 == 0 ? PrimitiveType.Sphere : PrimitiveType.Capsule, i % 4 == 0 ? "Teschio" : "Osso",
                        ProcKit.HasMat("kit_osso") ? ProcKit.Mat("kit_osso") : ShrineKit.Wax, p + Vector3.up * 0.08f,
                        i % 4 == 0 ? Vector3.one * 0.2f : new Vector3(0.06f, 0.22f, 0.06f), new Vector3(c.rng.Range(60f, 110f), c.rng.Range(0f, 180f), 0f));
                    _ = bone;
                }
            }
            else HubSite(c, "ossario", oss + Vector3.left * 0.6f + Vector3.forward * 1.2f, Vector3.left, 1.6f);
            HubLight(c, new Vector3(c.Center.x, c.r.wallH - 0.7f, c.Center.z), Warm, 2.2f, 9f);
        }

        /// <summary>Un cancello di ramo: due stipiti di pietra, l'architrave, la grata (alzata se il ramo e' aperto) e, se aperto, due torce.</summary>
        static void HubGate(Ctx c, int ramo, Vector3 wall, Vector3 inward)
        {
            bool open = Fabbrica.RamoAperto(ramo);
            var root = new GameObject("Cancello_" + ramo);
            root.transform.SetParent(c.props, false);
            root.transform.position = wall + inward * 0.02f;
            root.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
            var t = root.transform;
            ShrineKit.Prim(t, PrimitiveType.Cube, "Stipite", ShrineKit.Stone, new Vector3(-0.95f, 1.25f, 0.12f), new Vector3(0.3f, 2.5f, 0.3f), default, true);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Stipite", ShrineKit.Stone, new Vector3(0.95f, 1.25f, 0.12f), new Vector3(0.3f, 2.5f, 0.3f), default, true);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Architrave", ShrineKit.Stone, new Vector3(0f, 2.62f, 0.12f), new Vector3(2.3f, 0.32f, 0.34f), default, true);
            // il fondo: la bocca nera della scala che scende (o il muro di mattoni del cancello non ancora fatto)
            ShrineKit.Prim(t, PrimitiveType.Cube, open ? "Buio" : "Muratura", open ? ShrineKit.Dark : ShrineKit.StoneDark, new Vector3(0f, 1.2f, 0.005f), new Vector3(1.6f, 2.4f, 0.01f));
            float lift = open ? 2.1f : 0f;
            for (int i = 0; i <= 10; i++)
                ShrineKit.Prim(t, PrimitiveType.Cube, "Sbarra", ShrineKit.Iron, new Vector3(-0.78f + i * 0.156f, 1.2f + lift, 0.08f), new Vector3(0.03f, 2.3f, 0.03f));
            foreach (float y in new[] { 0.35f, 1.3f, 2.2f })
                ShrineKit.Prim(t, PrimitiveType.Cube, "Traversa", ShrineKit.Iron, new Vector3(0f, y + lift, 0.08f), new Vector3(1.62f, 0.05f, 0.04f));
            // la targa del ramo, col colore della risorsa del suo boss
            var ex = Rami.Tutti[ramo].esclusiva;
            var def = ex != null ? Risorse.Of(ex) : null;
            var col = def != null ? new Color(def.r, def.g, def.b) : new Color(0.7f, 0.1f, 0.12f);
            ShrineKit.Prim(t, PrimitiveType.Cube, "Targa", open ? ShrineKit.EmitOwn(col, 0.9f) : ShrineKit.Stone, new Vector3(0f, 2.62f, 0.3f), new Vector3(0.7f, 0.18f, 0.02f));
            if (open)
            {
                foreach (float s in new[] { -1.25f, 1.25f })
                {
                    ShrineKit.Prim(t, PrimitiveType.Cube, "Staffa", ShrineKit.Iron, new Vector3(s, 1.75f, 0.2f), new Vector3(0.05f, 0.3f, 0.05f));
                    var f = ShrineKit.Prim(t, PrimitiveType.Sphere, "Fiamma", ProcKit.HasMat("fiamma") ? ProcKit.Mat("fiamma") : ShrineKit.EmitOwn(Warm, 3f), new Vector3(s, 1.98f, 0.2f), new Vector3(0.08f, 0.15f, 0.08f));
                    f.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                HubLight(c, t.TransformPoint(new Vector3(0f, 2.0f, 0.6f)), Warm, 2.4f, 6f);
            }
            var gate = root.AddComponent<CancelloRamo>();
            gate.ramo = ramo;
            gate.point = t.TransformPoint(new Vector3(0f, 1.3f, 0.2f));
        }
    }
}

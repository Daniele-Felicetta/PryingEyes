using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes.Deserto
{
    /// <summary>Il garage all'aperto di "Bozzo Moto": una tettoia di lamiera su pali di ferro, aperta da ogni lato, a destra della scalinata della cattedrale (sulla sabbia piana del sagrato: a meno di
    /// trenta metri dalla cattedrale il terreno e' piatto), con una piazzola per ogni moto che si puo' avere. Le moto stanno in fila, il muso verso il deserto, e ognuna ha davanti (dal lato della scalinata)
    /// il suo cartello col nome; le piazzole di moto non ancora comprate sono vuote, col cartello spento. A sinistra, verso la scalinata, un totem con l'insegna e una lampada; sotto il tetto cinque lampade che
    /// si accendono di notte; in fondo a destra il banco da lavoro con barili, gomme e taniche. Nello schermo condiviso ogni posto ha la sua fila (piu' a sud), sotto lo stesso tetto.
    /// Il disegno e' a codice, di cubi e cilindri: le uniche cose con un collider sono i pali e il banco (le moto e i giocatori ci passano in mezzo).</summary>
    public sealed class DesertGarageSite : MonoBehaviour
    {
        public static DesertGarageSite I { get; private set; }

        static readonly Color Sign = new Color(0.95f, 0.78f, 0.38f);
        Transform root;
        readonly List<Material> mats = new List<Material>();
        readonly List<Light> lamps = new List<Light>();
        readonly List<Renderer> bulbs = new List<Renderer>();
        readonly List<Text> names = new List<Text>();
        Material bulbOn, bulbOff;
        int rows;
        bool lit;
        float tickT;

        readonly List<float> postXs = new List<float>();

        /// <summary>Per le prove: i cartelli delle piazzole della prima fila (il testo che si legge), le lampade accese e dove stanno i pali del tetto (la x di ognuno: sulle righe tra una piazzola e l'altra).</summary>
        public IReadOnlyList<Text> NameBoards => names;
        public IReadOnlyList<float> PostXs => postXs;
        public bool LampsOn => lit;
        public int Rows => rows;
        /// <summary>Il rettangolo coperto dal tetto (x0, z0, x1, z1) sul piano.</summary>
        public Rect Footprint { get; private set; }

        public static DesertGarageSite Build(Transform host, int rows)
        {
            var go = new GameObject("Garage di Bozzo Moto");
            go.transform.SetParent(host, false);
            var s = go.AddComponent<DesertGarageSite>();
            s.rows = Mathf.Clamp(rows, 1, Seats.MaxSeats);
            s.Make();
            I = s;
            return s;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            DesertGarage.Bought -= OnBought;
            foreach (var m in mats) if (m != null) Destroy(m);
        }

        // ------------------------------------------------------------------ materiali e attrezzi
        Material Lit(Color c, float metal, float smooth)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Garage" };
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            mats.Add(m);
            return m;
        }

        Material Glow(Color c, float k)
        {
            var m = Lit(new Color(0.05f, 0.05f, 0.05f), 0f, 0.6f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;          // con il flag di un materiale fatto a codice URP puo' spegnere _EMISSION (come DesertPickup)
            m.SetColor("_EmissionColor", c * k);
            return m;
        }

        GameObject Prim(PrimitiveType t, string name, Vector3 at, Vector3 size, Quaternion rot, Material m, bool solid)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            g.transform.SetParent(root, false);
            g.transform.localPosition = at; g.transform.localRotation = rot; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            var col = g.GetComponent<Collider>();
            if (col != null && !solid) Destroy(col);
            return g;
        }

        GameObject Box(string name, Vector3 at, Vector3 size, Material m, bool solid = false) => Prim(PrimitiveType.Cube, name, at, size, Quaternion.identity, m, solid);
        GameObject Box(string name, Vector3 at, Vector3 size, Quaternion rot, Material m, bool solid = false) => Prim(PrimitiveType.Cube, name, at, size, rot, m, solid);
        /// <summary>Un cilindro in piedi: altezza e diametro in metri, 'at' e' il centro.</summary>
        GameObject Drum(string name, Vector3 at, float height, float diameter, Material m, bool solid = false) => Prim(PrimitiveType.Cylinder, name, at, new Vector3(diameter, height * 0.5f, diameter), Quaternion.identity, m, solid);

        static Text WorldText(Transform parent, string name, Vector3 at, Quaternion rot, Vector2 sizeM, string s, int px, Color col)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localRotation = rot; go.transform.localScale = Vector3.one * 0.01f;
            ((RectTransform)go.transform).sizeDelta = sizeM * 100f;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var tg = new GameObject("Testo", typeof(RectTransform));
            tg.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tg.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tg.AddComponent<Text>();
            t.font = HUD.Serif(); t.text = s; t.fontSize = px; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter; t.color = col;
            // Truncate in verticale: con Overflow la misura "adatta al riquadro" guardava solo la larghezza, e un nome su due righe ("ARGO" e "600") scendeva fuori dalla tabella
            t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 8; t.resizeTextMaxSize = px;
            return t;
        }

        // ------------------------------------------------------------------ la costruzione
        void Make()
        {
            root = transform;
            float gy = DesertWorld.GroundY;
            float xa = DesertGarage.X0 - 1.6f, xb = DesertGarage.X0 + DesertGarage.Pitch * (DesertGarage.Bays - 1) + 1.6f;
            float zN = DesertGarage.RowZ0 + 2.7f, zS = DesertGarage.RowZ0 - DesertGarage.RowPitch * (rows - 1) - 2.7f;
            float cx = (xa + xb) * 0.5f, w = xb - xa, cz = (zN + zS) * 0.5f, d = zN - zS;
            Footprint = new Rect(xa, zS, w, d);
            var concrete = Lit(new Color(0.34f, 0.33f, 0.31f), 0f, 0.22f);
            var line = Lit(new Color(0.78f, 0.76f, 0.68f), 0f, 0.2f);
            var steel = Lit(new Color(0.11f, 0.11f, 0.12f), 0.55f, 0.45f);
            var rust = Lit(new Color(0.34f, 0.17f, 0.10f), 0.35f, 0.32f);
            var rustLight = Lit(new Color(0.42f, 0.24f, 0.14f), 0.3f, 0.3f);
            var wood = Lit(new Color(0.20f, 0.13f, 0.08f), 0f, 0.18f);
            var board = Lit(new Color(0.07f, 0.06f, 0.055f), 0.2f, 0.4f);
            var rubber = Lit(new Color(0.03f, 0.03f, 0.032f), 0f, 0.12f);
            bulbOn = Glow(new Color(1f, 0.86f, 0.55f), 2.4f);
            bulbOff = Glow(new Color(1f, 0.86f, 0.55f), 0.15f);

            // la platea: un'unghia di cemento sulla sabbia (niente collider: la sabbia c'e' gia'), con le righe di ogni piazzola
            Box("Platea", new Vector3(cx, gy + 0.012f, cz), new Vector3(w, 0.05f, d), concrete);
            Box("Cordolo nord", new Vector3(cx, gy + 0.03f, zN - 0.05f), new Vector3(w, 0.07f, 0.1f), line);
            for (int r = 0; r < rows; r++)
            {
                float zr = DesertGarage.RowZ0 - DesertGarage.RowPitch * r;
                for (int k = 0; k <= DesertGarage.Bays; k++)
                {
                    float x = DesertGarage.X0 + DesertGarage.Pitch * (k - 0.5f);
                    Box("Riga", new Vector3(x, gy + 0.04f, zr), new Vector3(0.07f, 0.012f, 2.9f), line);
                }
            }

            // il tetto: due file di pali (alti a nord, bassi a sud), una lamiera ondulata in discesa verso il deserto con le nervature, e la gronda di nord con l'insegna.
            // I pali stanno sulle righe che dividono le piazzole, una riga su due, e mai davanti al muso di una moto: con i pali a passo regolare ce n'era uno in mezzo alla vista di quasi meta' delle moto,
            // e chi ci saliva si trovava davanti una colonna scura (la prova della cabina lo mostro')
            const float hN = 3.4f, hS = 2.65f;
            for (int k = 0; k <= DesertGarage.Bays; k += 2)
            {
                float x = DesertGarage.X0 + DesertGarage.Pitch * (k - 0.5f);
                postXs.Add(x);
                Box("Palo nord", new Vector3(x, gy + hN * 0.5f, zN - 0.12f), new Vector3(0.16f, hN, 0.16f), steel, true);
                Box("Palo sud", new Vector3(x, gy + hS * 0.5f, zS + 0.12f), new Vector3(0.16f, hS, 0.16f), steel, true);
            }
            float slope = Mathf.Atan2(hN - hS, d) * Mathf.Rad2Deg;                          // positivo: a sud piu' basso
            float len = Mathf.Sqrt(d * d + (hN - hS) * (hN - hS)) + 0.9f;
            var tilt = Quaternion.Euler(slope, 0f, 0f);
            var roofC = new Vector3(cx, gy + (hN + hS) * 0.5f + 0.08f, cz);
            Box("Lamiera", roofC, new Vector3(w + 0.9f, 0.07f, len), tilt, rust);
            for (int i = 0; i < (int)(w / 0.55f); i++)
            {
                float x = xa - 0.35f + i * 0.55f;
                Box("Nervatura", roofC + new Vector3(x - cx, 0.06f, 0f), new Vector3(0.09f, 0.05f, len), tilt, rustLight);
            }
            Box("Gronda", new Vector3(cx, gy + hN - 0.22f, zN + 0.34f), new Vector3(w + 0.9f, 0.55f, 0.1f), board);
            var fascia = WorldText(root, "Insegna", new Vector3(cx, gy + hN - 0.22f, zN + 0.395f), Quaternion.Euler(0f, 180f, 0f), new Vector2(11f, 0.52f), "GARAGE  ·  BOZZO MOTO", 44, Sign);
            fascia.fontStyle = FontStyle.Bold;

            // le lampade sotto la lamiera: un'asta, un paralume e la lampadina (si accende di notte)
            for (int i = 0; i < 5; i++)
            {
                float x = Mathf.Lerp(xa + 2f, xb - 2f, i / 4f);
                float y = gy + Mathf.Lerp(hN, hS, 0.5f) - 0.5f;
                Box("Asta", new Vector3(x, y + 0.34f, cz), new Vector3(0.025f, 0.55f, 0.025f), steel);
                Drum("Paralume", new Vector3(x, y + 0.04f, cz), 0.12f, 0.34f, steel);
                var bulb = Prim(PrimitiveType.Sphere, "Lampadina", new Vector3(x, y - 0.04f, cz), Vector3.one * 0.13f, Quaternion.identity, bulbOff, false);
                bulbs.Add(bulb.GetComponent<Renderer>());
                var lg = new GameObject("Luce");
                lg.transform.SetParent(root, false); lg.transform.localPosition = new Vector3(x, y - 0.25f, cz);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.range = 13f; l.intensity = 2.6f; l.color = new Color(1f, 0.82f, 0.55f); l.shadows = LightShadows.None; l.enabled = false;
                lamps.Add(l);
            }

            // il totem a sinistra, verso la scalinata: un palo alto con l'insegna sui due lati e una lampada sopra
            float tx = xa - 1.2f, tz = DesertGarage.RowZ0 + 1.4f;
            // l'insegna e' piu' spessa del palo (0,30 contro 0,22 m): con l'insegna sottile il palo sporgeva davanti alle facce e copriva le lettere di mezzo ("GAR AGE" nella foto)
            Box("Totem", new Vector3(tx, gy + 2.2f, tz), new Vector3(0.22f, 4.4f, 0.22f), steel, true);
            Box("Insegna totem", new Vector3(tx, gy + 3.45f, tz), new Vector3(0.30f, 1.7f, 1.45f), board);
            WorldText(root, "Insegna totem ovest", new Vector3(tx - 0.155f, gy + 3.45f, tz), Quaternion.Euler(0f, 90f, 0f), new Vector2(1.35f, 1.55f), "GARAGE\nBozzo\nMoto", 30, Sign);
            WorldText(root, "Insegna totem est", new Vector3(tx + 0.155f, gy + 3.45f, tz), Quaternion.Euler(0f, -90f, 0f), new Vector2(1.35f, 1.55f), "GARAGE\nBozzo\nMoto", 30, Sign);
            var tb = Prim(PrimitiveType.Sphere, "Lampadina totem", new Vector3(tx, gy + 4.5f, tz), Vector3.one * 0.2f, Quaternion.identity, bulbOff, false);
            bulbs.Add(tb.GetComponent<Renderer>());
            var tlg = new GameObject("Luce totem");
            tlg.transform.SetParent(root, false); tlg.transform.localPosition = new Vector3(tx, gy + 4.3f, tz);
            var tl = tlg.AddComponent<Light>();
            tl.type = LightType.Point; tl.range = 11f; tl.intensity = 2.2f; tl.color = new Color(1f, 0.82f, 0.55f); tl.shadows = LightShadows.None; tl.enabled = false;
            lamps.Add(tl);

            // il cartello di ogni piazzola, dietro la moto (dal lato della scalinata) e girato verso il deserto: lo legge chi arriva davanti alla moto, che e' da dove ci si sale. Il testo sta sulla faccia di sud
            // della tabella con la rotazione di fabbrica: una tela di uGUI nel mondo si legge dal suo lato -z, cioe' da sud (con mezzo giro il nome si vedeva allo specchio, la prima prova a occhio lo mostro')
            for (int k = 0; k < DesertGarage.Bays; k++)
            {
                float x = DesertGarage.X0 + DesertGarage.Pitch * k;
                float z = DesertGarage.RowZ0 + 1.85f;
                // la tabella sta a un metro e tre quarti, sopra la sagoma della moto vista da davanti. Il paletto finisce sotto la tabella (con la punta dentro le lettere ne copriva quella di mezzo
                // della seconda riga: "ARGO 6 0" nella foto)
                Box("Paletto", new Vector3(x, gy + 0.805f, z), new Vector3(0.06f, 1.61f, 0.06f), steel);
                Box("Tabella", new Vector3(x, gy + 1.78f, z), new Vector3(0.92f, 0.34f, 0.04f), board);
                names.Add(WorldText(root, "Nome " + (k + 1), new Vector3(x, gy + 1.78f, z - 0.025f), Quaternion.identity, new Vector2(0.88f, 0.30f), "", 26, Sign));
            }

            // l'officina in fondo a destra: il banco con la morsa e la cassetta, tre barili, una pila di gomme e due taniche
            float bx = xb + 1.5f, bz = DesertGarage.RowZ0 + 0.4f;
            Box("Banco", new Vector3(bx, gy + 0.46f, bz), new Vector3(2.4f, 0.1f, 0.9f), wood, true);
            foreach (float lx in new[] { -1.05f, 1.05f }) foreach (float lz in new[] { -0.35f, 0.35f }) Box("Gamba", new Vector3(bx + lx, gy + 0.22f, bz + lz), new Vector3(0.07f, 0.44f, 0.07f), steel);
            Box("Morsa", new Vector3(bx - 0.8f, gy + 0.6f, bz), new Vector3(0.18f, 0.18f, 0.22f), steel);
            Box("Cassetta", new Vector3(bx + 0.5f, gy + 0.62f, bz + 0.12f), new Vector3(0.5f, 0.2f, 0.24f), Lit(new Color(0.5f, 0.07f, 0.05f), 0.2f, 0.4f));
            Box("Ripiano", new Vector3(bx, gy + 1.5f, bz + 0.4f), new Vector3(2.4f, 0.05f, 0.3f), wood);
            Box("Parete", new Vector3(bx, gy + 1.1f, bz + 0.58f), new Vector3(2.5f, 2.2f, 0.06f), wood, true);
            var barrels = new[] { Lit(new Color(0.5f, 0.07f, 0.05f), 0.3f, 0.4f), Lit(new Color(0.10f, 0.20f, 0.38f), 0.3f, 0.4f), rust };
            for (int i = 0; i < 3; i++) Drum("Barile", new Vector3(bx + 2.1f, gy + 0.46f, bz - 0.5f + i * 0.62f), 0.92f, 0.56f, barrels[i], true);
            for (int i = 0; i < 4; i++) Drum("Gomma", new Vector3(xa - 0.4f, gy + 0.09f + i * 0.17f, DesertGarage.RowZ0 + 0.9f), 0.17f, 0.62f, rubber);
            var jerry = Lit(new Color(0.18f, 0.28f, 0.12f), 0.15f, 0.35f);
            Box("Tanica", new Vector3(bx - 1.0f, gy + 0.14f, bz - 0.7f), new Vector3(0.32f, 0.5f, 0.17f), jerry);
            Box("Tanica", new Vector3(bx - 0.6f, gy + 0.14f, bz - 0.74f), new Vector3(0.32f, 0.5f, 0.17f), jerry);

            DesertGarage.Bought += OnBought;
            RefreshNames();
        }

        // ------------------------------------------------------------------ nomi e luci
        void OnBought(int seat, DesertBikeDef d) => RefreshNames();

        /// <summary>Scrive sui cartelli i nomi delle moto del primo posto (quello della prima fila): le piazzole oltre l'ultima moto restano spente.</summary>
        public void RefreshNames()
        {
            var owned = DesertGarage.Owned(0);
            owned.RemoveAll(d => d.IsPlane);
            for (int k = 0; k < names.Count; k++)
            {
                bool has = k < owned.Count;
                var t = names[k];
                if (t == null) continue;
                t.text = has ? owned[k].name.ToUpperInvariant() : "libera";
                t.color = has ? Sign : new Color(Sign.r, Sign.g, Sign.b, 0.22f);
            }
        }

        void Update()
        {
            tickT -= Time.deltaTime;
            if (tickT > 0f) return;
            tickT = 0.5f;
            var sky = DesertSky.I;
            bool dark = sky != null && sky.Night > 0.28f;
            if (dark == lit) return;
            lit = dark;
            foreach (var l in lamps) if (l != null) l.enabled = dark;
            foreach (var b in bulbs) if (b != null) b.sharedMaterial = dark ? bulbOn : bulbOff;
        }

        /// <summary>Una moto e' appena arrivata in una piazzola: un velo di polvere, un tonfo e il suono di un acquisto.</summary>
        public static void Delivered(Vector3 at)
        {
            var sky = DesertSky.I;
            float day = sky != null ? Mathf.Lerp(0.16f, 1f, sky.State.daylight) * (1f - 0.5f * sky.Night) : 1f;
            var tint = new Color(0.78f * day, 0.62f * day, 0.42f * day);
            for (int i = 0; i < 16; i++)
            {
                var v = new Vector3(Random.Range(-1f, 1f), Random.Range(0.3f, 1.2f), Random.Range(-1f, 1f)) * 1.6f;
                Fx.Particle(0, false, at + Vector3.up * 0.1f, v, Vector3.zero, 0.2f, 1.0f + 0.6f * Random.value, 1.0f + 0.5f * Random.value, tint, 0.25f);
            }
            Sfx.At(SoundBank.BikeCrash, at + Vector3.up * 0.3f, 0.45f, 0.8f, 3f, 70f, 1f);
            Sfx.At(SoundBank.GrimorioNodo, at + Vector3.up * 1.0f, 0.7f, 1.1f, 3f, 70f, 1f);
        }
    }
}

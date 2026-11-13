using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Armi appese ai muri stile Call of Duty Zombies: sagoma di gesso, prezzo in punti,
    /// acquisto con F e ricarica della riserva riacquistando la stessa arma.</summary>
    public class WallBuy : MonoBehaviour
    {
        public int weaponIndex;
        public int price;
        public string displayName;
        public Color tint = new Color(0.30f, 0.95f, 1f);

        static readonly List<WallBuy> all = new List<WallBuy>();
        static WallBuy near;
        static Transform lastCamera;
        Light glow;

        public int AmmoPrice => Mathf.Max(150, Mathf.RoundToInt(price * 0.5f));

        /// <summary>Costruisce i tre espositori (chiamato dal GameManager all'avvio).</summary>
        public static void BuildAll()
        {
            all.RemoveAll(w => w == null);   // sopravvive al ricaricamento della scena in editor
            near = null;
            if (all.Count > 0) return;
            // sud: a sinistra dell'ingresso, ovest: accanto al salotto, est: sotto le vetrate
            int v = WeaponDefs.IndexOf("smg_vesper9"), br = WeaponDefs.IndexOf("sg_breacher12"), la = WeaponDefs.IndexOf("dmr_lance7");
            Create(v, WeaponDefs.All[v].price, new Vector3(-10f, 1.55f, 22.42f), Quaternion.Euler(0f, 180f, 0f), WeaponDefs.All[v].GradeColor);
            Create(br, WeaponDefs.All[br].price, new Vector3(-34.42f, 1.55f, 12f), Quaternion.Euler(0f, 90f, 0f), WeaponDefs.All[br].GradeColor);
            Create(la, WeaponDefs.All[la].price, new Vector3(34.42f, 1.55f, 14f), Quaternion.Euler(0f, -90f, 0f), WeaponDefs.All[la].GradeColor);
        }

        /// <summary>Piazza un espositore (usato dai piani procedurali): indice dell'arma, prezzo, posizione e rotazione (il davanti guarda la stanza).</summary>
        public static WallBuy Place(int index, int cost, Vector3 pos, Quaternion rot, Color tint)
        {
            all.RemoveAll(w => w == null);
            return Create(index, cost, pos, rot, tint);
        }

        public static void ClearAll()
        {
            foreach (var w in all) if (w != null) Destroy(w.gameObject);
            all.Clear();
            near = null;
        }

        static WallBuy Create(int index, int cost, Vector3 pos, Quaternion rot, Color tint)
        {
            var def = WeaponDefs.All[index];
            var go = new GameObject("Muro_" + def.name);
            go.transform.position = pos;
            go.transform.rotation = rot;

            var metal = Lit(new Color(0.20f, 0.22f, 0.26f), 0.50f, 0.28f);
            var trim = Lit(new Color(0.24f, 0.18f, 0.09f), 0.95f, 0.35f);

            // pannello + cornice
            Box(go, "Pannello", new Vector3(1.32f, 0.86f, 0.06f), Vector3.zero, metal);
            Box(go, "Cornice", new Vector3(1.38f, 0.92f, 0.02f), new Vector3(0f, 0f, -0.02f), trim);

            // sagoma di gesso (fronte del pannello)
            var chalk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chalk.name = "Gesso";
            NoCol(chalk);
            chalk.transform.SetParent(go.transform, false);
            chalk.transform.localPosition = new Vector3(0f, 0f, 0.035f);
            chalk.transform.localScale = new Vector3(1.26f, 0.80f, 0.012f);
            var chalkTex = ChalkTex(def);
            var chalkMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            chalkMat.color = Color.white;
            chalkMat.SetTexture("_BaseMap", chalkTex);
            chalkMat.SetTexture("_MainTex", chalkTex);
            chalk.GetComponent<MeshRenderer>().material = chalkMat;

            // arma vera e propria, parallela al muro
            var gun = BuildWallGun(index);
            gun.transform.SetParent(go.transform, false);
            gun.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            gun.transform.localScale = Vector3.one;
            FitOnPanel(gun, go.transform);

            // striscia luminosa + luce di richiamo
            var stripMat = Lit(new Color(0.02f, 0.03f, 0.04f), 0.2f, 0.4f);
            stripMat.EnableKeyword("_EMISSION");
            stripMat.SetColor("_EmissionColor", tint * 1.0f);
            stripMat.SetFloat("_EmissiveIntensity", 0.9f);
            Box(go, "Striscia", new Vector3(0.035f, 0.80f, 0.02f), new Vector3(-0.70f, 0f, 0.04f), stripMat);

            // luce calda e discreta: il colore identificativo resta sulla striscia
            var lightGo = new GameObject("Luce");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.2f, 1.15f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.intensity = 0.85f;
            light.range = 4.2f;
            light.shadows = LightShadows.None;

            // nome e prezzo incisi sul pannello
            var t1 = MakeText(go.transform, def.name, Color.Lerp(new Color(0.93f, 0.89f, 0.80f), def.GradeColor, 0.55f), -0.300f, 0.055f, 0.0135f);
            var t2 = MakeText(go.transform, (cost > 0 ? cost + " PUNTI" : "MUNIZIONI") + "  ·  GRADO " + HUD.Roman(def.grade), def.GradeColor, -0.385f, 0.055f, 0.0105f);

            var wb = go.AddComponent<WallBuy>();
            wb.texts = new[] { t1, t2 };
            wb.weaponIndex = index;
            wb.price = cost;
            wb.displayName = def.name;
            wb.tint = tint;
            wb.glow = light;
            all.Add(wb);
            return wb;
        }

        /// <summary>Lo shader di font di Unity (GUI/Text Shader) disegna con ZTest Always: il nome e il prezzo dell'arma si leggevano, specchiati e
        /// accesi nel buio, attraverso i muri e da dietro il pannello. Qui si spengono quando il pannello non si vede davvero: da dietro, oltre 14 m
        /// o con un ostacolo (muro, colonna, porta) tra l'occhio e la fronte del pannello.</summary>
        MeshRenderer[] texts;
        float textT;
        bool textsOn = true;

        void Update()
        {
            textT -= Time.unscaledDeltaTime;
            if (textT > 0f || texts == null) return;
            textT = 0.15f;
            Transform cam = lastCamera != null ? lastCamera : (Camera.main != null ? Camera.main.transform : null);
            if (cam == null) return;
            Vector3 to = cam.position - transform.position;
            bool show = to.sqrMagnitude < 14f * 14f && Vector3.Dot(to, transform.forward) > 0.05f &&
                        !Physics.Linecast(cam.position, transform.position + transform.forward * 0.3f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
            if (show == textsOn) return;
            textsOn = show;
            foreach (var r in texts) if (r != null) r.enabled = show;
        }

        static MeshRenderer MakeText(Transform parent, string s, Color c, float y, float z, float size)
        {
            // il font di sistema dinamico funziona con la UI ma non sempre con TextMesh:
            // uso il font legacy integrato e assegno esplicitamente il materiale al renderer
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Georgia", "Palatino Linotype", "Book Antiqua", "Times New Roman" }, 48);
            var go = new GameObject("Testo_" + s);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, z);
            // il pannello guarda la stanza: testo ruotato di 180 rispetto al rack
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = s;
            tm.font = font;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.characterSize = size;   // altezza in metri (0.0135 ≈ 8,5 cm)
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = c;
            var mr = go.GetComponent<MeshRenderer>();
            if (font != null && font.material != null) mr.sharedMaterial = font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }

        // ---------- modello decorativo (canna lungo +Z) ----------
        // riusa la geometria dettagliata del viewmodel e i materiali PBR procedurali
        static GameObject BuildWallGun(int index) => Weapon.BuildDisplayModel(index);

        static void NoCol(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
        }

        static void Box(GameObject parent, string name, Vector3 size, Vector3 pos, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            NoCol(b);
            b.transform.SetParent(parent.transform, false);
            b.transform.localPosition = pos;
            b.transform.localScale = size;
            b.GetComponent<MeshRenderer>().material = m;
        }

        static void Cyl(GameObject parent, string name, float radius, float length, Vector3 pos, Material m)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = name;
            NoCol(c);
            c.transform.SetParent(parent.transform, false);
            c.transform.localPosition = pos;
            c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            c.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            c.GetComponent<MeshRenderer>().material = m;
        }

        static Material Lit(Color c, float metallic, float smooth)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        // ---------- sagoma di gesso: cornice con gli angoli ----------
        static Texture2D ChalkTex(WeaponDef d)
        {
            const int W = 512, H = 320;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            var bg = new Color32(13, 17, 22, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            var chalk = new Color32(228, 238, 250, 255);
            void Dot(int x, int y) { if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = chalk; }
            void HLine(int x0, int x1, int y) { for (int x = x0; x <= x1; x++) { Dot(x, y); Dot(x, y + 1); } }
            void VLine(int x, int y0, int y1) { for (int y = y0; y <= y1; y++) { Dot(x, y); Dot(x + 1, y); } }
            int m = 16, L = 54;
            foreach (int sx in new[] { 0, 1 })
                foreach (int sy in new[] { 0, 1 })
                {
                    int x = sx == 0 ? m : W - m - 1, y = sy == 0 ? m : H - m - 1;
                    int dx = sx == 0 ? 1 : -1, dy = sy == 0 ? 1 : -1;
                    HLine(Mathf.Min(x, x + dx * L), Mathf.Max(x, x + dx * L), y);
                    VLine(x, Mathf.Min(y, y + dy * L), Mathf.Max(y, y + dy * L));
                }
            for (int i = 0; i < 900; i++)
            {
                int x = Random.Range(0, W), y = Random.Range(0, H);
                int k = y * W + x;
                if (px[k].r > 100) px[k] = new Color32(228, 238, 250, (byte)Random.Range(120, 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        /// <summary>Scala e centra il modello nel pannello (1,26 x 0,80 m, lato lungo orizzontale), un po' davanti al muro.</summary>
        static void FitOnPanel(GameObject gun, Transform panel)
        {
            var rs = gun.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Vector3 size = panel.InverseTransformVector(b.size);
            float length = Mathf.Abs(size.x), height = Mathf.Abs(size.y);
            float k = Mathf.Min(1.0f, Mathf.Min(1.12f / Mathf.Max(0.05f, length), 0.64f / Mathf.Max(0.05f, height)));
            gun.transform.localScale = Vector3.one * k;
            b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Vector3 target = panel.TransformPoint(new Vector3(0f, -0.01f, 0.13f));
            gun.transform.position += target - b.center;
        }

        // ---------- interazione ----------
        public static WallBuy Find(Transform cam)
        {
            if (cam == null) return null;
            WallBuy best = null;
            float bd = float.MaxValue;
            Vector3 cp = cam.position, fwd = cam.forward;
            foreach (var w in all)
            {
                if (w == null) continue;
                Vector3 to = w.transform.position - cp;
                float d = to.magnitude;
                if (d > 3.4f || d >= bd) continue;
                if (Vector3.Dot(fwd, to / Mathf.Max(d, 0.01f)) < 0.55f) continue;
                if (!Proc.Interact.Visible(cam, w.transform.position, w.transform)) continue;
                best = w; bd = d;
            }
            return best;
        }

        /// <summary>Vero se si guarda un espositore da vicino (e il prompt e' il suo).</summary>
        public static bool Tick(Transform cam)
        {
            var gm = GameManager.I;
            lastCamera = cam;
            if (gm == null || gm.player == null || gm.hud == null) return false;
            near = Find(cam);
            if (near == null)
            {
                gm.hud.SetPrompt("");
                foreach (var w in all) if (w != null && w.glow != null) w.glow.intensity = 1.1f;
                return false;
            }
            var weapon = gm.player.weapon;
            int sl = weapon.SlotOf(near.weaponIndex);
            bool same = sl >= 0;
            bool full = same && weapon.ReserveOf(sl) >= weapon.ReserveMaxOf(sl);
            int cost = same ? near.AmmoPrice : near.price;
            if (full)
                gm.hud.SetPrompt(near.displayName + " — riserva al massimo");
            else
            {
                string extra = "";
                if (!same && weapon.WeaponInSlot(0) >= 0 && weapon.WeaponInSlot(1) >= 0)
                    extra = "   (sostituisce " + WeaponDefs.All[weapon.weaponIndex].name + ")";
                gm.hud.SetPrompt((same ? "MUNIZIONI " : "ACQUISTA ") + near.displayName
                    + " — " + cost + " PUNTI   [F]" + extra);
            }
            foreach (var w in all)
                if (w != null && w.glow != null) w.glow.intensity = w == near ? 2.4f + Mathf.Sin(Time.time * 3.2f) * 0.5f : 1.0f;
            return true;
        }

        public static void TryBuy()
        {
            var gm = GameManager.I;
            if (GameMenu.Blocked || near == null || gm == null || gm.player == null || Find(lastCamera) != near) return;
            var weapon = gm.player.weapon;
            int sl = weapon.SlotOf(near.weaponIndex);
            bool same = sl >= 0;
            bool full = same && weapon.ReserveOf(sl) >= weapon.ReserveMaxOf(sl);
            if (same && full) { gm.hud?.Toast("Riserva già al massimo"); return; }
            int cost = same ? near.AmmoPrice : near.price;
            if (gm.score < cost)
            {
                gm.hud?.Toast("Punti insufficienti: " + cost);
                Synth.Empty();
                return;
            }
            gm.AddPoints(-cost);
            if (same)
            {
                weapon.FillReserve(sl);
                gm.hud?.Toast("Munizioni " + near.displayName + " ripristinate — −" + cost + " punti");
            }
            else
            {
                weapon.Equip(near.weaponIndex);
                gm.hud?.Toast(WeaponDefs.All[near.weaponIndex].name + " acquisita — −" + cost + " punti");
            }
            Synth.Buy();
        }
    }
}

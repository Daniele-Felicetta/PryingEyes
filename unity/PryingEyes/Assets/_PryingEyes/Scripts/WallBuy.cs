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
        Light glow;

        public int AmmoPrice => Mathf.RoundToInt(price * 0.5f);

        /// <summary>Costruisce i tre espositori (chiamato dal GameManager all'avvio).</summary>
        public static void BuildAll()
        {
            all.RemoveAll(w => w == null);   // sopravvive al ricaricamento della scena in editor
            near = null;
            if (all.Count > 0) return;
            var cyan = new Color(0.30f, 0.95f, 1f);
            var orange = new Color(1f, 0.62f, 0.22f);
            var violet = new Color(0.70f, 0.55f, 1f);
            // sud: a sinistra dell'ingresso, verso la stanza
            Create(1, 500, new Vector3(-10f, 1.55f, 22.42f), Quaternion.Euler(0f, 180f, 0f), cyan);
            // ovest: accanto al salotto, sopra il carrello
            Create(2, 1200, new Vector3(-34.42f, 1.55f, 12f), Quaternion.Euler(0f, 90f, 0f), orange);
            // est: sotto le vetrate, vicino alla reception
            Create(3, 1800, new Vector3(34.42f, 1.55f, 14f), Quaternion.Euler(0f, -90f, 0f), violet);
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
            var def = Weapon.Defs[index];
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
            var chalkTex = ChalkTex(Weapon.Defs[index].model);
            var chalkMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            chalkMat.color = Color.white;
            chalkMat.SetTexture("_BaseMap", chalkTex);
            chalkMat.SetTexture("_MainTex", chalkTex);
            chalk.GetComponent<MeshRenderer>().material = chalkMat;

            // arma vera e propria, parallela al muro
            var gun = BuildWallGun(index);
            gun.transform.SetParent(go.transform, false);
            gun.transform.localPosition = new Vector3(0f, -0.01f, 0.14f);
            gun.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            gun.transform.localScale = Vector3.one * 0.82f;

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
            MakeText(go.transform, def.name, new Color(0.93f, 0.89f, 0.80f), -0.300f, 0.055f, 0.0135f);
            MakeText(go.transform, cost + " PUNTI", tint, -0.385f, 0.055f, 0.0115f);

            var wb = go.AddComponent<WallBuy>();
            wb.weaponIndex = index;
            wb.price = cost;
            wb.displayName = def.name;
            wb.tint = tint;
            wb.glow = light;
            all.Add(wb);
            return wb;
        }

        static void MakeText(Transform parent, string s, Color c, float y, float z, float size)
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

        // ---------- sagoma di gesso disegnata a pixel ----------
        static Texture2D ChalkTex(string model)
        {
            const int W = 512, H = 320;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            var bg = new Color32(13, 17, 22, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            var chalk = new Color32(228, 238, 250, 255);

            void Dot(int x, int y)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return;
                px[y * W + x] = chalk;
            }
            void Line(int x0, int y0, int x1, int y1)
            {
                if (y0 == y1) for (int x = Mathf.Min(x0, x1); x <= Mathf.Max(x0, x1); x++) { Dot(x, y0); Dot(x, y0 + 1); }
                else for (int y = Mathf.Min(y0, y1); y <= Mathf.Max(y0, y1); y++) { Dot(x0, y); Dot(x0 + 1, y); }
            }
            void Outline(int x0, int y0, int x1, int y1)
            {
                Line(x0, y0, x1, y0); Line(x0, y1, x1, y1);
                Line(x0, y0, x0, y1); Line(x1, y0, x1, y1);
            }

            // Texture2D ha l'origine in basso a sinistra: +y = verso l'alto.
            const int mid = 165;
            if (model == "smg")
            {
                Outline(120, mid - 34, 330, mid + 34);
                Outline(330, mid - 8, 430, mid + 14);
                Outline(252, mid - 96, 286, mid - 34);   // caricatore sotto
                Outline(205, mid - 92, 238, mid - 34);   // impugnatura sotto
                Line(120, mid - 6, 72, mid + 8); Line(72, mid + 8, 72, mid + 22); Line(72, mid + 22, 120, mid + 18);
                Outline(250, mid + 40, 300, mid + 56);   // mirino sopra
            }
            else if (model == "shotgun")
            {
                Outline(58, mid - 30, 150, mid + 34);
                Outline(150, mid + 4, 460, mid + 24);    // canna alta
                Outline(150, mid - 22, 432, mid - 6);    // tubo sotto
                Outline(258, mid - 50, 340, mid - 22);   // pompa
                Outline(205, mid - 94, 240, mid - 24);
                Outline(460, mid - 26, 500, mid + 32);
            }
            else
            {
                Outline(55, mid - 26, 180, mid + 30);
                Outline(180, mid + 2, 486, mid + 20);    // canna
                Outline(180, mid - 20, 476, mid - 8);    // fusto
                Outline(225, mid - 94, 262, mid - 22);   // caricatore
                Outline(230, mid + 30, 345, mid + 62);   // ottica
                Outline(240, mid + 62, 338, mid + 76);
                Outline(486, mid - 24, 508, mid + 26);
            }

            // granulosità del gesso
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

        // ---------- interazione ----------
        public static WallBuy Find(Transform cam)
        {
            WallBuy best = null;
            float bd = float.MaxValue;
            Vector3 cp = cam.position, fwd = cam.forward;
            foreach (var w in all)
            {
                if (w == null) continue;
                Vector3 to = w.transform.position - cp;
                float d = to.magnitude;
                if (d > 3.4f || d >= bd) continue;
                if (Vector3.Dot(fwd, to / d) < 0.55f) continue;
                best = w; bd = d;
            }
            return best;
        }

        /// <summary>Vero se si guarda un espositore da vicino (e il prompt e' il suo).</summary>
        public static bool Tick(Transform cam)
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null || gm.hud == null) return false;
            near = Find(cam);
            if (near == null)
            {
                gm.hud.SetPrompt("");
                foreach (var w in all) if (w != null && w.glow != null) w.glow.intensity = 1.1f;
                return false;
            }
            var weapon = gm.player.weapon;
            bool same = weapon.CurrentIndex == near.weaponIndex;
            bool full = same && weapon.reserve >= weapon.reserveMax;
            int cost = same ? near.AmmoPrice : near.price;
            if (full)
                gm.hud.SetPrompt(near.displayName + " — riserva al massimo");
            else
                gm.hud.SetPrompt((same ? "MUNIZIONI " : "ACQUISTA ") + near.displayName
                    + " — " + cost + " PUNTI   [F]");
            foreach (var w in all)
                if (w != null && w.glow != null) w.glow.intensity = w == near ? 2.4f + Mathf.Sin(Time.time * 3.2f) * 0.5f : 1.0f;
            return true;
        }

        public static void TryBuy()
        {
            var gm = GameManager.I;
            if (near == null || gm == null || gm.player == null) return;
            var weapon = gm.player.weapon;
            bool same = weapon.CurrentIndex == near.weaponIndex;
            bool full = same && weapon.reserve >= weapon.reserveMax;
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
                weapon.reserve = weapon.reserveMax;
                gm.hud?.SetAmmo(weapon.mag, weapon.reserve);
                gm.hud?.Toast("Munizioni " + near.displayName + " ripristinate — −" + cost + " punti");
            }
            else
            {
                weapon.Equip(near.weaponIndex);
                gm.hud?.Toast(Weapon.Defs[near.weaponIndex].name + " acquisita — −" + cost + " punti");
            }
            Synth.Buy();
        }
    }
}

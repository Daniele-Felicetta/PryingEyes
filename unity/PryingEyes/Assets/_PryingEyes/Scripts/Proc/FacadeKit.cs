using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>I materiali e le abitudini di una facciata del deserto: il muro (il materiale del kit, tinto per sito), le rifiniture di pietra, i tetti (tegole,
    /// ardesia, piombo, catrame), le persiane, i vetri, i sassi dei crolli. Gli edifici del deserto si vedono da fuori e da lontano: le stanze dei piani hanno
    /// soffitti che guardano solo in basso e muri che guardano solo dentro, quindi l'esterno lo disegna <see cref="FloorBuilder"/> (FloorBuilder.Facade.cs) con questi.</summary>
    public sealed class FacadeStyle
    {
        public string name;
        public Material wall, trim, plinth, roofFlat, roofPitch, parapet, shutter, glass, glassLit, metal, rubble;
        public bool shutters, pilasters, buttresses, blind;
        public float plinthH = 0.55f;
    }

    public static class FacadeKit
    {
        static readonly Dictionary<int, FacadeStyle> cache = new Dictionary<int, FacadeStyle>();
        static Material litGlass;
        static readonly List<Object> owned = new List<Object>();

        // le tinte dei muri (si moltiplicano al colore del materiale del kit): sabbia, ocra, terracotta, calce, azzurro slavato, oliva
        static readonly Color[] Plaster =
        {
            new Color(1.00f, 0.88f, 0.70f), new Color(0.96f, 0.78f, 0.52f), new Color(0.94f, 0.66f, 0.56f),
            new Color(1.05f, 1.02f, 0.97f), new Color(0.72f, 0.82f, 0.92f), new Color(0.82f, 0.84f, 0.62f),
        };
        // la pietra del kit e' scura (albedo 0.12 in lineare, fatta per il buio dei corridoi): al sole del deserto una chiesa era un blocco nero accanto alla sabbia. Qui si schiarisce
        // (circa 2 volte: pietra calcarea sbiadita, 0.25) con le stesse tinte di prima; l'interno usa il materiale del kit com'e'.
        const float StoneGain = 2.05f;
        static readonly Color[] Stone =
        {
            new Color(1.10f * StoneGain, 1.00f * StoneGain, 0.88f * StoneGain), new Color(0.90f * StoneGain, 0.95f * StoneGain, 1.00f * StoneGain),
            new Color(0.68f * StoneGain, 0.68f * StoneGain, 0.72f * StoneGain), new Color(1.20f * StoneGain, 1.02f * StoneGain, 0.78f * StoneGain),
        };
        static readonly Color[] Cold =
        {
            new Color(1.00f, 1.00f, 1.00f), new Color(1.10f, 1.06f, 0.82f), new Color(0.86f, 0.96f, 1.08f),
        };
        // l'intonaco nero del teatro ha un albedo di 0.12: all'aperto una casa o un teatro erano scatole nere. Raddoppiato: carbone, non buio (l'interno non cambia)
        const float DarkGain = 2.0f;
        static readonly Color[] Dark =
        {
            new Color(1.00f * DarkGain, 1.00f * DarkGain, 1.00f * DarkGain), new Color(1.9f * DarkGain, 0.9f * DarkGain, 0.9f * DarkGain), new Color(0.95f * DarkGain, 1.12f * DarkGain, 1.30f * DarkGain),
        };

        static Material Tinted(string kitName, Color tint, string suffix)
        {
            var src = ProcKit.Mat(kitName);
            var m = new Material(src) { name = kitName + "_" + suffix };
            string prop = m.HasProperty("_BaseColor") ? "_BaseColor" : m.HasProperty("_Color") ? "_Color" : null;
            if (prop != null)
            {
                var c = src.GetColor(prop);
                m.SetColor(prop, new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a));
            }
            owned.Add(m);
            return m;
        }

        /// <summary>Lo stile della facciata di un tipo di stanza (teatro, sanatorio, albergo, chiesa) con la tavolozza di un sito (0..n). Si fa una volta sola per combinazione.</summary>
        public static FacadeStyle For(Theme theme, bool church, int palette)
        {
            palette = palette < 0 ? -palette : palette;
            int key = ((int)theme * 2 + (church ? 1 : 0)) * 64 + (palette & 63);
            if (cache.TryGetValue(key, out var st)) return st;
            st = new FacadeStyle { name = $"{theme}{(church ? "/chiesa" : "")}/{palette}" };
            string sfx = "f" + key;
            if (church)
            {
                var t = Stone[palette % Stone.Length];
                st.wall = Tinted("mur_pietra", t, sfx);
                st.trim = Tinted("cornice_pietra", t * 1.08f, sfx);
                st.plinth = Tinted("mur_pietra", t * 0.62f, sfx + "z");
                st.parapet = st.trim;
                st.roofPitch = Slate(new Color(0.34f, 0.37f, 0.40f), palette);
                st.roofFlat = Lead();
                st.shutters = false; st.pilasters = false; st.buttresses = true; st.blind = true;
                st.plinthH = 0.8f;
            }
            else if (theme == Theme.Teatro)
            {
                var t = Dark[palette % Dark.Length];
                st.wall = Tinted("mur_intonaco_nero", t, sfx);
                st.trim = Tinted("cornice_pietra", new Color(1.25f, 1.0f, 0.62f), sfx);
                st.plinth = Tinted("mur_pietra", new Color(0.4f, 0.38f, 0.38f), sfx + "z");
                st.parapet = st.trim;
                st.roofPitch = Slate(new Color(0.30f, 0.20f, 0.20f), palette);
                st.roofFlat = Tar();
                st.shutters = false; st.pilasters = true; st.buttresses = false; st.blind = true;
            }
            else if (theme == Theme.Sanatorio)
            {
                var t = Cold[palette % Cold.Length];
                st.wall = Tinted("mur_intonaco_freddo", t, sfx);
                st.trim = Tinted("cornice_pietra", new Color(1.1f, 1.1f, 1.08f), sfx);
                st.plinth = Tinted("mur_pietra", new Color(0.55f, 0.58f, 0.58f), sfx + "z");
                st.parapet = st.trim;
                st.roofPitch = Tiles(new Color(0.55f, 0.30f, 0.24f), palette);
                st.roofFlat = Tar();
                st.shutters = false; st.pilasters = true; st.buttresses = false; st.blind = true;
            }
            else
            {
                var t = Plaster[palette % Plaster.Length];
                st.wall = Tinted("mur_intonaco", t, sfx);
                st.trim = Tinted("cornice_pietra", new Color(1.1f, 1.04f, 0.92f), sfx);
                st.plinth = Tinted("mur_pietra", new Color(0.62f, 0.55f, 0.48f), sfx + "z");
                st.parapet = Tinted("mur_intonaco", t * 0.94f, sfx + "p");
                st.roofPitch = Tiles(new Color(0.72f, 0.36f, 0.24f), palette);
                st.roofFlat = Tar();
                st.shutters = (palette % 3) != 2; st.pilasters = true; st.buttresses = false; st.blind = true;
            }
            st.shutter = Shutter(palette);
            st.glass = Glass();
            st.glassLit = LitGlass();
            st.metal = Plain("metallo", new Color(0.18f, 0.17f, 0.16f), 0.45f, 0.7f);
            st.rubble = Tinted("mur_pietra", new Color(0.7f, 0.64f, 0.56f) * 1.7f, sfx + "r");
            cache[key] = st;
            return st;
        }

        static Material sandMat;

        /// <summary>La sabbia accumulata contro i muri (FloorBuilder.Drift): lo shader della sabbia del terreno (stessi colori, stesse increspature, stessa ombra delle nuvole), con la tinta della regione
        /// nei colori dei vertici. Null se lo shader non c'e'.</summary>
        public static Material Sand()
        {
            if (sandMat != null) return sandMat;
            var shader = Shader.Find("PryingEyes/DesertSand");
            if (shader == null) return null;
            sandMat = new Material(shader) { name = "SabbiaAlMuro" };
            owned.Add(sandMat);
            return sandMat;
        }

        /// <summary>Finestre accese: 0 di giorno, 1 di notte (DesertSky la aggiorna con l'ora).</summary>
        public static void SetNight(float k)
        {
            if (litGlass == null) return;
            litGlass.SetColor("_EmissionColor", new Color(1.55f, 1.0f, 0.5f) * Mathf.Clamp01(k) * 1.1f);
        }

        /// <summary>Libera materiali e texture del deserto (a fine partita).</summary>
        public static void Clear()
        {
            cache.Clear();
            litGlass = null;
            foreach (var o in owned) if (o != null) Object.Destroy(o);
            owned.Clear();
            glassMat = null; leadMat = null; tarMat = null; sandMat = null;
            tilesCache.Clear(); slateCache.Clear(); shutterCache.Clear();
        }

        // ------------------------------------------------------------------ materiali procedurali
        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");

        static Material Plain(string name, Color c, float smooth, float metal)
        {
            var m = new Material(Lit) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            owned.Add(m);
            return m;
        }

        static Material Textured(string name, Texture2D tex, Color tint, float smooth)
        {
            var m = new Material(Lit) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.mainTextureScale = Vector2.one;
            owned.Add(m); owned.Add(tex);
            return m;
        }

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777216f;
            }
        }

        static Texture2D Gen(int size, System.Func<float, float, Color> f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        static readonly Dictionary<int, Material> tilesCache = new Dictionary<int, Material>(), slateCache = new Dictionary<int, Material>(), shutterCache = new Dictionary<int, Material>();
        static Material glassMat, leadMat, tarMat;

        /// <summary>Tegole a coppo: file sfalsate di scaglie con il colore di ognuna un po' diverso e l'ombra sotto il bordo. Una ripetizione = un metro (4 x 4 tegole).</summary>
        static Material Tiles(Color baseColor, int palette)
        {
            int k = (palette % 4) * 1000 + Mathf.RoundToInt(baseColor.r * 100f);
            if (tilesCache.TryGetValue(k, out var have)) return have;
            var tex = Gen(128, (u, v) =>
            {
                float rows = 4f, cols = 4f;
                float ry = v * rows; int row = Mathf.FloorToInt(ry); float fy = ry - row;
                float cx = u * cols + (row % 2) * 0.5f; int col = Mathf.FloorToInt(cx); float fx = cx - col;
                float h = Hash(col, row, 11 + palette);
                float shade = 0.82f + 0.30f * h;
                float edge = Mathf.Clamp01(1.0f - fy * fy * 0.55f);                 // piu' scura verso il bordo basso (la tegola sopra la ombreggia)
                float round = fx < 0.07f || fx > 0.93f ? 0.55f : 1f;                  // il giunto tra due tegole
                float g = shade * (0.55f + 0.45f * edge) * round;
                float n = 0.94f + 0.06f * Mathf.PerlinNoise(u * 23f + palette, v * 23f);
                return new Color(g * n, g * n * 0.97f, g * n * 0.95f, 1f);
            });
            var m = Textured("TegoleDeserto", tex, baseColor, 0.18f);
            tilesCache[k] = m;
            return m;
        }

        /// <summary>Ardesia: lastre rettangolari sfalsate, grigio-verde con il bordo chiaro e il giunto scuro.</summary>
        static Material Slate(Color baseColor, int palette)
        {
            int k = (palette % 4) * 1000 + Mathf.RoundToInt(baseColor.g * 100f);
            if (slateCache.TryGetValue(k, out var have)) return have;
            var tex = Gen(128, (u, v) =>
            {
                float rows = 8f, cols = 5f;
                float ry = v * rows; int row = Mathf.FloorToInt(ry); float fy = ry - row;
                float cx = u * cols + (row % 2) * 0.5f; int col = Mathf.FloorToInt(cx); float fx = cx - col;
                float h = Hash(col, row, 31 + palette);
                float shade = 0.78f + 0.34f * h;
                float joint = (fx < 0.05f || fy < 0.07f) ? 0.45f : 1f;
                float rim = fy > 0.86f ? 1.12f : 1f;
                float n = 0.92f + 0.08f * Mathf.PerlinNoise(u * 40f, v * 40f + palette);
                float g = shade * joint * rim * n;
                return new Color(g * 0.96f, g, g * 1.04f, 1f);
            });
            var m = Textured("ArdesiaDeserto", tex, baseColor * 1.8f, 0.25f);
            slateCache[k] = m;
            return m;
        }

        /// <summary>Lamiera di piombo o di rame ossidato: cuciture verticali ogni 25 cm.</summary>
        static Material Lead()
        {
            if (leadMat != null) return leadMat;
            var tex = Gen(128, (u, v) =>
            {
                float s = (u * 4f) % 1f;
                float seam = s < 0.04f || s > 0.96f ? 1.22f : 1f;
                float n = 0.84f + 0.16f * Mathf.PerlinNoise(u * 9f, v * 3f);
                float g = 0.50f * seam * n;
                return new Color(g * 0.82f, g * 1.0f, g * 0.92f, 1f);
            });
            leadMat = Textured("PiomboDeserto", tex, Color.white, 0.32f);
            return leadMat;
        }

        /// <summary>Catrame e ghiaietto dei tetti piani: grigio scuro macchiato.</summary>
        static Material Tar()
        {
            if (tarMat != null) return tarMat;
            var tex = Gen(128, (u, v) =>
            {
                float n = 0.55f + 0.30f * Mathf.PerlinNoise(u * 12f, v * 12f) + 0.15f * Hash(Mathf.FloorToInt(u * 128f), Mathf.FloorToInt(v * 128f), 5);
                float stain = Mathf.PerlinNoise(u * 3f + 4f, v * 3f + 9f);
                float g = 0.26f * n * (0.8f + 0.4f * stain);
                return new Color(g, g * 0.97f, g * 0.93f, 1f);
            });
            tarMat = Textured("CatramaDeserto", tex, Color.white, 0.08f);
            return tarMat;
        }

        /// <summary>Assi verticali di persiana: verde bosco, rosso scuro, blu-grigio o legno secondo la tavolozza.</summary>
        static Material Shutter(int palette)
        {
            int k = palette % 5;
            if (shutterCache.TryGetValue(k, out var have)) return have;
            Color[] cols = { new Color(0.22f, 0.34f, 0.24f), new Color(0.42f, 0.16f, 0.14f), new Color(0.24f, 0.30f, 0.38f), new Color(0.34f, 0.24f, 0.15f), new Color(0.16f, 0.22f, 0.18f) };
            var tex = Gen(64, (u, v) =>
            {
                float s = (u * 6f) % 1f;
                float slat = s < 0.07f ? 0.55f : 1f;
                float n = 0.86f + 0.14f * Mathf.PerlinNoise(u * 30f, v * 4f);
                float g = slat * n;
                return new Color(g, g, g, 1f);
            });
            var m = Textured("PersianaDeserto", tex, cols[k] * 1.5f, 0.15f);
            shutterCache[k] = m;
            return m;
        }

        static Material Glass()
        {
            if (glassMat != null) return glassMat;
            glassMat = Plain("VetroSpento", new Color(0.035f, 0.042f, 0.05f), 0.92f, 0.1f);
            return glassMat;
        }

        static Material LitGlass()
        {
            if (litGlass != null) return litGlass;
            litGlass = Plain("VetroAcceso", new Color(0.05f, 0.045f, 0.03f), 0.8f, 0.05f);
            litGlass.EnableKeyword("_EMISSION");
            litGlass.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            litGlass.SetColor("_EmissionColor", Color.black);
            return litGlass;
        }
    }
}

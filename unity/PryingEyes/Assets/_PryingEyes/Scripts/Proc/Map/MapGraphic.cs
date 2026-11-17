using UnityEngine;
using UnityEngine.UI;
using PryingEyes.Proc.MapArt;

namespace PryingEyes.Proc
{
    /// <summary>Un Graphic uGUI che disegna una Mesh2D del pittore della mappa (MapArt). Le coordinate della mesh sono quelle del Graphic
    /// (metri, per la mappa; pixel dell'interfaccia, per la cornice): chi lo usa mette il suo oggetto sotto un contenitore scalato.
    /// Il rettangolo del Graphic copre la mesh: RectMask2D nasconde i Graphic il cui rettangolo e' fuori dalla finestra, e uno che
    /// restasse piccolo e fermo all'origine sparirebbe appena la finestra si sposta.</summary>
    public sealed class MapGraphic : MaskableGraphic
    {
        Mesh2D mesh;
        Texture tex;

        public override Texture mainTexture => tex != null ? tex : s_WhiteTexture;

        /// <summary>La mesh e la sua texture; 'bounds' e' lo spazio che la mesh occupa (nelle sue stesse unita').</summary>
        public void Set(Mesh2D m, Texture texture, R2 bounds)
        {
            mesh = m; tex = texture;
            raycastTarget = false;
            var rt = rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            float w = Mathf.Max(1f, bounds.W), h = Mathf.Max(1f, bounds.H);
            rt.pivot = new Vector2(-bounds.x0 / w, -bounds.y0 / h);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            SetAllDirty();
        }

        /// <summary>La mesh e' cambiata (stesso oggetto, altri vertici): si rifa.</summary>
        public void Refresh() { SetVerticesDirty(); }

        /// <summary>Come Set, ma lascia il rettangolo di prima (la mesh cambia a ogni fotogramma e resta nello stesso spazio).</summary>
        public void Use(Mesh2D m, Texture texture)
        {
            mesh = m; tex = texture;
            raycastTarget = false;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (mesh == null || mesh.Count == 0) return;
            int n = Mathf.Min(mesh.Count, 64998);
            var tint = (Color32)color;
            bool plain = tint.r == 255 && tint.g == 255 && tint.b == 255 && tint.a == 255;
            for (int i = 0; i < n; i++)
            {
                var p = mesh.pos[i]; var t = mesh.uv[i]; var c = mesh.col[i];
                Color32 col = plain ? new Color32(c.r, c.g, c.b, c.a)
                    : new Color32((byte)(c.r * tint.r / 255), (byte)(c.g * tint.g / 255), (byte)(c.b * tint.b / 255), (byte)(c.a * tint.a / 255));
                vh.AddVert(new Vector3(p.x, p.y, 0f), col, new Vector2(t.x, t.y));
            }
            var idx = mesh.idx;
            for (int i = 0; i + 2 < idx.Count; i += 3)
            {
                int a = idx[i], b = idx[i + 1], c2 = idx[i + 2];
                if (a < n && b < n && c2 < n) vh.AddTriangle(a, b, c2);
            }
        }
    }

    /// <summary>Le texture della mappa, costruite una volta sola dal pittore (che non conosce Unity): i nastri della penna, l'acquerello che si
    /// ripete, l'atlante delle macchie, la carta.</summary>
    public static class MapAssets
    {
        public static Texture2D Line, Wash, Stain, Paper;
        static bool built;
        static Sprite shade;

        static System.Threading.Tasks.Task<PixelMap[]> pending;

        /// <summary>Quanto e' costato costruire le texture sul filo principale, in millisecondi (caricarle, e aspettare l'altro filo se non aveva finito).</summary>
        public static float BuildMs { get; private set; }

        /// <summary>Quanto ha lavorato l'altro filo per dipingere i pixel, in millisecondi.</summary>
        public static float GenerateMs { get; private set; }

        /// <summary>Comincia a preparare i pixel delle texture in un altro filo: il pittore non conosce Unity e non ha stato in comune, quindi si puo'.
        /// Quando il primo HUD crea la sua mappa di solito sono gia' pronti (a Mono costano mezzo secondo) e al filo principale resta solo caricarli.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Warm()
        {
            if (built || pending != null) return;
            pending = System.Threading.Tasks.Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var px = new[] { MapTextures.LineTex(), MapTextures.WashTex(), MapTextures.StainAtlas(), MapTextures.PaperTex(3, MapTextures.PaperW, MapTextures.PaperH) };
                GenerateMs = (float)sw.Elapsed.TotalMilliseconds;
                return px;
            });
        }

        public static void Ensure()
        {
            if (built && Line != null && Paper != null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Warm();
            var px = pending.Result;                 // se l'altro filo non ha ancora finito, si aspetta qui
            pending = null;
            Line = Make(px[0], TextureWrapMode.Clamp, false, "MapaInchiostro");
            Wash = Make(px[1], TextureWrapMode.Repeat, true, "MapaAcquerello");
            Stain = Make(px[2], TextureWrapMode.Clamp, true, "MapaMacchie");
            Paper = Make(px[3], TextureWrapMode.Clamp, true, "MapaCarta");
            built = true;
            BuildMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        static Texture2D Make(PixelMap p, TextureWrapMode wrap, bool mips, string name)
        {
            var t = new Texture2D(p.w, p.h, TextureFormat.RGBA32, mips) { wrapMode = wrap, filterMode = FilterMode.Bilinear, name = name, hideFlags = HideFlags.HideAndDontSave };
            t.anisoLevel = mips ? 2 : 0;
            t.SetPixelData(p.rgba, 0);             // solo il livello 0: Apply rifa le mipmap (LoadRawTextureData le vorrebbe gia' tutte)
            t.Apply(mips, false);
            return t;
        }

        /// <summary>Una sfumatura nera dal bordo verso il centro, per la finestra della mappa (la carta si perde nel buio).</summary>
        public static Sprite Shade()
        {
            if (shade != null) return shade;
            const int S = 96;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) * 0.55f + Mathf.Sqrt(u * u + v * v) * 0.45f;
                    float a = Mathf.SmoothStep(0.52f, 1.02f, r);
                    px[y * S + x] = new Color32(6, 4, 3, (byte)Mathf.RoundToInt(255f * 0.92f * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return shade = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
    }
}

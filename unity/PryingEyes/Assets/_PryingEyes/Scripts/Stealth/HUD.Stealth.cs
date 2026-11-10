using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PryingEyes
{
    /// <summary>La parte furtiva dell'HUD: pila della torcia (cinque tacche e scorte), segnalini a forma di occhio sopra i preti
    /// che ti stanno notando o cercando (l'occhio si apre man mano che ti nota: socchiuso e chiaro = sospetto, ambra con la
    /// pupilla che vaga = ti cerca, rosso spalancato = ti ha visto; fuori dallo schermo restano sul bordo, con una freccia),
    /// la freccia dell'ascensore, i segnali pulsanti del ping e il bordo rosso che pulsa quando un prete e' vicino.</summary>
    public partial class HUD
    {
        // ---- torcia
        Image[] torchSeg;
        Image torchCap;
        Text torchCount, keyHint;
        bool torchOn, torchSwap;
        float torchCharge, torchSwapP;
        int torchSpares;
        string lastHint = "";
        // ---- freccia dell'ascensore
        RectTransform arrowRoot;
        Image arrowImg;
        Text arrowText;
        bool arrowShow, arrowSealed;
        float arrowTarget, arrowAngle, arrowAlpha, arrowMeters;
        // ---- ping
        Image pingRingImg;
        float pingRingT = -1f, pingCd;
        // ---- segnalini
        sealed class Tag { public RectTransform rt; public Image icon, arrow; public Text label; }
        RectTransform stealthRoot;
        Canvas stealthCanvas;
        readonly List<Tag> eyeTags = new List<Tag>(), markTags = new List<Tag>();
        Image threatVig;
        static Sprite[,] eyeSprites;
        static Sprite arrowSprite, flameSprite, doorSprite, ringSprite, triSprite;

        // ------------------------------------------------------------------ costruzione
        void BuildStealthUI()
        {
            stealthCanvas = GetComponent<Canvas>();
            // bordo rosso della minaccia: sotto tutto il resto
            threatVig = MakeImage("ThreatVig", Color.clear);
            threatVig.sprite = ThreatVigSprite();
            var tv = threatVig.GetComponent<RectTransform>();
            tv.anchorMin = Vector2.zero; tv.anchorMax = Vector2.one; tv.offsetMin = Vector2.zero; tv.offsetMax = Vector2.zero;

            var rootGo = Child("Furtivita");
            stealthRoot = Comp<RectTransform>(rootGo);
            stealthRoot.anchorMin = Vector2.zero; stealthRoot.anchorMax = Vector2.one;
            stealthRoot.offsetMin = Vector2.zero; stealthRoot.offsetMax = Vector2.zero;

            // pila della torcia: cinque tacche in basso a sinistra, sopra le barre di vita e scudo
            var tr = Comp<RectTransform>(SubChild(rootGo, "Pila"));
            tr.anchorMin = tr.anchorMax = Vector2.zero; tr.pivot = Vector2.zero;
            tr.anchoredPosition = new Vector2(28f, 56f); tr.sizeDelta = new Vector2(112f, 18f);
            var back = Comp<Image>(tr.gameObject); back.color = new Color(0f, 0f, 0f, 0.5f); back.raycastTarget = false;
            torchSeg = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var sg = Comp<RectTransform>(SubChild(tr.gameObject, "Tacca" + i));
                sg.anchorMin = sg.anchorMax = Vector2.zero; sg.pivot = Vector2.zero;
                sg.anchoredPosition = new Vector2(4f + i * 20f, 4f); sg.sizeDelta = new Vector2(17f, 10f);
                torchSeg[i] = Comp<Image>(sg.gameObject); torchSeg[i].raycastTarget = false;
            }
            var cap = Comp<RectTransform>(SubChild(tr.gameObject, "Polo"));
            cap.anchorMin = cap.anchorMax = Vector2.zero; cap.pivot = Vector2.zero;
            cap.anchoredPosition = new Vector2(108f, 5f); cap.sizeDelta = new Vector2(4f, 8f);
            torchCap = Comp<Image>(cap.gameObject); torchCap.raycastTarget = false;
            torchCount = MakeText("PileScorta", new Vector2(0f, 0f), new Vector2(146f, 55f), new Vector2(120f, 20f), 16, TextAnchor.MiddleLeft, "", Ink, FontStyle.Normal);
            keyHint = MakeText("TastiFurtivi", new Vector2(0f, 0f), new Vector2(28f, 78f), new Vector2(560f, 20f), 12, TextAnchor.LowerLeft, "", Dim, FontStyle.Italic);
            torchCount.transform.SetParent(rootGo.transform, false);
            keyHint.transform.SetParent(rootGo.transform, false);

            // freccia dell'ascensore: in alto al centro
            var ar = Comp<RectTransform>(SubChild(rootGo, "FrecciaAscensore"));
            ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 1f); ar.pivot = new Vector2(0.5f, 0.5f);
            ar.anchoredPosition = new Vector2(0f, -50f); ar.sizeDelta = new Vector2(54f, 54f);
            arrowRoot = ar;
            arrowImg = Comp<Image>(ar.gameObject); arrowImg.sprite = ArrowSprite(); arrowImg.raycastTarget = false; arrowImg.color = Color.clear;
            arrowText = MakeText("TestoAscensore", new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(520f, 22f), 15, TextAnchor.UpperCenter, "", Dim, FontStyle.Italic);
            arrowText.transform.SetParent(rootGo.transform, false);

            // onda del ping a schermo: un anello che si allarga dal centro
            var pr = Comp<RectTransform>(SubChild(rootGo, "OndaPing"));
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.pivot = new Vector2(0.5f, 0.5f);
            pr.anchoredPosition = Vector2.zero; pr.sizeDelta = new Vector2(220f, 220f);
            pingRingImg = Comp<Image>(pr.gameObject); pingRingImg.sprite = RingSprite(); pingRingImg.raycastTarget = false; pingRingImg.color = Color.clear;
        }

        // ------------------------------------------------------------------ comandi dal gioco
        public void SetTorch(bool on, float charge, int spares, bool swapping, float swapProgress)
        {
            torchOn = on; torchCharge = charge; torchSpares = spares; torchSwap = swapping; torchSwapP = swapProgress;
        }

        public void SetExitArrow(bool show, float relAngleDeg, float meters, bool sealedNow)
        {
            arrowShow = show; arrowTarget = relAngleDeg; arrowMeters = meters; arrowSealed = sealedNow;
        }

        public void SetPingCooldown(float fraction) { pingCd = fraction; }
        public void PingPulse() { pingRingT = 0f; }

        // ------------------------------------------------------------------ aggiornamento
        void UpdateStealthUI(float dt)
        {
            if (stealthRoot == null) return;
            bool on = Stealth.Enabled;
            if (stealthRoot.gameObject.activeSelf != on) stealthRoot.gameObject.SetActive(on);
            if (!on) { if (threatVig != null) threatVig.color = Color.clear; return; }
            UpdateThreatVig();
            UpdateTorchUI();
            UpdateArrow(dt);
            UpdatePingRing(dt);
            var cam = Camera.main;
            if (cam == null) return;
            UpdateMarks(cam);
            UpdateEyes(cam);
        }

        void UpdateThreatVig()
        {
            float lv = Stealth.Level;
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * Mathf.Lerp(3.5f, 9f, lv));
            float a = lv > 0.15f ? (lv - 0.15f) / 0.85f * 0.40f * pulse : 0f;
            threatVig.color = new Color(1f, 1f, 1f, a);
        }

        void UpdateTorchUI()
        {
            if (torchSeg == null) return;
            int full = torchCharge > 0f ? Mathf.Clamp(Mathf.CeilToInt(torchCharge * 5f - 0.001f), 1, 5) : 0;
            bool blink = torchCharge < 0.15f && Mathf.Repeat(Time.time, 0.8f) < 0.4f;
            for (int i = 0; i < 5; i++)
            {
                bool filled = i < full;
                Color c;
                if (torchSwap) { filled = i <= Mathf.FloorToInt(torchSwapP * 5f); c = new Color(1f, 0.72f, 0.28f, filled ? 0.9f : 0.15f); }
                else if (!filled) c = new Color(0.4f, 0.37f, 0.33f, 0.28f);
                else if (torchCharge < 0.15f) c = blink ? new Color(1f, 0.22f, 0.16f, 0.95f) : new Color(0.55f, 0.10f, 0.08f, 0.8f);
                else if (torchCharge < 0.4f) c = new Color(1f, 0.70f, 0.25f, 0.95f);
                else c = new Color(0.93f, 0.89f, 0.80f, 0.95f);
                if (!torchOn && !torchSwap && filled) c.a *= 0.5f;
                torchSeg[i].color = c;
            }
            torchCap.color = torchOn ? new Color(1f, 0.86f, 0.5f, 1f) : new Color(0.45f, 0.42f, 0.38f, 0.5f);
            torchCount.text = torchSpares > 0 ? "× " + torchSpares : "";
            string hint = "T torcia  ·  Y cambia pila  ·  V ping" + (pingCd > 0f ? "  (" + Mathf.CeilToInt(pingCd * Guide.Cooldown) + " s)" : "");
            if (hint != lastHint) { lastHint = hint; keyHint.text = hint; }
        }

        void UpdateArrow(float dt)
        {
            if (arrowImg == null) return;
            float want = arrowShow ? (arrowSealed ? 0.42f : 1f) : 0f;
            arrowAlpha = Mathf.MoveTowards(arrowAlpha, want, dt * 2.5f);
            arrowAngle = Mathf.LerpAngle(arrowAngle, arrowTarget, 1f - Mathf.Exp(-14f * dt));
            arrowRoot.localRotation = Quaternion.Euler(0f, 0f, -arrowAngle);
            arrowRoot.localScale = Vector3.one * (arrowSealed ? 1f : 1f + 0.07f * Mathf.Sin(Time.time * 5f));
            Color c = arrowSealed ? new Color(0.62f, 0.70f, 0.82f) : new Color(0.55f, 1f, 0.66f);
            c.a = arrowAlpha;
            arrowImg.color = c;
            arrowText.color = new Color(c.r, c.g, c.b, arrowAlpha * 0.9f);
            arrowText.text = arrowAlpha < 0.02f ? "" : (arrowSealed ? "ascensore  ·  sigillato  ·  " : "ascensore  ·  ") + Mathf.RoundToInt(arrowMeters) + " m";
        }

        void UpdatePingRing(float dt)
        {
            if (pingRingImg == null) return;
            if (pingRingT < 0f) { pingRingImg.color = Color.clear; return; }
            pingRingT += dt;
            float t = pingRingT / 0.9f;
            if (t >= 1f) { pingRingT = -1f; pingRingImg.color = Color.clear; return; }
            float s = Mathf.Lerp(0.15f, 4.2f, 1f - (1f - t) * (1f - t));
            pingRingImg.rectTransform.localScale = new Vector3(s, s, 1f);
            pingRingImg.color = new Color(0.55f, 0.85f, 1f, 0.55f * (1f - t));
        }

        // ------------------------------------------------------------------ segnalini
        Tag GetTag(List<Tag> pool, int i, string prefix, Vector2 size, bool withLabel)
        {
            while (pool.Count <= i) pool.Add(MakeTag(prefix + pool.Count, size, withLabel));
            return pool[i];
        }

        Tag MakeTag(string name, Vector2 size, bool withLabel)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(stealthRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = size;
            var t = new Tag { rt = rt };
            var ig = new GameObject("Icona", typeof(RectTransform));
            ig.transform.SetParent(rt, false);
            var irt = (RectTransform)ig.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f); irt.pivot = new Vector2(0.5f, 0.5f); irt.sizeDelta = size;
            t.icon = ig.AddComponent<Image>(); t.icon.raycastTarget = false;
            var ag = new GameObject("Freccia", typeof(RectTransform));
            ag.transform.SetParent(rt, false);
            var art = (RectTransform)ag.transform;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f); art.pivot = new Vector2(0.5f, 0.5f); art.sizeDelta = new Vector2(18f, 18f);
            t.arrow = ag.AddComponent<Image>(); t.arrow.sprite = TriSprite(); t.arrow.raycastTarget = false;
            if (withLabel)
            {
                t.label = MakeText("Etichetta" + name, new Vector2(0.5f, 0.5f), new Vector2(0f, -size.y * 0.5f - 12f), new Vector2(160f, 18f), 13, TextAnchor.UpperCenter, "", Ink, FontStyle.Normal);
                t.label.transform.SetParent(rt, false);
            }
            go.SetActive(false);
            return t;
        }

        /// <summary>Mette il segnalino sul punto del mondo, in coordinate del canvas; se il punto e' fuori dallo schermo o alle spalle
        /// lo spinge sul bordo. 'dir' e' la direzione dal centro dello schermo.</summary>
        void PlaceTag(Tag t, Camera cam, Vector3 world, out Vector2 dir, out bool off)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            float sf = stealthCanvas != null ? Mathf.Max(0.01f, stealthCanvas.scaleFactor) : 1f;
            Vector2 c = new Vector2(cam.pixelWidth, cam.pixelHeight) * 0.5f;
            Vector2 d = (new Vector2(sp.x, sp.y) - c) / sf;
            bool behind = sp.z < 0f;
            if (behind) d = -d;
            var cr = (RectTransform)transform;
            float hw = Mathf.Max(100f, cr.rect.width * 0.5f - 64f), hh = Mathf.Max(100f, cr.rect.height * 0.5f - 74f);
            float k = Mathf.Max(Mathf.Abs(d.x) / hw, Mathf.Abs(d.y) / hh);
            off = behind || k > 1f;
            if (behind && d.sqrMagnitude < 4f) d = new Vector2(0f, -hh);
            else if (off) d /= Mathf.Max(k, 0.0001f);
            dir = d.sqrMagnitude > 0.01f ? d.normalized : Vector2.down;
            t.rt.anchoredPosition = d;
        }

        void AimArrow(Tag t, Vector2 dir, bool off)
        {
            t.arrow.gameObject.SetActive(off);
            if (!off) return;
            var art = t.arrow.rectTransform;
            art.anchoredPosition = dir * 38f;
            art.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
        }

        void UpdateEyes(Camera cam)
        {
            int n = 0;
            foreach (var e in Enemy.All)
            {
                if (e == null || e.IsDead) continue;
                if (e.alert == Alert.Patrol && e.awareness < 0.07f) continue;
                Vector3 head = e.MarkerPoint;
                float dist = Vector3.Distance(head, cam.transform.position);
                if (dist > 44f) continue;
                var t = GetTag(eyeTags, n++, "Occhio", new Vector2(58f, 36f), false);
                t.rt.gameObject.SetActive(true);
                PlaceTag(t, cam, head, out Vector2 dir, out bool off);
                int open, pupil = 1;
                Color col;
                float scale = 1f;
                switch (e.alert)
                {
                    case Alert.Hunting:
                        open = 5; col = new Color(1f, 0.16f, 0.12f);
                        scale = 1f + 0.14f * Mathf.Abs(Mathf.Sin(Time.time * 9f));
                        break;
                    case Alert.Searching:
                        open = 3; pupil = new[] { 0, 1, 2, 1 }[(int)(Time.time * 2.4f) & 3];
                        col = new Color(1f, 0.72f, 0.25f);
                        break;
                    default:
                    {
                        float a = Mathf.Clamp01((e.awareness - 0.05f) / 0.95f);
                        open = Mathf.Clamp(Mathf.RoundToInt(a * 4.2f), 0, 4);
                        col = Color.Lerp(new Color(0.92f, 0.90f, 0.84f), new Color(1f, 0.72f, 0.25f), a);
                        break;
                    }
                }
                col.a = off ? 0.92f : Mathf.Clamp01(1.25f - dist / 44f);
                t.icon.sprite = EyeSprite(open, pupil);
                t.icon.color = col;
                t.rt.localScale = Vector3.one * (off ? 0.85f : 1f) * scale;
                AimArrow(t, dir, off);
            }
            for (int i = n; i < eyeTags.Count; i++) eyeTags[i].rt.gameObject.SetActive(false);
        }

        void UpdateMarks(Camera cam)
        {
            int n = 0;
            var g = Guide.I;
            if (g != null)
                foreach (var mk in g.marks)
                {
                    if (!mk.shown) continue;
                    float age = Time.time - mk.born;
                    if (age > Guide.MarkLife) continue;
                    var t = GetTag(markTags, n++, "Segnale", new Vector2(30f, 44f), true);
                    t.rt.gameObject.SetActive(true);
                    PlaceTag(t, cam, mk.pos, out Vector2 dir, out bool off);
                    float pulse = Mathf.Exp(-Mathf.Repeat(age, 1.1f) * 3.2f);
                    float fade = Mathf.Clamp01((Guide.MarkLife - age) / 1.4f);
                    Color col = mk.kind == 0 ? new Color(0.62f, 0.80f, 1f) : mk.kind == 2 ? new Color(0.55f, 1f, 0.66f) : new Color(0.70f, 0.74f, 0.82f);
                    col.a = fade * (0.55f + 0.45f * pulse);
                    t.icon.sprite = mk.kind == 0 ? FlameSprite() : DoorSprite();
                    t.icon.color = col;
                    t.rt.localScale = Vector3.one * (1f + 0.4f * pulse);
                    float dist = Vector3.Distance(mk.pos, cam.transform.position);
                    t.label.text = mk.kind == 0 ? Mathf.RoundToInt(dist) + " m" : "ascensore " + Mathf.RoundToInt(dist) + " m";
                    t.label.color = new Color(col.r, col.g, col.b, col.a);
                    AimArrow(t, dir, off);
                }
            for (int i = n; i < markTags.Count; i++) markTags[i].rt.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ sprite procedurali
        /// <summary>Un occhio stilizzato 64x40: palpebre a mandorla, sclera, iride, pupilla e riflesso. 'open' 0..5 (0 = una linea chiusa),
        /// 'pupil' 0..2 (sinistra, centro, destra). Il colore si da' con Image.color; il contorno e' chiaro con un alone scuro intorno.</summary>
        static Sprite EyeSprite(int open, int pupil)
        {
            if (eyeSprites == null) eyeSprites = new Sprite[6, 3];
            open = Mathf.Clamp(open, 0, 5); pupil = Mathf.Clamp(pupil, 0, 2);
            var cached = eyeSprites[open, pupil];
            if (cached != null) return cached;
            const int W = 64, H = 40;
            float o = open / 5f;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f, A = 28.5f;
            float hUp = Mathf.Lerp(1.4f, 16.5f, o), hLo = hUp * 0.8f;
            float pdx = (pupil - 1) * 7.5f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float u = dx / A;
                    float sh = Mathf.Max(0f, 1f - u * u);
                    float up = hUp * sh, lo = hLo * sh;
                    float d;      // distanza dal bordo delle palpebre, positiva dentro
                    if (Mathf.Abs(u) < 1f) d = dy >= 0f ? up - dy : dy + lo;
                    else d = -Mathf.Sqrt((Mathf.Abs(dx) - A) * (Mathf.Abs(dx) - A) + dy * dy);
                    float fill = Mathf.Clamp01(d + 0.5f);
                    float ir = Mathf.Sqrt((dx - pdx) * (dx - pdx) + dy * dy);
                    float irisMask = 1f - Stealth.Smooth(8.2f, 9.8f, ir);
                    float lum = Mathf.Lerp(0.93f, 0.30f + 0.12f * Mathf.Clamp01(ir / 9f), irisMask);
                    lum = Mathf.Lerp(lum, 0.02f, 1f - Stealth.Smooth(3.4f, 4.6f, ir));
                    float hl = 1f - Stealth.Smooth(1.1f, 2.0f, Mathf.Sqrt((dx - pdx + 3f) * (dx - pdx + 3f) + (dy - 3.2f) * (dy - 3.2f)));
                    lum = Mathf.Lerp(lum, 1f, hl * 0.85f * irisMask);
                    float line = 1f - Stealth.Smooth(0.6f, 1.7f, Mathf.Abs(d));
                    float halo = d < 0f ? Mathf.Clamp01(1f - (-d) / 4.5f) * 0.55f : 0f;
                    float ca = 0f, cc = 0f;
                    Over(ref ca, ref cc, 0f, halo * (1f - fill));
                    Over(ref ca, ref cc, lum, fill);
                    Over(ref ca, ref cc, 1f, line);
                    byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(cc) * 255f);
                    px[y * W + x] = new Color32(g, g, g, (byte)Mathf.RoundToInt(Mathf.Clamp01(ca) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return eyeSprites[open, pupil] = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Sovrappone (colore sc, alfa sa) a quello che c'e' gia' (ca, cc).</summary>
        static void Over(ref float ca, ref float cc, float sc, float sa)
        {
            float oa = sa + ca * (1f - sa);
            cc = oa > 0.0001f ? (sc * sa + cc * ca * (1f - sa)) / oa : 0f;
            ca = oa;
        }

        static float PolySD(Vector2 p, Vector2[] poly)
        {
            float md = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                Vector2 ab = b - a, ap = p - a;
                float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                md = Mathf.Min(md, (ap - ab * t).magnitude);
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside ? md : -md;
        }

        /// <summary>Poligono bianco con bordo antialias e un alone scuro (leggibile anche su sfondi chiari). Coordinate in pixel, S x S.</summary>
        static Sprite PolySprite(Vector2[] poly, int S, bool halo)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float sd = PolySD(new Vector2(x + 0.5f, y + 0.5f), poly);
                    float fill = Mathf.Clamp01(sd + 0.5f);
                    float h = halo ? Mathf.Clamp01(1f - (-sd) / 3.5f) * 0.55f * (1f - fill) : 0f;
                    float a = fill + h;
                    byte g = a > 0.0001f ? (byte)Mathf.RoundToInt(255f * fill / a) : (byte)0;
                    px[y * S + x] = new Color32(g, g, g, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        static Sprite ArrowSprite() =>
            arrowSprite != null ? arrowSprite : arrowSprite = PolySprite(new[] { new Vector2(32f, 60f), new Vector2(56f, 6f), new Vector2(32f, 20f), new Vector2(8f, 6f) }, 64, true);

        static Sprite FlameSprite() =>
            flameSprite != null ? flameSprite : flameSprite = PolySprite(new[]
            {
                new Vector2(24f, 46f), new Vector2(31f, 32f), new Vector2(36f, 18f), new Vector2(33f, 8f),
                new Vector2(24f, 3f), new Vector2(15f, 8f), new Vector2(12f, 18f), new Vector2(17f, 32f),
            }, 48, true);

        static Sprite DoorSprite() =>
            doorSprite != null ? doorSprite : doorSprite = PolySprite(new[]
            {
                new Vector2(10f, 4f), new Vector2(38f, 4f), new Vector2(38f, 30f), new Vector2(24f, 44f), new Vector2(10f, 30f),
            }, 48, true);

        static Sprite TriSprite() =>
            triSprite != null ? triSprite : triSprite = PolySprite(new[] { new Vector2(9f, 16f), new Vector2(16f, 2f), new Vector2(2f, 2f) }, 18, true);

        static Sprite threatSprite;

        /// <summary>Bordo rosso scuro che sfuma verso il centro (il centro resta pulito): il pulsare della minaccia.</summary>
        static Sprite ThreatVigSprite()
        {
            if (threatSprite != null) return threatSprite;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v) / 1.414f;
                    float a = Stealth.Smooth(0.45f, 1f, r);
                    px[y * S + x] = new Color32(150, 10, 8, (byte)Mathf.RoundToInt(255f * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return threatSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        static Sprite RingSprite()
        {
            if (ringSprite != null) return ringSprite;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ring = Stealth.Smooth(0.70f, 0.82f, r) * (1f - Stealth.Smooth(0.88f, 0.99f, r));
                    float glow = 0.12f * r * (1f - Stealth.Smooth(0.8f, 1f, r));
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(ring + glow) * 255f);
                    px[y * S + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return ringSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
    }
}

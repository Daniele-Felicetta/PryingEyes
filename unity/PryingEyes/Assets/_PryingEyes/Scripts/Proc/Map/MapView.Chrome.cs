using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PryingEyes.Proc.MapArt;

namespace PryingEyes.Proc
{
    public sealed partial class MapView
    {
        // ---- la tavola intorno alla carta: cornice d'ottone con i chiodi, l'occhio sentinella e, nel foglio grande, il titolo, il conto, la legenda,
        // la bussola e la scala. Il testo sta sulla tavola scura (pergamena su cuoio), mai sulla carta: li' ci sono solo il disegno e le scritte a mano.
        // Le coordinate di questi disegni sono in pixel dell'HUD, con l'origine al centro del foglio.
        static readonly Color Parch = new Color(0.88f, 0.79f, 0.60f), ParchDim = new Color(0.66f, 0.58f, 0.45f);

        RectTransform chromeRt, legendRt;
        Mesh2D chromeMesh, legendMesh, sentMesh;
        Pen chromePen, sentPen;
        MapGraphic chromeGfx, legendGfx, sentGfx;
        Text title, subtitle, tallyLabels, tallyValues, scaleText, northText;
        readonly List<Text> legendTexts = new List<Text>();
        Vector2 chromeSize;
        float chromeE = -1f, chromeScale = -1f, chromeTextT;
        bool chromeDirty = true;
        static readonly List<V2> sockPoly = new List<V2>(40);

        // la legenda: segni disegnati dal pittore a 6 px al metro, quattro colonne in basso a destra del foglio
        const float LegendScale = 6f, LegendColW = 150f, LegendRowH = 30f, LegendRight = 16f, LegendBottom = 20f;
        const int LegendLiftIndex = 6;                  // la voce "Ascensore" della legenda

        static Col C(Color c, float a = 1f) => Col.F(c.r, c.g, c.b, a);

        void BuildChrome()
        {
            chromeRt = NewRt("Cornice", sheetRt);          // un punto al centro del foglio: l'origine dei disegni
            Middle(chromeRt);
            chromeRt.sizeDelta = Vector2.zero;
            chromeMesh = new Mesh2D();
            chromePen = new Pen(chromeMesh, 11) { tremor = 0.7f, step = 15f };
            chromeGfx = NewGraphic("Filo", chromeRt);
            sentMesh = new Mesh2D();
            sentPen = new Pen(sentMesh, 23) { wobble = 0f };
            sentGfx = NewGraphic("Sentinella", chromeRt);

            var tl = new Vector2(0.5f, 1f);
            title = ChromeText("Titolo", HUD.Serif(), 27, FontStyle.Italic, Parch, TextAnchor.UpperCenter, tl, tl, new Vector2(0f, -6f), new Vector2(900f, 36f));
            subtitle = ChromeText("Sottotitolo", Hand(), 14, FontStyle.Normal, ParchDim, TextAnchor.UpperCenter, tl, tl, new Vector2(0f, -41f), new Vector2(900f, 20f));
            var bl = Vector2.zero;
            tallyLabels = ChromeText("ContoVoci", HUD.Serif(), 15, FontStyle.Normal, ParchDim, TextAnchor.UpperLeft, bl, bl, new Vector2(28f, 14f), new Vector2(170f, 100f));
            tallyValues = ChromeText("ContoValori", HUD.Serif(), 15, FontStyle.Bold, Parch, TextAnchor.UpperRight, bl, new Vector2(1f, 0f), new Vector2(250f, 14f), new Vector2(86f, 100f));
            tallyLabels.lineSpacing = tallyValues.lineSpacing = 1.25f;
            scaleText = ChromeText("Scala", HUD.Serif(), 12, FontStyle.Italic, ParchDim, TextAnchor.MiddleLeft, bl, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(60f, 18f));
            northText = ChromeText("Nord", HUD.Serif(), 12, FontStyle.Bold, Parch, TextAnchor.MiddleCenter, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-64f, -9f), new Vector2(24f, 16f));
            northText.text = "N";

            legendRt = NewRt("Legenda", sheetRt);
            legendRt.anchorMin = legendRt.anchorMax = new Vector2(1f, 0f);
            legendRt.pivot = Vector2.zero;
            legendRt.anchoredPosition = Vector2.zero;
            legendRt.sizeDelta = Vector2.zero;
            legendRt.localScale = new Vector3(LegendScale, LegendScale, 1f);
            legendMesh = new Mesh2D();
            legendGfx = NewGraphic("Segni", legendRt);
            BuildLegend();
        }

        /// <summary>Una scritta del foglio: 'anchor' dove sta nel foglio, 'pivot' quale punto del suo riquadro e' li', 'pos' lo spostamento in pixel.</summary>
        Text ChromeText(string name, Font font, int size, FontStyle style, Color color, TextAnchor align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box)
        {
            var rt = NewRt(name, sheetRt);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = box;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = false;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.75f); sh.effectDistance = new Vector2(1.2f, -1.2f);
            return t;
        }

        void ChromeReset() { chromeDirty = true; chromeE = -1f; chromeScale = -1f; chromeTextT = 0f; }

        void ChromeSync() { chromeTextT = 0f; }

        // ------------------------------------------------------------------ legenda
        /// <summary>I segni della carta e cosa vogliono dire (dodici, tre righe da quattro). Le coordinate sono in "metri" a 6 px l'uno,
        /// dal bordo destro e dal fondo del foglio.</summary>
        void BuildLegend()
        {
            legendMesh.Clear();
            var pen = new Pen(legendMesh, 5) { wobble = 0.4f, tremor = 0.05f };
            string[] names =
            {
                "Arma a muro", "Scrigno", "Biblioteca", "Altare",
                "Quadro elettrico", "Candela", "Ascensore", "Potenziamento",
                "Porta sigillata", "Stanza ignota", "Tu", "Chi ti ha visto",
            };
            const int cols = 4;
            for (int i = 0; i < names.Length; i++)
            {
                int col = i % cols, row = i / cols;
                float px = -LegendRight - (cols - col) * LegendColW;                          // dal bordo destro del foglio, in pixel
                float py = LegendBottom + (2 - row) * LegendRowH + 11f;                       // dal fondo del foglio
                var c = new V2((px + 14f) / LegendScale, py / LegendScale);
                switch (i)
                {
                    case 0: MapPainter.PoiGlyph(pen, new MapPoi { kind = PoiKind.Weapon }, c); break;
                    case 1: MapPainter.PoiGlyph(pen, new MapPoi { kind = PoiKind.Scrigno }, c); break;
                    case 2: MapPainter.PoiGlyph(pen, new MapPoi { kind = PoiKind.Biblioteca }, c); break;
                    case 3: MapPainter.PoiGlyph(pen, new MapPoi { kind = PoiKind.Altare }, c); break;
                    case 4: MapPainter.PoiGlyph(pen, new MapPoi { kind = PoiKind.Quadro, state = 1 }, c); break;
                    case 5: MapPainter.PoiGlyph(pen, new MapPoi { kind = PoiKind.Candela, state = 1 }, c); break;
                    case 6: MapPainter.PoiGlyph(pen, new MapPoi { kind = PoiKind.ExitLift, state = 1 }, c); break;
                    case 7: MapPainter.PoiGlyph(pen, new MapPoi { kind = PoiKind.Drop, tint = 0 }, c); break;
                    case 8: MapPainter.Seal(pen, c, 1.3f, 7); break;
                    case 9:
                        pen.Blob(c, 1.9f, 1.5f, MapStyle.Smudge.A(0.95f), 3, 0.25f, 18);
                        MapPainter.Eye(pen, c, 2.6f, 0.8f, default, Col.F(0.84f, 0.77f, 0.60f), Col.F(0.60f, 0.07f, 0.05f), Col.F(0.03f, 0.02f, 0.02f), Col.F(0.12f, 0.09f, 0.07f));
                        break;
                    case 10:
                        pen.Disc(c, 1.7f, MapStyle.Paper.A(0.85f), 14);
                        arrowPoly[0] = c + new V2(0f, 1.5f); arrowPoly[1] = c + new V2(1.1f, -1.0f); arrowPoly[2] = c + new V2(0f, -0.4f); arrowPoly[3] = c + new V2(-1.1f, -1.0f);
                        pen.Fill(arrowPoly, Col.F(0.93f, 0.16f, 0.12f));
                        pen.Path(arrowPoly, true, 0.13f, MapStyle.Ink, 0f);
                        break;
                    default:
                        pen.Disc(c, 2.0f, Col.F(0f, 0f, 0f, 0.4f), 14);
                        MapPainter.Eye(pen, c, 3.6f, 1f, default, Col.F(0.92f, 0.88f, 0.78f), Col.F(0.95f, 0.12f, 0.08f), Col.F(0.03f, 0.02f, 0.02f), Col.F(0.06f, 0.04f, 0.03f));
                        break;
                }
                var t = ChromeText("Voce" + i, Hand(), 13, FontStyle.Normal, ParchDim, TextAnchor.MiddleLeft, new Vector2(1f, 0f), new Vector2(0f, 0.5f), new Vector2(px + 32f, py), new Vector2(112f, 22f));
                t.text = names[i];
                legendTexts.Add(t);
            }
            legendGfx.Set(legendMesh, MapAssets.Line, legendMesh.Bounds().Grow(1f));
        }

        // ------------------------------------------------------------------ ogni fotogramma
        void UpdateChrome(float e)
        {
            var size = sheetRt.sizeDelta;
            if (chromeDirty || (size - chromeSize).sqrMagnitude > 0.05f || Mathf.Abs(e - chromeE) > 0.004f || Mathf.Abs(scale - chromeScale) > 0.01f)
            {
                chromeDirty = false; chromeSize = size; chromeE = e; chromeScale = scale;
                RebuildChrome(size, e);
            }
            PaintSentinel(size, e, Time.unscaledTime);
            float ta = Ease((e - 0.6f) / 0.4f);
            SetText(title, ta); SetText(subtitle, ta); SetText(tallyLabels, ta); SetText(tallyValues, ta); SetText(scaleText, ta); SetText(northText, ta);
            foreach (var t in legendTexts) SetText(t, ta);
            legendGfx.canvasRenderer.SetAlpha(ta); SetCull(legendGfx, ta < 0.01f);
            if (ta <= 0.01f) return;
            chromeTextT -= Time.unscaledDeltaTime;
            if (chromeTextT > 0f) return;
            chromeTextT = 0.4f;
            RefreshTexts();
        }

        static void SetText(Text t, float a) { t.canvasRenderer.SetAlpha(a); SetCull(t, a < 0.01f); }

        /// <summary>Per le prove: i pezzi della mappa che dovrebbero vedersi (accesi, non nascosti, non trasparenti) e non hanno un materiale. uGUI
        /// non ricostruisce un pezzo finche' e' nascosto: se nessuno glielo chiede dopo, resta vuoto per sempre (la legenda e la N sono spariti cosi').
        /// Vuoto se va tutto bene.</summary>
        public string UndrawnParts()
        {
            var bad = new System.Text.StringBuilder();
            foreach (var g in rootRt.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.enabled || g.canvasRenderer.cull || g.canvasRenderer.GetAlpha() * g.color.a < 0.01f) continue;
                if (g is Text tx && string.IsNullOrEmpty(tx.text)) continue;
                if (g.canvasRenderer.materialCount == 0) bad.Append(g.name).Append(' ');
            }
            return bad.ToString().TrimEnd();
        }

        void RefreshTexts()
        {
            var plan = map.plan;
            int round = RoundDirector.Round;
            title.text = string.IsNullOrEmpty(plan.epithet) ? plan.name : plan.epithet;
            // nelle catacombe il luogo e' il livello ("L'Ossario · livello III"), nella torre il piano
            var fd = FloorDirector.I;
            string place = Cattedrale.Catacombe.Attive && fd != null && fd.floor >= 1 ? Cattedrale.Rami.NomeLivello(fd.floor) : plan.name;
            subtitle.text = place + (round > 0 ? "   ·   Ondata " + HUD.Roman(round) : "");
            var sc = map.scene;
            var lines = new System.Text.StringBuilder();
            var vals = new System.Text.StringBuilder();
            lines.Append("Stanze viste"); vals.Append(map.SeenCount).Append(" / ").Append(map.RoomTotal);
            if (sc != null && sc.paidDoors)
            {
                int open = 0, all = 0;
                for (int i = 0; i < plan.rooms.Count; i++) { if (plan.rooms[i].IsLift) continue; all++; if (sc.open[i]) open++; }
                lines.Append("\nAree aperte"); vals.Append('\n').Append(open).Append(" / ").Append(all);
            }
            lines.Append("\nArmi viste"); vals.Append('\n').Append(map.foundWeapons.Count);
            if (Power.Panel != null)
            {
                lines.Append("\nCorrente");
                vals.Append('\n').Append(!map.foundPanel ? "?" : Power.On ? "accesa" : "spenta");
            }
            var gm = GameManager.I;
            if (gm != null) { lines.Append("\nUccisioni"); vals.Append('\n').Append(gm.kills); }
            tallyLabels.text = lines.ToString(); tallyValues.text = vals.ToString();
            scaleText.text = "10 m";
            if (legendTexts.Count > LegendLiftIndex) legendTexts[LegendLiftIndex].text = Cattedrale.Catacombe.Attive ? "Cancello" : "Ascensore";      // nelle catacombe l'uscita e' un cancello
        }

        // ------------------------------------------------------------------ il filo
        void RebuildChrome(Vector2 size, float e)
        {
            var m = chromeMesh;
            m.Clear();
            var p = chromePen;
            float hw = size.x * 0.5f, hh = size.y * 0.5f;
            var brass = MapStyle.Brass;
            var dark = Col.F(0.03f, 0.02f, 0.015f);
            // la cornice: un filo d'ottone al bordo del foglio e uno scuro attorno alla finestra
            Frame(p, new R2(-hw + 2.5f, -hh + 2.5f, hw - 2.5f, hh - 2.5f), 1.5f, brass.A(0.62f));
            var wc = windowRt.anchoredPosition; var ws = windowRt.sizeDelta;
            var win = new R2(wc.x - ws.x * 0.5f, wc.y - ws.y * 0.5f, wc.x + ws.x * 0.5f, wc.y + ws.y * 0.5f);
            Frame(p, win.Grow(1.5f), 1.4f, dark.A(0.95f));
            if (e > 0.01f) Frame(p, win.Grow(4.5f), 0.9f, brass.A(0.33f * e));
            // i chiodi agli angoli
            float inset = Mathf.Lerp(5.5f, 10f, e), stud = Mathf.Lerp(2.6f, 3.4f, e);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sy in new[] { -1f, 1f })
                {
                    var c = new V2(sx * (hw - inset), sy * (hh - inset));
                    p.Disc(c, stud + 0.8f, dark.A(0.7f), 12);
                    p.Disc(c, stud, brass.Mul(0.82f), 12);
                    p.Disc(c + new V2(-stud * 0.28f, stud * 0.3f), stud * 0.34f, Col.F(1f, 0.95f, 0.8f, 0.7f), 6);
                }
            if (e > 0.02f)
            {
                float a = e;
                // sotto il titolo: un filo che si perde ai lati, con l'occhio della sentinella al centro
                float ty = hh - 69f;
                p.Line(new V2(-hw + 46f, ty), new V2(-36f, ty), 1.1f, brass.A(0.55f * a), false);
                p.Line(new V2(36f, ty), new V2(hw - 46f, ty), 1.1f, brass.A(0.55f * a), false);
                // la bussola: due cerchi e una stella a quattro punte con l'occhio al centro; il nord e' la punta lunga
                var cc = new V2(hw - 64f, hh - 44f);
                p.Ring(cc, 20f, 1.2f, brass.A(0.6f * a), 24, 0.4f);
                p.Ring(cc, 12f, 0.9f, brass.A(0.4f * a), 20, 0.4f);
                p.Fill(new[] { cc + new V2(0f, 27f), cc + new V2(4.2f, 3f), cc + new V2(-4.2f, 3f) }, brass.A(0.95f * a));
                p.Fill(new[] { cc + new V2(0f, -19f), cc + new V2(3.5f, -3f), cc + new V2(-3.5f, -3f) }, brass.Mul(0.7f).A(0.85f * a));
                p.Fill(new[] { cc + new V2(19f, 0f), cc + new V2(3f, 3.5f), cc + new V2(3f, -3.5f) }, brass.Mul(0.7f).A(0.85f * a));
                p.Fill(new[] { cc + new V2(-19f, 0f), cc + new V2(-3f, 3.5f), cc + new V2(-3f, -3.5f) }, brass.Mul(0.7f).A(0.85f * a));
                p.Disc(cc, 4.6f, Col.F(0.88f, 0.84f, 0.72f, a), 12);
                p.Disc(cc, 2.8f, Col.F(0.70f, 0.08f, 0.06f, a), 10);
                p.Disc(cc, 1.2f, Col.F(0.03f, 0.02f, 0.02f, a), 8);
                // la scala: dieci metri, tra il conto e la legenda
                float len = 10f * scale, bx = -hw + 300f, by = -hh + 30f;
                var pc = C(Parch, 0.8f * a);
                p.Line(new V2(bx, by), new V2(bx + len, by), 1.4f, pc, false);
                p.Line(new V2(bx, by - 3.5f), new V2(bx, by + 3.5f), 1.2f, pc, false);
                p.Line(new V2(bx + len * 0.5f, by - 2.5f), new V2(bx + len * 0.5f, by + 2.5f), 1.0f, pc, false);
                p.Line(new V2(bx + len, by - 3.5f), new V2(bx + len, by + 3.5f), 1.2f, pc, false);
                scaleText.rectTransform.anchoredPosition = new Vector2(bx + hw + len + 9f, 30f);
            }
            chromeGfx.Set(m, MapAssets.Line, new R2(-hw, -hh, hw, hh));
        }

        static void Frame(Pen p, R2 r, float w, Col c)
        {
            p.Path(new[] { new V2(r.x0, r.y0), new V2(r.x1, r.y0), new V2(r.x1, r.y1), new V2(r.x0, r.y1) }, true, w, c, 1f);
        }

        // ------------------------------------------------------------------ la sentinella
        /// <summary>L'occhio d'ottone sul bordo alto del foglio. Nella minimappa sta a cavallo della cornice; nel foglio grande scende sotto il
        /// titolo. Si socchiude quando nessuno ti cerca e si spalanca (e arrossisce) con la minaccia: chi guarda la mappa lo vede prima di sentire il cuore.</summary>
        void PaintSentinel(Vector2 size, float e, float t)
        {
            var m = sentMesh;
            m.Clear();
            float hh = size.y * 0.5f;
            float threat = Mathf.Clamp01(Stealth.Level);
            float w = Mathf.Lerp(30f, 44f, e);
            var c = new V2(0f, Mathf.Lerp(hh - 1f, hh - 69f, e));
            // l'orbita: una mandorla scura con il bordo d'ottone
            sockPoly.Clear();
            for (int i = 0; i <= 14; i++) { float u = -1f + 2f * i / 14f; sockPoly.Add(c + new V2(u * w * 0.64f, w * 0.215f * Mathf.Pow(Mathf.Max(0f, 1f - u * u), 0.85f))); }
            for (int i = 13; i >= 1; i--) { float u = -1f + 2f * i / 14f; sockPoly.Add(c + new V2(u * w * 0.64f, -w * 0.215f * Mathf.Pow(Mathf.Max(0f, 1f - u * u), 0.85f))); }
            sentPen.Fill(sockPoly, Col.F(0.03f, 0.02f, 0.015f, 0.96f));
            sentPen.Path(sockPoly, true, 1.7f, MapStyle.Brass.Mul(0.9f), 0f);
            // la palpebra: mezza chiusa, si apre con la minaccia; ogni tanto sbatte
            float cycle = 5.3f + 1.1f * Mathf.Sin(t * 0.13f);
            float u2 = Mathf.Repeat(t, cycle);
            float open = Mathf.Lerp(0.34f, 1f, threat) + (threat > 0.6f ? 0.06f * Mathf.Sin(t * 12f) : 0f);
            if (u2 < 0.22f) open *= Mathf.Abs(u2 / 0.22f * 2f - 1f);
            var look = new V2(Mathf.Sin(t * 0.55f) * w * 0.12f * (1f - threat), 0f);
            var iris = Col.Mix(Col.F(0.80f, 0.58f, 0.18f), Col.F(0.97f, 0.10f, 0.07f), Mathf.Clamp01(threat * 1.4f));
            MapPainter.Eye(sentPen, c, w * 0.88f, Mathf.Clamp01(open), look, Col.F(0.90f, 0.84f, 0.70f), iris, Col.F(0.03f, 0.02f, 0.02f), Col.F(0.05f, 0.03f, 0.02f));
            sentGfx.Set(m, MapAssets.Line, new R2(c.x - w, c.y - w * 0.5f, c.x + w, c.y + w * 0.5f));
        }
    }
}

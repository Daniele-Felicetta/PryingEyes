using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>La mappa grande e la minimappa di un giocatore che sta su un monitor oltre il primo (schermo condiviso su piu' monitor). Unity disegna
    /// l'IMGUI (FloorDirector.DrawMap) solo sul primo monitor: qui la stessa mappa, dagli stessi dati (FloorDirector.SeatMapItems), e' fatta con uGUI sotto
    /// l'HUD del giocatore, quindi sta sul suo monitor. Un canvas per pannello (la minimappa si sposta a ogni passo: non deve rifare la mesh dell'HUD),
    /// elementi riusati da un mucchio, rifatta venti volte al secondo e solo se la si vede.</summary>
    sealed class SeatMapUi : MonoBehaviour
    {
        internal Seat seat;
        Panel mini, big;
        float nextBuild;
        bool lastMini, lastBig;

        const float MiniSize = 202f, MiniMargin = 18f, Period = 0.05f;

        // ---- per le prove
        internal bool MiniShown => mini != null && mini.Shown;
        internal bool BigShown => big != null && big.Shown;
        internal int MiniItems => mini != null ? mini.Count : 0;
        internal int BigItems => big != null ? big.Count : 0;
        internal bool BigHasText(string text) => big != null && big.HasText(text);

        void LateUpdate()
        {
            var fd = FloorDirector.I;
            bool shown = seat != null && seat.player != null && fd != null && fd.MapsShown;
            bool wantMini = shown && fd.MiniMapOn(seat.index), wantBig = shown && fd.MapOpen(seat.index);
            if (mini == null)
            {
                if (!wantMini && !wantBig) return;
                mini = new Panel(transform, "Minimappa");
                big = new Panel(transform, "Mappa");
                var rm = mini.root;
                rm.anchorMin = rm.anchorMax = rm.pivot = new Vector2(1f, 1f);          // in alto a destra, come l'IMGUI (202 unita' a 18 dal bordo)
                rm.anchoredPosition = new Vector2(-MiniMargin, -MiniMargin);
                rm.sizeDelta = new Vector2(MiniSize, MiniSize);
                mini.size = new Vector2(MiniSize, MiniSize);
                var rb = big.root;
                rb.anchorMin = new Vector2(0.12f, 0.1f); rb.anchorMax = new Vector2(0.88f, 0.9f);         // la mappa grande: il 76% x l'80% dell'HUD
                rb.offsetMin = rb.offsetMax = Vector2.zero;
            }
            mini.SetShown(wantMini);
            big.SetShown(wantBig);
            if (!wantMini && !wantBig) return;
            if (Time.unscaledTime < nextBuild && wantMini == lastMini && wantBig == lastBig) return;
            nextBuild = Time.unscaledTime + Period;
            lastMini = wantMini; lastBig = wantBig;
            var player = seat.player.transform.position;
            if (wantMini) Rebuild(mini, fd, player, false);
            if (wantBig)
            {
                var hud = ((RectTransform)transform).rect.size;
                big.size = new Vector2(hud.x * 0.76f, hud.y * 0.8f);
                Rebuild(big, fd, player, true);
            }
        }

        void Rebuild(Panel p, FloorDirector fd, Vector3 player, bool full)
        {
            p.Begin();
            foreach (var it in fd.SeatMapItems(seat.index, player, p.size, full))
            {
                switch (it.kind)
                {
                    case FloorDirector.SeatMapItem.Kind.Fill:
                        p.Fill(it.r, it.c);
                        break;
                    case FloorDirector.SeatMapItem.Kind.Box:
                        p.Fill(new Rect(it.r.x, it.r.y, it.r.width, it.t), it.c);
                        p.Fill(new Rect(it.r.x, it.r.yMax - it.t, it.r.width, it.t), it.c);
                        p.Fill(new Rect(it.r.x, it.r.y, it.t, it.r.height), it.c);
                        p.Fill(new Rect(it.r.xMax - it.t, it.r.y, it.t, it.r.height), it.c);
                        break;
                    default:
                        p.Label(it);
                        break;
                }
            }
            p.End();
        }

        /// <summary>Un pannello: lo sfondo, un contenitore che ritaglia, e due mucchi di elementi (riquadri sotto, testi sopra) che si riusano a ogni rifacimento.</summary>
        sealed class Panel
        {
            public readonly RectTransform root;
            readonly RectTransform fillRoot, textRoot;
            readonly List<Image> fills = new List<Image>();
            readonly List<Text> texts = new List<Text>();
            int nf, nt;
            public Vector2 size;

            public bool Shown => root.gameObject.activeSelf;
            public int Count => nf + nt;

            public Panel(Transform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(Image));
                root = (RectTransform)go.transform;
                root.SetParent(parent, false);
                var bg = go.GetComponent<Image>();
                bg.color = new Color(0.025f, 0.035f, 0.045f, 0.92f);
                bg.raycastTarget = false;
                var clip = new GameObject("Contenuto", typeof(RectTransform), typeof(RectMask2D));
                var crt = (RectTransform)clip.transform;
                crt.SetParent(root, false);
                Stretch(crt);
                fillRoot = Container(crt, "Riquadri");
                textRoot = Container(crt, "Testi");
                go.SetActive(false);
            }

            static void Stretch(RectTransform rt)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            }

            static RectTransform Container(RectTransform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                Stretch(rt);
                return rt;
            }

            /// <summary>Un elemento in coordinate dell'area: origine in alto a sinistra, y verso il basso.</summary>
            static void Place(RectTransform rt, Rect r)
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(r.x, -r.y);
                rt.sizeDelta = new Vector2(r.width, r.height);
            }

            public void SetShown(bool shown)
            {
                if (root.gameObject.activeSelf != shown) root.gameObject.SetActive(shown);
            }

            public void Begin() { nf = 0; nt = 0; }

            public void End()
            {
                for (int i = nf; i < fills.Count; i++) if (fills[i].gameObject.activeSelf) fills[i].gameObject.SetActive(false);
                for (int i = nt; i < texts.Count; i++) if (texts[i].gameObject.activeSelf) texts[i].gameObject.SetActive(false);
            }

            public void Fill(Rect r, Color c)
            {
                Image img;
                if (nf < fills.Count) img = fills[nf];
                else
                {
                    var go = new GameObject("r", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(fillRoot, false);
                    img = go.GetComponent<Image>();
                    img.raycastTarget = false;
                    fills.Add(img);
                }
                nf++;
                Place(img.rectTransform, r);
                img.color = c;
                if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
            }

            public void Label(FloorDirector.SeatMapItem it)
            {
                Text t;
                if (nt < texts.Count) t = texts[nt];
                else
                {
                    var go = new GameObject("t", typeof(RectTransform), typeof(Text));
                    go.transform.SetParent(textRoot, false);
                    t = go.GetComponent<Text>();
                    t.font = HUD.SansFont();
                    t.raycastTarget = false;
                    t.supportRichText = false;
                    t.verticalOverflow = VerticalWrapMode.Overflow;
                    texts.Add(t);
                }
                nt++;
                Place(t.rectTransform, it.r);
                t.text = it.text;
                t.color = it.c;
                t.alignment = it.align;
                t.fontSize = it.size;
                t.horizontalOverflow = it.wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            }

            public bool HasText(string text)
            {
                for (int i = 0; i < nt; i++) if (texts[i].text == text) return true;
                return false;
            }
        }
    }

    public partial class HUD
    {
        /// <summary>Il carattere senza grazie dell'HUD (anche per la mappa di chi sta su un altro monitor).</summary>
        internal static Font SansFont() => Utility();
    }
}

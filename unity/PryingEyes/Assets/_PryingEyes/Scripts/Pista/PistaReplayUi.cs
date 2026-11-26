using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Production.Racing;

namespace PryingEyes.Pista
{
    /// <summary>What is drawn over a replay, in the style of a broadcast: the tag REPLAY with the circuit, a timeline of the whole race with a mark for every fall, every lap and the finish, the time, the speed and the
    /// camera, the standings of the moment as a tower, the rider on screen with their place, speed and lap, and the keys. H hides it all (for a clean picture); the photo is taken without it.</summary>
    public sealed class PistaReplayUi
    {
        const float BarW = 1000f;
        const int Tower = 10;
        static readonly Color Ink = PistaHud.Ink, Dim = PistaHud.Dim, Amber = PistaHud.Amber, Red = PistaHud.Red;

        GameObject root;
        Canvas canvas;
        Text tag, sub, timeText, modeText, caption, captionSub, hint, toast, ended;
        Image barFill, barHead;
        Text[] towerPos, towerName;
        Image[] towerBack;
        float toastT, towerT = 99f;
        readonly int[] order = new int[64];
        string shownTime = "", shownMode = "", shownCaption = "", shownSub = "";
        int shownSubject = -1;
        public bool Shown { get; private set; } = true;
        public Canvas CanvasRef { get { return canvas; } }

        public PistaReplayUi(RaceRecording rec, string circuitName)
        {
            canvas = PistaHud.MakeCanvas("PistaReplay", 36, out root);
            var rt = (RectTransform)root.transform;
            var tl = new Vector2(0f, 1f); var tr = new Vector2(1f, 1f); var bc = new Vector2(0.5f, 0f); var bl = new Vector2(0f, 0f); var tc = new Vector2(0.5f, 1f); var cc = new Vector2(0.5f, 0.5f);

            // the tag, top left
            PistaHud.Box(rt, "Etichetta", tl, tl, new Vector2(26f, -24f), new Vector2(8f, 62f), Red);
            tag = PistaHud.Label(rt, "Replay", tl, tl, new Vector2(44f, -20f), new Vector2(360f, 44f), 36, TextAnchor.UpperLeft, Ink, "REPLAY", FontStyle.Bold);
            sub = PistaHud.Label(rt, "Pista", tl, tl, new Vector2(46f, -62f), new Vector2(520f, 24f), 19, TextAnchor.UpperLeft, Dim, circuitName, FontStyle.Normal);

            // the tower of the standings, top right
            towerPos = new Text[Tower]; towerName = new Text[Tower]; towerBack = new Image[Tower];
            for (int i = 0; i < Tower; i++)
            {
                float y = -24f - i * 26f;
                towerBack[i] = PistaHud.Box(rt, "TorreFondo" + i, tr, tr, new Vector2(-24f, y), new Vector2(236f, 24f), new Color(0.015f, 0.02f, 0.025f, 0.62f));
                towerPos[i] = PistaHud.Label(rt, "TorrePos" + i, tr, tr, new Vector2(-224f, y - 1f), new Vector2(30f, 24f), 18, TextAnchor.MiddleRight, Amber, "", FontStyle.Bold, false);
                towerName[i] = PistaHud.Label(rt, "TorreNome" + i, tr, tr, new Vector2(-34f, y - 1f), new Vector2(170f, 24f), 18, TextAnchor.MiddleLeft, Ink, "", FontStyle.Bold, false);
            }

            // the rider on screen, bottom left
            caption = PistaHud.Label(rt, "Pilota", bl, bl, new Vector2(40f, 118f), new Vector2(560f, 50f), 40, TextAnchor.LowerLeft, Ink, "", FontStyle.Bold);
            captionSub = PistaHud.Label(rt, "PilotaDati", bl, bl, new Vector2(42f, 96f), new Vector2(620f, 26f), 21, TextAnchor.LowerLeft, Amber, "", FontStyle.Bold);

            // the timeline
            PistaHud.Box(rt, "BarraFondo", bc, bc, new Vector2(0f, 54f), new Vector2(BarW, 10f), new Color(1f, 1f, 1f, 0.22f));
            barFill = PistaHud.Box(rt, "BarraTempo", bc, new Vector2(0f, 0f), new Vector2(-BarW * 0.5f, 54f), new Vector2(0f, 10f), Amber);
            float dur = Mathf.Max(1f, rec.Duration);
            foreach (var m in rec.crashes) Mark(rt, m.t / dur, Red, 22f);
            for (int j = 1; j < rec.laps; j++) { float tl2 = rec.TimeOfProgress(rec.me, j * rec.length); if (tl2 > 0f) Mark(rt, tl2 / dur, Ink, 18f); }
            for (int i = 0; i < rec.count; i++) if (rec.finish[i] >= 0f && i == rec.me) Mark(rt, rec.finish[i] / dur, Amber, 26f);
            barHead = PistaHud.Box(rt, "BarraTesta", bc, bc, new Vector2(-BarW * 0.5f, 59f), new Vector2(4f, 26f), Ink);
            timeText = PistaHud.Label(rt, "Tempo", bc, new Vector2(0f, 0f), new Vector2(-BarW * 0.5f, 70f), new Vector2(240f, 30f), 24, TextAnchor.LowerLeft, Ink, "", FontStyle.Bold);
            modeText = PistaHud.Label(rt, "Modo", bc, new Vector2(1f, 0f), new Vector2(BarW * 0.5f, 70f), new Vector2(560f, 30f), 22, TextAnchor.LowerRight, Dim, "", FontStyle.Bold);
            hint = PistaHud.Label(rt, "Comandi", bc, bc, new Vector2(0f, 16f), new Vector2(1200f, 26f), 16, TextAnchor.LowerCenter, Dim,
                "SPAZIO  pausa     SX DX  salta 5 s (con MAIUSC 30)     SU GIU  velocita'     C  camera     TAB  pilota     H  nascondi     P  foto     T  esci", FontStyle.Normal);
            toast = PistaHud.Label(rt, "Avviso", tc, tc, new Vector2(0f, -120f), new Vector2(900f, 56f), 40, TextAnchor.MiddleCenter, Ink, "", FontStyle.Bold);
            ended = PistaHud.Label(rt, "Fine", cc, cc, new Vector2(0f, 0f), new Vector2(900f, 90f), 60, TextAnchor.MiddleCenter, Ink, "", FontStyle.Bold);
        }

        void Mark(RectTransform parent, float u, Color col, float h)
        {
            var bc = new Vector2(0.5f, 0f);
            PistaHud.Box(parent, "Segno", bc, bc, new Vector2(-BarW * 0.5f + Mathf.Clamp01(u) * BarW, 54f + 5f), new Vector2(3f, h), col);
        }

        public void SetShown(bool on)
        {
            Shown = on;
            if (root != null) root.SetActive(on);
        }

        public void Toast(string text, float seconds) { toast.text = text; toastT = seconds; }

        /// <summary>Every frame: the bar and the head, the time, the mode, who is on screen; the tower four times a second.</summary>
        public void Tick(float dt, RaceRecording rec, float t, bool paused, float speed, string mode, int subject, bool atEnd)
        {
            if (root == null) return;
            float dur = Mathf.Max(0.05f, rec.Duration);
            float u = Mathf.Clamp01(t / dur);
            barFill.rectTransform.sizeDelta = new Vector2(u * BarW, 10f);
            barHead.rectTransform.anchoredPosition = new Vector2(-BarW * 0.5f + u * BarW, 59f);
            string tm = PistaHud.Clock(t) + "  /  " + PistaHud.Clock(dur);
            if (tm != shownTime) { shownTime = tm; timeText.text = tm; }
            string md = (paused ? "PAUSA" : speed == 1f ? "" : "x" + speed.ToString("0.##")) + (paused || speed != 1f ? "      " : "") + mode;
            if (md != shownMode) { shownMode = md; modeText.text = md; }
            if (toastT > 0f) { toastT -= dt; if (toastT <= 0f) toast.text = ""; }
            ended.text = atEnd ? "FINE DEL REPLAY" : "";

            towerT += dt;
            if (subject != shownSubject) { shownSubject = subject; towerT = 99f; }               // a cut: the name on screen changes at once
            if (towerT >= 0.25f || shownCaption.Length == 0)
            {
                towerT = 0f;
                int n = Mathf.Min(rec.count, order.Length);
                for (int r = 0; r < n; r++) { int p = rec.PositionAt(r, t) - 1; if (p >= 0 && p < n) order[p] = r; }
                for (int i = 0; i < Tower; i++)
                {
                    bool has = i < n;
                    towerBack[i].enabled = has;
                    towerPos[i].text = has ? (i + 1).ToString() : "";
                    towerName[i].text = has ? rec.names[order[i]] : "";
                    Color c = has && order[i] == subject ? Amber : has && order[i] == rec.me ? Ink : Dim;
                    towerName[i].color = c;
                    if (has) towerBack[i].color = order[i] == subject ? new Color(0.2f, 0.14f, 0.02f, 0.78f) : new Color(0.015f, 0.02f, 0.025f, 0.62f);
                }
                var f = rec.PoseAt(subject, t);
                int pos = rec.PositionAt(subject, t);
                int lap = Mathf.Clamp(Mathf.FloorToInt(rec.ProgressAt(subject, t) / rec.length) + 1, 1, rec.laps);
                string cap = rec.names[subject] + (subject == rec.me ? "  (tu)" : "");
                string sb = pos + "°   -   " + Mathf.RoundToInt(f.speed * 3.6f) + " km/h   -   GIRO " + lap + "/" + rec.laps;
                if (cap != shownCaption) { shownCaption = cap; caption.text = cap; }
                if (sb != shownSub) { shownSub = sb; captionSub.text = sb; }
            }
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            root = null;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    /// <summary>Gli aiuti per orientarsi al buio. V = ping: un'onda parte dal giocatore, le candele ancora spente rispondono con
    /// una campanella nel punto in cui si trovano (e un segnale pulsante sullo schermo che dura qualche secondo); ma il ping
    /// si sente, e i preti nel raggio vengono a vedere. La freccia dell'ascensore segue il percorso vero sulla navmesh (le
    /// porte, non la linea retta attraverso i muri) ed e' piena quando l'uscita si e' aperta.</summary>
    public class Guide : MonoBehaviour
    {
        public static Guide I;
        public const float Cooldown = 9f, WaveSpeed = 38f, MarkLife = 7.5f, PingNoise = 15f;

        /// <summary>Segnale di un ping: kind 0 = candela da accendere, 1 = ascensore sigillato, 2 = ascensore aperto.</summary>
        public sealed class Mark { public Vector3 pos; public int kind; public float reveal, born; public bool shown; public Candelabro candle; }
        public readonly List<Mark> marks = new List<Mark>();

        float cd, waveT0 = -1f, pathT;
        Vector3 waveO, exitDir;
        float exitMeters;
        bool exitHas;
        NavMeshPath path;

        // onda a terra: un anello a larghezza quasi costante che si allarga
        Mesh ringMesh;
        MeshRenderer ringMr;
        MaterialPropertyBlock ringMpb;
        Vector3[] ringVerts;
        const int Seg = 72;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public void ClearMarks()
        {
            marks.Clear();
            waveT0 = -1f;
            if (ringMr != null) ringMr.enabled = false;
            cd = 0f;
            pathT = 0f;
        }

        public void CandleLit(Candelabro candle, bool exitOpen)
        {
            marks.RemoveAll(m => m.candle == candle);
            if (!exitOpen) return;
            foreach (var mark in marks)
                if (mark.kind == 1) mark.kind = 2;
        }

        // ------------------------------------------------------------------ ping
        public bool TryPing()
        {
            var fd = Proc.FloorDirector.I;
            var gm = GameManager.I;
            if (cd > 0f || fd == null || fd.cur == null || gm == null || gm.player == null) return false;
            var pl = gm.player;
            cd = Cooldown;
            waveO = pl.transform.position;
            waveT0 = Time.time;
            marks.Clear();
            foreach (var c in fd.cur.candles)
            {
                if (c == null || c.lit) continue;
                Vector3 p = c.transform.position + Vector3.up * 1.3f;
                marks.Add(new Mark { pos = p, kind = 0, candle = c, reveal = Time.time + Vector3.Distance(p, waveO) / WaveSpeed });
            }
            var ex = fd.cur.exit;
            if (ex != null)
            {
                Vector3 p = ex.frontPoint + Vector3.up * 1.7f;
                marks.Add(new Mark { pos = p, kind = fd.ExitOpen ? 2 : 1, reveal = Time.time + Vector3.Distance(p, waveO) / WaveSpeed });
            }
            Sfx.UI(SoundBank.PingOut, 0.6f);
            Noise.Emit(pl.transform.position, PingNoise, NoiseKind.Ping);
            Perks.OnPing();             // Occhio della Torre: per qualche secondo le etichette mostrano anche i preti che non ti hanno notato
            gm.hud?.PingPulse();
            return true;
        }

        void Update()
        {
            var gm = GameManager.I;
            var fd = Proc.FloorDirector.I;
            var hud = gm != null ? gm.hud : null;
            if (gm == null || fd == null || fd.cur == null || hud == null || gm.player == null) return;
            float dt = Time.deltaTime;
            if (cd > 0f) cd -= dt;
            hud.SetPingCooldown(cd > 0f ? cd / Cooldown : 0f);
            if (!GameMenu.Blocked && gm.player.alive && !fd.transitioning && (Input.GetKeyDown(KeyCode.V) || TouchControls.Down(TouchControls.Action.Ping))) TryPing();
            TickWave();
            TickArrow(dt, fd, hud);
        }

        void TickWave()
        {
            if (waveT0 < 0f) return;
            float r = (Time.time - waveT0) * WaveSpeed;
            bool anyPending = false;
            foreach (var m in marks)
            {
                if (m.shown) continue;
                if (Time.time >= m.reveal)
                {
                    m.shown = true;
                    m.born = Time.time;
                    float pitch = m.kind == 0 ? Random.Range(0.96f, 1.06f) : 0.72f;
                    Sfx.At(SoundBank.PingEcho, m.pos, 0.8f, pitch, 3f, 95f);
                }
                else anyPending = true;
            }
            const float MaxR = 130f;
            if (r < MaxR)
            {
                EnsureRing();
                ringMr.enabled = true;
                UpdateRing(r, Mathf.Clamp01(1f - r / MaxR) * 0.55f);
            }
            else if (ringMr != null) ringMr.enabled = false;
            if (r >= MaxR && !anyPending) waveT0 = -1f;
        }

        void EnsureRing()
        {
            if (ringMesh != null) return;
            var go = new GameObject("OndaPing");
            go.layer = 2;
            ringMesh = new Mesh { name = "OndaPing" };
            ringMesh.MarkDynamic();
            ringVerts = new Vector3[(Seg + 1) * 2];
            var uv = new Vector2[ringVerts.Length];
            var tris = new int[Seg * 6];
            for (int i = 0; i <= Seg; i++) { uv[i * 2] = new Vector2(0f, 0f); uv[i * 2 + 1] = new Vector2(1f, 0f); }
            for (int i = 0; i < Seg; i++)
            {
                int a = i * 2;
                tris[i * 6 + 0] = a; tris[i * 6 + 1] = a + 1; tris[i * 6 + 2] = a + 2;
                tris[i * 6 + 3] = a + 1; tris[i * 6 + 4] = a + 3; tris[i * 6 + 5] = a + 2;
            }
            ringMesh.vertices = ringVerts;
            ringMesh.uv = uv;
            ringMesh.triangles = tris;
            go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            ringMr = go.AddComponent<MeshRenderer>();
            ringMr.sharedMaterial = Fx.Unlit(RingGrad(), true);
            ringMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringMr.receiveShadows = false;
            ringMpb = new MaterialPropertyBlock();
        }

        static Texture2D ringGrad;
        static Texture2D RingGrad()
        {
            if (ringGrad != null) return ringGrad;
            const int W = 32;
            var t = new Texture2D(W, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * 2];
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float s = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f));
                byte a = (byte)Mathf.RoundToInt(255f * s * s);
                px[x] = px[W + x] = new Color32(255, 255, 255, a);
            }
            t.SetPixels32(px);
            t.Apply(false);
            return ringGrad = t;
        }

        void UpdateRing(float r, float alpha)
        {
            float w = Mathf.Lerp(0.5f, 2.2f, Mathf.Clamp01(r / 80f));
            for (int i = 0; i <= Seg; i++)
            {
                float a = (float)i / Seg * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                ringVerts[i * 2] = waveO + d * Mathf.Max(0.05f, r - w) + Vector3.up * 0.06f;
                ringVerts[i * 2 + 1] = waveO + d * r + Vector3.up * 0.06f;
            }
            ringMesh.vertices = ringVerts;
            ringMesh.RecalculateBounds();
            ringMpb.SetColor("_BaseColor", new Color(0.55f, 0.85f, 1f, alpha));
            ringMr.SetPropertyBlock(ringMpb);
        }

        // ------------------------------------------------------------------ freccia dell'ascensore
        void TickArrow(float dt, Proc.FloorDirector fd, HUD hud)
        {
            var gm = GameManager.I;
            var pl = gm.player;
            var exit = fd.cur.exit;
            if (exit == null || fd.transitioning || !pl.alive) { hud.SetExitArrow(false, 0f, 0f, true); return; }
            pathT -= dt;
            Vector3 pp = pl.transform.position;
            if (pathT <= 0f)
            {
                pathT = 0.35f;
                exitHas = false;
                if (NavMesh.SamplePosition(pp, out NavMeshHit a, 2.5f, NavMesh.AllAreas) && NavMesh.SamplePosition(exit.frontPoint, out NavMeshHit b, 2.5f, NavMesh.AllAreas))
                {
                    if (path == null) path = new NavMeshPath();
                    if (NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        var cs = path.corners;
                        float len = 0f;
                        for (int i = 1; i < cs.Length; i++) len += Vector3.Distance(cs[i - 1], cs[i]);
                        Vector3 next = cs[cs.Length - 1];
                        for (int i = 1; i < cs.Length; i++)
                            if (new Vector2(cs[i].x - pp.x, cs[i].z - pp.z).magnitude > 1.3f) { next = cs[i]; break; }
                        Vector3 dir = next - pp; dir.y = 0f;
                        if (dir.sqrMagnitude > 0.01f) { exitDir = dir.normalized; exitMeters = len; exitHas = true; }
                    }
                }
                if (!exitHas)
                {
                    Vector3 dir = exit.frontPoint - pp; dir.y = 0f;
                    exitDir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                    exitMeters = dir.magnitude;
                }
            }
            Vector3 f = pl.cam.forward; f.y = 0f;
            float rel = f.sqrMagnitude > 0.001f ? Vector3.SignedAngle(f.normalized, exitDir, Vector3.up) : 0f;
            hud.SetExitArrow(true, rel, exitMeters, !fd.ExitOpen);
        }
    }
}

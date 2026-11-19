using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PryingEyes.Deserto
{
    /// <summary>La carta della minimappa del deserto (0.9.1): una texture a giro di 10 x 10 quadrati da 64 m (DesertMapGen) che si legge con una finestra di 512 m centrata su chi gioca.
    /// Il quadrato (tx, tz) vive nel posto (tx mod 10, tz mod 10): camminando entrano in finestra quadrati nuovi, che prendono il posto di quelli rimasti dietro, e la finestra
    /// (uvRect, con la texture che si ripete) scorre da sola. Ogni quadrato si dipinge su un thread (DesertMapGen e' puro; 2-3 ms l'uno) e il thread principale ne copia al piu'
    /// <see cref="MaxApply"/> per fotogramma. Un quadrato si dipinge solo quando i luoghi attorno sono gia' fatti (DesertWorld.KnownAround): la sabbia li fa prima, a poco a poco.
    /// Fuori dai quadrati pronti c'e' la carta scura dei posti non esplorati.</summary>
    public sealed class DesertMapTiles
    {
        const int S = DesertMapGen.Slots, T = DesertMapGen.TilePx, TexSize = S * T, MaxJobs = 3, MaxApply = 4;
        /// <summary>Metri dal centro ai bordi dei quadrati che servono: mezza finestra e un margine, perche' il bordo si vede mentre si cammina.</summary>
        const float Half = DesertMapGen.WindowSize * 0.5f + 12f;

        sealed class Job { public int slot, tx, tz; public Task<byte[]> task; }

        readonly Texture2D tex;
        readonly int[] slotX = new int[S * S], slotZ = new int[S * S];
        readonly bool[] ready = new bool[S * S];
        readonly List<Job> jobs = new List<Job>();
        readonly List<Site> near = new List<Site>();
        readonly List<(float d, int tx, int tz)> wanted = new List<(float, int, int)>();
        readonly byte[] blank = new byte[T * T * 4];
        bool dirty, disposed;

        public Texture2D Texture => tex;
        /// <summary>Quanti quadrati servono alla finestra, quanti sono pronti, quanti ne sono stati dipinti in tutto e quanti hanno dato errore (per le prove).</summary>
        public int Wanted { get; private set; }
        public int Ready { get; private set; }
        public int Painted { get; private set; }
        public int Failed { get; private set; }
        public int Running => jobs.Count;

        public DesertMapTiles()
        {
            tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { name = "MinimappaDeserto", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            DesertMapGen.Blank(blank);
            for (int k = 0; k < S * S; k++) { slotX[k] = slotZ[k] = int.MinValue; Write(k, blank); }
            tex.Apply(false);
        }

        /// <summary>La parte della texture che la finestra mostra quando chi gioca sta in (cx, cz): le coordinate escono dall'intervallo 0..1 e la texture si ripete.</summary>
        public static Rect UvRect(float cx, float cz)
        {
            float tw = S * DesertMapGen.TileSize, w = DesertMapGen.WindowSize;
            return new Rect((cx - w * 0.5f) / tw, (cz - w * 0.5f) / tw, w / tw, w / tw);
        }

        /// <summary>Il pezzo di carta del quadrato (tx, tz) e' dipinto? (per le prove)</summary>
        public bool IsReady(int tx, int tz)
        {
            int slot = DesertMapGen.SlotOf(tx) + S * DesertMapGen.SlotOf(tz);
            return slotX[slot] == tx && slotZ[slot] == tz && ready[slot];
        }

        /// <summary>Un colore della carta com'e' adesso nel punto del mondo (x, z), se il suo quadrato e' pronto (per le prove).</summary>
        public bool TryGetPixel(float x, float z, out Color32 c)
        {
            c = default;
            int tx = DesertMapGen.TileOf(x), tz = DesertMapGen.TileOf(z);
            if (!IsReady(tx, tz)) return false;
            int px = Mathf.Clamp((int)((x - tx * DesertMapGen.TileSize) / DesertMapGen.Mpp), 0, T - 1), pz = Mathf.Clamp((int)((z - tz * DesertMapGen.TileSize) / DesertMapGen.Mpp), 0, T - 1);
            c = tex.GetPixel(DesertMapGen.SlotOf(tx) * T + px, DesertMapGen.SlotOf(tz) * T + pz);
            return true;
        }

        /// <summary>Ogni fotogramma: copia i quadrati finiti e lancia quelli che mancano, i piu' vicini al centro per primi.</summary>
        public void Update(float cx, float cz)
        {
            if (disposed) return;
            Collect();
            wanted.Clear();
            int t0x = DesertMapGen.TileOf(cx - Half), t1x = DesertMapGen.TileOf(cx + Half), t0z = DesertMapGen.TileOf(cz - Half), t1z = DesertMapGen.TileOf(cz + Half);
            for (int tz = t0z; tz <= t1z; tz++)
                for (int tx = t0x; tx <= t1x; tx++)
                {
                    float dx = (tx + 0.5f) * DesertMapGen.TileSize - cx, dz = (tz + 0.5f) * DesertMapGen.TileSize - cz;
                    wanted.Add((dx * dx + dz * dz, tx, tz));
                }
            wanted.Sort((a, b) => a.d.CompareTo(b.d));
            Wanted = wanted.Count;
            int done = 0;
            foreach (var w in wanted)
            {
                int slot = DesertMapGen.SlotOf(w.tx) + S * DesertMapGen.SlotOf(w.tz);
                if (slotX[slot] != w.tx || slotZ[slot] != w.tz)
                {
                    // il posto era di un quadrato rimasto dietro: carta scura finche' il nuovo non e' pronto
                    slotX[slot] = w.tx; slotZ[slot] = w.tz; ready[slot] = false;
                    Write(slot, blank);
                }
                if (ready[slot]) { done++; continue; }
                if (jobs.Count >= MaxJobs || IsRunning(slot)) continue;
                float x0 = w.tx * DesertMapGen.TileSize, z0 = w.tz * DesertMapGen.TileSize;
                if (!DesertWorld.KnownAround(x0, z0, x0 + DesertMapGen.TileSize, z0 + DesertMapGen.TileSize)) continue;
                DesertWorld.SitesNearKnown(x0 + DesertMapGen.TileSize * 0.5f, z0 + DesertMapGen.TileSize * 0.5f, DesertMapGen.SiteReach, near);
                var sites = near.ToArray();
                int tx2 = w.tx, tz2 = w.tz;
                jobs.Add(new Job
                {
                    slot = slot, tx = tx2, tz = tz2,
                    task = Task.Run(() => { var px = new byte[T * T * 4]; DesertMapGen.Paint(tx2, tz2, sites, px); return px; }),
                });
            }
            Ready = done;
            if (dirty) { tex.Apply(false); dirty = false; }
        }

        bool IsRunning(int slot)
        {
            foreach (var j in jobs) if (j.slot == slot) return true;
            return false;
        }

        void Collect()
        {
            int applied = 0;
            for (int k = jobs.Count - 1; k >= 0 && applied < MaxApply; k--)
            {
                var j = jobs[k];
                if (!j.task.IsCompleted) continue;
                jobs.RemoveAt(k);
                bool current = slotX[j.slot] == j.tx && slotZ[j.slot] == j.tz;
                if (j.task.IsFaulted)
                {
                    Failed++;
                    Debug.LogError($"[PryingEyes] minimappa del deserto: il quadrato {j.tx},{j.tz} non si dipinge: " + j.task.Exception?.GetBaseException().Message);
                    if (current) ready[j.slot] = true;          // resta la carta scura: non si riprova a ogni fotogramma
                    continue;
                }
                if (!current) continue;                         // la finestra e' andata altrove: il posto e' di un altro quadrato
                Write(j.slot, j.task.Result);
                ready[j.slot] = true;
                Painted++;
                applied++;
            }
        }

        void Write(int slot, byte[] px)
        {
            var raw = tex.GetRawTextureData<byte>();
            int sx = slot % S, sz = slot / S;
            for (int j = 0; j < T; j++)
                NativeArray<byte>.Copy(px, j * T * 4, raw, ((sz * T + j) * TexSize + sx * T) * 4, T * 4);
            dirty = true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            jobs.Clear();
            if (tex != null) Object.Destroy(tex);
        }
    }
}

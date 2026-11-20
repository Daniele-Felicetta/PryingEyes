using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le tracce delle gomme. Dove la moto passa sulla sabbia le sue due ruote lasciano un solco (quella di dietro piu' largo e piu' fondo di quella davanti, con il disegno del tassellato) e il
    /// vento lo cancella in un minuto: dietro a chi guida si vede la sua strada, e di curva in curva anche com'e' andato. Come le orme (<see cref="DesertPrints"/>) sono quadrati piatti posati sul
    /// terreno (la normale del collider della sabbia), tutti in una mesh sola ridisegnata a ogni fotogramma per tutte le camere (Graphics.DrawMesh: va bene anche nello schermo condiviso e per
    /// le moto dei compagni, che passano dalla stessa DesertBike.Pose); qui pero' ogni ruota ha un nastro (<see cref="Pen"/>) che si allunga di un quadrato ogni <see cref="Step"/> metri circa, e
    /// la forma e la luce le fa DesertTrack.shader. Ne restano al piu' <see cref="Max"/>: il piu' vecchio lascia il posto al nuovo. Una ruota in aria, su una pietra o in acqua interrompe il
    /// nastro: il pezzo dopo non si lega a quello prima.</summary>
    public sealed class DesertTracks : MonoBehaviour
    {
        public static DesertTracks I { get; private set; }

        public const int Max = 2048;
        /// <summary>Dopo quanti secondi il vento ha cancellato un pezzo di traccia.</summary>
        public const float Life = 60f;
        /// <summary>I metri minimi tra due pezzi dello stesso nastro: ogni pezzo e' lungo quanto la strada fatta dalla ruota da quello prima.</summary>
        public const float Step = 0.8f;

        /// <summary>Il nastro di una ruota: dove ha posato l'ultimo pezzo, e quanta strada ha fatto da quando e' cominciato (per il disegno del tassellato).</summary>
        public sealed class Pen
        {
            /// <summary>Mezza larghezza di un pezzo (m): il solco e' circa il 60 % di questo, il resto sono i bordi rialzati.</summary>
            public readonly float half;
            internal Vector3 last, lastNormal;
            internal bool has;
            internal float v;
            public Pen(float halfWidth) { half = halfWidth; }
            /// <summary>Il nastro si interrompe (la ruota e' in aria, la moto e' stata parcheggiata o ripresa): il pezzo dopo non si lega a quello prima.</summary>
            public void Lift() { has = false; }
            /// <summary>Per le prove: il nastro ha un punto da cui continuare.</summary>
            public bool Down => has;
        }

        Mesh mesh;
        Material mat;
        readonly Vector3[] verts = new Vector3[Max * 4];
        readonly Vector2[] uvs = new Vector2[Max * 4];
        readonly Color32[] cols = new Color32[Max * 4];
        readonly float[] born = new float[Max];
        int next, alive;
        bool dirty;
        float refresh;

        /// <summary>Quanti pezzi ci sono adesso (non ancora cancellati).</summary>
        public int Alive
        {
            get { int n = 0; for (int i = 0; i < Max; i++) if (Time.time - born[i] < Life) n++; return n; }
        }
        /// <summary>Quanti ne sono stati fatti da quando c'e' il deserto.</summary>
        public int Stamped { get; private set; }

        public static DesertTracks Create(GameObject host) => host.AddComponent<DesertTracks>();

        /// <summary>Un pezzo di nastro: la ruota che tocca la sabbia in 'contact' (sotto la ruota, anche in alto: il raggio parte da 80 cm sopra) lo allunga fino li', se ha fatto abbastanza strada dall'ultimo
        /// pezzo. 'deep' 0..1 e' quanto scava (una ruota che spinge, frena o derapa scava di piu'). Non fa niente se sotto non c'e' sabbia (un pavimento, una scala, l'acqua delle saline).</summary>
        public static void Lay(Pen pen, Vector3 contact, float deep)
        {
            if (I != null) I.Add(pen, contact, deep);
        }

        /// <summary>Per le prove: invecchia tutti i pezzi di tanti secondi (come se il vento avesse soffiato per quel tempo).</summary>
        public void Age(float seconds)
        {
            for (int i = 0; i < Max; i++) born[i] -= seconds;
            refresh = 0f;
        }

        /// <summary>Per le prove: i centri dei pezzi vivi (non ancora cancellati), dal piu' vecchio al piu' nuovo.</summary>
        public void Centres(List<Vector3> into)
        {
            into.Clear();
            for (int k = 0; k < Max; k++)
            {
                int i = (next + k) % Max;
                if (Time.time - born[i] >= Life) continue;
                into.Add((verts[i * 4] + verts[i * 4 + 2]) * 0.5f);
            }
        }

        /// <summary>Per le prove: la larghezza e la lunghezza del pezzo piu' nuovo (m), e il suo centro; false se non ce n'e' o se il vento l'ha gia' cancellato.</summary>
        public bool Newest(out Vector3 centre, out float width, out float length)
        {
            int i = (next + Max - 1) % Max;
            centre = (verts[i * 4] + verts[i * 4 + 2]) * 0.5f;
            width = (verts[i * 4 + 1] - verts[i * 4]).magnitude;
            length = (verts[i * 4 + 3] - verts[i * 4]).magnitude;
            return Time.time - born[i] < Life;
        }

        void Awake()
        {
            I = this;
            var shader = Shader.Find("PryingEyes/DesertTrack");
            if (shader != null) mat = new Material(shader) { name = "DesertTrack" };
            else Debug.LogWarning("[PryingEyes] deserto: lo shader delle tracce non c'e'");
            mesh = new Mesh { name = "TracceDeserto", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.MarkDynamic();
            var tris = new int[Max * 6];
            for (int i = 0; i < Max; i++)
            {
                int v = i * 4;
                tris[i * 6] = v; tris[i * 6 + 1] = v + 1; tris[i * 6 + 2] = v + 2;
                tris[i * 6 + 3] = v; tris[i * 6 + 4] = v + 2; tris[i * 6 + 5] = v + 3;
                born[i] = -1e9f;
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (mesh != null) Destroy(mesh);
            if (mat != null) Destroy(mat);
        }

        void Add(Pen pen, Vector3 contact, float deep)
        {
            if (mat == null || pen == null) return;
            if (pen.has)
            {
                Vector3 d0 = contact - pen.last; d0.y = 0f;
                float run = d0.magnitude;
                if (run < Step) return;                                       // troppo presto: il pezzo di prima non e' ancora lungo abbastanza
                if (run > 6f) pen.has = false;                                // un salto (teletrasporto, un lungo volo): si ricomincia
            }
            if (!Physics.Raycast(contact + Vector3.up * 0.8f, Vector3.down, out var hit, 2.5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)
                || !DesertTerrain.IsSand(hit.collider) || DesertWorld.Brine(hit.point.x, hit.point.z) > 0.5f)
            {
                pen.has = false;                                              // un pavimento, una scala, una pietra, l'acqua bassa delle saline: nessun solco, e il nastro si spezza
                return;
            }
            Vector3 n = hit.normal, p = hit.point + n * 0.012f;
            if (!pen.has) { pen.has = true; pen.last = p; pen.lastNormal = n; pen.v = 0f; return; }
            Vector3 a = pen.last, d = p - a;
            Vector3 na = (n + pen.lastNormal).normalized;
            Vector3 f = d - na * Vector3.Dot(d, na);                          // il verso del pezzo, sul piano del terreno
            float len = f.magnitude;
            if (len < 0.05f) return;
            f /= len;
            Vector3 r = Vector3.Cross(na, f).normalized;
            int i = next; next = (next + 1) % Max;
            born[i] = Time.time;
            Stamped++;
            int v = i * 4;
            verts[v] = a - r * pen.half; verts[v + 1] = a + r * pen.half; verts[v + 2] = p + r * pen.half; verts[v + 3] = p - r * pen.half;
            // il tassellato si ripete ogni undici centimetri: tolti multipli di 16 m (cioe' di 144 ripetizioni) il disegno non cambia ma i numeri restano piccoli
            float v0 = pen.v, v1 = pen.v + len, off = Mathf.Floor(v0 / 16f) * 16f;
            pen.v = v1;
            uvs[v] = new Vector2(-1f, v0 - off); uvs[v + 1] = new Vector2(1f, v0 - off); uvs[v + 2] = new Vector2(1f, v1 - off); uvs[v + 3] = new Vector2(-1f, v1 - off);
            byte hx = (byte)Mathf.Clamp(Mathf.RoundToInt((f.x * 0.5f + 0.5f) * 255f), 0, 255), hz = (byte)Mathf.Clamp(Mathf.RoundToInt((f.z * 0.5f + 0.5f) * 255f), 0, 255);
            byte dp = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(deep) * 255f), 0, 255);
            for (int k = 0; k < 4; k++) cols[v + k] = new Color32(hx, hz, 0, dp);
            pen.last = p; pen.lastNormal = n;
            dirty = true; alive++;
        }

        // Update e non LateUpdate: DrawMesh vale per il fotogramma in cui si chiama, e le foto di prova fanno cam.Render() da una coroutine, che gira dopo gli Update e prima dei LateUpdate
        // (come le orme e le cose sulla sabbia).
        void Update()
        {
            if (mat == null) return;
            refresh -= Time.deltaTime;
            if (refresh <= 0f)
            {
                refresh = 0.25f;
                float now = Time.time;
                int live = 0;
                for (int i = 0; i < Max; i++)
                {
                    float age = (now - born[i]) / Life;
                    if (age >= 1f)
                    {
                        // cancellato: il quadrato torna a un punto (non si disegna)
                        if (verts[i * 4] != verts[i * 4 + 2]) { for (int k = 0; k < 4; k++) verts[i * 4 + k] = Vector3.zero; dirty = true; }
                        continue;
                    }
                    live++;
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(age * 255f), 0, 255);
                    if (cols[i * 4].b != b) { for (int k = 0; k < 4; k++) { var c = cols[i * 4 + k]; c.b = b; cols[i * 4 + k] = c; } dirty = true; }
                }
                alive = live;
            }
            if (dirty)
            {
                dirty = false;
                mesh.vertices = verts;
                mesh.uv = uvs;
                mesh.colors32 = cols;
            }
            if (alive > 0) Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, null, 0, null, false, false, false);
        }
    }
}

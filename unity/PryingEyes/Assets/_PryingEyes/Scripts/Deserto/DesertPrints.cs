using UnityEngine;

namespace PryingEyes.Deserto
{
    /// <summary>Le orme. Ogni passo del giocatore sulla sabbia lascia l'impronta di uno scarpone (a piedi alternati, con una leggera apertura), che il vento cancella in poco piu' di un minuto: dietro
    /// a chi cammina si vede la sua strada, e nella terza persona la scia e' la prima cosa che si nota. Sono quadrati piatti posati sul terreno (la normale del collider della sabbia), tutti in
    /// una mesh sola ridisegnata a ogni fotogramma (Graphics.DrawMesh, per tutte le camere: va bene anche nello split screen); la forma e la luce di ogni buca le fa DesertPrint.shader. Ne restano
    /// al piu' <see cref="Max"/>: la piu' vecchia lascia il posto alla nuova.</summary>
    public sealed class DesertPrints : MonoBehaviour
    {
        public static DesertPrints I { get; private set; }

        public const int Max = 96;
        /// <summary>Dopo quanti secondi il vento ha cancellato un'impronta.</summary>
        public const float Life = 80f;
        const float HalfW = 0.15f, HalfL = 0.24f;

        Mesh mesh;
        Material mat;
        readonly Vector3[] verts = new Vector3[Max * 4];
        readonly Color32[] cols = new Color32[Max * 4];
        readonly float[] born = new float[Max];
        int next;
        bool right;
        float refresh;

        /// <summary>Quante impronte ci sono adesso (non ancora cancellate).</summary>
        public int Alive
        {
            get { int n = 0; for (int i = 0; i < Max; i++) if (Time.time - born[i] < Life) n++; return n; }
        }
        /// <summary>Quante ne sono state fatte da quando c'e' il deserto.</summary>
        public int Stamped { get; private set; }

        public static DesertPrints Create(GameObject host) => host.AddComponent<DesertPrints>();

        /// <summary>Per le prove: invecchia tutte le impronte di tanti secondi (come se il vento avesse soffiato per quel tempo).</summary>
        public void Age(float seconds)
        {
            for (int i = 0; i < Max; i++) born[i] -= seconds;
            refresh = 0f;
        }

        /// <summary>Per le prove: il centro dell'ultima impronta fatta; false se non ce n'e' o se il vento l'ha gia' cancellata.</summary>
        public bool Newest(out Vector3 centre)
        {
            int i = (next + Max - 1) % Max;
            centre = (verts[i * 4] + verts[i * 4 + 2]) * 0.5f;
            return Time.time - born[i] < Life;
        }

        /// <summary>Un passo: l'impronta di un piede ai 'feet' (sotto il giocatore), con il passo che va verso 'forward' (sul piano orizzontale). Alterna sinistro e destro. Non fa niente se sotto
        /// il piede non c'e' la sabbia (un pavimento, una scala).</summary>
        public static void Stamp(Vector3 feet, Vector3 forward)
        {
            if (I != null) I.Add(feet, forward);
        }

        void Awake()
        {
            I = this;
            var shader = Shader.Find("PryingEyes/DesertPrint");
            if (shader != null) mat = new Material(shader) { name = "DesertPrint" };
            else Debug.LogWarning("[PryingEyes] deserto: lo shader delle orme non c'e'");
            mesh = new Mesh { name = "OrmeDeserto" };
            mesh.MarkDynamic();
            var uvs = new Vector2[Max * 4];
            var tris = new int[Max * 6];
            for (int i = 0; i < Max; i++)
            {
                int v = i * 4;
                uvs[v] = new Vector2(-1f, -1f); uvs[v + 1] = new Vector2(-1f, 1f); uvs[v + 2] = new Vector2(1f, 1f); uvs[v + 3] = new Vector2(1f, -1f);
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

        void Add(Vector3 feet, Vector3 forward)
        {
            if (mat == null) return;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return;
            forward.Normalize();
            right = !right;
            var side = new Vector3(forward.z, 0f, -forward.x);
            // il piede va un po' in fuori (sei gradi): gira la direzione intorno alla verticale
            var dir = Quaternion.AngleAxis(right ? 6f : -6f, Vector3.up) * forward;
            Vector3 at = feet + side * (right ? 0.11f : -0.11f);
            if (!Physics.Raycast(at + Vector3.up * 0.8f, Vector3.down, out var hit, 2.5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return;
            if (!DesertTerrain.IsSand(hit.collider)) return;
            if (DesertWorld.Brine(at.x, at.z) > 0.5f) return;               // nell'acqua bassa delle saline non resta niente
            Vector3 n = hit.normal;
            Vector3 f = (dir - n * Vector3.Dot(dir, n)).normalized;
            Vector3 r = Vector3.Cross(n, f);
            Vector3 c = hit.point + n * 0.012f;
            int i = next; next = (next + 1) % Max;
            born[i] = Time.time;
            Stamped++;
            int v = i * 4;
            verts[v] = c - r * HalfW - f * HalfL;
            verts[v + 1] = c - r * HalfW + f * HalfL;
            verts[v + 2] = c + r * HalfW + f * HalfL;
            verts[v + 3] = c + r * HalfW - f * HalfL;
            byte hx = (byte)Mathf.Clamp(Mathf.RoundToInt((dir.x * 0.5f + 0.5f) * 255f), 0, 255), hz = (byte)Mathf.Clamp(Mathf.RoundToInt((dir.z * 0.5f + 0.5f) * 255f), 0, 255);
            for (int k = 0; k < 4; k++) cols[v + k] = new Color32(hx, hz, 0, 255);
            mesh.vertices = verts;
            mesh.colors32 = cols;
            refresh = 0f;
        }

        // Update e non LateUpdate: DrawMesh vale per il fotogramma in cui si chiama, e le foto di prova fanno cam.Render() da una coroutine, che gira dopo gli Update e prima dei LateUpdate
        // (come DesertScatter e DesertLife, che disegnano in Update): in LateUpdate le impronte c'erano in gioco ma non nelle foto.
        void Update()
        {
            if (mat == null) return;
            refresh -= Time.deltaTime;
            if (refresh <= 0f)
            {
                refresh = 0.25f;
                float now = Time.time;
                bool dirty = false;
                for (int i = 0; i < Max; i++)
                {
                    float age = (now - born[i]) / Life;
                    if (age >= 1f)
                    {
                        // cancellata: il quadrato torna a un punto (non si disegna)
                        if (verts[i * 4] != verts[i * 4 + 2]) { for (int k = 0; k < 4; k++) verts[i * 4 + k] = Vector3.zero; dirty = true; }
                        continue;
                    }
                    byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(age * 255f), 0, 255);
                    if (cols[i * 4].b != a) { for (int k = 0; k < 4; k++) { var c = cols[i * 4 + k]; c.b = a; cols[i * 4 + k] = c; } dirty = true; }
                }
                if (dirty) { mesh.vertices = verts; mesh.colors32 = cols; }
            }
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, null, 0, null, false, false, false);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>L'aspetto dell'arma in mano col sigillo (rifatto il 2026-11-17: prima i metalli prendevano tutti insieme una tinta piatta).
    /// Una seconda pelle (shader PryingEyes/WeaponInfusion) sta sopra ogni pezzo dell'arma, a mezzo millimetro, e si accende DAL BASSO:
    /// il profilo dell'arma (sotto e sopra a ogni punto della sua lunghezza, dalle sagome di WeaponSurface) e un livello di calore per
    /// ogni punto della lunghezza (una texture di 48 pixel, aggiornata a ogni fotogramma) dicono allo shader fin dove e' salito il
    /// fuoco, il gelo, la corrente. Il calore lo mette la mano che passa sotto l'arma (Deposit, a ogni fotogramma del gesto) e a fine
    /// gesto sale ovunque (Flood): si vede il fronte arrivare dal basso e risalire i fianchi fino alla cima. Le particelle nascono
    /// dal sotto dell'arma (fiamme che lambiscono i fianchi, vapore freddo che cade, gocce che si staccano, archi sul fondo).
    /// A regime l'arma e' piu' calda sotto che sopra. Vive sul modello in prima persona; per ogni giocatore ha il suo materiale.</summary>
    [DefaultExecutionOrder(300)]
    public sealed class WeaponAura : MonoBehaviour
    {
        /// <summary>Il nome degli oggetti figli dei pezzi dell'arma che portano la pelle (chi clona un pezzo, come il caricatore che
        /// cade, li toglie).</summary>
        public const string OverlayName = "Sigillo";
        const int N = 48;
        /// <summary>Livello a regime: mezzo punto oltre la cima dell'arma (h = 1), cosi' il fronte esce dall'arma e tutto e' acceso.</summary>
        public const float Full = 1.6f;

        static Shader shader;
        static bool shaderTried;
        static readonly int ModeId = Shader.PropertyToID("_Mode"), StrengthId = Shader.PropertyToID("_Strength"), PulseId = Shader.PropertyToID("_Pulse"),
            BoundsId = Shader.PropertyToID("_Bounds"), ProfileId = Shader.PropertyToID("_Profile"),
            W0 = Shader.PropertyToID("_W2L0"), W1 = Shader.PropertyToID("_W2L1"), W2 = Shader.PropertyToID("_W2L2");

        WeaponRig rig;
        Transform viewRoot;
        WeaponSurface surface;
        sealed class Over { public MeshRenderer ov; public Renderer src; }
        readonly List<Over> overlays = new List<Over>();
        Material mat, blank;
        Texture2D prof;
        readonly Color[] texels = new Color[N];
        readonly float[] lo = new float[N], hi = new float[N], lvl = new float[N], xl = new float[N];
        float z0, zLen = 1f;
        bool built, ok, shown;
        int element = -1, mode = 2;
        bool cursed;
        float fade, flood, pulse, fxT, mean;
        Light glow;

        /// <summary>L'elemento acceso (12..17, 18 = Arma Maledetta), -1 spento. Per le prove.</summary>
        public int Element => element;
        /// <summary>Quanto del calore e' salito, 0..1. Per le prove.</summary>
        public float Heat => mean;
        /// <summary>Le pelli costruite. Per le prove.</summary>
        public int OverlayCount => overlays.Count;

        public static WeaponAura For(Transform viewRoot, WeaponRig rig)
        {
            if (viewRoot == null || rig == null) return null;
            var a = viewRoot.GetComponent<WeaponAura>();
            if (a == null) { a = viewRoot.gameObject.AddComponent<WeaponAura>(); a.viewRoot = viewRoot; a.rig = rig; }
            return a;
        }

        static int ModeOf(int node) => node switch { 12 => 0, 13 => 1, 14 => 2, 15 => 3, 16 => 4, 17 => 5, _ => 6 };

        static bool IsGlass(Material m) => m == null || m.name.Contains("Vetro") || m.name.Contains("Fx_") || !m.HasProperty("_BaseColor");

        // ------------------------------------------------------------------ costruzione (alla prima accensione)
        bool Build()
        {
            if (built) return ok;
            built = true;
            if (shader == null && !shaderTried)
            {
                shaderTried = true;
                shader = Shader.Find("PryingEyes/WeaponInfusion");
                if (shader == null) Debug.LogWarning("[PryingEyes] PryingEyes/WeaponInfusion non c'e' nella build: i sigilli non cambiano l'aspetto dell'arma");
            }
            if (shader == null || rig == null || rig.renderers == null || viewRoot == null) return false;
            surface = new WeaponSurface(viewRoot, rig);
            mat = new Material(shader) { name = "SigilloArma" };
            blank = new Material(shader) { name = "SigilloVetro" };
            blank.SetFloat(ModeId, 9f);
            float zMin = float.MaxValue, zMax = float.MinValue;
            Matrix4x4 w2l = viewRoot.worldToLocalMatrix;
            foreach (var r in rig.renderers)
            {
                if (r == null || r.transform == rig.reticle || r.transform == rig.scopeLens || System.Array.IndexOf(rig.lenses, r.transform) >= 0) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !(r is MeshRenderer)) continue;
                var mesh = mf.sharedMesh;
                var src = r.sharedMaterials;
                var arr = new Material[Mathf.Max(1, mesh.subMeshCount)];
                bool any = false;
                for (int k = 0; k < arr.Length; k++)
                {
                    bool glass = IsGlass(k < src.Length ? src[k] : null);
                    arr[k] = glass ? blank : mat;
                    any |= !glass;
                }
                if (!any) continue;
                var go = new GameObject(OverlayName) { layer = r.gameObject.layer };
                go.transform.SetParent(r.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = arr;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.renderingLayerMask = r.renderingLayerMask;
                mr.enabled = false;
                overlays.Add(new Over { ov = mr, src = r });
                // l'ingombro in z, come lo calcola WeaponSurface (le mesh non sono leggibili, i riquadri si')
                var m4 = w2l * r.transform.localToWorldMatrix;
                var b = mesh.bounds;
                if (Vector3.Scale(b.size, r.transform.lossyScale).magnitude > 2.5f) continue;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 q = m4.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    zMin = Mathf.Min(zMin, q.z); zMax = Mathf.Max(zMax, q.z);
                }
            }
            if (overlays.Count == 0) return false;
            if (zMax - zMin < 0.1f) { zMin = -0.15f; zMax = 0.85f; }
            z0 = zMin - 0.005f; zLen = zMax - zMin + 0.01f;
            for (int i = 0; i < N; i++)
            {
                float z = z0 + zLen * ((i + 0.5f) / N);
                float b = surface.Bottom(z), t = surface.Top(z);
                if (t - b < 0.03f) { float c = (t + b) * 0.5f; b = c - 0.015f; t = c + 0.015f; }
                lo[i] = b; hi[i] = t; xl[i] = Mathf.Min(-0.012f, surface.Left(z));
            }
            // una media con i vicini: le sagome a riquadri fanno gradini che sulla superficie si vedrebbero come strisce
            var lo2 = new float[N]; var hi2 = new float[N];
            for (int i = 0; i < N; i++)
            {
                int a = Mathf.Max(0, i - 1), c = Mathf.Min(N - 1, i + 1);
                lo2[i] = (lo[a] + lo[i] * 2f + lo[c]) * 0.25f; hi2[i] = (hi[a] + hi[i] * 2f + hi[c]) * 0.25f;
            }
            for (int i = 0; i < N; i++) { lo[i] = lo2[i]; hi[i] = hi2[i]; }
            prof = new Texture2D(N, 1, TextureFormat.RGBAHalf, false, true) { name = "ProfiloSigillo", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            mat.SetTexture(ProfileId, prof);
            mat.SetVector(BoundsId, new Vector4(z0, 1f / zLen, 0f, 0f));
            ok = true;
            return true;
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (blank != null) Destroy(blank);
            if (prof != null) Destroy(prof);
            if (glow != null) Destroy(glow.gameObject);
        }

        // ------------------------------------------------------------------ comandi
        /// <summary>Accende (element 12..17, o 18 l'Arma Maledetta) o spegne (-1) l'aspetto. 'from' (mondo): il sigillo e' appena stato
        /// tracciato e il calore parte da li' (il resto lo mette la mano con Deposit e Flood); senza 'from' l'arma e' gia' accesa a regime
        /// (cambio d'arma, ritorno). 'crawl' non serve piu': resta per i chiamanti di prima.</summary>
        public void Set(int node, bool isCursed, Vector3? from = null, float crawl = 0.6f)
        {
            int want = isCursed ? 18 : node;
            if (want == element && isCursed == cursed && from == null) return;
            int before = element;
            element = want; cursed = isCursed;
            if (want < 0) { flood = 0f; return; }
            if (!Build()) { element = -1; return; }
            mode = ModeOf(want);
            if (from.HasValue)
            {
                if (before != want) Fill(0f);
                flood = 0f;
                Deposit(from.Value, 0.3f, 0.08f);
            }
            else { Fill(Full); flood = 0f; }
        }

        void Fill(float v) { for (int i = 0; i < N; i++) lvl[i] = v; }

        /// <summary>Calore alla mano: il livello sale attorno al punto 'world' (la mano sotto l'arma), a ogni fotogramma del gesto.</summary>
        public void Deposit(Vector3 world, float amount, float radius = 0.07f)
        {
            if (!ok || element < 0 || viewRoot == null) return;
            float z = viewRoot.InverseTransformPoint(world).z;
            float inv = 1f / (radius * radius);
            for (int i = 0; i < N; i++)
            {
                float d = z0 + zLen * ((i + 0.5f) / N) - z;
                float w = Mathf.Exp(-d * d * inv);
                if (w > 0.01f) lvl[i] = Mathf.Min(Full, lvl[i] + amount * w);
            }
        }

        /// <summary>Il calore sale ovunque fino al regime in 'seconds' (a fine gesto: il fronte arriva alla cima).</summary>
        public void Flood(float seconds) { if (ok && element >= 0) flood = Full / Mathf.Max(0.1f, seconds); }

        /// <summary>Un lampo (il colpo): l'arma si accende un attimo di piu'.</summary>
        public void Pulse(float k = 1f) { if (ok && element >= 0) pulse = Mathf.Max(pulse, k); }

        /// <summary>Per le vecchie chiamate: il calore parte da 'from' e sale ovunque in 'dur' secondi.</summary>
        public void Crawl(Vector3 from, float dur) { Deposit(from, 0.3f, 0.08f); Flood(dur); }

        /// <summary>I centri dell'arma in alto e in basso al punto z per chi disegna effetti sul suo sotto (mondo). Falso se non c'e' la sagoma.</summary>
        public bool UnderPoint(float t01, out Vector3 world)
        {
            world = default;
            if (!Build() || viewRoot == null) return false;
            float z = z0 + zLen * Mathf.Clamp01(t01);
            int i = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(t01) * N), 0, N - 1);
            world = viewRoot.TransformPoint(new Vector3(0f, lo[i], z));
            return true;
        }

        // ------------------------------------------------------------------ per fotogramma
        void SetShown(bool on)
        {
            shown = on;
            foreach (var o in overlays) if (o.ov != null) o.ov.enabled = on && o.src != null && o.src.enabled && o.src.gameObject.activeInHierarchy;
            if (!on && glow != null) glow.enabled = false;
        }

        void LateUpdate()
        {
            if (!ok || viewRoot == null) return;
            float dt = Time.deltaTime;
            bool on = element >= 0;
            fade = Mathf.MoveTowards(fade, on ? 1f : 0f, dt / (on ? 0.10f : 0.5f));
            if (!on && fade <= 0f) { if (shown) SetShown(false); return; }
            shown = true;
            // una pelle per pezzo: segue il pezzo (nascosto o spento = niente pelle)
            foreach (var o in overlays)
            {
                if (o.ov == null) continue;
                bool v = o.src != null && o.src.enabled && o.src.gameObject.activeInHierarchy;
                if (o.ov.enabled != v) o.ov.enabled = v;
            }
            // il calore
            float sum = 0f;
            for (int i = 0; i < N; i++)
            {
                if (flood > 0f && lvl[i] < Full) lvl[i] = Mathf.Min(Full, lvl[i] + flood * dt);
                texels[i] = new Color(lo[i], hi[i], lvl[i], 1f);
                sum += lvl[i];
            }
            mean = sum / (N * Full);
            prof.SetPixels(texels);
            prof.Apply(false);
            pulse = Mathf.MoveTowards(pulse, 0f, dt * 3.5f);
            mat.SetFloat(ModeId, mode);
            mat.SetFloat(StrengthId, fade);
            mat.SetFloat(PulseId, pulse);
            Matrix4x4 m = viewRoot.worldToLocalMatrix;
            mat.SetVector(W0, m.GetRow(0)); mat.SetVector(W1, m.GetRow(1)); mat.SetVector(W2, m.GetRow(2));
            if (on) { Lamp(); Emit(dt); }
            else if (glow != null) glow.intensity *= Mathf.Clamp01(fade);
        }

        /// <summary>Una luce debole sul guanto e sull'arma (solo il loro strato): il fuoco sotto l'arma illumina le mani. A pochi centimetri
        /// il quadrato inverso della distanza la porterebbe al bianco: sta a una trentina di centimetri, sotto e a sinistra.</summary>
        void Lamp()
        {
            if (glow == null)
            {
                var go = new GameObject("LuceSigillo");
                go.transform.SetParent(viewRoot, false);
                glow = go.AddComponent<Light>();
                glow.type = LightType.Point; glow.range = 0.9f; glow.shadows = LightShadows.None;
                Stealth.SetLightLayers(glow, Weapon.ViewLayer);
            }
            glow.enabled = true;
            Color c = MagicFx.OfInfusion(cursed ? 18 : element);
            glow.color = c;
            float flick = element == 14 || cursed ? 0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 9f, 0.37f) : element == 16 ? 0.6f + 0.8f * Mathf.PerlinNoise(Time.time * 24f, 0.11f) : 1f;
            float k = element == 14 ? 0.16f : element == 17 ? 0.09f : element == 16 ? 0.13f : 0.10f;
            glow.intensity = k * flick * Mathf.Clamp01(mean * 1.4f) * fade;
            glow.transform.localPosition = new Vector3(-0.10f, lo[N / 2] - 0.22f, z0 + zLen * 0.5f);
        }

        /// <summary>Le particelle nascono dal sotto dell'arma e salgono sui fianchi (o cadono da sotto: gelo, acido, sangue), solo dove il
        /// calore e' arrivato.</summary>
        void Emit(float dt)
        {
            fxT -= dt;
            if (fxT > 0f || mean < 0.04f) return;
            fxT = cursed ? 0.045f : 0.055f;
            int i = -1;
            for (int k = 0; k < 6; k++) { int c = Random.Range(0, N); if (lvl[c] > 0.12f) { i = c; break; } }
            if (i < 0) return;
            float z = z0 + zLen * ((i + 0.5f + Random.Range(-0.5f, 0.5f)) / N);
            float half = Mathf.Max(0.012f, -xl[i]);
            float side = Random.value < 0.5f ? -1f : 1f;
            float reach = Mathf.Clamp01((lvl[i] - 0.05f) / 1.0f);                       // quanto del fianco il calore ha raggiunto qui
            float y = Mathf.Lerp(lo[i], hi[i], Mathf.Pow(Random.value, 2.0f) * reach);   // piu' spesso in basso: e' la sorgente
            Vector3 under = viewRoot.TransformPoint(new Vector3(Random.Range(-half, half), lo[i] - 0.004f, z));
            Vector3 flank = viewRoot.TransformPoint(new Vector3(side * (half + 0.004f), y, z));
            Vector3 up = Vector3.up;
            int el = cursed ? 18 : element;
            switch (el)
            {
                case 14:
                    // lingue piccole attaccate al metallo, dove il fuoco e' piu' vivo (in basso e sul fronte): non candele sospese
                    MagicFx.Flame(flank, up, 0.17f + 0.10f * reach);
                    if (Random.value < 0.3f) MagicFx.Flame(under, up, 0.2f);
                    if (MagicVfx.On && Random.value < 0.3f)
                        MagicVfx.Emit(MagicVfx.Kind.Ember, flank, up * Random.Range(0.08f, 0.3f) + Random.insideUnitSphere * 0.10f, Random.Range(0.0025f, 0.005f), Random.Range(0.5f, 1.0f), MagicFx.Fire);
                    if (MagicVfx.On && Random.value < 0.07f)
                        MagicVfx.Emit(MagicVfx.Kind.Smoke, viewRoot.TransformPoint(new Vector3(0f, hi[i] + 0.015f, z)), up * 0.12f + Random.insideUnitSphere * 0.02f, 0.025f, 0.9f, new Color(0.1f, 0.09f, 0.085f, 0.22f));
                    break;
                case 17:
                    MagicFx.Mist(under, Vector3.down * 0.07f + Random.insideUnitSphere * 0.03f, MagicFx.Frost, 0.014f, 0.8f, 0.17f);
                    if (MagicVfx.On && Random.value < 0.25f)
                        MagicVfx.Emit(MagicVfx.Kind.Shard, flank, Random.insideUnitSphere * 0.08f + Vector3.down * 0.05f, Random.Range(0.006f, 0.011f), 0.4f, new Color(0.82f, 0.94f, 1f));
                    if (Random.value < 0.15f) MagicFx.Glow(flank, Color.white, 0.008f, 0.18f, 0.9f);
                    break;
                case 16:
                    if (Random.value < 0.4f) MagicFx.Arc(under, under + (Random.onUnitSphere + Vector3.down * 0.4f) * 0.045f, MagicFx.Shock, 0.003f, 3, 0.8f);
                    if (Random.value < 0.25f) MagicFx.Arc(flank, flank + (up * 0.5f + Random.onUnitSphere) * 0.04f, MagicFx.Shock, 0.003f, 3, 0.8f);
                    if (Random.value < 0.3f) MagicFx.Sparks(under, MagicFx.Shock, 1, 0.9f, 0.004f);
                    break;
                case 15:
                    if (Random.value < 0.6f) MagicFx.Drip(under, MagicFx.Acid, 0.0048f);
                    if (Random.value < 0.35f) MagicFx.Mist(flank, up * 0.05f, MagicFx.Acid, 0.009f, 0.6f, 0.16f);
                    if (Random.value < 0.2f) MagicFx.Glow(flank, MagicFx.Acid, 0.01f, 0.2f, 0.7f);
                    break;
                case 13:
                    if (Random.value < 0.6f) MagicFx.Drip(under, new Color(0.35f, 0.01f, 0.01f), 0.0052f);
                    if (MagicVfx.On && Random.value < 0.2f)
                        MagicVfx.Emit(MagicVfx.Kind.Drop, flank, Vector3.down * 0.08f, 0.007f, 0.5f, new Color(0.4f, 0.01f, 0.01f));
                    break;
                case 12:
                    if (Random.value < 0.45f) MagicFx.Sparks(flank, MagicFx.Crit, 1, 0.9f, 0.006f);
                    if (Random.value < 0.3f) MagicFx.Glow(flank, MagicFx.Crit, 0.012f, 0.22f, 0.65f);
                    break;
                default:
                    if (MagicVfx.On)
                    {
                        MagicVfx.Flame(flank, up, 0.24f, MagicVfx.CursedFire);
                        if (Random.value < 0.25f) MagicVfx.Emit(MagicVfx.Kind.Smoke, flank, up * 0.15f, 0.025f, 0.9f, new Color(0.06f, 0.01f, 0.01f, 0.32f));
                        if (Random.value < 0.1f) MagicVfx.Emit(MagicVfx.Kind.Rune, flank + up * 0.02f, up * 0.03f, 0.018f, 0.7f, MagicFx.Cursed, 0f);
                        break;
                    }
                    MagicFx.Flame(flank, up, 0.24f);
                    break;
            }
        }
    }
}

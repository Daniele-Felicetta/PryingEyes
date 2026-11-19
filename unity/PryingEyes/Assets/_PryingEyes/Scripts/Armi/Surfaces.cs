using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Le famiglie di superficie che un proiettile puo' colpire: decidono polvere, schegge, scintille, foro e suono (Fx.Impact).</summary>
    public enum Surface : byte { Plaster, Stone, Tile, Wood, Metal, Fabric, Glass, Sand }

    /// <summary>Di che cosa e' fatto il punto colpito (2026-11-15). Il materiale si prende dal triangolo colpito (i gusci delle stanze sono
    /// una mesh con un materiale per pavimento, muro, zoccolo, soffitto...), dal renderer dell'oggetto o, per i blocchi di collisione senza
    /// renderer (il pavimento), dalla superficie visibile nello stesso punto. Il nome del materiale si legge come lo chiede il generatore
    /// (pav_parquet, mur_pietra...: la tabella di ProcKit) e altrimenti come si chiama, insieme al nome dell'oggetto, cercando parole
    /// chiave italiane e inglesi. Risultato in cache per materiale.</summary>
    public static class Surfaces
    {
        static readonly Dictionary<Material, Surface> cache = new Dictionary<Material, Surface>();
        static readonly Dictionary<Material, string> kitKey = new Dictionary<Material, string>();
        static Proc.ProcKit indexed;
        static readonly RaycastHit[] probe = new RaycastHit[8];
        static readonly List<Material> mats = new List<Material>(8);

        // in ordine: vince la prima famiglia con una parola che compare nel nome (parole scelte perche' non stiano dentro altre:
        // "_osso" e non "osso", che sta in "rosso"; "_oro" e non "oro", che sta in "coro")
        static readonly (Surface s, string[] words)[] rules =
        {
            (Surface.Sand,   new[] { "desertsand", "sabbia_" }),
            (Surface.Glass,  new[] { "vetro", "vetrat", "glass", "specchio", "mirror", "neon", "lampadin", "bulb", "cornea", "window", "uscita", "pannello_luce" }),
            (Surface.Metal,  new[] { "ferro", "acciaio", "steel", "ottone", "brass", "_oro$", "gold", "dorat", "metal", "ruggine", "rust",
                                     "ghisa", "_rame", "rame_", "copper", "serrand", "shutter", "griglia", "grata", "armi_", "canne", "lamiera",
                                     "cancell", "inferriat", "catena", "chain", "rack", "iron", "candelabr", "lampadar", "chandelier", "turibol", "campana",
                                     "indicatore" }),
            (Surface.Wood,   new[] { "legno", "wood", "parquet", "palco", "boiserie", "trave", "quercia", "noce", "panca", "pew", "tavol",
                                     "table", "sedia", "chair", "armad", "librer", "shelf", "scaffal", "cassa", "botte", "barrel", "crate",
                                     "lacca", "ebano", "confession", "leggio", "inginocch", "bancon", "porta", "door", "plank" }),
            (Surface.Tile,   new[] { "piastrell", "tile", "porcellan", "ceram", "lavandin", "vasca", "sink" }),
            (Surface.Fabric, new[] { "tessut", "velluto", "velvet", "tenda", "curtain", "lenzuol", "moquet", "carpet", "felt", "cuscin", "pillow",
                                     "leather", "cuoio", "seta", "silk", "stoffa", "gomma", "rubber", "linoleo", "paralume", "lampshade", "paper",
                                     "carta", "cera", "materass", "wrap", "cardboard", "libro", "book" }),
            (Surface.Stone,  new[] { "pietra", "stone", "marmo", "marble", "lastre", "cripta", "volta", "scultur", "statua", "_osso", "ossa", "bone",
                                     "rock", "brick", "mattoni", "cemento", "concrete", "asphalt", "pavement", "altare", "servizio", "colonn", "column" }),
            (Surface.Plaster, new[] { "intonaco", "plaster", "sof_", "vernice", "paint", "wall", "ceil", "damasco", "mur_" }),
        };

        /// <summary>La famiglia di un nome (chiave del kit, nome del materiale o dell'oggetto); 'known' falso se nessuna parola corrisponde.</summary>
        public static Surface OfName(string name, out bool known)
        {
            known = false;
            if (string.IsNullOrEmpty(name)) return Surface.Plaster;
            string n = name.ToLowerInvariant().Replace(" (instance)", "");
            foreach (var (s, words) in rules)
                foreach (var w in words)
                    // "parola$" vale solo in fondo al nome ("abate_oro" si', "pendolo_orologio" no)
                    if (w[w.Length - 1] == '$' ? n.EndsWith(w.Substring(0, w.Length - 1)) : n.Contains(w)) { known = true; return s; }
            return Surface.Plaster;
        }

        /// <summary>La chiave con cui il generatore chiede questo materiale (pav_marmo...), o null se non e' del kit.</summary>
        public static string KitKey(Material m)
        {
            if (m == null) return null;
            var kit = Proc.ProcKit.I;
            if (kit != indexed)
            {
                indexed = kit;
                kitKey.Clear(); cache.Clear();
                if (kit != null) foreach (var r in kit.mats) if (r.mat != null && !kitKey.ContainsKey(r.mat)) kitKey[r.mat] = r.name;
            }
            return kitKey.TryGetValue(m, out var k) ? k : null;
        }

        /// <summary>La famiglia di un materiale (e dell'oggetto che lo porta, se il nome del materiale non dice nulla).</summary>
        public static Surface Of(Material m, string objectName = null)
        {
            if (m != null)
            {
                string key = KitKey(m);                           // (rifa l'indice, e svuota la cache, se il kit e' cambiato)
                if (cache.TryGetValue(m, out var s)) return s;
                if (key != null) { s = OfName(key, out _); cache[m] = s; return s; }
                s = OfName(m.name, out bool known);
                if (known) { cache[m] = s; return s; }
            }
            return OfName(objectName, out _);                     // il materiale non dice nulla: decide il nome dell'oggetto
        }

        /// <summary>La famiglia del punto colpito da un raggio.</summary>
        public static Surface Of(RaycastHit h)
        {
            var m = MaterialAt(h, out var owner);
            return Of(m, owner != null ? owner.name : (h.collider != null ? h.collider.name : null));
        }

        /// <summary>Per le prove: la famiglia del punto, il nome che l'ha decisa e se una regola l'ha riconosciuta (falso = intonaco per
        /// difetto, perche' nessuna parola chiave corrisponde).</summary>
        public static Surface Describe(RaycastHit h, out string name, out bool known)
        {
            var m = MaterialAt(h, out var owner);
            string key = KitKey(m);
            if (key != null) { name = "kit:" + key; return OfName(key, out known); }
            if (m != null)
            {
                var s = OfName(m.name, out known);
                if (known) { name = m.name; return s; }
            }
            string obj = owner != null ? owner.name : h.collider != null ? h.collider.name : null;
            name = (m != null ? m.name : "(nessun materiale)") + " / " + obj;
            return OfName(obj, out known);
        }

        /// <summary>Il materiale visibile nel punto colpito: del triangolo per le mesh a piu' materiali, del renderer dell'oggetto (o del suo
        /// pezzo piu' vicino al punto), o della superficie visibile nello stesso punto per i collider senza renderer.</summary>
        public static Material MaterialAt(RaycastHit h, out Renderer owner)
        {
            owner = null;
            var col = h.collider;
            if (col == null) return null;
            var r = col.GetComponent<Renderer>();
            if (r != null)
            {
                owner = r;
                r.GetSharedMaterials(mats);                  // senza allocare a ogni colpo
                if (mats.Count > 1 && col is MeshCollider mc && mc.sharedMesh != null && h.triangleIndex >= 0)
                {
                    var mesh = mc.sharedMesh;
                    int idx = h.triangleIndex * 3;
                    for (int s = 0; s < mesh.subMeshCount && s < mats.Count; s++)
                    {
                        var sd = mesh.GetSubMesh(s);
                        if (idx >= sd.indexStart && idx < sd.indexStart + sd.indexCount) return mats[s];
                    }
                }
                return mats.Count > 0 ? mats[0] : null;
            }
            // un arredo col collider sulla radice e i pezzi sotto: il pezzo piu' vicino al punto
            var parts = col.GetComponentsInChildren<Renderer>();
            if (parts.Length > 0 && parts.Length <= 24)
            {
                float best = float.MaxValue;
                foreach (var p in parts)
                {
                    if (p is ParticleSystemRenderer || p is LineRenderer) continue;
                    float d = p.bounds.SqrDistance(h.point);
                    if (d < best) { best = d; owner = p; }
                }
                if (owner != null) return owner.sharedMaterial;
            }
            var up = col.GetComponentInParent<Renderer>();
            if (up != null) { owner = up; return up.sharedMaterial; }
            // nessun renderer (il blocco del pavimento): la superficie visibile nello stesso punto
            int n = Physics.RaycastNonAlloc(h.point + h.normal * 0.04f, -h.normal, probe, 0.09f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (probe[i].collider == col) continue;
                var pr = probe[i].collider.GetComponent<Renderer>();
                if (pr == null) continue;
                return MaterialAt(probe[i], out owner);
            }
            return null;
        }

        /// <summary>Stima della luce che arriva in un punto, senza ombre: ambiente, luci della riserva (inverso del quadrato, finestra del
        /// raggio come in URP) e torcia. 1 = come una superficie chiara vicino a una lampada. Gli effetti Unlit (polvere, schegge) ne
        /// prendono la tinta, cosi' al buio restano scuri invece di brillare.</summary>
        public static float LightAt(Vector3 p) => LightColorAt(p).grayscale;

        /// <summary>Come LightAt, col colore delle luci (le lampade calde scaldano il foro): per i fori e le macchie, che sono Unlit e si
        /// tingono da soli (Fx, ogni 0,15 s).</summary>
        public static Color LightColorAt(Vector3 p) => LightColorAt(p, Vector3.zero);

        /// <summary>Con la normale 'n' (non zero) conta anche l'angolo: una lampada sullo stesso muro lo prende di striscio e il foro resta
        /// scuro come il muro (un filo di luce di rimbalzo resta anche di taglio).</summary>
        public static Color LightColorAt(Vector3 p, Vector3 n)
        {
            bool facing = n.sqrMagnitude > 0.5f;
            Color e = RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Trilight ? RenderSettings.ambientEquatorColor : RenderSettings.ambientLight;
            foreach (var pl in Proc.LightPool.All)
            {
                var l = pl?.l;
                if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
                Vector3 toL = l.transform.position - p;
                float d2 = toL.sqrMagnitude, r2 = l.range * l.range;
                if (d2 >= r2) continue;
                float f = 1f - (d2 * d2) / (r2 * r2);
                float ndl = facing ? Mathf.Clamp01(Vector3.Dot(n, toL / Mathf.Sqrt(Mathf.Max(d2, 1e-6f))) * 0.85f + 0.15f) : 1f;
                e += l.color * (l.intensity * f * f * ndl / Mathf.Max(d2, 0.25f));
            }
            var torch = Torch.I;
            if (torch != null && torch.on && torch.Beam != null && torch.Beam.isActiveAndEnabled)
            {
                var b = torch.Beam;
                Vector3 d = p - b.transform.position;
                float dist2 = d.sqrMagnitude;
                if (dist2 < b.range * b.range)
                {
                    Vector3 dn = d.normalized;
                    float cos = Vector3.Dot(dn, b.transform.forward), outer = Mathf.Cos(b.spotAngle * 0.5f * Mathf.Deg2Rad);
                    float ndl = facing ? Mathf.Clamp01(-Vector3.Dot(n, dn) * 0.85f + 0.15f) : 1f;
                    if (cos > outer) e += b.color * (b.intensity * ndl * Mathf.Clamp01((cos - outer) / Mathf.Max(0.02f, 1f - outer) * 2f) / Mathf.Max(dist2, 0.25f));
                }
            }
            e.a = 1f;
            return e;
        }
    }
}

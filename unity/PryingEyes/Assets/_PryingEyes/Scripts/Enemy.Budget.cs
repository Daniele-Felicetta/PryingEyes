using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>Budget di rendering dei corpi nelle orde. Un umanoide ha 110-170 mila vertici (le fibre sfilacciate della tunica da sole ne
    /// hanno 62 mila) e il round ne porta fino a 24: con 24 corpi pieni la GPU impiegava 52 ms per fotogramma (90 con tutte le ombre).
    /// Due leve, ricalcolate da LightPool.Tick cinque volte al secondo:
    /// 1) ombre: fanno ombra solo i corpi piu' vicini a chi guarda, fino a un peso massimo (1 = un umanoide pieno, un occhio volante pesa
    ///    un settimo): ogni luce con ombra ridisegna i corpi che le stanno intorno, un punto luce sono fino a sei facce;
    /// 2) dettaglio: la mesh di ogni parte scende ai livelli automatici (importatore: generateMeshLods) con la distanza, e piu' in fretta
    ///    se la folla e' grande; le fibre e le parti minute sotto la veste spariscono lontano. Unity da solo non scende (misurato: i
    ///    corpi restano al livello 0), quindi il livello si forza qui con Renderer.forceMeshLod.
    /// Da vicino (corpo a corpo) il corpo e' sempre intero.</summary>
    public partial class Enemy
    {
        /// <summary>Somma massima dei pesi dei corpi che fanno ombra insieme (circa quattro umanoidi e qualche volante).</summary>
        public static float ShadowBudget = 4.5f;
        /// <summary>Oltre questa distanza dal punto di vista un corpo non fa mai ombra.</summary>
        public static float ShadowRange = 14f;
        /// <summary>Spento, ogni corpo fa ombra come il suo prefab (la sonda delle orde lo usa per misurare il costo pieno).</summary>
        public static bool ShadowBudgetOn = true;
        /// <summary>Spento, ogni parte resta al livello 0 e visibile (la sonda delle orde, per misurare il costo pieno).</summary>
        public static bool DetailBudgetOn = true;
        /// <summary>Quanti corpi fanno ombra adesso (per le prove).</summary>
        public static int ShadowCasters { get { int n = 0; foreach (var e in all) if (e != null && e.shadowsOn && e.shadowWeight > 0f) n++; return n; } }

        /// <summary>Distanze (m) alle quali una parte passa al livello 1, 2, 3...: da 3 m il primo, a 17 m il quinto.</summary>
        static readonly float[] LodDistances = { 3f, 5f, 8f, 12f, 17f };
        /// <summary>Le fibre sfilacciate (62 mila vertici sottilissimi) si vedono solo da vicino; la sottoveste e la collana stanno sotto la
        /// tunica e a distanza non si distinguono.</summary>
        const float FibreRange = 4.5f, MinorRange = 8f;
        const float FibreFullRange = 2.5f;

        enum Part : byte { Fixed, Body, Fibres, Minor }

        /// <summary>Isteresi (m) del cambio di livello: un corpo che e' sceso a un livello piu' basso risale solo se si avvicina di tanto
        /// oltre la soglia, cosi' uno fermo sul confine non cambia mesh a ogni giro del budget.</summary>
        const float LodHysteresis = 0.6f;

        ShadowCastingMode[] shadowModes;
        Part[] partRoles;
        short[] partLods;
        Mesh[] partMeshes;          // mesh originale di ogni parte con livelli
        Mesh[][] partLadders;       // scala compatta (BodyLodLibrary); null = si ricade sui LOD di Unity
        Bounds[] partBounds;        // localBounds originali: cambiare mesh li azzera
        float shadowWeight;
        bool shadowsOn = true;
        static readonly List<(float key, Enemy enemy)> shadowOrder = new List<(float, Enemy)>();

        /// <summary>Modo d'ombra originale di ogni renderer (chi non ne fa, come i bagliori, resta senza), ruolo e peso del corpo.</summary>
        void CacheRenderParts()
        {
            if (shadowModes != null) return;
            var list = renderers;
            int n = list != null ? list.Length : 0;
            shadowModes = new ShadowCastingMode[n];
            partRoles = new Part[n];
            partLods = new short[n];
            partMeshes = new Mesh[n];
            partLadders = new Mesh[n][];
            partBounds = new Bounds[n];
            int vertices = 0;
            for (int i = 0; i < n; i++)
            {
                var r = list[i];
                if (r == null) continue;
                shadowModes[i] = r.shadowCastingMode;
                partLods[i] = -1;
                Mesh mesh = r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh != null && shadowModes[i] != ShadowCastingMode.Off && (r is SkinnedMeshRenderer || r is MeshRenderer)) vertices += mesh.vertexCount;
                if (!(r is SkinnedMeshRenderer) || mesh == null || mesh.lodCount < 2) continue;      // senza livelli (creature procedurali, accessori) non si tocca
                string name = r.name;
                partRoles[i] = name == "FiliSfilacciati" ? Part.Fibres : name == "sottoveste" || name == "metallo" ? Part.Minor : Part.Body;
                partMeshes[i] = mesh;
                partLadders[i] = BodyLodLibrary.Ladder(mesh);
                partBounds[i] = ((SkinnedMeshRenderer)r).localBounds;
            }
            shadowWeight = vertices / 100000f;
        }

        /// <summary>Mette la parte al livello 'lod' (0 = mesh intera). Con la scala compatta cambia proprio la mesh, quindi la pelle elabora
        /// solo i vertici di quel livello; senza, forza il livello automatico di Unity (che accorcia gli indici ma non i vertici).</summary>
        void ApplyLod(int i, int lod)
        {
            var skin = (SkinnedMeshRenderer)renderers[i];
            var ladder = partLadders[i];
            if (ladder != null)
            {
                Mesh wanted = lod <= 0 ? partMeshes[i] : ladder[Mathf.Min(lod, ladder.Length) - 1];
                if (skin.sharedMesh != wanted)
                {
                    skin.sharedMesh = wanted;
                    skin.localBounds = partBounds[i];
                }
                if (skin.forceMeshLod != -1) skin.forceMeshLod = -1;
            }
            else skin.forceMeshLod = (short)(lod <= 0 ? -1 : lod);
            partLods[i] = (short)lod;
        }

        void SetShadows(bool on)
        {
            CacheRenderParts();
            if (on == shadowsOn) return;
            shadowsOn = on;
            for (int i = 0; i < shadowModes.Length; i++)
                if (renderers[i] != null) renderers[i].shadowCastingMode = on ? shadowModes[i] : ShadowCastingMode.Off;
        }

        /// <summary>Porta ogni parte al dettaglio che merita a questa distanza. 'scale' (1 = poca folla) accorcia le distanze quando i corpi
        /// pieni sono tanti.</summary>
        void SetDetail(float distance, float scale)
        {
            CacheRenderParts();
            for (int i = 0; i < partRoles.Length; i++)
            {
                var r = renderers[i];
                if (r == null || partRoles[i] == Part.Fixed) continue;
                if (!DetailBudgetOn)
                {
                    if (partLods[i] != -1) { ApplyLod(i, 0); partLods[i] = -1; }
                    if (partRoles[i] != Part.Body && !r.enabled) r.enabled = true;
                    continue;
                }
                int top = partMeshes[i].lodCount - 1;
                int current = partLods[i];
                int lod = 0;
                switch (partRoles[i])
                {
                    case Part.Fibres:
                        SetPartOn(r, distance <= FibreRange * scale);
                        lod = distance <= FibreFullRange * scale - (current > 0 ? LodHysteresis : 0f) ? 0 : 2;
                        break;
                    case Part.Minor:
                        SetPartOn(r, distance <= MinorRange * scale);
                        goto default;
                    default:
                        while (lod < LodDistances.Length)
                        {
                            float edge = LodDistances[lod] * scale;
                            if (current > lod) edge -= LodHysteresis;       // gia' piu' in basso: risale solo se si avvicina davvero
                            if (distance <= edge) break;
                            lod++;
                        }
                        break;
                }
                lod = Mathf.Min(lod, top);
                if (current != lod) ApplyLod(i, lod);
            }
        }

        static void SetPartOn(Renderer r, bool on)
        {
            if (r.enabled != on) r.enabled = on;
        }

        /// <summary>Un corpo appena nato non fa ombra finche' il prossimo giro del budget non lo ammette (al piu' 0,2 s), e nasce gia' al
        /// dettaglio della sua distanza: cosi' una raffica di nascite non paga per un attimo il costo pieno di tutti.</summary>
        static void BeginBudget(Enemy e)
        {
            if (ShadowBudgetOn) e.SetShadows(false);
            var cam = Camera.main;
            if (cam != null) e.SetDetail(Vector3.Distance(e.transform.position, cam.transform.position), CrowdScale(all.Count));
        }

        /// <summary>Con molti corpi in scena ogni distanza di cambio livello si accorcia (fino a un terzo).</summary>
        static float CrowdScale(int crowd) => crowd <= 8 ? 1f : crowd <= 14 ? 0.8f : crowd <= 20 ? 0.65f : 0.55f;

        public static void UpdateBudgets(Vector3 viewer)
        {
            shadowOrder.Clear();
            int heavy = 0;
            foreach (var e in all)
            {
                if (e == null) continue;
                e.CacheRenderParts();
                if (e.shadowWeight >= 0.5f) heavy++;
            }
            float scale = CrowdScale(heavy);
            foreach (var e in all)
            {
                if (e == null) continue;
                float distance = Vector3.Distance(e.transform.position, viewer);
                e.SetDetail(distance, scale);
                if (!ShadowBudgetOn) { e.SetShadows(true); continue; }
                // isteresi: chi ha gia' l'ombra la tiene un po' piu' a lungo, cosi' due corpi alla pari non la rimbalzano
                shadowOrder.Add((e.shadowsOn ? distance - 1.5f : distance, e));
            }
            if (!ShadowBudgetOn) return;
            shadowOrder.Sort((a, b) => a.key.CompareTo(b.key));
            float used = 0f;
            foreach (var (_, e) in shadowOrder)
            {
                bool on = Vector3.Distance(e.transform.position, viewer) <= ShadowRange && (e.shadowWeight <= 0f || used <= 0f || used + e.shadowWeight <= ShadowBudget);
                if (on) used += e.shadowWeight;
                e.SetShadows(on);
            }
        }
    }
}

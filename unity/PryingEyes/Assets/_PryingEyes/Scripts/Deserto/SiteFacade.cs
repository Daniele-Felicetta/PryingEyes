using System.Collections.Generic;
using PryingEyes.Proc;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.Deserto
{
    /// <summary>L'esterno di un luogo visto da lontano. Il guscio vero (muri con le aperture, pavimenti, collider) si costruisce solo entro poche centinaia di metri,
    /// ma i luoghi si vedono fino al limite della sabbia: oltre quella distanza ogni luogo ha questa pelle, disegnata dalla pianta con gli stessi tetti, le stesse
    /// tinte e le finestre dipinte (alcune accese), un pezzo di poche stanze per volta, senza collider e senza ombre. Quando il guscio e' completo la pelle si spegne
    /// (sporge di pochi centimetri dal muro vero, quindi i due non si sovrappongono mai a filo mentre il guscio si costruisce); quando il guscio sparisce si riaccende.</summary>
    public sealed class SiteFacade
    {
        /// <summary>Entro questa distanza (m) dal bordo del luogo la pelle si costruisce; oltre la seconda si butta.</summary>
        public const float BuildRadius = 760f, KeepRadius = 830f;
        const int RoomsPerPiece = 14;

        public readonly Site site;
        readonly GameObject root;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<(int unit, int from, int to)> pieces = new List<(int, int, int)>();
        int next;
        bool shown = true;
        readonly List<GameObject> banners = new List<GameObject>();          // l'albero sul tetto di una torre intera e i suoi stendardi di muro: non stanno sotto 'root' (la pelle si spegne quando c'e' il guscio, loro restano)
        bool bannersDone;

        public bool Done => next >= pieces.Count;
        public bool Shown => shown;
        public int Vertices { get; private set; }
        /// <summary>Quanti pezzi (di 14 stanze l'uno) servono a costruirla: se ne fa uno per fotogramma.</summary>
        public int Pieces => pieces.Count;

        Plan PlanOf(int u) => site.IsStack ? site.levels[u] : site.plan;
        float YOf(int u) => site.IsStack ? site.LevelY(u) : site.padY;

        public SiteFacade(Site s, Transform parent)
        {
            site = s;
            root = new GameObject($"Lontano_{s.cx}_{s.cz}_{s.kind}");
            root.transform.SetParent(parent, false);
            if (!s.IsStack) AddPieces(0);
            else if (s.IsTower) for (int u = 0; u < s.LevelCount; u++) AddPieces(u);
            else AddPieces(s.entrance);                         // di una catacomba fuori dalla sabbia c'e' solo il mausoleo
        }

        void AddPieces(int u)
        {
            int n = PlanOf(u).rooms.Count;
            for (int a = 0; a < n; a += RoomsPerPiece) pieces.Add((u, a, a + RoomsPerPiece));
        }

        /// <summary>Costruisce il prossimo pezzo. Falso se la pelle e' finita.</summary>
        public bool Step()
        {
            if (Done) return false;
            var pc = pieces[next++];
            var plan = PlanOf(pc.unit);
            // i tetti e i merli solo sull'ultimo livello (la cima di una torre, l'ingresso di una catacomba), come per il guscio
            bool top = !site.IsStack || (site.IsTower ? pc.unit == site.LevelCount - 1 : pc.unit == site.entrance);
            var mb = new MeshBuilder();
            FloorBuilder.FacadeBuild(plan, YOf(pc.unit), site.palette, top, site.IsStack && top, true, false, mb, mb, pc.from, pc.to, site.IsTower && top);
            if (top && site.IsTower && !bannersDone && pc.to >= plan.rooms.Count)
            {
                bannersDone = true;
                var bn = DesertBanner.OnTower(root.transform.parent, site);
                if (bn != null) banners.Add(bn.transform.parent.gameObject);
                foreach (var w in DesertBanner.OnTowerWalls(root.transform.parent, site)) banners.Add(w.transform.parent.gameObject);
            }
            if (mb.Empty) return true;
            var mesh = mb.ToMesh($"Lontano_{site.cx}_{site.cz}_{pc.unit}_{pc.from}", out var mats, false);
            mesh.UploadMeshData(true);
            meshes.Add(mesh);
            Vertices += mesh.vertexCount;
            var g = new GameObject("Pezzo");
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return true;
        }

        public void SetVisible(bool v)
        {
            if (v == shown) return;
            shown = v;
            if (root != null) root.SetActive(v);
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            foreach (var b in banners) if (b != null) Object.Destroy(b);
            banners.Clear();
            foreach (var m in meshes) if (m != null) Object.Destroy(m);
            meshes.Clear();
        }
    }
}

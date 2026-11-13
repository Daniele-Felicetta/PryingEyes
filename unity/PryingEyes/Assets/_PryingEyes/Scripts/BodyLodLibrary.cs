using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    /// <summary>Copie compatte dei livelli di dettaglio degli umanoidi. I LOD automatici di Unity (importatore: generateMeshLods) accorciano
    /// solo l'elenco degli indici: il buffer dei vertici resta intero (110-170 mila per corpo) e la pelle (skinning) li elabora tutti a ogni
    /// fotogramma, anche per un corpo lontano. Qui ogni livello e' una mesh a se', con i soli vertici che usa, gli stessi pesi e le stesse pose
    /// di legatura: Enemy.Budget ne cambia la mesh con la distanza. Si genera con Editor/BodyLodBuilder.cs (Resources/BodyLods.asset); se un
    /// modello viene rifatto e il numero di vertici o di ossa non torna, la voce si ignora e si ricade sui LOD di Unity.</summary>
    [PreferBinarySerialization]     // le mesh in YAML pesano il triplo (51 MB invece di una ventina)
    public sealed class BodyLodLibrary : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public Mesh source;
            public int vertices, bones;
            public Mesh[] lods;         // lods[0] e' il livello 1, lods[1] il 2...
        }

        public List<Entry> entries = new List<Entry>();

        static Dictionary<Mesh, Mesh[]> map;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { map = null; }

        /// <summary>La scala dei livelli compatti di una mesh (null se non c'e' o non e' piu' quella del modello).</summary>
        public static Mesh[] Ladder(Mesh source)
        {
            if (source == null) return null;
            if (map == null)
            {
                map = new Dictionary<Mesh, Mesh[]>();
                var library = Resources.Load<BodyLodLibrary>("BodyLods");
                if (library != null)
                    foreach (var e in library.entries)
                        if (e.source != null && e.lods != null && e.lods.Length > 0 && e.source.vertexCount == e.vertices && e.source.bindposeCount == e.bones)
                            map[e.source] = e.lods;
            }
            return map.TryGetValue(source, out var ladder) ? ladder : null;
        }
    }
}

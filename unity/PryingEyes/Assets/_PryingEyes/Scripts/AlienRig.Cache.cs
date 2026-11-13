using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    public sealed partial class AlienRig
    {
        // Tre sagome immutabili per partita; ogni creatura mantiene i propri materiali che cambiano al colpo/morte.
        static readonly Dictionary<Enemy.EnemyType,AlienRig> templates = new();

        static AlienRig Template(Enemy.EnemyType kind)
        {
            if(templates.TryGetValue(kind,out var rig)&&rig!=null)return rig;
            var source=new GameObject("Sagoma "+kind);
            rig=source.AddComponent<AlienRig>();rig.Build(kind);
            source.SetActive(false);DontDestroyOnLoad(source);
            templates[kind]=rig;return rig;
        }

        public static void Warmup()
        {
            Template(Enemy.EnemyType.Veglia);
            Template(Enemy.EnemyType.Medusa);
            Template(Enemy.EnemyType.Portachiavi);
        }

        public static AlienRig Create(Enemy.EnemyType kind)
        {
            var source=Template(kind);
            var copy=Instantiate(source.gameObject);copy.name=kind.ToString();
            var rig=copy.GetComponent<AlienRig>();rig.ownsGeometry=false;rig.kind=kind;
            Transform JointCopy(Transform original)=>original==null?null:
                original==source.transform?rig.transform:JointCopy(original.parent).GetChild(original.GetSiblingIndex());
            rig.model=JointCopy(source.model);
            rig.Shell=new Material(source.Shell);rig.Glow=new Material(source.Glow);
            rig.membrane=source.membrane;rig.vigilMetal=source.vigilMetal;
            if(source.vigilFire!=null)rig.vigilFire=new Material(source.vigilFire);
            if(source.vigilHeart!=null)rig.vigilHeart=new Material(source.vigilHeart);
            Material Instance(Material material)=>material==source.Shell?rig.Shell:
                material==source.Glow?rig.Glow:
                source.vigilFire!=null&&material==source.vigilFire?rig.vigilFire:
                source.vigilHeart!=null&&material==source.vigilHeart?rig.vigilHeart:material;
            foreach(var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)materials[i]=Instance(materials[i]);
                renderer.sharedMaterials=materials;
            }
            foreach(var limb in source.limbs)
                rig.limbs.Add((JointCopy(limb.joint),limb.axis,limb.phase,limb.amplitude));
            rig.vigilHalo=JointCopy(source.vigilHalo);rig.vigilIris=JointCopy(source.vigilIris);
            for(int i=0;i<rig.vigilFlames.Length;i++)rig.vigilFlames[i]=JointCopy(source.vigilFlames[i]);
            copy.SetActive(true);return rig;
        }
    }
}

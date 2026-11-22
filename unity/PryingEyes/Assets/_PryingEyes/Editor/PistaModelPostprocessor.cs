using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PryingEyes.EditorTools
{
    /// <summary>The models of the track mode drawn in Blender (modelli/moto/genera_moto.py -- export, Assets/_PryingEyes/Resources/Moto/*.fbx): how Unity reads them. Every piece is a mesh node at the origin of the
    /// bike (the rig is built by code, PistaRiderModel and PistaBikeMesh), the names of the materials are the roles the game reads, so the materials of the file are imported only for their names; the meshes
    /// stay readable because the limbs are skinned at run time (the weights come from the rest joints). Axes: the Blender files are x right, y forward, z up and come in as x right, y up, z forward.</summary>
    public sealed class PistaModelPostprocessor : AssetPostprocessor
    {
        /// <summary>Raise it when this class changes what it does to the models: Unity then imports them again.</summary>
        public override uint GetVersion() { return 2; }

        bool Ours() { return assetPath.Replace('\\', '/').Contains("/Resources/Moto/"); }

        /// <summary>The importer leaves the axis conversion of the file in the nodes (a turn of 90 degrees about x) and the vertices in the frame of Blender (x right, y forward, z down), so a shader that reads the position of a pixel
        /// would see the model on its side. The turn goes into the vertices and the normals here, and the nodes keep their position only: the mesh of every piece is then in the frame of the game (x right, y up, z forward) round the origin of its node.</summary>
        void OnPostprocessModel(GameObject root)
        {
            if (!Ours()) return;
            var done = new HashSet<Mesh>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var t = mf.transform;
                var mesh = mf.sharedMesh;
                if (mesh == null || t.localRotation == Quaternion.identity || !done.Add(mesh)) continue;
                Quaternion q = t.localRotation;
                var v = mesh.vertices;
                var n = mesh.normals;
                bool hasN = n != null && n.Length == v.Length;
                for (int i = 0; i < v.Length; i++)
                {
                    v[i] = q * v[i];
                    if (hasN) n[i] = q * n[i];
                }
                mesh.vertices = v;
                if (hasN) mesh.normals = n;
                mesh.RecalculateBounds();
                t.localRotation = Quaternion.identity;
            }
        }

        void OnPreprocessModel()
        {
            if (!Ours()) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.isReadable = true;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
        }
    }
}

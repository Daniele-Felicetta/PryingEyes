using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>One sewn panel between the wrist, shoulder and hip.</summary>
    sealed class WingsuitFabric : MonoBehaviour
    {
        const int Sections = 7;
        static Material fabric, reinforcement;
        readonly Vector3[] vertices = new Vector3[(Sections + 1) * 3];
        Mesh mesh;

        public static WingsuitFabric Build(Transform parent, int layer, uint lights, bool shadow)
        {
            var go = new GameObject("Wingsuit fabric") { layer = layer };
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<WingsuitFabric>();
            var mesh = panel.mesh = new Mesh { name = "Wingsuit panel" };
            mesh.MarkDynamic();
            mesh.vertices = panel.vertices;
            mesh.subMeshCount = 2;
            var uv = new Vector2[panel.vertices.Length];
            var triangles = new[] { new int[Sections * 6], new int[Sections * 6] };
            for (int i = 0; i <= Sections; i++)
                for (int strip = 0; strip < 3; strip++)
                {
                    int a = i * 3 + strip;
                    uv[a] = new Vector2(i / (float)Sections, strip == 1 ? 0.20f : strip * 0.5f);
                    if (i == Sections || strip == 2) continue;
                    int t = i * 6;
                    triangles[strip][t] = a;
                    triangles[strip][t + 1] = a + 3;
                    triangles[strip][t + 2] = a + 1;
                    triangles[strip][t + 3] = a + 1;
                    triangles[strip][t + 4] = a + 3;
                    triangles[strip][t + 5] = a + 4;
                }
            mesh.uv = uv;
            mesh.SetTriangles(triangles[0], 0);
            mesh.SetTriangles(triangles[1], 1);
            if (fabric == null) fabric = Cloth("Oxblood ripstop", new Color(0.30f, 0.085f, 0.064f));
            if (reinforcement == null) reinforcement = Cloth("Wingsuit reinforcement", new Color(0.14f, 0.12f, 0.096f));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { reinforcement, fabric };
            renderer.renderingLayerMask = lights;
            renderer.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            panel.Hide();
            return panel;
        }

        static Material Cloth(string name, Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            mat.SetFloat("_Cull", 0);
            mat.SetFloat("_Smoothness", 0.25f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 0.12f);
            return mat;
        }

        public void Pose(Vector3 shoulder, Vector3 wrist, Vector3 hip, Vector3 billow, float flutter)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            shoulder = transform.InverseTransformPoint(shoulder);
            wrist = transform.InverseTransformPoint(wrist);
            hip = transform.InverseTransformPoint(hip);
            billow = transform.InverseTransformVector(billow);
            for (int i = 0; i <= Sections; i++)
            {
                float t = i / (float)Sections;
                float shape = Mathf.Sin(t * Mathf.PI);
                Vector3 a = Vector3.Lerp(shoulder, wrist, t);
                Vector3 b = Vector3.Lerp(hip, wrist, t) + billow * (0.09f * shape);
                vertices[i * 3] = a;
                vertices[i * 3 + 1] = Vector3.Lerp(a, b, 0.20f) + billow *
                    (shape * (0.04f + 0.016f * Mathf.Sin(Time.time * 22f + t * 4f + flutter)));
                vertices[i * 3 + 2] = b;
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        public void Hide() => gameObject.SetActive(false);
        void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}

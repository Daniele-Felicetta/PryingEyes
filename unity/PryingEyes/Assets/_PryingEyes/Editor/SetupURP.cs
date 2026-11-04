using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.EditorTools
{
    /// <summary>One-shot URP setup: pipeline asset + renderer, assigned to all quality levels. Run via CLI.</summary>
    public static class SetupURP
    {
        public static void Setup()
        {
            const string dir = "Assets/_PryingEyes/Settings";
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes"))
                AssetDatabase.CreateFolder("Assets", "_PryingEyes");
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets/_PryingEyes", "Settings");

            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, dir + "/HotelRenderer.asset");

            var pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
            AssetDatabase.CreateAsset(pipeline, dir + "/HotelURP.asset");
            var so = new SerializedObject(pipeline);
            var list = so.FindProperty("m_RendererDataList");
            if (list != null)
            {
                list.ClearArray();
                list.InsertArrayElementAtIndex(0);
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            }
            var defIdx = so.FindProperty("m_DefaultRendererIndex");
            if (defIdx != null) defIdx.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(Mathf.Clamp(current, 0, QualitySettings.names.Length - 1), true);
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log("[PryingEyes] URP setup OK: " + dir + "/HotelURP.asset");
        }
    }
}

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

        /// <summary>Idempotent quality pass on the existing URP assets (called by HotelBuilder.Build).</summary>
        public static void Configure()
        {
            const string dir = "Assets/_PryingEyes/Settings";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(dir + "/HotelURP.asset");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(dir + "/HotelRenderer.asset");
            if (pipeline == null || renderer == null)
            {
                Debug.LogError("[PryingEyes] URP assets missing: run SetupURP.Setup first");
                return;
            }

            var so = new SerializedObject(pipeline);
            SetInt(so, "m_MSAA", 4);                                 // anti-aliasing 4x (prima: spento)
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_SoftShadowQuality", 3);                    // High
            SetInt(so, "m_MainLightShadowmapResolution", 4096);
            SetFloat(so, "m_ShadowDistance", 70f);                   // tutta la hall (70 m)
            SetInt(so, "m_ShadowCascadeCount", 4);
            SetBool(so, "m_AdditionalLightShadowsSupported", true);  // ombre di lampadari e lampade
            SetInt(so, "m_AdditionalLightsShadowmapResolution", 4096);
            SetBool(so, "m_ReflectionProbeBoxProjection", true);
            SetBool(so, "m_ReflectionProbeBlending", true);
            so.ApplyModifiedPropertiesWithoutUndo();

            var ro = new SerializedObject(renderer);
            var ppData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            ro.FindProperty("postProcessData").objectReferenceValue = ppData; // null = post-processing spento
            SetInt(ro, "m_RenderingMode", 2);                        // Forward+: niente limite di 4 luci per oggetto
            ro.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            Debug.Log("[PryingEyes] URP configured: msaa=" + pipeline.msaaSampleCount + " postData=" + (ppData != null)
                + " shadowDist=" + pipeline.shadowDistance + " cascades=" + pipeline.shadowCascadeCount);
        }

        static SerializedProperty Prop(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            if (p == null) Debug.LogWarning("[PryingEyes] URP property not found: " + name);
            return p;
        }

        static void SetInt(SerializedObject so, string name, int v) { var p = Prop(so, name); if (p != null) p.intValue = v; }
        static void SetBool(SerializedObject so, string name, bool v) { var p = Prop(so, name); if (p != null) p.boolValue = v; }
        static void SetFloat(SerializedObject so, string name, float v) { var p = Prop(so, name); if (p != null) p.floatValue = v; }
    }
}

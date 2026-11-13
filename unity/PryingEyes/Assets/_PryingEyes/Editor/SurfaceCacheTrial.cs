using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.EditorTools
{
    /// <summary>Prova a tempo del GI in tempo reale di URP (Surface Cache, sperimentale): Enable aggiunge il renderer feature a
    /// HotelRenderer.asset, Disable lo toglie. Il codice del feature esiste solo col define SURFACE_CACHE (Player Settings, Scripting
    /// Define Symbols) e col feature acceso lo static batching dev'essere spento (ProjectSettings.asset, a mano): senza il define,
    /// Enable lo dice ed esce con 3.
    /// Si trova la classe per nome, cosi' questo file compila anche col define spento.
    /// -executeMethod PryingEyes.EditorTools.SurfaceCacheTrial.Enable | Disable, poi build completa.</summary>
    public static class SurfaceCacheTrial
    {
        const string RendererPath = "Assets/_PryingEyes/Settings/HotelRenderer.asset";
        const string FeatureTypeName = "UnityEngine.Rendering.Universal.SurfaceCacheGIRendererFeature";

        static Type FeatureType() => typeof(ScriptableRendererFeature).Assembly.GetType(FeatureTypeName);

        public static void Enable()
        {
            var type = FeatureType();
            if (type == null)
            {
                Debug.LogError("[PryingEyes] SurfaceCacheTrial: classe " + FeatureTypeName + " non trovata (serve il define SURFACE_CACHE)");
                EditorApplication.Exit(3);
                return;
            }
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null) { Debug.LogError("[PryingEyes] SurfaceCacheTrial: manca " + RendererPath); EditorApplication.Exit(4); return; }

            var feature = renderer.rendererFeatures.Find(f => f != null && f.GetType() == type);
            if (feature == null)
            {
                feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                feature.name = "SurfaceCacheGI";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
                var ro = new SerializedObject(renderer);
                var list = ro.FindProperty("m_RendererFeatures");
                var map = ro.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                ro.ApplyModifiedPropertiesWithoutUndo();
            }
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            renderer.SetDirty();

            // lo static batching va spento in ProjectSettings.asset (m_BuildTargetBatching: Standalone, m_StaticBatching: 0): il feature lo
            // controlla in editor e non parte; l'API per farlo e' interna, quindi si scrive a mano. I piani procedurali non lo usano.
            AssetDatabase.SaveAssets();
            Debug.Log("[PryingEyes] SurfaceCacheTrial: feature attivo, features=" + renderer.rendererFeatures.Count);
        }

        /// <summary>Enable + build completa nella stessa sessione (con -buildOut): una partenza di Unity in meno.</summary>
        public static void EnableAndBuild()
        {
            Enable();
            BuildPlayer.Windows();
        }

        public static void Disable()
        {
            var type = FeatureType();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null) { Debug.LogError("[PryingEyes] SurfaceCacheTrial: manca " + RendererPath); EditorApplication.Exit(4); return; }
            var ro = new SerializedObject(renderer);
            var list = ro.FindProperty("m_RendererFeatures");
            var map = ro.FindProperty("m_RendererFeatureMap");
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                var obj = list.GetArrayElementAtIndex(i).objectReferenceValue;
                // via solo il nostro feature o un riferimento rimasto nullo (la classe sparisce se manca il define); gli altri restano
                bool ours = obj != null && type != null && obj.GetType() == type;
                if (!ours && obj != null) continue;
                int before = list.arraySize;
                list.DeleteArrayElementAtIndex(i);          // su un riferimento a oggetto non nullo la prima chiamata lo azzera e basta
                if (list.arraySize == before) list.DeleteArrayElementAtIndex(i);
                if (i < map.arraySize) map.DeleteArrayElementAtIndex(i);
                if (ours) { AssetDatabase.RemoveObjectFromAsset(obj); UnityEngine.Object.DestroyImmediate(obj, true); }
            }
            ro.ApplyModifiedPropertiesWithoutUndo();
            renderer.SetDirty();
            AssetDatabase.SaveAssets();
            Debug.Log("[PryingEyes] SurfaceCacheTrial: feature tolto, features=" + renderer.rendererFeatures.Count);
        }
    }
}

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PryingEyes.EditorTools
{
    public static class EnemyMeleeImport
    {
        const string File = "Assets/_PryingEyes/Models/Monaco/mischia.fbx";
        public static void Build() { Run(); BuildPlayer.Torre(); }
        public static void Run()
        {
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(File);
            importer.globalScale = 1; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true; importer.animationCompression = ModelImporterAnimationCompression.Off;
            var takes = importer.defaultClipAnimations;
            foreach (var take in takes) { take.name = "mischia"; take.loopTime = false; take.firstFrame = 0; take.lastFrame = 38; }
            importer.clipAnimations = takes; importer.SaveAndReimport();
            foreach (string name in new[] { "Monaco", "Abate" })
            {
                string folder = "Assets/_PryingEyes/Models/" + name + "/";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + name + ".controller");
                var original = AssetDatabase.LoadAllAssetsAtPath(folder + name.ToLowerInvariant() + ".fbx").OfType<AnimationClip>().First(c => c.name == "attacco");
                Configure(controller, original);
            }
            AssetDatabase.SaveAssets();
        }

        public static void Configure(AnimatorController controller, AnimationClip original)
        {
            var melee = AssetDatabase.LoadAllAssetsAtPath(File).OfType<AnimationClip>().FirstOrDefault(c => c.name == "mischia");
            if (melee == null) throw new InvalidOperationException("Generare e importare mischia.fbx prima dei controller");
            // L'FBX con solo ossa ha un contenitore in più. Manteniamo la trasformazione statica
            // dell'armatura del costume; la nuova clip anima soltanto le sue ossa.
            var corrected = UnityEngine.Object.Instantiate(melee);
            corrected.name = "mischia"; corrected.hideFlags = HideFlags.None;
            foreach (var binding in AnimationUtility.GetCurveBindings(original))
                if (binding.path == "Monaco")
                {
                    var curve = AnimationUtility.GetEditorCurve(original, binding);
                    float value = curve.Evaluate(0);
                    if (curve.keys.Any(k => Mathf.Abs(k.value - value) > 0.001f)) throw new InvalidOperationException("Radice animata da preservare esplicitamente");
                    AnimationUtility.SetEditorCurve(corrected, binding, AnimationCurve.Constant(0, Enemy.MeleeDuration, value));
                }
            foreach (var binding in AnimationUtility.GetCurveBindings(corrected))
            {
                var curve = AnimationUtility.GetEditorCurve(corrected, binding);
                float value = curve.Evaluate(0);
                if (curve.keys.All(k => k.value == value))
                    AnimationUtility.SetEditorCurve(corrected, binding, AnimationCurve.Constant(0, Enemy.MeleeDuration, value));
            }
            const string correctedFile = "Assets/_PryingEyes/Models/Monaco/Mischia.anim";
            var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(correctedFile);
            if (saved == null) { AssetDatabase.CreateAsset(corrected, correctedFile); saved = corrected; }
            else { EditorUtility.CopySerialized(corrected, saved); UnityEngine.Object.DestroyImmediate(corrected); EditorUtility.SetDirty(saved); }
            melee = saved;
            var sm = controller.layers[0].stateMachine;
            var attack = sm.states.First(s => s.state.name == "Attacco").state;
            attack.motion = melee;
            foreach (var transition in sm.anyStateTransitions)
                if (transition.destinationState == attack) transition.duration = 0.04f;
            foreach (var transition in attack.transitions) { transition.exitTime = 0.98f; transition.duration = 0.12f; }
            if (!controller.parameters.Any(p => p.name == "Lancia")) controller.AddParameter("Lancia", AnimatorControllerParameterType.Trigger);
            var cast = sm.states.FirstOrDefault(s => s.state.name == "Incanto").state;
            if (cast == null)
            {
                cast = sm.AddState("Incanto", new Vector3(800, 0));
                var enter = sm.AddAnyStateTransition(cast);
                enter.AddCondition(AnimatorConditionMode.If, 0, "Lancia");
                enter.AddCondition(AnimatorConditionMode.IfNot, 0, "Morto");
                enter.hasExitTime = false; enter.duration = 0.08f; enter.canTransitionToSelf = false;
                var exit = cast.AddTransition(sm.defaultState);
                exit.hasExitTime = true; exit.exitTime = 0.92f; exit.duration = 0.15f;
            }
            cast.motion = original;
            var originalPaths = AnimationUtility.GetCurveBindings(original).Select(b => b.path).ToHashSet();
            var paths = AnimationUtility.GetCurveBindings(melee).Select(b => b.path).ToHashSet();
            int matched = paths.Count(p => originalPaths.Contains(p));
            Debug.Log($"[Mischia] {controller.name}: {matched}/{paths.Count} percorsi compatibili, durata {melee.length:F3} s");
            if (matched != paths.Count || paths.Count < 20) throw new InvalidOperationException("Scheletro mischia incompatibile: " + string.Join(",", paths.Except(originalPaths)));
            EditorUtility.SetDirty(controller);
        }
    }
}

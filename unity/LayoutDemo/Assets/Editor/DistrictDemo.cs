using System;
using System.IO;
using Production.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Consumes the local package in an independent project, without Prying Eyes scripts or assets.</summary>
public static class DistrictDemo
{
    [MenuItem("Layout Demo/Generate and Verify")]
    public static void Verify()
    {
        var plan = DistrictPlan.Create(2026, 39, 29, 5);
        if (JsonUtility.ToJson(plan) != JsonUtility.ToJson(DistrictPlan.Create(2026, 39, 29, 5)))
            throw new Exception("Layout is not deterministic");
        var variant = DistrictPlan.Create(7, 50, 40, 6);
        if (variant.spawns[0].z != -35 || variant.blocks[0].width != 35)
            throw new Exception("Variant parameters were ignored");
        bool rejected = false;
        try { DistrictPlan.Create(0, 25, 20, 2); } catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("Invalid dimensions were accepted");
        rejected = false;
        try { DistrictPlan.Create(0, 3e38f, 29, 5); } catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("Overflowing ground dimensions were accepted");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground"; ground.transform.localScale = new Vector3(plan.halfWidth * 2, .2f, plan.halfDepth * 2);
        foreach (var block in plan.blocks)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = block.id;
            cube.transform.position = new Vector3(block.building.x, block.height / 2, block.building.z);
            cube.transform.localScale = new Vector3(block.width, block.height, block.depth);
        }
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(new Vector3(55, 70, -55), Quaternion.LookRotation(new Vector3(-55, -70, 55)));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .09f, .13f);
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(45, -30, 0);
        EditorSceneManager.SaveScene(scene, "Assets/DistrictDemo.unity");

        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Verification"));
        var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-layoutDemoOut");
        if (at >= 0 && at + 1 < args.Length) folder = Path.GetFullPath(args[at + 1]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "layout.json"), JsonUtility.ToJson(plan, true));
        File.WriteAllText(Path.Combine(folder, "report.json"), "{\"ok\":true,\"checks\":[\"determinism\",\"variant\",\"invalid_dimensions\",\"overflow\",\"scene_generation\"]}");
        Debug.Log("[LayoutDemo] PASS: independent local package, deterministic layout, variant, validation, scene");
    }
}

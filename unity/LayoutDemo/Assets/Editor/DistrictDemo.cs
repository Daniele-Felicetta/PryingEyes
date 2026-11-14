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
        var recipe = JsonUtility.FromJson<RoomPlan.Recipe>(File.ReadAllText("Assets/room-demo.json"));
        var room = RoomPlan.Create(recipe, 32, 20);
        if (room.width != 32 || room.parts[0].size.x != 4 * (32f / 24)) throw new Exception("Room scaling ignored");
        // One stairs entry becomes a flight of solid steps that rise from the floor to the deck the route ends on.
        var steps = Array.FindAll(room.parts, p => p.id.StartsWith("flight_"));
        var top = steps[steps.Length - 1];
        if (steps.Length != 10 || Math.Abs(top.position.y + top.size.y / 2 - 2f) > 1e-3 || room.route[room.route.Length - 1].y != 2f)
            throw new Exception("Stairs were not expanded into a flight reaching the deck");
        var tall = JsonUtility.FromJson<RoomPlan.Recipe>(File.ReadAllText("Assets/room-demo.json"));
        tall.route[tall.route.Length - 1].y = 2.5f;     // hovering half a metre above the deck
        rejected = false;
        try { RoomPlan.Create(tall, 24, 14); } catch (ArgumentException e) { rejected = e.Message.StartsWith("ROOM_STAND_FLOATING"); }
        if (!rejected) throw new Exception("A route point resting on nothing was accepted");
        tall = JsonUtility.FromJson<RoomPlan.Recipe>(File.ReadAllText("Assets/room-demo.json"));
        tall.parts[1].steps = 5;
        rejected = false;
        try { RoomPlan.Create(tall, 24, 14); } catch (ArgumentException e) { rejected = e.Message.StartsWith("ROOM_STAIRS_STEEP"); }
        if (!rejected) throw new Exception("A flight too steep to climb was accepted");
        recipe.parts[0].position = new RoomPlan.Vector(0, .5f, 6);
        rejected = false;
        try { RoomPlan.Create(recipe, 24, 14); } catch (ArgumentException e) { rejected = e.Message.StartsWith("PASSAGE_BLOCKED"); }
        if (!rejected) throw new Exception("Room exit obstruction accepted");
        var roomScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Floor";
        ground.transform.localScale = new Vector3(room.width, .2f, room.depth);
        foreach (var part in room.parts)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = part.id;
            cube.transform.position = new Vector3(part.position.x, part.position.y, part.position.z);
            cube.transform.localScale = new Vector3(part.size.x, part.size.y, part.size.z);
        }
        camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(new Vector3(22, 26, -22), Quaternion.LookRotation(new Vector3(-22, -26, 22)));
        light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, -30, 0);
        var weapon = new GameObject("IndependentWeapon").AddComponent<WeaponDemo>(); weapon.ResetDemo();
        if (!weapon.FireOnce() || weapon.State.Magazine != 7 || weapon.FireOnce()) throw new Exception("Independent firing/cadence failed");
        weapon.Advance(.2f);
        if (!weapon.Reload() || weapon.FireOnce()) throw new Exception("Independent reload lock failed");
        weapon.Advance(.6f);
        if (!weapon.State.IsReloading || weapon.State.Magazine != 7) throw new Exception("Independent reload completed too soon");
        weapon.Advance(.6f);
        if (weapon.State.IsReloading || weapon.State.Magazine != 8 || weapon.State.Reserve != 31) throw new Exception("Independent reload conservation failed");
        weapon.ResetDemo();
        var waves = new GameObject("IndependentWaves").AddComponent<WaveDemo>(); waves.ResetDemo();
        for (int i = 0; i < 6; i++) waves.Advance(.5f);
        if (waves.Living != 3 || waves.Schedule.Pending != 9) throw new Exception("Independent wave cap failed");
        waves.DefeatOne(); waves.Advance(.5f);
        if (waves.Living != 3 || waves.Schedule.Pending != 8) throw new Exception("Independent wave resume failed");
        waves.ResetDemo();
        var access = new GameObject("IndependentAccess").AddComponent<AccessDemo>(); access.ResetDemo();
        if (access.Buy("roof") || access.Balance != 1000) throw new Exception("Independent access adjacency failed");
        if (!access.Buy("shop") || access.Balance != 250 || access.Buy("shop")) throw new Exception("Independent access charge failed");
        access.Earn(); access.Earn();
        if (!access.Buy("roof") || access.Balance != 0) throw new Exception("Independent access progression failed");
        access.ResetDemo();
        EditorSceneManager.SaveScene(roomScene, "Assets/RoomDemo.unity");
        File.WriteAllText(Path.Combine(folder, "room.json"), JsonUtility.ToJson(room, true));
        File.WriteAllText(Path.Combine(folder, "report.json"), "{\"ok\":true,\"checks\":[\"determinism\",\"variant\",\"invalid_dimensions\",\"overflow\",\"scene_generation\",\"room_variant\",\"room_stairs_flight\",\"room_floating_route\",\"room_steep_stairs\",\"room_blocked_exit\",\"room_scene_generation\",\"weapon_firing\",\"weapon_cadence\",\"weapon_reload_lock\",\"weapon_ammo_conservation\",\"wave_population_cap\",\"wave_population_resume\",\"access_adjacency\",\"access_charge_once\",\"access_progression\"]}");
        Debug.Log("[LayoutDemo] PASS: independent layout, weapon, wave and access state, interactive scene, 20 checks");
    }
}

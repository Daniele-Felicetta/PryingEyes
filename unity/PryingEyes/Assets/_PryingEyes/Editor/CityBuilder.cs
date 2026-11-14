using System.IO;
using System.Security.Cryptography;
using Production.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PryingEyes.EditorTools
{
    /// <summary>Generates the urban survival scene from an editable, seeded recipe.</summary>
    public static class CityBuilder
    {
        public const string ScenePath = "Assets/_PryingEyes/Scenes/Citta.unity";
        const string MaterialPath = "Assets/_PryingEyes/City/Materials";

        [System.Serializable]
        public sealed class Recipe
        {
            public int seed = 2026;
            public float halfWidth = 39f, halfDepth = 29f;
            public float roadHalfWidth = 5f;
            public int blocksX = 2, blocksZ = 2;
            public string district = "Quartiere della Stazione";
            public string[] shopWeapons;
        }

        [MenuItem("Prying Eyes/City/Generate Preview Scene")]
        public static void Build()
        {
            var recipe = JsonUtility.FromJson<Recipe>(File.ReadAllText(
                Path.Combine(Application.dataPath, "_PryingEyes/City/citta.json")));
            if (recipe == null || recipe.halfWidth < 25 || recipe.halfDepth < 22 ||
                recipe.roadHalfWidth < 4 || recipe.blocksX != 2 || recipe.blocksZ != 2 ||
                recipe.shopWeapons == null || recipe.shopWeapons.Length != 3)
                throw new System.InvalidOperationException("City recipe: expected a 2x2 district with roads at least 8 m wide.");
            foreach (var weapon in recipe.shopWeapons)
                if (WeaponDefs.IndexOf(weapon) < 0)
                    throw new System.InvalidOperationException("City weapon is not registered: " + weapon);

            var plan = DistrictPlan.Create(recipe.seed, recipe.halfWidth, recipe.halfDepth, recipe.roadHalfWidth);
            HotelBuilder.EnsureTagsLayers();
            foreach (var id in recipe.shopWeapons)
                if (WeaponDefs.Catalog.weapons[WeaponDefs.IndexOf(id)].source["family"] != null) ArmiImport.ImportOne(id);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            scene.name = "Citta";
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.11f, .15f, .22f);
            RenderSettings.ambientEquatorColor = new Color(.09f, .10f, .14f);
            RenderSettings.ambientGroundColor = new Color(.04f, .05f, .06f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.07f, .09f, .13f);
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = .012f;

            var asphalt = Mat("Asphalt", new Color(.085f, .095f, .11f));
            var pavement = Mat("Pavement", new Color(.27f, .27f, .25f));
            var brick = Mat("Brick", new Color(.33f, .19f, .16f));
            var stone = Mat("Stone", new Color(.23f, .26f, .27f));
            var glass = Mat("Windows", new Color(.10f, .19f, .25f));
            var accent = Mat("Accent", new Color(.47f, .30f, .12f));
            Box("Ground", Vector3.zero, new Vector3(recipe.halfWidth * 2, .2f, recipe.halfDepth * 2), asphalt);
            foreach (var block in plan.blocks)
            {
                Box("Sidewalk", At(block.pavement, .06f), new Vector3(block.pavementWidth, .12f, block.pavementDepth), pavement);
                Building(block.id, At(block.building), block.width, block.depth, block.height,
                    block.alternate ? stone : brick, glass, accent, block.facing);
                Lamp(At(block.lamp), accent);
            }
            Box("North barrier", new Vector3(0, 1f, recipe.halfDepth), new Vector3(recipe.halfWidth * 2, 2f, .3f), stone);
            Box("South barrier", new Vector3(0, 1f, -recipe.halfDepth), new Vector3(recipe.halfWidth * 2, 2f, .3f), stone);
            Box("East barrier", new Vector3(recipe.halfWidth, 1f, 0), new Vector3(.3f, 2f, recipe.halfDepth * 2), stone);
            Box("West barrier", new Vector3(-recipe.halfWidth, 1f, 0), new Vector3(.3f, 2f, recipe.halfDepth * 2), stone);

            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(.48f, .61f, .85f);
            moon.intensity = .8f;
            moon.transform.rotation = Quaternion.Euler(40f, -30f, 0);
            var volume = new GameObject("Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_PryingEyes/Settings/HotelVolume.asset");

            var spawns = new GameObject("SpawnPoints").transform;
            foreach (var point in plan.spawns)
            {
                var t = new GameObject("Spawn").transform;
                t.SetParent(spawns); t.position = At(point, .1f);
            }
            HotelBuilder.PlayerAndGame();
            var player = GameObject.Find("Player");
            player.transform.position = new Vector3(0, .1f, 0);
            // The player's original spawn must agree with this scene, including after death.
            player.GetComponent<PlayerController>().SetSpawn(player.transform.position);
            var city = new GameObject("CityDirector").AddComponent<CityDirector>();
            city.shops = new CityDirector.Shop[plan.panels.Length];
            for (int i = 0; i < plan.panels.Length; i++)
                city.shops[i] = new CityDirector.Shop { weaponId = recipe.shopWeapons[i], position = At(plan.panels[i].position, 1.5f), yaw = plan.panels[i].yaw };
            city.layoutJson = JsonUtility.ToJson(plan);
            using (var sha = SHA256.Create())
                city.recipeSha256 = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(
                    Path.Combine(Application.dataPath, "_PryingEyes/City/citta.json")))).Replace("-", "").ToLowerInvariant();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[PryingEyes] city built: " + recipe.district + " seed=" + recipe.seed);
        }

        static Vector3 At(DistrictPlan.Point point, float y = 0) => new Vector3(point.x, y, point.z);

        static Material Mat(string id, Color color)
        {
            if (!AssetDatabase.IsValidFolder(MaterialPath))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/City"))
                    AssetDatabase.CreateFolder("Assets/_PryingEyes", "City");
                AssetDatabase.CreateFolder("Assets/_PryingEyes/City", "Materials");
            }
            var path = MaterialPath + "/" + id + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Box(string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void Building(string name, Vector3 p, float w, float d, float h, Material wall,
            Material window, Material trim, int doorFacing)
        {
            var root = new GameObject(name).transform;
            void Part(string label, Vector3 local, Vector3 size, Material m)
            {
                var b = Box(label, p + local, size, m);
                b.transform.SetParent(root, true);
            }
            // Shops are accessible from the central east-west street; the entrance has a real opening.
            float front = doorFacing * d / 2f;
            Part("Floor", new Vector3(0, .12f, 0), new Vector3(w, .24f, d), wall);
            Part("Roof", new Vector3(0, h, 0), new Vector3(w, .3f, d), wall);
            Part("Back", new Vector3(0, h / 2, -front), new Vector3(w, h, .3f), wall);
            Part("Left", new Vector3(-w / 2, h / 2, 0), new Vector3(.3f, h, d), wall);
            Part("Right", new Vector3(w / 2, h / 2, 0), new Vector3(.3f, h, d), wall);
            Part("Front left", new Vector3(-w / 4 - .85f, h / 2, front), new Vector3(w / 2 - 1.7f, h, .3f), wall);
            Part("Front right", new Vector3(w / 4 + .85f, h / 2, front), new Vector3(w / 2 - 1.7f, h, .3f), wall);
            Part("Door lintel", new Vector3(0, (h + 2.6f) / 2, front), new Vector3(3.4f, h - 2.6f, .3f), trim);
            Part("Counter", new Vector3(0, .55f, -front * .5f), new Vector3(w * .55f, 1.1f, .6f), trim);
            for (int level = 1; level < 3; level++)
                Part("Front window", new Vector3(-w * .28f, level * 2.3f + .7f, front + doorFacing * .17f),
                     new Vector3(1.6f, 1.2f, .05f), window);
            root.position = Vector3.zero;
        }

        static void Lamp(Vector3 p, Material metal)
        {
            Box("Lamp post", p + Vector3.up * 1.7f, new Vector3(.12f, 3.4f, .12f), metal);
            var light = new GameObject("Street light").AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .73f, .45f);
            light.range = 12f;
            light.intensity = 2f;
            light.transform.position = p + Vector3.up * 3.3f;
        }
    }
}

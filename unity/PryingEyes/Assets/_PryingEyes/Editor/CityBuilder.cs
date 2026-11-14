using System.IO;
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
            HotelBuilder.EnsureTagsLayers();
            ArmiImport.ImportOne("p9_ronda");
            var recipe = JsonUtility.FromJson<Recipe>(File.ReadAllText(
                Path.Combine(Application.dataPath, "_PryingEyes/City/citta.json")));
            if (recipe == null || recipe.halfWidth < 25 || recipe.halfDepth < 22 ||
                recipe.roadHalfWidth < 4 || recipe.blocksX != 2 || recipe.blocksZ != 2 ||
                recipe.shopWeapons == null || recipe.shopWeapons.Length != 3)
                throw new System.InvalidOperationException("City recipe: expected a 2x2 district with roads at least 8 m wide.");
            foreach (var weapon in recipe.shopWeapons)
                if (WeaponDefs.IndexOf(weapon) < 0)
                    throw new System.InvalidOperationException("City weapon is not registered: " + weapon);

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
            var rng = new System.Random(recipe.seed);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float cx = sx * (recipe.halfWidth + recipe.roadHalfWidth) / 2f;
                float cz = sz * (recipe.halfDepth + recipe.roadHalfWidth) / 2f;
                float w = recipe.halfWidth - recipe.roadHalfWidth - 3f;
                float d = recipe.halfDepth - recipe.roadHalfWidth - 3f;
                Box("Sidewalk", new Vector3(cx, .06f, cz), new Vector3(w, .12f, d), pavement);
                float bx = cx + sx * 2f, bz = cz + sz * 2f;
                float height = 8f + rng.Next(0, 4) * 2.5f;
                Building($"Building_{sx}_{sz}", new Vector3(bx, 0, bz), w - 6f, d - 6f, height,
                    (sx == sz) ? brick : stone, glass, accent, -sz);
                // Street furniture remains outside the road and the building doorway.
                Lamp(new Vector3(cx - sx * (w / 2 - 1f), 0, cz - sz * (d / 2 - 1f)), accent);
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
            foreach (var p in new[] { new Vector3(0, .1f, -24), new Vector3(0, .1f, 24),
                     new Vector3(-34, .1f, 0), new Vector3(34, .1f, 0) })
            {
                var t = new GameObject("Spawn").transform;
                t.SetParent(spawns);
                t.position = p;
            }
            HotelBuilder.PlayerAndGame();
            var player = GameObject.Find("Player");
            player.transform.position = new Vector3(0, .1f, 0);
            // The player's original spawn must agree with this scene, including after death.
            player.GetComponent<PlayerController>().SetSpawn(player.transform.position);
            float shopX = (recipe.halfWidth + recipe.roadHalfWidth) / 2f + 2f;
            float buildingDepth = recipe.halfDepth - recipe.roadHalfWidth - 9f;
            float shopZ = (recipe.halfDepth + recipe.roadHalfWidth) / 2f + 2f - buildingDepth / 2f - .6f;
            var city = new GameObject("CityDirector").AddComponent<CityDirector>();
            city.shops = new[]
            {
                new CityDirector.Shop { weaponId = recipe.shopWeapons[0], position = new Vector3(-shopX + 8f, 1.5f, -shopZ), yaw = 0 },
                new CityDirector.Shop { weaponId = recipe.shopWeapons[1], position = new Vector3(shopX - 8f, 1.5f, -shopZ), yaw = 0 },
                new CityDirector.Shop { weaponId = recipe.shopWeapons[2], position = new Vector3(-shopX + 8f, 1.5f, shopZ), yaw = 180 }
            };
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[PryingEyes] city built: " + recipe.district + " seed=" + recipe.seed);
        }

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

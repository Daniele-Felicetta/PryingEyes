using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Production.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes.EditorTools
{
    public static class CityRoomBuilder
    {
        const string Source = "Assets/_PryingEyes/Resources/Production/rooms.json";
        [Serializable] sealed class Palette { public string id; public float[] color; public float metallic, smoothness, emission; }
        [Serializable] sealed class Catalog { public int schemaVersion; public Palette[] materials; public RoomPlan.Recipe[] rooms; }

        static Catalog Load() => JsonUtility.FromJson<Catalog>(File.ReadAllText(Source));
        static string Hash()
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Source))).Replace("-", "").ToLowerInvariant();
        }
        static Material Material(Palette p)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/City/Materials/Rooms"))
                AssetDatabase.CreateFolder("Assets/_PryingEyes/City/Materials", "Rooms");
            string path = "Assets/_PryingEyes/City/Materials/Rooms/" + p.id + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            var color = new Color(p.color[0], p.color[1], p.color[2]);
            material.SetColor("_BaseColor", color); material.SetFloat("_Metallic", p.metallic); material.SetFloat("_Smoothness", p.smoothness);
            if (p.emission > 0) material.EnableKeyword("_EMISSION"); else material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * p.emission); EditorUtility.SetDirty(material);
            // URP's importer derives _EMISSION from these flags; the default EmissiveIsBlack erased it after saving.
            material.globalIlluminationFlags = p.emission > 0 ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            return material;
        }
        public static void VerifyImportedMaterials()
        {
            foreach (var palette in Load().materials)
            {
                string path = "Assets/_PryingEyes/City/Materials/Rooms/" + palette.id + ".mat";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.IsKeywordEnabled("_EMISSION") != (palette.emission > 0))
                    throw new InvalidOperationException("ROOM_EMISSION_IMPORT: " + palette.id);
            }
            Debug.Log("[RoomLab] MATERIAL_IMPORT_PASS: " + Load().materials.Length);
        }
        static Vector3 V(RoomPlan.Vector p) => new Vector3(p.x, p.y, p.z);

        public static CityRoom Build(DistrictPlan.Block block, string identity, Material wall)
        {
            var catalog = Load();
            if (catalog.schemaVersion != 1) throw new InvalidOperationException("ROOM_CATALOG_SCHEMA");
            var recipe = Array.Find(catalog.rooms, r => r.id == identity);
            if (recipe == null) throw new InvalidOperationException("ROOM_UNKNOWN: " + identity);
            var plan = RoomPlan.Create(recipe, block.width, block.depth);
            var materials = catalog.materials.ToDictionary(p => p.id, Material);
            var root = new GameObject(block.id).transform;
            root.position = new Vector3(block.building.x, 0, block.building.z);
            root.rotation = Quaternion.Euler(0, block.facing == 1 ? 0 : 180, 0);
            GameObject Box(string name, Vector3 position, Vector3 size, Material material, bool collider = true)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
                go.transform.SetParent(root, false); go.transform.localPosition = position; go.transform.localScale = size;
                go.GetComponent<Renderer>().sharedMaterial = material;
                if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                return go;
            }
            Box("Floor", new Vector3(0, .12f, 0), new Vector3(plan.width, .24f, plan.depth), materials["concrete"]);
            Box("Roof", new Vector3(0, block.height, 0), new Vector3(plan.width, .3f, plan.depth), wall);
            foreach (string side in new[] { "front", "back", "left", "right" })
            {
                bool horizontal = side == "front" || side == "back";
                float extent = horizontal ? plan.width : plan.depth;
                float sign = side == "front" || side == "right" ? 1 : -1;
                float line = sign * (horizontal ? plan.depth : plan.width) / 2;
                Vector3 Position(float along, float y) => horizontal ? new Vector3(along, y, line) : new Vector3(line, y, along);
                Vector3 Size(float along, float h) => horizontal ? new Vector3(along, h, .3f) : new Vector3(.3f, h, along);
                var door = Array.Find(plan.doors, d => d.side == side);
                if (door == null) Box("Wall_" + side, Position(0, block.height / 2), Size(extent, block.height), wall);
                else
                {
                    float a = door.center - door.width / 2, b = door.center + door.width / 2;
                    Box("Wall_" + side + "_a", Position((-extent / 2 + a) / 2, block.height / 2), Size(a + extent / 2, block.height), wall);
                    Box("Wall_" + side + "_b", Position((extent / 2 + b) / 2, block.height / 2), Size(extent / 2 - b, block.height), wall);
                    Box("Wall_" + side + "_lintel", Position(door.center, (block.height + door.height + .24f) / 2), Size(door.width, block.height - door.height - .24f), wall);
                }
            }
            foreach (var part in plan.parts)
            {
                if (!materials.TryGetValue(part.material, out var material)) throw new InvalidOperationException("ROOM_MATERIAL_MISSING: " + part.material);
                var position = V(part.position) + Vector3.up * .24f;
                GameObject go;
                if (part.kind == "box") go = Box(part.id, position, V(part.size), material, part.collider);
                else if (part.kind == "cylinder")
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = part.id; go.transform.SetParent(root, false);
                    go.transform.localPosition = position; go.transform.localScale = new Vector3(part.size.x, part.size.y / 2, part.size.z);
                    go.GetComponent<Renderer>().sharedMaterial = material;
                    UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                    if (part.collider) go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                }
                else
                {
                    go = new GameObject(part.id); go.transform.SetParent(root, false); go.transform.localPosition = position;
                    if (part.kind == "light")
                    {
                        var light = go.AddComponent<Light>(); light.type = LightType.Point;
                        light.color = material.GetColor("_BaseColor"); light.intensity = part.intensity; light.range = part.range; light.shadows = LightShadows.None;
                    }
                    else
                    {
                        var text = go.AddComponent<TextMesh>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                        text.font.RequestCharactersInTexture(part.text, 64, FontStyle.Bold);
                        text.text = part.text; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
                        text.fontSize = 64; text.fontStyle = FontStyle.Bold; text.characterSize = part.size.y / 6.3f;
                        text.color = material.GetColor("_BaseColor"); go.GetComponent<Renderer>().sharedMaterial = text.font.material;
                        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                        go.AddComponent<CitySign>();
                    }
                }
                go.transform.localRotation = Quaternion.Euler(0, part.yaw, 0);
            }
            var marker = root.gameObject.AddComponent<CityRoom>(); marker.recipeId = identity;
            marker.sourceSha256 = Hash(); marker.planJson = JsonUtility.ToJson(plan);
            return marker;
        }

        public static void Preview()
        {
            ShaderUtil.allowAsyncCompilation = false;
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-room");
            string id = at >= 0 ? args[at + 1] : "room_workshop_station";
            int outputAt = Array.IndexOf(args, "-roomout");
            string folder = outputAt >= 0 ? args[outputAt + 1] : Path.GetFullPath("../../../unity_logs/room-preview");
            Directory.CreateDirectory(folder);
            var recipe = Array.Find(Load().rooms, r => r.id == id); if (recipe == null) throw new Exception("ROOM_UNKNOWN: " + id);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var wall = Material(new Palette { id = "workshop_wall", color = new[] { .30f, .23f, .18f }, smoothness = .15f });
            var marker = Build(new DistrictPlan.Block { id = "RoomLab", width = recipe.width, depth = recipe.depth, height = 4.5f, facing = 1 }, id, wall);
            marker.transform.Find("Roof").gameObject.SetActive(false);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
            var sun = new GameObject("LabSun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.5f; sun.transform.rotation = Quaternion.Euler(40, -25, 0);
            var camera = new GameObject("LabCamera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .055f, .07f); camera.fieldOfView = 45;
            camera.transform.SetPositionAndRotation(new Vector3(18, 26, 24), Quaternion.LookRotation(new Vector3(-18, -25, -24)));
            AssetDatabase.SaveAssets();
            VerifyImportedMaterials();
            var target = new RenderTexture(1280, 900, 24); camera.targetTexture = target;
            // Come ShotScene: il primo render prepara le varianti e i buffer dei materiali appena creati.
            camera.Render(); camera.Render(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(1280, 900, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(folder, id + ".png"), image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
            File.WriteAllText(Path.Combine(folder, "plan.json"), marker.planJson);
            if (!AssetDatabase.IsValidFolder("Assets/_PryingEyes/Scenes/Labs")) AssetDatabase.CreateFolder("Assets/_PryingEyes/Scenes", "Labs");
            EditorSceneManager.SaveScene(scene, "Assets/_PryingEyes/Scenes/Labs/" + id + ".unity"); AssetDatabase.SaveAssets();
            Debug.Log("[RoomLab] PASS: " + id + "; isolated preview and plan written");
        }

        public static void BuildAsset()
        {
            Preview(); BuildPlayer.City();
        }
    }
}

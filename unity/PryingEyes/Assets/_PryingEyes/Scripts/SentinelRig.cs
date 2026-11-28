using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes
{
    public sealed class SentinelRig : MonoBehaviour
    {
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        static readonly Color Ember = new(1f, 0.41f, 0.09f);
        static Dictionary<string, Material> palette;

        MaterialPropertyBlock properties;
        Transform chassis, aperture, censer;
        Vector3 chassisRest;
        Quaternion chassisRotation, censerRotation;
        float shutterTravel;
        readonly Transform[] wings = new Transform[2];
        readonly Quaternion[] wingRest = new Quaternion[2];
        readonly Transform[,] feathers = new Transform[2, 3];
        readonly Quaternion[,] featherRest = new Quaternion[2, 3];
        readonly Transform[] shutters = new Transform[6];
        readonly Vector3[] shutterRest = new Vector3[6];
        readonly Quaternion[] shutterRotation = new Quaternion[6];
        Renderer eye;
        Light light;
        float phase, chargeStart, chargeDuration, fireStart, fireDuration, hitTime, deathTime = -1f;

        public static GameObject Create()
        {
            var source = Resources.Load<GameObject>("Sentinel/Sentinel");
            if (source == null) return null;
            var actor = new GameObject("Sentinella");
            var model = Instantiate(source, actor.transform, false);
            var rig = actor.AddComponent<SentinelRig>();
            rig.Bind(model);
            var bodyCollider = actor.AddComponent<SphereCollider>();
            bodyCollider.radius = 0.40f;
            var body = actor.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;

            var weakPoint = new GameObject("Iride") { tag = "Head" };
            weakPoint.transform.SetParent(actor.transform, false);
            weakPoint.transform.localPosition = new Vector3(0f, 0.04f, 0.36f);
            weakPoint.AddComponent<SphereCollider>().radius = 0.14f;
            return actor;
        }

        void Bind(GameObject model)
        {
            properties = new MaterialPropertyBlock();
            palette ??= CreatePalette();
            var nodes = new Dictionary<string, Transform>();
            foreach (var node in model.GetComponentsInChildren<Transform>()) nodes[node.name] = node;
            chassis = nodes["Chassis"];
            aperture = nodes["Aperture"];
            censer = nodes["Censer"];
            chassisRest = chassis.localPosition;
            chassisRotation = chassis.localRotation;
            censerRotation = censer.localRotation;
            // FBX nodes can keep centimetre-scale coordinates under their imported parent.
            shutterTravel = 0.066f / chassis.TransformVector(Vector3.right).magnitude;
            for (int side = 0; side < 2; side++)
            {
                string label = side == 0 ? "Left" : "Right";
                wings[side] = nodes["Wing" + label];
                wingRest[side] = wings[side].localRotation;
                for (int i = 0; i < 3; i++)
                {
                    var feather = feathers[side, i] = nodes[$"Feather{label}{i}"];
                    featherRest[side, i] = feather.localRotation;
                }
            }
            for (int i = 0; i < shutters.Length; i++)
            {
                var blade = shutters[i] = nodes[$"Shutter{i}"];
                shutterRest[i] = blade.localPosition;
                shutterRotation[i] = blade.localRotation;
            }
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && palette.TryGetValue(mats[i].name, out var replacement)) mats[i] = replacement;
                renderer.sharedMaterials = mats;
            }
            eye = aperture.GetComponentInChildren<Renderer>();
            light = new GameObject("Aperture glow").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = new Vector3(0f, 0.04f, 0.67f);
            light.type = LightType.Point;
            light.color = Ember;
            light.range = 2.4f;
            light.shadows = LightShadows.None;
            light.intensity = 0f;
            phase = Random.value * 6.28f;
        }

        static Dictionary<string, Material> CreatePalette()
        {
            var wear = Resources.Load<Texture2D>("Sentinel/Patina");
            var materials = new Dictionary<string, Material>
            {
                ["Iron"] = Metal(new Color(0.24f, 0.20f, 0.17f), 0.53f, 0.37f),
                ["Brass"] = Metal(new Color(0.78f, 0.50f, 0.23f), 0.59f, 0.50f),
                ["Verdigris"] = Metal(new Color(0.17f, 0.38f, 0.33f), 0.39f, 0.41f),
                ["Enamel"] = Metal(new Color(0.34f, 0.095f, 0.06f), 0.32f, 0.43f),
            };
            foreach (var material in materials.Values) material.mainTexture = wear;
            var emit = Metal(Ember, 0f, 0.5f);
            emit.EnableKeyword("_EMISSION");
            emit.SetColor(Emission, Ember * 0.26f);
            materials["Ember"] = emit;
            return materials;
        }

        static Material Metal(Color color, float metallic, float smoothness)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        public void Charge(float duration) { chargeStart = Time.time; chargeDuration = duration; }
        public void Fire(float duration) { chargeDuration = 0f; fireStart = Time.time; fireDuration = duration; }
        public void Hit() => hitTime = Time.time + 0.2f;
        public void Collapse() { chargeDuration = fireDuration = 0f; deathTime = Time.time; }

        void LateUpdate()
        {
            float now = Time.time;
            float charge = chargeDuration > 0 ? Mathf.Clamp01((now - chargeStart) / chargeDuration) : 0f;
            float fire = fireDuration > 0 ? Mathf.Clamp01((now - fireStart) / fireDuration) : 1f;
            float recoil = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fire / 0.09f)) * Mathf.Pow(1f - fire, 3f);
            float brace = Mathf.SmoothStep(0f, 1f, charge) * 0.8f + (1f - fire) * 0.8f;
            float limp = deathTime < 0 ? 0f : Mathf.Clamp01((now - deathTime) * 3.5f);
            float hit = Mathf.Clamp01((hitTime - now) * 5f);
            float beat = now * 5.4f + phase;
            Vector3 offset = new Vector3(0f, Mathf.Sin(beat) * 0.04f * (1f - brace) * (1f - limp), -0.16f * recoil);
            chassis.localPosition = chassisRest + chassis.parent.InverseTransformVector(transform.TransformVector(offset));
            chassis.localRotation = chassisRotation * Quaternion.Euler(-15f * recoil + 7f * limp, 0f, (4f * Mathf.Sin(beat * .53f) + 9f * hit) * (1f - brace));
            censer.localRotation = censerRotation * Quaternion.Euler(Mathf.Sin(beat * .71f) * 12f + 34f * recoil, 0f, Mathf.Sin(beat * .51f) * 15f);

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                float flap = Mathf.Sin(beat + side * .14f) * (16f - 13f * brace) + 14f * brace - 36f * limp;
                wings[side].localRotation = wingRest[side] * Quaternion.Euler(0f, sign * (brace * 13f + limp * 32f), sign * flap);
                for (int i = 0; i < 3; i++)
                    feathers[side, i].localRotation = featherRest[side, i] * Quaternion.Euler(0f, 0f,
                        sign * (Mathf.Sin(beat - i * .41f) * (4f + 3f * i) * (1f - brace) * (1f - limp) - 6f * i * brace + 10f * recoil));
            }

            float open = Mathf.Lerp(.25f, .95f, brace) * (1f - limp);
            for (int i = 0; i < shutters.Length; i++)
            {
                shutters[i].localPosition = shutterRest[i] + Vector3.ProjectOnPlane(shutterRest[i] - aperture.localPosition, Vector3.forward).normalized * (shutterTravel * open);
                shutters[i].localRotation = Quaternion.AngleAxis(-35f * open, Vector3.forward) * shutterRotation[i];
            }
            float flash = Mathf.Pow(1f - fire, 12f);
            properties.SetColor(Emission, Color.Lerp(Ember * 0.65f, new Color(1f, .78f, .32f) * 3.9f, charge * charge + flash) * (1f - limp));
            eye.SetPropertyBlock(properties);
            light.intensity = (charge * charge * 1.7f + flash * 3.1f) * (1f - limp);
        }
    }
}

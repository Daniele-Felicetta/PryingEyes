using UnityEngine;
using UnityEngine.Rendering;

namespace PryingEyes
{
    /// <summary>The gunplay gloves and sleeve IK, with alternating contacts on the surface in world space.</summary>
    sealed class ParkourHands
    {
        static readonly HandPose StoneGrip = HandPose.Fingers(12f, 42f, 0.58f, 0.51f).Thumb(7f, 18f, 14f, 12f);
        readonly Transform root;
        readonly Light fill;
        readonly Transform[] wrists = new Transform[2];
        readonly HandRig[] gloves = new HandRig[2];
        readonly ArmRig[] arms = new ArmRig[2];

        ParkourHands(Transform root)
        {
            this.root = root;
            fill = Weapon.CreateViewmodelFill(root);
        }

        public static ParkourHands Build(Transform camera, int layer)
        {
            var glovePrefab = Resources.Load<GameObject>("ManoLibera");
            var sleevePrefab = Resources.Load<GameObject>("ManicaLibera");
            if (glovePrefab == null || sleevePrefab == null) return null;
            var model = new GameObject("Parkour hands");
            model.transform.SetParent(camera, false);
            var rig = new ParkourHands(model.transform);
            for (int i = 0; i < 2; i++)
            {
                bool right = i == 1;
                var wrist = new GameObject(right ? "Right wrist" : "Left wrist").transform;
                wrist.SetParent(rig.root, false);
                var glove = Object.Instantiate(glovePrefab, wrist).transform;
                var sleeve = Object.Instantiate(sleevePrefab, rig.root).transform;
                if (right) { HandMirror.Mirror(glove); HandMirror.Mirror(sleeve); }
                foreach (var t in rig.root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                var fingers = HandRig.Build(glove, null, null);
                if (fingers == null) { Object.Destroy(model); return null; }
                // The target transform represents the palm frame at the actual wrist joint.
                glove.localRotation = Quaternion.Inverse(fingers.Palm.localRotation);
                glove.localPosition = -(glove.localRotation * fingers.Palm.localPosition);
                var arm = ArmRig.Build(rig.root, wrist, right ? "d" : "s", freeSleeve: sleeve);
                if (arm != null)
                {
                    arm.Free = 1f;
                    arm.PoleHint = new Vector3(right ? 0.48f : -0.48f, -1f, -0.16f);
                    arm.ThumbLocal = Vector3.right * fingers.ThumbSign;
                    arm.FingersLocal = Vector3.forward;
                    arm.AnchorTo(rig.root, Vector3.zero);
                }
                rig.wrists[i] = wrist;
                rig.gloves[i] = fingers;
                rig.arms[i] = arm;
            }
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.renderingLayerMask = Weapon.ViewLayer;
            }
            rig.Hide();
            return rig;
        }

        public void Hide() => root.gameObject.SetActive(false);

        public void Tick(Vector3 previous, Vector3 hold, Vector3 previousNormal, Vector3 wallNormal,
                         bool rightLeads, bool transfer, float progress, bool attached, float weight, float dt)
        {
            root.gameObject.SetActive(weight > 0.02f);
            if (!root.gameObject.activeSelf) return;
            fill.intensity = 0.95f * weight;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                bool lead = (i == 1) == rightLeads;
                float start = lead ? 0f : 0.20f;
                float reach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, lead ? 0.70f : 0.96f, progress));
                float curl = transfer ? 1f - Mathf.Sin(reach * Mathf.PI) : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.92f, reach));
                if (!attached) curl *= weight;
                Vector3 normal = transfer ? Vector3.Slerp(previousNormal, wallNormal, reach) : wallNormal;
                Vector3 course = transfer ? Vector3.Lerp(previous, hold, reach) : hold;
                Vector3 contact = course + Vector3.Cross(normal, Vector3.up) * (side * 0.21f);
                if (Physics.Raycast(contact + normal * 0.16f, -normal, out var stone, 0.27f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore) &&
                    Vector3.Dot(stone.normal, normal) > 0.6f && Surfaces.Of(stone) != Surface.Glass) contact = stone.point;
                else contact = course + Vector3.Cross(normal, Vector3.up) * (side * 0.095f);
                contact += normal * 0.044f - Vector3.up * 0.056f;
                if (transfer) contact += normal * (0.12f * Mathf.Sin(reach * Mathf.PI));

                var rotation = Quaternion.LookRotation(Vector3.up - normal * 0.42f, normal);
                var hand = gloves[i];
                // Set the palm just outside the stone, letting the fingertips curl into the course.
                Vector3 target = contact - rotation * (hand.PalmCenter - Vector3.up * 0.012f);
                Vector3 rest = root.TransformPoint(new Vector3(side * 0.29f, -0.42f, 0.32f));
                float approach = weight * (transfer || !attached ? 1f : reach);
                wrists[i].SetPositionAndRotation(Vector3.Lerp(rest, target, approach),
                    Quaternion.Slerp(root.rotation * Quaternion.Euler(-37f, side * -12f, side * 13f), rotation, approach));
                hand.Apply(HandPose.Lerp(HandPoses.Open, StoneGrip, curl), dt);
                arms[i]?.Solve(0f, dt);
                hand.UpdateCuff(arms[i] != null ? arms[i].LastForearmDirW : wrists[i].forward);
            }
        }
    }
}

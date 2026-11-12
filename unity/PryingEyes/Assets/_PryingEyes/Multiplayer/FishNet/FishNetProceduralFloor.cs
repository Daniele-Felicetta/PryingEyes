#if PRYINGEYES_FISHNET
using System;
using System.Collections.Generic;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend
{
    /// <summary>Opt-in geometry/spawn probe on the world prefab. No FloorDirector, enemy simulation or navmesh.</summary>
    [RequireComponent(typeof(FishNetWorldState))]
    public sealed class FishNetProceduralFloor : MonoBehaviour
    {
        public string Failure { get; private set; }
        public int BuiltEpoch { get; private set; }
        public FloorInstance Current { get; private set; }
        readonly Dictionary<PlayerSeat, Vector3> reservations = new Dictionary<PlayerSeat, Vector3>();
        FishNetWorldState world;
        FishNetSessionLauncher session;
        Plan currentPlan;
        GameObject geometryRoot;

        void Awake() => world = GetComponent<FishNetWorldState>();
        void OnEnable()
        {
            if (world == null) world = GetComponent<FishNetWorldState>();
            if (world.PlaceAvatar != null) throw new InvalidOperationException("Only one procedural builder may attach to a world.");
            world.PlaceAvatar = TryPlace;
            world.BuildRequested += Build;
        }
        void OnDisable()
        {
            world.BuildRequested -= Build;
            if (world.PlaceAvatar == TryPlace) world.PlaceAvatar = null;
            world.InvalidateLocalBuild();
            ReleaseGeometry();
        }

        void ReleaseGeometry()
        {
            if (geometryRoot != null)
            {
                // Destroy is deferred. Deactivation removes old collision before any readiness acknowledgement.
                geometryRoot.SetActive(false);
                Destroy(geometryRoot);
            }
            geometryRoot = null;
            Current = null; currentPlan = null; BuiltEpoch = 0; reservations.Clear();
        }

        void Build(Plan plan, int epoch, string digest)
        {
            ReleaseGeometry();
            Failure = null;
            session = world.NetworkManager.GetComponent<FishNetSessionLauncher>();
            try
            {
                if (transform.position != Vector3.zero || transform.rotation != Quaternion.identity || transform.lossyScale != Vector3.one)
                    throw new InvalidOperationException("Procedural world prefab must have identity world transform.");
                // Owned container allows cleanup even if FloorBuilder throws before returning its FloorInstance.
                geometryRoot = new GameObject("Multiplayer Floor " + epoch);
                geometryRoot.transform.SetParent(transform, false);
                // Preserve the caller's random state; decoration contains cosmetic random users in Awake.
                var randomState = UnityEngine.Random.state;
                try { Current = FloorBuilder.Build(plan, geometryRoot.transform); }
                finally { UnityEngine.Random.state = randomState; }
                currentPlan = plan;
                // Interactions remain disabled in this probe: they currently mutate singleton gameplay state.
                // Includes Claude's new book stands, chest and consecration altar.
                foreach (var behaviour in geometryRoot.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour is IInteract interaction)
                    { Interact.Remove(interaction); behaviour.enabled = false; }
                if (Current.arrival != null) Current.arrival.SetOpenInstant(true);
                Physics.SyncTransforms();
                BuiltEpoch = epoch;
                world.NotifyBuilt(epoch, digest);
            }
            catch (Exception error)
            {
                ReleaseGeometry();
                Failure = error.Message;
                throw;
            }
        }

        // Called on the authority before an avatar may move. Reservations survive roster reordering/late joins.
        public bool TryPlace(FishNetSeatAvatar avatar)
        {
            if (Failure != null || Current == null || BuiltEpoch != world.Descriptor.Epoch || !avatar.IsServerInitialized) return false;
            var seat = new PlayerSeat(avatar.Owner.ClientId, avatar.SeatIndex);
            if (!session.Roster.Owns(seat.Peer, seat)) return false;
            foreach (var former in new List<PlayerSeat>(reservations.Keys))
                if (!session.Roster.Owns(former.Peer, former)) reservations.Remove(former);
            if (!reservations.TryGetValue(seat, out var position))
            {
                var body = avatar.GetComponent<CharacterController>();
                if (body.radius > .6f || body.height < body.radius * 2 || body.height > 2.5f ||
                    avatar.transform.lossyScale != Vector3.one)
                { ReportFailure("Spawn probe requires unit-scale avatars, radius <= 0.6 and height <= 2.5."); return false; }
                int attempts = 0;
                foreach (var point in NetworkFloorPlan.SpawnCandidates(currentPlan))
                {
                    if (++attempts > 4096) break;
                    var feet = new Vector3(point.x, .1f, point.z);
                    bool reserved = false;
                    foreach (var other in reservations.Values)
                        if ((other - feet).sqrMagnitude < 1.5f * 1.5f) { reserved = true; break; }
                    if (reserved) continue;
                    // Require actual floor support and a clear standing capsule, not just a valid Plan rectangle.
                    if (!Physics.Raycast(feet + Vector3.up * .2f, Vector3.down, out var hit, .5f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < .9f) continue;
                    feet.y = hit.point.y + .1f;
                    var bottom = feet + Vector3.up * body.radius;
                    var top = feet + Vector3.up * (body.height - body.radius);
                    bool wasEnabled = body.enabled;
                    body.enabled = false;
                    bool occupied;
                    try { occupied = Physics.CheckCapsule(bottom, top, body.radius + body.skinWidth,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore); }
                    finally { body.enabled = wasEnabled; }
                    if (occupied) continue;
                    position = feet;
                    reservations.Add(seat, position);
                    break;
                }
                if (!reservations.ContainsKey(seat))
                { ReportFailure("No collision-free procedural spawn found. Movement remains blocked."); return false; }
            }
            avatar.PlaceAt(position, BuiltEpoch);
            return true;
        }

        void ReportFailure(string message)
        {
            Failure = message;
            world.ReportBuildFailure(message);
        }
    }
}
#endif

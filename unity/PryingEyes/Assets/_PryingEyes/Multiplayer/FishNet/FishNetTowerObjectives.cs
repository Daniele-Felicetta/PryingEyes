#if PRYINGEYES_FISHNET
using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend
{
    public struct TowerObjectiveSnapshot
    {
        public int Epoch;
        public bool[] Lit;
    }

    /// <summary>Cooperative candle/exit authority. Reuses production objects, never calls the singleton interaction registry.</summary>
    [RequireComponent(typeof(FishNetWorldState), typeof(FishNetProceduralFloor))]
    public sealed class FishNetTowerObjectives : NetworkBehaviour
    {
        readonly SyncVar<TowerObjectiveSnapshot> state = new SyncVar<TowerObjectiveSnapshot>();
        readonly HashSet<PlayerSeat> holdingUse = new HashSet<PlayerSeat>();
        FishNetWorldState world;
        FishNetProceduralFloor builder;
        TowerObjectiveSnapshot applied;
        public int ObjectiveCount => state.Value.Lit?.Length ?? 0;
        public int Epoch => state.Value.Epoch;
        public int LitCount
        {
            get { int count = 0; foreach (bool lit in state.Value.Lit ?? Array.Empty<bool>()) if (lit) count++; return count; }
        }
        public bool ExitUnlocked => state.Value.Epoch > 0 && state.Value.Lit != null && LitCount == state.Value.Lit.Length;

        void Awake() { world = GetComponent<FishNetWorldState>(); builder = GetComponent<FishNetProceduralFloor>(); }
        void OnEnable()
        {
            if (world == null) Awake();
            world.ServerInput += HandleInput;
        }
        void OnDisable() { if (world != null) world.ServerInput -= HandleInput; }
        void Update()
        {
            var current = builder.Current;
            if (current == null || !world.LocalReady || builder.BuiltEpoch != world.Descriptor.Epoch) return;
            if (IsServerInitialized && state.Value.Epoch != builder.BuiltEpoch)
            {
                holdingUse.Clear();
                state.Value = new TowerObjectiveSnapshot { Epoch = builder.BuiltEpoch, Lit = new bool[current.candles.Count] };
            }
            if (IsServerInitialized && holdingUse.Count > 0)
            {
                var roster = NetworkManager.GetComponent<FishNetSessionLauncher>().Roster;
                holdingUse.RemoveWhere(seat => !roster.Owns(seat.Peer, seat));
            }
            var snapshot = state.Value;
            if (snapshot.Epoch != builder.BuiltEpoch || snapshot.Lit == null) return;
            if (snapshot.Lit.Length != current.candles.Count)
            { world.ReportBuildFailure("Objective list does not match the host floor."); return; }
            if (applied.Epoch == snapshot.Epoch && ReferenceEquals(applied.Lit, snapshot.Lit)) return;
            for (int i = 0; i < current.candles.Count; i++)
            {
                current.candles[i].Setup(snapshot.Lit[i]);
                current.candles[i].enabled = true; // visual flicker only; removed from legacy Interact registry.
            }
            if (current.exit != null)
            {
                current.exit.unlocked = ExitUnlocked;
                current.exit.SetOpenInstant(ExitUnlocked);
                current.exit.enabled = true;
            }
            applied = snapshot;
        }
        void HandleInput(FishNetSeatAvatar avatar, SeatCommand input, float dt)
        {
            var seat = new PlayerSeat(avatar.OwnerId, avatar.SeatIndex);
            if (!input.Use) { holdingUse.Remove(seat); return; }
            if (!holdingUse.Add(seat) || state.Value.Epoch != world.Descriptor.Epoch || builder.Current == null) return;
            var current = builder.Current;
            var snapshot = state.Value;
            if (snapshot.Lit == null || snapshot.Lit.Length != current.candles.Count) return;
            Candelabro nearest = null; int index = -1; float distance = float.MaxValue;
            for (int i = 0; i < current.candles.Count; i++)
            {
                var candle = current.candles[i];
                if (snapshot.Lit[i] || !CanReach(avatar, candle, candle.transform, out float d) || d >= distance) continue;
                nearest = candle; index = i; distance = d;
            }
            if (nearest != null)
            {
                // Use preserves Claude's ignition behavior. A fresh array makes SyncVar mutation explicit.
                nearest.Use();
                var lit = (bool[])snapshot.Lit.Clone(); lit[index] = nearest.lit;
                state.Value = new TowerObjectiveSnapshot { Epoch = snapshot.Epoch, Lit = lit };
                return;
            }
            if (ExitUnlocked && current.exit != null && CanReach(avatar, current.exit, current.exit.transform, out _))
                world.BeginFloor(world.Descriptor.RunSeed, checked(world.Descriptor.Floor + 1));
        }
        static bool CanReach(FishNetSeatAvatar avatar, IInteract target, Transform targetRoot, out float distance)
        {
            var origin = avatar.transform.position + Vector3.up * 1.62f;
            var delta = target.Point - origin; distance = delta.magnitude;
            if (distance > target.Radius || distance < .01f || Vector3.Dot(avatar.transform.forward, delta / distance) < .6f) return false;
            return !Physics.Raycast(origin, delta / distance, out var hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || hit.transform == targetRoot || hit.transform.IsChildOf(targetRoot);
        }
    }
}
#endif

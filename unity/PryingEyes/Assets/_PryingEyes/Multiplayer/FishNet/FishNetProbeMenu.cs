#if PRYINGEYES_FISHNET
using System;
using UnityEngine;

namespace PryingEyes.Multiplayer.FishNetBackend
{
    /// <summary>Manual test harness, not the production game menu. No connection is started automatically.</summary>
    public sealed class FishNetProbeMenu : MonoBehaviour
    {
        public FishNetSessionLauncher session;
        public Camera lobbyCamera;
        string address = "127.0.0.1", port = "7807", failure;
        int selection;
        bool visible = true;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) visible = !visible;
            if (lobbyCamera == null || session == null) return;
            bool hasLocalView = false;
            foreach (var avatar in session.Avatars)
                if (avatar != null && avatar.IsClientInitialized && avatar.IsOwner) { hasLocalView = true; break; }
            lobbyCamera.enabled = !hasLocalView;
        }

        void OnGUI()
        {
            if (!visible || session == null) return;
            GUILayout.BeginArea(new Rect(12, 12, 430, 470), GUI.skin.box);
            GUILayout.Label("MULTIPLAYER PROBE - F1 hides panel");
            GUILayout.Label("Status: " + session.Status);
            string error = failure ?? session.Failure ?? (session.World != null ? session.World.Failure : null);
            if (error != null) GUILayout.Label("Error: " + error);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && session.CanConfigure;
            GUILayout.Label("Local players (fixed during a session)");
            selection = GUILayout.SelectionGrid(selection, new[] { "1", "2", "3", "4" }, 4);
            GUILayout.Label("Host address (Join)");
            address = GUILayout.TextField(address, 253);
            GUILayout.Label("UDP port"); port = GUILayout.TextField(port, 5);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Local")) StartSession(session.Local);
            if (GUILayout.Button("Host")) StartSession(session.Host);
            if (GUILayout.Button("Join")) StartSession(() => session.Join(address));
            GUILayout.EndHorizontal();
            GUI.enabled = previousEnabled;
            if (GUILayout.Button("Stop")) session.Stop();
            if (session.World != null)
            {
                var floor = session.World.Descriptor;
                GUILayout.Label("Floor " + floor.Floor + " / epoch " + floor.Epoch + " / local geometry " + session.World.LocalReady);
                GUILayout.Label("Network ready: " + session.World.MovementEnabled);
                var objectives = session.World.GetComponent<FishNetTowerObjectives>();
                if (objectives != null) GUILayout.Label("Candles: " + objectives.LitCount + "/" + objectives.ObjectiveCount + "; exit " + (objectives.ExitUnlocked ? "open" : "sealed"));
                GUI.enabled = previousEnabled && session.World.IsServerInitialized;
                if (GUILayout.Button("Host: next procedural floor"))
                {
                    try { session.World.BeginFloor(floor.RunSeed, checked(floor.Floor + 1)); }
                    catch (Exception floorError) { failure = floorError.Message; }
                }
                GUI.enabled = previousEnabled;
            }
            GUILayout.Label("P1 WASD / Q,E turn / Space fire request");
            GUILayout.Label("P2 Arrows / Home,End turn / RightCtrl fire");
            GUILayout.Label("P3 IJKL / U,O turn / Return fire");
            GUILayout.Label("P4 TFGH / R,Y turn / Backspace fire");
            GUILayout.Label("Interact: P1 Tab / P2 RightShift / P3 RightAlt / P4 LeftAlt");
            GUILayout.Label("Movement probe only; no predicted FPS/combat yet.");
            GUILayout.EndArea();
        }

        void StartSession(Action start)
        {
            try
            {
                failure = null;
                if (!ushort.TryParse(port, out ushort value) || value == 0) throw new ArgumentException("Port must be 1..65535.");
                session.port = value;
                session.SetLocalPlayerCount(selection + 1);
                start();
            }
            catch (Exception error) { failure = error.Message; }
        }
    }
}
#endif

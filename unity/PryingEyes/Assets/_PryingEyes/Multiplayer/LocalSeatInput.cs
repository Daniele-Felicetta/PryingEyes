using UnityEngine;

namespace PryingEyes.Multiplayer
{
    /// <summary>One explicitly assigned input device/map per local seat. No global InputManager changes.</summary>
    public sealed class LocalSeatInput : MonoBehaviour
    {
        [Range(0, 3)] public int seat;
        public KeyCode forward = KeyCode.W, back = KeyCode.S, left = KeyCode.A, right = KeyCode.D;
        public KeyCode lookLeft = KeyCode.Q, lookRight = KeyCode.E;
        public KeyCode fire = KeyCode.Space;
        public KeyCode use = KeyCode.Tab;
        public Vector2 Move { get; private set; }
        public float Turn { get; private set; }
        public bool Fire { get; private set; }
        public bool Use { get; private set; }
        public bool useKeyboard = true;

        void Update()
        {
            if (!useKeyboard) return;
            Submit(new Vector2((Input.GetKey(right) ? 1 : 0) - (Input.GetKey(left) ? 1 : 0),
                (Input.GetKey(forward) ? 1 : 0) - (Input.GetKey(back) ? 1 : 0)),
                (Input.GetKey(lookRight) ? 1 : 0) - (Input.GetKey(lookLeft) ? 1 : 0), Input.GetKey(fire), Input.GetKey(use));
        }

        // Gamepad/Input System adapter calls this with useKeyboard=false. Axes are rates, not mouse deltas.
        public void Submit(Vector2 move, float turn, bool firing, bool interacting = false)
        {
            Move = float.IsNaN(move.x) || float.IsInfinity(move.x) || float.IsNaN(move.y) || float.IsInfinity(move.y)
                ? Vector2.zero : Vector2.ClampMagnitude(move, 1);
            Turn = float.IsNaN(turn) || float.IsInfinity(turn) ? 0 : Mathf.Clamp(turn, -1, 1);
            Fire = firing;
            Use = interacting;
        }

        void OnDisable() => Submit(Vector2.zero, 0, false);
        void OnApplicationFocus(bool focused) { if (!focused) Submit(Vector2.zero, 0, false); }
    }
}

using PryingEyes.Deserto;
using PryingEyes.Proc;
using UnityEngine;

namespace PryingEyes
{
    public partial class PlayerController
    {
        const float GripHeight = 1.70f, WallDistance = 0.58f;
        enum ClimbMove { Free, Catch, Step, Corner, Exit, Hold }
        struct Grip
        {
            public Vector3 point, normal;
            public Collider wall;
            public Vector3 Feet => point + normal * WallDistance - Vector3.up * GripHeight;
        }

        ClimbMove climbMove;
        Grip grip, lastGrip;
        Vector3 climbFrom, climbTo, climbLift, climbOver;
        float climbTime, climbDuration, climbLockout, parkourStow, transferTime;
        Vector3 transferFrom;
        bool airJumpUsed, rightHandLeads, lowClimb;
        ParkourHands climbHands;
#if !PE_DISTRIBUTION_BUILD
        internal bool parkourTestJump;
        internal Vector3? parkourTestDirection;
#endif

        public bool OnClimbWall => climbMove != ClimbMove.Free;
        public float ClimbWallYaw => Mathf.Atan2(-grip.normal.x, -grip.normal.z);
        public bool ParkourHandsBusy => OnClimbWall || Gliding || transferTime > 0f || parkourStow > 0.3f;
        Vector3 ClimbEyeOffset => (-grip.normal * 0.075f - Vector3.up * 0.035f) * (parkourStow * (1f - WingAmount) * GameSettings.Motion);

        void ResetParkour()
        {
            climbMove = ClimbMove.Free;
            grip = lastGrip = default;
            airJumpUsed = false;
            climbLockout = parkourStow = transferTime = 0f;
            ResetWingsuit();
            climbHands?.Hide();
            if (weapon != null) { weapon.ParkourStow = 0f; weapon.ParkourHolding = false; }
        }

        bool TryAirJump(Vector3 wish)
        {
            if (!DesertMode.Active || airJumpUsed || Downed) return false;
            airJumpUsed = true;
            jumpBuf = 0f;
            mantleWindow = 0.38f;
            Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
            if (wish.sqrMagnitude > 0f)
                horizontal = Vector3.MoveTowards(horizontal, wish * Mathf.Max(walk, horizontal.magnitude), 1.8f);
            vel = horizontal + Vector3.up * jumpVel * 1.12f;
            FovPunch(2f);
            return true;
        }

        static bool FindGrip(Vector3 origin, Vector3 toward, float distance, out Grip grip)
        {
            const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
            grip = default;
            if (!Physics.Raycast(origin, toward, out var hit, distance, HotelNav.EnvMask, ignore) ||
                Mathf.Abs(hit.normal.y) > 0.3f || hit.rigidbody != null || hit.collider is CharacterController ||
                DesertTerrain.IsSand(hit.collider) || Surfaces.Of(hit) == PryingEyes.Surface.Glass) return false;
            Vector3 normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            if (Vector3.Dot(-toward, normal) < 0.35f) return false;

            // A course is shared by the actual stone cuts and the hand targets on generated walls.
            if (hit.collider.name == "Guscio")
            {
                float y = hit.collider.transform.position.y;
                float snap = y + Mathf.Round((hit.point.y - y) / MeshBuilder.ClimbCourse) * MeshBuilder.ClimbCourse;
                if (Physics.Raycast(origin + Vector3.up * (snap - hit.point.y), toward, out var inset, distance, HotelNav.EnvMask, ignore) &&
                    inset.collider == hit.collider && Vector3.Dot(inset.normal, hit.normal) > 0.85f && Surfaces.Of(inset) != PryingEyes.Surface.Glass)
                    hit = inset;
            }
            grip = new Grip { wall = hit.collider, point = hit.point, normal = normal };
            return true;
        }

        bool ParkourStep(float dt, bool input, bool dropPressed, float forward, float sideways, Vector3 wish, Vector3 facing)
        {
            climbLockout = Mathf.Max(0f, climbLockout - dt);
            transferTime = Mathf.Max(0f, transferTime - dt);
            if (!DesertMode.Active || Downed || !cc.enabled)
            {
                climbMove = ClimbMove.Free;
                return false;
            }

            if (!OnClimbWall)
            {
                if (cc.isGrounded) { airJumpUsed = false; transferTime = 0f; }
                bool transferring = transferTime > 0f;
                if (!input || Mantling || cc.isGrounded || climbLockout > 0f ||
                    (!transferring && (forward < 0.15f || (!Gliding && jumpBuf <= 0f && mantleWindow <= 0f)))) return false;
                if (transferring && (transform.position - transferFrom).sqrMagnitude < 0.91f) return false;
                Vector3 reach = transform.position + Vector3.up * GripHeight;
                if (!TryReachWall(reach, facing, transferring || Gliding || jumpBuf > 0f, out var contact)) return false;
                transferTime = 0f;
                StopWingsuit();
                lastGrip = grip = contact;
                StartClimbMove(ClimbMove.Catch, grip.Feet, 0.15f);
                weapon.BeginParkour();
            }
            else if (grip.wall == null || !grip.wall.enabled || !grip.wall.gameObject.activeInHierarchy ||
                     (input && dropPressed))
            {
                LeaveWall(grip.normal * 1.1f + Vector3.down * 1.7f);
                return false;
            }
            else if (input && climbMove == ClimbMove.Hold)
            {
                if (jumpBuf > 0f)
                {
                    JumpFromWall(forward, sideways, wish, facing);
                    return false;
                }
                AdvanceClimb(forward, sideways);
                jumpBuf = 0f;
            }

            vel = Vector3.zero;
            coyote = 0f;
            crouch = lowClimb;
            slide = false;
            float bodyHeight = lowClimb ? heightCrouch : heightStand;
            if (Mathf.Abs(cc.height - bodyHeight) > 0.001f)
            {
                cc.height = bodyHeight;
                cc.center = Vector3.up * (bodyHeight * 0.5f);
            }
            if (climbMove == ClimbMove.Hold) return true;
            climbTime += dt;
            float t = Mathf.Clamp01(climbTime / climbDuration);
            Vector3 target = climbMove switch
            {
                ClimbMove.Exit => ExitAt(t),
                ClimbMove.Corner => t < 0.5f
                    ? Vector3.Lerp(climbFrom, climbOver, Mathf.SmoothStep(0f, 1f, t * 2f))
                    : Vector3.Lerp(climbOver, climbTo, Mathf.SmoothStep(0f, 1f, (t - 0.5f) * 2f)),
                _ => Vector3.Lerp(climbFrom, climbTo, Mathf.SmoothStep(0f, 1f, t))
            };
            cc.Move(target - transform.position);
            if ((transform.position - target).sqrMagnitude > 0.075f * 0.075f)
            {
                LeaveWall(grip.normal * 1.1f + Vector3.down);
                return true;
            }
            if (t < 1f) return true;
            if (climbMove == ClimbMove.Exit)
            {
                LeaveWall(Vector3.down * 0.8f);
                Synth.Step(Surface(), 0.42f);
            }
            else
            {
                climbMove = ClimbMove.Hold;
                airJumpUsed = false;
                Sfx.At(SoundBank.Impact(PryingEyes.Surface.Stone), grip.point, 0.13f, 0.94f);
                Kick(0.36f * GameSettings.Motion, 0f, 13f);
            }
            return true;
        }

        bool TryReachWall(Vector3 reach, Vector3 facing, bool longReach, out Grip contact)
        {
            float distance = longReach ? 1.38f : 0.96f;
            if (FindGrip(reach, facing, distance, out contact) && ClimbClear(transform.position, contact.Feet)) return true;
            if (!longReach) return false;
            if (FindGrip(reach + Vector3.up * 0.29f, facing, distance, out contact) && ClimbClear(transform.position, contact.Feet)) return true;
            return transferTime > 0f && FindGrip(reach, -grip.normal, distance, out contact) && ClimbClear(transform.position, contact.Feet);
        }

        void JumpFromWall(float forward, float sideways, Vector3 wish, Vector3 facing)
        {
            float tangent = Mathf.Abs(sideways) > 0.15f ? sideways : 0f;
            bool rising = forward > 0.15f;
            bool directed = rising || Mathf.Abs(tangent) > 0.15f;
            Vector3 along = Vector3.Cross(grip.normal, Vector3.up);
            bool across = rising && Vector3.Dot(facing, grip.normal) > 0.32f;
            Vector3 launch = across ? facing * 7.4f + along * tangent * 2f : directed
                ? grip.normal * (rising ? 0.9f : 1.6f) + along * tangent * 6.4f
                : (grip.normal + Vector3.ProjectOnPlane(wish, grip.normal) * 0.42f).normalized * 4.6f;
            transferFrom = transform.position;
            LeaveWall(launch + Vector3.up * jumpVel * (rising ? 1.46f : directed ? 1.08f : 1f));
            if (directed)
            {
                climbLockout = 0.17f;
                transferTime = 0.82f;
                rightHandLeads = Mathf.Abs(tangent) > 0.15f ? tangent > 0f : !rightHandLeads;
                FovPunch(rising ? 4f : 2.8f);
            }
        }

        void AdvanceClimb(float forward, float sideways)
        {
            Vector3 along = Vector3.Cross(grip.normal, Vector3.up);
            Vector3 lateral = along * Mathf.Sign(sideways);
            float vertical = Mathf.Abs(forward) > 0.15f ? Mathf.Sign(forward) : 0f;
            bool movingSide = Mathf.Abs(sideways) > 0.15f;
            if (vertical == 0f && !movingSide) return;

            // A side opening can be reached from the wall even when there is another grip beside it.
            if (movingSide && vertical == 0f && TryClimbExit(lateral, lateral: true)) return;
            Vector3 offset = (Vector3.up * vertical + (movingSide ? lateral : Vector3.zero)) * MeshBuilder.ClimbCourse;
            Vector3 probe = grip.point + offset + grip.normal * (WallDistance + 0.12f);
            if (FindGrip(probe, -grip.normal, 0.95f, out var next) && ClimbClear(transform.position, next.Feet))
            {
                lastGrip = grip;
                grip = next;
                rightHandLeads = movingSide ? sideways > 0f : !rightHandLeads;
                StartClimbMove(ClimbMove.Step, grip.Feet, 0.24f);
                return;
            }
            if (movingSide && vertical == 0f && WrapClimbCorner(lateral, sideways > 0f)) return;
            if (vertical > 0f && !TryWindowEntry()) TryClimbExit(-grip.normal, lateral: false);
        }

        bool WrapClimbCorner(Vector3 side, bool right)
        {
            Vector3 search = grip.point + side * 0.78f - grip.normal * 0.30f;
            if (!FindGrip(search, -side, 0.70f, out var next) || Vector3.Dot(next.normal, side) < 0.80f) return false;
            Vector3 corner = grip.Feet + side * Vector3.Dot(next.point - grip.point, side) + next.normal * WallDistance;
            if (!ClimbClear(transform.position, corner) || !ClimbClear(corner, next.Feet)) return false;
            climbOver = corner;
            lastGrip = grip;
            grip = next;
            rightHandLeads = right;
            StartClimbMove(ClimbMove.Corner, grip.Feet, 0.42f);
            return true;
        }

        bool ClimbClear(Vector3 from, Vector3 to, float bodyHeight = 0f)
        {
            if (bodyHeight <= 0f) bodyHeight = heightStand;
            float radius = Mathf.Max(0.05f, cc.radius - cc.skinWidth * 0.5f);
            Vector3 travel = to - from;
            return StandFree(to, bodyHeight, radius) && (travel.sqrMagnitude < 1e-6f ||
                !Physics.CapsuleCast(from + Vector3.up * (radius + 0.045f), from + Vector3.up * (bodyHeight - radius),
                    radius, travel.normalized, travel.magnitude, HotelNav.EnvMask, QueryTriggerInteraction.Ignore));
        }

        void StartClimbMove(ClimbMove move, Vector3 to, float duration, bool low = false)
        {
            climbMove = move;
            lowClimb = low;
            climbFrom = transform.position;
            climbTo = to;
            climbDuration = duration;
            climbTime = jumpBuf = mantleWindow = 0f;
        }

        bool TryWindowEntry()
        {
            const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
            Vector3 inward = -grip.normal;
            Vector3 from = transform.position;
            Vector3 opening = grip.point + Vector3.up * 0.65f;
            if (Physics.Raycast(opening - inward * 0.18f, inward, 1.05f, HotelNav.EnvMask, ignore) ||
                !Physics.Raycast(opening + inward * 0.14f, Vector3.down, out var sill, 1.2f, HotelNav.EnvMask, ignore) ||
                sill.collider != grip.wall || sill.normal.y < 0.68f) return false;
            Vector3 inside = sill.point + inward * 0.96f;
            if (!Physics.Raycast(inside + Vector3.up * 0.25f, Vector3.down, out var floor, 1.95f, HotelNav.EnvMask, ignore) ||
                floor.normal.y < 0.7f || DesertTerrain.IsSand(floor.collider) || floor.point.y > sill.point.y - 0.18f) return false;

            float y = sill.point.y + 0.07f;
            Vector3 lift = new Vector3(from.x, y, from.z);
            Vector3 over = new Vector3(inside.x, y, inside.z);
            Vector3 land = floor.point + Vector3.up * 0.045f;
            if (!ClimbClear(from, lift, heightCrouch) || !ClimbClear(lift, over, heightCrouch) || !ClimbClear(over, land, heightCrouch)) return false;
            climbLift = lift;
            climbOver = over;
            StartClimbMove(ClimbMove.Exit, land, 0.68f, low: true);
            return true;
        }

        bool TryClimbExit(Vector3 direction, bool lateral)
        {
            const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
            Vector3 from = transform.position;
            float rise = lateral ? 1.35f : 2.55f;
            Vector3 approach = from + direction * (lateral ? 1.03f : 1.64f);
            if (!Physics.Raycast(approach + Vector3.up * rise, Vector3.down, out var support, rise + 0.35f, HotelNav.EnvMask, ignore) ||
                support.normal.y < 0.7f || DesertTerrain.IsSand(support.collider) ||
                support.point.y < from.y + (lateral ? 0.10f : 0.16f)) return false;

            float lift = Mathf.Max(from.y, support.point.y + 0.09f);
            if (!lateral && Physics.Raycast(grip.point + direction * 0.13f + Vector3.up * 0.85f, Vector3.down,
                out var edge, 1.45f, HotelNav.EnvMask, ignore))
                lift = Mathf.Max(lift, edge.point.y + 0.09f);
            Vector3 up = new Vector3(from.x, lift, from.z);
            Vector3 over = new Vector3(approach.x, lift, approach.z);
            Vector3 land = support.point + Vector3.up * 0.045f;
            if (!ClimbClear(from, up) || !ClimbClear(up, over) || !ClimbClear(over, land)) return false;
            climbLift = up;
            climbOver = over;
            StartClimbMove(ClimbMove.Exit, land, lateral ? 0.46f : 0.75f);
            return true;
        }

        Vector3 ExitAt(float t)
        {
            if (t < 0.46f) return Vector3.Lerp(climbFrom, climbLift, Mathf.SmoothStep(0f, 1f, t / 0.46f));
            if (t < 0.85f) return Vector3.Lerp(climbLift, climbOver, Mathf.SmoothStep(0f, 1f, (t - 0.46f) / 0.39f));
            return Vector3.Lerp(climbOver, climbTo, Mathf.SmoothStep(0f, 1f, (t - 0.85f) / 0.15f));
        }

        void LeaveWall(Vector3 velocity)
        {
            climbMove = ClimbMove.Free;
            climbLockout = 0.25f;
            jumpBuf = mantleWindow = 0f;
            vel = velocity;
        }

        void DrawParkourHands(float dt)
        {
            bool attached = OnClimbWall;
            bool busy = attached || Gliding || transferTime > 0f;
            parkourStow = Mathf.MoveTowards(parkourStow, busy ? 1f : 0f, dt / (busy ? 0.12f : 0.20f));
            weapon.ParkourStow = parkourStow;
            weapon.ParkourHolding = busy;
            if (parkourStow <= 0f) { climbHands?.Hide(); return; }
            climbHands ??= ParkourHands.Build(cam, Seats.ViewLayer);
            if (!attached && WingAmount > 0.02f) { climbHands?.Glide(WingAmount, WingsuitRoll, dt); return; }
            if (!attached && transferTime > 0f) { climbHands?.Leap(rightHandLeads, 1f - transferTime / 0.82f, dt); return; }
            if (grip.normal.sqrMagnitude < 0.1f) { climbHands?.Hide(); return; }
            float progress = Mathf.Clamp01(climbTime / climbDuration);
            float release = climbMove == ClimbMove.Exit ? Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.12f, 0.56f, progress)) : 1f;
            climbHands?.Tick(lastGrip.point, grip.point, lastGrip.normal, grip.normal, rightHandLeads, climbMove == ClimbMove.Step || climbMove == ClimbMove.Corner,
                progress, attached, parkourStow * release, dt);
        }

        bool ParkourPrompt()
        {
            if (!DesertMode.Active) return false;
            if (OnClimbWall)
            {
                hud?.SetPrompt(PadControls.Preferred ? "STICK  ARRAMPICATI   /   STICK + A  SALTO" : "WASD  ARRAMPICATI   /   DIREZIONE + SPAZIO  BALZO");
                return true;
            }
            if (Gliding || CanOpenWingsuit())
            {
                hud?.SetPrompt(PadControls.Preferred ? "TIENI A  TUTA ALARE" : "TIENI SPAZIO  TUTA ALARE");
                return true;
            }
            var facing = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            if (!FindGrip(transform.position + Vector3.up * GripHeight, facing, 1f, out _)) return false;
            hud?.SetPrompt(PadControls.Preferred ? "AVANTI + A  AGGRAPPATI" : "AVANTI + SALTO  AGGRAPPATI");
            return true;
        }
    }
}

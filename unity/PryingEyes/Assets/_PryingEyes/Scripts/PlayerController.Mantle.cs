using UnityEngine;
using UnityEngine.AI;
using PryingEyes.Proc;

namespace PryingEyes
{
    // Scavalcare e arrampicarsi (2026-11-15). Il salto arriva a 60 cm: tavoli, panche, sarcofagi, muretti e lampadari caduti erano
    // muri. Ora Salto davanti a un ostacolo alto da 42 a 135 cm (o contro il suo bordo mentre si e' in aria) lo supera: se e' sottile
    // e dietro c'e' pavimento alla stessa quota (sulla navmesh) lo si scavalca, altrimenti ci si sale sopra se c'e' posto in piedi o
    // accovacciati. Il corpo segue una curva (su fino al bordo, poi avanti), l'arma si abbassa come in corsa, la visuale si china e
    // si inclina un poco, all'arrivo il passo. Mai giu' da una balconata (dietro ci vuole pavimento alla stessa quota) ne' fuori
    // dalle stanze.
    public partial class PlayerController
    {
        public const float MantleMin = 0.42f, MantleMax = 1.35f;
        float mantleT = -1f, mantleDur, mantleWindow, mantleSide = 1f;
        Vector3 mantleFrom, mantleCtrl, mantleTo, mantleDir;
        bool mantleVault, mantleCrouch;
        float mantlePitch, mantleRoll, mantleDrop;

        /// <summary>Sta scavalcando o salendo.</summary>
        public bool Mantling => mantleT >= 0f;
        /// <summary>L'ultimo superamento: vero se scavalcato (dall'altra parte), falso se salito sopra.</summary>
        public bool LastMantleVault => mantleVault;

        /// <summary>Cerca davanti (direzione orizzontale) un ostacolo da superare; se c'e' parte e torna vero.</summary>
        public bool TryMantle(Vector3 dir)
        {
            if (Mantling || !alive || cc == null || !cc.enabled) return false;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();
            Vector3 feet = transform.position;
            float bodyR = Mathf.Max(0.05f, cc.radius - cc.skinWidth);
            // 1) qualcosa davanti, sopra il gradino, quasi di fronte
            if (!Physics.SphereCast(feet + Vector3.up * 0.36f - dir * 0.05f, bodyR * 0.8f, dir, out var front, 0.9f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return false;
            Vector3 fn = front.normal; fn.y = 0f;
            if (fn.sqrMagnitude < 1e-4f || Vector3.Dot(fn.normalized, -dir) < 0.55f) return false;
            // 2) la cima, cercata dall'alto appena oltre la faccia
            Vector3 probe = front.point + dir * 0.16f;
            probe.y = feet.y + MantleMax + 0.4f;
            if (!Physics.Raycast(probe, Vector3.down, out var top, MantleMax + 0.4f - MantleMin + 0.03f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return false;
            float h = top.point.y - feet.y;
            if (h < MantleMin || h > MantleMax || top.normal.y < 0.75f) return false;
            // sopra la cima ci deve passare il corpo accovacciato (niente soffitti bassi, mensole, ringhiere alte)
            Vector3 over = new Vector3(front.point.x, top.point.y, front.point.z) + dir * 0.2f;
            if (Physics.CheckCapsule(over + Vector3.up * (bodyR + 0.04f), over + Vector3.up * (heightCrouch - bodyR), bodyR * 0.9f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return false;
            var room = FloorDirector.I != null && FloorDirector.I.cur != null ? FloorDirector.I.cur : null;

            // 3a) scavalcare: ostacolo sottile e pavimento dietro alla stessa quota, sulla navmesh, con posto in piedi
            foreach (float d in new[] { 0.45f, 0.7f, 0.95f })
            {
                Vector3 beyond = front.point + dir * (d + bodyR + 0.05f);
                Vector3 from = new Vector3(beyond.x, top.point.y + 0.3f, beyond.z);
                if (!Physics.Raycast(from, Vector3.down, out var fl, h + 0.3f + 0.5f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(fl.point.y - feet.y) > 0.35f || fl.normal.y < 0.75f) continue;
                if (!NavMesh.SamplePosition(fl.point, out var nh, 0.45f, NavMesh.AllAreas) || Mathf.Abs(nh.position.y - fl.point.y) > 0.3f) continue;
                if (room != null && room.RoomAt(fl.point) == null) continue;
                if (!StandFree(fl.point, heightStand, bodyR)) continue;
                // la strada sopra l'ostacolo e' libera fino a terra dall'altra parte
                Vector3 mid = new Vector3(front.point.x, top.point.y, front.point.z) + dir * (d * 0.5f);
                if (Physics.CheckCapsule(mid + Vector3.up * (bodyR + 0.06f), mid + Vector3.up * (heightCrouch - bodyR), bodyR * 0.9f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) continue;
                StartMantle(true, dir, fl.point, new Vector3(mid.x, top.point.y + 0.42f, mid.z), 0.30f + 0.12f * h, false);
                return true;
            }
            // 3b) salire sopra: c'e' una superficie piana e posto in piedi (o accovacciati)
            Vector3 land = new Vector3(top.point.x, top.point.y, top.point.z) + dir * (bodyR + 0.12f);
            if (!Physics.Raycast(land + Vector3.up * 0.4f, Vector3.down, out var lt, 0.7f, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(lt.point.y - top.point.y) > 0.12f || lt.normal.y < 0.75f) return false;
            if (room != null && room.RoomAt(lt.point) == null) return false;
            bool stand = StandFree(lt.point, heightStand, bodyR);
            if (!stand && !StandFree(lt.point, heightCrouch, bodyR)) return false;
            Vector3 edge = new Vector3(front.point.x, top.point.y + 0.22f, front.point.z) - dir * 0.05f;
            StartMantle(false, dir, lt.point, edge, 0.36f + 0.14f * h, !stand);
            return true;
        }

        /// <summary>Teletrasporto, rinascita: lo scavalco in corso si interrompe (il chiamante rimette a posto il controller).</summary>
        void CancelMantle()
        {
            ClearGrapple();
            ResetParkour();
            mantleT = -1f;
            mantleWindow = 0f;
            mantlePitch = mantleRoll = mantleDrop = 0f;
        }

        bool StandFree(Vector3 at, float height, float r)
        {
            return !Physics.CheckCapsule(at + Vector3.up * (r + 0.03f), at + Vector3.up * (height - r), r, HotelNav.EnvMask, QueryTriggerInteraction.Ignore);
        }

        void StartMantle(bool vault, Vector3 dir, Vector3 to, Vector3 ctrl, float dur, bool crouchAfter)
        {
            mantleVault = vault;
            mantleDir = dir;
            mantleFrom = transform.position;
            mantleTo = to;
            mantleCtrl = ctrl;
            mantleDur = dur;
            mantleCrouch = crouchAfter;
            mantleT = 0f;
            mantleSide = -mantleSide;
            mantleWindow = 0f;
            jumpBuf = 0f; coyote = 0f; slide = false;
            cc.enabled = false;
            Synth.Step(Surface(), 0.3f);
        }

        /// <summary>Un fotogramma del superamento: posizione lungo la curva, visuale china e inclinata; all'arrivo si riparte.</summary>
        void MantleTick(float dt)
        {
            mantleT += dt;
            float k = Mathf.Clamp01(mantleT / mantleDur);
            // su in fretta fino al bordo, poi avanti: la curva ha il punto di controllo sopra il bordo
            float e = 1f - (1f - k) * (1f - k);
            Vector3 a = Vector3.Lerp(mantleFrom, mantleCtrl, e), b = Vector3.Lerp(mantleCtrl, mantleTo, e);
            transform.position = Vector3.Lerp(a, b, e);
            float bump = Mathf.Sin(k * Mathf.PI);
            float motion = GameSettings.Motion;
            mantlePitch = 7f * bump * motion;
            mantleRoll = 5f * bump * mantleSide * motion;
            mantleDrop = 0.12f * bump;
            if (k < 1f) return;
            transform.position = mantleTo;
            cc.enabled = true;
            mantleT = -1f;
            mantlePitch = mantleRoll = mantleDrop = 0f;
            vel = mantleDir * Mathf.Max(new Vector2(vel.x, vel.z).magnitude, mantleVault ? 3.6f : 2.2f);
            vel.y = 0f;
            if (mantleCrouch) crouch = true;
            Synth.Step(Surface(), 0.42f);
        }
    }
}

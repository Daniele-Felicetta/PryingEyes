using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Qualcosa con cui si interagisce premendo F guardandolo da vicino (candelabri, ascensore di uscita).</summary>
    public interface IInteract
    {
        Vector3 Point { get; }
        float Radius { get; }
        string Prompt { get; }     // null: per ora non si puo' usare
        void Use();
    }

    public static class Interact
    {
        static readonly List<IInteract> all = new List<IInteract>();
        static IInteract near;
        static Transform lastCamera;

        public static void Add(IInteract i) { if (!all.Contains(i)) all.Add(i); }
        public static void Remove(IInteract i) { all.Remove(i); if (near == i) near = null; }
        public static void Clear() { all.Clear(); near = null; lastCamera = null; }

        /// <summary>Il primo ostacolo deve essere l'oggetto stesso: niente uso attraverso pareti, vetri o serrande.</summary>
        public static bool Visible(Transform cam, Vector3 point, Transform owner)
        {
            return cam != null && Visible(cam.position, point, owner);
        }

        public static bool Visible(Vector3 eye, Vector3 point, Transform owner)
        {
            Vector3 to = point - eye;
            float distance = to.magnitude;
            if (distance < 0.001f) return true;
            if (!Physics.Raycast(eye, to / distance, out var hit, distance, HotelNav.EnvMask, QueryTriggerInteraction.Ignore)) return true;
            return owner != null && (hit.transform == owner || hit.transform.IsChildOf(owner));
        }

        static bool Selectable(IInteract it, Transform cam)
        {
            if (cam == null || it == null || (it is Object obj && obj == null) || it.Prompt == null) return false;
            Vector3 to = it.Point - cam.position;
            float distance = to.magnitude;
            return distance <= it.Radius && Vector3.Dot(cam.forward, to / Mathf.Max(distance, 0.01f)) >= 0.6f &&
                Visible(cam, it.Point, (it as Component)?.transform);
        }

        /// <summary>Sceglie cio' che si guarda da vicino e scrive il prompt. Vero se c'e' qualcosa da usare.</summary>
        public static bool Tick(Transform cam)
        {
            var gm = GameManager.I;
            near = null;
            lastCamera = cam;
            if (gm == null || gm.hud == null || cam == null) return false;
            float bd = float.MaxValue;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var it = all[i];
                if (it == null || (it is Object uo && uo == null)) { all.RemoveAt(i); continue; }
                float d = Vector3.Distance(it.Point, cam.position);
                if (d >= bd || !Selectable(it, cam)) continue;
                near = it; bd = d;
            }
            if (near == null) return false;
            gm.hud.SetPrompt(near.Prompt + "   [F]");
            return true;
        }

        public static void TryUse()
        {
            if (!GameMenu.Blocked && Selectable(near, lastCamera)) near.Use();
        }
    }
}

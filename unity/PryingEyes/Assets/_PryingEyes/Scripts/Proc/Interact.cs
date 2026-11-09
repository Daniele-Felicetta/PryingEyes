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

        public static void Add(IInteract i) { if (!all.Contains(i)) all.Add(i); }
        public static void Remove(IInteract i) { all.Remove(i); if (near == i) near = null; }
        public static void Clear() { all.Clear(); near = null; }

        /// <summary>Sceglie cio' che si guarda da vicino e scrive il prompt. Vero se c'e' qualcosa da usare.</summary>
        public static bool Tick(Transform cam)
        {
            var gm = GameManager.I;
            if (gm == null || gm.hud == null) return false;
            near = null;
            float bd = float.MaxValue;
            Vector3 cp = cam.position, fwd = cam.forward;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var it = all[i];
                if (it == null || (it is Object uo && uo == null)) { all.RemoveAt(i); continue; }
                if (it.Prompt == null) continue;
                Vector3 to = it.Point - cp;
                float d = to.magnitude;
                if (d > it.Radius || d >= bd) continue;
                if (Vector3.Dot(fwd, to / Mathf.Max(d, 0.01f)) < 0.6f) continue;
                near = it; bd = d;
            }
            if (near == null) return false;
            gm.hud.SetPrompt(near.Prompt + "   [F]");
            return true;
        }

        public static void TryUse()
        {
            if (near != null) near.Use();
        }
    }
}

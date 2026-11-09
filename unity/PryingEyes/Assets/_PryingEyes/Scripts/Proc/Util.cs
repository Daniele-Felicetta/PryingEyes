using UnityEngine;

namespace PryingEyes.Proc
{
    public static class Util
    {
        /// <summary>Distrugge un oggetto in gioco (Destroy) o in editor (DestroyImmediate): il generatore gira anche in edit-mode per le foto.</summary>
        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}

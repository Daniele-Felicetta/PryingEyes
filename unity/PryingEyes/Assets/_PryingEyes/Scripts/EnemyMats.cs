using UnityEngine;

namespace PryingEyes
{
    /// <summary>Materiali dei nemici generati da codice. Il corpo di occhio, abate e monaco viene dai modelli di
    /// Blender (Resources/Occhio, Abate, Monaco); resta solo l'emissivo semplice (sfera di carica, onde d'urto).</summary>
    public static class EnemyMats
    {
        public static Material Glow(Color c, float intensity)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = Color.black;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            m.SetFloat("_Metallic", 0.2f);
            m.SetFloat("_Smoothness", 0.35f);
            return m;
        }
    }
}

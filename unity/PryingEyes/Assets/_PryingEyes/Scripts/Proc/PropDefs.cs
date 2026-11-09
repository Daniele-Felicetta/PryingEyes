using System.Collections.Generic;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Ingombro degli arredi (metri): larghezza X, altezza Y, profondita' Z. Davanti = +Z, origine al centro a terra.
    /// Lo usano i modelli Blender (stesse misure), l'editor (collider dei prefab) e il generatore (dove metterli).</summary>
    public static class PropDefs
    {
        public struct Def
        {
            public Vector3 size;      // x larghezza, y altezza, z profondita'
            public Vector3 center;    // centro del collider rispetto all'origine
            public bool solid;        // false: decorazione senza collider
            public bool tall;         // alto oltre i 2 m: conta per i soffitti bassi
        }

        static readonly Dictionary<string, Def> d = new Dictionary<string, Def>();

        static void A(string n, float w, float h, float dp, bool solid = true, float cy = -1f, float cz = 0f)
        {
            d[n] = new Def { size = new Vector3(w, h, dp), center = new Vector3(0f, cy < 0f ? h * 0.5f : cy, cz), solid = solid, tall = h > 2f };
        }

        static PropDefs()
        {
            // albergo, gia' esistenti
            A("arr_divano", 2.48f, 0.9f, 1.02f);
            A("arr_poltrona", 1.08f, 0.88f, 0.95f);
            A("arr_tavolino", 1.4f, 0.48f, 0.72f);
            A("arr_banco", 4.04f, 1.2f, 1.48f, true, 0.6f, 0.08f);
            A("arr_chiavi", 5.1f, 2.8f, 0.5f);
            A("arr_carrello", 1.1f, 1.6f, 0.7f);
            A("arr_lampada", 0.42f, 1.45f, 0.42f);
            A("arr_orologio", 0.6f, 2.6f, 0.38f);
            A("arr_palma", 0.66f, 0.55f, 0.66f);
            // albergo, nuovi
            A("arr_letto", 1.75f, 1.05f, 2.15f);
            A("arr_comodino", 0.5f, 0.55f, 0.42f);
            A("arr_armadio", 1.3f, 2.2f, 0.62f);
            A("arr_scrivania", 1.25f, 0.76f, 0.62f);
            A("arr_sedia", 0.5f, 0.92f, 0.5f);
            A("arr_tavolo", 0.95f, 0.76f, 0.95f);
            A("arr_pianoforte", 1.6f, 1.05f, 2.2f);
            A("arr_vasca", 0.8f, 0.62f, 1.75f);
            A("arr_fornello", 1.7f, 2.2f, 0.85f);
            A("arr_tavolo_acciaio", 1.9f, 0.9f, 0.8f);
            A("arr_scaffale", 1.2f, 1.9f, 0.45f);
            // chiesa
            A("chi_panca", 2.6f, 0.95f, 0.9f);
            A("chi_altare", 2.4f, 1.05f, 1.0f);
            A("chi_pala", 3.4f, 4.4f, 0.45f);
            A("chi_candelabro", 0.6f, 1.9f, 0.6f);
            A("chi_ceri", 0.9f, 1.25f, 0.45f);
            A("chi_confessionale", 2.5f, 2.7f, 1.15f);
            A("chi_pulpito", 1.3f, 3.4f, 1.3f);
            A("chi_fonte", 0.9f, 1.1f, 0.9f);
            A("chi_statua", 0.8f, 2.5f, 0.8f);
            A("chi_organo", 3.6f, 5.5f, 1.3f);
            A("chi_sarcofago", 2.3f, 1.0f, 1.0f);
            A("chi_lampadario", 1.5f, 1.6f, 1.5f, false);
            A("chi_stallo", 2.8f, 2.3f, 0.9f);
            A("chi_inginocchiatoio", 0.6f, 0.95f, 0.5f);
            A("chi_occhio", 1.2f, 1.2f, 0.12f, false);
        }

        public static Def Get(string name)
        {
            if (d.TryGetValue(name, out var v)) return v;
            return new Def { size = new Vector3(1f, 1f, 1f), center = new Vector3(0f, 0.5f, 0f), solid = true };
        }

        public static IEnumerable<string> Names => d.Keys;
    }
}

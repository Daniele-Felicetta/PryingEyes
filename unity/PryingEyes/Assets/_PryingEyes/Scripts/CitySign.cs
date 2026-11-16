using UnityEngine;

namespace PryingEyes
{
    // Il font legacy ignora la profondità: un'insegna deve invece sparire dietro i muri e dal retro.
    [RequireComponent(typeof(TextMesh))]
    public sealed class CitySign : MonoBehaviour
    {
        Renderer label;
        void Awake() => label = GetComponent<Renderer>();
        void LateUpdate()
        {
            var eyes = Seats.Eyes();         // la camera principale o, a schermo condiviso, un occhio per giocatore: basta che la veda uno
            if (eyes.Count == 0) return;
            bool visible = false;
            for (int i = 0; i < eyes.Count && !visible; i++) visible = SeenFrom(eyes[i].position);
            label.enabled = visible;
        }

        bool SeenFrom(Vector3 eye)
        {
            bool visible = Vector3.Dot(transform.forward, eye - transform.position) < 0;
            if (visible && Physics.Linecast(eye, transform.position, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                visible = hit.distance >= Vector3.Distance(eye, transform.position) - .1f;
            return visible;
        }
    }
}

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
            var camera = Camera.main;
            if (camera == null) return;
            Vector3 eye = camera.transform.position;
            bool visible = Vector3.Dot(transform.forward, eye - transform.position) < 0;
            if (visible && Physics.Linecast(eye, transform.position, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                visible = hit.distance >= Vector3.Distance(eye, transform.position) - .1f;
            label.enabled = visible;
        }
    }
}

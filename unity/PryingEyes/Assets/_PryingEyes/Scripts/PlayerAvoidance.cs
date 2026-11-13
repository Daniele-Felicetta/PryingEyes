using UnityEngine;
using UnityEngine.AI;

namespace PryingEyes
{
    /// <summary>Il corpo del giocatore per l'aggiramento dei nemici: senza questo gli agenti si spingono tra loro
    /// dentro il CharacterController, che poi espelle il giocatore anche oltre un parapetto. Non modifica la navmesh.</summary>
    public sealed class PlayerAvoidance : MonoBehaviour
    {
        ITarget target;
        NavMeshObstacle obstacle;

        public static void Bind(GameObject body, ITarget target)
        {
            var avoidance = body.AddComponent<PlayerAvoidance>();
            avoidance.target = target;
            avoidance.obstacle = body.AddComponent<NavMeshObstacle>();
            avoidance.obstacle.shape = NavMeshObstacleShape.Capsule;
            avoidance.obstacle.radius = 0.44f; // capsula del giocatore (0,38 m) piu' margine di contatto
            avoidance.obstacle.carving = false;
            avoidance.obstacle.enabled = false;
        }

        void LateUpdate()
        {
            bool active = target != null && target.Alive;
            obstacle.enabled = active;
            if (!active) return;
            float height = target.BodyHeight;
            obstacle.height = height;
            obstacle.center = Vector3.up * (height * 0.5f);
        }

        void OnDisable() { if (obstacle != null) obstacle.enabled = false; }
    }
}

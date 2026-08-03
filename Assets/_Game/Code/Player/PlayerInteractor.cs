using _Game.Code.Doors;
using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// Turns the player's Interact press into a request against whatever they are
    /// looking at. Doors today; block 3 adds the grab / release branch here, since
    /// Interact is one button for everything.
    /// Reach comes from MECHANICS.md section 2.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private Transform lookSource;
        [SerializeField] private PlayerHands hands;

        // World metres, and it stays world metres even though the prefab root is
        // scaled 0.1: Physics.Raycast works in world space and lookSource hands out
        // a world position and direction. See the local-vs-world table in
        // MECHANICS.md section 2.
        [Header("Reach, m (MECHANICS.md section 2)")]
        [SerializeField] private float reach = 1.5f;

        // Door + BlockedArea. BlockedArea is in the mask so that a door cannot be
        // used through a wall — the ray stops on the frame first.
        [SerializeField] private LayerMask blockers;

        private PlayerController _controller;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (!_controller.InteractPressedThisFrame)
            {
                return;
            }

            // Hands full: the player is carrying an animal and has nothing free to
            // work a door with (MECHANICS.md 3.4).
            if (hands != null && !hands.IsEmpty)
            {
                return;
            }

            if (lookSource == null)
            {
                return;
            }

            RaycastHit hit;
            if (!Physics.Raycast(lookSource.position, lookSource.forward, out hit, reach, blockers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            var door = hit.collider.GetComponent<Door>();
            if (door == null)
            {
                return;
            }

            door.Use(transform);
        }
    }
}

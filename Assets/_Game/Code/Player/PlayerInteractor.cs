using _Game.Code.Doors;
using _Game.Code.Pets;
using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// Turns the player's Interact press into a request against whatever is in
    /// front of them: an animal to pick up or put down, or a door to work.
    /// Distances come from MECHANICS.md section 2.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private Transform lookSource;
        [SerializeField] private PlayerHands hands;

        // World metres, and they stay world metres even though the prefab root is
        // scaled 0.1: the physics queries work in world space and lookSource hands
        // out a world position and direction. See the local-vs-world table in
        // MECHANICS.md section 2.
        [Header("Distances, m (MECHANICS.md section 2)")]
        [SerializeField] private float reach = 1.5f;

        // Search wide, let the animal authorise narrow: the real capture distance
        // lives on Pet, which is what the host will re-check. Keep this at or above
        // it, or animals inside their own capture distance never get found.
        [SerializeField] private float petSearchRadius = 2f;

        // Door + BlockedArea. BlockedArea is in the mask so that a door cannot be
        // used through a wall — the ray stops on the frame first. The same mask
        // stops an animal from being grabbed through a wall.
        [SerializeField] private LayerMask blockers;

        private PlayerController _controller;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (!_controller.InteractPressedThisFrame || hands == null || lookSource == null)
            {
                return;
            }

            // Hands full: the press puts the animal down, and nothing else is
            // available while carrying — MECHANICS.md 3.4.
            if (!hands.IsEmpty)
            {
                hands.Carried.Release();
                return;
            }

            // An animal beats a door on purpose: the animal is running away, the
            // door will wait.
            var pet = FindPet();
            if (pet != null)
            {
                pet.TryTake(hands);
                return;
            }

            if (!Physics.Raycast(lookSource.position, lookSource.forward, out var hit, reach, blockers, QueryTriggerInteraction.Ignore))
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

        /// <summary>
        /// Nearest animal within the capture distance that is in front of the player
        /// and not behind a wall. By radius rather than by a ray: MECHANICS.md sizes
        /// the capture as a distance, and a fleeing Parrot is 17 cm of target.
        /// </summary>
        private Pet FindPet()
        {
            // The allocating overload on purpose. The NonAlloc one needs a fixed
            // buffer, and measured 2026-08-03 a furnished room puts 26-29 colliders
            // inside this radius: with a 16-slot buffer the animal itself landed at
            // index 23-27 and was never seen. This query runs on a button press, not
            // per frame, so one small array is the cheaper mistake.
            var nearby = Physics.OverlapSphere(transform.position, petSearchRadius, ~0,
                QueryTriggerInteraction.Ignore);

            Pet best = null;
            var bestDistance = float.MaxValue;
            var eye = lookSource.position;

            foreach (var collider in nearby)
            {
                var pet = collider.GetComponentInParent<Pet>();
                if (pet == null || !pet.CanBeTakenBy(hands))
                {
                    continue;
                }

                var toPet = pet.transform.position - transform.position;
                if (Vector3.Dot(toPet, lookSource.forward) <= 0f)
                {
                    continue;
                }

                var distance = toPet.sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                // Aim at the middle of the body. Aiming near the feet made the line
                // graze the furniture the animal stands next to — a Kitty by the bed
                // was unreachable because a blanket clipped the line.
                if (Physics.Linecast(eye, PetAimPoint(pet), blockers, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                best = pet;
                bestDistance = distance;
            }

            return best;
        }

        private static Vector3 PetAimPoint(Pet pet)
        {
            var controller = pet.GetComponent<CharacterController>();
            if (controller == null)
            {
                return pet.transform.position;
            }

            // center is in the animal's local space, so it scales with the transform
            // exactly like height and radius do (see MECHANICS.md section 2).
            return pet.transform.TransformPoint(controller.center);
        }
    }
}

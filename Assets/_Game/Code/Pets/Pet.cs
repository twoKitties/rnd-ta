using _Game.Code.Player;
using UnityEngine;

namespace _Game.Code.Pets
{
    /// <summary>How an animal rides while it is being carried.</summary>
    public enum CarryPose
    {
        /// <summary>Held overhead. For a Dog, which is longer than the alien is tall.</summary>
        Overhead,

        /// <summary>Held in front, in the arms. For a Kitty or a Parrot.</summary>
        InFront
    }

    /// <summary>
    /// An animal the players came to steal. The three species differ by numbers
    /// only (MECHANICS.md section 4), so everything species-specific lives in the
    /// serialized fields of this component on Dog / Kitty / Parrot prefabs.
    ///
    /// This component also owns the carrier slot from MECHANICS.md 3.3: one carrier
    /// per animal, and this class is where that is enforced. Two players pressing
    /// Interact in the same frame both arrive here, and the second one is refused.
    ///
    /// Split for the netcode pass (MECHANICS.md 7.4): <see cref="CanBeTakenBy"/> is
    /// the rule, <see cref="TryTake"/> and <see cref="Release"/> are the authority's
    /// decision, and the private Apply* methods are the state change. Tomorrow the
    /// client asks, the host runs TryTake, and every peer runs Apply* off replicated
    /// state — none of the three needs rewriting, only wiring.
    /// </summary>
    public class Pet : MonoBehaviour
    {
        [Header("Carrying (MECHANICS.md section 2)")]
        [SerializeField] private float carrySpeedMultiplier = 1f;

        [SerializeField] private CarryPose carryPose = CarryPose.InFront;

        [Tooltip("How close the carrier must be. Checked here rather than in the " +
                 "interactor so the authority can re-check it, not just the asking client.")]
        [SerializeField] private float captureDistance = 1.5f;

        [Tooltip("Where the animal lands when released, metres in front of the carrier.")]
        [SerializeField] private float dropDistance = 0.6f;

        // Floor and walls, for finding ground under the drop spot.
        [SerializeField] private LayerMask groundMask;

        /// <summary>How much the carrier slows down while holding this one.</summary>
        public float CarrySpeedMultiplier => carrySpeedMultiplier;

        /// <summary>The hands holding this animal, or null. One carrier at a time.</summary>
        public PlayerHands Carrier { get; private set; }

        private CharacterController _controller;

        // Kept apart from the Carrier reference on purpose: if the carrier is
        // destroyed — shot by Old Man (3.7), or gone from the session — the
        // reference goes fake-null while the animal is still mid-air with its
        // controller off. This flag is what notices and puts it down.
        private bool _isCarried;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        /// <summary>
        /// The rule: is this animal free, are those hands free, and are they close
        /// enough. Pure — it changes nothing, so the host can ask it about a request
        /// that arrived over the wire.
        /// </summary>
        public bool CanBeTakenBy(PlayerHands hands)
        {
            if (hands == null || Carrier != null || !hands.IsEmpty)
            {
                return false;
            }

            return Vector3.Distance(hands.transform.position, transform.position) <= captureDistance;
        }

        /// <summary>Picks the animal up if the rule allows it.</summary>
        public bool TryTake(PlayerHands hands)
        {
            if (!CanBeTakenBy(hands))
            {
                return false;
            }

            ApplyCarry(hands);
            return true;
        }

        /// <summary>
        /// Handed over to the saucer: off the level and counted (MECHANICS.md 4.5).
        /// Lives here rather than in LevelGoal so that block 4's NavMeshAgent gets
        /// switched off in the same place everything else about this animal is.
        /// </summary>
        public void Deliver()
        {
            gameObject.SetActive(false);
        }

        /// <summary>Puts the animal back on the floor and frees both slots.</summary>
        public void Release()
        {
            if (!_isCarried)
            {
                return;
            }

            // Carrier == null here means it was destroyed while carrying; the animal
            // then drops where it is rather than at a carrier that no longer exists.
            var where = Carrier == null ? transform.position : FindDropPosition(Carrier.transform);
            ApplyRelease(where);
        }

        // After the carrier has already moved this frame, so the load does not lag a
        // frame behind and shiver.
        private void LateUpdate()
        {
            if (!_isCarried)
            {
                return;
            }

            if (Carrier == null)
            {
                Release();
                return;
            }

            var anchor = Carrier.AnchorFor(carryPose);
            if (anchor == null)
            {
                return;
            }

            transform.position = anchor.position;
            transform.rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);
        }

        // The state change itself. This is the line a netcode pass will drive from
        // replicated state so that every peer shows the same thing.
        private void ApplyCarry(PlayerHands hands)
        {
            Carrier = hands;
            _isCarried = true;
            hands.Take(this);

            // The controller and the carry both want to drive the transform; leaving
            // it on makes the animal jitter or refuse to move at all. The NavMeshAgent
            // of block 4 will have to be switched off in exactly the same place.
            if (_controller != null)
            {
                _controller.enabled = false;
            }
        }

        private void ApplyRelease(Vector3 position)
        {
            var carrier = Carrier;
            Carrier = null;
            _isCarried = false;

            if (carrier != null)
            {
                carrier.Clear();
            }

            transform.position = position;

            if (_controller != null)
            {
                // Re-enabled after the move: an enabled CharacterController caches its
                // own position and would drag the animal back.
                _controller.enabled = true;
            }
        }

        private Vector3 FindDropPosition(Transform carrier)
        {
            var ahead = carrier.position + carrier.forward * dropDistance;

            // Drop spots are chosen from above: the carrier may be standing on the
            // porch step or in a doorway, and the floor under their feet is not
            // necessarily the floor half a metre ahead.
            var from = ahead + Vector3.up;
            if (Physics.Raycast(from, Vector3.down, out var hit, 3f, groundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            // No floor ahead (a doorway onto the yard, a hole): drop at the carrier's
            // own feet, which are on solid ground by definition.
            return carrier.position;
        }
    }
}

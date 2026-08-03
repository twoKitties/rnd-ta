using _Game.Code.Player;
using UnityEngine;
using UnityEngine.AI;

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

        [Tooltip("What the animal must not be pushed or dropped through: BlockedArea + Door.")]
        [SerializeField] private LayerMask obstacleMask;

        /// <summary>How much the carrier slows down while holding this one.</summary>
        public float CarrySpeedMultiplier => carrySpeedMultiplier;

        /// <summary>Where this one rides. The carrier's animator matches its pose to it.</summary>
        public CarryPose Pose => carryPose;

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

            transform.position = ClampedCarryPosition(anchor);
            transform.rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);
        }

        /// <summary>
        /// Where the load can actually ride this frame. The anchor sits 0.4 m in front
        /// of the carrier, so walking into a wall would otherwise hold the animal a
        /// quarter of a metre inside it; the same check keeps an overhead Dog out of
        /// low doorways. The load slides back towards its carrier instead.
        /// </summary>
        private Vector3 ClampedCarryPosition(Transform anchor)
        {
            var radius = BodyRadius();
            var centre = BodyCentreHeight();

            // Cast from inside the carrier's own capsule: that point is free by
            // definition, which a point on the floor is not.
            var from = Carrier.transform.position + Vector3.up * centre;
            var to = anchor.position + Vector3.up * centre;
            var travel = to - from;
            var distance = travel.magnitude;
            if (distance < 0.0001f)
            {
                return anchor.position;
            }

            // A line, not a sphere cast: a sphere the size of a Kitty already overlaps
            // the wall when the carrier stands against it, and an overlapping sphere
            // cast reports no hit at all — measured, and it is exactly how the animal
            // ended up on the far side. The carrier's own middle is never inside a
            // wall, so a line from there is reliable; the body radius is subtracted
            // afterwards to keep the animal clear of the surface.
            var direction = travel / distance;
            if (Physics.Linecast(from, to, out var hit, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                var stop = Mathf.Max(0f, hit.distance - radius);
                return from + direction * stop - Vector3.up * centre;
            }

            return anchor.position;
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

        /// <summary>
        /// Where the animal may be put down. Dropping it blindly a fixed distance
        /// ahead would push it straight through a wall the carrier is facing — and an
        /// animal left in a room nobody can reach makes the raid unwinnable, since the
        /// win needs all three aboard. So the spot is walked to, not assumed.
        /// </summary>
        private Vector3 FindDropPosition(Transform carrier)
        {
            var radius = BodyRadius();
            var centre = BodyCentreHeight();
            var from = carrier.position + Vector3.up * centre;

            // 1. how far ahead can this body actually travel before something stops it.
            // A line rather than a sphere cast, for the reason spelled out in
            // ClampedCarryPosition: an overlapping sphere cast reports nothing.
            var distance = dropDistance;
            if (Physics.Linecast(from, from + carrier.forward * dropDistance, out var blocker, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                distance = Mathf.Max(0f, blocker.distance - radius - 0.02f);
            }

            var ahead = from + carrier.forward * distance;

            // 2. the floor under that spot, found from above: the carrier may be on a
            // step or in a doorway, and the floor under their feet is not necessarily
            // the floor half a metre ahead.
            if (!Physics.Raycast(ahead + Vector3.up, Vector3.down, out var ground, 3f + centre, groundMask, QueryTriggerInteraction.Ignore))
            {
                return carrier.position;
            }

            var spot = ground.point;

            // 3. is the body actually free there — the sphere cast only checked the
            // path, not whether the end of it is inside something.
            if (Physics.CheckSphere(spot + Vector3.up * centre, radius * 0.95f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                return carrier.position;
            }

            // 4. last guard: it must be somewhere an animal could later walk out of.
            // Block 4 puts these on NavMeshAgents, so "not on the navmesh" means "lost".
            if (!NavMesh.SamplePosition(spot, out var navHit, 0.5f, NavMesh.AllAreas))
            {
                return carrier.position;
            }

            // Snap back down: the navmesh floats a couple of centimetres over the floor.
            return Physics.Raycast(navHit.position + Vector3.up * 0.5f, Vector3.down, out var settle, 1.5f, groundMask, QueryTriggerInteraction.Ignore)
                ? settle.point
                : spot;
        }

        /// <summary>The animal's own radius in world metres — no new tunable needed.</summary>
        private float BodyRadius()
        {
            return _controller == null ? 0.1f : _controller.radius * Mathf.Abs(transform.lossyScale.x);
        }

        /// <summary>Height of the body's middle above its feet, in world metres.</summary>
        private float BodyCentreHeight()
        {
            return _controller == null ? 0.1f : _controller.center.y * Mathf.Abs(transform.lossyScale.y);
        }
    }
}

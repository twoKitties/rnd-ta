using _Game.Code.Player;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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
    public class Pet : NetworkBehaviour
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

        /// <summary>
        /// Where somebody should aim a line at this animal: the middle of its body,
        /// not its feet. Asked of the animal rather than worked out by the caller,
        /// because the body is this class's own knowledge — the same
        /// <see cref="BodyCentreHeight"/> the two wall checks use.
        ///
        /// A ray at floor level is stopped by the first skirting board or blanket,
        /// which is exactly how a Kitty standing by the bed became impossible to pick
        /// up.
        /// </summary>
        public Vector3 AimPoint => transform.position + Vector3.up * BodyCentreHeight();

        // The CharacterController the asset pack shipped was removed in block 4:
        // it and the agent both claim the transform, and together they give jitter or
        // total stillness (MECHANICS.md 4.1). The capsule stays behind as a plain
        // collider — it is what puts the animal on the Pet layer for the interactor's
        // search, and what stops a player from walking through it.
        private NavMeshAgent _agent;
        private CapsuleCollider _capsule;

        // Asked through the NetworkObject rather than through NetworkBehaviour's own
        // IsSpawned: that property throws when the behaviour was never initialised,
        // which is exactly the case for an animal in a level played with no networking
        // (measured 2026-08-05 on LevelGoal).
        private NetworkObject _nob;

        private bool IsReplicated => _nob != null && _nob.IsSpawned;

        /// <summary>Where the decisions are taken: the server, or us when alone.</summary>
        private bool IsAuthority => !IsReplicated || _nob.IsServerInitialized;

        // Who is carrying this animal, as shared state. A reference to the carrier's
        // networked avatar, because a component reference means nothing on another
        // machine. Every peer applies the change below, which is what makes the animal
        // ride the right pair of hands on all four screens.
        private readonly SyncVar<NetworkObject> _carrierObject = new SyncVar<NetworkObject>();

        // Aboard the saucer. Its own value rather than a side effect of the carrier
        // being cleared, because releasing an animal and handing it over are different
        // things and only one of them takes it out of the level.
        private readonly SyncVar<bool> _delivered = new SyncVar<bool>();

        // Presentation of being carried lives here rather than in PetBrain, because
        // the brain is the animal's decision-making and will run on one machine only,
        // while being carried has to look and sound the same on all of them.
        private Animator _animator;
        private PetVoice _voice;

        /// <summary>
        /// The two floats of the vendor controller: whether the legs move, and how
        /// fast. Owned here, next to the code that stops them, and read by PetBrain
        /// while the animal is on the ground.
        /// </summary>
        internal static readonly int VertParameter = Animator.StringToHash("Vert");

        internal static readonly int StateParameter = Animator.StringToHash("State");

        // How far a failed Warp may look for legal ground, world metres. Not a
        // tunable: it is a rescue for a case that should not happen, and a wider
        // search would teleport the animal somewhere the player did not put it.
        private const float RescueRadius = 1f;

        // How far the drop spot may be off the navmesh and still be accepted, world
        // metres. Deliberately stricter than the animals' own tunable of the same
        // shape (MECHANICS.md section 2, "радиус приёма точки навмеша"): that one
        // picks where an animal runs to, this one decides where a player is allowed
        // to leave it, and a metre of slack there is a metre the animal was never put
        // down at. Not a tunable, for the same reason RescueRadius is not.
        private const float DropSampleRadius = 0.5f;

        // Kept apart from the Carrier reference on purpose: if the carrier is
        // destroyed — shot by Old Man (3.7), or gone from the session — the
        // reference goes fake-null while the animal is still mid-air with its
        // agent off. This flag is what notices and puts it down.
        private bool _isCarried;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _capsule = GetComponent<CapsuleCollider>();
            _animator = GetComponent<Animator>();
            _voice = GetComponent<PetVoice>();
            _nob = GetComponent<NetworkObject>();

            _carrierObject.OnChange += OnCarrierChanged;
            _delivered.OnChange += OnDeliveredChanged;
        }

        private void OnDestroy()
        {
            _carrierObject.OnChange -= OnCarrierChanged;
            _delivered.OnChange -= OnDeliveredChanged;
        }

        // The one place the change is applied, on every peer. The authority writes the
        // reference; everybody — including the authority — reacts here, so the picture
        // is produced by exactly one path (MECHANICS.md 7.4).
        private void OnCarrierChanged(NetworkObject previous, NetworkObject next, bool asServer)
        {
            if (next == null)
            {
                if (_isCarried)
                {
                    ApplyRelease(FindDropPosition(Carrier == null ? transform : Carrier.transform));
                }

                return;
            }

            var hands = next.GetComponent<PlayerHands>();
            if (hands != null)
            {
                ApplyCarry(hands);
            }
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

        /// <summary>
        /// Picks the animal up if the rule allows it. On a client this is a request:
        /// the host re-runs <see cref="CanBeTakenBy"/> over it, because two machines
        /// disagree about a 1.5 m distance often enough to matter and only one answer
        /// may win (MECHANICS.md 7.4).
        /// </summary>
        public bool TryTake(PlayerHands hands)
        {
            if (!CanBeTakenBy(hands))
            {
                return false;
            }

            if (IsAuthority)
            {
                Take(hands);
                return true;
            }

            var asker = hands.GetComponent<NetworkObject>();
            if (asker == null)
            {
                return false;
            }

            RequestTake(asker);
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestTake(NetworkObject asker, NetworkConnection sender = null)
        {
            // The avatar has to belong to whoever asked: a client may pick an animal up
            // with its own hands and nobody else's.
            if (asker == null || sender == null || asker.Owner != sender)
            {
                return;
            }

            var hands = asker.GetComponent<PlayerHands>();
            if (hands != null && CanBeTakenBy(hands))
            {
                Take(hands);
            }
        }

        // The decision, taken once. Off the network it applies straight away; on it,
        // the replicated reference is what carries it to everybody, this peer included.
        private void Take(PlayerHands hands)
        {
            if (!IsReplicated)
            {
                ApplyCarry(hands);
                return;
            }

            var carrier = hands.GetComponent<NetworkObject>();
            if (carrier != null)
            {
                _carrierObject.Value = carrier;
            }
        }

        /// <summary>
        /// Handed over to the saucer: off the level and counted (MECHANICS.md 4.5).
        /// Lives here rather than in LevelGoal so that the NavMeshAgent gets switched
        /// off in the same place everything else about this animal is.
        ///
        /// Decided by the authority and carried to everybody by the flag below. Being
        /// aboard is not something a peer can work out for itself: the rule is about
        /// where the *carrier* stood, and only one machine judges that.
        /// </summary>
        public void Deliver()
        {
            if (!IsReplicated)
            {
                ApplyDeliver();
                return;
            }

            if (IsAuthority)
            {
                // Every peer, this one included, reacts in OnDeliveredChanged.
                _delivered.Value = true;
            }
        }

        // The state change: the animal is gone from the level, on every screen.
        private void ApplyDeliver()
        {
            if (_agent != null)
            {
                // Before the object goes inactive, not after: an agent switched off
                // while already disabled never unregisters from the crowd simulation.
                _agent.enabled = false;
            }

            gameObject.SetActive(false);
        }

        private void OnDeliveredChanged(bool previous, bool next, bool asServer)
        {
            if (next)
            {
                ApplyDeliver();
            }
        }

        /// <summary>Puts the animal back on the floor and frees both slots.</summary>
        public void Release()
        {
            if (!_isCarried)
            {
                return;
            }

            if (!IsReplicated)
            {
                // Carrier == null here means it was destroyed while carrying; the animal
                // then drops where it is rather than at a carrier that no longer exists.
                var where = Carrier == null ? transform.position : FindDropPosition(Carrier.transform);
                ApplyRelease(where);
                return;
            }

            if (IsAuthority)
            {
                // Clearing the reference is the whole of it: every peer, this one
                // included, puts the animal down in OnCarrierChanged.
                _carrierObject.Value = null;
                return;
            }

            RequestRelease();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestRelease(NetworkConnection sender = null)
        {
            // Only the carrier may put it down, and the carrier is what the server has
            // recorded — not what the asking client claims.
            if (sender == null || _carrierObject.Value == null || _carrierObject.Value.Owner != sender)
            {
                return;
            }

            _carrierObject.Value = null;
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

            // Here rather than in PetBrain: a Dog whines all the way to the saucer, and
            // that is a mechanic (MECHANICS.md 4.4), not decoration — every machine has
            // to hear it, including the ones not running the brain.
            if (_voice != null)
            {
                _voice.WhileCarried();
            }
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

        /// <summary>
        /// The state change itself. This is the line a netcode pass will drive from
        /// replicated state so that every peer shows the same thing — which is why it
        /// is callable from outside: the machine that decided is not always the
        /// machine that applies.
        /// </summary>
        public void ApplyCarry(PlayerHands hands)
        {
            if (hands == null || _isCarried)
            {
                return;
            }

            Carrier = hands;
            _isCarried = true;
            hands.Take(this);

            // The legs are stopped once, here, and not every frame: nothing drives the
            // animator while the animal is carried, so the two floats keep whatever the
            // last frame on the ground left in them. An animal grabbed at a run went on
            // running in mid-air over its carrier's head the whole way to the saucer,
            // which in first person is right in view.
            if (_animator != null)
            {
                _animator.SetFloat(VertParameter, 0f);
                _animator.SetFloat(StateParameter, 0f);
            }

            if (_voice != null)
            {
                _voice.Caught();
            }

            // The agent and the carry both want to drive the transform; leaving it on
            // makes the animal jitter or refuse to move at all.
            if (_agent != null)
            {
                _agent.enabled = false;
            }

            // The collider goes with it: the load rides 0.4 m in front of a carrier
            // whose own body is 0.12 m wide, so a live collider there would shove its
            // own carrier around.
            if (_capsule != null)
            {
                _capsule.enabled = false;
            }
        }

        /// <summary>
        /// The other half of the same pair, public for the same reason: the peer that
        /// applies a release is not necessarily the one that decided it.
        /// </summary>
        public void ApplyRelease(Vector3 position)
        {
            var carrier = Carrier;
            Carrier = null;
            _isCarried = false;

            if (carrier != null)
            {
                carrier.Clear();
            }

            transform.position = position;

            if (_capsule != null)
            {
                _capsule.enabled = true;
            }

            // Only where the animal is actually simulated. On a client the agent was
            // switched off by ServerSimulated on purpose — turning it back on here
            // would give that machine an animal walking a path of its own, fighting the
            // position arriving over the wire.
            if (_agent != null && IsAuthority)
            {
                // Re-enabled after the move, and warped rather than just switched on:
                // an agent keeps its own idea of where it is, and would drag the animal
                // back to wherever it was picked up.
                _agent.enabled = true;
                if (!_agent.Warp(position))
                {
                    // Warp reports failure and still moves the transform — measured
                    // 2026-08-03: warping 5 m into the air returned false, moved the
                    // animal there anyway and left isOnNavMesh false, which strands it
                    // for good. FindDropPosition validates its own spot, but the branch
                    // above does not: an animal whose carrier was shot falls wherever it
                    // happened to be. So the nearest navmesh position is the fallback.
                    NavMeshHit navHit;
                    if (NavMesh.SamplePosition(position, out navHit, RescueRadius, NavMesh.AllAreas))
                    {
                        _agent.Warp(navHit.position);
                    }
                    else
                    {
                        // Not even a rescue point: the agent goes back off rather than
                        // being left switched on and off the mesh, where it logs an
                        // error a frame and its brain bails out for ever (4.1). A still
                        // animal beats an uncontrollable one.
                        _agent.enabled = false;
                    }
                }
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

            // 3. is the body actually free there — the line above only checked the
            // path, not whether the end of it is inside something.
            if (Physics.CheckSphere(spot + Vector3.up * centre, radius * 0.95f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                return carrier.position;
            }

            // 4. last guard: it must be somewhere an animal could later walk out of.
            // Block 4 puts these on NavMeshAgents, so "not on the navmesh" means "lost".
            if (!NavMesh.SamplePosition(spot, out var navHit, DropSampleRadius, NavMesh.AllAreas))
            {
                return carrier.position;
            }

            // Snap back down: the navmesh floats a couple of centimetres over the floor.
            return Physics.Raycast(navHit.position + Vector3.up * 0.5f, Vector3.down, out var settle, 1.5f, groundMask, QueryTriggerInteraction.Ignore)
                ? settle.point
                : spot;
        }

        /// <summary>
        /// The animal's own radius in world metres — no new tunable needed. Read from
        /// the capsule rather than from the agent: the capsule is the body physics
        /// actually collides with, and it is what these two wall checks are about.
        /// Both values are local on the collider and scale with the transform, which
        /// is what makes the Parrot's 0.3 prefab scale come out right.
        /// </summary>
        private float BodyRadius()
        {
            return _capsule == null ? 0.1f : _capsule.radius * Mathf.Abs(transform.lossyScale.x);
        }

        /// <summary>Height of the body's middle above its feet, in world metres.</summary>
        private float BodyCentreHeight()
        {
            return _capsule == null ? 0.1f : _capsule.center.y * Mathf.Abs(transform.lossyScale.y);
        }
    }
}

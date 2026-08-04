using System.Collections.Generic;
using _Game.Code.AI;
using _Game.Code.Noise;
using UnityEngine;
using UnityEngine.AI;

namespace _Game.Code.Pets
{
    /// <summary>What an animal does when it hears something above its threshold.</summary>
    public enum PetNoiseReaction
    {
        /// <summary>Walks towards it. The Dog — this is what makes noise a lure.</summary>
        Approach,

        /// <summary>Stands still for a moment. The Kitty.</summary>
        Freeze,

        /// <summary>Bolts. The Parrot, which is frightened of everything.</summary>
        Flee
    }

    /// <summary>Which of its states an animal is in. Read by the animator and by tests.</summary>
    public enum PetState
    {
        Idle,
        Alert,
        Flee,
        Cornered,
        Freeze,
        Approach,
        Carried
    }

    /// <summary>
    /// The animals' whole behaviour (MECHANICS.md 4). One component for all three
    /// species, because the doc requires that they "differ by numbers only, not by
    /// code" — Dog, Kitty and Parrot are this class with different serialized values.
    ///
    /// The shape of the rule, in the order it is decided every frame:
    ///
    /// 1. Being carried beats everything; the agent is off and this class waits.
    /// 2. Just dropped: 30 s of distrust of every player, and it bolts.
    /// 3. Seen player inside the panic radius, and it is afraid of them → flee.
    ///    Afraid means: it always is (the Parrot), or it distrusts, or the player is
    ///    moving upright. A player who sneaks or stands still and is trusted may walk
    ///    right up to it — that is the entire luring mechanic.
    /// 4. Seen player further off → alert: it turns and watches.
    /// 5. Heard something ≥ its threshold → the species' reaction. Sight always beats
    ///    noise (4.1, 4.3), which is why this is below both checks above.
    /// 6. Nothing for the calm-down time → idle.
    ///
    /// Across all of that: a seen player sprinting straight at the animal makes it
    /// give voice (4.4), whether it is standing its ground or already running. The
    /// bark is noise like a step, so charging an animal is what brings Old Man —
    /// walking up to the same animal is not.
    ///
    /// Sight is directional on purpose: an animal has a cone, so a player can come up
    /// behind it at any speed. That is the counterpart of never being able to
    /// approach a Parrot from the front.
    ///
    /// A shut door is a wall to an animal (4.6): the navmesh runs through every
    /// doorway, so a flee target behind a closed door is rejected here rather than
    /// being trusted to the agent, which would walk straight through the leaf.
    /// </summary>
    [RequireComponent(typeof(Pet))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class PetBrain : MonoBehaviour
    {
        [Header("Senses (MECHANICS.md section 2)")]
        [Tooltip("How far it notices a player. Larger than the panic radius: it warns before it bolts.")]
        [SerializeField] private float sightRange = 8f;

        [Tooltip("Full cone, degrees. Behind it, a player is invisible at any speed.")]
        [SerializeField] private float sightCone = 120f;

        [Tooltip("Inside this, a player it is afraid of makes it run.")]
        [SerializeField] private float panicRadius = 6f;

        [Tooltip("Noise at or above this is reacted to. The animals' threshold is deliberately above a step.")]
        [SerializeField] private float hearingThreshold = 30f;

        [Tooltip("Full angle of the wedge a sprint counts as a charge in. A player running " +
                 "at the animal inside it gets barked at (MECHANICS.md 4.4).")]
        [SerializeField] private float chargeCone = 60f;

        [Header("Speeds, m/s (MECHANICS.md section 2)")]
        [SerializeField] private float fleeSpeed = 3.2f;

        [Tooltip("Half the flee speed: it goes to a noise carefully and runs away fast.")]
        [SerializeField] private float approachSpeed = 1.6f;

        [Header("Timers, s (MECHANICS.md section 2)")]
        [SerializeField] private float calmTime = 3f;
        [SerializeField] private float freezeTime = 2f;

        [Tooltip("After being dropped it distrusts every player for this long — crouching stops working.")]
        [SerializeField] private float distrustTime = 30f;

        [Header("Species")]
        [Tooltip("The Parrot: never lets anyone near, crouching or not. It is caught by cornering.")]
        [SerializeField] private bool alwaysAfraid;

        [SerializeField] private PetNoiseReaction noiseReaction = PetNoiseReaction.Approach;

        [Header("Fleeing (MECHANICS.md section 2)")]
        [Tooltip("Nowhere better than this to run to means it is cornered: it freezes and can be picked up.")]
        [SerializeField] private float corneredDistance = 1.5f;

        [Tooltip("Candidate directions tried per repath, fanned out behind it.")]
        [SerializeField] private int fleeFanCount = 7;

        [Tooltip("Degrees between neighbouring candidates.")]
        [SerializeField] private float fleeFanSpread = 25f;

        [Tooltip("Shorter and shorter hops tried along each direction before giving up on it.")]
        [SerializeField] private int fleeHopSteps = 3;

        [Tooltip("Seconds between recalculating where to run.")]
        [SerializeField] private float repathInterval = 0.5f;

        [Tooltip("How far from a candidate point a navmesh position is still accepted, m.")]
        [SerializeField] private float navSampleRadius = 1f;

        [Tooltip("Height above the floor the path is swept at when looking for shut doors, m.")]
        [SerializeField] private float pathProbeHeight = 0.5f;

        [Header("Masks")]
        [Tooltip("What breaks its line of sight: BlockedArea + Door.")]
        [SerializeField] private LayerMask blockers;

        [Tooltip("Just the Door layer.")]
        [SerializeField] private LayerMask doorMask;

        // Hashes, not state: nothing per-animal lives here (MECHANICS.md 7.3). The two
        // floats are the whole interface of the pack's animator controllers — Vert is
        // 0 idle / 1 moving, State is 0 walk / 1 run.
        private static readonly int VertParameter = Animator.StringToHash("Vert");
        private static readonly int StateParameter = Animator.StringToHash("State");

        private Pet _pet;
        private NavMeshAgent _agent;
        private Animator _animator;
        private PetVoice _voice;
        private NoiseEmitter _ownNoise;

        private IReadOnlyList<SensedPlayer> _players;
        private IReadOnlyList<NoiseEmitter> _noiseSources;

        private NavMeshPath _path;
        private readonly RaycastHit[] _doorHits = new RaycastHit[4];

        private float _distrustUntil;
        private float _calmFor;
        private float _stateTimer;
        private float _repathAt;
        private bool _wasCarried;
        private Vector3 _noiseSpot;

        // Who it backed away from, kept so a cornered animal can go on turning to face
        // them between two repaths.
        private Vector3 _corneredFrom;

        /// <summary>What it is doing. Read by measurements and by the animator.</summary>
        public PetState State { get; private set; } = PetState.Idle;

        /// <summary>
        /// True while it refuses to be approached even by someone crouching. Set by
        /// being dropped, and it applies to every player at once.
        /// </summary>
        public bool Distrusts => Time.time < _distrustUntil;

        private void Awake()
        {
            _pet = GetComponent<Pet>();
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponent<Animator>();
            _voice = GetComponent<PetVoice>();
            _ownNoise = GetComponent<NoiseEmitter>();
            _path = new NavMeshPath();
        }

        /// <summary>
        /// Handed the world by Bootstrapper right after this animal is spawned: the
        /// players and the noise sources are other spawned actors, so a prefab cannot
        /// reference them up front (MECHANICS.md 7.6).
        /// </summary>
        public void Bind(IReadOnlyList<SensedPlayer> players, IReadOnlyList<NoiseEmitter> noiseSources)
        {
            _players = players;
            _noiseSources = noiseSources;
        }

        private void Update()
        {
            // Carried: Pet has switched the agent off, and nothing here may fight it.
            // The voice still runs — a Dog whines all the way to the saucer.
            if (_pet.Carrier != null)
            {
                if (!_wasCarried)
                {
                    if (_voice != null)
                    {
                        _voice.Caught();
                    }

                    // The legs are stopped once, here, and not every frame: DriveAnimator
                    // never runs while the animal is carried, so the two floats keep
                    // whatever the last frame on the ground left in them. An animal
                    // grabbed at a run went on running in mid-air over its carrier's head
                    // the whole way to the saucer, which in first person is right in view.
                    if (_animator != null)
                    {
                        _animator.SetFloat(VertParameter, 0f);
                        _animator.SetFloat(StateParameter, 0f);
                    }
                }

                _wasCarried = true;
                State = PetState.Carried;

                if (_voice != null)
                {
                    _voice.WhileCarried();
                }

                return;
            }

            if (_wasCarried)
            {
                // Dropped, or the carrier was shot out from under it. Either way it has
                // just been in somebody's hands and it wants nothing to do with anyone.
                _wasCarried = false;
                _distrustUntil = Time.time + distrustTime;
                EnterFlee();
            }

            // A freshly released animal spends a frame off the navmesh while Pet warps
            // it; asking an agent that is not on the mesh for a path logs an error.
            if (!_agent.enabled || !_agent.isOnNavMesh)
            {
                return;
            }

            Think();
            DriveAnimator();
        }

        private void Think()
        {
            var threat = NearestSeenPlayer();

            // Someone running straight at it: it gives voice, in whatever state it is
            // in (4.4). PetVoice paces the repeats with its own cooldown, and an animal
            // with no clip in the slot stays completely silent.
            if (threat != null && _voice != null && IsChargedBy(threat))
            {
                _voice.Noticed();
            }

            CheckDoorAhead();

            var afraid = threat != null && IsAfraidOf(threat) &&
                         HorizontalDistance(threat.Transform.position) <= panicRadius;

            if (afraid)
            {
                if (State != PetState.Flee && State != PetState.Cornered)
                {
                    EnterFlee();
                }

                RunAway(threat);
                return;
            }

            // Still running from someone it can no longer see: it keeps going until
            // nobody has been inside the panic radius for the calm-down time. Distance,
            // not sight — an animal that turned a corner has not forgotten.
            if (State == PetState.Flee || State == PetState.Cornered)
            {
                if (NearestPlayerDistance() > panicRadius)
                {
                    _calmFor += Time.deltaTime;
                    if (_calmFor >= calmTime)
                    {
                        EnterIdle();
                    }
                    else
                    {
                        RunAway(null);
                    }
                }
                else
                {
                    _calmFor = 0f;
                    RunAway(null);
                }

                return;
            }

            if (State == PetState.Freeze)
            {
                _stateTimer -= Time.deltaTime;
                if (_stateTimer <= 0f)
                {
                    EnterIdle();
                }

                return;
            }

            // Sight beats noise (4.1): an animal that can see the player watches them
            // rather than walking towards the racket they are making. Out here, past
            // the panic radius, that racket is usually the player's own sprint — the
            // other order had the animal trot towards the very thing it was warning
            // about, which is what this ring exists to warn about.
            if (threat != null)
            {
                EnterAlert(threat);
                return;
            }

            var heard = Hearing.Loudest(_noiseSources, transform.position, hearingThreshold, _ownNoise);
            if (heard != null)
            {
                ReactToNoise(heard.transform.position);
                return;
            }

            if (State == PetState.Approach)
            {
                KeepApproaching();
                return;
            }

            EnterIdle();
        }

        /// <summary>
        /// Somebody sprinting into this animal rather than merely sprinting nearby.
        /// Judged by the player's own velocity and not by the distance closing: a
        /// player running a circle around the animal closes on it for half of every
        /// lap, and would otherwise be barked at for going past.
        /// </summary>
        private bool IsChargedBy(SensedPlayer player)
        {
            if (!player.IsRunning)
            {
                return false;
            }

            var run = player.Velocity;
            if (run.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            var towards = transform.position - player.Transform.position;
            towards.y = 0f;
            if (towards.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            return Vector3.Angle(run, towards) <= chargeCone * 0.5f;
        }

        /// <summary>
        /// A door shut across the route it is already walking. DoorGate is asked when a
        /// route is built and never again, but a player can close a leaf in front of a
        /// running animal — and at flee speed it covers over a metre between two
        /// repaths, straight through the leaf, because the navmesh ignores doors (4.6).
        ///
        /// One probe as far as the next corner is enough: it does not decide anything,
        /// it only brings the repath forward, and the flee fan then rejects the blocked
        /// way itself. A door further along the path is caught by the repath that was
        /// due anyway.
        /// </summary>
        private void CheckDoorAhead()
        {
            if (_agent.isStopped || _agent.pathPending || !_agent.hasPath || Time.time >= _repathAt)
            {
                return;
            }

            var lift = Vector3.up * pathProbeHeight;
            if (DoorGate.FirstClosedDoorBetween(transform.position + lift, _agent.steeringTarget + lift,
                    doorMask, _doorHits) != null)
            {
                _repathAt = 0f;
            }
        }

        private bool IsAfraidOf(SensedPlayer player)
        {
            // The Parrot is afraid of everyone always; everybody else is afraid of a
            // player they distrust, and of one moving upright rather than sneaking.
            return alwaysAfraid || Distrusts || !player.IsQuiet;
        }

        private void ReactToNoise(Vector3 where)
        {
            _noiseSpot = where;

            switch (noiseReaction)
            {
                case PetNoiseReaction.Freeze:
                    if (State != PetState.Freeze)
                    {
                        State = PetState.Freeze;
                        _stateTimer = freezeTime;
                        Stop();
                    }

                    break;

                case PetNoiseReaction.Flee:
                    if (State != PetState.Flee && State != PetState.Cornered)
                    {
                        EnterFlee();
                    }

                    RunAway(null);
                    break;

                default:
                    State = PetState.Approach;
                    KeepApproaching();
                    break;
            }
        }

        /// <summary>
        /// Walks towards the noise, but never closer to a player than the panic radius
        /// (MECHANICS.md 4.3): the pull is reconnaissance — a way to draw an animal out
        /// of a room nobody can reach — and never a way to walk it into someone's arms.
        /// </summary>
        private void KeepApproaching()
        {
            var target = ClampAwayFromPlayers(_noiseSpot);

            _agent.speed = approachSpeed;
            _agent.isStopped = false;

            if (Time.time >= _repathAt)
            {
                _repathAt = Time.time + repathInterval;
                if (!TrySetDestination(target))
                {
                    EnterIdle();
                }
            }

            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
            {
                EnterIdle();
            }
        }

        private Vector3 ClampAwayFromPlayers(Vector3 target)
        {
            if (_players == null)
            {
                return target;
            }

            for (var i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                if (!player.IsAlive)
                {
                    continue;
                }

                var here = player.Transform.position;
                var offset = target - here;
                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance >= panicRadius)
                {
                    continue;
                }

                // Pushed out to the edge of the ring rather than abandoned: the animal
                // still comes as far as it dares.
                var direction = distance < 0.0001f ? (transform.position - here).normalized : offset / distance;
                target = here + direction * panicRadius;
            }

            return target;
        }

        // The voice is not raised here: an animal notices a crouching player too, and
        // barking at one would bring Old Man to the very approach the crouch is meant
        // to buy. Charging it is what it answers, and that is decided in Think.
        private void EnterAlert(SensedPlayer player)
        {
            State = PetState.Alert;
            Stop();
            FaceTowards(player.Transform.position);
        }

        private void EnterIdle()
        {
            State = PetState.Idle;
            _calmFor = 0f;
            Stop();
        }

        private void EnterFlee()
        {
            State = PetState.Flee;
            _calmFor = 0f;
            _repathAt = 0f;
        }

        /// <summary>
        /// Picks somewhere to run and goes there. The threat may be null — an animal
        /// that has lost sight of the player still runs from where it last knew them
        /// to be, which is what the nearest player's position gives.
        /// </summary>
        private void RunAway(SensedPlayer threat)
        {
            _agent.speed = fleeSpeed;

            // Turning stays outside the gate below. FaceTowards is one RotateTowards
            // step of angularSpeed × deltaTime, so running it only on repath frames
            // would turn the animal at a fortieth of its own turn rate: a cornered
            // Parrot would take seconds to come round and face the player it is
            // cowering from, and read as a frozen animation.
            if (State == PetState.Cornered)
            {
                // The live position when there is one: _corneredFrom is only written on
                // repath frames, so following it alone would have the animal turning
                // towards where the player was up to half a second ago and catching up
                // in jerks. It stays as the fallback for the calls that pass no threat.
                FaceTowards(threat == null ? _corneredFrom : threat.Transform.position);
            }

            // Cornered is gated too, and that is the point of naming it here: it is the
            // state in which nothing changes, so leaving it out would run the whole fan
            // — seven directions, each a SamplePosition, a CalculatePath and a door
            // sweep — every single frame for as long as the animal stays in its corner.
            if (Time.time < _repathAt && (State == PetState.Flee || State == PetState.Cornered))
            {
                return;
            }

            _repathAt = Time.time + repathInterval;

            var from = threat == null ? NearestPlayerPosition() : threat.Transform.position;
            Vector3 target;
            if (!TryPickFleeTarget(from, out target))
            {
                // Nowhere to go: it is in a corner or behind a shut door it cannot open.
                // It stops and can be picked up — this is how a Parrot is caught.
                State = PetState.Cornered;
                _corneredFrom = from;
                Stop();
                FaceTowards(from);
                return;
            }

            State = PetState.Flee;
            _agent.isStopped = false;
            _agent.SetDestination(target);
        }

        /// <summary>
        /// Fans candidate points out behind the animal and keeps the one that ends up
        /// furthest from the threat. Rejects anything off the navmesh, unreachable, or
        /// behind a shut door. Returns false when the best it can do is shorter than
        /// the cornered distance.
        ///
        /// Each direction is tried at shrinking distances rather than only at one
        /// panic radius. Measured 2026-08-03 with every door in the house shut: from
        /// where the animals spawn, all 16 compass directions are blocked at 5 m and
        /// beyond, and only 2–4 m is reachable. Probing one ring would therefore have
        /// reported "cornered" for every animal on the first frame of every match, and
        /// the whole raid would have been three frozen pets waiting to be picked up.
        /// An animal runs as far as it can, not exactly one radius.
        /// </summary>
        private bool TryPickFleeTarget(Vector3 threat, out Vector3 target)
        {
            target = transform.position;

            var away = transform.position - threat;
            away.y = 0f;
            away = away.sqrMagnitude < 0.0001f ? transform.forward : away.normalized;

            var found = false;
            var bestScore = float.NegativeInfinity;
            var half = (fleeFanCount - 1) * 0.5f;
            var steps = Mathf.Max(1, fleeHopSteps);

            for (var i = 0; i < fleeFanCount; i++)
            {
                var direction = Quaternion.Euler(0f, (i - half) * fleeFanSpread, 0f) * away;

                for (var step = 0; step < steps; step++)
                {
                    // Longest hop first: one panic radius, then shorter, so an animal
                    // with room to run uses it and one without still moves.
                    var hop = panicRadius * (steps - step) / steps;
                    var probe = transform.position + direction * hop;

                    NavMeshHit navHit;
                    if (!NavMesh.SamplePosition(probe, out navHit, navSampleRadius, NavMesh.AllAreas))
                    {
                        continue;
                    }

                    if (!_agent.CalculatePath(navHit.position, _path) ||
                        _path.status != NavMeshPathStatus.PathComplete)
                    {
                        continue;
                    }

                    if (DoorGate.FirstClosedDoor(_path, pathProbeHeight, doorMask, _doorHits) != null)
                    {
                        continue;
                    }

                    // Distance from the threat decides, with a nudge towards running
                    // straight rather than sideways so it does not weave in an open room.
                    var score = Vector3.Distance(navHit.position, threat) + Vector3.Dot(direction, away);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        target = navHit.position;
                        found = true;
                    }

                    // This direction has given its best; the shorter hops along it can
                    // only score lower.
                    break;
                }
            }

            return found && HorizontalDistance(target) >= corneredDistance;
        }

        private bool TrySetDestination(Vector3 target)
        {
            NavMeshHit navHit;
            if (!NavMesh.SamplePosition(target, out navHit, navSampleRadius, NavMesh.AllAreas))
            {
                return false;
            }

            if (!_agent.CalculatePath(navHit.position, _path) || _path.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            if (DoorGate.FirstClosedDoor(_path, pathProbeHeight, doorMask, _doorHits) != null)
            {
                return false;
            }

            _agent.SetPath(_path);
            return true;
        }

        private void Stop()
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }

        private void FaceTowards(Vector3 point)
        {
            var direction = point - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var wanted = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted,
                _agent.angularSpeed * Time.deltaTime);
        }

        private SensedPlayer NearestSeenPlayer()
        {
            if (_players == null)
            {
                return null;
            }

            SensedPlayer best = null;
            var bestDistance = float.PositiveInfinity;

            for (var i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                if (!player.IsAlive)
                {
                    continue;
                }

                var distance = HorizontalDistance(player.Transform.position);
                if (distance >= bestDistance)
                {
                    continue;
                }

                if (!Sight.CanSee(EyePoint(), transform.forward, player.AimPoint, sightRange, sightCone, blockers))
                {
                    continue;
                }

                best = player;
                bestDistance = distance;
            }

            return best;
        }

        private float NearestPlayerDistance()
        {
            var nearest = float.PositiveInfinity;
            if (_players == null)
            {
                return nearest;
            }

            for (var i = 0; i < _players.Count; i++)
            {
                if (!_players[i].IsAlive)
                {
                    continue;
                }

                var distance = HorizontalDistance(_players[i].Transform.position);
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            return nearest;
        }

        private Vector3 NearestPlayerPosition()
        {
            var nearest = float.PositiveInfinity;
            var where = transform.position - transform.forward;

            if (_players == null)
            {
                return where;
            }

            for (var i = 0; i < _players.Count; i++)
            {
                if (!_players[i].IsAlive)
                {
                    continue;
                }

                var distance = HorizontalDistance(_players[i].Transform.position);
                if (distance < nearest)
                {
                    nearest = distance;
                    where = _players[i].Transform.position;
                }
            }

            return where;
        }

        /// <summary>
        /// Eyes at the middle of the body. The agent's height is in world metres and
        /// already accounts for the Parrot's scaled-down prefab.
        /// </summary>
        private Vector3 EyePoint()
        {
            return transform.position + Vector3.up * (_agent.height * 0.5f);
        }

        private float HorizontalDistance(Vector3 point)
        {
            var offset = point - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        private void DriveAnimator()
        {
            if (_animator == null)
            {
                return;
            }

            var speed = _agent.velocity.magnitude;

            // Vert 0/1 is idle-or-moving, State 0/1 is walk-or-run: the pack's
            // controllers blend on exactly these two floats and nothing else.
            _animator.SetFloat(VertParameter, speed > 0.05f ? 1f : 0f);
            _animator.SetFloat(StateParameter, Mathf.InverseLerp(approachSpeed, fleeSpeed, speed));
        }
    }
}

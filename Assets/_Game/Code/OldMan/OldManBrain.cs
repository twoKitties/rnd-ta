using System.Collections.Generic;
using _Game.Code.AI;
using _Game.Code.Doors;
using _Game.Code.Noise;
using UnityEngine;
using UnityEngine.AI;

namespace _Game.Code.OldMan
{
    /// <summary>Which of his states Old Man is in. Read by measurements and by tests.</summary>
    public enum OldManState
    {
        /// <summary>Walking his round, waiting at each point.</summary>
        Patrol,

        /// <summary>Going to look at a noise he heard.</summary>
        Investigate,

        /// <summary>Standing still, pointed at a player, counting down to the shot.</summary>
        Aim,

        /// <summary>Walking to where he last saw someone, after a shot was cancelled.</summary>
        Search
    }

    /// <summary>
    /// Old Man's whole behaviour (MECHANICS.md 5). A hand-written state machine, for
    /// the reason recorded in section 7: there is no behaviour-tree package in this
    /// project and every leaf would be C# anyway.
    ///
    /// The shape of the rule, in the order it is decided every frame:
    ///
    /// 1. He can see a player → aim. Sight beats everything, including a noise he is
    ///    already walking to (5.2). The delay before the shot is the whole point of
    ///    the mechanic: it is the window in which the player can still fix their
    ///    mistake, which is what makes death read as a mistake rather than bad luck.
    /// 2. The delay ran out and he can still see them → he fires. Since 2026-08-06
    ///    the shot is a pellet flown at a dodgeable speed, and death happens on
    ///    impact rather than on the trigger (5.2) — a sidestep after the bang is
    ///    the second chance the aim delay used to be the only one of.
    /// 3. They broke the line first → the shot is cancelled and he walks to where he
    ///    last saw them, waits, and goes back to his round (5.4).
    /// 4. Otherwise, a noise at or above his threshold → he goes to the loudest one
    ///    and waits there (5.3). A fresh noise re-aims him and refills the wait, so
    ///    he follows a running player rather than arriving where they used to be.
    /// 5. Otherwise, the patrol round (5.1).
    ///
    /// A shut door is a door to him, not a wall (5.5): the same DoorGate call the
    /// animals use to reject a route is what tells him which leaf to pull. That
    /// asymmetry is the whole of "only he and the players open doors".
    ///
    /// The shot itself is not this brain's business: ShotFlash blinks the light and
    /// flies the pellet, RifleAim points the rifle and takes the kick (IK, no clips
    /// — since 2026-08-06). They only read from here; the delay, the decision to
    /// fire and the log stay the mechanic, but the death now belongs to the pellet.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class OldManBrain : MonoBehaviour
    {
        [Header("Sight (MECHANICS.md section 2)")]
        [SerializeField] private float sightRange = 12f;

        [Tooltip("Full cone, degrees. Behind him a player is invisible at any speed.")]
        [SerializeField] private float sightCone = 80f;

        [Tooltip("Eye height above his feet, world metres. His prefab root scale is 1, " +
                 "so this is not converted — unlike anything on the player's 0.1 root.")]
        [SerializeField] private float eyeHeight = 1.6f;

        [Header("Hearing (MECHANICS.md section 2)")]
        [Tooltip("Lower than the animals' 30: a step reaches him and them it does not.")]
        [SerializeField] private float hearingThreshold = 15f;

        [Header("Speeds, m/s (MECHANICS.md section 2)")]
        [SerializeField] private float patrolSpeed = 1.5f;

        [Tooltip("Checking a noise and searching. Faster than the player's walk, slower than their sprint.")]
        [SerializeField] private float alertSpeed = 3f;

        [Header("Timers, s (MECHANICS.md section 2)")]
        [SerializeField] private float patrolWait = 2f;

        [Tooltip("At a noise, and after a cancelled shot. One number, one row in the table.")]
        [SerializeField] private float alertWait = 4f;

        [Tooltip("Between seeing a player and firing. The window the player can still escape in.")]
        [SerializeField] private float aimDelay = 0.4f;

        [Header("Doors (MECHANICS.md 5.5)")]
        [Tooltip("How close he must be to a leaf to pull it. Same reach the player has.")]
        [SerializeField] private float doorReach = 1.5f;

        [Tooltip("Seconds he waits for the leaf to swing clear. A 90 degree swing takes ~0.4 s.")]
        [SerializeField] private float doorSwingTime = 0.5f;

        [Tooltip("Height above the floor the route is swept at when looking for shut doors, m.")]
        [SerializeField] private float pathProbeHeight = 0.5f;

        [Header("Pathing")]
        [SerializeField] private float repathInterval = 0.5f;

        [Tooltip("How far from a target a navmesh position is still accepted, m.")]
        [SerializeField] private float navSampleRadius = 1f;

        [Header("Masks")]
        [Tooltip("What breaks his line of sight: BlockedArea + Door.")]
        [SerializeField] private LayerMask blockers;

        [Tooltip("Just the Door layer.")]
        [SerializeField] private LayerMask doorMask;

        [Header("Shot")]
        [Tooltip("Delivers the shot: the blink, the kick and — since 2026-08-06 — the " +
                 "pellet that carries the kill, so it is no longer optional. A separate " +
                 "component because this brain runs on the server only, and anything " +
                 "shown from here would be seen by nobody else.")]
        [SerializeField] private ShotFlash shotFlash;

        // Hashes, not state: nothing per-actor lives here (MECHANICS.md 7.3). The two
        // bools are the whole interface of the vendor controller — it has no float
        // speed parameter, and every state returns to Idle when its bool goes false,
        // so exactly one of these may be true at a time.
        private static readonly int WalkParameter = Animator.StringToHash("Walk 1");
        private static readonly int RunParameter = Animator.StringToHash("Run 1");

        private NavMeshAgent _agent;
        private Animator _animator;

        private IReadOnlyList<SensedPlayer> _players;
        private IReadOnlyList<NoiseEmitter> _noiseSources;
        private IReadOnlyList<Transform> _patrolPoints;

        private NavMeshPath _path;
        private readonly RaycastHit[] _doorHits = new RaycastHit[4];

        private int _patrolIndex;
        private float _waitLeft;
        private float _aimLeft;
        private float _repathAt;
        private float _doorWaitUntil;

        // Where he is going, and — while aiming or searching — where he last saw a
        // player. The two never overlap: he does not walk towards someone he can see.
        private Vector3 _targetSpot;

        private Door _blockingDoor;
        private Collider _blockingDoorBody;

        // Set by GoTo on a frame it could not build a route at all. Its callers all
        // treat "not on his way" as "arrived", and patrol is the one that has to tell
        // the two apart — an unreachable point is worth a word in the console.
        private bool _routeFailed;

        /// <summary>What he is doing. Read by measurements and by tests.</summary>
        public OldManState State { get; private set; } = OldManState.Patrol;

        /// <summary>
        /// Where he is going — while aiming, the victim's position, refreshed every
        /// frame he can still see them. Read by <see cref="RifleAim"/> to point the
        /// rifle; a read-only window, so the netcode stays out of the brain.
        /// </summary>
        public Vector3 TargetSpot => _targetSpot;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponent<Animator>();
            _path = new NavMeshPath();

        }

        /// <summary>
        /// Handed the world by LevelBootstrapper right after he is spawned: the players,
        /// the noise sources and the patrol points are other spawned actors or scene
        /// objects, so a prefab cannot reference them up front (MECHANICS.md 7.6).
        /// </summary>
        public void Bind(IReadOnlyList<SensedPlayer> players, IReadOnlyList<Transform> patrolPoints,
            IReadOnlyList<NoiseEmitter> noiseSources)
        {
            _players = players;
            _noiseSources = noiseSources;
            _patrolPoints = patrolPoints;

            if (patrolPoints == null || patrolPoints.Count == 0)
            {
                // Loud on purpose, like ActorSpawner: an Old Man who stands still for
                // the whole match reads as broken AI and costs far more to find later.
                Debug.LogError($"{name}: no patrol points, he will not walk his round (MECHANICS.md 5.1).");
                return;
            }

            // Start on the nearest point rather than the first: he is dropped on a
            // random spawn, and marching across the house to point zero would be the
            // first thing every match showed.
            _patrolIndex = NearestPatrolIndex();
        }

        private void Update()
        {
            // An agent that is not on the mesh answers every path query with an error.
            if (!_agent.enabled || !_agent.isOnNavMesh)
            {
                return;
            }

            Think();
            DriveAnimator();
        }

        private void Think()
        {
            // Sight first and unconditionally: a player who walks into view while he
            // is on his way to a noise is seen, and the noise stops mattering (5.2).
            var seen = NearestSeenPlayer();
            if (seen != null)
            {
                if (State != OldManState.Aim)
                {
                    State = OldManState.Aim;
                    _aimLeft = aimDelay;
                }

                _targetSpot = seen.Transform.position;
            }

            switch (State)
            {
                case OldManState.Aim:
                    TickAim(seen);
                    break;

                case OldManState.Search:
                    TickSearch();
                    break;

                case OldManState.Investigate:
                    TickInvestigate();
                    break;

                default:
                    TickPatrol();
                    break;
            }
        }

        private void TickAim(SensedPlayer seen)
        {
            Halt();
            FaceTowards(_targetSpot);

            // Lost during the delay — behind a wall, or out of range. The shot is
            // cancelled (5.2) and he goes to have a look (5.4).
            if (seen == null)
            {
                EnterSearch();
                return;
            }

            _aimLeft -= Time.deltaTime;
            if (_aimLeft > 0f)
            {
                return;
            }

            Shoot();
        }

        private void Shoot()
        {
            // The kill left this method on 2026-08-06: ShotFlash grows a ShotPellet
            // from the muzzle on every peer, and death happens where it lands (5.2).
            // A dodged pellet leaves the victim alive and in view, so the sight
            // check re-aims at them on the very next frame — the aim delay doubles
            // as his rate of fire.
            //
            // Unity object: a destroyed one compares == null but is not a real null.
            if (shotFlash != null)
            {
                shotFlash.Fire();
            }
            else
            {
                // Loud: with the kill riding the pellet, no flash means firing blanks.
                Debug.LogError($"{name}: no ShotFlash wired, the shot can hit nobody (MECHANICS.md 5.2).");
            }

            Debug.Log($"{name} fired (MECHANICS.md 5.2).");

            // Straight back to the round; a player still in view is picked up by the
            // sight check on the very next frame.
            EnterPatrol();
        }

        private void TickSearch()
        {
            if (GoTo(_targetSpot, alertSpeed))
            {
                return;
            }

            Halt();
            FaceTowards(_targetSpot);

            _waitLeft -= Time.deltaTime;
            if (_waitLeft <= 0f)
            {
                EnterPatrol();
            }
        }

        private void TickInvestigate()
        {
            var heard = Loudest();
            if (heard != null)
            {
                // A noise that is still going re-aims him and refills the wait, so he
                // follows a sprinting player instead of walking to where they were.
                _targetSpot = heard.transform.position;
                _waitLeft = alertWait;
            }

            if (GoTo(_targetSpot, alertSpeed))
            {
                return;
            }

            Halt();

            _waitLeft -= Time.deltaTime;
            if (_waitLeft <= 0f)
            {
                EnterPatrol();
            }
        }

        private void TickPatrol()
        {
            var heard = Loudest();
            if (heard != null)
            {
                State = OldManState.Investigate;
                _targetSpot = heard.transform.position;
                _waitLeft = alertWait;
                _repathAt = 0f;
                return;
            }

            if (_patrolPoints == null || _patrolPoints.Count == 0)
            {
                Halt();
                return;
            }

            // Standing at a point, counting out his pause before the next leg.
            if (_waitLeft > 0f)
            {
                Halt();
                _waitLeft -= Time.deltaTime;
                return;
            }

            var point = _patrolPoints[_patrolIndex];

            // Unity object: a destroyed one compares == null but is not a real null.
            if (point == null)
            {
                AdvancePatrol();
                return;
            }

            if (GoTo(point.position, patrolSpeed))
            {
                return;
            }

            if (_routeFailed)
            {
                // Points are placed by hand and reordered by dragging (5.1), so a marker
                // ends up inside a wall or off the navmesh sooner or later. He walks on
                // to the next one either way, but silently skipping it would read as
                // broken AI — the same reason Bind shouts about an empty list.
                Debug.LogWarning(
                    $"{name}: patrol point '{point.name}' cannot be reached — skipped. Move it onto the navmesh.",
                    point);
            }

            _waitLeft = patrolWait;
            AdvancePatrol();
        }

        private void EnterPatrol()
        {
            State = OldManState.Patrol;
            _waitLeft = 0f;
            _repathAt = 0f;
            _blockingDoor = null;
            _blockingDoorBody = null;
        }

        private void EnterSearch()
        {
            State = OldManState.Search;
            _waitLeft = alertWait;
            _repathAt = 0f;
        }

        private void AdvancePatrol()
        {
            _patrolIndex = (_patrolIndex + 1) % _patrolPoints.Count;
            _repathAt = 0f;
        }

        /// <summary>
        /// Walks him towards a point, pulling open any shut door in the way. True
        /// while he is still on his way there, false once he has arrived.
        /// </summary>
        private bool GoTo(Vector3 target, float speed)
        {
            _agent.speed = speed;

            // Standing back while a leaf he has just pulled swings clear.
            if (Time.time < _doorWaitUntil)
            {
                Halt();
                return true;
            }

            if (Time.time >= _repathAt)
            {
                _repathAt = Time.time + repathInterval;
                _routeFailed = !Repath(target);
            }

            // The verdict has to hold between two repaths, not only on the frame that
            // produced it: a target that could not be reached half a second ago is
            // still out of reach, and meanwhile the agent holds either a path built for
            // somewhere else or no path at all — and an agent with no path answers
            // remainingDistance with infinity, which would read as "still walking" for
            // ever. Reported as "no longer on his way" so each caller falls into the
            // branch it already has for arriving: patrol moves on to the next point,
            // investigating and searching count their wait down and go back to the
            // round. Walking on down a path built for somewhere else is the one thing
            // he must not do.
            if (_routeFailed)
            {
                Halt();
                return false;
            }

            CheckDoorAhead();

            if (OpenDoorInTheWay())
            {
                return true;
            }

            _agent.isStopped = false;
            return _agent.pathPending || _agent.remainingDistance > _agent.stoppingDistance;
        }

        /// <summary>
        /// A leaf shut across the leg he is already walking. Repath names his blocking
        /// door once every half second, and a player shutting a door behind them does
        /// it in between — at alert speed he is through the doorway before the next
        /// repath, and nothing stops him on the way: the navmesh runs through every
        /// opening (4.6) and he carries no collider. The animals got the same probe
        /// the same day; for him it is not a route to reject but a door to pull (5.5).
        ///
        /// It overrules whatever Repath named, and that is the point: Repath answers
        /// with the first shut leaf on the <em>whole</em> route, which can be ten metres
        /// off, while this one is on the leg he is walking right now and so is never the
        /// farther of the two. Filling only an empty slot would have left him walking
        /// through the door just shut in his face whenever the route happened to cross
        /// another one further along — which in this house it usually does.
        /// </summary>
        private void CheckDoorAhead()
        {
            if (_agent.pathPending || !_agent.hasPath)
            {
                return;
            }

            var lift = Vector3.up * pathProbeHeight;
            var door = DoorGate.FirstClosedDoorBetween(transform.position + lift,
                _agent.steeringTarget + lift, doorMask, _doorHits);

            // Unity object: a destroyed one compares == null but is not a real null.
            // The second half of the test is what keeps this off GetComponent every
            // frame — the answer is the same leaf until he opens it.
            if (door == null || door == _blockingDoor)
            {
                return;
            }

            _blockingDoor = door;
            _blockingDoorBody = door.GetComponent<Collider>();
        }

        /// <summary>
        /// Pulls the shut door standing across his route once he is within reach of
        /// it. True if this frame went on opening a door rather than walking.
        ///
        /// The route is never redirected at the leaf: the navmesh runs through every
        /// doorway (4.6), so his path already goes right up to and through it, and he
        /// passes inside <see cref="doorReach"/> on his own. The distance is measured
        /// to the leaf's body rather than to its transform, because the pack hangs the
        /// pivot on the hinge — a metre off-centre, and inside the frame.
        /// </summary>
        private bool OpenDoorInTheWay()
        {
            if (_blockingDoor == null)
            {
                return false;
            }

            if (_blockingDoor.IsOpen)
            {
                // A player, or another Old Man, got there first.
                _blockingDoor = null;
                _blockingDoorBody = null;
                _repathAt = 0f;
                return false;
            }

            var leaf = _blockingDoor.transform.position;
            if (_blockingDoorBody != null)
            {
                leaf = _blockingDoorBody.ClosestPoint(transform.position);
            }

            if (HorizontalDistance(leaf) > doorReach)
            {
                return false;
            }

            _blockingDoor.Use(transform);
            _doorWaitUntil = Time.time + doorSwingTime;
            _blockingDoor = null;
            _blockingDoorBody = null;
            _repathAt = 0f;

            Halt();
            FaceTowards(leaf);
            return true;
        }

        /// <summary>
        /// Points him at a target. False means there is no route to it — nothing
        /// walkable near it, or no complete path — and the caller decides what that
        /// means; the agent is left holding the path it already had, untouched.
        /// </summary>
        private bool Repath(Vector3 target)
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

            _agent.SetPath(_path);

            // The same call PetBrain uses to reject a route. For him it is not a
            // rejection: it names the leaf he has to pull (5.5).
            var door = DoorGate.FirstClosedDoor(_path, pathProbeHeight, doorMask, _doorHits);
            if (door == _blockingDoor)
            {
                return true;
            }

            _blockingDoor = door;
            _blockingDoorBody = door == null ? null : door.GetComponent<Collider>();
            return true;
        }

        private void Halt()
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

        private NoiseEmitter Loudest()
        {
            // No emitter of his own to skip: he carries none, which is also why he
            // opens doors silently (Door.Use raises the noise on the actor).
            return Hearing.Loudest(_noiseSources, transform.position, hearingThreshold, null);
        }

        private SensedPlayer NearestSeenPlayer()
        {
            if (_players == null)
            {
                return null;
            }

            SensedPlayer best = null;
            var bestDistance = float.PositiveInfinity;
            var eye = EyePoint();

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

                // The carried animal is not part of the target (the open question in
                // 5.2, settled 2026-08-04): only the player's own capsule is aimed at,
                // so hiding behind furniture with a Dog overhead works exactly as well
                // as hiding without one.
                if (!Sight.CanSee(eye, transform.forward, player.AimPoint, sightRange, sightCone, blockers))
                {
                    continue;
                }

                best = player;
                bestDistance = distance;
            }

            return best;
        }

        private int NearestPatrolIndex()
        {
            var best = 0;
            var bestDistance = float.PositiveInfinity;

            for (var i = 0; i < _patrolPoints.Count; i++)
            {
                var point = _patrolPoints[i];
                if (point == null)
                {
                    continue;
                }

                var distance = HorizontalDistance(point.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        private Vector3 EyePoint()
        {
            return transform.position + Vector3.up * eyeHeight;
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
            var moving = speed > 0.05f;

            // Halfway between the two speeds: below it he is on his round, above it he
            // is going somewhere. Exactly one bool may be true — every state in the
            // vendor controller falls back to Idle when its own bool goes false, so
            // two at once would leave the choice to transition ordering.
            var running = moving && speed > (patrolSpeed + alertSpeed) * 0.5f;

            _animator.SetBool(WalkParameter, moving && !running);
            _animator.SetBool(RunParameter, running);
        }
    }
}

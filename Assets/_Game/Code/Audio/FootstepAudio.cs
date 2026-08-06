using _Game.Code.Player;
using UnityEngine;

namespace _Game.Code.Audio
{
    /// <summary>
    /// Footsteps for anything that walks. One component for both the players and Old
    /// Man, and the reason it can be one is that the pace comes from **how far the
    /// transform actually moved**, not from anybody's idea of their own speed.
    ///
    /// <para>
    /// That choice is forced for Old Man — on a client his NavMeshAgent is switched off
    /// by ServerSimulated and the transform is moved by NetworkTransform — but it is
    /// also better for the player: <see cref="PlayerController.Speed"/> is an
    /// *intention*, so somebody walking into a wall would tramp on the spot, and the
    /// carry multiplier is already in the transform, so nothing has to know about it.
    /// </para>
    ///
    /// <para>
    /// <see cref="PlayerController.State"/> is asked for one thing only — how loud —
    /// which is exactly the thing Old Man has no answer for, and which speed cannot
    /// give: crouching is 1.0 m/s and walking with a Dog is 0.91, so the two overlap.
    /// </para>
    ///
    /// **This component must not go into LocalAvatar's list or ServerSimulated's.**
    /// Both of those switch things off on peers that do not own or simulate the actor,
    /// which is precisely where the footsteps of *other* people are supposed to be
    /// heard. That is the same trap PlayerMotion exists to fix.
    ///
    /// It emits no noise. Noise is a value on the actor (MECHANICS.md 7.5) and is
    /// PlayerNoise's alone; a step that raised it here would be counted twice, and
    /// Old Man — who has no emitter at all — would start hearing himself.
    /// </summary>
    public class FootstepAudio : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Named rather than found with GetComponent, because Old Man carries a " +
                 "second source for the shot and GetComponent would hand back whichever " +
                 "was added first — and then every step would rewrite the shotgun's pitch.")]
        [SerializeField] private AudioSource source;

        [SerializeField] private FootstepBank bank;

        [Tooltip("Played when the ground underfoot carries no FootstepSurface. Silence " +
                 "would be worse: it reads as a bug and disappears quietly the moment " +
                 "somebody adds new geometry.")]
        [SerializeField] private SurfaceKind fallback = SurfaceKind.Grass;

        [Header("Pace (MECHANICS.md section 2)")]
        [Tooltip("Metres of travel per step. Tuned against the animation by ear — the " +
                 "blend tree plays retargeted clips at a fixed rate, so the visible " +
                 "cadence is the clip's, not m/s.")]
        [SerializeField] private float strideLength = 0.8f;

        [Tooltip("Movement further than this in one frame is not movement — it is a " +
                 "warp, a spawn or an interpolation snap. Dropped, world m.")]
        [SerializeField] private float maxTravelPerFrame = 0.5f;

        [Header("Ground probe, world m")]
        [Tooltip("How far above the feet the probe starts, so a step does not begin " +
                 "inside the floor.")]
        [SerializeField] private float probeUp = 0.2f;

        [SerializeField] private float probeDown = 0.35f;

        [SerializeField] private LayerMask groundMask;

        [Header("Volume (MECHANICS.md section 2)")]
        [Tooltip("Derived from the noise table, not chosen by ear — see the note in " +
                 "section 2. Crouch is audible to you and to a player beside you, and " +
                 "dies before any listener's threshold.")]
        [SerializeField] private float crouchVolume = 0.13f;

        [SerializeField] private float walkVolume = 0.42f;
        [SerializeField] private float sprintVolume = 1f;

        [SerializeField] private float pitchJitter = 0.06f;

        // Null on Old Man, and that is the whole difference between the two actors.
        private PlayerController _controller;

        private Vector3 _lastPosition;
        private float _travelled;
        private int _lastClip = -1;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            // Here rather than in Awake: an actor switched off and on again — a teleport,
            // a spectator handover — must not bank the distance it covered while away.
            _lastPosition = transform.position;
            _travelled = 0f;
        }

        private void Update()
        {
            var position = transform.position;
            var travel = position - _lastPosition;
            travel.y = 0f;

            var distance = travel.magnitude;
            _lastPosition = position;

            if (distance > maxTravelPerFrame)
            {
                return;
            }

            _travelled += distance;
            if (_travelled < strideLength)
            {
                return;
            }

            // Subtracted rather than zeroed: the remainder belongs to the next step, so
            // sprinting does not quietly lose part of every stride. And because the
            // accumulator never resets, stopping and starting again keeps the cadence —
            // somebody creeping in short bursts is not silent for free.
            _travelled -= strideLength;
            Step();
        }

        private void Step()
        {
            // Unity objects: a destroyed one compares == null but is not a real null.
            if (bank == null || source == null)
            {
                return;
            }

            var volume = VolumeForState();
            if (volume <= 0f)
            {
                return;
            }

            SurfaceKind kind;
            if (!TryFindSurface(out kind))
            {
                // Nothing underfoot at all — mid-jump, or over a hole. Silent, which
                // agrees with section 2: a jump makes no noise either.
                return;
            }

            var clip = bank.Pick(kind, ref _lastClip);
            if (clip == null)
            {
                return;
            }

            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.PlayOneShot(clip, volume);
        }

        // The probe runs once per step, not once per frame — about twice a second.
        //
        // Deliberately not CharacterController.isGrounded: that is only refreshed inside
        // Move, which is never called on an avatar this machine does not own, so it
        // would leave every remote player permanently mute.
        private bool TryFindSurface(out SurfaceKind kind)
        {
            kind = fallback;

            RaycastHit hit;
            if (!Physics.Raycast(transform.position + Vector3.up * probeUp, Vector3.down, out hit,
                    probeUp + probeDown, groundMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // Up the hierarchy, not on the collider: one marker on the house's root
            // covers all its floor tiles.
            var surface = hit.collider.GetComponentInParent<FootstepSurface>();

            // Unity object: a destroyed one compares == null but is not a real null.
            if (surface != null)
            {
                kind = surface.Kind;
            }

            return true;
        }

        // Old Man has no MoveState and needs none: he is only ever heard at one volume.
        private float VolumeForState()
        {
            if (_controller == null)
            {
                return walkVolume;
            }

            switch (_controller.State)
            {
                case MoveState.Crouch:
                    return crouchVolume;

                case MoveState.Walk:
                    return walkVolume;

                case MoveState.Sprint:
                    return sprintVolume;

                default:
                    // Idle. One line of insurance for the tick where the replicated
                    // state has not caught up with the transform yet.
                    return 0f;
            }
        }
    }
}

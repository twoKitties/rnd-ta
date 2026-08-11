using UnityEngine;
using UnityEngine.InputSystem;

namespace _Game.Code.Player
{
    /// <summary>
    /// Movement state of an avatar. The four values are the four rows of the
    /// "Передвижение и шум игрока" table in MECHANICS.md section 2 — block 2 maps
    /// them straight onto noise levels, so keep them in sync with that table.
    /// </summary>
    public enum MoveState
    {
        Idle,
        Crouch,
        Walk,
        Sprint
    }

    /// <summary>
    /// One frame of what an avatar is being asked to do (MECHANICS.md 7.1). The
    /// simulation below consumes only this and never asks a device anything, so the
    /// same frame can arrive from the network instead of from a keyboard without the
    /// shape of the code changing.
    ///
    /// <see cref="Look"/> is the raw pointer delta, already a per-frame increment —
    /// sensitivity is applied by the simulation, not by the reader, because it is a
    /// tunable of this avatar rather than a property of the device.
    /// </summary>
    public struct PlayerIntent
    {
        public Vector2 Move;
        public Vector2 Look;
        public bool Sprint;
        public bool Crouch;

        /// <summary>Down on this frame, not held.</summary>
        public bool Jump;

        /// <summary>Down on this frame, not held.</summary>
        public bool Interact;
    }

    /// <summary>
    /// First-person movement and look. Speeds come from the tunables table in
    /// MECHANICS.md section 2 — change them there first, one at a time.
    /// </summary>
    // Runs before the rest of the avatar. Everything else on it — PlayerInteractor,
    // PlayerNoise, PlayerAnimator — reads Intent, State and Speed, which are written
    // here; without a fixed order they would read the previous frame's values, and
    // Interact would fire a frame late.
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Speed, m/s (MECHANICS.md section 2)")]
        [SerializeField] private float crouchSpeed = 1f;
        [SerializeField] private float walkSpeed = 1.4f;
        [SerializeField] private float sprintSpeed = 4f;

        [Header("Carrying")]
        [SerializeField] private PlayerHands hands;

        [Header("Look")]
        [SerializeField] private Transform cameraRoot;
        [SerializeField] private float lookSensitivity = 0.12f;
        [SerializeField] private float pitchLimit = 85f;

        [Header("Gravity and jump")]
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float groundedStick = -1f;
        [SerializeField] private float jumpHeight = 0.15f;

        [Header("Crouch (MECHANICS.md 3.1)")]
        [Tooltip("Capsule height while crouched, local units on the 0.1 root — 2.6 is 0.26 m. " +
                 "The gaps it has to fit through are measured in MECHANICS.md 3.1; the standing " +
                 "height, centre and eye are read off the prefab, not retyped here.")]
        [SerializeField] private float crouchHeight = 2.6f;

        [Tooltip("Seconds to drop or rise.")]
        [SerializeField] private float crouchBlend = 0.12f;

        // Default: Default + BlockedArea + MovingObstacles + Door. WalkingArea is
        // deliberately out — the floor sits within a capsule radius of the crouched
        // head, so including it would report a ceiling every frame, and in a
        // single-storey house nothing walkable is ever overhead. The Pet layer is out
        // too: a carried Dog rides above the head and is not parented to the avatar,
        // so it cannot be skipped as one of our own colliders.
        [Tooltip("What counts as a ceiling when standing up.")]
        [SerializeField] private LayerMask headroomMask = (1 << 0) | (1 << 7) | (1 << 8) | (1 << 9);

        /// <summary>Current movement state. Block 2 reads this to derive noise.</summary>
        public MoveState State { get; private set; } = MoveState.Idle;

        /// <summary>
        /// Horizontal speed actually being applied, m/s — the state's speed times
        /// whatever the player is carrying. Zero while Idle. Block 2 must not read
        /// this for noise: noise comes from <see cref="State"/>, so hauling a Dog
        /// makes a player slow but no quieter.
        /// </summary>
        public float Speed { get; private set; }

        /// <summary>
        /// What this avatar is being asked to do this frame. Filled from the device by
        /// <see cref="ReadInput"/> today; the setter is public because tomorrow the
        /// host fills it for the avatars it does not own, from a frame that arrived
        /// over the wire (MECHANICS.md 7.1). Everything downstream — movement, look,
        /// noise, interaction — reads this and nothing else.
        /// </summary>
        public PlayerIntent Intent { get; set; }

        /// <summary>
        /// The movement half of this avatar, applied from outside. This is what a peer
        /// that does **not** own the avatar uses: <see cref="PlayerMotion"/> carries the
        /// owner's answer over the wire and writes it here, so that everything reading
        /// <see cref="State"/> and <see cref="Speed"/> — the noise, the animator, both
        /// brains through SensedPlayer — keeps reading exactly one place and none of
        /// them has to know whose avatar this is.
        ///
        /// Without it those readers see the values this component's own OnDisable left
        /// behind, which is Idle at zero: measured 2026-08-05, that made every remote
        /// player silent to Old Man (idleNoise is 0) and motionless in everyone else's
        /// animator, no matter how they were actually moving.
        ///
        /// The state change of MECHANICS.md 7.4, and the mirror of <see cref="Move"/>,
        /// which is the same assignment made from a device.
        /// </summary>
        public void ApplyMotion(MoveState state, float speed, bool crouched)
        {
            State = state;
            Speed = speed;

            // Snapped rather than blended: Update does not run on an avatar we do not
            // own, and the capsule is invisible — what reads it is SensedPlayer.AimPoint
            // (and a spectator, through CameraRoot).
            ApplyCrouch(crouched ? 1f : 0f);
        }

        /// <summary>
        /// Down on one knee. Read by <see cref="PlayerMotion"/>, which carries it to the
        /// peers that do not own this avatar — without it the server keeps aiming at a
        /// crouched player's standing capsule centre.
        /// </summary>
        public bool Crouched => _crouch > 0.5f;

        /// <summary>
        /// Where this avatar's eyes are: the transform <see cref="Look"/> pitches, and
        /// the one a spectator copies to look out of a teammate's head. Exposed rather
        /// than searched for by name, because the camera is a child of a prefab that
        /// exists up to four times over.
        /// </summary>
        public Transform CameraRoot => cameraRoot;

        /// <summary>
        /// The Interact button's name for the HUD — "E" on a keyboard. Comes from the
        /// bindings, so it follows the .inputactions asset instead of being retyped
        /// in the UI. DontIncludeInteractions strips the "Hold " the asset's own
        /// interaction would otherwise prepend.
        /// </summary>
        public string InteractDisplayName =>
            _input.Player.Interact.GetBindingDisplayString(0, InputBinding.DisplayStringOptions.DontIncludeInteractions);

        private CharacterController _controller;
        private InputSystem_Actions _input;
        private float _pitch;
        private float _verticalVelocity;

        // The prefab's own standing pose, so the two heights are never both tunables.
        private float _standHeight;
        private Vector3 _standCenter;
        private float _standEye;

        // 0 standing, 1 crouched.
        private float _crouch;

        private readonly Collider[] _headroom = new Collider[8];

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = new InputSystem_Actions();

            _standHeight = _controller.height;
            _standCenter = _controller.center;

            // Unity object: a destroyed one compares == null but is not a real null.
            if (cameraRoot != null)
            {
                _standEye = cameraRoot.localPosition.y;
            }
        }

        private void OnEnable()
        {
            // A domain reload re-runs OnEnable on a live instance without re-running
            // Awake, and _input is not serialized — the wrapper Awake made is gone.
            if (_input == null)
            {
                _input = new InputSystem_Actions();
            }

            _input.Player.Enable();
        }

        private void OnDisable()
        {
            _input.Player.Disable();

            // Move stops running, so State and Speed would keep their last values for
            // good. Everything downstream reads them: a player shot mid-sprint would
            // go on emitting sprint noise from PlayerNoise — walking Old Man to a
            // corpse for the rest of the match — and walking on the spot in
            // PlayerAnimator. Clearing them here means death needs no special case in
            // either.
            State = MoveState.Idle;
            Speed = 0f;
            Intent = default;
        }

        private void OnDestroy()
        {
            // Null after a domain reload if this instance stayed disabled throughout
            // (LocalAvatar keeps it off on every avatar but ours), so OnEnable never
            // rebuilt it.
            if (_input != null)
            {
                _input.Dispose();
            }
        }

        private void Update()
        {
            // The one place a device is read. Once an avatar can be driven from the
            // network, this is the line that is skipped for the ones we do not own —
            // Look and Move below stay exactly as they are.
            Intent = ReadInput();

            Look();
            Crouch();
            Move();
        }

        // Before Move, so this frame walks on the capsule the player asked for.
        // Driven by the button and not by State: ResolveState answers Idle whenever
        // there is no movement input, so a player crouching on the spot would stand
        // straight back up.
        private void Crouch()
        {
            var target = Intent.Crouch ? 1f : 0f;

            if (target < _crouch && NoHeadroom())
            {
                target = _crouch;
            }

            var step = crouchBlend > 0f ? Time.deltaTime / crouchBlend : 1f;
            ApplyCrouch(Mathf.MoveTowards(_crouch, target, step));
        }

        private void ApplyCrouch(float amount)
        {
            _crouch = amount;

            var height = Mathf.Lerp(_standHeight, crouchHeight, amount);
            _controller.height = height;

            // Half the height: the capsule stands on the ground, so its centre follows
            // the top rather than staying put.
            _controller.center = new Vector3(_standCenter.x, height * 0.5f, _standCenter.z);

            if (cameraRoot == null)
            {
                return;
            }

            var eye = cameraRoot.localPosition;
            eye.y = _standEye * (height / _standHeight);
            cameraRoot.localPosition = eye;
        }

        /// <summary>
        /// Something over the head, so standing up is refused — which is what keeps a
        /// player who crawled under a table under it. Swept upward from the top of the
        /// capsule as it is now, through the space the standing one would need.
        /// </summary>
        private bool NoHeadroom()
        {
            var scale = transform.lossyScale.y;
            var radius = _controller.radius * scale;
            var rise = (_standHeight - _controller.height) * scale;

            if (rise <= 0f)
            {
                return false;
            }

            var foot = transform.TransformPoint(new Vector3(_standCenter.x, 0f, _standCenter.z));
            var from = foot + Vector3.up * (_controller.height * scale - radius);
            var to = from + Vector3.up * rise;

            var found = Physics.OverlapCapsuleNonAlloc(from, to, radius, _headroom, headroomMask,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < found; i++)
            {
                // Our own capsule is in that space by definition.
                if (_headroom[i].transform.root != transform)
                {
                    return true;
                }
            }

            return false;
        }

        // Devices in, intent out, nothing else. Deliberately produces no side effects:
        // the button states are sampled once per frame so that two readers of the same
        // intent cannot disagree about whether Interact went down.
        private PlayerIntent ReadInput()
        {
            return new PlayerIntent
            {
                Move = _input.Player.Move.ReadValue<Vector2>(),
                Look = _input.Player.Look.ReadValue<Vector2>(),
                Sprint = _input.Player.Sprint.IsPressed(),
                Crouch = _input.Player.Crouch.IsPressed(),
                Jump = _input.Player.Jump.WasPressedThisFrame(),
                Interact = _input.Player.Interact.WasPressedThisFrame()
            };
        }

        private void Look()
        {
            // cameraRoot is a Unity object: a destroyed one compares == null but is not
            // a real null, so `?.` and `??` would lie about it.
            if (cameraRoot == null)
            {
                return;
            }

            var look = Intent.Look * lookSensitivity;

            // Pointer delta is already a per-frame increment. Multiplying it by
            // Time.deltaTime is the classic bug that ties sensitivity to frame rate.
            transform.Rotate(Vector3.up, look.x, Space.Self);

            _pitch = Mathf.Clamp(_pitch - look.y, -pitchLimit, pitchLimit);
            cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            var input = Intent.Move;

            State = ResolveState(input);
            Speed = State switch
            {
                MoveState.Crouch => crouchSpeed,
                MoveState.Walk => walkSpeed,
                MoveState.Sprint => sprintSpeed,
                _ => 0f
            };

            // The load is the price of the trophy: Dog x0.65, Kitty x0.85, Parrot x1
            // (MECHANICS.md section 2).
            if (hands != null)
            {
                Speed *= hands.SpeedMultiplier;
            }

            var direction = transform.right * input.x + transform.forward * input.y;
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            if (_controller.isGrounded)
            {
                if (_verticalVelocity < 0f)
                {
                    // A small downward bias keeps the capsule in contact with the floor,
                    // so isGrounded stays true instead of flickering on every step.
                    _verticalVelocity = groundedStick;
                }

                if (Intent.Jump)
                {
                    // Launch speed that peaks at exactly jumpHeight: v = sqrt(2 * g * h).
                    // Deriving it from the height keeps the tunable in metres, so it stays
                    // meaningful if gravity is ever retuned for this character's scale.
                    _verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
                }
            }

            _verticalVelocity += gravity * Time.deltaTime;

            var velocity = direction * Speed + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
        }

        private MoveState ResolveState(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f)
            {
                return MoveState.Idle;
            }

            // Crouch wins over Sprint: it is the quiet state, and holding both must
            // not let a player be quiet and fast at the same time.
            if (Intent.Crouch)
            {
                return MoveState.Crouch;
            }

            return Intent.Sprint ? MoveState.Sprint : MoveState.Walk;
        }
    }
}

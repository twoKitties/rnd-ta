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
    /// First-person movement and look. Speeds come from the tunables table in
    /// MECHANICS.md section 2 — change them there first, one at a time.
    /// </summary>
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
        [SerializeField] private FirstPersonBody body;
        [SerializeField] private float lookSensitivity = 0.12f;
        [SerializeField] private float pitchLimit = 85f;

        [Header("Gravity and jump")]
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float groundedStick = -1f;
        [SerializeField] private float jumpHeight = 0.15f;

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
        /// True on the frame Interact went down. This component owns the avatar's
        /// input, so other systems read the intent from here instead of opening a
        /// second InputSystem_Actions — see MECHANICS.md 7.1.
        /// </summary>
        public bool InteractPressedThisFrame => _input.Player.Interact.WasPressedThisFrame();

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

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = new InputSystem_Actions();
        }

        private void Start()
        {
            // This component only ever drives the avatar the local player looks
            // through, so reaching the first-person view from here is the ownership
            // check. Once netcode lands, gate it on "is the local avatar" instead.
            if (body != null)
            {
                body.ApplyFirstPersonView();
            }
        }

        private void OnEnable()
        {
            _input.Player.Enable();
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void OnDisable()
        {
            _input.Player.Disable();
            Cursor.lockState = CursorLockMode.None;
        }

        private void OnDestroy()
        {
            _input.Dispose();
        }

        private void Update()
        {
            Look();
            Move();
        }

        private void Look()
        {
            // cameraRoot is a Unity object: a destroyed one compares == null but is not
            // a real null, so `?.` and `??` would lie about it.
            if (cameraRoot == null)
            {
                return;
            }

            var look = _input.Player.Look.ReadValue<Vector2>() * lookSensitivity;

            // Pointer delta is already a per-frame increment. Multiplying it by
            // Time.deltaTime is the classic bug that ties sensitivity to frame rate.
            transform.Rotate(Vector3.up, look.x, Space.Self);

            _pitch = Mathf.Clamp(_pitch - look.y, -pitchLimit, pitchLimit);
            cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            var input = _input.Player.Move.ReadValue<Vector2>();

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

                if (_input.Player.Jump.WasPressedThisFrame())
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
            if (_input.Player.Crouch.IsPressed())
            {
                return MoveState.Crouch;
            }

            return _input.Player.Sprint.IsPressed() ? MoveState.Sprint : MoveState.Walk;
        }
    }
}

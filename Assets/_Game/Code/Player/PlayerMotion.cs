using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// How this avatar is moving, as everybody else needs to know it: the movement
    /// state and the speed, sent by whoever owns the avatar and applied by everybody
    /// who does not.
    ///
    /// It exists because the avatar's own <see cref="PlayerController"/> is switched
    /// off on every machine but its owner's (LocalAvatar's list — one keyboard must
    /// not drive four aliens), and that component is where State and Speed are
    /// produced. Measured 2026-08-05, with nothing carrying them:
    /// <list type="bullet">
    /// <item>PlayerNoise runs on every avatar on every machine and emitted
    /// <c>idleNoise</c>, which is <b>0</b> on Player.prefab — so on the server every
    /// client was permanently, perfectly silent and neither Old Man (5.4) nor the
    /// animals (4.x) could hear one sprint past.</item>
    /// <item>PlayerAnimator likewise read zero, so teammates slid about standing
    /// still.</item>
    /// <item>Both brains ask SensedPlayer, which asks PlayerController, so the AI's
    /// whole idea of "is that player being quiet" was wrong for three players out of
    /// four.</item>
    /// </list>
    ///
    /// The three readers are untouched by this and keep reading
    /// <see cref="PlayerController"/>. That is the point: the fix is a component that
    /// fills the same field from the wire, not a branch inside the noise, the animator
    /// and two tuned brains (MECHANICS.md 7.4).
    ///
    /// Movement itself is not sent here — that is NetworkTransform's job, and the
    /// avatar is client-authoritative. This is only the *description* of the movement,
    /// which the transform does not carry.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerMotion : NetworkBehaviour
    {
        [Tooltip("How much the speed must change before it is worth a packet, m/s. " +
                 "Speed is quantised by the movement state anyway, so this only " +
                 "swallows the step between carrying and not carrying.")]
        [SerializeField] private float speedEpsilon = 0.05f;

        private readonly SyncVar<MoveState> _state = new SyncVar<MoveState>();
        private readonly SyncVar<float> _speed = new SyncVar<float>();

        private PlayerController _controller;

        // Asked through the NetworkObject rather than through NetworkBehaviour's own
        // IsOwner: that property dereferences a cache which is only filled once a
        // NetworkManager has launched, so in a level played with no networking at all —
        // a supported mode, and the one this project's AI is actually tested in — it is
        // null and every frame throws. Same idiom as Pet and PlayerLife.
        private NetworkObject _nob;

        private bool IsReplicated => _nob != null && _nob.IsSpawned;

        // Ours to drive: the avatar we own, or the only avatar there is when nothing is
        // networked.
        private bool IsMine => !IsReplicated || _nob.IsOwner;

        // The last pair this machine sent, so that standing still costs nothing. Both
        // values only change when the player changes what they are doing, so this is a
        // handful of packets a second at worst.
        private MoveState _sent = MoveState.Idle;
        private float _sentSpeed;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _nob = GetComponent<NetworkObject>();

            _state.OnChange += OnStateChanged;
            _speed.OnChange += OnSpeedChanged;
        }

        private void OnDestroy()
        {
            _state.OnChange -= OnStateChanged;
            _speed.OnChange -= OnSpeedChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // A player who joins and then stands perfectly still would otherwise be
            // described by whatever PlayerController.OnDisable left behind. It is the
            // same value in practice, but the picture should come from the wire from
            // the first frame rather than from a coincidence.
            if (!IsMine)
            {
                Apply();
            }
        }

        private void OnStateChanged(MoveState previous, MoveState next, bool asServer)
        {
            Apply();
        }

        private void OnSpeedChanged(float previous, float next, bool asServer)
        {
            Apply();
        }

        // Only where this avatar is not being driven locally. On the owner's own
        // machine PlayerController is live and is the authority on its own movement;
        // writing to it there would be a round trip fighting the keyboard.
        private void Apply()
        {
            if (IsMine || _controller == null)
            {
                return;
            }

            _controller.ApplyMotion(_state.Value, _speed.Value);
        }

        private void Update()
        {
            // Nothing to send when there is no network: the one PlayerController in the
            // process is already the source everything reads.
            if (!IsReplicated || !IsMine || _controller == null)
            {
                return;
            }

            var state = _controller.State;
            var speed = _controller.Speed;

            if (state == _sent && Mathf.Abs(speed - _sentSpeed) < speedEpsilon)
            {
                return;
            }

            _sent = state;
            _sentSpeed = speed;

            // A host owns its own avatar and is already where the decisions are made.
            if (IsServerInitialized)
            {
                _state.Value = state;
                _speed.Value = speed;
                return;
            }

            Report(state, speed);
        }

        // The owner is the only one who can answer this, so ownership is required —
        // unlike the door and the animal, where the rule is re-checked against the
        // world and a client's claim cannot be trusted. Here the claim *is* the fact:
        // nobody else knows which keys are being held.
        [ServerRpc]
        private void Report(MoveState state, float speed)
        {
            _state.Value = state;
            _speed.Value = speed;
        }
    }
}

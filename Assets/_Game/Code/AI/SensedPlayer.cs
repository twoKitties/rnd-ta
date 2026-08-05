using _Game.Code.Player;
using UnityEngine;

namespace _Game.Code.AI
{
    /// <summary>
    /// One player as an AI sees them: where they are, how loudly they are moving and
    /// whether they still count. Built once when LevelBootstrapper binds an actor, so that
    /// no brain does GetComponent in Update and none of them searches the scene
    /// (MECHANICS.md 7.6).
    ///
    /// LevelGoal reads it too, for <see cref="IsAlive"/> and <see cref="Transform"/>:
    /// "who is still playing" has to mean the same thing to the outcome as it does to
    /// the animals, or a player could be dead to Old Man and alive to the win check.
    ///
    /// A plain class rather than a component: it describes somebody else's avatar, so
    /// it must not live on it. Nothing per-player is stored here beyond the
    /// references — the avatar's own components stay the source of truth.
    /// </summary>
    public class SensedPlayer
    {
        private readonly GameObject _avatar;
        private readonly PlayerController _controller;
        private readonly PlayerLife _life;
        private readonly CharacterController _body;

        public SensedPlayer(GameObject avatar)
        {
            _avatar = avatar;
            _controller = avatar.GetComponent<PlayerController>();
            _life = avatar.GetComponent<PlayerLife>();
            _body = avatar.GetComponent<CharacterController>();
            Transform = avatar.transform;

            // So the first difference is taken against where they actually are rather
            // than against the origin, which would read as a sprint from across the map.
            _lastPosition = Transform.position;
            _lastSampledAt = Time.time;
        }

        public Transform Transform { get; }

        /// <summary>How this player is moving. Idle if they have no controller.</summary>
        public MoveState State => _controller == null ? MoveState.Idle : _controller.State;

        /// <summary>
        /// Sneaking or standing still. This is what an animal reads to decide whether
        /// to bolt: moving quietly is the whole of "приманивать" for a Dog and a Kitty
        /// (MECHANICS.md 4).
        ///
        /// Idle counts as quiet, and that is a decision rather than an oversight. If
        /// standing still frightened an animal, one inside the panic radius could
        /// never settle and would panic at a statue. It does not weaken the crouch:
        /// closing the last metres to the 1.5 m capture distance still means moving,
        /// and moving upright is what makes the animal run.
        /// </summary>
        public bool IsQuiet => State == MoveState.Crouch || State == MoveState.Idle;

        /// <summary>
        /// Sprinting. The other end of the same scale as <see cref="IsQuiet"/>, and it
        /// lives here for the same reason: which MoveState means what is this class's
        /// knowledge, so a brain never compares against the enum itself.
        /// </summary>
        public bool IsRunning => State == MoveState.Sprint;

        /// <summary>
        /// Where they are actually going, world m/s, flattened — the vertical part is
        /// dropped because falling is not running at anybody.
        ///
        /// Differenced between frames rather than read off the CharacterController, and
        /// that is a correctness fix, not a preference (audit 2026-08-05).
        /// <c>CharacterController.velocity</c> is the distance its own <c>Move</c>
        /// covered, so it is zero for any avatar this machine does not drive: on the
        /// server every client's controller is switched off and the transform is moved
        /// by NetworkTransform instead. The charge rule of MECHANICS.md 4.1 and 4.4
        /// reads this, so it worked for the host and for nobody else — three players out
        /// of four could run animals down for free. A transform difference is one source
        /// for every avatar and costs the network nothing.
        ///
        /// Sampled once per frame however many brains ask, and a gap longer than
        /// <see cref="MaxSampleGap"/> is thrown away rather than turned into a false
        /// sprint — that gap means a scene load or a spawn, not a player moving.
        /// </summary>
        public Vector3 Velocity
        {
            get
            {
                Sample();
                return _velocity;
            }
        }

        // Longer than this between two reads and the difference is not movement, world
        // seconds. A scene load is the case that matters: it would otherwise read as a
        // player crossing the house in one frame.
        private const float MaxSampleGap = 0.5f;

        private Vector3 _lastPosition;
        private float _lastSampledAt;
        private Vector3 _velocity;

        private void Sample()
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (Transform == null)
            {
                _velocity = Vector3.zero;
                return;
            }

            var now = Time.time;
            var elapsed = now - _lastSampledAt;

            // Already answered this frame. Both brains and the outcome all ask, and the
            // difference between two reads in the same frame is zero — which would wipe
            // the real value for whoever asked second.
            if (elapsed <= 0f)
            {
                return;
            }

            var position = Transform.position;

            if (elapsed > MaxSampleGap)
            {
                _velocity = Vector3.zero;
            }
            else
            {
                var travel = position - _lastPosition;
                travel.y = 0f;
                _velocity = travel / elapsed;
            }

            _lastPosition = position;
            _lastSampledAt = now;
        }

        /// <summary>
        /// A dead player scares nobody and is not worth shooting again. The avatar
        /// stays in the scene as a spectator camera, so being switched off is not the
        /// test — PlayerLife is.
        /// </summary>
        public bool IsAlive
        {
            // Unity object: a destroyed one compares == null but is not a real null, so
            // `?.` and `??` would lie about it.
            get
            {
                if (_avatar == null || !_avatar.activeInHierarchy)
                {
                    return false;
                }

                return _life == null || !_life.IsDead;
            }
        }

        /// <summary>
        /// Shot by Old Man: instant death, no respawn (MECHANICS.md 5.2 and 3.7).
        /// Routed through here so his brain never reaches for a component on somebody
        /// else's avatar, and does nothing to an avatar with no PlayerLife.
        ///
        /// Under 7.4 this is the host's decision — a client does not decide it was
        /// seen — and PlayerLife.Kill is already idempotent, so a confirmation
        /// arriving after the local shot changes nothing.
        /// </summary>
        public void Kill()
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (_life != null)
            {
                _life.Kill();
            }
        }

        /// <summary>
        /// Where a line of sight should be aimed: the middle of the capsule, not the
        /// feet. PlayerInteractor learned the same lesson pointing the other way — a
        /// ray at floor level is stopped by the first skirting board.
        ///
        /// The capsule's centre is a local value on a prefab scaled 0.1, so it goes
        /// through TransformPoint rather than being added as world metres.
        /// </summary>
        public Vector3 AimPoint => _body == null
            ? Transform.position
            : Transform.TransformPoint(_body.center);
    }
}

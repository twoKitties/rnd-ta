using _Game.Code.Player;
using UnityEngine;

namespace _Game.Code.AI
{
    /// <summary>
    /// One player as an AI sees them: where they are, how loudly they are moving and
    /// whether they still count. Built once when Bootstrapper binds an actor, so that
    /// no brain does GetComponent in Update and none of them searches the scene
    /// (MECHANICS.md 7.6).
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

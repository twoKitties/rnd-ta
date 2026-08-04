using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// Everything on an avatar that belongs to the person sitting in front of this
    /// screen, and nothing else: the camera, the ear, the HUD and the input. One
    /// process will hold up to four avatars and exactly one of them is ours.
    ///
    /// Unity allows a single active <see cref="AudioListener"/> per scene, and a
    /// Screen Space – Overlay canvas ignores whose avatar it hangs on — four of
    /// either is not a degraded picture, it is a broken one. The same goes for
    /// <see cref="FirstPersonBody"/>: applied to somebody else's avatar it would take
    /// their head off on our screen.
    ///
    /// Written as one list of references, the same idiom as PlayerLife.disableOnDeath,
    /// because the alternative — each component asking "am I local?" — spreads the
    /// question over seven files that have no business knowing the answer.
    /// </summary>
    public class LocalAvatar : MonoBehaviour
    {
        [Tooltip("Switched on only for our own avatar: camera, AudioListener, the HUD " +
                 "canvas, PlayerController, PlayerInteractor and the two HUD scripts.")]
        [SerializeField] private Behaviour[] localOnly;

        [Tooltip("Hides the head we are looking out of. Ours only — everyone else " +
                 "must be seen whole.")]
        [SerializeField] private FirstPersonBody body;

        /// <summary>True once this avatar has been claimed as the local player's.</summary>
        public bool IsLocal { get; private set; }

        /// <summary>
        /// Claim this avatar, or disown it. Called by the level's entry point today,
        /// which spawns exactly one avatar and owns it; tomorrow the same call is made
        /// from whatever tells us which of the four is ours.
        /// </summary>
        public void Apply(bool isLocal)
        {
            IsLocal = isLocal;

            for (var i = 0; i < localOnly.Length; i++)
            {
                // Unity object: a destroyed one compares == null but is not a real
                // null, so `?.` and `??` would lie about it.
                if (localOnly[i] != null)
                {
                    localOnly[i].enabled = isLocal;
                }
            }

            if (isLocal && body != null)
            {
                body.ApplyFirstPersonView();
            }

            // The cursor is one per process, so it cannot be owned by a component that
            // exists four times over. It used to be locked and unlocked by
            // PlayerController.OnEnable/OnDisable, which meant that disabling three
            // other people's avatars unlocked ours at the start of every match, and
            // that a teammate being shot unlocked it again mid-game.
            if (isLocal)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }
        }
    }
}

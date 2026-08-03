using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// Drives the avatar's animator from the speed the controller is actually
    /// applying. One float, because that is all the blend tree needs: the clips are
    /// laid out along m/s (idle at 0, walk at 2.5, run at 5 — the speeds from
    /// MECHANICS.md section 2), so carrying a Dog at x0.65 lands between walk and run
    /// on its own and no extra state is needed for it.
    ///
    /// The clips are humanoid ones borrowed from the Old Man's pack, retargeted onto
    /// the alien's humanoid avatar — the alien's own model ships no usable animation.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        [Tooltip("Seconds the animator takes to catch up with a speed change. Keeps " +
                 "the legs from snapping between clips when a pet is picked up.")]
        [SerializeField] private float damping = 0.12f;

        private PlayerController _controller;

        // A hash, not state: nothing per-player lives here (MECHANICS.md 7.3).
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }
        }

        private void Update()
        {
            if (animator == null)
            {
                return;
            }

            animator.SetFloat(SpeedParameter, _controller.Speed, damping, Time.deltaTime);
        }
    }
}

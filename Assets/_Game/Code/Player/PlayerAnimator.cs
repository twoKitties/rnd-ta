using _Game.Code.Pets;
using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// Drives the avatar's animator from what the player is doing.
    ///
    /// The base layer is one blend tree along speed in m/s: the clips are laid out at
    /// idle 0, walk 2.5 and run 5 (the speeds from MECHANICS.md section 2), so
    /// carrying a Dog at x0.65 lands between walk and run on its own and needs no
    /// state of its own.
    ///
    /// The carry layer sits on top through an upper-body mask: the legs keep walking
    /// from the base layer while the arms take the pose of whatever is being carried —
    /// out front for a Kitty or a Parrot, overhead for a Dog. All the clips are
    /// humanoid ones retargeted onto the alien, which ships none of its own.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerHands hands;

        [Tooltip("Seconds the animator takes to catch up with a speed change. Keeps " +
                 "the legs from snapping between clips when a pet is picked up.")]
        [SerializeField] private float damping = 0.12f;

        [Tooltip("Seconds to fade the carry pose in and out.")]
        [SerializeField] private float carryFade = 0.2f;

        private PlayerController _controller;
        private int _carryLayer = -1;
        private float _carryWeight;

        // Hashes, not state: nothing per-player lives here (MECHANICS.md 7.3).
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int CarryPoseParameter = Animator.StringToHash("CarryPose");

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (hands == null)
            {
                hands = GetComponent<PlayerHands>();
            }

            if (animator != null)
            {
                _carryLayer = animator.GetLayerIndex("Carry");
            }
        }

        private void Update()
        {
            if (animator == null)
            {
                return;
            }

            animator.SetFloat(SpeedParameter, _controller.Speed, damping, Time.deltaTime);
            UpdateCarryPose();
        }

        private void UpdateCarryPose()
        {
            if (_carryLayer < 0 || hands == null)
            {
                return;
            }

            var carried = hands.Carried;

            // Plain == on a Unity object: a destroyed one compares == null but is not
            // a real null, so `?.` would lie about it.
            if (carried != null)
            {
                // Switching the pose only while something is held means the arms never
                // travel between poses in plain sight — the change happens under a
                // layer weight that is already fading in.
                animator.SetFloat(CarryPoseParameter, carried.Pose == CarryPose.Overhead ? 1f : 0f);
            }

            var target = carried == null ? 0f : 1f;
            var step = carryFade <= 0f ? 1f : Time.deltaTime / carryFade;
            _carryWeight = Mathf.MoveTowards(_carryWeight, target, step);
            animator.SetLayerWeight(_carryLayer, _carryWeight);
        }
    }
}

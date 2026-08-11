using _Game.Code.Pets;
using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// The player's one pair of hands (MECHANICS.md 3.4). An animal, an item and
    /// food occupy it on equal terms, and a full pair blocks every other
    /// interaction — today that means doors.
    /// </summary>
    public class PlayerHands : MonoBehaviour
    {
        [Tooltip("Where a Dog rides: overhead. It is 1.27 m long against a 0.5 m " +
                 "alien and would fill the screen if held in front.")]
        [SerializeField] private Transform overheadAnchor;

        [Tooltip("Where a Kitty or a Parrot rides: in front, in the arms.")]
        [SerializeField] private Transform frontAnchor;

        [SerializeField] private Pet carried;

        private PlayerController _controller;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        /// <summary>What the player is holding, or null.</summary>
        public Pet Carried => carried;

        /// <summary>
        /// Down on one knee, asked of the hands because that is what the pickup rule
        /// already holds (MECHANICS.md 3.3). Cached: <see cref="Pet.CanBeTakenBy"/>
        /// runs over every animal every frame.
        /// </summary>
        public bool Crouched => _controller != null && _controller.Crouched;

        // Plain == on a Unity object: a destroyed one compares == null but is not a
        // real null, so `?.` and `??` would lie about it.
        public bool IsEmpty => carried == null;

        /// <summary>How much the load slows this player down. 1 when empty.</summary>
        public float SpeedMultiplier => carried == null ? 1f : carried.CarrySpeedMultiplier;

        /// <summary>The spot an animal rides at, chosen by its own pose.</summary>
        public Transform AnchorFor(CarryPose pose)
        {
            return pose == CarryPose.Overhead ? overheadAnchor : frontAnchor;
        }

        // Filling the slot goes through Pet, which owns the carrier slot on the other
        // side. Writing it from anywhere else would let the two disagree, and that
        // desync reads as "the animal teleports" long after the fact.
        internal void Take(Pet pet)
        {
            carried = pet;
        }

        internal void Clear()
        {
            carried = null;
        }
    }
}

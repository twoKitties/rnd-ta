using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// The player's one pair of hands (MECHANICS.md 3.4). An animal, an item and
    /// food occupy it on equal terms, and a full pair blocks every other
    /// interaction — today that means doors.
    ///
    /// Block 3 (grab / release) is what will actually fill the slot. Until then the
    /// field is serialized so the rule can still be tested: drop anything into it
    /// in the inspector and the door stops responding.
    /// </summary>
    public class PlayerHands : MonoBehaviour
    {
        [SerializeField] private Transform carried;

        /// <summary>What the player is holding, or null.</summary>
        public Transform Carried
        {
            get { return carried; }
            set { carried = value; }
        }

        // Plain == on a Unity object: a destroyed one compares == null but is not a
        // real null, so `?.` and `??` would lie about it.
        public bool IsEmpty => carried == null;
    }
}

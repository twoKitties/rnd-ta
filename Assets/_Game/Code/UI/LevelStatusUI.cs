using _Game.Code.Level;
using UnityEngine;
using UnityEngine.UI;

namespace _Game.Code.UI
{
    /// <summary>
    /// The raid's score on the player's own HUD: how many animals are aboard, and
    /// the outcome once there is one. Section 6 of MECHANICS.md defers the real
    /// end screen — this is the counter and one line, nothing more.
    /// </summary>
    public class LevelStatusUI : MonoBehaviour
    {
        [SerializeField] private Text counter;
        [SerializeField] private Text result;

        private LevelGoal _goal;
        private string _shownCounter;
        private string _shownResult;

        /// <summary>Bound by Bootstrapper: the goal is a scene object.</summary>
        public void Bind(LevelGoal goal)
        {
            _goal = goal;
        }

        private void Update()
        {
            if (_goal == null)
            {
                Write(counter, ref _shownCounter, string.Empty);
                Write(result, ref _shownResult, string.Empty);
                return;
            }

            Write(counter, ref _shownCounter, $"Pets: {_goal.Delivered}/{_goal.Total}");

            var outcome = _goal.IsWon ? "YOU WIN" : _goal.IsLost ? "RAID FAILED" : string.Empty;
            Write(result, ref _shownResult, outcome);
        }

        // Assigning Text.text rebuilds the mesh, so only touch it when the line
        // actually changed rather than every frame.
        private static void Write(Text label, ref string shown, string text)
        {
            if (label == null || shown == text)
            {
                return;
            }

            shown = text;
            label.text = text;
            label.gameObject.SetActive(text.Length > 0);
        }
    }
}

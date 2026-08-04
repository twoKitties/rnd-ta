using _Game.Code.Level;
using _Game.Code.Player;
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

        // This HUD lives on the avatar it belongs to, so the death it reports is that
        // avatar's own — no lookup and no "which player is this" question.
        private PlayerLife _life;

        private void Awake()
        {
            _life = GetComponent<PlayerLife>();
        }

        /// <summary>Bound by LevelBootstrapper: the goal is a scene object.</summary>
        public void Bind(LevelGoal goal)
        {
            _goal = goal;
        }

        private void Update()
        {
            // Being dead is reported even with no goal bound: it is the one thing the
            // player must be told, and it stays on screen while they watch the rest of
            // the raid as a spectator (MECHANICS.md 3.7).
            // Unity object: a destroyed one compares == null but is not a real null.
            var dead = _life != null && _life.IsDead;

            if (_goal == null)
            {
                Write(counter, ref _shownCounter, string.Empty);
                Write(result, ref _shownResult, dead ? "YOU DIED" : string.Empty);
                return;
            }

            Write(counter, ref _shownCounter, $"Pets: {_goal.Delivered}/{_goal.Total}");

            // The raid's outcome outranks a personal death: once it is over, "won" or
            // "lost" is what everybody needs to read, dead or alive.
            var outcome = _goal.IsWon ? "YOU WIN"
                : _goal.IsLost ? "RAID FAILED"
                : dead ? "YOU DIED"
                : string.Empty;

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

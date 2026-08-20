using _Game.Code.App;
using _Game.Code.Level;
using _Game.Code.Player;
using UnityEngine;
using UnityEngine.UI;

namespace _Game.Code.UI
{
    /// <summary>
    /// The raid's score on the player's own HUD while it is running: how many animals
    /// are aboard, and whether this player has been shot. The outcome itself belongs to
    /// <see cref="EndScreenUI"/> — once there is one, this whole line goes quiet, so
    /// there is exactly one place on screen saying how the raid ended.
    /// </summary>
    public class LevelStatusUI : MonoBehaviour
    {
        [SerializeField] private Text counter;
        [SerializeField] private Text result;

        [Tooltip("Which keys switch the watched player. Shown only while dead, and only " +
                 "with more than one living player to switch between.")]
        [SerializeField] private Text spectateHint;

        private LevelGoal _goal;
        private string _shownCounter;
        private string _shownResult;
        private string _shownHint;

        // This HUD lives on the avatar it belongs to, so the death it reports is that
        // avatar's own — no lookup and no "which player is this" question.
        private PlayerLife _life;

        private SpectatorCamera _spectator;
        private string _hint = string.Empty;

        private void Awake()
        {
            _life = GetComponent<PlayerLife>();
            _spectator = GetComponent<SpectatorCamera>();
        }

        // Read once: rebinding at runtime does not exist, the InteractionPromptUI idiom.
        private void Start()
        {
            if (_spectator == null)
            {
                return;
            }

            var previous = _spectator.PreviousDisplayName;
            var next = _spectator.NextDisplayName;

            // No names, no hint: "[] / [] switch view" is worse than nothing.
            if (previous.Length > 0 && next.Length > 0)
            {
                _hint = $"[{previous}] / [{next}] switch view";
            }
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

            // Once the raid is over, EndScreenUI takes the screen and says so itself.
            // These lines go quiet rather than repeating it: the panel's background is
            // translucent, so a second "RAID FAILED" underneath reads as the text
            // doubling (reported 2026-08-05).
            var over = _goal != null && (_goal.IsWon || _goal.IsLost);

            Write(counter, ref _shownCounter,
                _goal == null || over ? string.Empty : $"Pets: {_goal.Delivered}/{_goal.Total}");

            Write(result, ref _shownResult, dead && !over ? "YOU DIED" : string.Empty);

            // Nothing to switch between with one living player left.
            Write(spectateHint, ref _shownHint,
                dead && !over && LivingPlayers() >= 2 ? _hint : string.Empty);
        }

        private static int LivingPlayers()
        {
            var boot = LevelBootstrapper.Current;
            if (boot == null)
            {
                return 0;
            }

            var players = boot.SensedPlayers;
            var living = 0;
            for (var i = 0; i < players.Count; i++)
            {
                if (players[i].IsAlive)
                {
                    living++;
                }
            }

            return living;
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

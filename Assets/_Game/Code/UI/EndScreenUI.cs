using System.Collections.Generic;
using _Game.Code.App;
using _Game.Code.Level;
using UnityEngine;
using UnityEngine.UI;

namespace _Game.Code.UI
{
    /// <summary>
    /// The way out of a raid: the panel that appears when it is over, and the same
    /// panel on Escape while it is not (MECHANICS.md section 6).
    ///
    /// One panel for both, deliberately. "Leaving in the middle" and "the raid ended"
    /// need the same buttons, and a second menu would be a second thing holding the
    /// cursor. It is not a pause menu and does not claim to be one — nothing stops
    /// while it is open, which is why the mid-raid title asks a question rather than
    /// announcing a pause.
    ///
    /// Lives on the avatar next to <see cref="LevelStatusUI"/> and rides in
    /// LocalAvatar's list, so only the person at this screen has one. That matters
    /// more here than for the rest of the HUD: this one takes the mouse cursor, and
    /// the cursor is one per process.
    /// </summary>
    public class EndScreenUI : MonoBehaviour
    {
        [Tooltip("Switched on when the raid ends or Escape is pressed. Hidden with " +
                 "SetActive rather than alpha: an invisible panel still eats clicks, " +
                 "which is how the lobby lost its buttons on 2026-08-05.")]
        [SerializeField] private GameObject panel;

        [SerializeField] private Text title;

        [Tooltip("How the raid went. Filled in once, when it ends.")]
        [SerializeField] private Text stats;

        [Tooltip("Mid-raid only: closes the panel again. There is nothing to resume " +
                 "once the raid is over, so it is hidden then.")]
        [SerializeField] private Button resumeButton;

        [Tooltip("Host only, and only once the raid is over. Takes everybody back to " +
                 "the hub, which is also how a lost raid is played again.")]
        [SerializeField] private Button hubButton;

        [Tooltip("The host ends the session; anybody else leaves alone.")]
        [SerializeField] private Button leaveButton;

        [Tooltip("Switched off while the panel is up: PlayerController and " +
                 "PlayerInteractor. Same idiom as PlayerLife.disableOnDeath.")]
        [SerializeField] private Behaviour[] suspendWhileOpen;

        // Exactly the ones this panel switched off, so closing it cannot switch on
        // something that was already off. Death disables PlayerController through
        // PlayerLife, and Resume must not undo that.
        private readonly List<Behaviour> _suspended = new List<Behaviour>();

        private LevelGoal _goal;
        private InputSystem_Actions _input;
        private bool _open;
        private bool _summarised;
        private string _shownTitle;

        private void Awake()
        {
            _input = new InputSystem_Actions();

            // Never trust what the panel was saved as.
            if (panel != null)
            {
                panel.SetActive(false);
            }

            if (stats != null)
            {
                stats.gameObject.SetActive(false);
            }

            // Wired here rather than in the inspector, the same way MenuView does it:
            // a missing UnityEvent is silent, a missing reference here is not.
            if (resumeButton != null)
            {
                resumeButton.onClick.AddListener(Resume);
            }

            if (hubButton != null)
            {
                hubButton.onClick.AddListener(ToHub);
            }

            if (leaveButton != null)
            {
                leaveButton.onClick.AddListener(Leave);
            }
        }

        private void OnEnable()
        {
            // A domain reload re-runs OnEnable on a live instance without re-running
            // Awake, and _input is not serialized — the wrapper Awake made is gone.
            if (_input == null)
            {
                _input = new InputSystem_Actions();
            }

            // The UI map, not the Player map: Escape is UI/Cancel in
            // InputSystem_Actions, and the player's own map is switched off the moment
            // they are shot.
            _input.UI.Enable();
        }

        private void OnDisable()
        {
            _input.UI.Disable();

            if (!_open)
            {
                return;
            }

            // Something took this avatar out from under an open panel — the raid was
            // restarted, or the host ended it. The cursor stays free on purpose: what
            // comes next is the lobby or a fresh level, and both want a mouse. Locking
            // it here is what left the lobby with no cursor on 2026-08-05.
            _open = false;
            if (panel != null)
            {
                panel.SetActive(false);
            }

            ShowCursor(true);
        }

        private void OnDestroy()
        {
            // Null after a domain reload if this instance stayed disabled throughout
            // (LocalAvatar keeps it off on every avatar but ours), so OnEnable never
            // rebuilt it.
            if (_input != null)
            {
                _input.Dispose();
            }
        }

        /// <summary>Bound by LevelBootstrapper, like the rest of the HUD: the goal is a scene object.</summary>
        public void Bind(LevelGoal goal)
        {
            _goal = goal;
        }

        private void Update()
        {
            // No goal means this avatar is not in a raid — it is standing in the hub,
            // where Escape belongs to HubMenuUI. Two panels on one key would also make
            // two of them write the cursor.
            // Unity object: a destroyed one compares == null but is not a real null.
            if (_goal == null)
            {
                return;
            }

            var won = _goal.IsWon;
            var lost = _goal.IsLost;

            if (won || lost)
            {
                // Terminal: the raid does not un-end, so this cannot be dismissed.
                Summarise();
                SetOpen(true);
                Paint(won ? "RAID COMPLETE" : "RAID FAILED", true);
                return;
            }

            if (_input.UI.Cancel.WasPressedThisFrame())
            {
                SetOpen(!_open);
            }

            if (_open)
            {
                Paint("LEAVE RAID?", false);
            }
        }

        private void SetOpen(bool open)
        {
            if (_open == open)
            {
                return;
            }

            _open = open;

            if (panel != null)
            {
                panel.SetActive(open);
            }

            // Otherwise the mouse aims the camera while it is also clicking the buttons,
            // and Interact still reaches through the panel (reported 2026-08-05). The
            // raid itself does not stop — it cannot, three other people are in it — this
            // player simply stops steering.
            if (open)
            {
                Suspend();
            }
            else
            {
                Restore();
            }

            // The cursor is one per process. LocalAvatar locks it when this avatar is
            // claimed; this is the only other writer, and it hands it straight back.
            ShowCursor(open);
        }

        private void Suspend()
        {
            _suspended.Clear();

            if (suspendWhileOpen == null)
            {
                return;
            }

            for (var i = 0; i < suspendWhileOpen.Length; i++)
            {
                // Unity object: a destroyed one compares == null but is not a real null.
                if (suspendWhileOpen[i] == null || !suspendWhileOpen[i].enabled)
                {
                    continue;
                }

                suspendWhileOpen[i].enabled = false;
                _suspended.Add(suspendWhileOpen[i]);
            }
        }

        private void Restore()
        {
            for (var i = 0; i < _suspended.Count; i++)
            {
                if (_suspended[i] != null)
                {
                    _suspended[i].enabled = true;
                }
            }

            _suspended.Clear();
        }

        // Which buttons a raid in this state offers. The hub belongs to the host and
        // only once the raid is over; Resume only while there is something to go back
        // to; Leaving is everybody's, always — that is the whole "exit at any moment".
        private void Paint(string text, bool over)
        {
            var session = RaidSession.Active;

            if (resumeButton != null)
            {
                resumeButton.gameObject.SetActive(!over);
            }

            if (hubButton != null)
            {
                hubButton.gameObject.SetActive(over && session != null && session.IsHost);
            }

            if (leaveButton != null)
            {
                leaveButton.gameObject.SetActive(session != null);
            }

            // Assigning Text.text rebuilds the mesh, so only touch it when the line
            // actually changed rather than every frame.
            if (title == null || _shownTitle == text)
            {
                return;
            }

            _shownTitle = text;
            title.text = text;
        }

        private void Resume()
        {
            SetOpen(false);
        }

        // Counted once, when the raid ends: Old Man goes on shooting under the panel,
        // and a survivor count that keeps dropping behind "RAID FAILED" is noise.
        private void Summarise()
        {
            if (_summarised || stats == null)
            {
                return;
            }

            _summarised = true;

            var players = 0;
            var living = 0;
            var boot = LevelBootstrapper.Current;
            if (boot != null)
            {
                var sensed = boot.SensedPlayers;
                for (var i = 0; i < sensed.Count; i++)
                {
                    players++;
                    if (sensed[i].IsAlive)
                    {
                        living++;
                    }
                }
            }

            var seconds = Mathf.Max(0f, _goal.Duration);
            stats.text = $"Животных на борту: {_goal.Delivered}/{_goal.Total}\n" +
                         $"Выжило: {living}/{players}\n" +
                         $"Время: {(int)(seconds / 60f)}:{(int)(seconds % 60f):00}";

            stats.gameObject.SetActive(true);
        }

        private void ToHub()
        {
            var session = RaidSession.Active;
            if (session != null)
            {
                // A real scene load, so nothing has to be reset: the outcome, the
                // delivered count, who is dead and which doors are open are all scene
                // state, and the next flight builds them from scratch.
                session.GoToHub();
            }
        }

        private void Leave()
        {
            var session = RaidSession.Active;
            if (session == null)
            {
                return;
            }

            // The panel goes, the cursor stays: the menu is a mouse screen, and the
            // avatar that would otherwise hand the cursor back is about to be destroyed.
            _open = false;
            if (panel != null)
            {
                panel.SetActive(false);
            }

            ShowCursor(true);

            // Host migration does not exist, so the host walking out ends the session
            // and everybody else finds themselves in the menu through their own
            // disconnect. Anybody else leaves alone.
            session.Leave();
        }

        private static void ShowCursor(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}

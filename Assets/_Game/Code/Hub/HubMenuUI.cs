using System.Collections.Generic;
using _Game.Code.App;
using _Game.Code.Player;
using UnityEngine;
using UnityEngine.UI;

using UnityScenes = UnityEngine.SceneManagement.SceneManager;

namespace _Game.Code.Hub
{
    /// <summary>
    /// The hub's menu: where the host picks the next location and anybody can leave the
    /// session. Escape opens it, the same key and the same shape as EndScreenUI in a
    /// raid, so there is one way to reach a menu wherever the player is standing.
    ///
    /// Lives on the hub's own canvas rather than on the avatar's HUD: the locations are
    /// hub content, and the avatar is spawned and cannot be wired to them in the editor.
    /// </summary>
    public class HubMenuUI : MonoBehaviour
    {
        [Tooltip("Where the saucer can fly. The scene name must be in the build list.")]
        [SerializeField] private RaidLocation[] locations;

        [Tooltip("Switched on with Escape. Hidden with SetActive rather than alpha: an " +
                 "invisible panel still eats clicks.")]
        [SerializeField] private GameObject panel;

        [SerializeField] private Text title;

        [Tooltip("The host's own address, for reading out to the other players.")]
        [SerializeField] private Text addressLabel;

        [Tooltip("Always on screen, panel open or not: it is the only thing that says " +
                 "the menu exists.")]
        [SerializeField] private Text hint;

        [SerializeField] private RectTransform locationList;

        [Tooltip("One row of the location list. Its Text is filled in from the entry.")]
        [SerializeField] private Button locationButtonPrefab;

        [SerializeField] private Button leaveButton;

        // Exactly the ones this panel switched off, so closing it cannot switch on
        // something that was already off.
        private readonly List<Behaviour> _suspended = new List<Behaviour>();
        private readonly List<Button> _rows = new List<Button>();

        private GameObject _avatar;
        private InputSystem_Actions _input;
        private bool _open;

        // Nobody to ask when playing the hub on its own, and then the answer is yes:
        // a null session means standalone, not failure.
        private static bool CanChoose => RaidSession.Active == null || RaidSession.Active.IsHost;

        private void Awake()
        {
            _input = new InputSystem_Actions();

            // Never trust what the panel was saved as.
            if (panel != null)
            {
                panel.SetActive(false);
            }

            if (leaveButton != null)
            {
                leaveButton.onClick.AddListener(Leave);
            }
        }

        private void OnEnable()
        {
            // A domain reload re-runs OnEnable on a live instance without re-running
            // Awake, and _input is not serialized.
            if (_input == null)
            {
                _input = new InputSystem_Actions();
            }

            // The UI map, not the Player map: Escape is UI/Cancel.
            _input.UI.Enable();
        }

        // Not Awake: RaidSession is created by the Loading scene's bootstrapper and the
        // host's server is up before this scene is, so hosting is settled by now.
        private void Start()
        {
            BuildRows();
            Paint();
        }

        private void Update()
        {
            if (_input.UI.Cancel.WasPressedThisFrame())
            {
                SetOpen(!_open);
            }
        }

        private void OnDisable()
        {
            _input.UI.Disable();

            if (!_open)
            {
                return;
            }

            // Flying out from under an open panel. The cursor stays free on purpose:
            // what comes next is a level, whose avatar takes it back in LocalAvatar.
            _open = false;
            if (panel != null)
            {
                panel.SetActive(false);
            }

            ShowCursor(true);
        }

        private void OnDestroy()
        {
            if (leaveButton != null)
            {
                leaveButton.onClick.RemoveAllListeners();
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    _rows[i].onClick.RemoveAllListeners();
                }
            }

            // Null after a domain reload if this instance stayed disabled throughout.
            if (_input != null)
            {
                _input.Dispose();
            }
        }

        /// <summary>
        /// The local avatar, handed over by <see cref="HubBootstrapper"/> once it is
        /// claimed. Null clears it — the avatar can go away before this panel does.
        /// </summary>
        public void Bind(GameObject avatar)
        {
            _avatar = avatar;

            if (avatar == null)
            {
                _suspended.Clear();
            }
        }

        private void BuildRows()
        {
            if (locations == null || locationList == null || locationButtonPrefab == null || !CanChoose)
            {
                return;
            }

            for (var i = 0; i < locations.Length; i++)
            {
                var location = locations[i];
                if (string.IsNullOrEmpty(location.SceneName))
                {
                    Debug.LogError($"HubMenuUI: location \"{location.DisplayName}\" names no scene, skipped.");
                    continue;
                }

                var row = Instantiate(locationButtonPrefab, locationList);
                var label = row.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = location.DisplayName;
                }

                // Closure over a copy, in setup code: the list is built once and never
                // per frame.
                row.onClick.AddListener(() => Fly(location));
                _rows.Add(row);
            }
        }

        private void Paint()
        {
            var session = RaidSession.Active;
            var hosting = session != null && session.IsHost;

            if (title != null)
            {
                title.text = CanChoose ? "ВЫБЕРИТЕ ЛОКАЦИЮ" : "ЖДЁМ ХОСТА";
            }

            if (addressLabel != null)
            {
                addressLabel.text = hosting ? $"Ваш адрес: {session.LocalEndpoint}" : string.Empty;
                addressLabel.gameObject.SetActive(hosting);
            }

            if (leaveButton != null)
            {
                leaveButton.gameObject.SetActive(session != null);
            }
        }

        private void Fly(RaidLocation location)
        {
            var session = RaidSession.Active;
            if (session == null)
            {
                // The hub played on its own: there is nobody to carry the load to.
                UnityScenes.LoadScene(location.SceneName);
                return;
            }

            session.StartRaid(location.SceneName);
        }

        private void Leave()
        {
            var session = RaidSession.Active;
            if (session == null)
            {
                return;
            }

            // The panel goes, the cursor stays: the menu is a mouse screen.
            _open = false;
            if (panel != null)
            {
                panel.SetActive(false);
            }

            ShowCursor(true);
            session.Leave();
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

            if (hint != null)
            {
                hint.gameObject.SetActive(!open);
            }

            // Otherwise the mouse aims the camera while it is also clicking the buttons.
            if (open)
            {
                Suspend();
                Paint();
            }
            else
            {
                Restore();
            }

            // The cursor is one per process; CLAUDE.md names every writer of it.
            ShowCursor(open);
        }

        private void Suspend()
        {
            _suspended.Clear();

            // Unity object: a destroyed one compares == null but is not a real null.
            if (_avatar == null)
            {
                return;
            }

            SuspendOne(_avatar.GetComponent<PlayerController>());
            SuspendOne(_avatar.GetComponent<PlayerInteractor>());
        }

        private void SuspendOne(Behaviour behaviour)
        {
            if (behaviour == null || !behaviour.enabled)
            {
                return;
            }

            behaviour.enabled = false;
            _suspended.Add(behaviour);
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

        private static void ShowCursor(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}

using System.Collections.Generic;
using _Game.Code.App;
using UnityEngine;
using UnityEngine.UI;

using UnityScenes = UnityEngine.SceneManagement.SceneManager;

namespace _Game.Code.Hub
{
    /// <summary>
    /// The hub's map: where the host picks the next location and anybody can leave the
    /// session. It is always on and stands in the room — walking up to a row and pressing
    /// Interact is the whole interaction, and PlayerInteractor resolves it like a door.
    ///
    /// Lives on the hub's own canvas rather than on the avatar's HUD: the locations are
    /// hub content, and the avatar is spawned and cannot be wired to them in the editor.
    /// </summary>
    public class HubMenuUI : MonoBehaviour
    {
        [Tooltip("Where the saucer can fly. The scene name must be in the build list.")]
        [SerializeField] private RaidLocation[] locations;

        [Tooltip("The wall's face. Always on: the wall is the menu, not a screen that " +
                 "opens over one.")]
        [SerializeField] private GameObject panel;

        [SerializeField] private Text title;

        [Tooltip("The host's own address, for reading out to the other players.")]
        [SerializeField] private Text addressLabel;

        [SerializeField] private RectTransform locationList;

        [Tooltip("One row of the location list. Its Text is filled in from the entry.")]
        [SerializeField] private Button locationButtonPrefab;

        [SerializeField] private Button leaveButton;

        private readonly List<Button> _rows = new List<Button>();

        // Nobody to ask when playing the hub on its own, and then the answer is yes:
        // a null session means standalone, not failure.
        private static bool CanChoose => RaidSession.Active == null || RaidSession.Active.IsHost;

        private void Awake()
        {
            // Never trust what the panel was saved as.
            if (panel != null)
            {
                panel.SetActive(true);
            }

            if (leaveButton != null)
            {
                leaveButton.onClick.AddListener(Leave);
            }
        }

        // Not Awake: RaidSession is created by the Loading scene's bootstrapper and the
        // host's server is up before this scene is, so hosting is settled by now.
        private void Start()
        {
            BuildRows();
            Paint();
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

            // The cursor is handed back free: what comes next is the menu, which
            // wants a mouse. CLAUDE.md names every writer of Cursor.lockState.
            ShowCursor(true);
            session.Leave();
        }

        private static void ShowCursor(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}

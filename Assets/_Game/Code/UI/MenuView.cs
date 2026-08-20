using System.Collections.Generic;
using _Game.Code.App;
using UnityEngine;
using UnityEngine.UI;

namespace _Game.Code.UI
{
    /// <summary>
    /// The way into a session and nothing else: host or join, then the hub takes over.
    /// Who is connected and where they fly are the hub's questions, not this screen's.
    /// </summary>
    public class MenuView : MonoBehaviour
    {
        private enum Panel
        {
            Lobby,
            Settings,
            Main
        }

        /// <summary>
        /// Where we are inside the lobby panel. Two steps rather than two panels that
        /// happen to overlap: both Back buttons sat on exactly the same rectangle and
        /// the click went to the one on top, which was wired to nothing.
        /// </summary>
        private enum LobbyStep
        {
            Options,
            Address,
            Search
        }

        [SerializeField] private Button _playButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _exitButton;
        [SerializeField] private Button _backLobbyButton;
        [SerializeField] private Button _backSettingsButton;
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _connectButton;
        [SerializeField] private CanvasGroup _mainMenuPanel;
        [SerializeField] private CanvasGroup _lobbyPanel;
        [SerializeField] private CanvasGroup _settingsPanel;

        [Tooltip("Where the host's address is typed. Over Tailscale that is their " +
                 "tailnet IP — no relay, no matchmaking.")]
        [SerializeField] private InputField _addressField;

        [Header("Lobby steps")]
        [Tooltip("Host / Connect. LobbyPanel/Options.")]
        [SerializeField] private GameObject _optionsPanel;

        [Tooltip("Where ip:port is typed, shown after Connect.")]
        [SerializeField] private GameObject _addressPanel;

        [Tooltip("The old player list. Kept only to be switched off — an invisible " +
                 "panel left active still eats clicks.")]
        [SerializeField] private GameObject _playersPanel;

        [Tooltip("Confirms the typed address. The Connect button in Options only opens " +
                 "the address step; this one dials.")]
        [SerializeField] private Button _connectConfirmButton;

        [Tooltip("Back from the address step without dialling. Without it that step is a " +
                 "dead end: the only way out would be a successful connection.")]
        [SerializeField] private Button _backAddressButton;

        [Tooltip("Shown while an attempt is in flight. The transport takes about five " +
                 "seconds to admit failure, and a button that looks dead for five " +
                 "seconds reads as a bug.")]
        [SerializeField] private GameObject _connectingIndicator;

        [Header("Search")]
        [Tooltip("Opens the search step from Options.")]
        [SerializeField] private Button _searchButton;

        [Tooltip("The list of hosts found on the LAN. LobbyPanel/Search.")]
        [SerializeField] private GameObject _searchPanel;

        [Tooltip("Back from the search step. Leaving it stops the beacon listener.")]
        [SerializeField] private Button _backSearchButton;

        [Tooltip("One row per host. Needs a Button on the root and a Text anywhere under it.")]
        [SerializeField] private Button _sessionRowPrefab;

        [Tooltip("Rows are instantiated under this — the content object of the scroll view.")]
        [SerializeField] private Transform _sessionRowContainer;

        [Tooltip("Says whether anything was heard. An empty list with no line reads as a " +
                 "broken screen, and a blocked broadcast is exactly that case.")]
        [SerializeField] private Text _searchStatusText;

        [Header("Popup")]
        [SerializeField] private GameObject _popup;
        [SerializeField] private Text _popupText;
        [SerializeField] private Button _popupOkButton;

        private readonly Dictionary<Panel, CanvasGroup> _panels = new();

        private readonly List<Button> _sessionRows = new();

        private RaidSession _session;

        private LanDiscovery _discovery;

        // Where a failed attempt puts the player back — the step the dial came from.
        private LobbyStep _dialledFrom = LobbyStep.Address;

        /// <summary>
        /// The menu while it is on screen. Read by <see cref="LoadingScreen"/> as "is
        /// the main menu up" — SceneManager.sceneLoaded does not answer that, it never
        /// fires for a scene FishNet skips as already loaded.
        /// </summary>
        public static MenuView Current { get; private set; }

        private void Awake()
        {
            Current = this;

            _playButton.onClick.AddListener(OpenLobby);
            _settingsButton.onClick.AddListener(OpenSettings);
            _exitButton.onClick.AddListener(Exit);
            _backLobbyButton.onClick.AddListener(OpenMainMenu);
            _backSettingsButton.onClick.AddListener(OpenMainMenu);
            _hostButton.onClick.AddListener(Host);
            _connectButton.onClick.AddListener(OpenAddressStep);

            if (_connectConfirmButton != null)
            {
                _connectConfirmButton.onClick.AddListener(Connect);
            }

            if (_backAddressButton != null)
            {
                _backAddressButton.onClick.AddListener(BackFromAddress);
            }

            if (_searchButton != null)
            {
                _searchButton.onClick.AddListener(OpenSearchStep);
            }

            if (_backSearchButton != null)
            {
                _backSearchButton.onClick.AddListener(BackFromSearch);
            }

            if (_popupOkButton != null)
            {
                _popupOkButton.onClick.AddListener(ClosePopup);
            }

            InitializePanels();

            // The starting panel is decided here and not by whatever alpha the scene
            // was last saved with. Without this the scene's own values won — LobbyPanel
            // was saved at alpha 1 and the menu at 0, so the game opened on the lobby —
            // and, worse, every panel still blocked raycasts until the first switch, so
            // an invisible menu was catching clicks.
            OpenMainMenu();
            ShowStep(LobbyStep.Options);
            ClosePopup();

            // Same reason as the panel above: authored visible in the scene, and would
            // otherwise stay that way until something happened to change it.
            ShowConnecting(false);

            // Why the last session ended, if it ended on its own. It is collected here
            // rather than delivered by an event because the thing that ended it happened
            // a scene ago — there was nothing on screen then that could have said it,
            // and ClosePopup above would have wiped it anyway. Consumed, so it is shown
            // once.
            var notice = RaidSession.Active == null ? null : RaidSession.Active.ConsumeNotice();
            if (!string.IsNullOrEmpty(notice))
            {
                ShowPopup(notice);
            }
        }

        private void OnDestroy()
        {
            _playButton.onClick.RemoveAllListeners();
            _settingsButton.onClick.RemoveAllListeners();
            _exitButton.onClick.RemoveAllListeners();
            _backLobbyButton.onClick.RemoveAllListeners();
            _backSettingsButton.onClick.RemoveAllListeners();
            _hostButton.onClick.RemoveAllListeners();
            _connectButton.onClick.RemoveAllListeners();

            if (_connectConfirmButton != null)
            {
                _connectConfirmButton.onClick.RemoveAllListeners();
            }

            if (_backAddressButton != null)
            {
                _backAddressButton.onClick.RemoveAllListeners();
            }

            if (_searchButton != null)
            {
                _searchButton.onClick.RemoveAllListeners();
            }

            if (_backSearchButton != null)
            {
                _backSearchButton.onClick.RemoveAllListeners();
            }

            if (_popupOkButton != null)
            {
                _popupOkButton.onClick.RemoveAllListeners();
            }

            UnsubscribeSession();
            StopSearching();

            if (Current == this)
            {
                Current = null;
            }
        }

        private void Host()
        {
            var session = RequireSession();
            if (session == null)
            {
                return;
            }

            SubscribeSession(session);
            _dialledFrom = LobbyStep.Options;
            if (session.Host())
            {
                ShowConnecting(true);
            }
        }

        private void OpenAddressStep()
        {
            ShowStep(LobbyStep.Address);
        }

        // Nothing to disconnect here — this step is reached before dialling.
        private void BackFromAddress()
        {
            ShowStep(LobbyStep.Options);
        }

        private void OpenSearchStep()
        {
            ShowStep(LobbyStep.Search);
        }

        private void BackFromSearch()
        {
            ShowStep(LobbyStep.Options);
        }

        private void JoinFound(string endpoint)
        {
            var session = RequireSession();
            if (session == null)
            {
                return;
            }

            SubscribeSession(session);
            _dialledFrom = LobbyStep.Search;
            if (!session.Join(endpoint))
            {
                return;
            }

            ShowConnecting(true);

            // Stops the rows repainting under the cursor, so a second row cannot start a
            // second attempt on top of the first.
            StopSearching();
        }

        private void Connect()
        {
            var session = RequireSession();
            if (session == null)
            {
                return;
            }

            SubscribeSession(session);
            _dialledFrom = LobbyStep.Address;
            if (session.Join(_addressField == null ? string.Empty : _addressField.text))
            {
                ShowConnecting(true);
            }
        }

        private RaidSession RequireSession()
        {
            if (RaidSession.Active == null)
            {
                Debug.LogError("MenuView: no RaidSession — the Loading scene did not run.");
                ShowPopup("Сессия не запущена");
                return null;
            }

            return RaidSession.Active;
        }

        private void SubscribeSession(RaidSession session)
        {
            if (_session == session)
            {
                return;
            }

            UnsubscribeSession();
            _session = session;
            _session.Connected += OnConnected;
            _session.ConnectFailed += OnConnectFailed;
        }

        private void UnsubscribeSession()
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (_session == null)
            {
                return;
            }

            _session.Connected -= OnConnected;
            _session.ConnectFailed -= OnConnectFailed;
            _session = null;
        }

        // The host takes everybody to the hub. A client does nothing: FishNet sends a
        // newly authenticated connection the global scenes that are already loaded,
        // with the parameters of the load that put them there, so the hub and the
        // loading screen both arrive on their own.
        private void OnConnected()
        {
            ShowConnecting(false);

            if (RaidSession.Active != null && RaidSession.Active.IsHost)
            {
                RaidSession.Active.GoToHub();
            }
        }

        private void OnConnectFailed(string reason)
        {
            ShowConnecting(false);

            // Back to the step the attempt started from: the typed address stays
            // correctable, and a LAN row lands back on the list rather than on a form.
            ShowStep(_dialledFrom);
            ShowPopup(reason);
        }

        private void ShowStep(LobbyStep step)
        {
            if (_optionsPanel != null)
            {
                _optionsPanel.SetActive(step == LobbyStep.Options);
            }

            if (_addressPanel != null)
            {
                _addressPanel.SetActive(step == LobbyStep.Address);
            }

            if (_searchPanel != null)
            {
                _searchPanel.SetActive(step == LobbyStep.Search);
            }

            if (_playersPanel != null)
            {
                _playersPanel.SetActive(false);
            }

            // The listener is bound to the step, not to the panel: every way out of it —
            // Back, a row, a failed attempt, leaving the lobby — comes through here.
            if (step == LobbyStep.Search)
            {
                StartSearching();
            }
            else
            {
                StopSearching();
            }
        }

        private void StartSearching()
        {
            if (_discovery != null)
            {
                return;
            }

            if (LanDiscovery.Active == null)
            {
                Debug.LogError("MenuView: no LanDiscovery on the session object, nothing can be found.");
                if (_searchStatusText != null)
                {
                    _searchStatusText.text = "Поиск недоступен";
                }

                return;
            }

            _discovery = LanDiscovery.Active;
            _discovery.Changed += RepaintSessions;
            _discovery.StartSearch();
            RepaintSessions();
        }

        private void StopSearching()
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (_discovery == null)
            {
                return;
            }

            _discovery.Changed -= RepaintSessions;
            _discovery.StopSearch();
            _discovery = null;
            ClearSessionRows();
        }

        private void RepaintSessions()
        {
            ClearSessionRows();

            var sessions = _discovery == null ? null : _discovery.Sessions;
            var count = sessions == null ? 0 : sessions.Count;

            if (_searchStatusText != null)
            {
                _searchStatusText.text = count == 0 ? "Поиск сессий…" : $"Найдено: {count}";
            }

            if (_sessionRowPrefab == null || _sessionRowContainer == null)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var endpoint = sessions[i].Endpoint;
                // worldPositionStays false, or the row keeps the prefab's world rect and
                // the layout group under the container gets a torn first frame.
                var row = Instantiate(_sessionRowPrefab, _sessionRowContainer, false);
                var label = row.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = endpoint;
                }

                row.onClick.AddListener(() => JoinFound(endpoint));
                _sessionRows.Add(row);
            }
        }

        private void ClearSessionRows()
        {
            for (var i = 0; i < _sessionRows.Count; i++)
            {
                if (_sessionRows[i] != null)
                {
                    Destroy(_sessionRows[i].gameObject);
                }
            }

            _sessionRows.Clear();
        }

        private void ShowConnecting(bool connecting)
        {
            if (_connectingIndicator != null)
            {
                _connectingIndicator.SetActive(connecting);
            }

            // Both dial buttons, so a second click cannot start a second attempt on top
            // of the first.
            _hostButton.interactable = !connecting;
            _connectButton.interactable = !connecting;

            if (_connectConfirmButton != null)
            {
                _connectConfirmButton.interactable = !connecting;
            }
        }

        private void ShowPopup(string message)
        {
            if (_popupText != null)
            {
                _popupText.text = message;
            }

            if (_popup != null)
            {
                _popup.SetActive(true);
            }
            else
            {
                // No popup wired yet: say it somewhere rather than swallowing it.
                Debug.LogWarning($"MenuView: {message}");
            }
        }

        private void ClosePopup()
        {
            if (_popup != null)
            {
                _popup.SetActive(false);
            }
        }

        private void InitializePanels()
        {
            _panels.Add(Panel.Lobby, _lobbyPanel);
            _panels.Add(Panel.Settings, _settingsPanel);
            _panels.Add(Panel.Main, _mainMenuPanel);
        }

        // Resets the step as well: a hidden lobby panel would otherwise leave the beacon
        // listener bound with nothing on screen.
        private void OpenMainMenu()
        {
            SelectPanel(Panel.Main);
            ShowStep(LobbyStep.Options);
        }

        private void OpenLobby()
        {
            SelectPanel(Panel.Lobby);
        }

        private void OpenSettings()
        {
            SelectPanel(Panel.Settings);
        }

        private static void Exit()
        {
            Application.Quit();
        }

        private void SelectPanel(Panel selectedPanel)
        {
            foreach (var panel in _panels)
            {
                if (panel.Key == selectedPanel)
                {
                    panel.Value.alpha = 1;
                    panel.Value.interactable = true;
                    panel.Value.blocksRaycasts = true;
                }
                else
                {
                    panel.Value.alpha = 0;
                    panel.Value.interactable = false;
                    panel.Value.blocksRaycasts = false;
                }
            }
        }
    }
}

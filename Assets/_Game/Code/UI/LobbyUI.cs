using System.Collections.Generic;
using _Game.Code.App;
using FishNet;
using UnityEngine;
using UnityEngine.UI;

namespace _Game.Code.UI
{
    public class LobbyUI : MonoBehaviour
    {
        private enum Panel
        {
            Lobby,
            Settings,
            Main
        }

        /// <summary>
        /// Where we are inside the lobby panel. Three steps rather than three panels
        /// that happen to overlap: Options and the player list were both switched on at
        /// once, and their two Back buttons sat on exactly the same 200×70 rectangle —
        /// the click went to the one on top, which was wired to nothing (measured in
        /// Play mode, 2026-08-04).
        /// </summary>
        private enum LobbyStep
        {
            Options,
            Address,
            Players
        }
        
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _exitButton;
        [SerializeField] private Button _backLobbyButton;
        [SerializeField] private Button _backSettingsButton;
        [SerializeField] private Button _backOptionsButton;
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _connectButton;
        [SerializeField] private CanvasGroup _mainMenuPanel;
        [SerializeField] private CanvasGroup _lobbyPanel;
        [SerializeField] private CanvasGroup _settingsPanel;
        [SerializeField] private RectTransform _playerList;
        [SerializeField] private PlayerLobbyUI _playerLobbyUIPrefab;

        [Tooltip("Where the host's address is typed. Over Tailscale that is their " +
                 "tailnet IP — no relay, no matchmaking.")]
        [SerializeField] private InputField _addressField;

        [Tooltip("Starts the raid. Only the host has one that works, and only once " +
                 "everybody in the list is ready.")]
        [SerializeField] private Button _startButton;

        [Header("Lobby steps")]
        [Tooltip("Host / Connect. LobbyPanel/Options.")]
        [SerializeField] private GameObject _optionsPanel;

        [Tooltip("Where ip:port is typed, shown after Connect.")]
        [SerializeField] private GameObject _addressPanel;

        [Tooltip("The player list, shown once we are actually in a session. " +
                 "LobbyPanel/PanelAfterOptions.")]
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

        [Tooltip("Shows the host their own address so they can read it out. Only they " +
                 "have one worth showing — a client already knows what it typed.")]
        [SerializeField] private Text _hostAddressLabel;

        [Header("Popup")]
        [SerializeField] private GameObject _popup;
        [SerializeField] private Text _popupText;
        [SerializeField] private Button _popupOkButton;

        private readonly Dictionary<Panel, CanvasGroup> _panels = new();
        private readonly List<PlayerLobbyUI> _rows = new();

        private LobbyRoster _roster;
        private RaidSession _session;

        private void Awake()
        {
            _playButton.onClick.AddListener(OpenLobby);
            _settingsButton.onClick.AddListener(OpenSettings);
            _exitButton.onClick.AddListener(Exit);
            _backLobbyButton.onClick.AddListener(OpenMainMenu);
            _backSettingsButton.onClick.AddListener(OpenMainMenu);
            _hostButton.onClick.AddListener(Host);
            _connectButton.onClick.AddListener(OpenAddressStep);

            if (_startButton != null)
            {
                _startButton.onClick.AddListener(StartRaid);
            }

            if (_connectConfirmButton != null)
            {
                _connectConfirmButton.onClick.AddListener(Connect);
            }

            if (_backOptionsButton != null)
            {
                _backOptionsButton.onClick.AddListener(LeaveToOptions);
            }

            if (_backAddressButton != null)
            {
                _backAddressButton.onClick.AddListener(BackFromAddress);
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

            // Same reason as the panel above: these two are authored visible in the
            // scene and would otherwise stay that way until something happened to
            // change them — a "connecting" label on a screen that is not connecting,
            // and a Start button that works before there is a session to start.
            ShowConnecting(false);
            Repaint();

            // Why the last session ended, if it ended on its own. It is collected here
            // rather than delivered by an event because the thing that ended it happened
            // in the Level, a scene ago — there was nothing on screen then that could
            // have said it, and ClosePopup above would have wiped it anyway. Consumed,
            // so it is shown once.
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

            if (_startButton != null)
            {
                _startButton.onClick.RemoveAllListeners();
            }

            if (_connectConfirmButton != null)
            {
                _connectConfirmButton.onClick.RemoveAllListeners();
            }

            if (_backOptionsButton != null)
            {
                _backOptionsButton.onClick.RemoveAllListeners();
            }

            if (_backAddressButton != null)
            {
                _backAddressButton.onClick.RemoveAllListeners();
            }

            if (_popupOkButton != null)
            {
                _popupOkButton.onClick.RemoveAllListeners();
            }

            Unsubscribe();
            UnsubscribeSession();
        }

        // The roster is spawned by the host over the network, so it does not exist when
        // this scene loads and cannot be wired to in the editor. Watching for it here
        // is cheaper and less fragile than an event that has to survive a scene load;
        // it is one reference comparison a frame.
        private void Update()
        {
            var current = LobbyRoster.Current;
            if (current == _roster)
            {
                return;
            }

            Unsubscribe();
            _roster = current;

            if (_roster != null)
            {
                _roster.Changed += Repaint;
            }

            Repaint();
        }

        private void Unsubscribe()
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (_roster != null)
            {
                _roster.Changed -= Repaint;
            }
        }

        private void Host()
        {
            var session = RequireSession();
            if (session == null)
            {
                return;
            }

            // Straight to the list: hosting cannot "not be found", and the host's own
            // client is added to the roster like anybody else, so it lands there with
            // one row already in it.
            SubscribeSession(session);
            if (session.Host())
            {
                ShowStep(LobbyStep.Players);
                ShowConnecting(true);
            }
        }

        private void OpenAddressStep()
        {
            ShowStep(LobbyStep.Address);
        }

        // Nothing to disconnect here — this step is reached before dialling — so unlike
        // the way back from the player list it only changes the picture.
        private void BackFromAddress()
        {
            ShowStep(LobbyStep.Options);
        }

        private void Connect()
        {
            var session = RequireSession();
            if (session == null)
            {
                return;
            }

            SubscribeSession(session);
            if (session.Join(_addressField == null ? string.Empty : _addressField.text))
            {
                // Not to the list yet — being told "connected" is what moves us there.
                // Until then the attempt is in flight and has to look like it.
                ShowConnecting(true);
            }
        }

        /// <summary>
        /// Back out of the player list. It drops the connection rather than only
        /// changing the picture: staying in somebody's session while looking at the
        /// Host/Connect screen is a state with no way back out of it.
        /// </summary>
        private void LeaveToOptions()
        {
            if (RaidSession.Active != null)
            {
                RaidSession.Active.Leave();
            }

            ShowConnecting(false);
            ShowStep(LobbyStep.Options);
        }

        private RaidSession RequireSession()
        {
            if (RaidSession.Active == null)
            {
                Debug.LogError("LobbyUI: no RaidSession — the Loading scene did not run.");
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

        private void OnConnected()
        {
            ShowConnecting(false);
            ShowStep(LobbyStep.Players);
        }

        private void OnConnectFailed(string reason)
        {
            ShowConnecting(false);

            // Back to where the address was typed, so it can be corrected rather than
            // retyped from the start.
            ShowStep(LobbyStep.Address);
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

            if (_playersPanel != null)
            {
                _playersPanel.SetActive(step == LobbyStep.Players);
            }
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
                Debug.LogWarning($"LobbyUI: {message}");
            }
        }

        private void ClosePopup()
        {
            if (_popup != null)
            {
                _popup.SetActive(false);
            }
        }

        private void StartRaid()
        {
            // Asked again here rather than trusted from the last repaint: between the
            // button becoming available and the click, somebody can have un-readied or
            // left. The host is the only one this does anything on anyway.
            if (RaidSession.Active == null || _roster == null || !_roster.EveryoneReady())
            {
                return;
            }

            RaidSession.Active.StartRaid();
        }

        // The whole list, every time. It is at most four rows, and rebuilding is
        // simpler to be right about than patching one of them.
        private void Repaint()
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    Destroy(_rows[i].gameObject);
                }
            }

            _rows.Clear();

            var hosting = RaidSession.Active != null && RaidSession.Active.IsHost;

            if (_hostAddressLabel != null)
            {
                // Only while hosting, and read fresh rather than cached: the address can
                // change under the game if the machine reconnects to the network.
                _hostAddressLabel.text = hosting ? $"Ваш адрес: {RaidSession.Active.LocalEndpoint}" : string.Empty;
                _hostAddressLabel.gameObject.SetActive(hosting);
            }

            if (_startButton != null)
            {
                var canStart = _roster != null && RaidSession.Active != null &&
                               RaidSession.Active.IsHost && _roster.EveryoneReady();
                _startButton.gameObject.SetActive(_roster != null && RaidSession.Active != null &&
                                                  RaidSession.Active.IsHost);
                _startButton.interactable = canStart;
            }

            if (_roster == null || _playerLobbyUIPrefab == null || _playerList == null)
            {
                return;
            }

            var localId = InstanceFinder.ClientManager.Connection.ClientId;

            for (var i = 0; i < _roster.Slots.Count; i++)
            {
                var slot = _roster.Slots[i];
                var row = Instantiate(_playerLobbyUIPrefab, _playerList);
                row.Show(slot, slot.ClientId == localId);
                _rows.Add(row);
            }
        }

        private void InitializePanels()
        {
            _panels.Add(Panel.Lobby, _lobbyPanel);
            _panels.Add(Panel.Settings, _settingsPanel);
            _panels.Add(Panel.Main, _mainMenuPanel);
        }

        private void OpenMainMenu()
        {
            SelectPanel(Panel.Main);
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
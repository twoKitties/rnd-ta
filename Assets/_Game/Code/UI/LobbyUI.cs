using System.Collections.Generic;
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
        
        private readonly Dictionary<Panel, CanvasGroup> _panels = new();

        private void Awake()
        {
            _playButton.onClick.AddListener(OpenLobby);
            _settingsButton.onClick.AddListener(OpenSettings);
            _exitButton.onClick.AddListener(Exit);
            _backLobbyButton.onClick.AddListener(OpenMainMenu);
            _backSettingsButton.onClick.AddListener(OpenMainMenu);
            InitializePanels();
        }

        private void OnDestroy()
        {
            _playButton.onClick.RemoveAllListeners();
            _settingsButton.onClick.RemoveAllListeners();
            _exitButton.onClick.RemoveAllListeners();
            _backLobbyButton.onClick.RemoveAllListeners();
            _backSettingsButton.onClick.RemoveAllListeners();
        }

        private void InitializePanels()
        {
            _panels.Add(Panel.Lobby, _mainMenuPanel);
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
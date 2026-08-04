using _Game.Code.App;
using UnityEngine;
using UnityEngine.UI;

namespace _Game.Code.UI
{
    /// <summary>
    /// One row of the lobby list: a name and whether that player is ready.
    ///
    /// Only our own row is editable. Somebody else's name and readiness are shared
    /// state owned by the host — they arrive over the wire and are shown, never typed
    /// into here (MECHANICS.md 7.4).
    /// </summary>
    public class PlayerLobbyUI : MonoBehaviour
    {
        [SerializeField] private InputField _inputField;
        [SerializeField] private Button _readyButton;

        private int _clientId;
        private bool _isLocal;
        private bool _ready;

        private void Awake()
        {
            _readyButton.onClick.AddListener(ToggleReady);
            _inputField.onEndEdit.AddListener(SubmitName);
        }

        private void OnDestroy()
        {
            _readyButton.onClick.RemoveAllListeners();
            _inputField.onEndEdit.RemoveAllListeners();
        }

        /// <summary>
        /// Paint this row. <paramref name="isLocal"/> is what decides whether the two
        /// controls do anything: a row is a request form for its owner and a read-out
        /// for everybody else.
        /// </summary>
        public void Show(LobbySlot slot, bool isLocal)
        {
            _clientId = slot.ClientId;
            _isLocal = isLocal;
            _ready = slot.Ready;

            // Not while it is being typed into: overwriting the field on every roster
            // change would eat the letters as they are entered.
            if (!_inputField.isFocused)
            {
                _inputField.text = slot.Name;
            }

            _inputField.interactable = isLocal;
            _readyButton.interactable = isLocal;

            var label = _readyButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = slot.Ready ? "Готов" : "Не готов";
            }
        }

        private void ToggleReady()
        {
            if (!_isLocal || LobbyRoster.Current == null)
            {
                return;
            }

            // A request, not a fact: the host writes the roster, and it is the
            // connection this arrives on that names the slot — not _clientId, which is
            // only here so the row knows what it is showing.
            LobbyRoster.Current.RequestReady(!_ready);
        }

        private void SubmitName(string name)
        {
            if (!_isLocal || LobbyRoster.Current == null)
            {
                return;
            }

            LobbyRoster.Current.RequestName(name);
        }
    }
}

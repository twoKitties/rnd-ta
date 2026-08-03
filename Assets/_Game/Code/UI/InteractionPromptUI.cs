using _Game.Code.Player;
using UnityEngine;
using UnityEngine.UI;

namespace _Game.Code.UI
{
    /// <summary>
    /// Shows what Interact would do right now — "[E] Grab Kitty", "[E] Open door" —
    /// and hides itself when there is nothing in front of the player.
    ///
    /// Lives inside the avatar, like the camera does, so every player carries their
    /// own HUD. When netcode lands, switch it on where FirstPersonBody is switched
    /// on: only for the local avatar.
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        [SerializeField] private PlayerController controller;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private Text label;

        private string _key;
        private string _shown;

        private void Start()
        {
            // The button's name is a UI concern, which is why it is read here and not
            // in the interactor (MECHANICS.md 7.1). Read once: rebinding at runtime
            // does not exist today.
            _key = controller == null ? string.Empty : controller.InteractDisplayName;
            Show(string.Empty);
        }

        private void Update()
        {
            if (interactor == null || label == null)
            {
                return;
            }

            var target = interactor.Current;
            Show(target.Exists ? $"[{_key}] {target.Verb} {target.Subject}" : string.Empty);
        }

        private void Show(string text)
        {
            if (_shown == text)
            {
                return;
            }

            // Assigning Text.text rebuilds the mesh, so only touch it when the line
            // actually changed rather than every frame.
            _shown = text;
            label.text = text;
            label.gameObject.SetActive(text.Length > 0);
        }
    }
}

using FishNet;
using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.UI;

namespace _Game.Code.UI
{
    /// <summary>
    /// Covers a transition. One per launch, kept across scenes by
    /// <see cref="App.Bootstrapper"/>: it is shown in one scene and hidden after
    /// another has loaded.
    ///
    /// Told what to wait for, not when to stop — there is no public Hide. Restart
    /// queues Lobby then Level and the server does not wait for clients between queued
    /// operations, so hiding on "a scene loaded" would show the main menu mid-restart.
    ///
    /// Screen Space – Overlay draws without a camera, which is why this survives the
    /// camera-less Loading scene and the gap between levels. Does not touch
    /// <see cref="Cursor"/> — see CLAUDE.md, there are exactly two writers of it.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        /// <summary>Travels as one byte in <see cref="LoadParams.ClientParams"/>, so values are pinned.</summary>
        public enum Wait : byte
        {
            MainMenu = 0,
            RaidReady = 1
        }

        [SerializeField] private Image _loadingIcon;
        [SerializeField] private CanvasGroup _canvasGroup;

        [Tooltip("Degrees per second. Was per frame until 2026-08-05 — a leftover 15 here " +
                 "means one turn every 24 s.")]
        [SerializeField] private float _loadingIconRotationSpeed = 300f;

        [Tooltip("Seconds, unscaled.")]
        [SerializeField] private float _fadeDuration = 0.35f;

        [Tooltip("Seconds before uncovering anyway. Only uncovers — session recovery is " +
                 "RaidSession's, and its backstops are shorter.")]
        [SerializeField] private float _stuckTimeout = 30f;

        /// <summary>Null when there is none: pressing Play straight into Level never creates one.</summary>
        public static LoadingScreen Active { get; private set; }

        private Wait _waitingFor;
        private bool _showing;
        private bool _raidReady;
        private float _deadline;

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(gameObject);
                return;
            }

            Active = this;

            // Only reachable on launch, and launch is followed by the main menu.
            Show(Wait.MainMenu);
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        private void OnEnable()
        {
            // No networking is a supported mode; then there is nothing to subscribe to.
            if (InstanceFinder.NetworkManager == null)
            {
                return;
            }

            InstanceFinder.SceneManager.OnLoadStart += OnLoadStart;
        }

        private void OnDisable()
        {
            if (InstanceFinder.NetworkManager == null)
            {
                return;
            }

            InstanceFinder.SceneManager.OnLoadStart -= OnLoadStart;
        }

        /// <summary>
        /// Cover until <paramref name="what"/> happens. Waiting for something already
        /// true is refused — that is how the screen gets stuck up for good.
        /// </summary>
        public void Show(Wait what)
        {
            if (Satisfied(what))
            {
                return;
            }

            _waitingFor = what;
            _raidReady = false;
            _showing = true;
            _deadline = Time.unscaledTime + _stuckTimeout;

            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
        }

        /// <summary>Our avatar has arrived: the raid is playable, not merely loaded.</summary>
        public void RaidReady()
        {
            _raidReady = true;
        }

        // Fires on every peer, which is the point: a client never calls StartRaid.
        private void OnLoadStart(SceneLoadStartEventArgs args)
        {
            var stamp = args.QueueData.SceneLoadData.Params.ClientParams;
            if (stamp == null || stamp.Length == 0)
            {
                return;
            }

            Show((Wait)stamp[0]);
        }

        // LobbyUI.Current, not SceneManager.sceneLoaded: FishNet raises OnLoadStart even
        // for a scene it then skips as already loaded, and no sceneLoaded follows.
        private bool Satisfied(Wait what)
        {
            return what == Wait.MainMenu ? LobbyUI.Current != null : _raidReady;
        }

        private void Update()
        {
            if (_showing && (Satisfied(_waitingFor) || Overdue()))
            {
                _showing = false;

                // Released at the start of the fade: a visible panel that eats clicks is
                // a bug this project has hit twice.
                _canvasGroup.blocksRaycasts = false;
            }

            if (_canvasGroup.alpha <= 0f)
            {
                return;
            }

            // Unscaled: during a load the frame rate is the thing not to trust.
            _loadingIcon.transform.Rotate(0f, 0f, _loadingIconRotationSpeed * Time.unscaledDeltaTime);

            if (_showing)
            {
                return;
            }

            _canvasGroup.alpha = Mathf.Max(0f,
                _canvasGroup.alpha - Time.unscaledDeltaTime / Mathf.Max(0.01f, _fadeDuration));
        }

        private bool Overdue()
        {
            if (Time.unscaledTime < _deadline)
            {
                return false;
            }

            Debug.LogWarning($"LoadingScreen: {_waitingFor} never happened within {_stuckTimeout} s.");
            return true;
        }
    }
}

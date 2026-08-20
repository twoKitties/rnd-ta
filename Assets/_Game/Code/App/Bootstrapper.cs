using _Game.Code.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace _Game.Code.App
{
    /// <summary>
    /// The application's entry point, and the first thing in the build: it lives in
    /// the Loading scene, which holds nothing else.
    ///
    /// Its whole job is to make sure the things that must outlive a scene exist, and
    /// then hand over to the menu. Not to be confused with LevelBootstrapper, which
    /// wires up one raid and dies with it, while this runs once per launch.
    ///
    /// The network manager is instantiated here rather than placed in the Menu scene
    /// because it has to survive Menu → Hub → Level → Hub. Guarded on "does one already
    /// exist" so that pressing Play straight into any scene during development still
    /// works and still ends up with exactly one.
    /// </summary>
    public class Bootstrapper : MonoBehaviour
    {
        [Tooltip("Loaded once everything below exists: the menu, where connecting happens.")]
        [SerializeField] private string firstScene = "Menu";

        [Tooltip("Instantiated once per launch and kept for the whole session. Leave " +
                 "empty to run with no networking at all — single player still works.")]
        [SerializeField] private GameObject sessionPrefab;

        [Tooltip("Covers every transition for the whole launch. Kept alive across scenes " +
                 "because it is shown in one and hidden after another has loaded.")]
        [SerializeField] private GameObject loadingScreenPrefab;

        private static bool _sessionExists;

        private void Start()
        {
            EnsureSession();

            // After the session: Instantiate runs OnEnable synchronously, and the screen
            // subscribes to the network's scene manager there.
            EnsureLoadingScreen();

            // Start, not Awake: loading a scene out of Awake runs while this scene is
            // still being built.
            if (!string.IsNullOrEmpty(firstScene) && SceneManager.GetActiveScene().name != firstScene)
            {
                SceneManager.LoadScene(firstScene);
            }
        }

        /// <summary>
        /// Creates the session object if this launch has not created one. Public so that
        /// entering Play mode in the middle of the game — which is how this project is
        /// actually tested — can ask for the same thing without routing through the
        /// Loading scene.
        /// </summary>
        public void EnsureSession()
        {
            // Unity object: a destroyed one compares == null but is not a real null,
            // so `?.` and `??` would lie about it.
            if (_sessionExists || sessionPrefab == null)
            {
                return;
            }

            var session = Instantiate(sessionPrefab);
            session.name = sessionPrefab.name;
            DontDestroyOnLoad(session);
            _sessionExists = true;
        }

        /// <summary>
        /// Creates the loading screen if this launch has not. It shows itself on the way
        /// up. Its own object rather than part of the session prefab, which is optional.
        /// </summary>
        private void EnsureLoadingScreen()
        {
            // Asked of the object, not a static bool: needs no reset between Play
            // sessions. Unity object, so `== null` rather than `?.`.
            if (LoadingScreen.Active != null || loadingScreenPrefab == null)
            {
                return;
            }

            var screen = Instantiate(loadingScreenPrefab);
            screen.name = loadingScreenPrefab.name;
            DontDestroyOnLoad(screen);
        }

        // Domain reload is disabled in some project settings, and then a static would
        // keep last run's value into the next Play session — the object it refers to
        // is gone by then, and nothing would ever create a new one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _sessionExists = false;
        }
    }
}

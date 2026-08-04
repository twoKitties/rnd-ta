using UnityEngine;
using UnityEngine.SceneManagement;

namespace _Game.Code.App
{
    /// <summary>
    /// The application's entry point, and the first thing in the build: it lives in
    /// the Loading scene, which holds nothing else.
    ///
    /// Its whole job is to make sure the things that must outlive a scene exist, and
    /// then hand over to the menu. It is not <see cref="_Game.Code.LevelBootstrapper"/>
    /// — that one wires up one raid and dies with it, while this runs once per launch.
    ///
    /// The network manager is instantiated here rather than placed in the Lobby scene
    /// because it has to survive Lobby → Level → Lobby. Guarded on "does one already
    /// exist" so that pressing Play straight into Lobby or Level during development
    /// still works and still ends up with exactly one.
    /// </summary>
    public class Bootstrapper : MonoBehaviour
    {
        [Tooltip("Loaded once everything below exists. The menu and the lobby live here.")]
        [SerializeField] private string firstScene = "Lobby";

        [Tooltip("Instantiated once per launch and kept for the whole session. Leave " +
                 "empty to run with no networking at all — single player still works.")]
        [SerializeField] private GameObject sessionPrefab;

        private static bool _sessionExists;

        private void Start()
        {
            EnsureSession();

            // Start, not Awake: loading a scene out of Awake runs while this scene is
            // still being built.
            if (!string.IsNullOrEmpty(firstScene) && SceneManager.GetActiveScene().name != firstScene)
            {
                SceneManager.LoadScene(firstScene);
            }
        }

        /// <summary>
        /// Creates the session object if this launch has not created one. Public and
        /// static so that entering Play mode in the middle of the game — which is how
        /// this project is actually tested — can ask for the same thing without
        /// routing through the Loading scene.
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

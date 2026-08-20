using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One method per entry: [MenuItem] paths must be compile-time constants, so the list cannot be a loop.
namespace _Game.Code.Editor
{
    internal static class SceneMenu
    {
        private const string FOLDER = "Assets/_Game/Content/Scenes/";

        [MenuItem("Tools/Scenes/Loading", false, 0)]
        private static void OpenLoading() => Open("Loading");

        [MenuItem("Tools/Scenes/Menu", false, 1)]
        private static void OpenMenu() => Open("Menu");

        [MenuItem("Tools/Scenes/Hub", false, 2)]
        private static void OpenHub() => Open("Hub");

        [MenuItem("Tools/Scenes/Level", false, 3)]
        private static void OpenLevel() => Open("Level");

        // Disabled in play mode: a live session changes scenes through FishNet, and opening one here would bypass it.
        [MenuItem("Tools/Scenes/Loading", true)]
        [MenuItem("Tools/Scenes/Menu", true)]
        [MenuItem("Tools/Scenes/Hub", true)]
        [MenuItem("Tools/Scenes/Level", true)]
        private static bool NotPlaying() => !EditorApplication.isPlaying;

        private static void Open(string sceneName)
        {
            string path = FOLDER + sceneName + ".unity";
            if (AssetDatabase.AssetPathToGUID(path).Length == 0)
            {
                Debug.LogError($"SceneMenu: no scene at {path}");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path);
        }
    }
}

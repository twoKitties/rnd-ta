using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Hodaart.StylizedcharacterCollection1.Editor
{
    /// <summary>
    /// Automatically installs com.unity.inputsystem when this package is imported,
    /// if it is not already present in the project.
    /// </summary>
    [InitializeOnLoad]
    internal static class HodaartDependencyInstaller
    {
        private const string PackageId = "com.unity.inputsystem";
        private const string MinVersion = "1.7.0";
        private const string ActiveInputHandlerKey = "activeInputHandler:";

        private static AddRequest s_request;

        static HodaartDependencyInstaller()
        {
            if (IsInManifest(PackageId))
                return;

            Debug.Log("[Hodaart] Required dependency not found — installing " + PackageId + "@" + MinVersion + "...");
            s_request = Client.Add(PackageId + "@" + MinVersion);
            EditorApplication.update += PollInstall;
        }

        private static void PollInstall()
        {
            if (!s_request.IsCompleted)
                return;

            EditorApplication.update -= PollInstall;

            if (s_request.Status == StatusCode.Success)
            {
                Debug.Log("[Hodaart] Successfully installed: " + s_request.Result.packageId);
                PromptInputHandlingIfNeeded();
            }
            else
            {
                Debug.LogError("[Hodaart] Failed to install " + PackageId + ": " + s_request.Error.message);
            }

            s_request = null;
        }

        private static bool IsInManifest(string packageId)
        {
            var path = Path.GetFullPath("Packages/manifest.json");
            if (!File.Exists(path))
                return false;

            return File.ReadAllText(path).Contains("\"" + packageId + "\"");
        }

        private static void PromptInputHandlingIfNeeded()
        {
            if (!IsUsingOldInputManagerOnly())
                return;

            bool open = EditorUtility.DisplayDialog(
                "Hodaart — Input System Required",
                "The Input System package has been installed.\n\n" +
                "Your project's Active Input Handling is still set to 'Input Manager (Old)'.\n\n" +
                "To use this package correctly, go to:\n" +
                "Project Settings → Player → Other Settings\n" +
                "and set Active Input Handling to 'Both' or 'Input System Package (New)'.",
                "Open Project Settings",
                "Later"
            );

            if (open)
                SettingsService.OpenProjectSettings("Project/Player");
        }

        private static bool IsUsingOldInputManagerOnly()
        {
            var path = Path.GetFullPath("ProjectSettings/ProjectSettings.asset");
            if (!File.Exists(path))
                return false;

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith(ActiveInputHandlerKey))
                    continue;

                var value = trimmed.Substring(ActiveInputHandlerKey.Length).Trim();
                return value == "0";
            }

            return false;
        }
    }
}

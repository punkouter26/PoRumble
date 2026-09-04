#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoRumble.Editor
{
    /// <summary>
    /// Builds the shipping Android APK from one menu item.
    ///
    /// Exists because the settings that make this build correct are spread across Player
    /// Settings, Build Settings and a keystore that lives outside the project, and a build
    /// assembled by hand gets one of them wrong eventually. In particular the signing
    /// passwords are deliberately not stored in ProjectSettings.asset - Unity keeps them in
    /// EditorPrefs, which is per-machine - so a build made without setting them here produces
    /// an unsigned APK that installs nowhere, and says so only at the very end.
    ///
    /// The build target is switched here rather than assumed. Switching reimports every asset
    /// for the new platform, so a run started from a Windows target is slow the first time and
    /// fast afterwards.
    /// </summary>
    internal static class AndroidBuilder
    {
        private const string KEYSTORE_PATH = "Build/keystore/porumble-dev.keystore";
        private const string KEYSTORE_ALIAS = "porumble";

        /// <summary>
        /// The development signing password.
        ///
        /// In the clear on purpose: this key exists so a debug APK can be installed on a
        /// handset, and it is gitignored along with the keystore it opens. A key that can
        /// publish to Play must not be used here - swap both the path and this pair for the
        /// real ones before any store build.
        /// </summary>
        private const string KEYSTORE_PASSWORD = "porumble-dev";

        private const string OUTPUT_DIRECTORY = "Build/Android";

        [MenuItem("PoRumble/Build Android APK", priority = 200)]
        private static void BuildApk()
        {
            string keystore = Path.GetFullPath(KEYSTORE_PATH);

            if (!File.Exists(keystore))
            {
                Debug.LogError(
                    $"No keystore at {KEYSTORE_PATH}. Generate one with keytool (see " +
                    "CLAUDE.md, Android) before building, or the APK is unsigned and will " +
                    "not install.");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("Could not switch the active build target to Android.");
                return;
            }

            // Written here rather than assumed: these are what actually make the build
            // installable, and the passwords cannot live in ProjectSettings.asset at all.
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = KEYSTORE_PASSWORD;
            PlayerSettings.Android.keyaliasName = KEYSTORE_ALIAS;
            PlayerSettings.Android.keyaliasPass = KEYSTORE_PASSWORD;

            // An APK, not an App Bundle. A bundle cannot be sideloaded with adb install, and
            // sideloading is the only distribution this project has.
            EditorUserBuildSettings.buildAppBundle = false;

            Directory.CreateDirectory(OUTPUT_DIRECTORY);

            string output = Path.Combine(
                OUTPUT_DIRECTORY,
                $"PoRumble-{PlayerSettings.bundleVersion}-{PlayerSettings.Android.bundleVersionCode}.apk");

            string[] scenes = EnabledScenePaths();

            if (scenes.Length == 0)
            {
                Debug.LogError("No scenes are enabled in Build Settings; nothing to build.");
                return;
            }

            var options = new BuildPlayerOptions
            {
                // The build list, not a hard-coded scene path: SampleScene is the only enabled
                // entry and the training arenas are deliberately editor-side tools.
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"Android build {summary.result} after {summary.totalTime}, " +
                    $"{summary.totalErrors} error(s).");
                return;
            }

            // The APK's own size on disk, not summary.totalSize - that counts the whole
            // uncompressed output including the symbol and Burst-debug folders, and reported
            // 1481 MB for a 57 MB APK.
            float megabytes = File.Exists(output) ? new FileInfo(output).Length / 1048576f : 0f;

            Debug.Log(
                $"Android build succeeded: {output}  " +
                $"({megabytes:F1} MB APK in {summary.totalTime:mm\\:ss})");
        }

        private static string[] EnabledScenePaths()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            var enabled = new List<string>(scenes.Length);

            for (int index = 0; index < scenes.Length; index++)
            {
                if (scenes[index].enabled)
                {
                    enabled.Add(scenes[index].path);
                }
            }

            return enabled.ToArray();
        }
    }
}
#endif

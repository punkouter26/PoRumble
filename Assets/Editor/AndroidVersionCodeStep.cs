using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoRumble.EditorTools
{
    /// <summary>
    /// Bumps the Android version code on every Android player build.
    ///
    /// Android refuses to install an APK whose version code is lower than the one already on
    /// the device, and silently treats an equal one as the same build - which is the failure
    /// worth automating away, because it does not look like a failure. `adb install -r` of a
    /// rebuilt APK at the same version code succeeds, the app launches, and you are looking at
    /// the previous build wondering why the change is not there. Every deploy from here gets a
    /// number the device can tell apart.
    ///
    /// Runs first (callbackOrder 0) so the bumped value is what the manifest and the chrome
    /// bar's version label both report for this build rather than the previous one.
    ///
    /// Only Android is touched. The standalone target has its own buildNumber and no installer
    /// that cares about ordering, so incrementing it would be churn in ProjectSettings for
    /// nothing.
    /// </summary>
    public sealed class AndroidVersionCodeStep : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
            {
                return;
            }

            int previous = PlayerSettings.Android.bundleVersionCode;
            PlayerSettings.Android.bundleVersionCode = previous + 1;

            // Written back to disk here rather than left to Unity's own save point: a build
            // started from a script and followed by an editor kill is exactly the sequence that
            // loses an unsaved ProjectSettings change, and a version code that silently rolls
            // back is the same install-the-old-build trap this class exists to close.
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[Android] versionCode {previous} -> {PlayerSettings.Android.bundleVersionCode} " +
                $"for version {PlayerSettings.bundleVersion}.");
        }
    }
}

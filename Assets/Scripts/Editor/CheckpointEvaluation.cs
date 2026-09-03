#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using PoRumble.Models;
using Unity.InferenceEngine;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoRumble.Editor
{
    /// <summary>
    /// Plays a queue of trained checkpoints through the training arena and records how each
    /// one actually fights.
    ///
    /// This exists because reward cannot be used to choose between them. Finishing a match
    /// early truncates the episode, which caps the damage-dealt reward that can accumulate,
    /// so the reward function mildly punishes winning quickly; the run that ships is the one
    /// picked on <see cref="EvaluationReport.knockoutRate"/> instead. Doing that by hand meant
    /// assigning an asset, pressing Play, watching, and writing a number down - eighty times
    /// for an eight-million-step run.
    ///
    /// The queue lives in EditorPrefs rather than in a field because every play-mode
    /// transition destroys the domain, and this has to survive fifty of them.
    ///
    /// The file carries a UNITY_EDITOR guard as well as sitting under an Editor asmdef. The
    /// asmdef is what actually keeps it out of a player build; the guard is there because the
    /// repo's guard-editor-runtime hook matches Editor folders with a forward-slash glob and
    /// cannot recognise one in a Windows path.
    /// </summary>
    internal static class CheckpointEvaluation
    {
        private const string QUEUE_KEY = "PoRumble.Eval.Queue";
        private const string INDEX_KEY = "PoRumble.Eval.Index";
        private const string MATCHES_KEY = "PoRumble.Eval.Matches";
        private const string SCENE_KEY = "PoRumble.Eval.Scene";

        private const string DEFAULT_SCENE = "Assets/Scenes/Training10.unity";
        private const string REPORT_DIRECTORY = "results/evaluation";

        [MenuItem("PoRumble/Evaluate Checkpoints...")]
        private static void Begin()
        {
            string folder = EditorUtility.OpenFolderPanel(
                "Folder of .onnx checkpoints", "Assets/ML-Agents/Models", string.Empty);

            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            List<string> checkpoints = FindCheckpoints(folder);

            if (checkpoints.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No checkpoints",
                    $"No .onnx files under {folder} are inside this project's Assets folder.\n\n" +
                    "ML-Agents writes checkpoints into results/, which Unity does not import. " +
                    "Copy the ones to compare into Assets/ML-Agents/Models/ first.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Evaluate checkpoints",
                    $"{checkpoints.Count} checkpoint(s) will each play {MatchesPerCheckpoint} " +
                    $"matches in {DEFAULT_SCENE}.\n\nThe Editor will enter and leave Play mode " +
                    $"once per checkpoint. Reports land in {REPORT_DIRECTORY}/.",
                    "Start", "Cancel"))
            {
                return;
            }

            EditorPrefs.SetString(QUEUE_KEY, string.Join("\n", checkpoints));
            EditorPrefs.SetInt(INDEX_KEY, 0);
            EditorPrefs.SetString(SCENE_KEY, DEFAULT_SCENE);
            RunNext();
        }

        /// <summary>Matches each checkpoint is measured over. Two hundred is ~3% noise on a rate.</summary>
        private static int MatchesPerCheckpoint => EditorPrefs.GetInt(MATCHES_KEY, 200);

        /// <summary>
        /// Every .onnx under the chosen folder that Unity has actually imported.
        ///
        /// The filter matters: ML-Agents writes checkpoints into results/, which sits outside
        /// Assets and is therefore not an asset database entry at all. A model that has not
        /// been imported cannot be assigned to BehaviorParameters, and the failure is a silent
        /// null rather than an error.
        /// </summary>
        private static List<string> FindCheckpoints(string folder)
        {
            List<string> found = new();
            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            foreach (string file in Directory.EnumerateFiles(folder, "*.onnx", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(projectRoot ?? ".", file).Replace('\\', '/');

                if (!relative.StartsWith("Assets/"))
                {
                    continue;
                }

                found.Add(relative);
            }

            found.Sort();
            return found;
        }

        /// <summary>
        /// Starts the next checkpoint in the queue, or reports that the queue is done.
        ///
        /// Invoked from the play-mode state callback, so it must tolerate being called when
        /// nothing is queued at all - which is every ordinary Play session in this project.
        /// </summary>
        private static void RunNext()
        {
            string queue = EditorPrefs.GetString(QUEUE_KEY, string.Empty);

            if (string.IsNullOrEmpty(queue))
            {
                return;
            }

            string[] checkpoints = queue.Split('\n');
            int index = EditorPrefs.GetInt(INDEX_KEY, 0);

            if (index >= checkpoints.Length)
            {
                Finish(checkpoints.Length);
                return;
            }

            string assetPath = checkpoints[index];
            ModelAsset model = AssetDatabase.LoadAssetAtPath<ModelAsset>(assetPath);

            if (model == null)
            {
                Debug.LogWarning($"[PoRumble] Skipping {assetPath}: not an imported ModelAsset.");
                EditorPrefs.SetInt(INDEX_KEY, index + 1);
                RunNext();
                return;
            }

            if (!AssignModel(model))
            {
                Finish(checkpoints.Length);
                return;
            }

            WriteRequest(Path.GetFileNameWithoutExtension(assetPath));

            EditorSceneManager.OpenScene(EditorPrefs.GetString(SCENE_KEY, DEFAULT_SCENE));
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// Puts the checkpoint on the Boxer prefab, so every fighter the arena spawns runs it.
        ///
        /// Written to the prefab rather than to spawned instances because the arena
        /// instantiates its roster at runtime, long after any Editor script has stopped
        /// running. It does mean the prefab is left holding the last checkpoint measured -
        /// reassign the shipping model before playing the game scene.
        /// </summary>
        private static bool AssignModel(ModelAsset model)
        {
            const string prefabPath = "Assets/Prefabs/Boxer.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                Debug.LogError($"[PoRumble] {prefabPath} is missing; cannot evaluate.");
                return false;
            }

            BehaviorParameters parameters = prefab.GetComponentInChildren<BehaviorParameters>(true);

            if (parameters == null)
            {
                Debug.LogError($"[PoRumble] {prefabPath} has no BehaviorParameters.");
                return false;
            }

            parameters.Model = model;
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static void WriteRequest(string label)
        {
            EvaluationRequest request = new()
            {
                label = label,
                reportPath = $"{REPORT_DIRECTORY}/{label}.json",
                matches = MatchesPerCheckpoint,
                exitWhenDone = true,
            };

            string path = EvaluationRequest.Resolve(EvaluationRequest.DEFAULT_PATH);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonUtility.ToJson(request, true));
        }

        /// <summary>
        /// Advances the queue when the harness leaves Play mode.
        ///
        /// Deferred by a frame with delayCall: re-entering Play mode from inside the callback
        /// that reports leaving it is a reliable way to wedge the Editor.
        /// </summary>
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorPrefs.SetInt(INDEX_KEY, EditorPrefs.GetInt(INDEX_KEY, 0) + 1);
            EditorApplication.delayCall += RunNext;
        }

        private static void Finish(int count)
        {
            EditorPrefs.DeleteKey(QUEUE_KEY);
            EditorPrefs.DeleteKey(INDEX_KEY);
            EvaluationRequest.Clear();
            Debug.Log(
                $"[PoRumble] Evaluated {count} checkpoint(s). Reports are in {REPORT_DIRECTORY}/; " +
                "select on knockoutRate, not on reward.");
        }

        /// <summary>Abandons a queue mid-run, for when a measurement has to be called off.</summary>
        [MenuItem("PoRumble/Cancel Checkpoint Evaluation")]
        private static void Cancel()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Finish(0);
        }
    }
}
#endif

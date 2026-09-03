using System;
using System.IO;
using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// An ask to measure one policy over a fixed number of matches, left on disk for a
    /// running scene to pick up.
    ///
    /// A file rather than a serialized field, because the thing that writes it is an Editor
    /// script and the thing that reads it lives on the other side of a domain reload and a
    /// play-mode transition. Nothing survives that except the scene, and editing the scene to
    /// start a measurement would leave the harness wired into a training arena for ever.
    ///
    /// Absent means "do not evaluate", which is the case in every ordinary Play session, so
    /// the harness costs nothing when nobody asked for it.
    /// </summary>
    [Serializable]
    public sealed class EvaluationRequest
    {
        /// <summary>Name to file the results under - usually the checkpoint's own name.</summary>
        public string label;

        /// <summary>Where to write the report, relative to the project root.</summary>
        public string reportPath;

        /// <summary>How many matches to measure before reporting.</summary>
        public int matches = 200;

        /// <summary>
        /// Leave play mode once the report is written. Off when a human is watching the
        /// evaluation run, on when a script is driving a queue of checkpoints through it.
        /// </summary>
        public bool exitWhenDone = true;

        /// <summary>Project-root-relative path both halves agree on.</summary>
        public const string DEFAULT_PATH = "Temp/porumble_eval_request.json";

        /// <summary>
        /// Turns a project-root-relative path into an absolute one.
        /// <see cref="Application.dataPath"/> ends at Assets, so the project root is its
        /// parent.
        /// </summary>
        public static string Resolve(string relativePath)
        {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", relativePath);
        }

        /// <summary>Reads the pending request, or null when there is none.</summary>
        public static EvaluationRequest Load()
        {
            string path = Resolve(DEFAULT_PATH);

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                EvaluationRequest request = JsonUtility.FromJson<EvaluationRequest>(File.ReadAllText(path));

                // A request with no matches in it would report on an empty sample rather than
                // failing, which is the worst of the available outcomes.
                return request != null && request.matches > 0 ? request : null;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[PoRumble] Could not read {path}: {error.Message}");
                return null;
            }
        }

        /// <summary>Clears the pending request, so a later Play session is an ordinary one.</summary>
        public static void Clear()
        {
            string path = Resolve(DEFAULT_PATH);

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Appends each finished match as one line of JSON under the persistent data path.
    ///
    /// JSON Lines rather than one document, so a match is a single append and a run killed
    /// halfway leaves every earlier match readable. One file per day and per process: an
    /// overnight run drives several headless players at once, and they must not interleave
    /// writes in the same file.
    /// </summary>
    public sealed class FileMatchLogStore : IMatchLogStore
    {
        private const string LOG_FOLDER = "matchlog";

        private readonly string _folder;
        private readonly int _processId;

        public FileMatchLogStore()
        {
            _folder = Path.Combine(Application.persistentDataPath, LOG_FOLDER);
            _processId = Process.GetCurrentProcess().Id;
        }

        public void Append(MatchLogRecord record)
        {
            try
            {
                Directory.CreateDirectory(_folder);

                string file = Path.Combine(
                    _folder,
                    $"matches_{DateTime.UtcNow:yyyyMMdd}_{_processId}.jsonl");

                File.AppendAllText(file, JsonUtility.ToJson(record) + "\n");
            }
            catch (Exception exception)
            {
                // A full or read-only disk must not stop the results screen coming up.
                GameLog.Warning($"[PoRumble] Match log not written: {exception.Message}");
            }
        }
    }
}

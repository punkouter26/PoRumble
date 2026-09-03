using System;
using System.Collections.Generic;
using System.IO;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Keeps the standings in a JSON file under the player's persistent data path.
    ///
    /// Lives in Views rather than Systems because the file path is a platform concern:
    /// persistentDataPath is a different place on a phone, and the rating maths has no
    /// business knowing that. Registered as the <see cref="IRatingStore"/> the rating system
    /// asks for.
    ///
    /// Every failure is swallowed and logged. A corrupt or unreadable table is worth exactly
    /// one warning and a fresh start — it is a league table for a boxing game, and refusing
    /// to launch over it would be absurd.
    /// </summary>
    public sealed class FileRatingStore : IRatingStore
    {
        [Serializable]
        private sealed class SerializedRecord
        {
            public string id;
            public string displayName;
            public float rating;
            public int matches;
            public int wins;
            public int knockouts;
        }

        [Serializable]
        private sealed class SerializedTable
        {
            public List<SerializedRecord> records = new();
        }

        private const string FILE_NAME = "porumble_ratings.json";

        private readonly string _path;

        public FileRatingStore()
        {
            _path = Path.Combine(Application.persistentDataPath, FILE_NAME);
        }

        public void Load(RatingModel ratings)
        {
            if (!File.Exists(_path))
            {
                return;
            }

            SerializedTable table;

            try
            {
                table = JsonUtility.FromJson<SerializedTable>(File.ReadAllText(_path));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PoRumble] Could not read ratings from {_path}: {exception.Message}");
                return;
            }

            if (table?.records == null)
            {
                return;
            }

            for (int index = 0; index < table.records.Count; index++)
            {
                SerializedRecord stored = table.records[index];

                if (stored == null || string.IsNullOrEmpty(stored.id))
                {
                    continue;
                }

                RatingRecord record = ratings.GetOrCreate(stored.id, stored.displayName);

                if (record == null)
                {
                    continue;
                }

                record.Rating = stored.rating;
                record.Matches = stored.matches;
                record.Wins = stored.wins;
                record.Knockouts = stored.knockouts;

                // Deliberately not restored: it describes the last match of a previous
                // session, and showing "+18" next to a fighter who has not fought since the
                // app was last open would be a lie.
                record.LastDelta = 0f;
            }
        }

        public void Save(RatingModel ratings)
        {
            SerializedTable table = new();
            IReadOnlyList<RatingRecord> records = ratings.Records;

            for (int index = 0; index < records.Count; index++)
            {
                RatingRecord record = records[index];

                table.records.Add(new SerializedRecord
                {
                    id = record.Id,
                    displayName = record.DisplayName,
                    rating = record.Rating,
                    matches = record.Matches,
                    wins = record.Wins,
                    knockouts = record.Knockouts
                });
            }

            try
            {
                File.WriteAllText(_path, JsonUtility.ToJson(table, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PoRumble] Could not write ratings to {_path}: {exception.Message}");
            }
        }
    }
}

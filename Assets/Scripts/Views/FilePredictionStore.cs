using System;
using System.IO;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Keeps the viewer's bank in a JSON file under the persistent data path, beside the
    /// league table. In Views for the reason <see cref="FileRatingStore"/> is: the path is a
    /// platform concern.
    ///
    /// The pick itself is not saved. It names a contestant asset, and restoring it would mean
    /// resolving an id against a card that may have changed since - a stale pick silently
    /// staked on the next bell is worse than asking again.
    ///
    /// Every failure is one warning and a fresh bank. It is play money.
    /// </summary>
    public sealed class FilePredictionStore : IPredictionStore
    {
        [Serializable]
        private sealed class SerializedBook
        {
            public int bank = PredictionModel.STARTING_BANK;
            public int placed;
            public int correct;
            public int bailouts;
            public int bestPayout;
        }

        private const string FILE_NAME = "porumble_predictions.json";

        private readonly string _path;

        public FilePredictionStore()
        {
            _path = Path.Combine(Application.persistentDataPath, FILE_NAME);
        }

        public void Load(PredictionModel predictions)
        {
            if (!File.Exists(_path))
            {
                return;
            }

            SerializedBook book;

            try
            {
                book = JsonUtility.FromJson<SerializedBook>(File.ReadAllText(_path));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PoRumble] Could not read predictions from {_path}: {exception.Message}");
                return;
            }

            if (book == null)
            {
                return;
            }

            predictions.Bank.Value = book.bank;
            predictions.Placed = book.placed;
            predictions.Correct = book.correct;
            predictions.Bailouts = book.bailouts;
            predictions.BestPayout = book.bestPayout;
        }

        public void Save(PredictionModel predictions)
        {
            SerializedBook book = new()
            {
                bank = predictions.Bank.Value,
                placed = predictions.Placed,
                correct = predictions.Correct,
                bailouts = predictions.Bailouts,
                bestPayout = predictions.BestPayout
            };

            try
            {
                File.WriteAllText(_path, JsonUtility.ToJson(book, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PoRumble] Could not write predictions to {_path}: {exception.Message}");
            }
        }
    }
}

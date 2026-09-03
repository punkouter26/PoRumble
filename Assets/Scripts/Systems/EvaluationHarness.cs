using System;
using System.Collections.Generic;
using System.IO;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PoRumble.Systems
{
    /// <summary>
    /// Measures one policy over a fixed number of matches and writes a report.
    ///
    /// Registered only when a request file is waiting, so an ordinary Play session never
    /// constructs it. It exists because reward is a poor way to choose between checkpoints
    /// here - see <see cref="EvaluationReport"/> - and until now nothing in the project
    /// recorded the number that actually matters, which is how often a match finishes before
    /// the bell.
    ///
    /// It counts rather than judges: every fighter in the arena is running whichever policy
    /// the scene was set up with, so this reports on the roster as configured, not on any one
    /// seat. That is the right unit for a free-for-all, where "did this policy win" is
    /// meaningless against nine copies of itself.
    /// </summary>
    public sealed class EvaluationHarness : IFixedTickable, IDisposable
    {
        private readonly MatchModel _match;
        private readonly BoxerConfig _config;
        private readonly EvaluationRequest _request;
        private readonly IDisposable _subscription;

        private readonly EvaluationReport _report = new();

        /// <summary>Physics steps since the current match began.</summary>
        private int _episodeSteps;

        private long _totalSteps;
        private long _totalSurvivors;
        private float _totalWinnerHealth;
        private bool _finished;

        [Inject]
        public EvaluationHarness(
            MatchModel match,
            BoxerConfig config,
            EvaluationRequest request,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _match = match;
            _config = config;
            _request = request;
            _report.label = request.label;
            _subscription = endedSubscriber.Subscribe(OnMatchEnded);
        }

        public void FixedTick()
        {
            if (_finished)
            {
                return;
            }

            _episodeSteps++;
        }

        private void OnMatchEnded(MatchEndedMessage message)
        {
            if (_finished)
            {
                return;
            }

            _report.matches++;

            if (message.WinnerId == MatchModel.NO_WINNER)
            {
                _report.draws++;
            }

            // Read before the arena is re-racked. The director restarts the episode inside the
            // same fixed step this message is published on, so anything sampled later is a
            // snapshot of the next match's opening rather than this one's ending.
            if (message.EndedOnTimeout)
            {
                _report.timeouts++;
            }
            else
            {
                _report.knockouts++;
            }

            _totalSteps += _episodeSteps;
            _totalSurvivors += _match.CountAlive();
            _totalWinnerHealth += WinnerHealthFraction(message.WinnerId);
            _episodeSteps = 0;

            if (_report.matches < _request.matches)
            {
                return;
            }

            Finish();
        }

        private float WinnerHealthFraction(int winnerId)
        {
            if (winnerId == MatchModel.NO_WINNER)
            {
                return 0f;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                if (boxers[boxerIndex].Id == winnerId)
                {
                    return boxers[boxerIndex].Health.Value / (float)Mathf.Max(1, _config.MaxHealth);
                }
            }

            return 0f;
        }

        /// <summary>
        /// Averages the sample, writes the report and clears the request so the next Play
        /// session is an ordinary one.
        /// </summary>
        private void Finish()
        {
            _finished = true;

            float matches = Mathf.Max(1, _report.matches);
            _report.knockoutRate = _report.knockouts / matches;
            _report.meanEpisodeSteps = _totalSteps / matches;
            _report.meanSurvivors = _totalSurvivors / matches;
            _report.meanWinnerHealth = _totalWinnerHealth / matches;

            string path = EvaluationRequest.Resolve(_request.reportPath);
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonUtility.ToJson(_report, true));
            EvaluationRequest.Clear();

            Debug.Log(
                $"[PoRumble] {_report.label}: {_report.knockoutRate:P0} of {_report.matches} " +
                $"matches finished by knockout, mean {_report.meanEpisodeSteps:F0} steps, " +
                $"{_report.meanSurvivors:F2} left standing. Written to {path}");

            if (!_request.exitWhenDone)
            {
                return;
            }

            Stop();
        }

        /// <summary>
        /// Ends the session. In the Editor that means leaving Play mode; in a built player it
        /// means quitting, which is how a script driving a queue of checkpoints gets its
        /// process back.
        /// </summary>
        private static void Stop()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }
}

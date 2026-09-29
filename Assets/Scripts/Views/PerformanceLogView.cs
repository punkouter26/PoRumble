using System;
using System.IO;
using System.Text;
using PoRumble.Models;
using Unity.Profiling;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Records every match's performance, a sample a second, and writes it to a CSV on the
    /// device at the final bell.
    ///
    /// The diagnostics sheet shows what the frame is doing now. What it cannot show is the
    /// failure phones actually have: a build that holds its frame rate for the first minute and
    /// loses a third of it by the fifth because the device has heated and throttled. That is a
    /// trend over a whole match, and the only way to see it after the fact is a log - so every
    /// match leaves one, beside the ratings file, with the thermal status and battery
    /// temperature next to the frame times.
    ///
    /// Samples whether or not the sheet is up: a log that only ran while somebody was watching
    /// would miss every match played normally.
    ///
    /// Allocation-free per frame. The thermal probe goes through JNI and allocates, so it is
    /// polled every few seconds; the file is written once per match, at the results screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceLogView : MonoBehaviour
    {
        [Tooltip("Write each match's trace to persistentDataPath/perf at the final bell.")]
        [SerializeField] private bool _writeLog = true;

        [Tooltip("Logs kept on the device. The oldest are deleted beyond this, so a phone " +
                 "left running the exhibition all day does not fill up with CSVs.")]
        [SerializeField] private int _logsKept = 20;

        [Tooltip("Seconds between thermal readings. JNI calls allocate, so not every frame.")]
        [SerializeField] private float _thermalEverySeconds = 5f;

        private const string LOG_FOLDER = "perf";

        private readonly StringBuilder _csv = new(64 * 1024);
        private readonly CompositeDisposable _disposables = new();

        private MatchModel _match;
        private MatchFlowModel _flow;
        private PerformanceTraceModel _trace;

        private ThermalProbe _thermal;

        private ProfilerRecorder _srpBatcherDraws;
        private ProfilerRecorder _standardDraws;
        private ProfilerRecorder _dynamicDraws;
        private ProfilerRecorder _setPassRecorder;

        private bool _recording;
        private float _secondTimer;
        private float _accumulatedMs;
        private int _accumulatedFrames;
        private float _peakMs;
        private float _elapsed;
        private long _lastGcBytes;

        private float _thermalTimer;
        private int _thermalStatus = -1;
        private float _batteryCelsius = float.NaN;

        [Inject]
        public void Construct(MatchModel match, MatchFlowModel flow, PerformanceTraceModel trace)
        {
            _match = match;
            _flow = flow;
            _trace = trace;
        }

        private void Awake()
        {
            // The same counters the diagnostics sheet reads, by the names it found by asking
            // ProfilerRecorderHandle.GetAvailable - there is no single draw-call counter.
            _srpBatcherDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SRP Batcher Draw Calls Count");
            _standardDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Standard Draw Calls Count");
            _dynamicDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Dynamic Batched Draw Calls Count");
            _setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");

            _thermal = new ThermalProbe();
        }

        private void Start()
        {
            if (_flow == null || _trace == null)
            {
                return;
            }

            _lastGcBytes = GC.GetTotalMemory(false);
            _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
        }

        /// <summary>Unscaled: hitstop and the knockout hold change timeScale, and frame time must not move with them.</summary>
        private void Update()
        {
            if (!_recording)
            {
                return;
            }

            float delta = Time.unscaledDeltaTime;
            float frameMs = delta * 1000f;

            _accumulatedMs += frameMs;
            _accumulatedFrames++;
            _peakMs = Mathf.Max(_peakMs, frameMs);
            _elapsed += delta;

            _thermalTimer -= delta;

            if (_thermalTimer <= 0f)
            {
                _thermalTimer = _thermalEverySeconds;
                _thermalStatus = _thermal.ReadThermalStatus();
                _batteryCelsius = _thermal.ReadBatteryCelsius();
            }

            _secondTimer += delta;

            if (_secondTimer < 1f)
            {
                return;
            }

            long gcNow = GC.GetTotalMemory(false);
            long grown = gcNow - _lastGcBytes;
            _lastGcBytes = gcNow;

            _trace.Add(new PerformanceSecond(
                _elapsed,
                _accumulatedFrames > 0 ? _accumulatedMs / _accumulatedFrames : 0f,
                _peakMs,
                grown > 0 ? grown / 1024f : 0f,
                (int)(Read(_srpBatcherDraws) + Read(_standardDraws) + Read(_dynamicDraws)),
                (int)Read(_setPassRecorder),
                _thermalStatus,
                _batteryCelsius,
                _match == null ? 0 : _match.CountAlive()));

            _secondTimer = 0f;
            _accumulatedMs = 0f;
            _accumulatedFrames = 0;
            _peakMs = 0f;
        }

        /// <summary>
        /// Starts a fresh trace at the countdown and closes it at the results. The knockout hold
        /// stays inside the trace: it is part of the match, and the slow-motion replay is
        /// exactly where a hitch would be most visible.
        /// </summary>
        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            switch (phase)
            {
                case MatchFlowPhase.Countdown:
                    BeginTrace();
                    break;

                case MatchFlowPhase.Results:
                    if (_recording)
                    {
                        _recording = false;
                        WriteLog();
                    }

                    break;

                case MatchFlowPhase.Title:
                    // Abandoned through the MENU button: no final bell, so no log.
                    _recording = false;
                    break;
            }
        }

        private void BeginTrace()
        {
            _trace.Clear();
            _trace.MatchNumber = _flow.MatchNumber.Value;

            _recording = true;
            _elapsed = 0f;
            _secondTimer = 0f;
            _accumulatedMs = 0f;
            _accumulatedFrames = 0;
            _peakMs = 0f;
            _thermalTimer = 0f;
            _lastGcBytes = GC.GetTotalMemory(false);
        }

        private void WriteLog()
        {
            if (!_writeLog || _trace.Count == 0)
            {
                return;
            }

            try
            {
                string folder = Path.Combine(Application.persistentDataPath, LOG_FOLDER);
                Directory.CreateDirectory(folder);

                string file = Path.Combine(
                    folder,
                    $"match_{DateTime.Now:yyyyMMdd_HHmmss}_{_trace.MatchNumber:000}.csv");

                _csv.Clear();
                _trace.AppendCsv(_csv);
                File.WriteAllText(file, _csv.ToString());

                _trace.LastLogPath = file;
                Prune(folder);
            }
            catch (IOException exception)
            {
                // A full or read-only disk must not stop the results screen coming up.
                Debug.LogWarning($"Performance log not written: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                Debug.LogWarning($"Performance log not written: {exception.Message}");
            }
        }

        /// <summary>Deletes the oldest logs beyond the number kept. The names sort by time, so name order is age order.</summary>
        private void Prune(string folder)
        {
            string[] logs = Directory.GetFiles(folder, "match_*.csv");

            if (logs.Length <= _logsKept)
            {
                return;
            }

            Array.Sort(logs, StringComparer.Ordinal);

            for (int index = 0; index < logs.Length - _logsKept; index++)
            {
                File.Delete(logs[index]);
            }
        }

        private static long Read(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue : 0L;
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
            _srpBatcherDraws.Dispose();
            _standardDraws.Dispose();
            _dynamicDraws.Dispose();
            _setPassRecorder.Dispose();

            if (_thermal != null)
            {
                _thermal.Dispose();
            }
        }
    }
}

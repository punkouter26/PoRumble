using System.Collections.Generic;
using System.Text;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Match HUD: survivor count, per-boxer health bars, the countdown to the bell and the
    /// result banner.
    ///
    /// All styling comes from the shared stylesheet. This class assigns class names and the
    /// handful of values that are genuinely dynamic - a bar's width - and owns no colours,
    /// paddings or font sizes of its own.
    ///
    /// A View: it observes models and messages and never mutates game state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudView : MonoBehaviour
    {
        [Tooltip("The shared HUD stylesheet. Without it the panel renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);
        private readonly List<VisualElement> _healthFills = new();

        private MatchModel _match;
        private MatchFlowModel _flow;
        private BoxerConfig _config;
        private Label _survivorsLabel;
        private Label _resultLabel;
        private Label _captionLabel;
        private Label _promptLabel;

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            BoxerConfig config,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _match = match;
            _flow = flow;
            _config = config;
            endedSubscriber.Subscribe(OnMatchEnded).AddTo(_disposables);
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _match == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            VisualElement panel = new();
            panel.AddToClassList("match-hud");
            panel.pickingMode = PickingMode.Ignore;
            root.Add(panel);

            _survivorsLabel = MakeLabel("text", "text--lg", "text--bold");
            panel.Add(_survivorsLabel);

            VisualElement roster = new();
            roster.AddToClassList("match-hud__roster");
            panel.Add(roster);

            BuildHealthBars(roster);

            _resultLabel = MakeLabel("text", "text--xl", "text--bold", "result-banner");
            panel.Add(_resultLabel);

            BuildCenterCaption(root);
            RefreshSurvivors();

            if (_flow != null)
            {
                _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
                _flow.CountdownSeconds.Subscribe(OnCountdownChanged).AddTo(_disposables);
            }
        }

        /// <summary>
        /// The big middle-of-screen caption: "3 / 2 / 1 / FIGHT!", then the restart prompt.
        /// Non-picking so it never eats a click, and absolutely positioned so its text changing
        /// length cannot shift the health bars around.
        /// </summary>
        private void BuildCenterCaption(VisualElement root)
        {
            VisualElement centre = new();
            centre.AddToClassList("centre-stage");
            centre.pickingMode = PickingMode.Ignore;
            root.Add(centre);

            _captionLabel = MakeLabel("text", "text--display", "text--bold", "centre-stage__caption");
            centre.Add(_captionLabel);

            _promptLabel = MakeLabel("text", "text--md", "centre-stage__prompt");
            centre.Add(_promptLabel);
        }

        private static Label MakeLabel(params string[] classes)
        {
            Label label = new(string.Empty);
            label.pickingMode = PickingMode.Ignore;

            for (int index = 0; index < classes.Length; index++)
            {
                label.AddToClassList(classes[index]);
            }

            return label;
        }

        private void BuildHealthBars(VisualElement roster)
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                VisualElement row = new();
                row.AddToClassList("match-hud__row");

                Label name = MakeLabel("match-hud__name");
                name.text = $"#{boxer.Id:00}";
                row.Add(name);

                VisualElement track = new();
                track.AddToClassList("bar");
                track.AddToClassList("match-hud__bar");
                row.Add(track);

                VisualElement fill = new();
                fill.AddToClassList("bar__fill");
                fill.AddToClassList("bar__fill--healthy");
                track.Add(fill);

                _healthFills.Add(fill);
                roster.Add(row);

                int index = boxerIndex;
                boxer.Health.Subscribe(hp => OnHealthChanged(index, hp)).AddTo(_disposables);
                boxer.IsAlive.Subscribe(_ => RefreshSurvivors()).AddTo(_disposables);
            }
        }

        private void OnHealthChanged(int index, int health)
        {
            if (index >= _healthFills.Count)
            {
                return;
            }

            // Max health comes from the config, not from the first observed value, which would
            // rebase the bar after the boxer had already taken damage.
            float ratio = Mathf.Clamp01(health / (float)Mathf.Max(1, _config.MaxHealth));
            VisualElement fill = _healthFills[index];

            fill.style.width = Length.Percent(ratio * 100f);
            fill.EnableInClassList("bar__fill--healthy", ratio > 0.5f);
            fill.EnableInClassList("bar__fill--hurt", ratio <= 0.5f && ratio > 0.2f);
            fill.EnableInClassList("bar__fill--critical", ratio <= 0.2f);
        }

        private void RefreshSurvivors()
        {
            if (_survivorsLabel == null)
            {
                return;
            }

            _builder.Clear();
            _builder.Append("SURVIVORS  ")
                    .Append(_match.CountAlive())
                    .Append(" / ")
                    .Append(_match.Boxers.Count);

            if (_flow != null && _flow.MatchNumber.Value > 1)
            {
                _builder.Append("      MATCH ").Append(_flow.MatchNumber.Value);
            }

            _survivorsLabel.text = _builder.ToString();
        }

        private void OnCountdownChanged(int seconds)
        {
            if (_captionLabel == null || _flow.Phase.Value != MatchFlowPhase.Countdown)
            {
                return;
            }

            _captionLabel.text = seconds > 0 ? seconds.ToString() : string.Empty;
        }

        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            if (_captionLabel == null)
            {
                return;
            }

            switch (phase)
            {
                case MatchFlowPhase.Introducing:
                    // A restart clears the previous result, so the banner does not linger
                    // over the top of the next fight.
                    _captionLabel.text = "GET READY";
                    _promptLabel.text = string.Empty;
                    _resultLabel.text = string.Empty;
                    RefreshSurvivors();
                    break;

                case MatchFlowPhase.Countdown:
                    _captionLabel.text = _flow.CountdownSeconds.Value.ToString();
                    break;

                case MatchFlowPhase.Fighting:
                    _captionLabel.text = "FIGHT!";
                    _promptLabel.text = string.Empty;
                    ClearCaptionAfterBell();
                    break;

                case MatchFlowPhase.KnockoutHold:
                    _captionLabel.text = string.Empty;
                    break;

                case MatchFlowPhase.Results:
                    _captionLabel.text = string.Empty;
                    _promptLabel.text = RestartPrompt();
                    break;
            }
        }

        /// <summary>
        /// Names the input the player actually has. A phone has no R key, and telling someone
        /// to press one on a touchscreen reads as a dead end.
        /// </summary>
        private static string RestartPrompt()
        {
            return UnityEngine.InputSystem.Touchscreen.current != null
                && UnityEngine.InputSystem.Keyboard.current == null
                    ? "TAP TO FIGHT AGAIN"
                    : "PRESS  R  TO FIGHT AGAIN";
        }

        /// <summary>
        /// Wipes "FIGHT!" a beat after the bell. Scheduled on the panel rather than timed in
        /// Update so it costs nothing on the frames in between.
        /// </summary>
        private void ClearCaptionAfterBell()
        {
            _captionLabel.schedule.Execute(() =>
            {
                if (_flow.Phase.Value == MatchFlowPhase.Fighting)
                {
                    _captionLabel.text = string.Empty;
                }
            }).StartingIn(700);
        }

        private void OnMatchEnded(MatchEndedMessage message)
        {
            if (_resultLabel == null)
            {
                return;
            }

            _resultLabel.text = message.WinnerId == MatchModel.NO_WINNER
                ? "DRAW"
                : $"BOXER #{message.WinnerId:00} WINS";
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}

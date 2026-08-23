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
    /// Match HUD: survivor count, per-boxer health bars and the result banner.
    /// A View — it observes models and messages and never mutates game state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudView : MonoBehaviour
    {
        private static readonly Color HealthyColor = new(0.40f, 0.85f, 0.40f);
        private static readonly Color HurtColor = new(0.92f, 0.55f, 0.28f);

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);
        private readonly List<VisualElement> _healthFills = new();

        private MatchModel _match;
        private BoxerConfig _config;
        private Label _survivorsLabel;
        private Label _resultLabel;

        [Inject]
        public void Construct(
            MatchModel match,
            BoxerConfig config,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _match = match;
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

            root.style.paddingLeft = 14;
            root.style.paddingTop = 12;

            _survivorsLabel = MakeLabel(22, FontStyle.Bold);
            root.Add(_survivorsLabel);

            VisualElement list = new();
            list.style.marginTop = 8;
            root.Add(list);

            BuildHealthBars(list);

            _resultLabel = MakeLabel(34, FontStyle.Bold);
            _resultLabel.style.marginTop = 16;
            root.Add(_resultLabel);

            RefreshSurvivors();
        }

        private static Label MakeLabel(int fontSize, FontStyle style)
        {
            Label label = new(string.Empty);
            label.style.color = Color.white;
            label.style.fontSize = fontSize;
            label.style.unityFontStyleAndWeight = style;
            return label;
        }

        private void BuildHealthBars(VisualElement list)
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                VisualElement row = new();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 3;

                Label name = MakeLabel(13, FontStyle.Normal);
                name.text = $"#{boxer.Id:00}";
                name.style.width = 32;
                row.Add(name);

                VisualElement track = new();
                track.style.width = 130;
                track.style.height = 10;
                track.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
                row.Add(track);

                VisualElement fill = new();
                fill.style.height = 10;
                fill.style.width = Length.Percent(100f);
                fill.style.backgroundColor = HealthyColor;
                track.Add(fill);

                _healthFills.Add(fill);
                list.Add(row);

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
            _healthFills[index].style.width = Length.Percent(ratio * 100f);
            _healthFills[index].style.backgroundColor = ratio > 0.5f ? HealthyColor : HurtColor;
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
            _survivorsLabel.text = _builder.ToString();
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

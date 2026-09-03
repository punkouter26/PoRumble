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
    /// Structure comes from UXML and styling from the shared stylesheet. This class owns
    /// neither: it looks elements up by name and writes only what is genuinely dynamic - a
    /// bar's width, a label's text, a state class. No colours, paddings or font sizes.
    ///
    /// A View: it observes models and messages and never mutates game state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudView : MonoBehaviour
    {
        [Tooltip("The panel's structure. Without it the HUD renders nothing at all.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("One fighter's health row, cloned once per boxer slot.")]
        [SerializeField] private VisualTreeAsset _healthRowTemplate;

        [Tooltip("The shared HUD stylesheet. Without it the panel renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);
        private readonly List<VisualElement> _healthFills = new();

        /// <summary>
        /// The name beside each health bar, kept so a re-dealt card can rewrite them. Boxer
        /// slots are permanent; who is sitting in one is not.
        /// </summary>
        private readonly List<Label> _nameLabels = new();

        private MatchModel _match;
        private MatchFlowModel _flow;
        private RosterModel _roster;
        private BoxerConfig _config;
        private Label _survivorsLabel;
        private Label _resultLabel;
        private Label _captionLabel;
        private Label _promptLabel;

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            RosterModel roster,
            BoxerConfig config,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _match = match;
            _flow = flow;
            _roster = roster;
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

            if (_layout == null)
            {
                Debug.LogError(
                    $"{nameof(MatchHudView)} has no layout assigned; the match HUD will not " +
                    "render. Assign Assets/UI/Layouts/MatchHud.uxml.", this);
                return;
            }

            _layout.CloneTree(root);

            _survivorsLabel = root.Q<Label>("survivors");
            _resultLabel = root.Q<Label>("result");
            _captionLabel = root.Q<Label>("caption");
            _promptLabel = root.Q<Label>("prompt");

            BuildHealthBars(root.Q<VisualElement>("roster"));
            RefreshNames();
            RefreshSurvivors();

            if (_flow != null)
            {
                _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
                _flow.CountdownSeconds.Subscribe(OnCountdownChanged).AddTo(_disposables);
            }

            // Names follow the card, so re-dealing the roster rewrites the whole column.
            _roster.Revision.Subscribe(_ => RefreshNames()).AddTo(_disposables);
        }

        /// <summary>
        /// Clones one row per boxer slot and keeps the two elements that get written later.
        ///
        /// CloneTree(target) adds the template's own children straight into the target, with no
        /// TemplateContainer in between. That matters here: an extra wrapper element would sit
        /// in the middle of the column's flex layout and give every row a second box to
        /// inherit sizing from.
        /// </summary>
        private void BuildHealthBars(VisualElement roster)
        {
            if (roster == null || _healthRowTemplate == null)
            {
                Debug.LogError(
                    $"{nameof(MatchHudView)} is missing the health-row template or its " +
                    "container; per-fighter health will not be shown.", this);
                return;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                _healthRowTemplate.CloneTree(roster);
                VisualElement row = roster[roster.childCount - 1];

                _nameLabels.Add(row.Q<Label>("name"));
                _healthFills.Add(row.Q<VisualElement>("fill"));

                int index = boxerIndex;
                boxer.Health.Subscribe(hp => OnHealthChanged(index, hp)).AddTo(_disposables);
                boxer.IsAlive.Subscribe(_ => RefreshSurvivors()).AddTo(_disposables);
            }
        }

        /// <summary>
        /// Writes whoever is currently seated beside each bar, falling back to the slot number
        /// when no card is in play - which is what the training scenes and any scene without
        /// fighter profiles get.
        /// </summary>
        private void RefreshNames()
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < _nameLabels.Count && index < boxers.Count; index++)
            {
                FighterProfile profile = _roster.SeatOf(boxers[index].Id);
                _nameLabels[index].text = profile != null
                    ? profile.DisplayName
                    : $"#{boxers[index].Id:00}";
            }
        }

        /// <summary>The seated contestant's name, or the slot number when there is no card.</summary>
        private string NameOf(int boxerId)
        {
            FighterProfile profile = _roster.SeatOf(boxerId);
            return profile != null ? profile.DisplayName : $"BOXER #{boxerId:00}";
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
                case MatchFlowPhase.Title:
                    _captionLabel.text = "PO RUMBLE";
                    _promptLabel.text = StartPrompt();
                    _resultLabel.text = string.Empty;
                    break;

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
            return IsTouchOnly() ? "TAP TO CONTINUE" : "PRESS  R  TO CONTINUE";
        }

        /// <summary>
        /// The menu prompt. Names the fight card as well as the fight, because the card is the
        /// only thing here a player can change and on a phone there is no key to discover.
        /// </summary>
        private static string StartPrompt()
        {
            return IsTouchOnly()
                ? "TAP TO FIGHT      FIGHT CARD BELOW"
                : "PRESS  ENTER  TO FIGHT      TAB  FOR THE CARD";
        }

        /// <summary>
        /// Chosen from the devices actually present rather than from a platform define, so the
        /// Editor still reads "PRESS" while a phone reads "TAP" - and so a desktop with a
        /// touchscreen does not get told to tap when it has a keyboard sitting right there.
        /// </summary>
        private static bool IsTouchOnly()
        {
            return UnityEngine.InputSystem.Touchscreen.current != null
                && UnityEngine.InputSystem.Keyboard.current == null;
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
                : $"{NameOf(message.WinnerId)} WINS";
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}

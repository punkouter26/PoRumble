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
    /// Match HUD: survivor count, the countdown to the bell and the result banner.
    ///
    /// It no longer draws a health bar per fighter. Ten named rows occupied the top third of a
    /// portrait screen and sat directly over the ring the fighters were moving through; health
    /// is now read off the fighters themselves, which pulse as they approach a knockout - see
    /// BoxerView. A bar chart of ten numbers is also the wrong instrument for the question a
    /// player actually asks, which is "who near me is nearly out".
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

        [Tooltip("The shared HUD stylesheet. Without it the panel renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);

        private MatchModel _match;
        private MatchFlowModel _flow;
        private RosterModel _roster;
        private Label _survivorsLabel;
        private Label _resultLabel;
        private Label _captionLabel;
        private Label _promptLabel;

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            RosterModel roster,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _match = match;
            _flow = flow;
            _roster = roster;
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

            WatchSurvivors();
            RefreshSurvivors();

            if (_flow != null)
            {
                _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
                _flow.CountdownSeconds.Subscribe(OnCountdownChanged).AddTo(_disposables);
            }
        }

        /// <summary>
        /// Subscribes to every boxer's alive flag so the survivor count stays current.
        ///
        /// This is all that is left of what used to build ten health rows. The count is the
        /// one number worth spending screen space on: how many are still standing is match
        /// state, whereas an individual fighter's health belongs on that fighter.
        /// </summary>
        private void WatchSurvivors()
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                boxers[boxerIndex].IsAlive
                    .Subscribe(_ => RefreshSurvivors())
                    .AddTo(_disposables);
            }
        }

        /// <summary>The seated contestant's name, or the slot number when there is no card.</summary>
        private string NameOf(int boxerId)
        {
            FighterProfile profile = _roster.SeatOf(boxerId);
            return profile != null ? profile.DisplayName : $"BOXER #{boxerId:00}";
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
                ? "TAP TO FIGHT      MENU FOR THE CARD"
                : "PRESS  ENTER  TO FIGHT      MENU  OR  TAB  FOR THE CARD";
        }

        /// <summary>
        /// Chosen from the devices actually present rather than from a platform define, so the
        /// Editor still reads "PRESS" while a phone reads "TAP" - and so a desktop with a
        /// touchscreen does not get told to tap when it has a keyboard sitting right there.
        ///
        /// The keyboard test alone was not enough, and on the one platform that ships it was
        /// exactly wrong: Android reports a Keyboard device whether or not any physical
        /// keyboard exists, so `Keyboard.current == null` is never true on a handset and the
        /// phone build told the player to press Enter. The device *class* is what separates
        /// the two cases - a handheld with a touchscreen is a phone, a desktop with one is
        /// still a desktop - and it is a runtime query rather than a compile-time define, so
        /// the distinction the comment above promises is kept.
        /// </summary>
        private static bool IsTouchOnly()
        {
            if (UnityEngine.InputSystem.Touchscreen.current == null)
            {
                return false;
            }

            return SystemInfo.deviceType == DeviceType.Handheld
                || UnityEngine.InputSystem.Keyboard.current == null;
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

using System.Collections.Generic;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The player's own state: health, breath, the haymaker meter, a hit flash and a marker
    /// for whoever is about to hit them from outside their guard.
    ///
    /// Stamina already governed the player's speed, punch rate and damage, and the counter
    /// window already governed how much their next punch was worth, but neither was shown
    /// anywhere. The effect was a fighter who felt arbitrarily weak with no way to learn why.
    ///
    /// Styling lives in the shared stylesheet; this class only sets class names and the values
    /// that genuinely change per frame.
    ///
    /// A View: it observes the player's model and never writes to it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlayerStatusHudView : MonoBehaviour
    {
        [Tooltip("The panel's structure. Without it the player HUD renders nothing.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("One captioned bar, cloned for health, breath and power.")]
        [SerializeField] private VisualTreeAsset _statBarTemplate;

        [Tooltip("The shared HUD stylesheet. Without it the panel renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("Stamina below this turns the bar red.")]
        [Range(0f, 1f)]
        [SerializeField] private float _lowStaminaThreshold = 0.3f;

        [Tooltip("Seconds the red damage vignette takes to fade after a hit.")]
        [SerializeField] private float _vignetteFadeSeconds = 0.45f;

        [Tooltip("Peak opacity of the damage wash.")]
        [Range(0f, 1f)]
        [SerializeField] private float _vignettePeak = 0.42f;

        [Tooltip("Opponents closer than this are called out as a threat.")]
        [SerializeField] private float _threatRange = 4f;

        private readonly CompositeDisposable _disposables = new();
        private CompositeDisposable _playerDisposables = new();

        private MatchModel _match;
        private MatchFlowModel _flow;
        private BoxerConfig _config;
        private BoxerSpawnPoints _spawnPoints;
        private BoxerModel _player;

        private VisualElement _root;
        private VisualElement _panel;
        private VisualElement _vignette;
        private VisualElement _healthFill;
        private VisualElement _staminaFill;
        private Label _title;
        private Label _threatLabel;
        private Label _counterLabel;

        private float _vignetteAlpha;
        private float _counterFlashRemaining;

        /// <summary>
        /// What the threat label currently says, so the string is rebuilt when the threat or
        /// its bearing changes rather than every frame someone stands behind the player.
        /// </summary>
        private int _shownThreatId = NO_THREAT;
        private string _shownThreatArrow;

        private const int NO_THREAT = -1;

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            BoxerConfig config,
            BoxerSpawnPoints spawnPoints,
            ISubscriber<BoxerDamagedMessage> damagedSubscriber,
            ISubscriber<PunchBlockedMessage> blockedSubscriber)
        {
            _match = match;
            _flow = flow;
            _config = config;
            _spawnPoints = spawnPoints;

            damagedSubscriber.Subscribe(OnBoxerDamaged).AddTo(_disposables);
            blockedSubscriber.Subscribe(OnPunchBlocked).AddTo(_disposables);
        }

        private void Start()
        {
            if (_match == null || _spawnPoints == null)
            {
                return;
            }

            _root = GetComponent<UIDocument>().rootVisualElement;

            if (_root == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                _root.styleSheets.Add(_styleSheet);
            }

            // The panel is built once and hidden rather than built on demand: who is playing is
            // chosen on the title screen now, and changes between one fight and the next.
            BuildPanel();
            _match.HumanBoxerId.Subscribe(_ => BindPlayer()).AddTo(_disposables);
        }

        /// <summary>Points the panel at whoever is playing now, or hides it when nobody is.</summary>
        private void BindPlayer()
        {
            _playerDisposables.Dispose();
            _playerDisposables = new CompositeDisposable();
            _player = FindPlayer();
            _vignetteAlpha = 0f;

            if (_panel == null)
            {
                return;
            }

            _panel.style.display = _player == null ? DisplayStyle.None : DisplayStyle.Flex;

            if (_player == null)
            {
                return;
            }

            if (_title != null)
            {
                _title.text = $"YOU  #{_player.Id:00}";
            }

            // Each of these reports its current value on subscription, so the bars are right
            // from the first frame.
            _player.Health.Subscribe(OnHealthChanged).AddTo(_playerDisposables);
            _player.Stamina.Subscribe(OnStaminaChanged).AddTo(_playerDisposables);
            _player.IsAlive.Subscribe(OnAliveChanged).AddTo(_playerDisposables);
        }

        private BoxerModel FindPlayer()
        {
            int humanId = _spawnPoints.HumanBoxerId;

            if (humanId < 0)
            {
                return null;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                if (boxers[boxerIndex].Id == humanId)
                {
                    return boxers[boxerIndex];
                }
            }

            return null;
        }

        /// <summary>
        /// Clones the layout and keeps the handful of elements that get written at runtime.
        ///
        /// The vignette and the panel both come from the one document: they are siblings, and
        /// the wash has to paint behind the readouts rather than over them.
        /// </summary>
        private void BuildPanel()
        {
            if (_layout == null)
            {
                GameLog.Error(
                    $"{nameof(PlayerStatusHudView)} has no layout assigned; the player HUD " +
                    "will not render. Assign Assets/UI/Layouts/PlayerStatusHud.uxml.", this);
                return;
            }

            _layout.CloneTree(_root);
            _root.pickingMode = PickingMode.Ignore;

            _vignette = _root.Q<VisualElement>("vignette");
            _panel = _root.Q<VisualElement>("panel");

            if (_panel == null)
            {
                return;
            }

            _title = _root.Q<Label>("title");

            VisualElement bars = _root.Q<VisualElement>("bars");

            // Two bars, and the same two on every device. Hit points are what is left before
            // the knockout; energy is spent by punching and comes back when the punching stops.
            _healthFill = AddBar(bars, "HIT POINTS", "bar__fill--healthy", "player-hud__bar--health");
            _staminaFill = AddBar(bars, "ENERGY", "bar__fill--stamina", "player-hud__bar--health");

            _counterLabel = _root.Q<Label>("counter");
            _threatLabel = _root.Q<Label>("threat");
        }

        /// <summary>
        /// Clones one captioned bar and returns its fill.
        ///
        /// The size and state classes are applied here rather than in the template because
        /// they are what distinguishes the three otherwise identical rows - and because the
        /// health fill's state class is rewritten every time the value crosses a threshold.
        /// </summary>
        private VisualElement AddBar(
            VisualElement parent, string caption, string fillClass, string sizeClass)
        {
            if (parent == null || _statBarTemplate == null)
            {
                GameLog.Error(
                    $"{nameof(PlayerStatusHudView)} is missing the stat-bar template or its " +
                    "container; the player's bars will not be shown.", this);
                return null;
            }

            _statBarTemplate.CloneTree(parent);
            VisualElement row = parent[parent.childCount - 1];

            Label label = row.Q<Label>("caption");

            if (label != null)
            {
                label.text = caption;
            }

            row.Q<VisualElement>("track")?.AddToClassList(sizeClass);

            VisualElement fill = row.Q<VisualElement>("fill");
            fill?.AddToClassList(fillClass);

            return fill;
        }

        private void OnHealthChanged(int health)
        {
            if (_healthFill == null)
            {
                return;
            }

            float ratio = Mathf.Clamp01(health / (float)Mathf.Max(1, _config.MaxHealth));
            _healthFill.style.width = Length.Percent(ratio * 100f);
            _healthFill.EnableInClassList("bar__fill--healthy", ratio > 0.5f);
            _healthFill.EnableInClassList("bar__fill--hurt", ratio <= 0.5f && ratio > 0.25f);
            _healthFill.EnableInClassList("bar__fill--critical", ratio <= 0.25f);
        }

        private void OnStaminaChanged(float stamina)
        {
            if (_staminaFill == null)
            {
                return;
            }

            _staminaFill.style.width = Length.Percent(Mathf.Clamp01(stamina) * 100f);
            _staminaFill.EnableInClassList("bar__fill--stamina", stamina > _lowStaminaThreshold);
            _staminaFill.EnableInClassList("bar__fill--stamina-low", stamina <= _lowStaminaThreshold);
        }

        private void OnAliveChanged(bool isAlive)
        {
            _panel?.EnableInClassList("player-hud--down", !isAlive);
        }

        private void OnBoxerDamaged(BoxerDamagedMessage message)
        {
            if (_player == null || message.BoxerId != _player.Id)
            {
                return;
            }

            _vignetteAlpha = _vignettePeak;
        }

        private void OnPunchBlocked(PunchBlockedMessage message)
        {
            if (_player == null || message.BlockerId != _player.Id)
            {
                return;
            }

            _counterFlashRemaining = _config.CounterWindowDuration;
        }

        /// <summary>
        /// Fades the hit flash and refreshes the spatial callouts.
        ///
        /// These are sampled per frame rather than observed, because positions are plain
        /// vectors on the model rather than reactive properties - they change every physics
        /// tick, and waking a subscriber on each one to redraw a label would cost far more
        /// than reading them here.
        /// </summary>
        private void LateUpdate()
        {
            if (_vignette != null && _vignetteAlpha > 0f)
            {
                _vignetteAlpha = Mathf.Max(
                    0f,
                    _vignetteAlpha - Time.unscaledDeltaTime / Mathf.Max(0.01f, _vignetteFadeSeconds));

                Color color = _vignette.resolvedStyle.backgroundColor;
                color.a = _vignetteAlpha;
                _vignette.style.backgroundColor = color;
            }

            if (_player == null || _threatLabel == null)
            {
                return;
            }

            if (_counterFlashRemaining > 0f)
            {
                _counterFlashRemaining -= Time.unscaledDeltaTime;
            }

            _counterLabel.text = _counterFlashRemaining > 0f ? "COUNTER READY" : string.Empty;

            RefreshThreat();
        }

        /// <summary>
        /// Calls out the nearest opponent that is not in front of the player.
        ///
        /// Punches only land inside the target's forward face arc, so an opponent behind you
        /// is the one who can actually hurt you - and, in a top-down ten-way brawl, the one
        /// you are least likely to have noticed.
        /// </summary>
        private void RefreshThreat()
        {
            if (!_player.IsAlive.Value || _flow == null || !_flow.IsFightLive)
            {
                ClearThreat();
                return;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            float bestSqr = _threatRange * _threatRange;
            BoxerModel threat = null;
            Vector2 facing = _player.Facing.normalized;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel other = boxers[boxerIndex];

                if (other.Id == _player.Id || !other.IsAlive.Value)
                {
                    continue;
                }

                Vector2 offset = other.Position - _player.Position;
                float sqr = offset.sqrMagnitude;

                if (sqr > bestSqr)
                {
                    continue;
                }

                // Only a concern if they are outside the arc the player is guarding.
                if (Vector2.Dot(facing, offset.normalized) > 0.4f)
                {
                    continue;
                }

                bestSqr = sqr;
                threat = other;
            }

            if (threat == null)
            {
                ClearThreat();
                return;
            }

            string arrow = ArrowFor(threat.Position - _player.Position);

            if (threat.Id == _shownThreatId && ReferenceEquals(arrow, _shownThreatArrow))
            {
                return;
            }

            _shownThreatId = threat.Id;
            _shownThreatArrow = arrow;
            _threatLabel.text = $"{arrow}  #{threat.Id:00} BEHIND YOU";
        }

        private void ClearThreat()
        {
            if (_shownThreatId == NO_THREAT)
            {
                return;
            }

            _shownThreatId = NO_THREAT;
            _shownThreatArrow = null;
            _threatLabel.text = string.Empty;
        }

        /// <summary>Nearest of eight compass arrows for a world-space direction.</summary>
        private static string ArrowFor(Vector2 direction)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return "*";
            }

            float degrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            if (degrees < 0f)
            {
                degrees += 360f;
            }

            int octant = Mathf.RoundToInt(degrees / 45f) % 8;

            switch (octant)
            {
                case 0: return "-> E";
                case 1: return "/ NE";
                case 2: return "^ N";
                case 3: return "\\ NW";
                case 4: return "<- W";
                case 5: return "/ SW";
                case 6: return "v S";
                default: return "\\ SE";
            }
        }

        private void OnDestroy()
        {
            _playerDisposables.Dispose();
            _disposables.Dispose();
        }
    }
}

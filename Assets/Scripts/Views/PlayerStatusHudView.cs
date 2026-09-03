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
        private VisualElement _chargeFill;
        private Label _threatLabel;
        private Label _counterLabel;

        private float _vignetteAlpha;
        private float _counterFlashRemaining;

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

            _player = FindPlayer();
            _root = GetComponent<UIDocument>().rootVisualElement;

            if (_root == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                _root.styleSheets.Add(_styleSheet);
            }

            // No human in this scene - a training arena, or an all-AI exhibition. Nothing is
            // built at all rather than showing an empty set of bars. The damage vignette goes
            // with the panel: it lives in the same document, and with nobody to take damage
            // there was never anything to drive it.
            if (_player == null)
            {
                return;
            }

            BuildPanel();

            _player.Health.Subscribe(OnHealthChanged).AddTo(_disposables);
            _player.Stamina.Subscribe(OnStaminaChanged).AddTo(_disposables);
            _player.Charge.Subscribe(OnChargeChanged).AddTo(_disposables);
            _player.IsAlive.Subscribe(OnAliveChanged).AddTo(_disposables);

            OnHealthChanged(_player.Health.Value);
            OnStaminaChanged(_player.Stamina.Value);
            OnChargeChanged(_player.Charge.Value);
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
                Debug.LogError(
                    $"{nameof(PlayerStatusHudView)} has no layout assigned; the player HUD " +
                    "will not render. Assign Assets/UI/Layouts/PlayerStatusHud.uxml.", this);
                return;
            }

            _layout.CloneTree(_root);

            _vignette = _root.Q<VisualElement>("vignette");
            _panel = _root.Q<VisualElement>("panel");

            if (_panel == null)
            {
                return;
            }

            // The panel's usual home is bottom-left, which is exactly where the virtual stick
            // lives. On a touch device it moves up out of the way.
            _panel.EnableInClassList(
                "player-hud--touch", UnityEngine.InputSystem.Touchscreen.current != null);

            Label title = _root.Q<Label>("title");

            if (title != null)
            {
                title.text = $"YOU  #{_player.Id:00}";
            }

            VisualElement bars = _root.Q<VisualElement>("bars");

            _healthFill = AddBar(bars, "HEALTH", "bar__fill--healthy", "player-hud__bar--health");
            _staminaFill = AddBar(bars, "BREATH", "bar__fill--stamina", "player-hud__bar--minor");
            _chargeFill = AddBar(bars, "POWER", "bar__fill--charge", "player-hud__bar--minor");

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
                Debug.LogError(
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

        private void OnChargeChanged(float charge)
        {
            if (_chargeFill == null)
            {
                return;
            }

            _chargeFill.style.width = Length.Percent(Mathf.Clamp01(charge) * 100f);

            // Turns colour the moment the wind-up is worth releasing, so the player can see
            // when a hold has become a haymaker rather than having to count seconds.
            bool ready = charge >= _config.MinChargeToRelease;
            _chargeFill.EnableInClassList("bar__fill--charge", !ready);
            _chargeFill.EnableInClassList("bar__fill--charge-ready", ready);
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
                _threatLabel.text = string.Empty;
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
                _threatLabel.text = string.Empty;
                return;
            }

            Vector2 toThreat = threat.Position - _player.Position;
            _threatLabel.text = $"{ArrowFor(toThreat)}  #{threat.Id:00} BEHIND YOU";
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

        private void OnDestroy() => _disposables.Dispose();
    }
}

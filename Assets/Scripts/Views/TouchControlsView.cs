using System.Collections.Generic;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// On-screen controls for phones: a floating stick for movement and aim, a punch button and
    /// a haymaker button.
    ///
    /// A View, per the input rules: it reads pointers and writes <see cref="TouchInputModel"/>.
    /// It never touches a system or a boxer, because movement and punches have to travel
    /// through the agent's action buffer rather than being applied directly.
    ///
    /// The stick is floating rather than fixed: the touch that starts it becomes its centre, so
    /// a thumb landing anywhere in the left half is immediately in control instead of having to
    /// find a painted circle first.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class TouchControlsView : MonoBehaviour
    {
        [Tooltip("The control structure. Without it the controls do not render at all.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("The shared HUD stylesheet. Without it the controls render unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("How far the thumb travels for full deflection, as a fraction of the shorter " +
                 "screen edge. Small enough to reach without shifting grip.")]
        [Range(0.05f, 0.4f)]
        [SerializeField] private float _stickRadiusFraction = 0.16f;

        [Tooltip("Deflection below this is treated as no input, so resting a thumb does not " +
                 "walk the boxer into a corner.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _deadZone = 0.15f;

        [Tooltip("Show the controls even when no touchscreen is present. For testing in the " +
                 "Editor, where the mouse drives them.")]
        [SerializeField] private bool _forceVisible;

        [Header("Meter rings")]
        [Tooltip("Stroke width of the breath and charge rings, in reference pixels.")]
        [SerializeField] private float _ringWidth = 10f;

        [Tooltip("The unfilled part of a ring.")]
        [SerializeField] private Color _ringTrackColor = new(0f, 0f, 0f, 0.45f);

        [Tooltip("Breath above the low threshold.")]
        [SerializeField] private Color _breathColor = new(0.45f, 0.72f, 0.95f);

        [Tooltip("Breath at or below the low threshold - matches the player panel's red.")]
        [SerializeField] private Color _breathLowColor = new(0.92f, 0.32f, 0.28f);

        [Tooltip("Breath below this turns the ring red.")]
        [Range(0f, 1f)]
        [SerializeField] private float _lowBreathThreshold = 0.3f;

        [Tooltip("The haymaker winding up.")]
        [SerializeField] private Color _chargeColor = new(0.98f, 0.78f, 0.25f);

        [Tooltip("The haymaker worth releasing.")]
        [SerializeField] private Color _chargeReadyColor = new(1f, 0.42f, 0.2f);

        private readonly CompositeDisposable _disposables = new();

        private TouchInputModel _touch;
        private MatchFlowModel _flow;
        private BoxerSpawnPoints _spawnPoints;
        private MatchModel _match;
        private BoxerConfig _config;
        private BoxerModel _player;
        private VisualElement _punchButton;
        private VisualElement _chargeButton;

        private VisualElement _root;
        private VisualElement _stickZone;
        private VisualElement _stickBase;
        private VisualElement _stickKnob;

        private int _stickPointerId = -1;
        private Vector2 _stickOrigin;
        private float _stickRadius = 120f;

        [Inject]
        public void Construct(
            TouchInputModel touch,
            MatchFlowModel flow,
            BoxerSpawnPoints spawnPoints,
            MatchModel match,
            BoxerConfig config)
        {
            _touch = touch;
            _flow = flow;
            _spawnPoints = spawnPoints;
            _match = match;
            _config = config;
        }

        private void Start()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            if (_root == null || _touch == null || _layout == null)
            {
                return;
            }

            // No human boxer means nothing for these controls to drive - the all-AI exhibition
            // should not have a dead stick sitting over it.
            bool hasPlayer = _spawnPoints == null || _spawnPoints.HumanBoxerId >= 0;
            bool wanted = _forceVisible || Touchscreen.current != null;

            if (!hasPlayer || !wanted)
            {
                _root.style.display = DisplayStyle.None;
                return;
            }

            if (_styleSheet != null)
            {
                _root.styleSheets.Add(_styleSheet);
            }

            _layout.CloneTree(_root);
            _root.pickingMode = PickingMode.Ignore;
            _touch.IsActive = true;

            BindStick();
            BindButtons();
            BindRings();
        }

        /// <summary>
        /// Draws breath around PUNCH and the haymaker charge around POWER.
        ///
        /// Those two meters used to be bars in the player's panel, which on a phone meant looking
        /// away from both thumbs and the fight to read them. Around the buttons they are where
        /// the player is already looking when the numbers matter: breath is what a punch costs,
        /// and the charge ring is the one that says when to let go. Health is not drawn here - it
        /// is the player's own cell on the field strip, marked in blue.
        /// </summary>
        private void BindRings()
        {
            _player = FindPlayer();

            if (_player == null)
            {
                return;
            }

            _punchButton = _root.Q<VisualElement>("punch");
            _chargeButton = _root.Q<VisualElement>("charge");

            if (_punchButton != null)
            {
                _punchButton.generateVisualContent += DrawBreathRing;
                _player.Stamina.Subscribe(_ => _punchButton.MarkDirtyRepaint()).AddTo(_disposables);
            }

            if (_chargeButton != null)
            {
                _chargeButton.generateVisualContent += DrawChargeRing;
                _player.Charge.Subscribe(_ => _chargeButton.MarkDirtyRepaint()).AddTo(_disposables);
            }
        }

        private BoxerModel FindPlayer()
        {
            if (_spawnPoints == null || _match == null)
            {
                return null;
            }

            int humanId = _spawnPoints.HumanBoxerId;
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < boxers.Count; index++)
            {
                if (boxers[index].Id == humanId)
                {
                    return boxers[index];
                }
            }

            return null;
        }

        private void DrawBreathRing(MeshGenerationContext context)
        {
            float breath = Mathf.Clamp01(_player.Stamina.Value);
            DrawRing(context, breath, breath <= _lowBreathThreshold ? _breathLowColor : _breathColor);
        }

        private void DrawChargeRing(MeshGenerationContext context)
        {
            float charge = Mathf.Clamp01(_player.Charge.Value);
            bool ready = _config != null && charge >= _config.MinChargeToRelease;
            DrawRing(context, charge, ready ? _chargeReadyColor : _chargeColor);
        }

        /// <summary>
        /// A full track and a filled arc from twelve o'clock, clockwise. Drawn just inside the
        /// button's edge so the ring shows around a thumb resting in the middle of it.
        /// </summary>
        private void DrawRing(MeshGenerationContext context, float fraction, Color color)
        {
            Rect bounds = context.visualElement.contentRect;
            float radius = Mathf.Min(bounds.width, bounds.height) * 0.5f - _ringWidth * 0.5f - 2f;

            if (radius <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            Vector2 centre = bounds.center;

            painter.lineWidth = _ringWidth;
            painter.strokeColor = _ringTrackColor;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();

            if (fraction <= 0f)
            {
                return;
            }

            painter.strokeColor = color;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * fraction));
            painter.Stroke();
        }

        /// <summary>
        /// Finds the stick in the cloned layout and wires the pointer callbacks to it.
        ///
        /// The whole left half is the stick's catchment; the drawn circle only appears where
        /// the thumb actually lands, which is why the base and knob start hidden in the layout
        /// and are positioned from code on pointer-down.
        /// </summary>
        private void BindStick()
        {
            _stickZone = _root.Q<VisualElement>("stick-zone");
            _stickBase = _root.Q<VisualElement>("stick-base");
            _stickKnob = _root.Q<VisualElement>("stick-knob");

            if (_stickZone == null || _stickBase == null || _stickKnob == null)
            {
                return;
            }

            _stickZone.RegisterCallback<PointerDownEvent>(OnStickDown);
            _stickZone.RegisterCallback<PointerMoveEvent>(OnStickMove);
            _stickZone.RegisterCallback<PointerUpEvent>(OnStickUp);
            _stickZone.RegisterCallback<PointerCancelEvent>(OnStickUp);
        }

        private void BindButtons()
        {
            BindHoldButton("punch", held => _touch.PunchHeld = held);
            BindHoldButton("charge", held => _touch.ChargeHeld = held);

            // The slip is an edge, not a hold: it has its own window and cooldown, so a held
            // thumb must not queue a stream of them. Raised on press and left for the agent
            // to consume on the next frame.
            BindHoldButton("dodge", held =>
            {
                if (held)
                {
                    _touch.DodgeRequested = true;
                }
            });
        }

        /// <summary>
        /// Wires one button from the layout to report press and release rather than a click,
        /// because both punching and charging are held actions.
        /// </summary>
        private void BindHoldButton(string elementName, System.Action<bool> setHeld)
        {
            VisualElement button = _root.Q<VisualElement>(elementName);

            if (button == null)
            {
                return;
            }

            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                // Capture so a thumb that slides off the button still releases it here, rather
                // than the press sticking on forever.
                button.CapturePointer(evt.pointerId);
                button.AddToClassList("touch-button--down");
                setHeld(true);
                evt.StopPropagation();
            });

            void Release(IPointerEvent evt)
            {
                if (button.HasPointerCapture(evt.pointerId))
                {
                    button.ReleasePointer(evt.pointerId);
                }

                button.RemoveFromClassList("touch-button--down");
                setHeld(false);
            }

            button.RegisterCallback<PointerUpEvent>(evt => { Release(evt); evt.StopPropagation(); });
            button.RegisterCallback<PointerCancelEvent>(evt => { Release(evt); evt.StopPropagation(); });
        }

        private void OnStickDown(PointerDownEvent evt)
        {
            if (_stickPointerId >= 0)
            {
                return;
            }

            _stickPointerId = evt.pointerId;
            _stickZone.CapturePointer(evt.pointerId);

            // Radius is derived from the shorter screen edge so the stick is the same physical
            // size in portrait and landscape.
            _stickRadius = Mathf.Min(_root.resolvedStyle.width, _root.resolvedStyle.height)
                           * _stickRadiusFraction;

            _stickOrigin = evt.localPosition;
            PlaceStick(_stickBase, _stickOrigin, _stickRadius * 2f);
            PlaceStick(_stickKnob, _stickOrigin, _stickRadius * 0.9f);

            _stickBase.style.display = DisplayStyle.Flex;
            _stickKnob.style.display = DisplayStyle.Flex;

            evt.StopPropagation();
        }

        private void OnStickMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _stickPointerId)
            {
                return;
            }

            Vector2 local = evt.localPosition;
            Vector2 delta = local - _stickOrigin;

            // UI Toolkit's Y grows downward; the world does not.
            Vector2 direction = new(delta.x, -delta.y);
            float deflection = Mathf.Clamp01(direction.magnitude / Mathf.Max(1f, _stickRadius));

            _touch.Move = deflection < _deadZone
                ? Vector2.zero
                : direction.normalized * deflection;

            Vector2 knob = _stickOrigin + Vector2.ClampMagnitude(delta, _stickRadius);
            PlaceStick(_stickKnob, knob, _stickRadius * 0.9f);

            evt.StopPropagation();
        }

        private void OnStickUp(IPointerEvent evt)
        {
            if (evt.pointerId != _stickPointerId)
            {
                return;
            }

            if (_stickZone.HasPointerCapture(evt.pointerId))
            {
                _stickZone.ReleasePointer(evt.pointerId);
            }

            _stickPointerId = -1;
            _touch.Move = Vector2.zero;
            _stickBase.style.display = DisplayStyle.None;
            _stickKnob.style.display = DisplayStyle.None;
        }

        private static void PlaceStick(VisualElement element, Vector2 center, float diameter)
        {
            element.style.width = diameter;
            element.style.height = diameter;
            element.style.left = center.x - diameter * 0.5f;
            element.style.top = center.y - diameter * 0.5f;
        }

        /// <summary>
        /// Drops any held input while the fight is not live, so a thumb still resting on the
        /// punch button through the results screen does not carry into the next match.
        /// </summary>
        private void Update()
        {
            if (_touch == null || !_touch.IsActive || _flow == null)
            {
                return;
            }

            if (!_flow.IsFightLive && _stickPointerId < 0)
            {
                _touch.Move = Vector2.zero;
            }
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            if (_punchButton != null)
            {
                _punchButton.generateVisualContent -= DrawBreathRing;
            }

            if (_chargeButton != null)
            {
                _chargeButton.generateVisualContent -= DrawChargeRing;
            }

            if (_touch != null)
            {
                _touch.IsActive = false;
                _touch.Clear();
            }
        }
    }
}

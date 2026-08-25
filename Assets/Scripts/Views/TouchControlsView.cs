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

        private TouchInputModel _touch;
        private MatchFlowModel _flow;
        private BoxerSpawnPoints _spawnPoints;

        private VisualElement _root;
        private VisualElement _stickZone;
        private VisualElement _stickBase;
        private VisualElement _stickKnob;

        private int _stickPointerId = -1;
        private Vector2 _stickOrigin;
        private float _stickRadius = 120f;

        [Inject]
        public void Construct(TouchInputModel touch, MatchFlowModel flow, BoxerSpawnPoints spawnPoints)
        {
            _touch = touch;
            _flow = flow;
            _spawnPoints = spawnPoints;
        }

        private void Start()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            if (_root == null || _touch == null)
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

            _touch.IsActive = true;

            BuildStick();
            BuildButtons();
        }

        private void BuildStick()
        {
            // The whole left half is the stick's catchment; the drawn circle only appears where
            // the thumb actually lands.
            _stickZone = new VisualElement();
            _stickZone.AddToClassList("touch-stick-zone");
            _root.Add(_stickZone);

            _stickBase = new VisualElement();
            _stickBase.AddToClassList("touch-stick__base");
            _stickBase.pickingMode = PickingMode.Ignore;
            _stickBase.style.display = DisplayStyle.None;
            _stickZone.Add(_stickBase);

            _stickKnob = new VisualElement();
            _stickKnob.AddToClassList("touch-stick__knob");
            _stickKnob.pickingMode = PickingMode.Ignore;
            _stickKnob.style.display = DisplayStyle.None;
            _stickZone.Add(_stickKnob);

            _stickZone.RegisterCallback<PointerDownEvent>(OnStickDown);
            _stickZone.RegisterCallback<PointerMoveEvent>(OnStickMove);
            _stickZone.RegisterCallback<PointerUpEvent>(OnStickUp);
            _stickZone.RegisterCallback<PointerCancelEvent>(OnStickUp);
        }

        private void BuildButtons()
        {
            VisualElement cluster = new();
            cluster.AddToClassList("touch-buttons");
            _root.Add(cluster);

            // One punch button, not two. BoxerSystem already falls through to whichever arm is
            // free, so a held button alternates left and right on its own - and with only one
            // fist allowed out at a time, a second button would have nothing to do.
            AddHoldButton(cluster, "PUNCH", "touch-button--punch", held => _touch.PunchHeld = held);
            AddHoldButton(cluster, "POWER", "touch-button--charge", held => _touch.ChargeHeld = held);
        }

        /// <summary>
        /// A button that reports press and release rather than a click, because both punching
        /// and charging are held actions.
        /// </summary>
        private void AddHoldButton(VisualElement parent, string caption, string modifier, System.Action<bool> setHeld)
        {
            VisualElement button = new();
            button.AddToClassList("touch-button");
            button.AddToClassList(modifier);

            Label label = new(caption);
            label.AddToClassList("touch-button__label");
            label.pickingMode = PickingMode.Ignore;
            button.Add(label);

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

            parent.Add(button);
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
            if (_touch != null)
            {
                _touch.IsActive = false;
                _touch.Clear();
            }
        }
    }
}

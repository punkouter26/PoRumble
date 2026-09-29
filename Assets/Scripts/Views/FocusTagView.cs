using System.Collections.Generic;
using System.Text;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// A name tag over the fighter the camera is following: "FOLLOWING ALAN", or "PINNED ALAN"
    /// once the viewer has asked to stay on them.
    ///
    /// The director picks who to follow by itself, and in a ten-way that was invisible - the
    /// camera drifted to a fight and nothing said whose. Tapping the tag pins that fighter, and
    /// tapping it again lets the director choose again; the field board's rows pin too, which is
    /// how to follow somebody the camera is not already on.
    ///
    /// Positioned in LateUpdate, after the camera has moved, by projecting the fighter's head
    /// into panel space. Only rewrites the position when it has moved by a pixel or more.
    ///
    /// A View: reads the director's decision and forwards a tap to DirectorSystem.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class FocusTagView : MonoBehaviour
    {
        private const string SHOWN_CLASS = "focus-tag--on";
        private const string PINNED_CLASS = "focus-tag--pinned";

        [Tooltip("The tag's structure.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("The shared HUD stylesheet.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("World units above the fighter's centre that the tag points at - clear of the head and gloves.")]
        [SerializeField] private float _headroom = 1f;

        [Tooltip("Panel pixels from the top of the safe area that the tag never rises above: the " +
                 "fight strip lives there, and a tag drawn over it covers the fighters' health.")]
        [SerializeField] private float _topClearance = 250f;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(32);

        private MatchModel _match;
        private MatchFlowModel _flow;
        private DirectorModel _director;
        private DirectorSystem _directorSystem;
        private RosterModel _roster;

        private Camera _camera;
        private VisualElement _root;
        private Label _tag;

        private int _shownId = DirectorModel.NOBODY;
        private bool _shownPinned;
        private Vector2 _shownPosition = new(float.NaN, float.NaN);

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            DirectorModel director,
            DirectorSystem directorSystem,
            RosterModel roster)
        {
            _match = match;
            _flow = flow;
            _director = director;
            _directorSystem = directorSystem;
            _roster = roster;
        }

        private void Awake()
        {
            _camera = Camera.main;
        }

        private void Start()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            if (_root == null || _layout == null || _match == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                _root.styleSheets.Add(_styleSheet);
            }

            _layout.CloneTree(_root);
            _root.pickingMode = PickingMode.Ignore;

            _tag = _root.Q<Label>("tag");

            if (_tag != null)
            {
                _tag.RegisterCallback<ClickEvent>(_ => OnTagClicked());
            }
        }

        private void LateUpdate()
        {
            if (_tag == null || _camera == null)
            {
                return;
            }

            BoxerModel focus = FollowedFighter();

            if (focus == null)
            {
                Hide();
                return;
            }

            bool pinned = _director.PinnedId.Value == focus.Id;

            if (focus.Id != _shownId || pinned != _shownPinned)
            {
                _shownId = focus.Id;
                _shownPinned = pinned;

                _builder.Clear();
                _builder.Append(pinned ? "PINNED  " : "FOLLOWING  ").Append(_roster.SeatLabel(focus.Id) ?? $"#{focus.Id:00}");
                _tag.text = _builder.ToString();
                _tag.EnableInClassList(PINNED_CLASS, pinned);
            }

            Vector3 world = new(focus.Position.x, focus.Position.y + _headroom, 0f);

            // A fighter out of shot has nothing on screen to label. The camera is usually on
            // them, but a corner clamp or a slow pan can leave them at or past the edge.
            Vector3 viewport = _camera.WorldToViewportPoint(world);

            if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
            {
                Hide();
                return;
            }

            Vector2 panel = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, world, _camera);

            // The root is inset by the safe area, and the tag is positioned inside the root. Kept
            // inside it: the tag hangs centred above its point, so near an edge it would run off
            // the side, and near the top it would climb over the fight strip and the chrome.
            Vector2 local = panel - _root.worldBound.position;
            float halfWidth = _tag.resolvedStyle.width * 0.5f;
            float height = _tag.resolvedStyle.height;
            float rootWidth = _root.resolvedStyle.width;

            if (!float.IsNaN(halfWidth) && !float.IsNaN(rootWidth) && rootWidth > 2f * halfWidth)
            {
                local.x = Mathf.Clamp(local.x, halfWidth, rootWidth - halfWidth);
            }

            local.y = Mathf.Max(local.y, _topClearance + (float.IsNaN(height) ? 0f : height));

            if (float.IsNaN(_shownPosition.x) || (local - _shownPosition).sqrMagnitude >= 1f)
            {
                _shownPosition = local;
                _tag.style.left = local.x;
                _tag.style.top = local.y;
            }

            _tag.AddToClassList(SHOWN_CLASS);
            _tag.pickingMode = PickingMode.Position;
        }

        /// <summary>
        /// The fighter the camera is actually on, or null when it is not following anybody: the
        /// camera only frames a focus on a tracking or tighter shot, and a tag over a fighter on
        /// the wide shot would claim a follow that is not happening.
        /// </summary>
        private BoxerModel FollowedFighter()
        {
            // Impact cuts to a second camera; a tag projected through this one would point at
            // the wrong place for the length of the cut.
            if (!_flow.IsFightLive || _director.Shot.Value == ShotType.Impact)
            {
                return null;
            }

            bool pinned = _director.PinnedId.Value != DirectorModel.NOBODY;

            if (!pinned && _director.Shot.Value == ShotType.Wide)
            {
                return null;
            }

            int id = pinned ? _director.PinnedId.Value : _director.FocusId;
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < boxers.Count; index++)
            {
                if (boxers[index].Id == id && boxers[index].IsAlive.Value)
                {
                    return boxers[index];
                }
            }

            return null;
        }

        private void OnTagClicked()
        {
            if (_shownId != DirectorModel.NOBODY)
            {
                _directorSystem.TogglePin(_shownId);
            }
        }

        private void Hide()
        {
            if (_shownId == DirectorModel.NOBODY)
            {
                return;
            }

            _shownId = DirectorModel.NOBODY;
            _tag.RemoveFromClassList(SHOWN_CLASS);
            _tag.pickingMode = PickingMode.Ignore;
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}

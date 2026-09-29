using System.Collections.Generic;
using System.Text;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The second fight: a corner feed on the best exchange the main camera is not showing.
    ///
    /// In a ten-way the best action is regularly off camera - the director can only hold one
    /// pair, and while it watches the scrap it chose, another fighter is being stopped on the
    /// far side of the ring. This is the multi-view answer a broadcast uses: which pair is the
    /// director's call (<see cref="DirectorModel.SecondFocusId"/>, chosen never to share a
    /// fighter with the main pair); this frames them and puts the picture in the corner.
    ///
    /// A plain Camera rendering into a small texture rather than a second Cinemachine camera.
    /// The brain on the main camera would take any CinemachineCamera by priority, and this
    /// feed must never compete for the main picture. It is enabled only while the feed is up,
    /// so the second render pass costs nothing the rest of the time.
    ///
    /// Off on mobile. The portrait phone build keeps its thumb controls in that corner, and a
    /// second pass of the lit 2D renderer is the wrong thing to spend a phone's budget on.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PictureInPictureView : MonoBehaviour
    {
        [Tooltip("The feed's structure. Without it nothing renders.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("The shared HUD stylesheet. Without it the feed renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("The camera that films the second fight. Must not carry the MainCamera tag, a " +
                 "CinemachineBrain or an AudioListener - it is a picture, not a viewpoint.")]
        [SerializeField] private Camera _feedCamera;

        [Tooltip("Render texture size. 16:9 to match the frame the layout draws.")]
        [SerializeField] private Vector2Int _resolution = new(640, 360);

        [Tooltip("World units of air around the pair.")]
        [SerializeField] private float _framingPadding = 2.4f;

        [Tooltip("Closest the feed pulls in. Tighter than the main camera: a corner feed is " +
                 "small, and fighters framed at the main camera's zoom would be specks in it.")]
        [SerializeField] private float _minOrthographicSize = 3.2f;

        [Tooltip("How quickly the feed follows its pair. Unscaled, like every other camera here.")]
        [SerializeField] private float _damping = 5f;

        [Tooltip("Leave the feed off on phones. See the class summary for why.")]
        [SerializeField] private bool _disableOnMobile = true;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(48);

        private MatchModel _match;
        private RosterModel _roster;
        private DirectorModel _director;

        private RenderTexture _texture;
        private VisualElement _panel;
        private VisualElement _feed;
        private Label _caption;

        private int _shownA = DirectorModel.NOBODY;
        private int _shownB = DirectorModel.NOBODY;
        private bool _showing;

        [Inject]
        public void Construct(MatchModel match, RosterModel roster, DirectorModel director)
        {
            _match = match;
            _roster = roster;
            _director = director;
        }

        private void Start()
        {
            if (_feedCamera != null)
            {
                _feedCamera.enabled = false;
            }

            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _director == null || _feedCamera == null)
            {
                return;
            }

            if (_disableOnMobile && Application.isMobilePlatform)
            {
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            root.pickingMode = PickingMode.Ignore;

            if (_layout == null)
            {
                Debug.LogError(
                    $"{nameof(PictureInPictureView)} has no layout assigned; the second-fight " +
                    "feed will not render. Assign Assets/UI/Layouts/PictureInPicture.uxml.", this);
                return;
            }

            _layout.CloneTree(root);
            _panel = root.Q<VisualElement>("panel");
            _feed = root.Q<VisualElement>("feed");
            _caption = root.Q<Label>("caption");

            _texture = new RenderTexture(_resolution.x, _resolution.y, 16)
            {
                name = "SecondFightFeed"
            };

            _feedCamera.targetTexture = _texture;

            if (_feed != null)
            {
                _feed.style.backgroundImage = Background.FromRenderTexture(_texture);

                // Height from width, so the frame keeps the texture's aspect at any panel
                // width. USS has no aspect-ratio property to say this declaratively.
                _feed.RegisterCallback<GeometryChangedEvent>(OnFeedResized);
            }

            _director.ShowSecond.Subscribe(OnShowChanged).AddTo(_disposables);
        }

        /// <summary>Framed in LateUpdate, after the fighters have moved this frame - the same reason the main camera is.</summary>
        private void LateUpdate()
        {
            if (!_showing)
            {
                return;
            }

            BoxerModel a = FindAlive(_director.SecondFocusId);
            BoxerModel b = FindAlive(_director.SecondRivalId);

            if (a == null || b == null)
            {
                return;
            }

            bool pairChanged = !IsShownPair(a.Id, b.Id);

            if (pairChanged)
            {
                _shownA = a.Id;
                _shownB = b.Id;
                RefreshCaption();
            }

            Vector2 centre = (a.Position + b.Position) * 0.5f;
            Vector2 span = b.Position - a.Position;
            float aspect = _resolution.x / (float)Mathf.Max(1, _resolution.y);

            // Nested rather than the params overload, which allocates an array every frame.
            float size = Mathf.Max(
                _minOrthographicSize,
                Mathf.Max(
                    Mathf.Abs(span.y) * 0.5f + _framingPadding,
                    (Mathf.Abs(span.x) * 0.5f + _framingPadding) / aspect));

            centre = ClampToRing(centre, size, aspect);

            Transform cameraTransform = _feedCamera.transform;
            Vector3 wanted = new(centre.x, centre.y, cameraTransform.position.z);

            // A new pair is a cut, not a pan: sliding the feed across the ring from the last
            // fight to this one would show the travel a cut exists to skip.
            if (pairChanged)
            {
                cameraTransform.position = wanted;
                _feedCamera.orthographicSize = size;
                return;
            }

            float blend = 1f - Mathf.Exp(-_damping * Time.unscaledDeltaTime);
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, wanted, blend);
            _feedCamera.orthographicSize = Mathf.Lerp(_feedCamera.orthographicSize, size, blend);
        }

        /// <summary>
        /// Keeps the feed's frame inside the ropes, the rule SpectatorCameraView applies to the
        /// main picture. A pair fighting on the ropes otherwise puts half the feed on the black
        /// beyond the ring, and a corner feed has no area to spend on nothing. Centred on an
        /// axis where the frame is wider than the ring, since there is nothing to pan to.
        /// </summary>
        private Vector2 ClampToRing(Vector2 centre, float orthographicSize, float aspect)
        {
            Vector2 bounds = _match.ArenaHalfExtent;
            float halfHeight = orthographicSize;
            float halfWidth = orthographicSize * aspect;

            centre.x = halfWidth >= bounds.x
                ? 0f
                : Mathf.Clamp(centre.x, -bounds.x + halfWidth, bounds.x - halfWidth);

            centre.y = halfHeight >= bounds.y
                ? 0f
                : Mathf.Clamp(centre.y, -bounds.y + halfHeight, bounds.y - halfHeight);

            return centre;
        }

        private void OnShowChanged(bool show)
        {
            _showing = show;
            _feedCamera.enabled = show;

            if (_panel != null)
            {
                _panel.EnableInClassList("pip--hidden", !show);
            }

            // Forget the last pair so the first frame of a reopened feed cuts straight to its
            // fight instead of easing in from wherever the last one was.
            _shownA = DirectorModel.NOBODY;
            _shownB = DirectorModel.NOBODY;
        }

        private void OnFeedResized(GeometryChangedEvent change)
        {
            float width = change.newRect.width;

            if (width <= 0f)
            {
                return;
            }

            float height = width * _resolution.y / Mathf.Max(1, _resolution.x);

            if (Mathf.Abs(_feed.resolvedStyle.height - height) > 0.5f)
            {
                _feed.style.height = height;
            }
        }

        private void RefreshCaption()
        {
            if (_caption == null)
            {
                return;
            }

            _builder.Clear();
            _builder.Append("ALSO IN THE RING   ");
            AppendName(_shownA);
            _builder.Append("  VS  ");
            AppendName(_shownB);
            _caption.text = _builder.ToString();
        }

        private void AppendName(int boxerId)
        {
            string label = _roster.SeatLabel(boxerId);

            if (label != null)
            {
                _builder.Append(label);
                return;
            }

            _builder.Append('#').Append(boxerId.ToString("00"));
        }

        private bool IsShownPair(int idA, int idB)
        {
            return (_shownA == idA && _shownB == idB) || (_shownA == idB && _shownB == idA);
        }

        private BoxerModel FindAlive(int boxerId)
        {
            if (boxerId == DirectorModel.NOBODY)
            {
                return null;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < boxers.Count; index++)
            {
                if (boxers[index].Id == boxerId && boxers[index].IsAlive.Value)
                {
                    return boxers[index];
                }
            }

            return null;
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            if (_feedCamera != null)
            {
                _feedCamera.targetTexture = null;
            }

            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }
    }
}

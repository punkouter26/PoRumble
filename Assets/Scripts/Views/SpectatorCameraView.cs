using System.Collections.Generic;
using PoRumble.Models;
using Unity.Cinemachine;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Frames the fight. Picks one exchange to watch and follows it, rather than trying to hold
    /// every fighter on screen at once.
    ///
    /// The camera was previously a fixed transform pointed at the whole 40x40 ring, which meant
    /// the fighters were always small and always far away no matter how few were left. Fitting
    /// the bounding box of everyone still standing fixed that only for the last two: with ten
    /// boxers scattered over a 40x40 ring the box is the ring, so the camera sat at its widest
    /// for most of a match and the fighters were a few pixels tall - unwatchable, which is the
    /// state this replaces.
    ///
    /// So it picks a focus and frames the scrap around them. The focus is the human when there
    /// is one, and otherwise the living boxer closest to being knocked out - that is where the
    /// next elimination is coming from, and it is the most interesting thing on the canvas.
    ///
    /// Deliberately drives only a follow target and an orthographic size rather than using a
    /// CinemachineTargetGroup: the bounding box is computed straight from the models, which
    /// are the authority on where boxers are, and Cinemachine still supplies the damping.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpectatorCameraView : MonoBehaviour
    {
        [Tooltip("The camera whose orthographic size is driven. Optional: leave empty to " +
                 "move the follow target without rescaling.")]
        [SerializeField] private CinemachineCamera _camera;

        [Tooltip("Transform the Cinemachine camera follows. Moved to the centre of the fight.")]
        [SerializeField] private Transform _followTarget;

        [Header("Framing")]
        [Tooltip("Closest the camera will pull in on a landscape screen.")]
        [SerializeField] private float _minOrthographicSize = 6f;
        [Tooltip("Closest the camera will pull in on a portrait screen. Deliberately smaller " +
                 "than the landscape minimum - see the note on ResolveMinimumSize.")]
        [SerializeField] private float _portraitMinOrthographicSize = 6f;
        [Tooltip("Widest the camera will pull out, for a full ten-way brawl. Sits above what " +
                 "the ring itself allows on a landscape screen, so the ring-fit rule is what " +
                 "actually binds; it only matters as a backstop on very tall displays.")]
        [SerializeField] private float _maxOrthographicSize = 45f;
        [Tooltip("Padding around the fighters, in world units, so nobody sits on the edge.")]
        [SerializeField] private float _framingPadding = 4.5f;

        [Tooltip("How far past the ropes the camera may look, in world units. The ring " +
                 "dressing - posts, turnbuckles, corner stools - lives just outside the " +
                 "canvas and should stay visible; beyond that there is nothing but void.")]
        [SerializeField] private float _outsideRingMargin = 4f;

        [Header("Damping")]
        [Tooltip("How quickly the framing catches up. Low values drift; high values snap.")]
        [SerializeField] private float _positionDamping = 2.5f;
        [SerializeField] private float _zoomDamping = 1.8f;

        [Header("Player bias")]
        [Tooltip("How strongly the framing favours the human boxer over the rest of the " +
                 "field. 0 frames everyone equally; 1 follows the player alone. Only used " +
                 "when Focus On Fight is off - with it on, the human simply is the focus.")]
        [Range(0f, 1f)]
        [SerializeField] private float _playerBias = 0.35f;

        [Header("Focus")]
        [Tooltip("Frame one exchange rather than the whole field. Off reverts to fitting every " +
                 "living fighter on screen, which in a ten-way means the entire ring.")]
        [SerializeField] private bool _focusOnFight = true;

        [Tooltip("How far from the focus another fighter can be and still be kept in frame, in " +
                 "world units. Wide enough to hold a scrap together, short enough that someone " +
                 "circling the far ropes does not drag the camera back out to the whole ring.")]
        [SerializeField] private float _focusRadius = 7f;

        [Tooltip("How much less health a rival needs before the camera abandons the fighter it " +
                 "is watching. Without a margin the focus flips on almost every landed punch " +
                 "and the framing jitters between exchanges.")]
        [SerializeField] private int _focusSwitchMargin = 3;

        private MatchModel _match;
        private BoxerSpawnPoints _spawnPoints;

        private Vector2 _smoothedCenter;
        private float _smoothedSize;
        private bool _initialised;

        /// <summary>
        /// Who the camera is currently watching. Sticky across frames - see
        /// <see cref="ResolveFocus"/>; re-picking the most hurt fighter every frame is what
        /// makes a health-driven camera unusable.
        /// </summary>
        private int _focusId = -1;

        [Inject]
        public void Construct(MatchModel match, BoxerSpawnPoints spawnPoints)
        {
            _match = match;
            _spawnPoints = spawnPoints;
        }

        /// <summary>
        /// Framed in LateUpdate so the boxers have already been moved this frame. Doing it in
        /// Update would frame where everyone was last frame, which shows up as camera judder
        /// during fast exchanges.
        /// </summary>
        private void LateUpdate()
        {
            if (_match == null || _followTarget == null)
            {
                return;
            }

            if (!TryMeasureFight(out Vector2 center, out float extent))
            {
                return;
            }

            // The widest the camera may pull out before it starts showing the void outside
            // the ring. Derived from the aspect, because orthographic size is half-HEIGHT: on
            // a 2.2:1 phone screen a size that frames the ring vertically shows more than
            // twice the ring's width horizontally, which is most of what made the first
            // Android build look like it was pointed at a corner.
            // Only the widest-allowed size uses the margin; see the clamp below.
            Vector2 bounds = _match.ArenaHalfExtent + Vector2.one * _outsideRingMargin;
            float aspect = CurrentAspect();

            // The ring is square and no screen is, so one of two framings has to be chosen.
            //
            // Landscape crops: pull out only until the view is as wide as the ring, which
            // fills the screen and keeps the fighters large. Some of the ring's height is
            // off-frame, and the camera pans over it.
            //
            // Portrait letterboxes instead. Cropping a 0.56 aspect to fill would show barely
            // half the ring's width, so most of a ten-way brawl would be off-screen while the
            // HUD still claimed ten fighters were alive. Better to fit the whole ring and let
            // the spare height above and below carry the HUD.
            float cropToFill = Mathf.Min(bounds.y, bounds.x / aspect);
            float fitWhole = Mathf.Max(bounds.y, bounds.x / aspect);
            float maxByRing = aspect < 1f ? fitWhole : cropToFill;

            float desiredSize = Mathf.Clamp(
                extent + _framingPadding,
                ResolveMinimumSize(aspect),
                Mathf.Min(_maxOrthographicSize, maxByRing));

            if (!_initialised)
            {
                _smoothedCenter = center;
                _smoothedSize = desiredSize;
                _initialised = true;
            }
            else
            {
                // Exponential smoothing on unscaled time: the knockout hold slows the world
                // right down, and a camera damped on scaled time would crawl through exactly
                // the moment it most needs to keep up with.
                float delta = Time.unscaledDeltaTime;
                _smoothedCenter = Vector2.Lerp(
                    _smoothedCenter, center, 1f - Mathf.Exp(-_positionDamping * delta));
                _smoothedSize = Mathf.Lerp(
                    _smoothedSize, desiredSize, 1f - Mathf.Exp(-_zoomDamping * delta));
            }

            // Clamped to the ropes, not to `bounds`. The outside-ring margin exists so the
            // corner posts and stools stay visible when the camera is pulled out far enough to
            // show the whole ring; letting the *position* use it too means that at a focused
            // zoom the same four units are a quarter of the screen of empty backdrop, which is
            // what a tight framing must never spend its area on. When the view is wider than
            // the ring, ClampToRing centres on that axis and the dressing is visible anyway.
            Vector2 framed = ClampToRing(
                _smoothedCenter, _smoothedSize, _match.ArenaHalfExtent, aspect);
            _followTarget.position = new Vector3(framed.x, framed.y, _followTarget.position.z);

            if (_camera != null)
            {
                _camera.Lens.OrthographicSize = _smoothedSize;
            }
        }

        /// <summary>
        /// The closest the camera may pull in, which is not the same number in both
        /// orientations.
        ///
        /// Orthographic size is half-HEIGHT, so a single minimum means two very different
        /// framings: at 9 a 16:9 screen shows 32 world units across and a 9:16 phone shows 10.
        /// The ring is 40 across, so the landscape number was doing its job, while the same
        /// number in portrait produced a tall slot with a duel in the middle and most of the
        /// frame empty above and below it.
        ///
        /// Portrait therefore carries its own, smaller minimum. On a phone the binding
        /// dimension is width, and pulling in until the fighters fill it is what makes the
        /// fight readable.
        /// </summary>
        private float ResolveMinimumSize(float aspect)
        {
            return aspect < 1f ? _portraitMinOrthographicSize : _minOrthographicSize;
        }

        private static float CurrentAspect()
        {
            return Mathf.Max(0.1f, Screen.width / (float)Mathf.Max(1, Screen.height));
        }

        /// <summary>
        /// Keeps the visible rectangle inside the ring.
        ///
        /// Without this the camera simply centres on wherever the fighters are, so a scrap in
        /// a corner points it half out of the arena and fills the screen with empty backdrop.
        /// When the view is wider than the ring on an axis, it is centred on that axis instead
        /// of clamped - there is nothing to pan to.
        /// </summary>
        private static Vector2 ClampToRing(Vector2 center, float orthographicSize, Vector2 bounds, float aspect)
        {
            float halfHeight = orthographicSize;
            float halfWidth = orthographicSize * aspect;

            center.x = halfWidth >= bounds.x
                ? 0f
                : Mathf.Clamp(center.x, -bounds.x + halfWidth, bounds.x - halfWidth);

            center.y = halfHeight >= bounds.y
                ? 0f
                : Mathf.Clamp(center.y, -bounds.y + halfHeight, bounds.y - halfHeight);

            return center;
        }

        /// <summary>
        /// Finds the centre and half-extent of everyone still standing. Returns false when
        /// nobody is left, in which case the last framing is held rather than snapping to the
        /// origin the instant the match ends.
        /// </summary>
        private bool TryMeasureFight(out Vector2 center, out float extent)
        {
            center = Vector2.zero;
            extent = 0f;

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            int humanId = _spawnPoints != null ? _spawnPoints.HumanBoxerId : -1;

            BoxerModel focus = _focusOnFight ? ResolveFocus(boxers, humanId) : null;

            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);
            int framedCount = 0;
            Vector2 playerPosition = Vector2.zero;
            bool playerAlive = false;

            // Whoever is nearest the focus is kept in frame no matter how far away they are.
            // A focus fighter alone in shot is not a fight, and the moment the last two are
            // circling at range is exactly when the camera must not cut one of them off.
            BoxerModel nearest = focus == null ? null : NearestTo(boxers, focus);

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                Vector2 position = boxer.Position;

                if (boxer.Id == humanId)
                {
                    playerPosition = position;
                    playerAlive = true;
                }

                if (focus != null)
                {
                    bool keep = boxer == focus
                        || boxer == nearest
                        || Vector2.Distance(position, focus.Position) <= _focusRadius;

                    if (!keep)
                    {
                        continue;
                    }
                }

                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
                framedCount++;
            }

            if (framedCount == 0)
            {
                return false;
            }

            center = (min + max) * 0.5f;

            // Only meaningful without a focus: with one, the human already is the focus when
            // they are alive, so biasing toward them again would double-count.
            if (focus == null && playerAlive && _playerBias > 0f)
            {
                center = Vector2.Lerp(center, playerPosition, _playerBias);
            }

            Vector2 size = max - min;

            // Half-height drives orthographic size directly; width has to be divided by the
            // aspect first or a wide, flat spread would be framed far too tightly.
            extent = Mathf.Max(size.y * 0.5f, size.x * 0.5f / CurrentAspect());

            return true;
        }

        /// <summary>
        /// Chooses which fighter the camera watches.
        ///
        /// The human when there is one and they are standing - you should never have to hunt
        /// for yourself. Otherwise the living boxer on the least health, because that is where
        /// the next knockout is coming from.
        ///
        /// Sticky, and that is the whole difficulty of a health-driven camera. Health changes
        /// several times a second across ten fighters, so re-picking the lowest every frame
        /// swings the camera across the ring on almost every landed punch. The current focus is
        /// only given up when it dies or when a rival is <see cref="_focusSwitchMargin"/> HP
        /// worse off, which makes switching a real event rather than a tie-break.
        /// </summary>
        private BoxerModel ResolveFocus(IReadOnlyList<BoxerModel> boxers, int humanId)
        {
            BoxerModel human = null;
            BoxerModel current = null;
            BoxerModel weakest = null;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                if (boxer.Id == humanId)
                {
                    human = boxer;
                }

                if (boxer.Id == _focusId)
                {
                    current = boxer;
                }

                if (weakest == null || boxer.Health.Value < weakest.Health.Value)
                {
                    weakest = boxer;
                }
            }

            if (human != null)
            {
                _focusId = human.Id;
                return human;
            }

            if (weakest == null)
            {
                _focusId = -1;
                return null;
            }

            // Hold the current focus unless someone is decisively worse off.
            if (current != null && weakest.Health.Value > current.Health.Value - _focusSwitchMargin)
            {
                return current;
            }

            _focusId = weakest.Id;
            return weakest;
        }

        /// <summary>The closest living boxer to <paramref name="focus"/>, excluding it.</summary>
        private static BoxerModel NearestTo(IReadOnlyList<BoxerModel> boxers, BoxerModel focus)
        {
            BoxerModel nearest = null;
            float bestSqr = float.MaxValue;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                if (boxer == focus || !boxer.IsAlive.Value)
                {
                    continue;
                }

                float sqr = (boxer.Position - focus.Position).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = boxer;
                }
            }

            return nearest;
        }
    }
}

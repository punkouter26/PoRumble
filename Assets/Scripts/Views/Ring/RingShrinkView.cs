using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Moves the ropes with sudden death: the four walls, the corner posts and the turnbuckles
    /// follow <see cref="MatchModel.PlayableHalfExtent"/> in, and the stools, which sit outside
    /// a corner that is no longer there, are hidden while the ring is closed.
    ///
    /// The walls carry the colliders the ray sensors see, so moving them is not only cosmetic:
    /// the agents perceive the ropes where the clamp actually holds them, rather than a wall
    /// eight units away while something invisible pushes them back.
    ///
    /// Every positioned object keeps the offset it was authored with. A wall's centre sits half
    /// its thickness outside the rope line and a post sits a little beyond the corner, so each
    /// one is placed at (playable extent + authored overhang) rather than scaled with the ring -
    /// scaling would slide the thickness of the rope into the fighting area.
    ///
    /// A View: reads the model once a frame and writes transforms. Does nothing in a scene that
    /// never changes the ring scale, which is every training scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RingShrinkView : MonoBehaviour
    {
        [Tooltip("The four walls, any order. Each is moved along whichever axis it sits off " +
                 "centre on, and stretched along the other to span the smaller ring.")]
        [SerializeField] private Transform[] _walls;

        [Tooltip("Corner objects that stay on the corners: posts and turnbuckles.")]
        [SerializeField] private Transform[] _corners;

        [Tooltip("Objects that only make sense around the full ring, hidden while it is closed.")]
        [SerializeField] private GameObject[] _hideWhileClosed;

        private MatchModel _match;

        private Vector3[] _wallPositions;
        private Vector3[] _wallScales;
        private Vector3[] _cornerPositions;
        private float _appliedScale = -1f;

        [Inject]
        public void Construct(MatchModel match)
        {
            _match = match;
        }

        private void Awake()
        {
            _wallPositions = Capture(_walls, out _wallScales);
            _cornerPositions = Capture(_corners, out _);
        }

        private void LateUpdate()
        {
            if (_match == null || Mathf.Approximately(_match.RingScale, _appliedScale))
            {
                return;
            }

            _appliedScale = _match.RingScale;
            Apply(_match.ArenaHalfExtent, _match.PlayableHalfExtent);
        }

        private void Apply(Vector2 full, Vector2 playable)
        {
            for (int index = 0; index < _walls.Length; index++)
            {
                Transform wall = _walls[index];

                if (wall == null)
                {
                    continue;
                }

                Vector3 authored = _wallPositions[index];
                Vector3 scale = _wallScales[index];

                // A wall off centre in x is an east or west wall: it moves in x and spans y.
                if (Mathf.Abs(authored.x) > Mathf.Abs(authored.y))
                {
                    float overhang = Mathf.Abs(authored.x) - full.x;
                    wall.localPosition = new Vector3(Mathf.Sign(authored.x) * (playable.x + overhang), authored.y, authored.z);
                    wall.localScale = new Vector3(scale.x, Span(scale.y, full.y, playable.y), scale.z);
                }
                else
                {
                    float overhang = Mathf.Abs(authored.y) - full.y;
                    wall.localPosition = new Vector3(authored.x, Mathf.Sign(authored.y) * (playable.y + overhang), authored.z);
                    wall.localScale = new Vector3(Span(scale.x, full.x, playable.x), scale.y, scale.z);
                }
            }

            for (int index = 0; index < _corners.Length; index++)
            {
                Transform corner = _corners[index];

                if (corner == null)
                {
                    continue;
                }

                Vector3 authored = _cornerPositions[index];
                float overhangX = Mathf.Abs(authored.x) - full.x;
                float overhangY = Mathf.Abs(authored.y) - full.y;

                corner.localPosition = new Vector3(
                    Mathf.Sign(authored.x) * (playable.x + overhangX),
                    Mathf.Sign(authored.y) * (playable.y + overhangY),
                    authored.z);
            }

            bool closed = playable.x < full.x - 0.01f;

            for (int index = 0; index < _hideWhileClosed.Length; index++)
            {
                if (_hideWhileClosed[index] != null)
                {
                    _hideWhileClosed[index].SetActive(!closed);
                }
            }
        }

        /// <summary>
        /// A wall's length: its authored length less the rope it no longer has to span. The
        /// authored overrun past the corners (half a post's width each end) is kept.
        /// </summary>
        private static float Span(float authoredLength, float fullHalf, float playableHalf)
        {
            return Mathf.Max(0.01f, authoredLength - 2f * (fullHalf - playableHalf));
        }

        private static Vector3[] Capture(Transform[] targets, out Vector3[] scales)
        {
            int count = targets == null ? 0 : targets.Length;
            var positions = new Vector3[count];
            scales = new Vector3[count];

            for (int index = 0; index < count; index++)
            {
                if (targets[index] != null)
                {
                    positions[index] = targets[index].localPosition;
                    scales[index] = targets[index].localScale;
                }
            }

            return positions;
        }
    }
}

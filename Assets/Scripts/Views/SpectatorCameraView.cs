using System.Collections.Generic;
using PoRumble.Models;
using Unity.Cinemachine;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Frames the fight. Follows the centre of mass of everyone still standing and tightens as
    /// the field thins, so ten boxers read as a brawl and the last two read as a duel.
    ///
    /// The camera was previously a fixed transform pointed at the whole 40x40 ring, which
    /// meant the fighters were always small and always far away no matter how few were left.
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
        [Tooltip("Closest the camera will pull in, for the final one-on-one.")]
        [SerializeField] private float _minOrthographicSize = 6f;
        [Tooltip("Widest the camera will pull out, for a full ten-way brawl.")]
        [SerializeField] private float _maxOrthographicSize = 21f;
        [Tooltip("Padding around the fighters, in world units, so nobody sits on the edge.")]
        [SerializeField] private float _framingPadding = 4.5f;

        [Header("Damping")]
        [Tooltip("How quickly the framing catches up. Low values drift; high values snap.")]
        [SerializeField] private float _positionDamping = 2.5f;
        [SerializeField] private float _zoomDamping = 1.8f;

        [Header("Player bias")]
        [Tooltip("How strongly the framing favours the human boxer over the rest of the " +
                 "field. 0 frames everyone equally; 1 follows the player alone.")]
        [Range(0f, 1f)]
        [SerializeField] private float _playerBias = 0.35f;

        private MatchModel _match;
        private BoxerSpawnPoints _spawnPoints;

        private Vector2 _smoothedCenter;
        private float _smoothedSize;
        private bool _initialised;

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

            float desiredSize = Mathf.Clamp(
                extent + _framingPadding,
                _minOrthographicSize,
                _maxOrthographicSize);

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

            _followTarget.position = new Vector3(_smoothedCenter.x, _smoothedCenter.y, _followTarget.position.z);

            if (_camera != null)
            {
                _camera.Lens.OrthographicSize = _smoothedSize;
            }
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

            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);
            int aliveCount = 0;
            Vector2 playerPosition = Vector2.zero;
            bool playerAlive = false;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                Vector2 position = boxer.Position;
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
                aliveCount++;

                if (boxer.Id == humanId)
                {
                    playerPosition = position;
                    playerAlive = true;
                }
            }

            if (aliveCount == 0)
            {
                return false;
            }

            center = (min + max) * 0.5f;

            // Lean toward the player so the person holding the controller is never the one
            // pushed to the edge of frame.
            if (playerAlive && _playerBias > 0f)
            {
                center = Vector2.Lerp(center, playerPosition, _playerBias);
            }

            Vector2 size = max - min;

            // Half-height drives orthographic size directly; width has to be divided by the
            // aspect first or a wide, flat spread would be framed far too tightly.
            float aspect = Mathf.Max(0.1f, (float)Screen.width / Mathf.Max(1, Screen.height));
            extent = Mathf.Max(size.y * 0.5f, size.x * 0.5f / aspect);

            return true;
        }
    }
}

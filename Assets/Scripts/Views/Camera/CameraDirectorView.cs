using System.Collections.Generic;
using PoRumble.Models;
using Unity.Cinemachine;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Cuts. <see cref="SpectatorCameraView"/> tightens and loosens a single continuous shot,
    /// which is most of what a broadcast does and none of what makes one feel live; this owns
    /// the second camera and hands it the frame on a knockout or a landed haymaker.
    ///
    /// A real cut rather than a fast zoom, and the difference is the point. Cinemachine blends
    /// between cameras by priority, so raising this one's priority switches the brain over and
    /// - with the blend set to Cut on the brain - the frame changes between one frame and the
    /// next. A camera that merely rushed to a new framing would still show the travel, and the
    /// travel is exactly what a cut exists to skip.
    ///
    /// Optional, like every other presentation component: <see cref="GameLifetimeScope"/>
    /// injects it only if it is in the scene, so the training arenas carry none of this.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraDirectorView : MonoBehaviour
    {
        [Tooltip("The camera raised for an impact cut. Leave empty to disable cutting " +
                 "entirely, which leaves the spectator camera's own tightening in place.")]
        [SerializeField] private CinemachineCamera _impactCamera;

        [Tooltip("Transform the impact camera follows. Moved onto whoever just took the punch.")]
        [SerializeField] private Transform _impactTarget;

        [Tooltip("Priority the impact camera is raised to. Must beat the spectator camera's, " +
                 "which ships at 10.")]
        [SerializeField] private int _activePriority = 30;

        [Tooltip("Priority the impact camera rests at. Must lose to the spectator camera's.")]
        [SerializeField] private int _idlePriority = 0;

        [Tooltip("Orthographic size the impact camera holds. Fixed rather than fitted, " +
                 "because a cut whose framing depends on where the fighters happen to be " +
                 "standing is a cut that sometimes lands on nothing.")]
        [SerializeField] private float _impactOrthographicSize = 4.5f;

        [Tooltip("How far the shot is pushed off the subject toward whoever hit them, in " +
                 "world units, so the punch comes from somewhere rather than from off-frame.")]
        [SerializeField] private float _attackerBias = 0.9f;

        [Tooltip("How quickly the impact camera slides onto its subject. Deliberately quick: " +
                 "the shot lasts about a second and spends the first of it arriving.")]
        [SerializeField] private float _followDamping = 14f;

        private MatchModel _match;
        private DirectorModel _director;

        private readonly CompositeDisposable _disposables = new();

        private bool _cutting;

        [Inject]
        public void Construct(MatchModel match, DirectorModel director)
        {
            _match = match;
            _director = director;
        }

        private void Start()
        {
            if (_impactCamera != null)
            {
                _impactCamera.Priority = _idlePriority;
            }

            if (_director != null)
            {
                _director.Shot.Subscribe(OnShotChanged).AddTo(_disposables);
            }
        }

        /// <summary>
        /// Keeps the cut camera on its subject for as long as the shot is held.
        ///
        /// LateUpdate for the same reason the spectator camera uses it: the boxers have
        /// already been moved this frame, and framing where somebody was last frame shows up
        /// as judder at exactly the zoom where judder is most visible.
        /// </summary>
        private void LateUpdate()
        {
            if (!_cutting || _impactTarget == null || _match == null)
            {
                return;
            }

            BoxerModel subject = Find(_director.FocusId);

            if (subject == null)
            {
                return;
            }

            Vector2 framed = subject.Position;
            BoxerModel attacker = Find(_director.RivalId);

            // Pushed a little toward whoever threw it, so both fighters are in shot and the
            // punch reads as having come from somewhere. Not the midpoint: the subject is what
            // the cut is about, and centring the pair would frame the gap between them.
            if (attacker != null)
            {
                Vector2 toAttacker = attacker.Position - subject.Position;

                if (toAttacker.sqrMagnitude > Mathf.Epsilon)
                {
                    framed += toAttacker.normalized * _attackerBias;
                }
            }

            // Unscaled, because the knockout hold is running at a fraction of normal speed and
            // this camera is what is on screen during it. On scaled time the shot would still
            // be sliding into place when the hold ended.
            Vector3 current = _impactTarget.position;
            Vector3 wanted = new(framed.x, framed.y, current.z);

            _impactTarget.position = Vector3.Lerp(
                current, wanted, 1f - Mathf.Exp(-_followDamping * Time.unscaledDeltaTime));
        }

        private void OnShotChanged(ShotType shot)
        {
            _cutting = shot == ShotType.Impact;

            if (_impactCamera == null)
            {
                return;
            }

            _impactCamera.Priority = _cutting ? _activePriority : _idlePriority;

            if (!_cutting)
            {
                return;
            }

            _impactCamera.Lens.OrthographicSize = _impactOrthographicSize;

            // Snapped onto the subject on the frame of the cut rather than damped onto it.
            // The damping in LateUpdate is there to track a fighter who is still being shoved
            // backwards by the punch; using it to arrive as well would mean every cut opened
            // on the previous knockout's framing and swept across the ring.
            SnapToSubject();
        }

        private void SnapToSubject()
        {
            if (_impactTarget == null || _match == null)
            {
                return;
            }

            BoxerModel subject = Find(_director.FocusId);

            if (subject == null)
            {
                return;
            }

            _impactTarget.position = new Vector3(
                subject.Position.x, subject.Position.y, _impactTarget.position.z);
        }

        private BoxerModel Find(int boxerId)
        {
            if (boxerId == DirectorModel.NOBODY)
            {
                return null;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                if (boxers[boxerIndex].Id == boxerId)
                {
                    return boxers[boxerIndex];
                }
            }

            return null;
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }
    }
}

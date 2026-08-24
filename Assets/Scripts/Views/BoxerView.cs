using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>Renders one boxer. Observes the model; contains no game logic.</summary>
    [DisallowMultipleComponent]
    public sealed class BoxerView : MonoBehaviour
    {
        [Tooltip("The kinematic body that carries the head, colliders and sensors. Moved " +
                 "through physics; the fists are jointed siblings, so nothing may be parented " +
                 "to it or the hierarchy would move them a second time.")]
        [SerializeField] private Rigidbody2D _bodyRigidbody;

        [Tooltip("Every renderer making up this boxer: head, limbs and fists.")]
        [SerializeField] private Renderer[] _renderers;
        [SerializeField] private ArmView _leftArmView;
        [SerializeField] private ArmView _rightArmView;
        [SerializeField] private Color _eliminatedColor = new(0.22f, 0.24f, 0.22f, 1f);

        [Header("Role colours")]
        [Tooltip("Learning agents.")]
        [SerializeField] private Color _rlColor = new(0.11f, 0.11f, 0.13f);
        [Tooltip("The hand-written sparring partner.")]
        [SerializeField] private Color _scriptedColor = new(0.58f, 0.58f, 0.61f);

        /// <summary>Per-boxer tints so ten fighters stay distinguishable in a melee.</summary>
        private static readonly Color[] BoxerPalette =
        {
            new(0.93f, 0.93f, 0.90f), // bone
            new(0.13f, 0.13f, 0.15f), // near-black
            new(0.85f, 0.29f, 0.24f), // red
            new(0.29f, 0.51f, 0.84f), // blue
            new(0.95f, 0.78f, 0.25f), // gold
            new(0.40f, 0.73f, 0.36f), // green
            new(0.72f, 0.40f, 0.78f), // violet
            new(0.95f, 0.55f, 0.22f), // orange
            new(0.35f, 0.76f, 0.76f), // teal
            new(0.85f, 0.55f, 0.65f)  // rose
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly CompositeDisposable _disposables = new();

        private BoxerModel _model;
        private MaterialPropertyBlock _propertyBlock;
        private Color _aliveColor = Color.white;

        private void Awake()
        {
            // MaterialPropertyBlock rather than .material, so tinting never clones the
            // material and every boxer keeps batching against the shared one.
            _propertyBlock = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Forces the standard two-tone scheme: learning agents black, the scripted sparring
        /// partner white, so it is obvious which is which while watching a match.
        /// </summary>
        public void SetRoleColor(bool isScripted)
        {
            _aliveColor = isScripted ? _scriptedColor : _rlColor;
            Tint(_model == null || _model.IsAlive.Value ? _aliveColor : _eliminatedColor);
        }

        /// <summary>Called by the spawner once the model exists.</summary>
        public void Bind(BoxerModel model)
        {
            _model = model;
            _aliveColor = BoxerPalette[model.Id % BoxerPalette.Length];

            _model.IsAlive
                .Subscribe(OnAliveChanged)
                .AddTo(_disposables);

            if (_leftArmView != null)
            {
                _leftArmView.Bind(_model.LeftArm);
            }

            if (_rightArmView != null)
            {
                _rightArmView.Bind(_model.RightArm);
            }
        }

        private void FixedUpdate()
        {
            if (_model == null)
            {
                return;
            }

            float facingDegrees = Mathf.Atan2(_model.Facing.y, _model.Facing.x) * Mathf.Rad2Deg - 90f;

            // Moved through physics rather than by assigning a transform, because the fists are
            // jointed rigid bodies. Teleporting a transform would fight their SliderJoint2D.
            if (_bodyRigidbody != null)
            {
                _bodyRigidbody.MovePosition(_model.Position);
                _bodyRigidbody.MoveRotation(facingDegrees);
                return;
            }

            transform.SetPositionAndRotation(_model.Position, Quaternion.Euler(0f, 0f, facingDegrees));
        }

        private void OnAliveChanged(bool isAlive)
        {
            Tint(isAlive ? _aliveColor : _eliminatedColor);
        }

        private void Tint(Color color)
        {
            if (_renderers == null)
            {
                return;
            }

            _propertyBlock.SetColor(BaseColorId, color);

            for (int rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                Renderer target = _renderers[rendererIndex];

                if (target == null)
                {
                    continue;
                }

                // Sprite shaders ignore _BaseColor, so the fists are tinted via SpriteRenderer
                // .color instead. That does not clone the material either, so batching survives.
                if (target is SpriteRenderer spriteRenderer)
                {
                    spriteRenderer.color = color;
                    continue;
                }

                target.SetPropertyBlock(_propertyBlock);
            }
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }
    }
}

using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

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

        [Tooltip("The head, so a contestant's face can be drawn on it. Must also appear in " +
                 "Renderers - this reference only says which of them is the head.")]
        [SerializeField] private SpriteRenderer _headRenderer;
        [SerializeField] private ArmView _leftArmView;
        [SerializeField] private ArmView _rightArmView;
        [SerializeField] private Color _eliminatedColor = new(0.22f, 0.24f, 0.22f, 1f);

        [Header("Role colours")]
        [Tooltip("Learning agents.")]
        [SerializeField] private Color _rlColor = new(0.12f, 0.75f, 0.25f);
        [Tooltip("The hand-written sparring partner.")]
        [SerializeField] private Color _scriptedColor = new(0.85f, 0.12f, 0.12f);

        [Header("Impact")]
        [Tooltip("Seconds the white hit flash takes to fade.")]
        [SerializeField] private float _flashSeconds = 0.11f;
        [Tooltip("Seconds a knocked-out boxer takes to burn away.")]
        [SerializeField] private float _dissolveSeconds = 0.9f;

        [Header("Outline")]
        [Tooltip("Colour of the outline drawn while this boxer's counter window is open.")]
        [SerializeField] private Color _counterOutlineColor = new(1f, 0.85f, 0.25f);
        [Tooltip("Colour of the standing outline marking the fighter the player is driving.")]
        [SerializeField] private Color _playerOutlineColor = new(0.45f, 0.85f, 1f);
        [Tooltip("Strength of the player's standing outline. Deliberately faint - it is a " +
                 "way of finding yourself in a melee, not a highlight.")]
        [Range(0f, 1f)]
        [SerializeField] private float _playerOutlineAmount = 0.5f;
        [Tooltip("Beats per second the counter outline pulses at, so it reads as a timer " +
                 "running out rather than as a state that is simply on.")]
        [SerializeField] private float _counterPulseHz = 6f;

        [Header("Damage")]
        [Tooltip("Health fraction at which a fighter is considered fully hurt for the purpose " +
                 "of dropping their guard. Above zero because a boxer on their last point of " +
                 "health should already have their hands down, not reach that state as they " +
                 "fall.")]
        [Range(0f, 1f)]
        [SerializeField] private float _hurtGuardThreshold = 0.35f;

        [Tooltip("Head sprites are authored looking up the screen. Tick this if a face is " +
                 "ever cropped mirrored, so swelling stays on the cheek that was actually " +
                 "being hit - the shader has no way to know which way round a photograph went.")]
        [SerializeField] private bool _mirrorFaceDamage;

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
        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
        private static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
        private static readonly int OutlineAmountId = Shader.PropertyToID("_OutlineAmount");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int SwellLeftId = Shader.PropertyToID("_SwellLeft");
        private static readonly int SwellRightId = Shader.PropertyToID("_SwellRight");
        private static readonly int CutAmountId = Shader.PropertyToID("_CutAmount");

        private readonly CompositeDisposable _disposables = new();

        private BoxerConfig _config;
        private BoxerModel _model;
        private MaterialPropertyBlock _propertyBlock;
        private Color _aliveColor = Color.white;

        /// <summary>
        /// What the head is tinted while its owner is standing. White once a face is on it: a
        /// photograph carries its own colour, and multiplying it by the trunk colour only
        /// makes it muddy. It still darkens on elimination, which reads correctly.
        /// </summary>
        private Color _headAliveColor = Color.white;

        /// <summary>The generic head, kept so a re-seated boxer can be given its face back.</summary>
        private Sprite _defaultHeadSprite;

        private float _flashRemaining;
        private float _dissolveElapsed;
        private bool _dissolving;
        private bool _effectsActive;

        /// <summary>
        /// True on the seat the human is driving. Marked with a standing outline, which is the
        /// one effect here that never switches itself off - see <see cref="Update"/>.
        /// </summary>
        private bool _isPlayer;

        [Inject]
        public void Construct(BoxerConfig config, ISubscriber<BoxerDamagedMessage> damagedSubscriber)
        {
            _config = config;
            damagedSubscriber.Subscribe(OnBoxerDamaged).AddTo(_disposables);
        }

        private void Awake()
        {
            // MaterialPropertyBlock rather than .material, so tinting never clones the
            // material and every boxer keeps batching against the shared one.
            _propertyBlock = new MaterialPropertyBlock();

            if (_headRenderer != null)
            {
                _defaultHeadSprite = _headRenderer.sprite;
            }
        }

        /// <summary>
        /// Dresses this boxer as a given contestant: its face on the head and its colour on
        /// the body.
        ///
        /// Called again whenever the roster is re-dealt, so it has to be able to undo itself -
        /// a seat that used to hold a face and now holds a plain fighter must get the generic
        /// head back, which is why the original sprite is kept.
        /// </summary>
        public void ApplyIdentity(FighterProfile profile)
        {
            bool hasFace = profile != null && profile.Face != null;

            if (_headRenderer != null)
            {
                _headRenderer.sprite = hasFace ? profile.Face : _defaultHeadSprite;
            }

            _aliveColor = profile != null
                ? profile.Tint
                : BoxerPalette[Mathf.Max(0, _model == null ? 0 : _model.Id) % BoxerPalette.Length];

            _headAliveColor = hasFace ? Color.white : _aliveColor;

            Tint(_model == null || _model.IsAlive.Value);
        }

        /// <summary>
        /// Forces the standard two-tone scheme: learning agents black, the scripted sparring
        /// partner white, so it is obvious which is which while watching a match.
        /// </summary>
        /// <summary>
        /// Marks this seat as the one the human is driving, so it can be picked out of a
        /// ten-way. Called by the spawner when the roster is dealt.
        /// </summary>
        public void SetIsPlayer(bool isPlayer)
        {
            if (_isPlayer == isPlayer)
            {
                return;
            }

            _isPlayer = isPlayer;

            if (isPlayer)
            {
                _effectsActive = true;
                return;
            }

            // Dropping the marker has to actively push the cleared state, because the effect
            // loop only runs while something is animating and would otherwise leave the last
            // outline written on the renderers forever.
            PushEffectProperties();

            if (_flashRemaining <= 0f && !_dissolving)
            {
                _effectsActive = false;
                ClearEffectProperties();
            }
        }

        public void SetRoleColor(bool isScripted)
        {
            _aliveColor = isScripted ? _scriptedColor : _rlColor;
            _headAliveColor = _aliveColor;
            Tint(_model == null || _model.IsAlive.Value);
        }

        /// <summary>Called by the spawner once the model exists.</summary>
        public void Bind(BoxerModel model)
        {
            _model = model;
            _aliveColor = BoxerPalette[model.Id % BoxerPalette.Length];
            _headAliveColor = _aliveColor;

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

            PushFatigue();

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

        /// <summary>
        /// Advances the hit flash and the knockout dissolve.
        ///
        /// Unscaled, because both hitstop and the knockout hold slow the world right down at
        /// exactly the moment these are meant to be playing.
        /// </summary>
        private void Update()
        {
            // A counter window is model state rather than an event: nothing publishes a message
            // when one opens, so it has to be sampled here. Latching the flag rather than only
            // reading it is what makes the outline appear at all - the loop below is gated on
            // _effectsActive, and blocking a punch does no damage, so a fighter who opened a
            // window and was not also being hit never woke the loop and never drew the outline.
            // Latching also guarantees one more pass after the window closes, which is what
            // clears the outline again.
            if (_model != null && _model.HasCounterWindow)
            {
                _effectsActive = true;
            }

            if (!_effectsActive)
            {
                return;
            }

            float delta = Time.unscaledDeltaTime;
            bool stillActive = false;

            if (_flashRemaining > 0f)
            {
                _flashRemaining = Mathf.Max(0f, _flashRemaining - delta);
                stillActive = true;
            }

            if (_dissolving && _dissolveElapsed < _dissolveSeconds)
            {
                _dissolveElapsed += delta;
                stillActive = true;
            }

            // Keeps the loop alive for as long as the window is open, so the pulse animates.
            if (_model != null && _model.HasCounterWindow)
            {
                stillActive = true;
            }

            // The player marker is the one effect with no end condition. It holds the property
            // block open on this boxer's nine renderers for the whole match, which is nine draw
            // calls that will not batch - affordable for exactly one fighter, and the reason
            // this is a per-seat flag rather than something every boxer could switch on.
            if (_isPlayer)
            {
                stillActive = true;
            }

            PushEffectProperties();

            if (stillActive)
            {
                return;
            }

            // Nothing left to animate. A property block set on a renderer takes it out of the
            // shared batch, so it is cleared the moment it stops earning its place - otherwise
            // ninety renderers would each become their own draw call for the whole match.
            _effectsActive = _dissolving || _isPlayer;

            if (!_effectsActive)
            {
                ClearEffectProperties();
            }
        }

        /// <summary>
        /// Tells both arms how spent this fighter is, so the guard visibly drops as the match
        /// wears them down.
        ///
        /// The worse of breath and health rather than either alone, because they say different
        /// things and both end with the hands coming down: a fighter who has punched themselves
        /// out has no strength to hold a guard, and one who has been hurt has no inclination to.
        ///
        /// Only the drawn pose changes. Blocking is decided in CombatMath.ArmBlocks against the
        /// straight shoulder-to-glove segment the model believes in, and the model does not
        /// know this happened - so a tired fighter looks like they are guarding worse without
        /// the hit maths quietly agreeing, which would be a balance change smuggled in as a
        /// visual one and would put the shipped policy out of calibration again.
        /// </summary>
        private void PushFatigue()
        {
            if (_config == null)
            {
                return;
            }

            float breath = 1f - Mathf.Clamp01(_model.Stamina.Value);

            float healthRatio = _model.Health.Value / (float)Mathf.Max(1, _config.MaxHealth);
            float hurt = _hurtGuardThreshold > 0f
                ? Mathf.Clamp01(1f - healthRatio / _hurtGuardThreshold)
                : 0f;

            float fatigue = Mathf.Max(breath, hurt);

            if (_leftArmView != null)
            {
                _leftArmView.SetFatigue(fatigue);
            }

            if (_rightArmView != null)
            {
                _rightArmView.SetFatigue(fatigue);
            }
        }

        private void OnBoxerDamaged(BoxerDamagedMessage message)
        {
            if (_model == null || message.BoxerId != _model.Id)
            {
                return;
            }

            _flashRemaining = _flashSeconds;
            _effectsActive = true;
        }

        private void OnAliveChanged(bool isAlive)
        {
            Tint(isAlive);

            if (isAlive)
            {
                _dissolving = false;
                _dissolveElapsed = 0f;
                _flashRemaining = 0f;
                _effectsActive = false;
                ClearEffectProperties();
                return;
            }

            _dissolving = true;
            _dissolveElapsed = 0f;
            _effectsActive = true;
        }

        /// <summary>
        /// Writes the current flash and dissolve to every renderer.
        ///
        /// The shader clamps both, so a renderer whose material is the stock sprite shader
        /// simply ignores the properties rather than erroring - which is what keeps this safe
        /// if a boxer part is ever left on a different material.
        /// </summary>
        private void PushEffectProperties()
        {
            if (_renderers == null)
            {
                return;
            }

            float flash = _flashSeconds > 0f ? _flashRemaining / _flashSeconds : 0f;
            float dissolve = _dissolving && _dissolveSeconds > 0f
                ? Mathf.Clamp01(_dissolveElapsed / _dissolveSeconds)
                : 0f;

            // The counter window wins over the player marker where both apply: a counter is
            // about to expire and is worth acting on, whereas "this one is yours" is standing
            // information the player has already absorbed.
            bool countering = _model != null && _model.HasCounterWindow;
            float outline;
            Color outlineColor;

            if (countering)
            {
                // Unscaled, like everything else in this loop: a pulse timed on scaled time
                // would visibly stall during hitstop, which is exactly when a counter matters.
                float pulse = 0.5f + 0.5f * Mathf.Sin(
                    Time.unscaledTime * _counterPulseHz * 2f * Mathf.PI);
                outline = Mathf.Lerp(0.55f, 1f, pulse);
                outlineColor = _counterOutlineColor;
            }
            else if (_isPlayer)
            {
                outline = _playerOutlineAmount;
                outlineColor = _playerOutlineColor;
            }
            else
            {
                outline = 0f;
                outlineColor = _counterOutlineColor;
            }

            for (int rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                Renderer target = _renderers[rendererIndex];

                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetFloat(FlashAmountId, flash);
                _propertyBlock.SetFloat(DissolveAmountId, dissolve);
                _propertyBlock.SetFloat(OutlineAmountId, outline);
                _propertyBlock.SetColor(OutlineColorId, outlineColor);

                // Damage is written onto the head and nowhere else. Partly because a bruise
                // on a glove would be nonsense, and partly for cost: swelling has no end
                // condition inside a match, so whatever carries it stays out of the shared
                // sprite batch until the bell. One renderer per marked fighter is affordable;
                // all nine would be the ninety-draw-call trap this loop exists to avoid.
                bool isHead = _headRenderer != null && target == _headRenderer;
                WriteDamage(_propertyBlock, isHead);

                target.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>
        /// Writes the face's accumulated swelling and cut into a block, or zeroes them for a
        /// renderer that is not the head.
        /// </summary>
        private void WriteDamage(MaterialPropertyBlock block, bool isHead)
        {
            if (!isHead || _model == null)
            {
                block.SetFloat(SwellLeftId, 0f);
                block.SetFloat(SwellRightId, 0f);
                block.SetFloat(CutAmountId, 0f);
                return;
            }

            float left = _mirrorFaceDamage ? _model.SwellRight : _model.SwellLeft;
            float right = _mirrorFaceDamage ? _model.SwellLeft : _model.SwellRight;

            block.SetFloat(SwellLeftId, left);
            block.SetFloat(SwellRightId, right);
            block.SetFloat(CutAmountId, _model.Cut);
        }

        /// <summary>True once this fighter carries a mark worth keeping on screen.</summary>
        private bool HasVisibleDamage()
        {
            return _model != null
                   && _model.IsAlive.Value
                   && (_model.Swell > 0.01f || _model.Cut > 0.01f);
        }

        private void ClearEffectProperties()
        {
            if (_renderers == null)
            {
                return;
            }

            for (int rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                Renderer target = _renderers[rendererIndex];

                if (target != null)
                {
                    target.SetPropertyBlock(null);
                }
            }

            // Everything else here switches itself off, and damage does not: a marked face
            // stays marked until the bell. So the head gets its block put straight back,
            // carrying the bruise and nothing else, while the other eight renderers go back
            // into the shared batch where they belong.
            if (_headRenderer == null || !HasVisibleDamage())
            {
                return;
            }

            _headRenderer.GetPropertyBlock(_propertyBlock);
            WriteDamage(_propertyBlock, true);
            _headRenderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>
        /// Recolours every part. The head is passed separately from the body because a face
        /// sprite must not be multiplied by the trunk colour while its owner is standing.
        /// </summary>
        private void Tint(bool isAlive)
        {
            if (_renderers == null)
            {
                return;
            }

            Color bodyColor = isAlive ? _aliveColor : _eliminatedColor;
            Color headColor = isAlive ? _headAliveColor : _eliminatedColor;

            for (int rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                Renderer target = _renderers[rendererIndex];

                if (target == null)
                {
                    continue;
                }

                Color color = _headRenderer != null && target == _headRenderer ? headColor : bodyColor;

                // Sprite shaders ignore _BaseColor, so the parts are tinted via SpriteRenderer
                // .color instead. That does not clone the material either, and unlike a
                // property block it does not break the sprite batch.
                if (target is SpriteRenderer spriteRenderer)
                {
                    spriteRenderer.color = color;
                    continue;
                }

                _propertyBlock.SetColor(BaseColorId, color);
                target.SetPropertyBlock(_propertyBlock);
            }
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }
    }
}

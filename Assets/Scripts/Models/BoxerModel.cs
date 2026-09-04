using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>Runtime state for a single boxer. Pure data — no Unity components.</summary>
    public sealed class BoxerModel
    {
        public int Id { get; }

        public ReactiveProperty<int> Health { get; }
        public ReactiveProperty<bool> IsAlive { get; } = new(true);

        /// <summary>
        /// 1 = fresh, 0 = spent. Punching and moving drain it, standing off recovers it.
        /// A tired boxer punches slower, hits softer and moves less, the way a real one fades
        /// over rounds.
        /// </summary>
        public ReactiveProperty<float> Stamina { get; } = new(1f);

        /// <summary>
        /// How far a haymaker is wound up, 0..1. Reactive so the HUD can show the meter
        /// filling without polling it every frame.
        /// </summary>
        public ReactiveProperty<float> Charge { get; } = new(0f);

        /// <summary>True while the owner is holding the charge button down.</summary>
        public bool ChargeInput { get; set; }

        /// <summary>
        /// Seconds left on the counter window opened by blocking a punch. Landing a punch
        /// while this is running scores a counter, which is the payoff for holding a guard
        /// up rather than simply trading.
        ///
        /// A plain float rather than a ReactiveProperty: it changes every single tick, and
        /// waking a subscriber sixty times a second to redraw nothing is pure waste. The HUD
        /// reacts to the block message instead.
        /// </summary>
        public float CounterWindow { get; set; }

        public bool HasCounterWindow => CounterWindow > 0f;

        /// <summary>
        /// Seconds left on the slip. While this is running the boxer's face cannot be hit at
        /// all, which is what makes a dodge worth its stamina.
        ///
        /// Plain floats rather than ReactiveProperties for the same reason CounterWindow is:
        /// they change every tick, and waking a subscriber sixty times a second to redraw
        /// nothing is pure waste.
        /// </summary>
        public float DodgeWindow { get; set; }

        /// <summary>Seconds until another slip is allowed. Stops dodging being a permanent state.</summary>
        public float DodgeCooldown { get; set; }

        /// <summary>Direction of the current slip, used for the sideways burst it carries.</summary>
        public Vector2 DodgeDirection { get; set; }

        /// <summary>True while the face is untouchable.</summary>
        public bool IsDodging => DodgeWindow > 0f;

        /// <summary>True when a slip could be started right now.</summary>
        public bool CanDodge => DodgeWindow <= 0f && DodgeCooldown <= 0f;

        /// <summary>
        /// Accumulated trauma, shed over the following second or so. Above
        /// <see cref="BoxerConfig.StunThreshold"/> the boxer is wobbled: slower on its feet
        /// and much slower to turn, so the guard stops tracking the attacker.
        ///
        /// A plain float rather than a ReactiveProperty, for the same reason
        /// <see cref="CounterWindow"/> is one: it changes on every single tick and waking a
        /// subscriber sixty times a second to redraw nothing is pure waste.
        /// </summary>
        public float Stun { get; set; }

        /// <summary>
        /// Physical differences from the shipped tuning — power, chin, speed, breath.
        ///
        /// Set once when a contestant takes this seat and read by the systems every tick. It
        /// is the only reason two boxers running the identical policy network fight
        /// differently at all.
        /// </summary>
        public FighterAttributes Attributes { get; set; } = FighterAttributes.Neutral;

        /// <summary>Current velocity, carried between ticks so movement has weight.</summary>
        public Vector2 Velocity { get; set; }

        public Vector2 Position { get; set; }

        private Vector2 _facing = Vector2.up;

        /// <summary>
        /// Unit vector the boxer faces; the face arc is centred on this. Assigning it snaps the
        /// boxer round and cancels any turn in progress, so spawning and tests can place a
        /// fighter without it immediately rotating away. Use SetAim on the system to request a
        /// gradual turn instead.
        /// </summary>
        public Vector2 Facing
        {
            get => _facing;
            set
            {
                _facing = value;
                DesiredFacing = value;
            }
        }

        /// <summary>Heading the boxer is turning toward, reached at a finite turn rate.</summary>
        public Vector2 DesiredFacing { get; set; } = Vector2.up;

        /// <summary>Advances the current heading without disturbing the requested one.</summary>
        public void ApplyTurn(Vector2 facing)
        {
            _facing = facing;
        }

        public Vector2 MoveInput { get; set; }

        public ArmModel LeftArm { get; } = new(ArmSide.Left);
        public ArmModel RightArm { get; } = new(ArmSide.Right);

        /// <summary>
        /// What a full health bar is worth for this boxer.
        ///
        /// Held here rather than read from the config at the point of use, because the thing
        /// that wants it is a *fraction* - how close this fighter is to going down - and a
        /// consumer that divides by the first health value it happened to observe rebases
        /// itself the moment the boxer takes a hit. Re-set by ResetTo, so a curriculum that
        /// changes the health between episodes stays honest.
        /// </summary>
        public int MaxHealth { get; private set; }

        /// <summary>
        /// Health as 0..1. Clamped by hand rather than with Mathf so this model keeps its
        /// promise of depending on nothing.
        /// </summary>
        public float HealthFraction
        {
            get
            {
                if (MaxHealth <= 0)
                {
                    return 0f;
                }

                float fraction = Health.Value / (float)MaxHealth;

                if (fraction < 0f)
                {
                    return 0f;
                }

                return fraction > 1f ? 1f : fraction;
            }
        }

        public BoxerModel(int id, int maxHealth)
        {
            Id = id;
            MaxHealth = maxHealth;
            Health = new ReactiveProperty<int>(maxHealth);
        }

        public void ApplyDamage(int amount)
        {
            if (!IsAlive.Value)
            {
                return;
            }

            int reduced = Health.Value - amount;
            Health.Value = reduced < 0 ? 0 : reduced;
        }

        /// <summary>Restores the boxer to full health at a spawn pose, for a new episode.</summary>
        public void ResetTo(Vector2 position, Vector2 facing, int maxHealth)
        {
            Position = position;
            Facing = facing;
            DesiredFacing = facing;
            MoveInput = Vector2.zero;
            Velocity = Vector2.zero;
            Stamina.Value = 1f;
            Charge.Value = 0f;
            ChargeInput = false;
            CounterWindow = 0f;
            Stun = 0f;
            DodgeWindow = 0f;
            DodgeCooldown = 0f;
            DodgeDirection = Vector2.zero;
            LeftArm.ForceRetract();
            RightArm.ForceRetract();
            MaxHealth = maxHealth;
            Health.Value = maxHealth;
            IsAlive.Value = true;
        }

        /// <summary>Marks the boxer eliminated. Returns false if it was already eliminated.</summary>
        public bool Eliminate()
        {
            if (!IsAlive.Value)
            {
                return false;
            }

            IsAlive.Value = false;
            Charge.Value = 0f;
            ChargeInput = false;
            CounterWindow = 0f;
            Stun = 0f;
            DodgeWindow = 0f;
            DodgeCooldown = 0f;
            LeftArm.ForceRetract();
            RightArm.ForceRetract();
            return true;
        }
    }
}

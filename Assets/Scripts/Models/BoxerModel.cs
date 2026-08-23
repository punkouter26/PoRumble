using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>Runtime state for a single boxer. Pure data — no Unity components.</summary>
    public sealed class BoxerModel
    {
        public int Id { get; }

        public ReactiveProperty<int> Health { get; }
        public ReactiveProperty<bool> IsAlive { get; } = new(true);

        public Vector2 Position { get; set; }

        /// <summary>Unit vector the boxer faces. The face arc is centred on this.</summary>
        public Vector2 Facing { get; set; } = Vector2.up;

        public Vector2 MoveInput { get; set; }

        public ArmModel LeftArm { get; } = new(ArmSide.Left);
        public ArmModel RightArm { get; } = new(ArmSide.Right);

        public BoxerModel(int id, int maxHealth)
        {
            Id = id;
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
            MoveInput = Vector2.zero;
            LeftArm.ForceRetract();
            RightArm.ForceRetract();
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
            LeftArm.ForceRetract();
            RightArm.ForceRetract();
            return true;
        }
    }
}

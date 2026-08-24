using UnityEngine;

namespace PoRumble.Models
{
    public readonly struct PunchLandedMessage
    {
        public readonly int AttackerId;
        public readonly int TargetId;
        public readonly int Damage;
        public readonly bool IsCloseRange;
        public readonly Vector2 Position;

        public PunchLandedMessage(int attackerId, int targetId, int damage, bool isCloseRange, Vector2 position)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            Damage = damage;
            IsCloseRange = isCloseRange;
            Position = position;
        }
    }

    /// <summary>
    /// A punch was stopped by the defender's gloves instead of reaching the face. Worth
    /// something to the blocker: keeping the guard up is a real skill, just not as valuable
    /// as slipping the punch entirely.
    /// </summary>
    public readonly struct PunchBlockedMessage
    {
        public readonly int AttackerId;
        public readonly int BlockerId;
        public readonly Vector2 Position;

        public PunchBlockedMessage(int attackerId, int blockerId, Vector2 position)
        {
            AttackerId = attackerId;
            BlockerId = blockerId;
            Position = position;
        }
    }

    /// <summary>
    /// A punch came close enough to land but did not. Raised for the boxer who slipped it, so
    /// evasion can be rewarded rather than only aggression.
    /// </summary>
    public readonly struct PunchEvadedMessage
    {
        public readonly int AttackerId;
        public readonly int EvaderId;
        public readonly Vector2 Position;

        public PunchEvadedMessage(int attackerId, int evaderId, Vector2 position)
        {
            AttackerId = attackerId;
            EvaderId = evaderId;
            Position = position;
        }
    }

    public readonly struct BoxerDamagedMessage
    {
        public readonly int BoxerId;
        public readonly int NewHealth;

        public BoxerDamagedMessage(int boxerId, int newHealth)
        {
            BoxerId = boxerId;
            NewHealth = newHealth;
        }
    }

    public readonly struct BoxerEliminatedMessage
    {
        public readonly int BoxerId;
        public readonly int EliminatedById;

        public BoxerEliminatedMessage(int boxerId, int eliminatedById)
        {
            BoxerId = boxerId;
            EliminatedById = eliminatedById;
        }
    }

    public readonly struct MatchEndedMessage
    {
        /// <summary>MatchModel.NO_WINNER (-1) for a draw.</summary>
        public readonly int WinnerId;

        public MatchEndedMessage(int winnerId)
        {
            WinnerId = winnerId;
        }
    }
}

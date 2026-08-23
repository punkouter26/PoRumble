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

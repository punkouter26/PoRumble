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

        /// <summary>True when the attacker landed this inside a counter window.</summary>
        public readonly bool IsCounter;

        /// <summary>Charge the swing carried, 0 for an ordinary punch, 1 for a full haymaker.</summary>
        public readonly float ChargeLevel;

        public PunchLandedMessage(int attackerId, int targetId, int damage, bool isCloseRange, Vector2 position)
            : this(attackerId, targetId, damage, isCloseRange, position, false, 0f)
        {
        }

        public PunchLandedMessage(
            int attackerId,
            int targetId,
            int damage,
            bool isCloseRange,
            Vector2 position,
            bool isCounter,
            float chargeLevel)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            Damage = damage;
            IsCloseRange = isCloseRange;
            Position = position;
            IsCounter = isCounter;
            ChargeLevel = chargeLevel;
        }
    }

    /// <summary>
    /// A haymaker was released. Raised at the moment of commitment rather than impact, so
    /// the wind-up can be heard and seen before anyone knows whether it lands.
    /// </summary>
    public readonly struct HaymakerThrownMessage
    {
        public readonly int BoxerId;
        public readonly Vector2 Position;
        public readonly float ChargeLevel;

        public HaymakerThrownMessage(int boxerId, Vector2 position, float chargeLevel)
        {
            BoxerId = boxerId;
            Position = position;
            ChargeLevel = chargeLevel;
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

    /// <summary>
    /// A boxer slipped: the invulnerability window opened. Published at the start of the
    /// slip rather than when something misses, so the whoosh and the lean play whether or
    /// not a punch was actually coming.
    /// </summary>
    public readonly struct BoxerDodgedMessage
    {
        public readonly int BoxerId;
        public readonly Vector2 Position;
        public readonly Vector2 Direction;

        public BoxerDodgedMessage(int boxerId, Vector2 position, Vector2 direction)
        {
            BoxerId = boxerId;
            Position = position;
            Direction = direction;
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

using System.Collections.Generic;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Systems
{
    /// <summary>What a controller wants a boxer to do this tick.</summary>
    public readonly struct BoxerIntent
    {
        public readonly Vector2 Move;
        public readonly Vector2 Aim;
        public readonly bool PunchLeft;
        public readonly bool PunchRight;

        public BoxerIntent(Vector2 move, Vector2 aim, bool punchLeft, bool punchRight)
        {
            Move = move;
            Aim = aim;
            PunchLeft = punchLeft;
            PunchRight = punchRight;
        }

        public static BoxerIntent Idle => new(Vector2.zero, Vector2.zero, false, false);
    }

    /// <summary>
    /// A hand-written boxer, used as a sparring partner for the learning agents.
    ///
    /// Self-play against a policy that starts out random teaches very little early on: two
    /// flailing boxers rarely produce a clean exchange, so there is almost nothing to learn
    /// from. A competent scripted opponent gives the learner a consistent target from the
    /// first episode.
    ///
    /// Pure logic on purpose - no Unity components, no randomness beyond what is passed in -
    /// so its behaviour is deterministic and can be unit-tested without a scene.
    /// </summary>
    public sealed class ScriptedBoxerBrain
    {
        private readonly BoxerConfig _config;

        /// <summary>Range band the bot tries to hold, as a fraction of its punching reach.</summary>
        private const float ENGAGE_RANGE_SCALE = 0.95f;
        private const float BREAK_RANGE_SCALE = 1.35f;

        /// <summary>Below this stamina the bot backs off and breathes instead of trading.</summary>
        private const float RECOVER_STAMINA = 0.25f;
        private const float RESUME_STAMINA = 0.55f;

        /// <summary>How square the bot must be to a target before it commits to a punch.</summary>
        private const float PUNCH_ALIGNMENT = 0.9f;

        private bool _recovering;

        public ScriptedBoxerBrain(BoxerConfig config)
        {
            _config = config;
        }

        public BoxerIntent Decide(MatchModel match, int boxerId)
        {
            BoxerModel self = FindBoxer(match, boxerId);

            if (self == null || !self.IsAlive.Value)
            {
                return BoxerIntent.Idle;
            }

            BoxerModel target = FindNearestOpponent(match, self, out float distance);

            if (target == null)
            {
                return BoxerIntent.Idle;
            }

            Vector2 toTarget = target.Position - self.Position;
            Vector2 aim = toTarget.sqrMagnitude > Mathf.Epsilon ? toTarget.normalized : self.Facing;

            // Hysteresis, so the bot commits to breathing rather than flickering in and out.
            if (_recovering && self.Stamina.Value >= RESUME_STAMINA)
            {
                _recovering = false;
            }
            else if (!_recovering && self.Stamina.Value <= RECOVER_STAMINA)
            {
                _recovering = true;
            }

            float idealRange = _config.ArmReach + _config.HeadOffset;

            if (_recovering)
            {
                // Circle away and keep the guard toward the opponent while catching breath.
                Vector2 retreat = -aim;
                Vector2 strafe = new(-aim.y, aim.x);
                return new BoxerIntent((retreat + strafe * 0.6f).normalized, aim, false, false);
            }

            // Hold the range where a punch can actually reach the head.
            float engage = idealRange * ENGAGE_RANGE_SCALE;
            float breakOff = idealRange * BREAK_RANGE_SCALE;
            Vector2 move;

            if (distance > breakOff)
            {
                move = aim;                       // too far: close it down
            }
            else if (distance < engage * 0.75f)
            {
                move = -aim;                      // too close to extend: give ground
            }
            else
            {
                // In the pocket: circle rather than stand still, so it is not a stationary target.
                move = new Vector2(-aim.y, aim.x) * 0.5f;
            }

            // Only throw when square to the target and actually in range; flailing at the air
            // just burns stamina.
            bool aligned = Vector2.Dot(self.Facing.normalized, aim) >= PUNCH_ALIGNMENT;
            bool inRange = distance <= breakOff;
            bool throwPunch = aligned && inRange;

            return new BoxerIntent(move, aim, throwPunch, throwPunch);
        }

        private static BoxerModel FindBoxer(MatchModel match, int boxerId)
        {
            IReadOnlyList<BoxerModel> boxers = match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                if (boxers[boxerIndex].Id == boxerId)
                {
                    return boxers[boxerIndex];
                }
            }

            return null;
        }

        private static BoxerModel FindNearestOpponent(MatchModel match, BoxerModel self, out float distance)
        {
            BoxerModel nearest = null;
            float bestSqr = float.MaxValue;
            IReadOnlyList<BoxerModel> boxers = match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel other = boxers[boxerIndex];

                if (other.Id == self.Id || !other.IsAlive.Value)
                {
                    continue;
                }

                float sqr = (other.Position - self.Position).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = other;
                }
            }

            distance = nearest == null ? 0f : Mathf.Sqrt(bestSqr);
            return nearest;
        }
    }
}

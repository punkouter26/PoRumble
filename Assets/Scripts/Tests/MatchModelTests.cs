using NUnit.Framework;
using PoRumble.Models;

namespace PoRumble.Tests
{
    /// <summary>Covers acceptance criteria 7, 8 and 9.</summary>
    public sealed class MatchModelTests
    {
        private static MatchModel BuildMatch(int boxerCount, int maxHealth = 30)
        {
            MatchModel match = new();

            for (int boxerIndex = 0; boxerIndex < boxerCount; boxerIndex++)
            {
                match.AddBoxer(new BoxerModel(boxerIndex, maxHealth));
            }

            return match;
        }

        [Test]
        public void EliminateReturnsTrueOnceThenFalse()
        {
            BoxerModel boxer = new(0, 30);

            Assert.That(boxer.Eliminate(), Is.True, "first elimination");
            Assert.That(boxer.Eliminate(), Is.False, "must not double-eliminate");
        }

        [Test]
        public void DamageNeverDropsHealthBelowZero()
        {
            BoxerModel boxer = new(0, 30);
            boxer.ApplyDamage(999);

            Assert.That(boxer.Health.Value, Is.Zero);
        }

        [Test]
        public void EliminatedBoxerIgnoresFurtherDamage()
        {
            BoxerModel boxer = new(0, 30);
            boxer.ApplyDamage(30);
            boxer.Eliminate();
            boxer.ApplyDamage(5);

            Assert.That(boxer.Health.Value, Is.Zero);
        }

        [Test]
        public void TenBoxers_LastSurvivorIsTheWinner()
        {
            MatchModel match = BuildMatch(10);

            for (int boxerIndex = 0; boxerIndex < 9; boxerIndex++)
            {
                match.Boxers[boxerIndex].Eliminate();
            }

            Assert.That(match.CountAlive(), Is.EqualTo(1));
            Assert.That(match.ResolveLeaderId(), Is.EqualTo(9));
        }

        [Test]
        public void AllEliminated_ResolvesToNoWinner()
        {
            MatchModel match = BuildMatch(2);
            match.Boxers[0].Eliminate();
            match.Boxers[1].Eliminate();

            Assert.That(match.CountAlive(), Is.Zero);
            Assert.That(match.ResolveLeaderId(), Is.EqualTo(MatchModel.NO_WINNER));
        }

        [Test]
        public void TimeoutWithEqualHealth_IsADraw()
        {
            MatchModel match = BuildMatch(3);

            Assert.That(match.ResolveLeaderId(), Is.EqualTo(MatchModel.NO_WINNER),
                "three survivors on equal health must draw");
        }

        [Test]
        public void TimeoutWithHealthLead_PicksTheHealthiest()
        {
            MatchModel match = BuildMatch(3);
            match.Boxers[0].ApplyDamage(10);
            match.Boxers[2].ApplyDamage(5);

            Assert.That(match.ResolveLeaderId(), Is.EqualTo(1));
        }
    }
}

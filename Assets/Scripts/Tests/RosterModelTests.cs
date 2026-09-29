using System.Collections.Generic;
using NUnit.Framework;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// The card is usually shorter than the ring. Dealing the entrants round the corners
    /// cyclically is what lets the selection change between matches without tearing down and
    /// respawning ten agents.
    /// </summary>
    public sealed class RosterModelTests
    {
        private readonly List<FighterProfile> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < _created.Count; index++)
            {
                Object.DestroyImmediate(_created[index]);
            }

            _created.Clear();
        }

        private List<FighterProfile> MakeCard(int count)
        {
            List<FighterProfile> card = new();

            for (int index = 0; index < count; index++)
            {
                FighterProfile profile = ScriptableObject.CreateInstance<FighterProfile>();
                JsonUtility.FromJsonOverwrite($"{{\"_id\":\"f{index}\"}}", profile);
                _created.Add(profile);
                card.Add(profile);
            }

            return card;
        }

        [Test]
        public void EveryContestantIsSelectedByDefault()
        {
            RosterModel roster = new();
            roster.SetAvailable(MakeCard(8));

            Assert.That(roster.Entrants, Has.Count.EqualTo(8));
        }

        [Test]
        public void ShortCardsWrapRoundTheRing()
        {
            RosterModel roster = new();
            List<FighterProfile> card = MakeCard(3);
            roster.SetAvailable(card);
            roster.AssignSeats(10);

            for (int seat = 0; seat < 10; seat++)
            {
                Assert.That(roster.SeatOf(seat), Is.SameAs(card[seat % 3]),
                    $"seat {seat} was dealt the wrong contestant");
            }
        }

        [Test]
        public void DroppingAContestantRedealsTheRing()
        {
            RosterModel roster = new();
            List<FighterProfile> card = MakeCard(4);
            roster.SetAvailable(card);

            Assert.That(roster.Toggle(card[0]), Is.True);
            roster.AssignSeats(6);

            for (int seat = 0; seat < 6; seat++)
            {
                Assert.That(roster.SeatOf(seat), Is.Not.SameAs(card[0]),
                    "a dropped fighter must not still be in the ring");
            }
        }

        [Test]
        public void TheCardCannotFallBelowTwo()
        {
            RosterModel roster = new();
            List<FighterProfile> card = MakeCard(3);
            roster.SetAvailable(card);

            Assert.That(roster.Toggle(card[0]), Is.True);
            Assert.That(roster.Toggle(card[1]), Is.False,
                "one fighter dealt ten times could never resolve a match");
            Assert.That(roster.Entrants, Has.Count.EqualTo(2));
        }

        [Test]
        public void ToggleAddsAContestantBack()
        {
            RosterModel roster = new();
            List<FighterProfile> card = MakeCard(3);
            roster.SetAvailable(card);

            roster.Toggle(card[0]);
            Assert.That(roster.IsEntrant(card[0]), Is.False);

            roster.Toggle(card[0]);
            Assert.That(roster.IsEntrant(card[0]), Is.True);
        }

        [Test]
        public void RepublishingTheCardDropsFightersThatAreGone()
        {
            RosterModel roster = new();
            List<FighterProfile> card = MakeCard(4);
            roster.SetAvailable(card);

            List<FighterProfile> shorter = new() { card[0], card[1] };
            roster.SetAvailable(shorter);

            Assert.That(roster.Entrants, Has.Count.EqualTo(2));
            Assert.That(roster.IsEntrant(card[3]), Is.False,
                "an asset removed from the scene must not leave a dangling selection");
        }

        [Test]
        public void ASeatOutsideTheRingHasNobodyInIt()
        {
            RosterModel roster = new();
            roster.SetAvailable(MakeCard(2));
            roster.AssignSeats(4);

            Assert.That(roster.SeatOf(4), Is.Null);
            Assert.That(roster.SeatOf(-1), Is.Null);
        }

        [Test]
        public void DealingBumpsTheRevisionSoViewsRedraw()
        {
            RosterModel roster = new();
            roster.SetAvailable(MakeCard(3));

            int before = roster.Revision.Value;
            roster.AssignSeats(6);

            Assert.That(roster.Revision.Value, Is.GreaterThan(before));
        }

        private List<FighterProfile> MakeNamedCard(params string[] names)
        {
            List<FighterProfile> card = new();

            for (int index = 0; index < names.Length; index++)
            {
                FighterProfile profile = ScriptableObject.CreateInstance<FighterProfile>();
                JsonUtility.FromJsonOverwrite($"{{\"_id\":\"f{index}\",\"_displayName\":\"{names[index]}\"}}", profile);
                _created.Add(profile);
                card.Add(profile);
            }

            return card;
        }

        [Test]
        public void AContestantInTwoCornersIsNumberedTheSecondTime()
        {
            RosterModel roster = new();
            roster.SetAvailable(MakeNamedCard("RED", "GREEN", "ALAN"));
            roster.AssignSeats(5);

            Assert.That(roster.SeatLabel(0), Is.EqualTo("RED"));
            Assert.That(roster.SeatLabel(1), Is.EqualTo("GREEN"));
            Assert.That(roster.SeatLabel(2), Is.EqualTo("ALAN"));
            Assert.That(roster.SeatLabel(3), Is.EqualTo("RED 2"));
            Assert.That(roster.SeatLabel(4), Is.EqualTo("GREEN 2"));
        }

        [Test]
        public void AFullCardNumbersNobody()
        {
            RosterModel roster = new();
            roster.SetAvailable(MakeNamedCard("A", "B", "C"));
            roster.AssignSeats(3);

            Assert.That(roster.SeatLabel(0), Is.EqualTo("A"));
            Assert.That(roster.SeatLabel(2), Is.EqualTo("C"));
            Assert.That(roster.SeatLabel(3), Is.Null, "a seat past the ring has no label");
        }

        [Test]
        public void SeatsForMatchesTheDeal()
        {
            RosterModel roster = new();
            List<FighterProfile> card = MakeNamedCard("A", "B", "C", "D", "E", "F", "G", "H");
            roster.SetAvailable(card);
            roster.AssignSeats(10);

            for (int index = 0; index < card.Count; index++)
            {
                int dealt = 0;

                for (int seat = 0; seat < 10; seat++)
                {
                    if (roster.SeatOf(seat) == card[index])
                    {
                        dealt++;
                    }
                }

                Assert.That(roster.SeatsFor(card[index], 10), Is.EqualTo(dealt), card[index].DisplayName);
            }

            Assert.That(roster.SeatsFor(card[0], 10), Is.EqualTo(2));
            Assert.That(roster.SeatsFor(card[7], 10), Is.EqualTo(1));
        }
    }
}

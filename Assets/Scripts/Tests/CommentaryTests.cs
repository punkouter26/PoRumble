using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// Pins the commentator's restraint rather than his vocabulary.
    ///
    /// Almost everything that makes commentary work is a rule about when *not* to speak, and
    /// all of those are testable without a single audio clip: the bank only has to hold
    /// entries, not sound. What is deliberately not tested here is which words come out, which
    /// is editorial and belongs in Tools/commentary_lines.json.
    /// </summary>
    public sealed class CommentaryTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private MatchFlowModel _flow;
        private CommentaryModel _commentary;
        private CommentaryBank _bank;
        private BoxerConfig _config;
        private CommentarySystem _system;
        private List<CommentaryCue> _heard;

        /// <summary>
        /// A bank whose entries carry ids and events but no clips.
        ///
        /// Built through SerializedObject because CommentaryEntry's fields are private and
        /// serialized, which is the project's rule and worth honouring even here — a test that
        /// forces a public setter onto a model is a test that has changed the thing it is
        /// checking.
        /// </summary>
        private static CommentaryBank BuildBank(params (string id, CommentaryEvent trigger)[] lines)
        {
            CommentaryBank bank = ScriptableObject.CreateInstance<CommentaryBank>();

            SerializedObject so = new(bank);
            SerializedProperty entries = so.FindProperty("_entries");
            entries.ClearArray();

            // A clip is needed for a variant to be eligible, so every entry gets the same
            // one-sample placeholder. Its contents never matter: nothing here plays it.
            AudioClip placeholder = AudioClip.Create("silence", 1, 1, 8000, false);

            for (int index = 0; index < lines.Length; index++)
            {
                entries.InsertArrayElementAtIndex(index);
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("_id").stringValue = lines[index].id;
                entry.FindPropertyRelative("_event").enumValueIndex = (int)lines[index].trigger;
                entry.FindPropertyRelative("_text").stringValue = lines[index].id;
                entry.FindPropertyRelative("_usesName").boolValue = false;
                entry.FindPropertyRelative("_clip").objectReferenceValue = placeholder;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return bank;
        }

        [SetUp]
        public void SetUp()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<BoxerDodgedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);
            _container = builder.Build();

            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel();
            _match.AddBoxer(new BoxerModel(0, _config.MaxHealth));
            _match.AddBoxer(new BoxerModel(1, _config.MaxHealth));
            _match.AddBoxer(new BoxerModel(2, _config.MaxHealth));

            _flow = new MatchFlowModel();
            _commentary = new CommentaryModel();

            _bank = BuildBank(
                ("bell_0", CommentaryEvent.Bell),
                ("bell_1", CommentaryEvent.Bell),
                ("counter_0", CommentaryEvent.Counter),
                ("counter_1", CommentaryEvent.Counter),
                ("flurry_0", CommentaryEvent.Flurry),
                ("flurry_1", CommentaryEvent.Flurry),
                ("hurt_0", CommentaryEvent.Hurt),
                ("hurt_1", CommentaryEvent.Hurt),
                ("ko_0", CommentaryEvent.Elimination),
                ("ko_1", CommentaryEvent.Elimination));

            _system = new CommentarySystem(
                _match,
                _commentary,
                _bank,
                _flow,
                new RosterModel(),
                new RatingModel(),
                _config,
                _container.Resolve<ISubscriber<PunchLandedMessage>>(),
                _container.Resolve<ISubscriber<PunchBlockedMessage>>(),
                _container.Resolve<ISubscriber<BoxerDodgedMessage>>(),
                _container.Resolve<ISubscriber<BoxerEliminatedMessage>>(),
                _container.Resolve<ISubscriber<MatchEndedMessage>>());

            _heard = new List<CommentaryCue>();

            // Only cues that carry a line are recorded. ReactiveProperty notifies a new
            // subscriber with the value it is already holding, so subscribing here picks up
            // the empty opening cue - which CommentaryView discards for exactly this reason,
            // and which would otherwise make every count in this file one too many.
            _commentary.Cue.Subscribe(cue =>
            {
                if (cue.HasLine)
                {
                    _heard.Add(cue);
                }
            });

            _system.Tick(0.02f);
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _container?.Dispose();
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_bank);
        }

        private void Land(int attackerId, int targetId, int damage, bool counter = false)
        {
            _container.Resolve<IPublisher<PunchLandedMessage>>().Publish(
                new PunchLandedMessage(
                    attackerId, targetId, damage, true, Vector2.zero, counter, 0f, 0f));
        }

        /// <summary>Lets the floor clear so the next line is not swallowed by the hold.</summary>
        private void LetHimFinish()
        {
            _system.Tick(4f);
        }

        [Test]
        public void TheBellIsCalledWhenTheFightGoesLive()
        {
            _flow.Phase.Value = MatchFlowPhase.Fighting;

            Assert.That(_heard.Count, Is.EqualTo(1));
            Assert.That(_heard[0].LineId, Does.StartWith("bell_"));
        }

        /// <summary>
        /// The rule that makes the whole feature listenable. A ten-way publishes several
        /// landed punches a second; without the hold the commentator restarts a sentence on
        /// every one of them.
        /// </summary>
        [Test]
        public void ALineInProgressIsNotInterruptedByALesserOne()
        {
            Land(0, 1, 3, counter: true);
            int afterCounter = _heard.Count;

            // Three more landed punches would otherwise be a flurry, which outranks nothing.
            Land(0, 1, 1);
            Land(0, 1, 1);
            Land(0, 1, 1);

            Assert.That(_heard.Count, Is.EqualTo(afterCounter));
        }

        [Test]
        public void AKnockoutCutsAcrossWhateverHeWasSaying()
        {
            Land(0, 1, 3, counter: true);
            int afterCounter = _heard.Count;

            _match.Boxers[1].Eliminate();
            _container.Resolve<IPublisher<BoxerEliminatedMessage>>()
                .Publish(new BoxerEliminatedMessage(1, 0));

            Assert.That(_heard.Count, Is.GreaterThan(afterCounter));
            Assert.That(_heard[_heard.Count - 1].LineId, Does.StartWith("ko_"));
        }

        [Test]
        public void TheSameLineIsNotUsedTwiceInARow()
        {
            Land(0, 1, 3, counter: true);
            LetHimFinish();
            Land(0, 1, 3, counter: true);

            Assert.That(_heard.Count, Is.EqualTo(2));
            Assert.That(_heard[0].LineId, Is.Not.EqualTo(_heard[1].LineId));
        }

        /// <summary>
        /// Health only falls inside a match, so an unlatched trouble call re-fires on every
        /// subsequent punch - which is the single fastest way to make a commentator sound
        /// broken.
        /// </summary>
        [Test]
        public void AFighterIsOnlyCalledHurtOnce()
        {
            _match.Boxers[1].ApplyDamage(_config.MaxHealth - 2);

            Land(0, 1, 1);
            LetHimFinish();
            Land(0, 1, 1);
            LetHimFinish();

            int hurtCalls = 0;

            for (int index = 0; index < _heard.Count; index++)
            {
                if (_heard[index].LineId != null && _heard[index].LineId.StartsWith("hurt_"))
                {
                    hurtCalls++;
                }
            }

            Assert.That(hurtCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Two identical cues in a row are a real thing - the same fighter gets hurt twice
        /// across two matches - and ReactiveProperty compares before it notifies, so without
        /// the sequence number the second one is silently swallowed.
        /// </summary>
        [Test]
        public void ARepeatedCueStillReachesTheView()
        {
            Land(0, 1, 3, counter: true);
            CommentaryCue first = _heard[_heard.Count - 1];

            LetHimFinish();
            Land(0, 1, 3, counter: true);

            CommentaryCue second = _heard[_heard.Count - 1];

            Assert.That(_heard.Count, Is.EqualTo(2));
            Assert.That(second.Sequence, Is.GreaterThan(first.Sequence));
        }

        [Test]
        public void ASceneWithNoBankSaysNothing()
        {
            CommentaryModel silentModel = new();
            CommentarySystem silent = new(
                _match,
                silentModel,
                null,
                _flow,
                new RosterModel(),
                new RatingModel(),
                _config,
                _container.Resolve<ISubscriber<PunchLandedMessage>>(),
                _container.Resolve<ISubscriber<PunchBlockedMessage>>(),
                _container.Resolve<ISubscriber<BoxerDodgedMessage>>(),
                _container.Resolve<ISubscriber<BoxerEliminatedMessage>>(),
                _container.Resolve<ISubscriber<MatchEndedMessage>>());

            int said = 0;
            silentModel.Cue.Subscribe(cue =>
            {
                if (cue.HasLine)
                {
                    said++;
                }
            });

            silent.Tick(0.02f);
            _flow.Phase.Value = MatchFlowPhase.Fighting;
            Land(0, 1, 3, counter: true);

            Assert.That(said, Is.Zero);

            silent.Dispose();
        }
    }
}

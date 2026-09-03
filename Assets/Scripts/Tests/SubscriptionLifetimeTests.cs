using System;
using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// Covers acceptance criterion 18. Systems subscribe to MessagePipe for their whole life;
    /// if disposal does not unsubscribe them, reloading a scene stacks a second set of live
    /// handlers on the old ones and every punch is then applied twice.
    /// </summary>
    public sealed class SubscriptionLifetimeTests
    {
        private static IObjectResolver BuildContainer()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchEvadedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<HaymakerThrownMessage>(options);
            builder.RegisterMessageBroker<BoxerDodgedMessage>(options);
            builder.RegisterMessageBroker<BoxerDamagedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);
            builder.Register<MatchModel>(Lifetime.Singleton);
            builder.RegisterInstance(ScriptableObject.CreateInstance<BoxerConfig>());
            builder.Register<CombatSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchSystem>(Lifetime.Singleton).AsSelf();
            return builder.Build();
        }

        [Test]
        public void DisposedCombatSystemStopsApplyingDamage()
        {
            using IObjectResolver container = BuildContainer();
            MatchModel match = container.Resolve<MatchModel>();
            match.AddBoxer(new BoxerModel(0, 10));
            match.AddBoxer(new BoxerModel(1, 10));

            CombatSystem combat = container.Resolve<CombatSystem>();
            var publisher = container.Resolve<IPublisher<PunchLandedMessage>>();

            publisher.Publish(new PunchLandedMessage(0, 1, 2, false, Vector2.zero));
            Assert.That(match.Boxers[1].Health.Value, Is.EqualTo(8), "live system should apply damage");

            combat.Dispose();

            publisher.Publish(new PunchLandedMessage(0, 1, 2, false, Vector2.zero));
            Assert.That(match.Boxers[1].Health.Value, Is.EqualTo(8),
                "a disposed system must not still be handling messages");
        }

        [Test]
        public void ReloadingLeavesExactlyOneLiveHandler()
        {
            // Stand up a container, tear it down, then stand up another - the way a scene
            // reload does. The first generation must be completely detached.
            IObjectResolver first = BuildContainer();
            MatchModel firstMatch = first.Resolve<MatchModel>();
            firstMatch.AddBoxer(new BoxerModel(0, 10));
            firstMatch.AddBoxer(new BoxerModel(1, 10));
            CombatSystem firstCombat = first.Resolve<CombatSystem>();
            MatchSystem firstMatchSystem = first.Resolve<MatchSystem>();

            firstCombat.Dispose();
            firstMatchSystem.Dispose();
            first.Dispose();

            using IObjectResolver second = BuildContainer();
            MatchModel secondMatch = second.Resolve<MatchModel>();
            secondMatch.AddBoxer(new BoxerModel(0, 10));
            secondMatch.AddBoxer(new BoxerModel(1, 10));
            CombatSystem secondCombat = second.Resolve<CombatSystem>();

            second.Resolve<IPublisher<PunchLandedMessage>>()
                  .Publish(new PunchLandedMessage(0, 1, 3, false, Vector2.zero));

            Assert.That(secondMatch.Boxers[1].Health.Value, Is.EqualTo(7),
                "damage should be applied exactly once, not once per generation");
            Assert.That(firstMatch.Boxers[1].Health.Value, Is.EqualTo(10),
                "the previous generation's models must be untouched");

            secondCombat.Dispose();
        }

        [Test]
        public void DisposingTwiceIsHarmless()
        {
            using IObjectResolver container = BuildContainer();
            container.Resolve<MatchModel>().AddBoxer(new BoxerModel(0, 10));
            CombatSystem combat = container.Resolve<CombatSystem>();

            combat.Dispose();
            Assert.DoesNotThrow(() => combat.Dispose());
        }
    }
}

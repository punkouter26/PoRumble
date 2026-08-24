using MessagePipe;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PoRumble.Views
{
    /// <summary>Single place where every dependency is bound and resolved.</summary>
    public sealed class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] private BoxerConfig _boxerConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchEvadedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<BoxerDamagedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);

            builder.RegisterInstance(_boxerConfig);
            builder.Register<MatchModel>(Lifetime.Singleton);

            builder.Register<BoxerSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<CombatSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<SpawnSystem>(Lifetime.Singleton).AsSelf();

            // Scene components the systems depend on.
            builder.RegisterComponentInHierarchy<BoxerSpawnPoints>();

            builder.RegisterEntryPoint<MatchDirector>();

            // VContainer resolves lazily. CombatSystem and MatchSystem only ever subscribe to
            // messages, so nothing injects them and they would never be constructed - meaning
            // punches would silently do nothing. Force them to exist when the container builds.
            builder.RegisterBuildCallback(container =>
            {
                container.Resolve<CombatSystem>();
                container.Resolve<MatchSystem>();

                // The HUD is optional: the training scene deliberately has none, and
                // RegisterComponentInHierarchy would throw when it is absent.
                MatchHudView hud = Object.FindAnyObjectByType<MatchHudView>();

                if (hud != null)
                {
                    container.Inject(hud);
                }
            });
        }
    }
}

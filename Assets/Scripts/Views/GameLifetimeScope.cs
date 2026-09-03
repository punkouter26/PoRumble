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
            builder.RegisterMessageBroker<HaymakerThrownMessage>(options);
            builder.RegisterMessageBroker<BoxerDodgedMessage>(options);
            builder.RegisterMessageBroker<BoxerDamagedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);

            builder.RegisterInstance(_boxerConfig);
            builder.Register<MatchModel>(Lifetime.Singleton);
            builder.Register<TouchInputModel>(Lifetime.Singleton);
            builder.Register<MatchFlowModel>(Lifetime.Singleton);
            builder.Register<RosterModel>(Lifetime.Singleton);
            builder.Register<RatingModel>(Lifetime.Singleton);

            builder.Register<BoxerSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<CombatSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchFlowSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<SpawnSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<RosterSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<RatingSystem>(Lifetime.Singleton).AsSelf();

            // The league table on disk. A plain C# class rather than a component, so it is
            // registered as an instance; RatingSystem only ever sees the interface.
            builder.Register<IRatingStore>(_ => new FileRatingStore(), Lifetime.Singleton);

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

                // Same reason as the two above: RatingSystem only subscribes to messages, so
                // nothing injects it and VContainer would never construct it - every match
                // would resolve with the standings silently untouched.
                container.Resolve<RatingSystem>();

                // Presentation components are all optional: the training scenes deliberately
                // have no HUD, no camera rig and no feedback layer, and
                // RegisterComponentInHierarchy would throw when they are absent.
                InjectOptional<MatchHudView>(container);
                InjectOptional<PlayerStatusHudView>(container);
                InjectOptional<CombatFeedbackView>(container);
                InjectOptional<SpectatorCameraView>(container);
                InjectOptional<MatchInputView>(container);
                InjectOptional<RingAtmosphereView>(container);
                InjectOptional<DiagnosticsHudView>(container);
                InjectOptional<TouchControlsView>(container);
                InjectOptional<RosterSelectionView>(container);
                InjectOptional<StandingsHudView>(container);
            });
        }

        /// <summary>
        /// Injects a scene component if it is present, and does nothing if it is not.
        ///
        /// Inactive objects are included deliberately: a HUD that starts disabled still needs
        /// its dependencies before something enables it.
        /// </summary>
        private static void InjectOptional<T>(IObjectResolver container) where T : MonoBehaviour
        {
            T component = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

            if (component == null)
            {
                return;
            }

            container.Inject(component);
        }
    }
}

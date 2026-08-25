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
            builder.RegisterMessageBroker<BoxerDamagedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);

            builder.RegisterInstance(_boxerConfig);
            builder.Register<MatchModel>(Lifetime.Singleton);
            builder.Register<MatchFlowModel>(Lifetime.Singleton);

            builder.Register<BoxerSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<CombatSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchFlowSystem>(Lifetime.Singleton).AsSelf();
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

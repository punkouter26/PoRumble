using MessagePipe;
using PoRumble.Models;
using PoRumble.Systems;
using VContainer;
using VContainer.Unity;

namespace PoRumble.Views
{
    /// <summary>
    /// Everything one ring needs: its own roster, its own combat, its own director.
    ///
    /// Split out of <see cref="GameLifetimeScope"/> so a training scene can hold several
    /// arenas at once. A scene with one ring installs this into the root scope and is exactly
    /// what it always was; a scene with eight installs it into eight child scopes, and each
    /// gets a MatchModel and a set of message brokers of its own.
    ///
    /// The brokers are the load-bearing half. Boxer ids restart at zero in every arena, so a
    /// single shared PunchLandedMessage stream would pay arena three's fighter 0 for damage
    /// arena one's fighter 0 dealt - silently, and in a way that looks exactly like a policy
    /// learning something.
    /// </summary>
    internal static class ArenaInstaller
    {
        /// <summary>
        /// Registers the message brokers a ring publishes on.
        ///
        /// Called once at the root and again in each arena scope. A child registration
        /// shadows the parent's rather than conflicting with it, which is what keeps one
        /// arena's punches out of another's.
        /// </summary>
        public static void InstallMessaging(IContainerBuilder builder)
        {
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchEvadedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<PunchClashedMessage>(options);
            builder.RegisterMessageBroker<HaymakerThrownMessage>(options);
            builder.RegisterMessageBroker<BoxerDodgedMessage>(options);
            builder.RegisterMessageBroker<BoxerDamagedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);
        }

        /// <summary>
        /// Installs one ring. Pass the arena's own spawn points when this is a child scope;
        /// pass null in a single-arena scene, where the one set in the scene is found by
        /// hierarchy exactly as it always was.
        /// </summary>
        public static void Install(IContainerBuilder builder, BoxerSpawnPoints spawnPoints)
        {
            builder.Register<MatchModel>(Lifetime.Singleton);
            builder.Register<MatchFlowModel>(Lifetime.Singleton);

            builder.Register<BoxerSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<CombatSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchFlowSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<SpawnSystem>(Lifetime.Singleton).AsSelf();

            if (spawnPoints == null)
            {
                builder.RegisterComponentInHierarchy<BoxerSpawnPoints>();
            }
            else
            {
                // By instance, not by hierarchy: RegisterComponentInHierarchy searches the
                // whole scene, so in a multi-arena scene every arena would race to claim
                // whichever set of spawn points it happened to find first.
                builder.RegisterComponent(spawnPoints);
            }

            builder.RegisterEntryPoint<MatchDirector>();

            InstallEvaluationHarness(builder);

            // VContainer resolves lazily. CombatSystem and MatchSystem only ever subscribe to
            // messages, so nothing injects them and they would never be constructed - meaning
            // punches would silently do nothing.
            builder.RegisterBuildCallback(container =>
            {
                container.Resolve<CombatSystem>();
                container.Resolve<MatchSystem>();
            });
        }

        /// <summary>
        /// Wires up the checkpoint harness, but only when something has actually asked for a
        /// measurement.
        ///
        /// Gated on the request file rather than on a scene flag or a serialized bool: the
        /// thing that starts an evaluation is an Editor script, on the far side of a domain
        /// reload from anything that could hold state, and a scene edited to carry a harness
        /// would go on carrying it into every training run afterwards.
        /// </summary>
        private static void InstallEvaluationHarness(IContainerBuilder builder)
        {
            EvaluationRequest request = EvaluationRequest.Load();

            if (request == null)
            {
                return;
            }

            builder.RegisterInstance(request);
            builder.RegisterEntryPoint<EvaluationHarness>();
        }
    }
}

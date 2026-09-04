using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PoRumble.Views
{
    /// <summary>
    /// Single place where every dependency is bound and resolved.
    ///
    /// Two shapes, decided by what is in the scene. With no <see cref="ArenaLifetimeScope"/>
    /// children this registers the ring itself as well as everything shared, which is exactly
    /// what it always did and what both shipped scenes still do. With arena children it
    /// registers only the shared half and lets each arena own its own fight, so a training
    /// scene can run several rings at once.
    /// </summary>
    // Earlier than LifetimeScope's own -5000, so this scope has built before any
    // ArenaLifetimeScope awakes. VContainer resolves a child's parent with Find(type) and
    // *throws* when that parent has no container yet - it only auto-builds a parent that is
    // the VContainerSettings root, which this is not. Unity does not otherwise order Awake
    // between a parent object and its children.
    [DefaultExecutionOrder(-5100)]
    public sealed class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] private BoxerConfig _boxerConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            // Registered at the root whether or not arenas exist, because the shared systems
            // below subscribe to them. In a multi-arena scene each arena shadows these with
            // brokers of its own, and the root pair is then only what RatingSystem listens on.
            ArenaInstaller.InstallMessaging(builder);

            builder.RegisterInstance(_boxerConfig);
            builder.Register<TouchInputModel>(Lifetime.Singleton);

            // Shared by exactly two views: the chrome claims a frame when one of its buttons
            // is pressed, and MatchInputView yields that frame rather than also reading the
            // press as a confirmation.
            builder.Register<HudPointerModel>(Lifetime.Singleton);
            builder.Register<RosterModel>(Lifetime.Singleton);
            builder.Register<RatingModel>(Lifetime.Singleton);

            builder.Register<RosterSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<RatingSystem>(Lifetime.Singleton).AsSelf();

            // The league table on disk. A plain C# class rather than a component, so it is
            // registered as an instance; RatingSystem only ever sees the interface.
            builder.Register<IRatingStore>(_ => new FileRatingStore(), Lifetime.Singleton);

            // One ring, installed here. A multi-arena scene skips this and each
            // ArenaLifetimeScope installs its own; nothing else about the root changes.
            bool singleRing = !HasArenaChildren();

            if (singleRing)
            {
                ArenaInstaller.Install(builder, null);
            }

            builder.RegisterBuildCallback(container =>
            {
                // RatingSystem only subscribes to messages, so nothing injects it and
                // VContainer would never construct it - every match would resolve with the
                // standings silently untouched.
                //
                // Only in a single-ring scene, though. RatingSystem takes a MatchModel, and in
                // a multi-arena scene there is no such thing at the root - there are eight of
                // them, one per arena. Forcing it here throws on the first frame. Nothing is
                // lost: arenas exist only for training, training scenes carry no fight card,
                // and RosterModel.SeatOf returns null without one, so the league table would
                // stay empty in any case.
                if (singleRing)
                {
                    container.Resolve<RatingSystem>();
                }

                // Presentation components are all optional: the training scenes deliberately
                // have no HUD, no camera rig and no feedback layer, and
                // RegisterComponentInHierarchy would throw when they are absent.
                InjectOptional<AppChromeView>(container);
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
                InjectOptional<KnockoutMoodView>(container);
            });
        }

        /// <summary>
        /// True when this scene splits its rings into arena scopes.
        ///
        /// Searched under this object rather than across the scene, because "is this scope the
        /// parent of some arenas" is the actual question, and TrainingArenaBuilder always
        /// parents an arena here even though what really binds the two is the arena's
        /// serialized parentReference. Inactive children count: an
        /// arena disabled to shrink a run still has to be excluded from the single-ring path,
        /// or the root would register a second, competing MatchDirector.
        /// </summary>
        private bool HasArenaChildren()
        {
            return GetComponentInChildren<ArenaLifetimeScope>(true) != null;
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

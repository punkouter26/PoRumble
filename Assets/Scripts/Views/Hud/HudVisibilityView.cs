using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Clears the HUD off the screen while the fight is actually happening.
    ///
    /// A full HUD is the right amount of information for a menu and a results screen. It is the
    /// wrong amount for the thing it is describing: with the survivor column, the telemetry
    /// board and the standings all up at once,
    /// the ring - which is the whole point - is watched through the gaps between them. The
    /// panels come back the instant the fight is decided, which is when there is something to
    /// read rather than something to watch.
    ///
    /// Hidden with <see cref="VisualElement.visible"/> rather than display:none, and that is
    /// load-bearing. display:none takes the element out of layout, which leaves the document
    /// root with no resolved size - and <see cref="SafeAreaView"/> treats an unresolved root as
    /// a pass that has not finished yet, so it would re-run its scene-wide UIDocument search
    /// every single frame of every fight. visibility keeps the layout resolved and still draws
    /// nothing and takes no clicks.
    ///
    /// The diagnostics overlay is deliberately exempt. It is off by default and only ever up
    /// because somebody asked for it, so it is never part of the clutter this removes - and a
    /// frame-time readout that blanked itself during the only part of the run worth measuring
    /// would be useless. The field strip, the chrome rows and the player's own controls are
    /// exempt too - see <see cref="StaysUpDuringFight"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudVisibilityView : MonoBehaviour
    {
        [Tooltip("Hide the HUD while the fight is live. Off leaves every panel up throughout, " +
                 "which is the older behaviour.")]
        [SerializeField] private bool _clearDuringFight = true;

        private readonly CompositeDisposable _disposables = new();

        private MatchFlowModel _flow;

        [Inject]
        public void Construct(MatchFlowModel flow)
        {
            _flow = flow;
        }

        private void Start()
        {
            if (_flow == null)
            {
                return;
            }

            _flow.Phase.Subscribe(Apply).AddTo(_disposables);
        }

        /// <summary>
        /// The knockout hold counts as part of the fight. It is the slow-motion replay of the
        /// punch that ended it, and putting ten panels back over the top of that is exactly
        /// the moment the view most needs to be clear.
        /// </summary>
        private void Apply(MatchFlowPhase phase)
        {
            bool fighting = phase == MatchFlowPhase.Fighting ||
                            phase == MatchFlowPhase.KnockoutHold;

            bool hide = _clearDuringFight && fighting;

            // Searched on each phase change rather than cached at Start: this fires a handful
            // of times per match, and a cached array would go stale the moment anything in the
            // scene added or removed a document.
            UIDocument[] documents = FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include);

            for (int documentIndex = 0; documentIndex < documents.Length; documentIndex++)
            {
                UIDocument document = documents[documentIndex];

                if (document == null)
                {
                    continue;
                }

                if (StaysUpDuringFight(document))
                {
                    continue;
                }

                VisualElement root = document.rootVisualElement;

                if (root == null)
                {
                    continue;
                }

                root.visible = !hide;
            }
        }

        /// <summary>
        /// The documents this view never clears.
        ///
        /// The diagnostics overlay for the reason above. The field board and the second-fight
        /// feed because they are the broadcast layer for the live fight itself: the board turns
        /// into a thin strip of health and win chance for the fight, and a feed of the other
        /// fight has nothing to show at any other time.
        ///
        /// The chrome bar and the commentary caption because they sit in the chrome rows, off
        /// the ring entirely, so clearing them buys the fight nothing. The chrome bar used to be
        /// cleared with everything else, which took MENU - the only way out of a live fight on a
        /// phone - off the screen for exactly the phase it exists for.
        ///
        /// The player's own panel and the touch controls because a human cannot fight without
        /// them. Both used to be cleared too; nothing caught it only because the shipped build
        /// seats no human.
        /// </summary>
        private static bool StaysUpDuringFight(UIDocument document)
        {
            return document.GetComponent<DiagnosticsHudView>() != null
                   || document.GetComponent<MatchHudView>() != null
                   || document.GetComponent<PictureInPictureView>() != null
                   || document.GetComponent<AppChromeView>() != null
                   || document.GetComponent<CommentaryView>() != null
                   || document.GetComponent<PlayerStatusHudView>() != null
                   || document.GetComponent<TouchControlsView>() != null
                   || document.GetComponent<EliminationFeedView>() != null
                   || document.GetComponent<FocusTagView>() != null;
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}

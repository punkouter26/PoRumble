using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Clears the HUD off the screen while the fight is actually happening.
    ///
    /// Ten panels is the right amount of information for a menu, a results screen and a fight
    /// card. It is the wrong amount for the thing they are all describing: with the survivor
    /// column, the telemetry board, the standings and the commentary caption all up at once,
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
    /// would be useless.
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
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int documentIndex = 0; documentIndex < documents.Length; documentIndex++)
            {
                UIDocument document = documents[documentIndex];

                if (document == null)
                {
                    continue;
                }

                if (document.GetComponent<DiagnosticsHudView>() != null)
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

        private void OnDestroy() => _disposables.Dispose();
    }
}

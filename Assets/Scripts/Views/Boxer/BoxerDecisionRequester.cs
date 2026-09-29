using Unity.MLAgents;
using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// The stock requester, except that a knocked-out boxer stops asking for decisions.
    ///
    /// A match ends every agent's episode together, at the last knockout or the bell, so a
    /// boxer dropped early in a ten-way used to go on deciding for the rest of the match:
    /// hundreds of steps of a corpse's observations and actions that could change nothing,
    /// all shipped to the trainer as experience and run through inference in the game. With
    /// the requests stopped, the agent's next report is the terminal one EndEpisode sends,
    /// which still carries the knockout penalty - so the trajectory closes on the step that
    /// earned it instead of trailing a long run of zeros behind it.
    ///
    /// It also changes what Environment/Episode Length means in TensorBoard: it is now how
    /// long a learner stayed on its feet, not how long the match ran. Match/Length Seconds
    /// is the match.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoxerDecisionRequester : DecisionRequester
    {
        // Resolved lazily rather than in Awake: the base class does its Academy subscription
        // in an Awake of its own, and declaring one here would hide it and never subscribe.
        private BoxerAgentView _boxer;

        protected override bool ShouldRequestDecision(DecisionRequestContext context)
        {
            return base.ShouldRequestDecision(context) && IsStanding();
        }

        protected override bool ShouldRequestAction(DecisionRequestContext context)
        {
            return base.ShouldRequestAction(context) && IsStanding();
        }

        private bool IsStanding()
        {
            if (_boxer == null)
            {
                _boxer = Agent as BoxerAgentView;
            }

            return _boxer == null || _boxer.IsStanding;
        }
    }
}

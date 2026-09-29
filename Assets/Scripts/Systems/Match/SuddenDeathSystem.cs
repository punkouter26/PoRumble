using System;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Runs the fight clock and closes the ropes in once it passes
    /// <see cref="SuddenDeathMath.START_SECONDS"/>.
    ///
    /// Stepped by MatchDirector on the physics clock, and only in the game scene: training has
    /// its own MaxStep bell and must see exactly the ring it always has, so a training scene
    /// never calls <see cref="Step"/> and <see cref="MatchModel.RingScale"/> stays at 1.
    ///
    /// Scaled time on purpose, unlike the flow loop. The ropes are part of the fight, so hitstop
    /// and the knockout hold should slow them exactly as they slow the fighters.
    /// </summary>
    public sealed class SuddenDeathSystem : IDisposable
    {
        private readonly MatchModel _match;
        private readonly MatchFlowModel _flow;
        private readonly SuddenDeathModel _model;
        private readonly CompositeDisposable _disposables = new();

        private float _fightSeconds;

        [Inject]
        public SuddenDeathSystem(MatchModel match, MatchFlowModel flow, SuddenDeathModel model)
        {
            _match = match;
            _flow = flow;
            _model = model;

            // A fight is introduced from the title, so both are fresh-ring moments. Keyed off
            // the flow rather than MatchModel.Phase, which MENU can leave unchanged.
            _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
        }

        /// <summary>
        /// Advances the clock and the ropes while the fight is live. Returns true once the
        /// final bell is due, which the caller answers by deciding the match on health.
        /// </summary>
        public bool Step(float deltaSeconds)
        {
            if (!_flow.IsFightLive || deltaSeconds <= 0f)
            {
                return false;
            }

            _fightSeconds += deltaSeconds;

            _match.RingScale = SuddenDeathMath.RingScaleAt(_fightSeconds);
            _model.Closing.Value = SuddenDeathMath.IsClosing(_fightSeconds);
            _model.FightSeconds.Value = Mathf.FloorToInt(_fightSeconds);

            return SuddenDeathMath.IsBell(_fightSeconds);
        }

        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            if (phase == MatchFlowPhase.Introducing || phase == MatchFlowPhase.Title)
            {
                Reset();
            }
        }

        private void Reset()
        {
            _fightSeconds = 0f;
            _match.RingScale = 1f;
            _model.Closing.Value = false;
            _model.FightSeconds.Value = 0;
        }

        public void Dispose() => _disposables.Dispose();
    }
}

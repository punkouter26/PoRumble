using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Drives the loop that wraps a fight: rack the fighters, count down to the bell, run the
    /// fight, hold on the knockout, show the result, restart.
    ///
    /// Before this existed the game scene had no loop at all. A match resolved, the banner
    /// appeared, and nothing further could happen without leaving Play mode - the reset path
    /// that training used every episode was simply never reachable from the game.
    ///
    /// Runs on unscaled time so the knockout hold can slow the world down without also
    /// slowing down the clock that ends the hold.
    /// </summary>
    public sealed class MatchFlowSystem
    {
        private readonly MatchFlowModel _flow;
        private readonly MatchModel _match;
        private readonly SpawnSystem _spawnSystem;

        /// <summary>Seconds of "get ready" before the countdown numbers start.</summary>
        private const float INTRO_SECONDS = 0.8f;

        /// <summary>Countdown length. Three beats, matching the caption the HUD shows.</summary>
        private const float COUNTDOWN_SECONDS = 3f;

        /// <summary>How long the world stays in slow motion after the final knockout.</summary>
        private const float KNOCKOUT_HOLD_SECONDS = 1.6f;

        /// <summary>World speed during the knockout hold.</summary>
        private const float KNOCKOUT_TIME_SCALE = 0.25f;

        private float _phaseElapsed;
        private int _boxerCount;
        private float _spawnRadius;

        [Inject]
        public MatchFlowSystem(MatchFlowModel flow, MatchModel match, SpawnSystem spawnSystem)
        {
            _flow = flow;
            _match = match;
            _spawnSystem = spawnSystem;
        }

        /// <summary>Remembers the roster shape so a restart can re-rack the same fight.</summary>
        public void Configure(int boxerCount, float spawnRadius)
        {
            _boxerCount = boxerCount;
            _spawnRadius = spawnRadius;
        }

        /// <summary>
        /// Advances the round loop. Returns true while the fight is live, which is the signal
        /// the director uses to decide whether to tick combat at all.
        /// </summary>
        public bool Tick(float unscaledDeltaTime)
        {
            _phaseElapsed += unscaledDeltaTime;

            switch (_flow.Phase.Value)
            {
                case MatchFlowPhase.Title:
                    // The menu waits for the player rather than a clock. Nothing to advance.
                    break;

                case MatchFlowPhase.Introducing:
                    TickIntro();
                    break;

                case MatchFlowPhase.Countdown:
                    TickCountdown();
                    break;

                case MatchFlowPhase.Fighting:
                    TickFighting();
                    break;

                case MatchFlowPhase.KnockoutHold:
                    TickKnockoutHold();
                    break;

                case MatchFlowPhase.Results:
                    break;
            }

            return _flow.Phase.Value == MatchFlowPhase.Fighting;
        }

        private void TickIntro()
        {
            if (_phaseElapsed < INTRO_SECONDS)
            {
                return;
            }

            _flow.CountdownSeconds.Value = Mathf.CeilToInt(COUNTDOWN_SECONDS);
            EnterPhase(MatchFlowPhase.Countdown);
        }

        private void TickCountdown()
        {
            float remaining = COUNTDOWN_SECONDS - _phaseElapsed;

            // Ceil so the caption reads 3, 2, 1 rather than sitting on 0 for a whole second.
            int seconds = Mathf.Max(0, Mathf.CeilToInt(remaining));

            if (seconds != _flow.CountdownSeconds.Value)
            {
                _flow.CountdownSeconds.Value = seconds;
            }

            if (remaining > 0f)
            {
                return;
            }

            EnterPhase(MatchFlowPhase.Fighting);
        }

        private void TickFighting()
        {
            if (_match.Phase.Value != MatchPhase.Ended)
            {
                return;
            }

            // Slow the world so the final blow reads, rather than cutting straight to a banner.
            Time.timeScale = KNOCKOUT_TIME_SCALE;
            EnterPhase(MatchFlowPhase.KnockoutHold);
        }

        private void TickKnockoutHold()
        {
            if (_phaseElapsed < KNOCKOUT_HOLD_SECONDS)
            {
                return;
            }

            Time.timeScale = 1f;
            EnterPhase(MatchFlowPhase.Results);
        }

        /// <summary>
        /// Re-racks the fighters and returns to the menu. Ignored unless the results are up, so
        /// a mashed restart key cannot cut a fight short.
        ///
        /// Lands on <see cref="MatchFlowPhase.Title"/> rather than going straight back into a
        /// countdown. The card is the only thing a player can actually change between matches
        /// and the menu is where it is reachable, so dropping them back at the bell would make
        /// a ten-way exhibition the only thing the game can do.
        /// </summary>
        public bool TryRestart()
        {
            if (!_flow.CanRestart)
            {
                return false;
            }

            Time.timeScale = 1f;
            _spawnSystem.ResetRoster(_boxerCount, _spawnRadius);
            _match.BeginNewEpisode();
            _flow.MatchNumber.Value++;
            EnterPhase(MatchFlowPhase.Title);
            return true;
        }

        /// <summary>
        /// Starts a fight from the menu. Ignored anywhere else, so the tap that dismissed the
        /// results cannot also be read as the tap that starts the next bout.
        /// </summary>
        public bool TryStartFight()
        {
            if (!_flow.CanStartFight)
            {
                return false;
            }

            EnterPhase(MatchFlowPhase.Introducing);
            return true;
        }

        /// <summary>
        /// Restores normal time. Called when the scene tears down: Time.timeScale is global
        /// and survives leaving Play mode, so a knockout hold interrupted by a scene change
        /// would otherwise leave the Editor running at quarter speed.
        /// </summary>
        public void ResetTimeScale()
        {
            Time.timeScale = 1f;
        }

        private void EnterPhase(MatchFlowPhase phase)
        {
            _phaseElapsed = 0f;
            _flow.Phase.Value = phase;
        }
    }
}

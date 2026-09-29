using System;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Records where every landed punch was landed, for the fight map on the results screen.
    ///
    /// Entirely a subscriber, like FightStatsSystem: it publishes nothing and nothing in combat
    /// reads what it writes. Resolved eagerly in GameLifetimeScope for the same reason that
    /// one is - nothing injects it, so VContainer would otherwise never construct it and the
    /// map would come up blank every match.
    /// </summary>
    public sealed class DamageMapSystem : IDisposable
    {
        /// <summary>
        /// Cells either side of the impact the damage is spread over. A punch lands at a point
        /// but the scrap around it covers a fighter's width, and a map of single-cell pinpricks
        /// reads as noise rather than as "they fought in that corner".
        /// </summary>
        private const int KERNEL_RADIUS = 2;

        /// <summary>Spread of the kernel, in cells.</summary>
        private const float KERNEL_SIGMA = 1.1f;

        private const int KERNEL_SIZE = KERNEL_RADIUS * 2 + 1;

        private readonly MatchModel _match;
        private readonly DamageMapModel _map;
        private readonly CompositeDisposable _disposables = new();

        /// <summary>Normalised Gaussian weights, summing to one so a punch deposits exactly its own damage.</summary>
        private readonly float[] _kernel = new float[KERNEL_SIZE * KERNEL_SIZE];

        [Inject]
        public DamageMapSystem(
            MatchModel match,
            DamageMapModel map,
            ISubscriber<PunchLandedMessage> landedSubscriber)
        {
            _match = match;
            _map = map;

            BuildKernel();

            landedSubscriber.Subscribe(OnPunchLanded).AddTo(_disposables);
            _match.Phase.Subscribe(OnMatchPhaseChanged).AddTo(_disposables);
        }

        /// <summary>Spreads one punch's damage over the cells around where it landed.</summary>
        public void Deposit(Vector2 position, float damage)
        {
            if (damage <= 0f)
            {
                return;
            }

            // The ring's size is set by the scene after this system is built, so the grid is
            // re-fitted the first time a punch finds it measuring the wrong ring.
            if (_map.HalfExtent != _match.ArenaHalfExtent)
            {
                _map.Configure(_match.ArenaHalfExtent);
            }

            if (!_map.TryWorldToCell(position, out int centreX, out int centreY))
            {
                return;
            }

            for (int offsetY = -KERNEL_RADIUS; offsetY <= KERNEL_RADIUS; offsetY++)
            {
                for (int offsetX = -KERNEL_RADIUS; offsetX <= KERNEL_RADIUS; offsetX++)
                {
                    float weight = _kernel[(offsetY + KERNEL_RADIUS) * KERNEL_SIZE + offsetX + KERNEL_RADIUS];
                    _map.Add(centreX + offsetX, centreY + offsetY, damage * weight);
                }
            }
        }

        private void OnPunchLanded(PunchLandedMessage message)
        {
            Deposit(message.Position, message.Damage);
        }

        private void OnMatchPhaseChanged(MatchPhase phase)
        {
            if (phase == MatchPhase.InProgress)
            {
                _map.Configure(_match.ArenaHalfExtent);
            }
        }

        private void BuildKernel()
        {
            float total = 0f;

            for (int offsetY = -KERNEL_RADIUS; offsetY <= KERNEL_RADIUS; offsetY++)
            {
                for (int offsetX = -KERNEL_RADIUS; offsetX <= KERNEL_RADIUS; offsetX++)
                {
                    float weight = Mathf.Exp(
                        -(offsetX * offsetX + offsetY * offsetY) / (2f * KERNEL_SIGMA * KERNEL_SIGMA));

                    _kernel[(offsetY + KERNEL_RADIUS) * KERNEL_SIZE + offsetX + KERNEL_RADIUS] = weight;
                    total += weight;
                }
            }

            for (int index = 0; index < _kernel.Length; index++)
            {
                _kernel[index] /= total;
            }
        }

        public void Dispose()
        {
            _disposables.Dispose();
        }
    }
}

using System.Collections.Generic;
using PoRumble.Models;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PoRumble.Views
{
    /// <summary>Scene-side spawn configuration and boxer view instantiation.</summary>
    [DisallowMultipleComponent]
    public sealed class BoxerSpawnPoints : MonoBehaviour
    {
        [Tooltip("Boxers already placed in the scene. When set, these are used instead of " +
                 "instantiating the prefab, so the ring is visible without entering Play mode.")]
        [SerializeField] private BoxerView[] _preplacedBoxers;
        [Tooltip("Used only when no pre-placed boxers are assigned.")]
        [SerializeField] private BoxerView _boxerPrefab;
        [SerializeField] private Transform _boxerParent;
        [SerializeField] private int _boxerCount = 10;
        [SerializeField] private float _spawnRadius = 15f;
        [Tooltip("Half width/height of the ring interior. Boxers are clamped inside this.")]
        [SerializeField] private Vector2 _arenaHalfExtent = new(20f, 20f);
        [Tooltip("Restart automatically when the match resolves. Required for training.")]
        [SerializeField] private bool _autoRestart;

        private readonly List<BoxerView> _views = new();
        private IObjectResolver _resolver;
        private readonly List<BoxerAgentView> _agents = new();

        public int BoxerCount => HasPreplacedBoxers ? _preplacedBoxers.Length : _boxerCount;

        private bool HasPreplacedBoxers => _preplacedBoxers != null && _preplacedBoxers.Length > 0;
        public float SpawnRadius => _spawnRadius;
        public Vector2 ArenaHalfExtent => _arenaHalfExtent;
        public bool AutoRestart => _autoRestart;

        [Inject]
        public void Construct(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public IReadOnlyList<BoxerAgentView> Agents => _agents;

        public void BuildViews(IReadOnlyList<BoxerModel> boxers)
        {
            if (_boxerPrefab == null)
            {
                return;
            }

            Transform parent = _boxerParent != null ? _boxerParent : transform;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];
                BoxerView view = Instantiate(_boxerPrefab, boxer.Position, Quaternion.identity, parent);
                view.name = $"Boxer_{boxer.Id:00}";

                // Boxers are created after the container is built, so they cannot be found by
                // RegisterComponentInHierarchy - each instance is injected on spawn instead.
                _resolver.InjectGameObject(view.gameObject);
                view.Bind(boxer);

                if (view.TryGetComponent(out BoxerAgentView agent))
                {
                    agent.Bind(boxer);
                    _agents.Add(agent);
                }

                _views.Add(view);
            }
        }
    }
}

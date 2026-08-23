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
        private readonly List<BoxerAgentView> _agents = new();
        private IObjectResolver _resolver;
        private bool _built;

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
            // A second call would spawn a duplicate set of boxers whose agents never get bound.
            // Those ghosts still receive decisions and feed empty experience to the trainer,
            // which silently halves the useful data in a run.
            if (_built)
            {
                Debug.LogWarning($"[PoRumble] BuildViews called twice on {name}; ignoring.");
                return;
            }

            _built = true;
            Transform parent = _boxerParent != null ? _boxerParent : transform;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];
                BoxerView view = ResolveView(boxerIndex, boxer, parent);

                if (view == null)
                {
                    continue;
                }

                // Boxers may be created after the container is built, so they cannot be found by
                // RegisterComponentInHierarchy - each instance is injected here instead.
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

        /// <summary>
        /// Uses a boxer already placed in the scene when one is assigned, so the ring is visible
        /// without entering Play mode. Falls back to instantiating the prefab.
        /// </summary>
        private BoxerView ResolveView(int boxerIndex, BoxerModel boxer, Transform parent)
        {
            if (HasPreplacedBoxers)
            {
                return boxerIndex < _preplacedBoxers.Length ? _preplacedBoxers[boxerIndex] : null;
            }

            if (_boxerPrefab == null)
            {
                return null;
            }

            BoxerView spawned = Instantiate(_boxerPrefab, boxer.Position, Quaternion.identity, parent);
            spawned.name = $"Boxer_{boxer.Id:00}";
            return spawned;
        }
    }
}

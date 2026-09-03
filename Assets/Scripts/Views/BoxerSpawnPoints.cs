using System;
using System.Collections.Generic;
using PoRumble.Models;
using Unity.MLAgents.Sensors;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PoRumble.Views
{
    /// <summary>Scene-side spawn configuration and boxer view instantiation.</summary>
    [DisallowMultipleComponent]
    public sealed class BoxerSpawnPoints : MonoBehaviour
    {
        /// <summary>
        /// A block of scripted opponents cut from the same profile. Several of these make up
        /// the undercard, so one match can field a pressure fighter, two journeymen and a
        /// counter-puncher rather than ten copies of a single bot.
        /// </summary>
        [Serializable]
        private sealed class RosterTier
        {
            [SerializeField] private BrainProfile _profile;
            [Min(0)]
            [SerializeField] private int _count = 1;

            public BrainProfile Profile => _profile;
            public int Count => _count;
        }

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
        [Tooltip("Boxer id handed to the keyboard. -1 leaves every boxer under AI control.")]
        [SerializeField] private int _humanBoxerId = -1;
        [Tooltip("Boxer ids driven by the hand-written sparring brain rather than a policy. " +
                 "Gives the learners a competent opponent from the very first episode.")]
        [SerializeField] private int[] _scriptedBoxerIds = Array.Empty<int>();

        [Tooltip("Difficulty tiers for the scripted undercard, filled in roster order after " +
                 "the player. Boxers left over once the tiers run out keep the trained policy, " +
                 "so a match can mix hand-written opponents with the learned one.")]
        [SerializeField] private RosterTier[] _rosterTiers = Array.Empty<RosterTier>();

        [Tooltip("Profile used for ids listed in Scripted Boxer Ids that no tier covers. " +
                 "Leave empty to fall back to the brain's original built-in tuning.")]
        [SerializeField] private BrainProfile _defaultProfile;

        [Tooltip("Colour learning agents black and scripted bots white, instead of the " +
                 "ten-way palette.")]
        [SerializeField] private bool _useRoleColors;

        [Tooltip("The full card of selectable contestants. When any are assigned they replace " +
                 "the Roster Tiers path entirely: each boxer is seated from the roster " +
                 "instead, and gets that contestant's face, colour, style and attributes. " +
                 "Leave empty in the training scenes - a run must learn against the " +
                 "unmodified policy.")]
        [SerializeField] private FighterProfile[] _fighterProfiles = Array.Empty<FighterProfile>();

        /// <summary>
        /// Prefix of the per-boxer collider layers, one per roster slot, that
        /// <see cref="IsolatePerception"/> moves each fighter's own colliders onto.
        /// </summary>
        private const string BOXER_LAYER_PREFIX = "BoxerBody";

        private readonly List<BoxerView> _views = new();
        private readonly List<BoxerAgentView> _agents = new();

        /// <summary>
        /// One entry per view, in seat order, holding null where a view has no agent.
        /// <see cref="_agents"/> is the compacted list the director iterates and cannot carry
        /// the gaps, but seating has to line up with <see cref="_views"/> exactly.
        /// </summary>
        private readonly List<BoxerAgentView> _seatAgents = new();
        private IObjectResolver _resolver;
        private RosterModel _roster;
        private bool _built;

        /// <summary>True when a card of contestants was assigned, so the roster drives seating.</summary>
        private bool HasRoster => _fighterProfiles != null && _fighterProfiles.Length > 0;

        /// <summary>The contestants this scene can field, for the roster screen to list.</summary>
        public IReadOnlyList<FighterProfile> FighterProfiles => _fighterProfiles;

        public int BoxerCount => HasPreplacedBoxers ? _preplacedBoxers.Length : _boxerCount;

        private bool HasPreplacedBoxers => _preplacedBoxers != null && _preplacedBoxers.Length > 0;
        public float SpawnRadius => _spawnRadius;
        public Vector2 ArenaHalfExtent => _arenaHalfExtent;
        public bool AutoRestart => _autoRestart;

        /// <summary>Boxer the keyboard drives, or -1. The player HUD needs to know who to watch.</summary>
        public int HumanBoxerId => _humanBoxerId;

        [Inject]
        public void Construct(IObjectResolver resolver, RosterModel roster)
        {
            _resolver = resolver;
            _roster = roster;

            // Published here rather than in BuildViews so the roster screen can list the card
            // before a single boxer exists.
            if (HasRoster)
            {
                _roster.SetAvailable(_fighterProfiles);
            }
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
            int tierIndex = 0;
            int tierRemaining = 0;

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

                // The agent lives on the Torso child, not the prefab root, because its ray
                // sensor has to rotate with the boxer's facing.
                BoxerAgentView agent = view.GetComponentInChildren<BoxerAgentView>();

                if (agent != null)
                {
                    agent.Bind(boxer);

                    bool isHuman = boxer.Id == _humanBoxerId;
                    agent.SetHumanControlled(isHuman);

                    // Marks the seat visually. Set here rather than in SeatRoster because
                    // which chair the human occupies is a scene setting, not a card decision:
                    // re-dealing the roster changes who sits where, never who is holding the
                    // keyboard. The shipped Android build passes -1 and nobody is marked.
                    view.SetIsPlayer(isHuman);

                    // A card of contestants replaces the tier path outright: who fights, how
                    // and with what face is the roster's business, and SeatRoster does all of
                    // it in one place so a re-deal between matches runs the same code.
                    if (!HasRoster)
                    {
                        ApplyTier(view, agent, boxer, isHuman, ref tierIndex, ref tierRemaining);
                    }

                    IsolatePerception(view, agent, boxer.Id);
                    DisableSelfCollision(view);
                    _agents.Add(agent);
                }

                _seatAgents.Add(agent);
                _views.Add(view);
            }

            SeatRoster();
        }

        /// <summary>
        /// Deals the selected contestants round the ring and dresses every boxer as whoever
        /// landed in its chair.
        ///
        /// Safe to call again between matches, which is the point: changing the card must not
        /// mean tearing down and respawning ten agents. Everything it touches - face, colour,
        /// controller, style, attributes - is a reconfiguration of an object that already
        /// exists.
        /// </summary>
        public void SeatRoster()
        {
            if (!HasRoster || _roster == null)
            {
                return;
            }

            _roster.AssignSeats(_views.Count);

            for (int seat = 0; seat < _views.Count; seat++)
            {
                FighterProfile profile = _roster.SeatOf(seat);
                _views[seat].ApplyIdentity(profile);

                BoxerAgentView agent = _seatAgents[seat];

                if (agent != null)
                {
                    agent.ApplyFighter(profile);
                }
            }
        }

        /// <summary>
        /// The pre-roster path: fills the undercard from the difficulty tiers. Still the only
        /// path the training scenes take, and the reason they can omit a card entirely.
        /// </summary>
        private void ApplyTier(
            BoxerView view,
            BoxerAgentView agent,
            BoxerModel boxer,
            bool isHuman,
            ref int tierIndex,
            ref int tierRemaining)
        {
            BrainProfile profile = isHuman
                ? null
                : NextTierProfile(ref tierIndex, ref tierRemaining);

            bool listedAsScripted = Array.IndexOf(_scriptedBoxerIds, boxer.Id) >= 0;

            if (listedAsScripted && profile == null)
            {
                profile = _defaultProfile;
            }

            // A boxer is hand-written if a tier claimed it or the id was listed explicitly.
            // Everyone else keeps the trained policy.
            bool scripted = !isHuman && (listedAsScripted || profile != null);

            agent.SetBrainProfile(profile);
            agent.SetScriptedBot(scripted);

            if (_useRoleColors)
            {
                view.SetRoleColor(scripted);
            }
        }

        /// <summary>
        /// Stops a boxer's own parts from colliding with each other.
        ///
        /// A HingeJoint2D only excludes the two bodies it directly connects, so a glove still
        /// collides with the torso it is nowhere near being jointed to. With the arms folded
        /// into a guard the gloves sit inside the torso's collider, and the servo then spends
        /// every frame pushing against a contact it can never win - the arm jitters and the
        /// pose never settles. Limbs of one body have no business colliding anyway; only
        /// other fighters and the ropes should stop them.
        /// </summary>
        private static void DisableSelfCollision(BoxerView view)
        {
            Collider2D[] colliders = view.GetComponentsInChildren<Collider2D>(true);

            for (int first = 0; first < colliders.Length; first++)
            {
                for (int second = first + 1; second < colliders.Length; second++)
                {
                    Physics2D.IgnoreCollision(colliders[first], colliders[second], true);
                }
            }
        }

        /// <summary>
        /// Moves one boxer's colliders onto a layer of its own and masks that layer out of
        /// that boxer's ray sensor, so a fighter never perceives itself.
        ///
        /// A 2D cast cannot be told to skip the collider that fired it, and there is nothing
        /// in RayPerceptionSensor that excludes the caster. Physics2D.queriesStartInColliders
        /// being off covers the body collider the sensor sits inside, but the face probe is
        /// nearly a metre in front of it and the gloves further still: without this, every
        /// forward ray - the ones pointing exactly where the boxer is about to punch - reports
        /// the boxer's own BoxerFace at half a metre, for ever.
        /// </summary>
        private static void IsolatePerception(BoxerView view, BoxerAgentView agent, int boxerId)
        {
            int layer = LayerMask.NameToLayer(BOXER_LAYER_PREFIX + boxerId);

            if (layer < 0)
            {
                Debug.LogWarning(
                    $"[PoRumble] No layer named '{BOXER_LAYER_PREFIX}{boxerId}'. Boxer " +
                    $"{boxerId}'s rays will keep reporting its own face probe as a target.");
                return;
            }

            Collider2D[] colliders = view.GetComponentsInChildren<Collider2D>(true);

            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                colliders[colliderIndex].gameObject.layer = layer;
            }

            // Subtracted from whatever the prefab authored rather than replacing it, so the
            // sensor keeps ignoring Ignore Raycast and anything else deliberately masked out.
            if (agent.TryGetComponent(out RayPerceptionSensorComponent2D sensor))
            {
                sensor.RayLayerMask = sensor.RayLayerMask.value & ~(1 << layer);
            }
        }

        /// <summary>
        /// Hands out the next tier's profile, or null once the tiers are exhausted. Tiers with
        /// no profile assigned are skipped rather than silently turning a policy bot into a
        /// default-tuned scripted one.
        /// </summary>
        private BrainProfile NextTierProfile(ref int tierIndex, ref int tierRemaining)
        {
            if (_rosterTiers == null)
            {
                return null;
            }

            while (tierRemaining <= 0)
            {
                if (tierIndex >= _rosterTiers.Length)
                {
                    return null;
                }

                RosterTier tier = _rosterTiers[tierIndex];
                tierIndex++;

                if (tier == null || tier.Profile == null || tier.Count <= 0)
                {
                    continue;
                }

                tierRemaining = tier.Count;
                tierRemaining--;
                return tier.Profile;
            }

            tierRemaining--;
            return _rosterTiers[tierIndex - 1].Profile;
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

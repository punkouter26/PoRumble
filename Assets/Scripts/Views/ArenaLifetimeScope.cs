using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PoRumble.Views
{
    /// <summary>
    /// One training ring's container, nested under <see cref="GameLifetimeScope"/>.
    ///
    /// A ten-way trains ten agents against one arena, and the trainer's throughput is bounded
    /// by how many experiences a physics step produces. A scene with eight of these produces
    /// eight times as many for little more than eight times the physics. The shared config,
    /// the rating table and the fight card stay in the parent scope; everything that is
    /// per-fight - the roster, the combat, the director, the message brokers - is installed
    /// here.
    ///
    /// VContainer finds the parent by the serialized parentReference type, not by walking the
    /// transform hierarchy - being a child object is for tidiness, the reference is what
    /// actually binds. TrainingArenaBuilder sets both.
    /// </summary>
    // After GameLifetimeScope's -5100 and before anything else. See the note there: a child
    // scope whose parent has not built yet throws rather than waiting.
    [DefaultExecutionOrder(-5050)]
    public sealed class ArenaLifetimeScope : LifetimeScope
    {
        [Tooltip("This arena's own spawn points. Assigned rather than found by hierarchy: a " +
                 "hierarchy search covers the whole scene, so every arena would claim " +
                 "whichever set it happened to find first and nine of them would end up " +
                 "fighting in the same ring.")]
        [SerializeField] private BoxerSpawnPoints _spawnPoints;

        protected override void Configure(IContainerBuilder builder)
        {
            if (_spawnPoints == null)
            {
                Debug.LogError(
                    $"[PoRumble] {name} has no spawn points assigned; this arena will not run.");
                return;
            }

            // Its own brokers, shadowing the parent's. Boxer ids restart at zero in every
            // arena, so sharing one punch stream would credit the wrong fighter in the wrong
            // ring - which reads exactly like a policy learning something.
            ArenaInstaller.InstallMessaging(builder);
            ArenaInstaller.Install(builder, _spawnPoints);
        }
    }
}

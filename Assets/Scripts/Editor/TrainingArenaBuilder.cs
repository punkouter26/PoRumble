#if UNITY_EDITOR
using PoRumble.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoRumble.Editor
{
    /// <summary>
    /// Builds a training scene holding several copies of the ten-way ring, each with its own
    /// container.
    ///
    /// Throughput in ML-Agents is bounded by experiences per physics step, and until now both
    /// training scenes held exactly one ring. Duplicating the arena is the standard answer and
    /// the only change here with a multiplier on it rather than a percentage.
    ///
    /// Generated rather than hand-built, because the wiring is the part that goes wrong: every
    /// arena needs its own <see cref="ArenaLifetimeScope"/> pointed at its own spawn points,
    /// and it needs to sit far enough from its neighbours that no ray sensor can see into the
    /// next ring. Both are easy to get subtly wrong by hand and neither fails loudly.
    ///
    /// The guard on the file is for the repo's guard-editor-runtime hook, which matches Editor
    /// folders with a forward-slash glob and cannot see one in a Windows path; the Editor
    /// asmdef is what actually keeps this out of a player build.
    /// </summary>
    internal static class TrainingArenaBuilder
    {
        private const string SOURCE_SCENE = "Assets/Scenes/Training10.unity";

        /// <summary>
        /// Centre-to-centre spacing between arenas.
        ///
        /// The ring is 40 across and RayLength is 24, so a fighter pinned against the east
        /// rope of one arena can see 24 units past it. Anything under 64 lets it perceive the
        /// fight next door - which does not crash, does not warn, and simply teaches the
        /// policy about opponents it can never reach. 80 leaves a clear margin.
        /// </summary>
        private const float ARENA_SPACING = 80f;

        [MenuItem("PoRumble/Build 4-Arena Training Scene")]
        private static void BuildFour() => Build(4);

        [MenuItem("PoRumble/Build 8-Arena Training Scene")]
        private static void BuildEight() => Build(8);

        private static void Build(int arenaCount)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(SOURCE_SCENE, OpenSceneMode.Single);

            GameLifetimeScope root = Object.FindAnyObjectByType<GameLifetimeScope>(FindObjectsInactive.Include);
            GameObject ring = GameObject.Find("/Ring");

            if (root == null || ring == null)
            {
                Debug.LogError(
                    $"[PoRumble] {SOURCE_SCENE} needs a GameLifetimeScope and a root object " +
                    "named Ring; cannot build a multi-arena scene from it.");
                return;
            }

            int columns = Mathf.CeilToInt(Mathf.Sqrt(arenaCount));

            for (int arenaIndex = 0; arenaIndex < arenaCount; arenaIndex++)
            {
                // The first arena reuses the ring that is already there; the rest are copies
                // of it, so every arena is dressed identically whatever the source scene grew.
                GameObject copy = arenaIndex == 0
                    ? ring
                    : Object.Instantiate(ring);

                copy.name = "Ring";
                BuildArena(root.transform, copy, arenaIndex, columns);
            }

            string path = $"Assets/Scenes/Training10x{arenaCount}.unity";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();

            Debug.Log(
                $"[PoRumble] Built {path} with {arenaCount} arenas at {ARENA_SPACING} spacing. " +
                "Add it to the build list if you intend to train from a player rather than the " +
                "Editor, and remember every arena runs the same behaviour name.");
        }

        /// <summary>
        /// Wraps one ring in an arena scope, offsets it and points the scope at that ring's own
        /// spawn points.
        /// </summary>
        private static void BuildArena(Transform rootScope, GameObject ring, int arenaIndex, int columns)
        {
            GameObject arena = new($"Arena_{arenaIndex:00}");

            // Under the root scope for tidiness only; the parentReference set below is what
            // actually binds the two. VContainer does not walk the transform hierarchy.
            arena.transform.SetParent(rootScope, false);
            arena.transform.localPosition = new Vector3(
                arenaIndex % columns * ARENA_SPACING,
                arenaIndex / columns * ARENA_SPACING,
                0f);

            ring.transform.SetParent(arena.transform, false);

            ArenaLifetimeScope scope = arena.AddComponent<ArenaLifetimeScope>();
            BoxerSpawnPoints spawnPoints = ring.GetComponentInChildren<BoxerSpawnPoints>(true);

            if (spawnPoints == null)
            {
                Debug.LogError($"[PoRumble] {arena.name} has no BoxerSpawnPoints under its ring.");
                return;
            }

            // Assigned through SerializedObject rather than a setter: the field is private and
            // serialized, which is what the project's encapsulation rules ask for, and this is
            // the one caller that has any business writing it.
            SerializedObject serialized = new(scope);
            serialized.FindProperty("_spawnPoints").objectReferenceValue = spawnPoints;

            // The parent link. VContainer resolves it by type name off this serialized field
            // and throws if it cannot find a built scope of that type - it does not search the
            // transform hierarchy, so an arena without this becomes a root scope of its own
            // and fails to resolve RosterModel the moment it injects its spawn points.
            serialized.FindProperty("parentReference.TypeName").stringValue =
                typeof(GameLifetimeScope).FullName;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif

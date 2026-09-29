using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PoRumble.Views
{
    /// <summary>
    /// The project's only route to the console. Compiled out of release builds, arguments and
    /// all, so a message built with interpolation costs nothing where nobody can read it; the
    /// Editor and development builds keep every line.
    /// </summary>
    internal static class GameLog
    {
        private const string EDITOR = "UNITY_EDITOR";
        private const string DEVELOPMENT = "DEVELOPMENT_BUILD";

        [Conditional(EDITOR), Conditional(DEVELOPMENT)]
        internal static void Info(object message, Object context = null)
        {
            Debug.Log(message, context);
        }

        [Conditional(EDITOR), Conditional(DEVELOPMENT)]
        internal static void Warning(object message, Object context = null)
        {
            Debug.LogWarning(message, context);
        }

        [Conditional(EDITOR), Conditional(DEVELOPMENT)]
        internal static void Error(object message, Object context = null)
        {
            Debug.LogError(message, context);
        }
    }
}

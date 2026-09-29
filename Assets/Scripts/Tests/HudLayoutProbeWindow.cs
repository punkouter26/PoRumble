using UnityEditor;

namespace PoRumble.Tests
{
    /// <summary>
    /// An empty editor window whose only job is to own a UI Toolkit panel, so the HUD layout
    /// tests have somewhere to lay the real UXML and stylesheet out. UI Toolkit has no public
    /// "compute layout" call; a live panel is the only thing that runs the layout engine.
    ///
    /// Its own file because Unity maps a ScriptableObject type to its script asset by file name,
    /// and a window created from a class in someone else's file logs a missing-script warning.
    /// </summary>
    public sealed class HudLayoutProbeWindow : EditorWindow
    {
    }
}

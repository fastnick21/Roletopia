using UnityEngine;

namespace Roletopia.AmongUsPlugin;

/// <summary>
/// Small always-visible status badge so players can immediately verify that
/// Roletopia is running. This also exposes the current lifecycle state while
/// the gameplay integration is active.
/// </summary>
internal sealed class RoletopiaOverlay : MonoBehaviour
{
    private static string _status = "Loaded";

    public RoletopiaOverlay(IntPtr pointer) : base(pointer)
    {
    }

    internal static void SetStatus(string status)
    {
        _status = string.IsNullOrWhiteSpace(status) ? "Loaded" : status;
    }

    private void OnGUI()
    {
        var previousColor = GUI.color;
        var previousBackgroundColor = GUI.backgroundColor;

        GUI.color = Color.white;
        GUI.backgroundColor = new Color(0f, 0f, 0f, 0.72f);
        GUI.Box(new Rect(12f, 12f, 355f, 34f), string.Empty);
        GUI.Label(new Rect(22f, 19f, 335f, 24f), $"Roletopia v{RoletopiaPlugin.PluginVersion}  •  {_status}");

        GUI.color = previousColor;
        GUI.backgroundColor = previousBackgroundColor;
    }
}

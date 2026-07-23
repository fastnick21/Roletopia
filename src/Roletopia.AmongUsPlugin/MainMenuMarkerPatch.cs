using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace Roletopia.AmongUsPlugin;

[HarmonyPatch]
internal static class MainMenuMarkerPatch
{
    private const string Marker = "Roletopia 0.1.0-alpha loaded";
    private static ManualLogSource? _log;

    public static void Initialize(ManualLogSource log) => _log = log;

    private static MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("MainMenuManager"), "Start");

    private static void Postfix()
    {
        try
        {
            if (TryMarkTextType("TMPro.TextMeshProUGUI") ||
                TryMarkTextType("TMPro.TextMeshPro"))
            {
                return;
            }

            _log?.LogWarning("Global main-menu text search found no suitable visible label.");
        }
        catch (Exception exception)
        {
            _log?.LogError($"Global main-menu marker failed: {exception}");
        }
    }

    private static bool TryMarkTextType(string textTypeName)
    {
        var textType = AccessTools.TypeByName(textTypeName);
        var resourcesType = AccessTools.TypeByName("UnityEngine.Resources");
        if (textType == null || resourcesType == null) return false;

        var findMethod = resourcesType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
            {
                if (method.Name != "FindObjectsOfTypeAll") return false;
                var parameters = method.GetParameters();
                return !method.IsGenericMethod && parameters.Length == 1 && parameters[0].ParameterType == typeof(Type);
            });
        if (findMethod == null) return false;

        if (findMethod.Invoke(null, new object[] { textType }) is not IEnumerable objects) return false;

        object? fallback = null;
        foreach (var item in objects)
        {
            if (item == null) continue;
            var current = ReadText(item);
            if (current == null || current.Contains("Roletopia", StringComparison.OrdinalIgnoreCase)) continue;

            fallback ??= item;
            if (!LooksLikeMenuLabel(current)) continue;

            WriteMarker(item, current);
            return true;
        }

        if (fallback != null)
        {
            var current = ReadText(fallback) ?? string.Empty;
            WriteMarker(fallback, current);
            return true;
        }

        return false;
    }

    private static string? ReadText(object target)
    {
        var property = target.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? target.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanRead != true) return null;

        try { return property.GetValue(target) as string; }
        catch { return null; }
    }

    private static void WriteMarker(object target, string current)
    {
        var property = target.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? target.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite != true || property.PropertyType != typeof(string))
            throw new InvalidOperationException("Selected menu text component is not writable.");

        var replacement = string.IsNullOrWhiteSpace(current)
            ? Marker
            : current + "\n<size=55%>" + Marker + "</size>";
        property.SetValue(target, replacement);
        _log?.LogInfo($"Added global Roletopia marker to {target.GetType().FullName}.");
    }

    private static bool LooksLikeMenuLabel(string text)
    {
        var normalized = text.Trim();
        return normalized.Equals("Credits", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Settings", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("How To Play", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Play", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("version", StringComparison.OrdinalIgnoreCase);
    }
}

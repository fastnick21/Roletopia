using System.Collections;
using System.Reflection;
using HarmonyLib;
using Roletopia.RoleSystem;

namespace Roletopia.AmongUsPlugin;

/// <summary>
/// Reliable host sidebar fallback. It automatically shows in a hosted lobby by
/// reusing an already-rendered TMP label. F7 can still hide/show the panel.
/// The patch listens to both GameStartManager and HudManager so it keeps working
/// when an Among Us update changes which lobby Update path is active.
/// </summary>
[HarmonyPatch]
internal static class HostSidebarHotkeyFallbackPatch
{
    private static readonly RoleType[] Roles = Enum.GetValues(typeof(RoleType)).Cast<RoleType>().ToArray();

    private static Type? _inputType;
    private static Type? _keyCodeType;
    private static MethodInfo? _getKeyDown;
    private static object? _f7Key;
    private static bool _inputReady;

    // Start visible so hosts get a working settings panel without having to know
    // about the emergency hotkey. F7 remains available as a manual toggle.
    private static bool _visible = true;
    private static object? _targetText;
    private static string? _originalText;
    private static int _frame;
    private const int SelectedRole = 0;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        var seen = new HashSet<MethodBase>();
        foreach (var typeName in new[] { "GameStartManager", "HudManager" })
        {
            var type = AccessTools.TypeByName(typeName);
            var update = type == null ? null : AccessTools.Method(type, "Update");
            if (update != null && seen.Add(update))
                yield return update;
        }
    }

    private static void Postfix(object __instance)
    {
        if (__instance == null) return;

        var coordinator = RoletopiaGameBridge.Coordinator;
        if (coordinator == null || !coordinator.IsHost)
        {
            HideAndRestore(resetVisibility: true);
            return;
        }

        EnsureInput();
        if (PressedF7())
        {
            _visible = !_visible;
            if (!_visible)
            {
                HideAndRestore(resetVisibility: false);
                return;
            }

            // Force a fresh lookup every time F7 enables the fallback.
            _targetText = null;
            _originalText = null;
        }

        if (!_visible) return;

        _frame++;
        if (_targetText == null)
        {
            _targetText = FindUsableTmpText(__instance);
            if (_targetText == null) return;
            _originalText = ReadText(_targetText);
        }

        // Keep this cheap: only refresh a few times per second.
        if (_frame % 20 != 1) return;

        var role = Roles[Math.Clamp(SelectedRole, 0, Roles.Length - 1)];
        var panel = coordinator.BuildHostSidebarText(role, 0);
        WriteText(_targetText, panel + "\n\n[F7] Hide / show Roletopia panel");
    }

    private static void EnsureInput()
    {
        if (_inputReady) return;
        _inputReady = true;

        _inputType = AccessTools.TypeByName("UnityEngine.Input");
        _keyCodeType = AccessTools.TypeByName("UnityEngine.KeyCode");
        if (_inputType == null || _keyCodeType == null) return;

        _getKeyDown = _inputType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "GetKeyDown" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == _keyCodeType);

        try { _f7Key = Enum.Parse(_keyCodeType, "F7"); }
        catch { _f7Key = null; }
    }

    private static bool PressedF7()
    {
        if (_getKeyDown == null || _f7Key == null) return false;
        try { return _getKeyDown.Invoke(null, new[] { _f7Key }) is bool down && down; }
        catch { return false; }
    }

    private static object? FindUsableTmpText(object owner)
    {
        var tmpType = ResolveType("TMPro.TextMeshPro", "Unity.TextMeshPro")
            ?? ResolveType("TMPro.TextMeshProUGUI", "Unity.TextMeshPro");
        if (tmpType == null) return null;

        var gameObject = ReadProperty(owner, "gameObject") ?? owner;
        var getComponents = gameObject.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (candidate.Name != "GetComponentsInChildren" || candidate.IsGenericMethod) return false;
                var p = candidate.GetParameters();
                return p.Length == 2 && p[0].ParameterType == typeof(Type) && p[1].ParameterType == typeof(bool);
            });
        if (getComponents == null) return null;

        if (getComponents.Invoke(gameObject, new object[] { tmpType, true }) is not IEnumerable components)
            return null;

        object? fallback = null;
        foreach (var component in components)
        {
            if (component == null) continue;
            fallback ??= component;
            var text = ReadText(component);
            if (!string.IsNullOrWhiteSpace(text) && text.Length >= 3)
                return component;
        }

        return fallback;
    }

    private static Type? ResolveType(string fullName, string assemblyName)
    {
        var type = Type.GetType($"{fullName}, {assemblyName}", false);
        if (type != null) return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = assembly.GetType(fullName, false, false);
                if (type != null) return type;
            }
            catch { }
        }
        return null;
    }

    private static object? ReadProperty(object owner, string name)
    {
        try
        {
            return owner.GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(owner);
        }
        catch { return null; }
    }

    private static string? ReadText(object target)
    {
        try
        {
            var property = target.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? target.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(target) as string;
        }
        catch { return null; }
    }

    private static void WriteText(object target, string text)
    {
        try
        {
            var property = target.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? target.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.CanWrite == true) property.SetValue(target, text);
        }
        catch { }
    }

    private static void HideAndRestore(bool resetVisibility)
    {
        if (_targetText != null && _originalText != null)
            WriteText(_targetText, _originalText);

        _targetText = null;
        _originalText = null;
        if (resetVisibility) _visible = true;
        _frame = 0;
    }
}

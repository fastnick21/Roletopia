using System.Collections;
using System.Reflection;
using HarmonyLib;
using Roletopia.RoleSystem;

namespace Roletopia.AmongUsPlugin;

/// <summary>
/// Emergency/diagnostic host sidebar fallback. Press F7 in a hosted lobby to
/// toggle a Roletopia settings panel using an already-rendered TMP label.
/// This avoids cloning/creating Unity UI and gives us a reliable visible path
/// while the full sidebar creation code is being hardened.
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

    private static bool _visible;
    private static object? _targetText;
    private static string? _originalText;
    private static int _frame;
    private static int _selectedRole;

    private static MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("GameStartManager"), "Update");

    private static void Postfix(object __instance)
    {
        if (__instance == null) return;

        var coordinator = RoletopiaGameBridge.Coordinator;
        if (coordinator == null || !coordinator.IsHost)
        {
            HideAndRestore();
            return;
        }

        EnsureInput();
        if (PressedF7())
        {
            _visible = !_visible;
            if (!_visible)
            {
                HideAndRestore();
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

        // Keep this very cheap: only refresh a few times per second.
        if (_frame % 20 != 1) return;

        var role = Roles[Math.Clamp(_selectedRole, 0, Roles.Length - 1)];
        var panel = coordinator.BuildHostSidebarText(role, 0);
        WriteText(_targetText, panel + "\n\n[F7] Close emergency panel");
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

    private static void HideAndRestore()
    {
        if (_targetText != null && _originalText != null)
            WriteText(_targetText, _originalText);

        _targetText = null;
        _originalText = null;
        _visible = false;
        _frame = 0;
    }
}

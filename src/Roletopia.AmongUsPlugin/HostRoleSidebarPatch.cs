using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Roletopia.RoleSystem;
using Roletopia.Runtime;

namespace Roletopia.AmongUsPlugin;

[HarmonyPatch]
internal static class HostRoleSidebarPatch
{
    private static readonly RoleType[] Roles = Enum.GetValues(typeof(RoleType)).Cast<RoleType>().ToArray();
    private static ManualLogSource? _log;
    private static object? _sidebarObject;
    private static object? _sidebarText;
    private static int _selectedRole;
    private static int _selectedSetting;
    private static bool _visible = true;
    private static int _frame;

    public static void Initialize(ManualLogSource log) => _log = log;

    private static MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("GameStartManager"), "Update");

    private static void Postfix(object __instance)
    {
        var coordinator = RoletopiaGameBridge.Coordinator;
        if (__instance == null || coordinator == null) return;

        try
        {
            if (!coordinator.IsHost)
            {
                SetSidebarActive(false);
                return;
            }

            HandleInput(coordinator);
            if (!_visible)
            {
                SetSidebarActive(false);
                return;
            }

            _frame++;
            if (_sidebarText == null && (_frame == 1 || _frame % 60 == 0))
                TryCreateSidebar(__instance);

            if (_sidebarText == null) return;
            SetSidebarActive(true);

            var role = Roles[Math.Clamp(_selectedRole, 0, Roles.Length - 1)];
            var option = coordinator.Settings.GetRole(role);
            if (option.Settings.Count == 0) _selectedSetting = 0;
            else _selectedSetting = Math.Clamp(_selectedSetting, 0, option.Settings.Count - 1);

            WriteText(_sidebarText, coordinator.BuildHostSidebarText(role, _selectedSetting));
        }
        catch (Exception exception)
        {
            if (_frame % 300 == 0)
                _log?.LogWarning($"Host role sidebar update failed: {exception.Message}");
        }
    }

    private static void HandleInput(RuntimeCoordinator coordinator)
    {
        if (GetKeyDown("F6"))
        {
            _visible = !_visible;
            return;
        }
        if (!_visible) return;

        if (GetKeyDown("UpArrow"))
        {
            _selectedRole = (_selectedRole - 1 + Roles.Length) % Roles.Length;
            _selectedSetting = 0;
        }
        if (GetKeyDown("DownArrow"))
        {
            _selectedRole = (_selectedRole + 1) % Roles.Length;
            _selectedSetting = 0;
        }

        var role = Roles[_selectedRole];
        if (GetKeyDown("LeftArrow")) coordinator.AdjustRoleCount(role, -1);
        if (GetKeyDown("RightArrow")) coordinator.AdjustRoleCount(role, 1);
        if (GetKeyDown("Return") || GetKeyDown("KeypadEnter")) coordinator.ToggleRole(role);

        var option = coordinator.Settings.GetRole(role);
        if (option.Settings.Count == 0) return;

        if (GetKeyDown("LeftBracket"))
            _selectedSetting = (_selectedSetting - 1 + option.Settings.Count) % option.Settings.Count;
        if (GetKeyDown("RightBracket"))
            _selectedSetting = (_selectedSetting + 1) % option.Settings.Count;

        if (GetKeyDown("Minus") || GetKeyDown("KeypadMinus"))
            coordinator.AdjustRoleSetting(role, _selectedSetting, -1);
        if (GetKeyDown("Equals") || GetKeyDown("KeypadPlus"))
            coordinator.AdjustRoleSetting(role, _selectedSetting, 1);
    }

    private static bool GetKeyDown(string keyName)
    {
        var inputType = AccessTools.TypeByName("UnityEngine.Input");
        var keyCodeType = AccessTools.TypeByName("UnityEngine.KeyCode");
        if (inputType == null || keyCodeType == null) return false;

        object key;
        try { key = Enum.Parse(keyCodeType, keyName); }
        catch { return false; }

        var method = inputType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetKeyDown" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == keyCodeType);
        if (method == null) return false;
        try { return method.Invoke(null, new[] { key }) is bool pressed && pressed; }
        catch { return false; }
    }

    private static void TryCreateSidebar(object gameStartManager)
    {
        var tmpType = ResolveType("TMPro.TextMeshPro", "Unity.TextMeshPro")
            ?? ResolveType("TMPro.TextMeshProUGUI", "Unity.TextMeshPro");
        if (tmpType == null) return;

        var source = FindTextComponent(gameStartManager, tmpType);
        if (source == null) return;

        var sourceGameObject = ReadProperty(source, "gameObject");
        if (sourceGameObject == null) return;

        var objectType = AccessTools.TypeByName("UnityEngine.Object");
        if (objectType == null) return;

        var instantiate = objectType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Instantiate" && !m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == objectType);
        if (instantiate == null) return;

        var clone = instantiate.Invoke(null, new[] { sourceGameObject });
        if (clone == null) return;

        TrySetName(clone, "RoletopiaHostSidebar");
        var cloneTransform = ReadProperty(clone, "transform");
        var ownerTransform = ReadProperty(gameStartManager, "transform");
        if (cloneTransform != null && ownerTransform != null)
        {
            var setParent = cloneTransform.GetType().GetMethod("SetParent", new[] { ownerTransform.GetType(), typeof(bool) });
            try { setParent?.Invoke(cloneTransform, new object[] { ownerTransform, false }); } catch { }
            TrySetLocalPosition(cloneTransform, 5.1f, 1.65f, -20f);
            TrySetLocalScale(cloneTransform, 0.55f, 0.55f, 1f);
        }

        var clonedText = FindTextComponent(clone, tmpType);
        if (clonedText == null)
        {
            DestroyObject(clone);
            return;
        }

        TrySetFloat(clonedText, "fontSize", 2.4f);
        TrySetFloat(clonedText, "fontSizeMin", 1.2f);
        TrySetBool(clonedText, "enableAutoSizing", false);
        TrySetBool(clonedText, "enableWordWrapping", false);
        WriteText(clonedText, "ROLETOPIA HOST\nLoading role settings...");

        _sidebarObject = clone;
        _sidebarText = clonedText;
        _log?.LogInfo("Created host-only Roletopia role settings sidebar.");
    }

    private static object? FindTextComponent(object owner, Type componentType)
    {
        var gameObject = ReadProperty(owner, "gameObject") ?? owner;
        var method = gameObject.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (candidate.Name != "GetComponentsInChildren" || candidate.IsGenericMethod) return false;
                var parameters = candidate.GetParameters();
                return parameters.Length == 2 && parameters[0].ParameterType == typeof(Type) && parameters[1].ParameterType == typeof(bool);
            });
        if (method == null) return null;

        if (method.Invoke(gameObject, new object[] { componentType, true }) is not IEnumerable components) return null;
        object? fallback = null;
        foreach (var component in components)
        {
            if (component == null) continue;
            fallback ??= component;
            var text = ReadText(component);
            if (!string.IsNullOrWhiteSpace(text)) return component;
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
        try { return owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(owner); }
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

    private static void SetSidebarActive(bool active)
    {
        if (_sidebarObject == null) return;
        try { _sidebarObject.GetType().GetMethod("SetActive", new[] { typeof(bool) })?.Invoke(_sidebarObject, new object[] { active }); }
        catch { }
    }

    private static void TrySetName(object target, string name)
    {
        try { target.GetType().GetProperty("name")?.SetValue(target, name); } catch { }
    }

    private static void TrySetFloat(object target, string propertyName, float value)
    {
        try
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.CanWrite == true) property.SetValue(target, Convert.ChangeType(value, property.PropertyType));
        }
        catch { }
    }

    private static void TrySetBool(object target, string propertyName, bool value)
    {
        try { target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, value); }
        catch { }
    }

    private static void TrySetLocalPosition(object transform, float x, float y, float z) => TrySetVector3(transform, "localPosition", x, y, z);
    private static void TrySetLocalScale(object transform, float x, float y, float z) => TrySetVector3(transform, "localScale", x, y, z);

    private static void TrySetVector3(object transform, string propertyName, float x, float y, float z)
    {
        try
        {
            var property = transform.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var vectorType = property?.PropertyType;
            if (property?.CanWrite != true || vectorType == null) return;
            var ctor = vectorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) });
            var vector = ctor?.Invoke(new object[] { x, y, z });
            if (vector != null) property.SetValue(transform, vector);
        }
        catch { }
    }

    private static void DestroyObject(object target)
    {
        try
        {
            var objectType = AccessTools.TypeByName("UnityEngine.Object");
            var destroy = objectType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Destroy" && m.GetParameters().Length == 1);
            destroy?.Invoke(null, new[] { target });
        }
        catch { }
    }
}

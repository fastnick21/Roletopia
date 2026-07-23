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
    private static readonly Dictionary<string, object> CachedKeys = new(StringComparer.Ordinal);

    private static ManualLogSource? _log;
    private static object? _sidebarObject;
    private static object? _sidebarText;
    private static Type? _tmpType;
    private static Type? _inputType;
    private static Type? _keyCodeType;
    private static MethodInfo? _getKeyDown;
    private static bool _inputInitialized;
    private static bool _visible = true;
    private static bool _isHost;
    private static bool _dirty = true;
    private static int _selectedRole;
    private static int _selectedSetting;
    private static int _frame;
    private static int _lastCreateAttempt;
    private static int _lastHostCheck;
    private static int _lastTextRefresh;

    public static void Initialize(ManualLogSource log)
    {
        _log = log;
        InitializeInput();
    }

    private static MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("GameStartManager"), "Update");

    private static void Postfix(object __instance)
    {
        if (__instance == null) return;
        var coordinator = RoletopiaGameBridge.Coordinator;
        if (coordinator == null) return;

        _frame++;

        try
        {
            // Host detection uses reflection internally. Do not repeat that work every frame.
            if (_frame == 1 || _frame - _lastHostCheck >= 30)
            {
                _lastHostCheck = _frame;
                _isHost = coordinator.IsHost;
                if (!_isHost)
                {
                    SetSidebarActive(false);
                    return;
                }
            }

            if (!_isHost) return;

            HandleInput(coordinator);

            if (!_visible)
            {
                SetSidebarActive(false);
                return;
            }

            if (_sidebarText == null && (_lastCreateAttempt == 0 || _frame - _lastCreateAttempt >= 120))
            {
                _lastCreateAttempt = _frame;
                TryCreateSidebar(__instance);
            }

            if (_sidebarText == null) return;
            SetSidebarActive(true);

            var role = Roles[Math.Clamp(_selectedRole, 0, Roles.Length - 1)];
            var option = coordinator.Settings.GetRole(role);
            _selectedSetting = option.Settings.Count == 0
                ? 0
                : Math.Clamp(_selectedSetting, 0, option.Settings.Count - 1);

            // Text was previously rebuilt every Update, which caused needless allocations and
            // TMP layout work in the lobby. Refresh only after input or twice per second.
            if (_dirty || _frame - _lastTextRefresh >= 30)
            {
                _lastTextRefresh = _frame;
                _dirty = false;
                WriteText(_sidebarText, coordinator.BuildHostSidebarText(role, _selectedSetting));
            }
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
            _dirty = true;
            return;
        }
        if (!_visible) return;

        var changed = false;

        if (GetKeyDown("UpArrow"))
        {
            _selectedRole = (_selectedRole - 1 + Roles.Length) % Roles.Length;
            _selectedSetting = 0;
            changed = true;
        }
        if (GetKeyDown("DownArrow"))
        {
            _selectedRole = (_selectedRole + 1) % Roles.Length;
            _selectedSetting = 0;
            changed = true;
        }

        var role = Roles[_selectedRole];
        if (GetKeyDown("LeftArrow")) { coordinator.AdjustRoleCount(role, -1); changed = true; }
        if (GetKeyDown("RightArrow")) { coordinator.AdjustRoleCount(role, 1); changed = true; }
        if (GetKeyDown("Return") || GetKeyDown("KeypadEnter")) { coordinator.ToggleRole(role); changed = true; }

        var option = coordinator.Settings.GetRole(role);
        if (option.Settings.Count > 0)
        {
            if (GetKeyDown("LeftBracket"))
            {
                _selectedSetting = (_selectedSetting - 1 + option.Settings.Count) % option.Settings.Count;
                changed = true;
            }
            if (GetKeyDown("RightBracket"))
            {
                _selectedSetting = (_selectedSetting + 1) % option.Settings.Count;
                changed = true;
            }
            if (GetKeyDown("Minus") || GetKeyDown("KeypadMinus"))
            {
                coordinator.AdjustRoleSetting(role, _selectedSetting, -1);
                changed = true;
            }
            if (GetKeyDown("Equals") || GetKeyDown("KeypadPlus"))
            {
                coordinator.AdjustRoleSetting(role, _selectedSetting, 1);
                changed = true;
            }
        }

        if (changed) _dirty = true;
    }

    private static void InitializeInput()
    {
        if (_inputInitialized) return;
        _inputInitialized = true;

        _inputType = AccessTools.TypeByName("UnityEngine.Input");
        _keyCodeType = AccessTools.TypeByName("UnityEngine.KeyCode");
        if (_inputType == null || _keyCodeType == null)
        {
            _log?.LogWarning("Host sidebar input API was not found.");
            return;
        }

        _getKeyDown = _inputType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "GetKeyDown" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == _keyCodeType);

        foreach (var keyName in new[]
        {
            "F6", "UpArrow", "DownArrow", "LeftArrow", "RightArrow", "Return", "KeypadEnter",
            "LeftBracket", "RightBracket", "Minus", "KeypadMinus", "Equals", "KeypadPlus"
        })
        {
            try { CachedKeys[keyName] = Enum.Parse(_keyCodeType, keyName); }
            catch { }
        }

        _log?.LogInfo($"Host sidebar input cache ready; keys={CachedKeys.Count}.");
    }

    private static bool GetKeyDown(string keyName)
    {
        if (!_inputInitialized) InitializeInput();
        if (_getKeyDown == null || !CachedKeys.TryGetValue(keyName, out var key)) return false;
        try { return _getKeyDown.Invoke(null, new[] { key }) is bool pressed && pressed; }
        catch { return false; }
    }

    private static void TryCreateSidebar(object gameStartManager)
    {
        _tmpType ??= ResolveType("TMPro.TextMeshPro", "Unity.TextMeshPro")
            ?? ResolveType("TMPro.TextMeshProUGUI", "Unity.TextMeshPro");
        if (_tmpType == null)
        {
            _log?.LogWarning("Host sidebar: no TextMeshPro type was found.");
            return;
        }

        var source = FindTextComponent(gameStartManager, _tmpType);
        if (source == null)
        {
            _log?.LogWarning("Host sidebar: GameStartManager has no usable TMP text component yet; retrying.");
            return;
        }

        var sourceGameObject = ReadProperty(source, "gameObject");
        var sourceTransform = sourceGameObject == null ? null : ReadProperty(sourceGameObject, "transform");
        if (sourceGameObject == null || sourceTransform == null)
        {
            _log?.LogWarning("Host sidebar: TMP source did not expose a GameObject/Transform.");
            return;
        }

        var objectType = AccessTools.TypeByName("UnityEngine.Object");
        if (objectType == null) return;

        var instantiate = objectType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "Instantiate" &&
                !m.IsGenericMethod &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == objectType);
        if (instantiate == null)
        {
            _log?.LogWarning("Host sidebar: Unity Object.Instantiate(Object) was not found.");
            return;
        }

        object? clone;
        try { clone = instantiate.Invoke(null, new[] { sourceGameObject }); }
        catch (Exception exception)
        {
            _log?.LogWarning($"Host sidebar: cloning source text failed: {exception.Message}");
            return;
        }
        if (clone == null) return;

        TrySetName(clone, "RoletopiaHostSidebar");
        var cloneTransform = ReadProperty(clone, "transform");
        var sourceParent = ReadProperty(sourceTransform, "parent");

        if (cloneTransform != null)
        {
            if (sourceParent != null) TrySetParent(cloneTransform, sourceParent);
            TrySetLocalPosition(cloneTransform, 4.15f, 0.65f, -20f);
            TrySetLocalScale(cloneTransform, 0.42f, 0.42f, 1f);
        }

        var clonedText = FindTextComponent(clone, _tmpType);
        if (clonedText == null)
        {
            DestroyObject(clone);
            _log?.LogWarning("Host sidebar: cloned object did not contain a TMP component.");
            return;
        }

        TrySetFloat(clonedText, "fontSize", 2.1f);
        TrySetFloat(clonedText, "fontSizeMin", 1.0f);
        TrySetBool(clonedText, "enableAutoSizing", false);
        TrySetBool(clonedText, "enableWordWrapping", false);
        WriteText(clonedText, "ROLETOPIA HOST\nLoading role settings...");

        _sidebarObject = clone;
        _sidebarText = clonedText;
        _dirty = true;
        _log?.LogInfo("Created host-only Roletopia role settings sidebar with cached low-lag update path.");
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
            if (!string.IsNullOrWhiteSpace(ReadText(component))) return component;
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

    private static void TrySetParent(object transform, object parent)
    {
        try
        {
            var method = transform.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "SetParent" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(bool));
            method?.Invoke(transform, new[] { parent, (object)false });
        }
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

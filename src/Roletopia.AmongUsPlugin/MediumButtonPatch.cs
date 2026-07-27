using System.Collections;
using System.Reflection;
using HarmonyLib;
using Roletopia.RoleSystem;

namespace Roletopia.AmongUsPlugin;

/// <summary>
/// Gives the local Medium a SEANCE button by reusing Among Us' existing hidden
/// kill-button object. A seance can only begin near a real DeadBody and must be
/// channeled for the configured duration before the Medium receives a limited clue.
/// </summary>
internal static class MediumButtonRuntime
{
    private const float MaxBodyDistance = 2.25f;

    private static object? _currentBody;
    private static string? _currentSpiritId;
    private static string? _channelSpiritId;
    private static DateTimeOffset _channelEndsAt;
    private static DateTimeOffset _localReadyAt;
    private static bool _labelApplied;

    internal static void UpdateHud(object hudManager)
    {
        if (hudManager == null) return;

        var localPlayer = GetLocalPlayer();
        var localId = ReadPlayerId(localPlayer);
        var coordinator = RoletopiaGameBridge.Coordinator;
        var isMedium = localPlayer != null && localId != null && coordinator?.IsRoleAssigned(localId, RoleType.Medium) == true;
        var killButton = ReadMember(hudManager, "KillButton") ?? ReadMember(hudManager, "killButton");
        if (killButton == null) return;

        if (!isMedium)
        {
            ResetState(clearCooldown: true);
            return;
        }

        if (IsPlayerDead(localPlayer))
        {
            ResetChannel();
            SetGameObjectActive(killButton, false);
            return;
        }

        if (coordinator?.CanUseRoleAbilities != true)
        {
            // Meetings pause the Medium's live interaction but do not erase cooldown.
            ResetChannel();
            SetGameObjectActive(killButton, false);
            return;
        }

        SetGameObjectActive(killButton, true);

        if (_channelSpiritId != null)
        {
            _labelApplied = TrySetButtonLabel(killButton, "CHANNELING");
            UpdateChannel(localPlayer, localId!, coordinator, killButton);
            return;
        }

        if (!_labelApplied)
            _labelApplied = TrySetButtonLabel(killButton, "SEANCE");

        FindClosestDeadBody(localPlayer, out _currentBody, out _currentSpiritId);
    }

    internal static bool TryHandleClick(object button)
    {
        var localPlayer = GetLocalPlayer();
        var actorId = ReadPlayerId(localPlayer);
        var coordinator = RoletopiaGameBridge.Coordinator;
        if (localPlayer == null || actorId == null || coordinator?.IsRoleAssigned(actorId, RoleType.Medium) != true)
            return false;

        // Never allow a Medium click to fall through into vanilla kill logic.
        if (!coordinator.CanUseRoleAbilities || IsPlayerDead(localPlayer))
            return true;

        if (_channelSpiritId != null)
        {
            ShowNotice("A seance is already in progress.");
            return true;
        }

        if (DateTimeOffset.UtcNow < _localReadyAt)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((_localReadyAt - DateTimeOffset.UtcNow).TotalSeconds));
            ShowNotice($"SEANCE cooling down: {seconds}s");
            return true;
        }

        if (_currentBody == null || string.IsNullOrWhiteSpace(_currentSpiritId))
        {
            ShowNotice("No spirit is close enough to contact.");
            return true;
        }

        var duration = coordinator.Settings.GetRole(RoleType.Medium).GetSetting("duration")?.Value ?? 8d;
        duration = Math.Max(0.5d, duration);
        _channelSpiritId = _currentSpiritId;
        _channelEndsAt = DateTimeOffset.UtcNow.AddSeconds(duration);
        _labelApplied = false;
        ShowNotice($"Seance started. Stay near the body for {duration:0.#}s.");
        return true;
    }

    private static void UpdateChannel(object localPlayer, string actorId, Roletopia.Runtime.RuntimeCoordinator coordinator, object button)
    {
        FindClosestDeadBody(localPlayer, out var nearbyBody, out var nearbySpiritId);
        if (nearbyBody == null || !string.Equals(nearbySpiritId, _channelSpiritId, StringComparison.Ordinal))
        {
            ShowNotice("Seance cancelled: you moved too far from the body.");
            ResetChannel();
            _labelApplied = false;
            return;
        }

        if (DateTimeOffset.UtcNow < _channelEndsAt)
            return;

        var spiritId = _channelSpiritId!;
        var result = coordinator.UseRoleAbility(actorId, spiritId, DateTimeOffset.UtcNow);
        if (result.Succeeded)
        {
            var cooldown = coordinator.Settings.GetRole(RoleType.Medium).GetSetting("cooldown")?.Value ?? 20d;
            _localReadyAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0d, cooldown));
            ShowNotice("MEDIUM: " + result.Message);
        }
        else
        {
            ShowNotice("SEANCE failed: " + result.Message);
        }

        ResetChannel();
        _labelApplied = false;
        TrySetButtonLabel(button, "SEANCE");
    }

    private static void ResetState(bool clearCooldown)
    {
        _currentBody = null;
        _currentSpiritId = null;
        _labelApplied = false;
        ResetChannel();
        if (clearCooldown) _localReadyAt = DateTimeOffset.MinValue;
    }

    private static void ResetChannel()
    {
        _channelSpiritId = null;
        _channelEndsAt = DateTimeOffset.MinValue;
    }

    private static bool FindClosestDeadBody(object localPlayer, out object? bestBody, out string? bestSpiritId)
    {
        bestBody = null;
        bestSpiritId = null;
        if (!TryGetPosition(localPlayer, out var localX, out var localY)) return false;

        var deadBodyType = AccessTools.TypeByName("DeadBody");
        if (deadBodyType == null) return false;

        var bestDistanceSquared = MaxBodyDistance * MaxBodyDistance;
        foreach (var body in FindObjectsOfType(deadBodyType))
        {
            if (body == null) continue;
            var spiritId = ReadBodyPlayerId(body);
            if (string.IsNullOrWhiteSpace(spiritId)) continue;
            if (!TryGetPosition(body, out var x, out var y)) continue;

            var dx = x - localX;
            var dy = y - localY;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared > bestDistanceSquared) continue;

            bestDistanceSquared = distanceSquared;
            bestBody = body;
            bestSpiritId = spiritId;
        }

        return bestBody != null;
    }

    private static IEnumerable FindObjectsOfType(Type componentType)
    {
        try
        {
            var objectType = AccessTools.TypeByName("UnityEngine.Object");
            if (objectType != null)
            {
                var method = objectType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(candidate =>
                    {
                        if (candidate.Name != "FindObjectsOfType" || candidate.IsGenericMethod) return false;
                        var parameters = candidate.GetParameters();
                        return parameters.Length >= 1 && parameters[0].ParameterType == typeof(Type);
                    });

                if (method != null)
                {
                    var parameters = method.GetParameters();
                    var args = new object?[parameters.Length];
                    args[0] = componentType;
                    for (var i = 1; i < parameters.Length; i++)
                        args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : Activator.CreateInstance(parameters[i].ParameterType);

                    if (method.Invoke(null, args) is IEnumerable objects)
                        return objects;
                }
            }

            var resourcesType = AccessTools.TypeByName("UnityEngine.Resources");
            var fallback = resourcesType == null ? null : AccessTools.Method(resourcesType, "FindObjectsOfTypeAll", new[] { typeof(Type) });
            if (fallback?.Invoke(null, new object[] { componentType }) is IEnumerable fallbackObjects)
                return fallbackObjects;
        }
        catch
        {
        }

        return Array.Empty<object>();
    }

    private static string? ReadBodyPlayerId(object body)
    {
        foreach (var name in new[] { "ParentId", "parentId", "PlayerId", "playerId" })
        {
            var value = ReadMember(body, name);
            if (value != null) return value.ToString();
        }
        return null;
    }

    private static bool TryGetPosition(object value, out float x, out float y)
    {
        x = 0;
        y = 0;
        try
        {
            var transform = ReadMember(value, "transform") ?? ReadMember(ReadMember(value, "gameObject"), "transform");
            var position = ReadMember(transform, "position");
            if (position == null) return false;
            x = Convert.ToSingle(ReadMember(position, "x") ?? 0f);
            y = Convert.ToSingle(ReadMember(position, "y") ?? 0f);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetButtonLabel(object button, string label)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return TrySetButtonLabelRecursive(button, label, flags, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
    }

    private static bool TrySetButtonLabelRecursive(object value, string label, BindingFlags flags, HashSet<object> visited, int depth)
    {
        if (depth > 4 || value is string || !visited.Add(value)) return false;

        var type = value.GetType();
        var textProperty = type.GetProperty("text", flags) ?? type.GetProperty("Text", flags);
        if (textProperty?.CanRead == true && textProperty.CanWrite && textProperty.PropertyType == typeof(string))
        {
            try
            {
                var current = textProperty.GetValue(value) as string ?? string.Empty;
                if (current.Contains("kill", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("shoot", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("seance", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("channel", StringComparison.OrdinalIgnoreCase))
                {
                    textProperty.SetValue(value, label);
                    return true;
                }
            }
            catch
            {
            }
        }

        foreach (var member in type.GetFields(flags).Cast<MemberInfo>().Concat(type.GetProperties(flags))
                     .Where(member => member.Name.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                                      member.Name.Contains("label", StringComparison.OrdinalIgnoreCase) ||
                                      member.Name.Contains("button", StringComparison.OrdinalIgnoreCase))
                     .Take(20))
        {
            var child = ReadMember(value, member);
            if (child != null && TrySetButtonLabelRecursive(child, label, flags, visited, depth + 1)) return true;
        }

        return false;
    }

    private static void ShowNotice(string message)
    {
        try
        {
            var hud = GetSingleton("HudManager");
            if (hud == null) return;

            foreach (var notifierName in new[] { "Notifier", "notifier", "Notifications", "notifications" })
            {
                var notifier = ReadMember(hud, notifierName);
                if (notifier == null) continue;
                foreach (var method in notifier.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             .Where(method => method.Name is "AddItem" or "AddMessage" or "AddNotification")
                             .OrderBy(method => method.GetParameters().Length))
                {
                    var args = BuildMessageArguments(method.GetParameters(), message);
                    if (args == null) continue;
                    try
                    {
                        method.Invoke(notifier, args);
                        return;
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static object?[]? BuildMessageArguments(ParameterInfo[] parameters, string message)
    {
        var args = new object?[parameters.Length];
        var usedMessage = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var type = parameter.ParameterType;
            if (!usedMessage && type == typeof(string))
            {
                args[i] = message;
                usedMessage = true;
            }
            else if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
            }
            else if (type == typeof(string))
            {
                args[i] = string.Empty;
            }
            else if (!type.IsValueType)
            {
                args[i] = null;
            }
            else if (type.IsEnum)
            {
                args[i] = Enum.ToObject(type, 0);
            }
            else
            {
                try { args[i] = Activator.CreateInstance(type); }
                catch { return null; }
            }
        }
        return usedMessage ? args : null;
    }

    private static bool IsPlayerDead(object? player)
    {
        if (player == null) return true;
        var data = ReadMember(player, "Data");
        return ReadBool(data, "IsDead") ?? ReadBool(data, "isDead") ?? false;
    }

    private static bool? ReadBool(object? instance, string memberName)
    {
        var value = ReadMember(instance, memberName);
        return value is bool result ? result : null;
    }

    private static object? GetLocalPlayer()
    {
        var type = AccessTools.TypeByName("PlayerControl");
        if (type == null) return null;
        return AccessTools.Property(type, "LocalPlayer")?.GetValue(null)
            ?? AccessTools.Field(type, "LocalPlayer")?.GetValue(null);
    }

    private static string? ReadPlayerId(object? player)
    {
        if (player == null) return null;
        return ReadMember(player, "PlayerId")?.ToString();
    }

    private static object? GetSingleton(string typeName)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null) return null;
        return AccessTools.Property(type, "Instance")?.GetValue(null)
            ?? AccessTools.Field(type, "Instance")?.GetValue(null);
    }

    private static object? ReadMember(object? instance, string memberName)
    {
        if (instance == null) return null;
        var type = instance.GetType();
        try
        {
            return type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance)
                ?? type.GetField(memberName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static object? ReadMember(object instance, MemberInfo member)
    {
        try
        {
            return member switch
            {
                FieldInfo field => field.GetValue(instance),
                PropertyInfo property when property.CanRead && property.GetIndexParameters().Length == 0 => property.GetValue(instance),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static void SetGameObjectActive(object component, bool active)
    {
        try
        {
            var gameObject = ReadMember(component, "gameObject") ?? component;
            gameObject.GetType().GetMethod("SetActive", new[] { typeof(bool) })?.Invoke(gameObject, new object[] { active });
        }
        catch
        {
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

[HarmonyPatch]
internal static class MediumHudUpdatePatch
{
    private static MethodBase? TargetMethod() => AccessTools.Method(AccessTools.TypeByName("HudManager"), "Update");

    private static void Postfix(object __instance)
    {
        try { MediumButtonRuntime.UpdateHud(__instance); }
        catch { }
    }
}

[HarmonyPatch]
internal static class MediumKillButtonClickPatch
{
    private static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("KillButtonManager") ?? AccessTools.TypeByName("KillButton");
        return type == null ? null : AccessTools.Method(type, "DoClick");
    }

    private static bool Prefix(object __instance)
    {
        try
        {
            return !MediumButtonRuntime.TryHandleClick(__instance);
        }
        catch
        {
            return true;
        }
    }
}

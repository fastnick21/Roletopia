using System.Collections;
using System.Reflection;
using HarmonyLib;
using Roletopia.RoleSystem;

namespace Roletopia.AmongUsPlugin;

/// <summary>
/// Reuses Among Us' own kill-button object for the local Sheriff. This avoids
/// creating new IL2CPP UI objects and gives the Sheriff a real clickable SHOOT
/// button while we keep the rest of the role HUD independent from the sidebar.
/// </summary>
internal static class SheriffButtonRuntime
{
    private const float MaxTargetDistance = 2.5f;
    private static object? _currentTarget;
    private static bool _labelApplied;

    internal static void UpdateHud(object hudManager)
    {
        if (hudManager == null) return;

        var localPlayer = GetSingleton("PlayerControl");
        var localId = ReadPlayerId(localPlayer);
        var isSheriff = localPlayer != null && localId != null && IsAssignedSheriff(localId);

        var killButton = ReadMember(hudManager, "KillButton") ?? ReadMember(hudManager, "killButton");
        if (killButton == null) return;

        if (!isSheriff || IsPlayerDead(localPlayer))
        {
            _currentTarget = null;
            return;
        }

        SetGameObjectActive(killButton, true);
        if (!_labelApplied)
            _labelApplied = TrySetShootLabel(killButton);

        _currentTarget = FindClosestLivingTarget(localPlayer!);
        TrySetButtonTarget(killButton, _currentTarget);
    }

    internal static bool TryHandleClick(object button)
    {
        var localPlayer = GetSingleton("PlayerControl");
        var actorId = ReadPlayerId(localPlayer);
        if (localPlayer == null || actorId == null || !IsAssignedSheriff(actorId))
            return false;

        // This is the Sheriff's button, so consume the vanilla click even when
        // there is no target. That prevents a crewmate Sheriff from accidentally
        // entering vanilla impostor kill code.
        if (_currentTarget == null || IsPlayerDead(_currentTarget))
            return true;

        var targetId = ReadPlayerId(_currentTarget);
        if (targetId == null) return true;

        var coordinator = RoletopiaGameBridge.Coordinator;
        if (coordinator == null) return true;

        var result = coordinator.UseRoleAbility(actorId, targetId, DateTimeOffset.UtcNow);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.EliminatedPlayerId))
            return true;

        var victim = string.Equals(result.EliminatedPlayerId, actorId, StringComparison.Ordinal)
            ? localPlayer
            : FindPlayer(result.EliminatedPlayerId);

        if (victim != null)
            TryApplyMurder(localPlayer, victim);

        _currentTarget = null;
        return true;
    }

    private static bool IsAssignedSheriff(string playerId)
    {
        try
        {
            var adapterField = typeof(RoletopiaGameBridge).GetField("_adapter", BindingFlags.Static | BindingFlags.NonPublic);
            var adapter = adapterField?.GetValue(null);
            if (adapter == null) return false;

            var rolesField = adapter.GetType().GetField("_assignedRoles", BindingFlags.Instance | BindingFlags.NonPublic);
            if (rolesField?.GetValue(adapter) is not IDictionary roles) return false;
            return roles.Contains(playerId) && roles[playerId] is RoleType role && role == RoleType.Sheriff;
        }
        catch
        {
            return false;
        }
    }

    private static object? FindClosestLivingTarget(object localPlayer)
    {
        var playerType = AccessTools.TypeByName("PlayerControl");
        var allPlayers = playerType == null ? null : AccessTools.Property(playerType, "AllPlayerControls")?.GetValue(null);
        if (allPlayers is not IEnumerable players) return null;

        if (!TryGetPosition(localPlayer, out var localX, out var localY)) return null;

        object? best = null;
        var bestDistanceSquared = MaxTargetDistance * MaxTargetDistance;
        foreach (var player in players)
        {
            if (player == null || ReferenceEquals(player, localPlayer) || IsPlayerDead(player)) continue;
            if (!TryGetPosition(player, out var x, out var y)) continue;

            var dx = x - localX;
            var dy = y - localY;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared > bestDistanceSquared) continue;

            bestDistanceSquared = distanceSquared;
            best = player;
        }

        return best;
    }

    private static bool IsPlayerDead(object? player)
    {
        if (player == null) return true;
        var data = ReadMember(player, "Data");
        return ReadBool(data, "IsDead") ?? ReadBool(data, "isDead") ?? false;
    }

    private static bool TryGetPosition(object player, out float x, out float y)
    {
        x = 0;
        y = 0;
        try
        {
            var transform = ReadMember(player, "transform") ?? ReadMember(ReadMember(player, "gameObject"), "transform");
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

    private static void TrySetButtonTarget(object button, object? target)
    {
        try
        {
            var methods = button.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name == "SetTarget" && method.GetParameters().Length == 1);
            foreach (var method in methods)
            {
                var parameter = method.GetParameters()[0].ParameterType;
                if (target == null || parameter.IsInstanceOfType(target))
                {
                    method.Invoke(button, new[] { target });
                    return;
                }
            }
        }
        catch { }
    }

    private static bool TrySetShootLabel(object button)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return TrySetShootLabelRecursive(button, flags, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
    }

    private static bool TrySetShootLabelRecursive(object value, BindingFlags flags, HashSet<object> visited, int depth)
    {
        if (depth > 4 || value is string || !visited.Add(value)) return false;

        var type = value.GetType();
        var textProperty = type.GetProperty("text", flags) ?? type.GetProperty("Text", flags);
        if (textProperty?.CanRead == true && textProperty.CanWrite && textProperty.PropertyType == typeof(string))
        {
            try
            {
                var current = textProperty.GetValue(value) as string ?? string.Empty;
                if (current.Contains("kill", StringComparison.OrdinalIgnoreCase) || current.Contains("shoot", StringComparison.OrdinalIgnoreCase))
                {
                    textProperty.SetValue(value, "SHOOT");
                    return true;
                }
            }
            catch { }
        }

        foreach (var member in type.GetFields(flags).Cast<MemberInfo>().Concat(type.GetProperties(flags))
                     .Where(member => member.Name.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                                      member.Name.Contains("label", StringComparison.OrdinalIgnoreCase) ||
                                      member.Name.Contains("button", StringComparison.OrdinalIgnoreCase))
                     .Take(20))
        {
            var child = ReadMember(value, member);
            if (child != null && TrySetShootLabelRecursive(child, flags, visited, depth + 1)) return true;
        }

        return false;
    }

    private static void SetGameObjectActive(object component, bool active)
    {
        try
        {
            var gameObject = ReadMember(component, "gameObject") ?? component;
            gameObject.GetType().GetMethod("SetActive", new[] { typeof(bool) })?.Invoke(gameObject, new object[] { active });
        }
        catch { }
    }

    private static bool TryApplyMurder(object killer, object victim)
    {
        // Prefer the RPC path so every client sees the same death. Fall back to
        // the local MurderPlayer/Die methods for compatibility with game builds
        // whose generated IL2CPP signatures differ.
        if (TryInvokePlayerAction(killer, "RpcMurderPlayer", victim)) return true;
        if (TryInvokePlayerAction(killer, "MurderPlayer", victim)) return true;
        return TryInvokeVictimDeath(victim);
    }

    private static bool TryInvokePlayerAction(object actor, string methodName, object victim)
    {
        try
        {
            foreach (var method in actor.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(method => method.Name == methodName))
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0 || !parameters[0].ParameterType.IsInstanceOfType(victim)) continue;

                var args = BuildArguments(parameters, victim);
                if (args == null) continue;
                method.Invoke(actor, args);
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool TryInvokeVictimDeath(object victim)
    {
        try
        {
            foreach (var method in victim.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(method => method.Name == "Die"))
            {
                var args = BuildArguments(method.GetParameters(), null);
                if (args == null) continue;
                method.Invoke(victim, args);
                return true;
            }
        }
        catch { }
        return false;
    }

    private static object?[]? BuildArguments(ParameterInfo[] parameters, object? firstArgument)
    {
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i == 0 && firstArgument != null)
            {
                args[i] = firstArgument;
                continue;
            }

            var parameter = parameters[i];
            if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
                continue;
            }

            var type = parameter.ParameterType;
            if (!type.IsValueType)
            {
                args[i] = null;
                continue;
            }
            if (type.IsEnum)
            {
                args[i] = Enum.ToObject(type, 0);
                continue;
            }

            try { args[i] = Activator.CreateInstance(type); }
            catch { return null; }
        }
        return args;
    }

    private static object? FindPlayer(string playerId)
    {
        var playerType = AccessTools.TypeByName("PlayerControl");
        var allPlayers = playerType == null ? null : AccessTools.Property(playerType, "AllPlayerControls")?.GetValue(null);
        if (allPlayers is not IEnumerable players) return null;
        foreach (var player in players)
            if (player != null && string.Equals(ReadPlayerId(player), playerId, StringComparison.Ordinal)) return player;
        return null;
    }

    private static object? GetSingleton(string typeName)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null) return null;
        return AccessTools.Property(type, "LocalPlayer")?.GetValue(null)
            ?? AccessTools.Property(type, "Instance")?.GetValue(null)
            ?? AccessTools.Field(type, "LocalPlayer")?.GetValue(null)
            ?? AccessTools.Field(type, "Instance")?.GetValue(null);
    }

    private static string? ReadPlayerId(object? player)
    {
        if (player == null) return null;
        return ReadMember(player, "PlayerId")?.ToString();
    }

    private static bool? ReadBool(object? instance, string memberName)
    {
        var value = ReadMember(instance, memberName);
        return value is bool result ? result : null;
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
        catch { return null; }
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
        catch { return null; }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

[HarmonyPatch]
internal static class SheriffHudUpdatePatch
{
    private static MethodBase? TargetMethod() => AccessTools.Method(AccessTools.TypeByName("HudManager"), "Update");

    private static void Postfix(object __instance)
    {
        try { SheriffButtonRuntime.UpdateHud(__instance); }
        catch { }
    }
}

[HarmonyPatch]
internal static class SheriffKillButtonClickPatch
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
            // Return false only when the click belonged to a Sheriff; otherwise
            // preserve the normal Among Us kill-button behavior unchanged.
            return !SheriffButtonRuntime.TryHandleClick(__instance);
        }
        catch
        {
            return true;
        }
    }
}

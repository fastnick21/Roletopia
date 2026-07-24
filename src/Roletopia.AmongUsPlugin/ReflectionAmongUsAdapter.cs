using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Roletopia.CoreEngine;
using Roletopia.RoleSystem;
using Roletopia.Runtime;

namespace Roletopia.AmongUsPlugin;

internal sealed class ReflectionAmongUsAdapter : IAmongUsRuntimeAdapter
{
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, RoleType> _assignedRoles = new(StringComparer.Ordinal);

    public ReflectionAmongUsAdapter(ManualLogSource log) => _log = log;

    public bool IsHost
    {
        get
        {
            var client = GetSingleton("AmongUsClient");
            return ReadBool(client, "AmHost") ?? ReadBool(client, "amHost") ?? false;
        }
    }

    public IReadOnlyCollection<string> ConnectedPlayerIds
    {
        get
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var playerControlType = AccessTools.TypeByName("PlayerControl");
            var allPlayerControls = playerControlType == null ? null : AccessTools.Property(playerControlType, "AllPlayerControls")?.GetValue(null);

            if (allPlayerControls is IEnumerable players)
            {
                foreach (var player in players)
                {
                    var id = ReadPlayerId(player);
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                }
            }

            return ids.ToArray();
        }
    }

    public TeamType GetPlayerTeam(string playerId)
    {
        var player = FindPlayer(playerId);
        if (player == null) return TeamType.Crewmate;

        try
        {
            var data = ReadMember(player, "Data");
            var role = ReadMember(data, "Role");
            var isImpostor = ReadBool(role, "IsImpostor")
                ?? ReadBool(role, "isImpostor")
                ?? ReadBool(data, "IsImpostor")
                ?? ReadBool(data, "isImpostor")
                ?? false;
            return isImpostor ? TeamType.Impostor : TeamType.Crewmate;
        }
        catch (Exception exception)
        {
            _log.LogDebug($"Could not resolve base team for player {playerId}: {exception.Message}");
            return TeamType.Crewmate;
        }
    }

    public bool IsRoleAssigned(string playerId, RoleType role) =>
        _assignedRoles.TryGetValue(playerId, out var assigned) && assigned == role;

    public void ShowHostMessage(string message)
    {
        _log.LogMessage(message);
        TrySetHudNotice(message);
    }

    public void AssignRole(string playerId, RoleType role)
    {
        _assignedRoles[playerId] = role;
        _log.LogInfo($"Assigned {role} to player {playerId}.");

        if (role == RoleType.Sheriff)
            TryMarkSheriffPlayer(playerId);
    }

    public void ResetRoleAssignments()
    {
        _assignedRoles.Clear();
        _log.LogDebug("Reset local Roletopia role assignments.");
    }

    public void ClearRoletopiaHud()
    {
        ResetRoleAssignments();
        _log.LogDebug("Cleared Roletopia HUD state.");
    }

    public void SetRoletopiaHudVisible(bool visible) =>
        _log.LogDebug($"Roletopia HUD visible: {visible}");

    public void BroadcastSettings(HostModSettings settings) =>
        _log.LogDebug($"Broadcast settings requested. Enabled={settings.RoletopiaEnabled}, role slots={settings.BuildRolePool().Count}.");

    public void ApplyWinResult(WinResult result)
    {
        if (result == null || !result.HasWinner || !IsHost) return;

        try
        {
            var manager = GetSingleton("GameManager") ?? GetSingleton("ShipStatus");
            if (manager == null)
            {
                _log.LogWarning("Could not end the live Among Us match: no GameManager or ShipStatus singleton was found.");
                return;
            }

            var methods = manager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name is "RpcEndGame" or "EndGame")
                .OrderBy(method => method.Name == "RpcEndGame" ? 0 : 1)
                .ToArray();

            foreach (var method in methods)
            {
                var args = BuildEndGameArguments(method.GetParameters(), result);
                if (args == null) continue;

                method.Invoke(manager, args);
                _log.LogInfo($"Applied Roletopia win to Among Us through {manager.GetType().Name}.{method.Name}: {result.WinningTeam}/{result.Reason}.");
                return;
            }

            _log.LogWarning("Could not end the live Among Us match: no compatible end-game method signature was found.");
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Could not apply Roletopia win to Among Us: {exception.Message}");
        }
    }

    private static object?[]? BuildEndGameArguments(ParameterInfo[] parameters, WinResult result)
    {
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var type = parameter.ParameterType;

            if (type.IsEnum)
            {
                args[i] = ResolveGameOverReason(type, result);
                if (args[i] == null) return null;
                continue;
            }

            if (type == typeof(bool))
            {
                args[i] = false;
                continue;
            }

            if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
                continue;
            }

            if (!type.IsValueType)
            {
                args[i] = null;
                continue;
            }

            try { args[i] = Activator.CreateInstance(type); }
            catch { return null; }
        }

        return args;
    }

    private static object? ResolveGameOverReason(Type enumType, WinResult result)
    {
        var preferredNames = result.WinningTeam switch
        {
            TeamType.Crewmate when result.Reason == WinReason.TasksCompleted => new[] { "HumansByTask", "CrewmatesByTask", "HumansByVote", "CrewmatesByVote" },
            TeamType.Crewmate => new[] { "HumansByVote", "CrewmatesByVote", "HumansByTask", "CrewmatesByTask" },
            TeamType.Impostor => new[] { "ImpostorByKill", "ImpostorsByKill", "ImpostorByVote", "ImpostorsByVote" },
            _ => Array.Empty<string>()
        };

        foreach (var name in preferredNames)
        {
            if (Enum.GetNames(enumType).Any(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return Enum.Parse(enumType, name, ignoreCase: true);
        }

        var values = Enum.GetValues(enumType);
        return values.Length > 0 ? values.GetValue(0) : null;
    }

    private object? FindPlayer(string playerId)
    {
        try
        {
            var playerControlType = AccessTools.TypeByName("PlayerControl");
            var allPlayerControls = playerControlType == null ? null : AccessTools.Property(playerControlType, "AllPlayerControls")?.GetValue(null);
            if (allPlayerControls is not IEnumerable players) return null;

            foreach (var player in players)
            {
                if (string.Equals(ReadPlayerId(player), playerId, StringComparison.Ordinal)) return player;
            }
        }
        catch (Exception exception)
        {
            _log.LogDebug($"Could not locate PlayerControl {playerId}: {exception.Message}");
        }

        return null;
    }

    private void TryMarkSheriffPlayer(string playerId)
    {
        try
        {
            var local = GetLocalPlayer();
            var playerControlType = AccessTools.TypeByName("PlayerControl");
            var allPlayerControls = playerControlType == null ? null : AccessTools.Property(playerControlType, "AllPlayerControls")?.GetValue(null);

            if (allPlayerControls is not IEnumerable players) return;

            foreach (var player in players)
            {
                if (!string.Equals(ReadPlayerId(player), playerId, StringComparison.Ordinal)) continue;

                var isLocal = local != null && ReferenceEquals(player, local);
                _log.LogInfo($"Sheriff assignment matched PlayerControl {playerId}; local={isLocal}.");

                var data = ReadMember(player, "Data");
                var playerName = data == null ? null : ReadString(data, "PlayerName");
                if (isLocal)
                {
                    ShowHostMessage(string.IsNullOrWhiteSpace(playerName)
                        ? "Your Roletopia role: SHERIFF"
                        : $"{playerName}: Your Roletopia role is SHERIFF");
                }

                TryAppendNameMarker(player, " [Sheriff]");
                return;
            }
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Could not apply Sheriff player marker: {exception.Message}");
        }
    }

    private void TryAppendNameMarker(object player, string suffix)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var memberName in new[] { "nameText", "NameText", "cosmetics", "Cosmetics" })
        {
            var value = ReadMember(player, memberName);
            if (value == null) continue;

            if (TryAppendTextRecursive(value, suffix, flags, new HashSet<object>(ReferenceEqualityComparer.Instance), 0))
            {
                _log.LogInfo($"Added Sheriff name marker through PlayerControl.{memberName}.");
                return;
            }
        }
    }

    private static bool TryAppendTextRecursive(object value, string suffix, BindingFlags flags, HashSet<object> visited, int depth)
    {
        if (depth > 3 || value is string || !visited.Add(value)) return false;

        var type = value.GetType();
        var textProperty = type.GetProperty("text", flags) ?? type.GetProperty("Text", flags);
        if (textProperty?.CanRead == true && textProperty.CanWrite && textProperty.PropertyType == typeof(string))
        {
            try
            {
                var current = textProperty.GetValue(value) as string ?? string.Empty;
                if (!current.Contains("Sheriff", StringComparison.OrdinalIgnoreCase))
                    textProperty.SetValue(value, current + suffix);
                return true;
            }
            catch { }
        }

        foreach (var member in type.GetFields(flags).Cast<MemberInfo>().Concat(type.GetProperties(flags))
                     .Where(member => member.Name.Contains("text", StringComparison.OrdinalIgnoreCase) || member.Name.Contains("name", StringComparison.OrdinalIgnoreCase))
                     .Take(12))
        {
            var child = member switch
            {
                FieldInfo field => SafeRead(() => field.GetValue(value)),
                PropertyInfo property when property.GetIndexParameters().Length == 0 && property.CanRead => SafeRead(() => property.GetValue(value)),
                _ => null
            };
            if (child != null && TryAppendTextRecursive(child, suffix, flags, visited, depth + 1)) return true;
        }

        return false;
    }

    private static object? SafeRead(Func<object?> getter)
    {
        try { return getter(); }
        catch { return null; }
    }

    private static object? GetLocalPlayer()
    {
        var type = AccessTools.TypeByName("PlayerControl");
        if (type == null) return null;

        return AccessTools.Property(type, "LocalPlayer")?.GetValue(null)
            ?? AccessTools.Field(type, "LocalPlayer")?.GetValue(null)
            ?? GetSingleton("PlayerControl");
    }

    private static object? GetSingleton(string typeName)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null) return null;

        return AccessTools.Property(type, "Instance")?.GetValue(null)
            ?? AccessTools.Field(type, "Instance")?.GetValue(null);
    }

    private static bool? ReadBool(object? instance, string memberName)
    {
        if (instance == null) return null;
        var type = instance.GetType();
        var property = AccessTools.Property(type, memberName);
        if (property?.GetValue(instance) is bool propertyValue) return propertyValue;
        var field = AccessTools.Field(type, memberName);
        return field?.GetValue(instance) is bool fieldValue ? fieldValue : null;
    }

    private static object? ReadMember(object? instance, string memberName)
    {
        if (instance == null) return null;
        var type = instance.GetType();
        return SafeRead(() => AccessTools.Property(type, memberName)?.GetValue(instance))
            ?? SafeRead(() => AccessTools.Field(type, memberName)?.GetValue(instance));
    }

    private static string? ReadString(object? instance, string memberName) => ReadMember(instance, memberName)?.ToString();

    private static string? ReadPlayerId(object? player)
    {
        if (player == null) return null;
        var type = player.GetType();
        var property = AccessTools.Property(type, "PlayerId");
        var value = property?.GetValue(player) ?? AccessTools.Field(type, "PlayerId")?.GetValue(player);
        return value?.ToString();
    }

    private void TrySetHudNotice(string message)
    {
        try
        {
            var hud = GetSingleton("HudManager");
            if (hud == null) return;

            foreach (var notifierName in new[] { "Notifier", "notifier", "Notifications", "notifications" })
            {
                var notifier = ReadMember(hud, notifierName);
                if (notifier == null) continue;

                var addItem = AccessTools.Method(notifier.GetType(), "AddItem")
                    ?? AccessTools.Method(notifier.GetType(), "AddMessage")
                    ?? AccessTools.Method(notifier.GetType(), "Show");
                if (addItem == null) continue;

                addItem.Invoke(notifier, new object?[] { message });
                _log.LogInfo($"Displayed Roletopia HUD notice through HudManager.{notifierName}.");
                return;
            }
        }
        catch (Exception exception)
        {
            _log.LogDebug($"Could not display an in-game notice yet: {exception.Message}");
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
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

    public void ShowHostMessage(string message)
    {
        _log.LogMessage(message);
        TrySetHudNotice(message);
    }

    public void AssignRole(string playerId, RoleType role)
    {
        _assignedRoles[playerId] = role;
        _log.LogInfo($"Assigned {role} to player {playerId}.");
    }

    public void ClearRoletopiaHud()
    {
        _assignedRoles.Clear();
        _log.LogDebug("Cleared Roletopia HUD state.");
    }

    public void SetRoletopiaHudVisible(bool visible) =>
        _log.LogDebug($"Roletopia HUD visible: {visible}");

    public void BroadcastSettings(HostModSettings settings) =>
        _log.LogDebug($"Broadcast settings requested. Enabled={settings.RoletopiaEnabled}, role slots={settings.BuildRolePool().Count}.");

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

            var notifier = AccessTools.Field(hud.GetType(), "Notifier")?.GetValue(hud)
                ?? AccessTools.Property(hud.GetType(), "Notifier")?.GetValue(hud);
            var addItem = notifier == null ? null : AccessTools.Method(notifier.GetType(), "AddItem");
            addItem?.Invoke(notifier, new object?[] { message });
        }
        catch (Exception exception)
        {
            _log.LogDebug($"Could not display an in-game notice yet: {exception.Message}");
        }
    }
}

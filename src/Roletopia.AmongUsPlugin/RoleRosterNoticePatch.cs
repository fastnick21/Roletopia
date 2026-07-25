using System.Reflection;
using HarmonyLib;
using Roletopia.RoleSystem;

namespace Roletopia.AmongUsPlugin;

/// <summary>
/// Gives every client visible proof that Roletopia role assignment ran when a
/// match begins. The roster only shows role quantities, never which player owns
/// a role. The local player's own custom role is shown privately on their HUD.
/// </summary>
[HarmonyPatch]
internal static class RoleRosterNoticePatch
{
    private static int _gameFrames;
    private static bool _shown;
    private static bool _wasInGame;

    private static MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("HudManager"), "Update");

    private static void Postfix()
    {
        try
        {
            var coordinator = RoletopiaGameBridge.Coordinator;
            var inGame = coordinator?.CanUseRoleAbilities == true;

            if (!inGame)
            {
                _wasInGame = false;
                _gameFrames = 0;
                _shown = false;
                return;
            }

            if (!_wasInGame)
            {
                _wasInGame = true;
                _gameFrames = 0;
                _shown = false;
            }

            if (_shown) return;
            _gameFrames++;

            // Give Among Us time to finish constructing the live HUD/notifier.
            if (_gameFrames < 45) return;

            var message = BuildNotice(coordinator!);
            if (TryShowHudNotice(message))
                _shown = true;
        }
        catch
        {
            // A failed visual notice must never interrupt the match.
        }
    }

    private static string BuildNotice(Roletopia.Runtime.RuntimeCoordinator coordinator)
    {
        var activeRoles = coordinator.Settings.Roles
            .Where(option => option.Enabled && option.Count > 0)
            .OrderBy(option => option.Role)
            .Select(option => $"{option.Role} x{option.Count}")
            .ToArray();

        var lines = new List<string>
        {
            "ROLETOPIA ACTIVE"
        };

        if (activeRoles.Length == 0)
        {
            lines.Add("No custom roles enabled");
        }
        else
        {
            lines.AddRange(activeRoles);
        }

        var localId = GetLocalPlayerId();
        RoleType? localRole = null;
        if (!string.IsNullOrWhiteSpace(localId))
        {
            foreach (RoleType role in Enum.GetValues(typeof(RoleType)))
            {
                if (!coordinator.IsRoleAssigned(localId!, role)) continue;
                localRole = role;
                break;
            }
        }

        lines.Add(localRole.HasValue
            ? $"Your role: {localRole.Value.ToString().ToUpperInvariant()}"
            : "Your role: no custom role");

        if (coordinator.IsHost && activeRoles.Length > 0)
            lines.Add("Roles assigned successfully");

        return string.Join("\n", lines);
    }

    private static string? GetLocalPlayerId()
    {
        try
        {
            var playerType = AccessTools.TypeByName("PlayerControl");
            if (playerType == null) return null;

            var local = AccessTools.Property(playerType, "LocalPlayer")?.GetValue(null)
                ?? AccessTools.Field(playerType, "LocalPlayer")?.GetValue(null);
            if (local == null) return null;

            return AccessTools.Property(local.GetType(), "PlayerId")?.GetValue(local)?.ToString()
                ?? AccessTools.Field(local.GetType(), "PlayerId")?.GetValue(local)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryShowHudNotice(string message)
    {
        var hud = GetSingleton("HudManager");
        if (hud == null) return false;

        foreach (var notifierName in new[] { "Notifier", "notifier", "Notifications", "notifications" })
        {
            var notifier = ReadMember(hud, notifierName);
            if (notifier == null) continue;

            var methods = notifier.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name is "AddItem" or "AddMessage" or "AddNotification")
                .OrderBy(method => method.GetParameters().Length)
                .ToArray();

            foreach (var method in methods)
            {
                var args = BuildMessageArguments(method.GetParameters(), message);
                if (args == null) continue;

                try
                {
                    method.Invoke(notifier, args);
                    return true;
                }
                catch
                {
                }
            }
        }

        // Fallback for Among Us builds where the notifier member has moved: look
        // for a writable TMP/text field on HudManager and temporarily write there.
        return TryWriteTextRecursive(hud, message, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
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
                continue;
            }

            if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
                continue;
            }

            if (type == typeof(string))
            {
                args[i] = string.Empty;
                continue;
            }

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

        return usedMessage ? args : null;
    }

    private static bool TryWriteTextRecursive(object value, string message, HashSet<object> visited, int depth)
    {
        if (value is string || depth > 3 || !visited.Add(value)) return false;

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = value.GetType();
        var textProperty = type.GetProperty("text", flags) ?? type.GetProperty("Text", flags);
        if (textProperty?.CanWrite == true && textProperty.PropertyType == typeof(string))
        {
            try
            {
                textProperty.SetValue(value, message);
                return true;
            }
            catch
            {
            }
        }

        foreach (var member in type.GetFields(flags).Cast<MemberInfo>().Concat(type.GetProperties(flags))
                     .Where(member => member.Name.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                                      member.Name.Contains("notice", StringComparison.OrdinalIgnoreCase) ||
                                      member.Name.Contains("notification", StringComparison.OrdinalIgnoreCase))
                     .Take(20))
        {
            var child = ReadMember(value, member);
            if (child != null && TryWriteTextRecursive(child, message, visited, depth + 1))
                return true;
        }

        return false;
    }

    private static object? GetSingleton(string typeName)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null) return null;
        return AccessTools.Property(type, "Instance")?.GetValue(null)
            ?? AccessTools.Field(type, "Instance")?.GetValue(null);
    }

    private static object? ReadMember(object instance, string memberName)
    {
        try
        {
            var type = instance.GetType();
            return AccessTools.Property(type, memberName)?.GetValue(instance)
                ?? AccessTools.Field(type, memberName)?.GetValue(instance);
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

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

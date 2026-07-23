using System.Reflection;
using BepInEx.Logging;
using Roletopia.Runtime;

namespace Roletopia.AmongUsPlugin;

internal static class RoletopiaGameBridge
{
    private static AmongUsLifecycleController? _lifecycle;
    private static RuntimeCoordinator? _coordinator;
    private static ReflectionAmongUsAdapter? _adapter;
    private static ManualLogSource? _log;

    public static void Initialize(
        AmongUsLifecycleController lifecycle,
        RuntimeCoordinator coordinator,
        ReflectionAmongUsAdapter adapter,
        ManualLogSource log)
    {
        _lifecycle = lifecycle;
        _coordinator = coordinator;
        _adapter = adapter;
        _log = log;
    }

    public static void Reset()
    {
        _lifecycle = null;
        _coordinator = null;
        _adapter = null;
        _log = null;
    }

    public static void LobbyCreated()
    {
        SafeInvoke("lobby creation", () => _lifecycle?.OnLobbyCreated());
    }

    public static void GameStarting()
    {
        SafeInvoke("game start", () => _lifecycle?.OnGameStarting(0));
    }

    public static void MeetingStarted()
    {
        SafeInvoke("meeting start", () => _lifecycle?.OnMeetingStarted());
    }

    public static void MeetingEnded()
    {
        SafeInvoke("meeting end", () => _lifecycle?.OnMeetingEnded());
    }

    public static void TaskCompleted()
    {
        SafeInvoke("task completion", () => _lifecycle?.OnTaskCompleted());
    }

    public static void GameEnded()
    {
        SafeInvoke("game end", () =>
        {
            _lifecycle?.OnGameEnded();
            return true;
        });
    }

    public static void MainMenuStarted(object __instance)
    {
        SafeInvoke("main-menu start", () =>
        {
            _lifecycle?.OnReturnedToMainMenu();
            TryAddMainMenuMarker(__instance);
            return true;
        });
    }

    private static void TryAddMainMenuMarker(object instance)
    {
        if (instance == null) return;

        const string marker = "Roletopia 0.1.0-alpha loaded";
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();

        // Prefer a version/build label when one exists. Among Us v17.4 does not expose
        // one directly on MainMenuManager, so visible menu buttons are safe fallbacks.
        var preferredNames = new[]
        {
            "version", "build", "creditsButton", "newsButton", "settingsButton",
            "howToPlayButton", "playButton"
        };

        var members = type.GetFields(flags).Cast<MemberInfo>()
            .Concat(type.GetProperties(flags))
            .Where(member => preferredNames.Any(name =>
                member.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(member => Array.FindIndex(preferredNames, name =>
                member.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var member in members)
        {
            var value = ReadMember(instance, member);
            if (value == null) continue;

            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!TrySetTextRecursive(value, marker, flags, visited, 0)) continue;

            _log?.LogInfo($"Added visible Roletopia marker through {type.Name}.{member.Name}.");
            return;
        }

        _log?.LogWarning("Could not find a writable main-menu text target. Roletopia remains loaded; only the visual marker was skipped.");
    }

    private static object? ReadMember(object owner, MemberInfo member)
    {
        try
        {
            return member switch
            {
                FieldInfo field => field.GetValue(owner),
                PropertyInfo property when property.GetIndexParameters().Length == 0 && property.CanRead => property.GetValue(owner),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySetTextRecursive(
        object value,
        string marker,
        BindingFlags flags,
        HashSet<object> visited,
        int depth)
    {
        if (depth > 3 || value is string || !visited.Add(value)) return false;

        var valueType = value.GetType();
        var textProperty = valueType.GetProperty("text", flags) ?? valueType.GetProperty("Text", flags);
        if (textProperty?.CanRead == true && textProperty.CanWrite && textProperty.PropertyType == typeof(string))
        {
            try
            {
                var current = textProperty.GetValue(value) as string ?? string.Empty;
                if (!current.Contains("Roletopia", StringComparison.OrdinalIgnoreCase))
                {
                    var replacement = string.IsNullOrWhiteSpace(current)
                        ? marker
                        : current + "\n<size=55%>" + marker + "</size>";
                    textProperty.SetValue(value, replacement);
                }
                return true;
            }
            catch
            {
                // Continue into child members when this wrapper's text property is unusable.
            }
        }

        var childMembers = valueType.GetFields(flags).Cast<MemberInfo>()
            .Concat(valueType.GetProperties(flags))
            .Where(member =>
                member.Name.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("label", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("title", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("button", StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains("graphic", StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var childMember in childMembers)
        {
            var child = ReadMember(value, childMember);
            if (child != null && TrySetTextRecursive(child, marker, flags, visited, depth + 1))
                return true;
        }

        return false;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static void SafeInvoke(string eventName, Func<bool?> callback)
    {
        try
        {
            var result = callback();
            _log?.LogDebug($"Handled {eventName}; accepted={result?.ToString() ?? "n/a"}.");
        }
        catch (Exception exception)
        {
            _log?.LogError($"Roletopia failed while handling {eventName}: {exception}");
        }
    }
}

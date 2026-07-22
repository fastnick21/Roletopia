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

        foreach (var member in type.GetFields(flags).Cast<MemberInfo>().Concat(type.GetProperties(flags)))
        {
            object? value;
            try
            {
                value = member switch
                {
                    FieldInfo field => field.GetValue(instance),
                    PropertyInfo property when property.GetIndexParameters().Length == 0 && property.CanRead => property.GetValue(instance),
                    _ => null
                };
            }
            catch
            {
                continue;
            }

            if (value == null) continue;
            var valueType = value.GetType();
            var textProperty = valueType.GetProperty("text", flags) ?? valueType.GetProperty("Text", flags);
            if (textProperty?.CanRead != true || textProperty.CanWrite != true || textProperty.PropertyType != typeof(string)) continue;

            var memberName = member.Name;
            if (!memberName.Contains("version", StringComparison.OrdinalIgnoreCase) &&
                !memberName.Contains("build", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var current = textProperty.GetValue(value) as string ?? string.Empty;
                if (!current.Contains("Roletopia", StringComparison.OrdinalIgnoreCase))
                {
                    textProperty.SetValue(value, current + "\n" + marker);
                    _log?.LogInfo($"Added Roletopia marker through {type.Name}.{memberName}.");
                }
                return;
            }
            catch (Exception exception)
            {
                _log?.LogWarning($"Could not update menu text member {memberName}: {exception.Message}");
            }
        }

        var memberNames = string.Join(", ", type.GetMembers(flags).Select(member => member.Name).Distinct().OrderBy(name => name));
        _log?.LogWarning($"Main-menu marker target not found on {type.FullName}. Available members: {memberNames}");
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

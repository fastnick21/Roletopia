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

    public static void ReturnedToMainMenu()
    {
        SafeInvoke("main-menu return", () =>
        {
            _lifecycle?.OnReturnedToMainMenu();
            return true;
        });
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

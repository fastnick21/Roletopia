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
        SafeInvoke("lobby creation", () =>
        {
            var accepted = _lifecycle?.OnLobbyCreated();
            RoletopiaOverlay.SetStatus(accepted == true ? "Lobby ready — Roletopia enabled" : "Lobby joined — host controls Roletopia");
            return accepted;
        });
    }

    public static void GameStarting()
    {
        SafeInvoke("game start", () =>
        {
            var accepted = _lifecycle?.OnGameStarting(0);
            RoletopiaOverlay.SetStatus(accepted == true ? "Game active" : "Game start not prepared");
            return accepted;
        });
    }

    public static void MeetingStarted()
    {
        SafeInvoke("meeting start", () =>
        {
            var accepted = _lifecycle?.OnMeetingStarted();
            if (accepted == true) RoletopiaOverlay.SetStatus("Meeting");
            return accepted;
        });
    }

    public static void MeetingEnded()
    {
        SafeInvoke("meeting end", () =>
        {
            var accepted = _lifecycle?.OnMeetingEnded();
            if (accepted == true) RoletopiaOverlay.SetStatus("Game active");
            return accepted;
        });
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
            RoletopiaOverlay.SetStatus("Results");
            return true;
        });
    }

    public static void ReturnedToMainMenu()
    {
        SafeInvoke("main-menu return", () =>
        {
            _lifecycle?.OnReturnedToMainMenu();
            RoletopiaOverlay.SetStatus("Loaded — enter or host a lobby");
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
            RoletopiaOverlay.SetStatus($"Error during {eventName}");
            _log?.LogError($"Roletopia failed while handling {eventName}: {exception}");
        }
    }
}

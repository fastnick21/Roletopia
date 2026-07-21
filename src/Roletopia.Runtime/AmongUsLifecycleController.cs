using System;
using Roletopia.CoreEngine;

namespace Roletopia.Runtime
{
    public enum RuntimeLifecycleState
    {
        NotLoaded,
        MainMenu,
        Lobby,
        Game,
        Meeting,
        Results
    }

    public interface IRuntimeLogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message, Exception exception);
    }

    public sealed class NullRuntimeLogger : IRuntimeLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception exception) { }
    }

    /// <summary>
    /// Receives lifecycle callbacks from Harmony/BepInEx patches and forwards them
    /// to the platform-independent Roletopia runtime. The real Among Us plugin only
    /// needs to translate game objects into stable player IDs before calling here.
    /// </summary>
    public sealed class AmongUsLifecycleController
    {
        private readonly GameEngine _engine;
        private readonly RuntimeCoordinator _coordinator;
        private readonly IRuntimeLogger _logger;

        public AmongUsLifecycleController(
            GameEngine engine,
            RuntimeCoordinator coordinator,
            IRuntimeLogger logger = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _logger = logger ?? new NullRuntimeLogger();
        }

        public RuntimeLifecycleState State { get; private set; } = RuntimeLifecycleState.NotLoaded;

        public void OnPluginLoaded()
        {
            State = RuntimeLifecycleState.MainMenu;
            _logger.Info("Roletopia runtime loaded.");
        }

        public bool OnLobbyCreated()
        {
            State = RuntimeLifecycleState.Lobby;
            var prepared = _coordinator.PrepareLobby();
            if (!prepared)
                _logger.Warning("Lobby preparation was ignored because this client is not the host.");
            return prepared;
        }

        public bool SetLobbyModEnabled(bool enabled)
        {
            if (State != RuntimeLifecycleState.Lobby)
                return false;
            return _coordinator.ApplyHostToggle(enabled);
        }

        public bool OnGameStarting(int totalTasks)
        {
            if (State != RuntimeLifecycleState.Lobby)
                return false;

            if (!_coordinator.Settings.RoletopiaEnabled)
            {
                _logger.Info("Starting a normal Among Us game because Roletopia is disabled.");
                State = RuntimeLifecycleState.Game;
                return true;
            }

            if (!_coordinator.AssignConfiguredRoles())
            {
                _logger.Warning("Roletopia could not assign roles; game start was rejected.");
                return false;
            }

            _engine.StartGame(Math.Max(0, totalTasks));
            State = RuntimeLifecycleState.Game;
            _logger.Info("Roletopia game started.");
            return true;
        }

        public bool OnMeetingStarted()
        {
            if (State != RuntimeLifecycleState.Game || !_coordinator.Settings.RoletopiaEnabled)
                return false;

            if (!_engine.EnterMeeting())
                return false;

            State = RuntimeLifecycleState.Meeting;
            return true;
        }

        public bool OnMeetingEnded()
        {
            if (State != RuntimeLifecycleState.Meeting)
                return false;

            _engine.ResolveVotes();
            State = RuntimeLifecycleState.Game;
            return true;
        }

        public bool OnTaskCompleted()
        {
            return State == RuntimeLifecycleState.Game
                && _coordinator.Settings.RoletopiaEnabled
                && _engine.CompleteTask();
        }

        public void OnGameEnded()
        {
            State = RuntimeLifecycleState.Results;
            _logger.Info("Roletopia game ended.");
        }

        public void OnReturnedToMainMenu()
        {
            State = RuntimeLifecycleState.MainMenu;
        }
    }
}

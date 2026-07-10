using System;
using System.Collections.Generic;

namespace Roletopia.CoreEngine
{
    public enum GamePhase
    {
        Lobby,
        InProgress,
        Meeting,
        Finished
    }

    public sealed class GameState
    {
        public GamePhase Phase { get; private set; } = GamePhase.Lobby;
        public int CompletedTasks { get; private set; }
        public int TotalTasks { get; private set; }

        public void Start(int totalTasks)
        {
            Phase = GamePhase.InProgress;
            TotalTasks = Math.Max(0, totalTasks);
            CompletedTasks = 0;
        }

        public void EnterMeeting()
        {
            if (Phase == GamePhase.InProgress)
            {
                Phase = GamePhase.Meeting;
            }
        }

        public void ExitMeeting()
        {
            if (Phase == GamePhase.Meeting)
            {
                Phase = GamePhase.InProgress;
            }
        }

        public void CompleteTask()
        {
            if (Phase != GamePhase.InProgress)
            {
                return;
            }

            CompletedTasks = Math.Min(CompletedTasks + 1, TotalTasks);
        }

        public void FinishGame()
        {
            Phase = GamePhase.Finished;
        }
    }

    public sealed class GameEngine
    {
        private readonly Dictionary<string, string> _votes = new Dictionary<string, string>();

        public GameState State { get; } = new GameState();

        public void StartGame(int totalTasks)
        {
            _votes.Clear();
            State.Start(totalTasks);
        }

        public void RegisterVote(string voterId, string targetId)
        {
            if (string.IsNullOrWhiteSpace(voterId) || State.Phase != GamePhase.Meeting)
            {
                return;
            }

            _votes[voterId] = targetId ?? string.Empty;
        }

        public IReadOnlyDictionary<string, string> GetVotes()
        {
            return _votes;
        }

        public void CompleteTask()
        {
            State.CompleteTask();
        }

        public bool EvaluateCrewmateTaskWin()
        {
            return State.TotalTasks > 0 && State.CompletedTasks >= State.TotalTasks;
        }
    }
}

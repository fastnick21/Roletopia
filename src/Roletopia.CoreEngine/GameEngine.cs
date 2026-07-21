using System;
using System.Collections.Generic;
using System.Linq;

namespace Roletopia.CoreEngine
{
    public enum GamePhase { Lobby, InProgress, Meeting, Finished }

    public sealed class PlayerState
    {
        public PlayerState(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Player ID is required.", nameof(id));
            Id = id;
        }

        public string Id { get; }
        public bool IsAlive { get; private set; } = true;
        public bool IsConnected { get; private set; } = true;
        public void Eliminate() => IsAlive = false;
        public void Disconnect() => IsConnected = false;
    }

    public sealed class VoteResult
    {
        public VoteResult(string ejectedPlayerId, bool wasTie, int skipVotes)
        {
            EjectedPlayerId = ejectedPlayerId;
            WasTie = wasTie;
            SkipVotes = skipVotes;
        }
        public string EjectedPlayerId { get; }
        public bool WasTie { get; }
        public int SkipVotes { get; }
    }

    public sealed class GameState
    {
        public GamePhase Phase { get; private set; } = GamePhase.Lobby;
        public int CompletedTasks { get; private set; }
        public int TotalTasks { get; private set; }

        public void Start(int totalTasks)
        {
            if (Phase != GamePhase.Lobby && Phase != GamePhase.Finished) throw new InvalidOperationException("A game is already active.");
            Phase = GamePhase.InProgress;
            TotalTasks = Math.Max(0, totalTasks);
            CompletedTasks = 0;
        }
        public bool EnterMeeting() { if (Phase != GamePhase.InProgress) return false; Phase = GamePhase.Meeting; return true; }
        public bool ExitMeeting() { if (Phase != GamePhase.Meeting) return false; Phase = GamePhase.InProgress; return true; }
        public bool CompleteTask() { if (Phase != GamePhase.InProgress || CompletedTasks >= TotalTasks) return false; CompletedTasks++; return true; }
        public void FinishGame() => Phase = GamePhase.Finished;
        public void Reset() { Phase = GamePhase.Lobby; CompletedTasks = 0; TotalTasks = 0; }
    }

    public sealed class GameEngine
    {
        private readonly Dictionary<string, PlayerState> _players = new Dictionary<string, PlayerState>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _votes = new Dictionary<string, string>(StringComparer.Ordinal);
        public GameState State { get; } = new GameState();
        public IReadOnlyCollection<PlayerState> Players => _players.Values.ToArray();

        public bool AddPlayer(string playerId)
        {
            if (State.Phase != GamePhase.Lobby || string.IsNullOrWhiteSpace(playerId) || _players.ContainsKey(playerId)) return false;
            _players.Add(playerId, new PlayerState(playerId));
            return true;
        }

        public bool DisconnectPlayer(string playerId)
        {
            if (!_players.TryGetValue(playerId ?? string.Empty, out var player)) return false;
            player.Disconnect();
            _votes.Remove(playerId);
            return true;
        }

        public void StartGame(int totalTasks)
        {
            if (_players.Count == 0) throw new InvalidOperationException("At least one player is required.");
            _votes.Clear();
            State.Start(totalTasks);
        }

        public bool EnterMeeting() { _votes.Clear(); return State.EnterMeeting(); }
        public bool ExitMeeting() { _votes.Clear(); return State.ExitMeeting(); }

        public bool RegisterVote(string voterId, string targetId)
        {
            if (State.Phase != GamePhase.Meeting || !_players.TryGetValue(voterId ?? string.Empty, out var voter) || !voter.IsAlive || !voter.IsConnected) return false;
            if (!string.IsNullOrEmpty(targetId) && (!_players.TryGetValue(targetId, out var target) || !target.IsAlive || !target.IsConnected)) return false;
            _votes[voterId] = targetId ?? string.Empty;
            return true;
        }

        public IReadOnlyDictionary<string, string> GetVotes() => new Dictionary<string, string>(_votes);

        public VoteResult ResolveVotes()
        {
            if (State.Phase != GamePhase.Meeting) throw new InvalidOperationException("Votes can only be resolved during a meeting.");
            var skipVotes = _votes.Values.Count(string.IsNullOrEmpty);
            var ranked = _votes.Values.Where(v => !string.IsNullOrEmpty(v)).GroupBy(v => v).Select(g => new { Id = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToArray();
            if (ranked.Length == 0) return new VoteResult(null, false, skipVotes);
            var highest = ranked[0].Count;
            var tie = ranked.Count(x => x.Count == highest) > 1 || skipVotes >= highest;
            if (tie) return new VoteResult(null, true, skipVotes);
            _players[ranked[0].Id].Eliminate();
            return new VoteResult(ranked[0].Id, false, skipVotes);
        }

        public bool CompleteTask() => State.CompleteTask();
        public bool EvaluateCrewmateTaskWin() => State.TotalTasks > 0 && State.CompletedTasks >= State.TotalTasks;
        public bool IsPlayerAlive(string playerId) => _players.TryGetValue(playerId ?? string.Empty, out var player) && player.IsAlive && player.IsConnected;
    }
}

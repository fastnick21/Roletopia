using System;
using System.Collections.Generic;
using System.Linq;

namespace Roletopia.CoreEngine
{
    public enum GamePhase { Lobby, InProgress, Meeting, Finished }
    public enum TeamType { Crewmate, Impostor, Neutral }
    public enum WinReason { None, TasksCompleted, CrewmatesEliminated, ImpostorsEliminated, NeutralObjective }

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
        public TeamType Team { get; private set; } = TeamType.Crewmate;
        public string RoleId { get; private set; } = "Crewmate";

        public void AssignRole(string roleId, TeamType team)
        {
            if (string.IsNullOrWhiteSpace(roleId)) throw new ArgumentException("Role ID is required.", nameof(roleId));
            RoleId = roleId.Trim();
            Team = team;
        }

        public void SetTeam(TeamType team) => Team = team;
        public void Eliminate() => IsAlive = false;
        public void Revive() { if (IsConnected) IsAlive = true; }
        public void Disconnect() { IsConnected = false; IsAlive = false; }
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

    public sealed class WinResult
    {
        public WinResult(TeamType? winningTeam, WinReason reason, string winnerPlayerId = null)
        {
            WinningTeam = winningTeam;
            Reason = reason;
            WinnerPlayerId = winnerPlayerId;
        }
        public TeamType? WinningTeam { get; }
        public WinReason Reason { get; }
        public string WinnerPlayerId { get; }
        public bool HasWinner => Reason != WinReason.None;
        public static WinResult None { get; } = new WinResult(null, WinReason.None);
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

        public event Action<PlayerState> PlayerAdded;
        public event Action<PlayerState> PlayerEliminated;
        public event Action<GamePhase> PhaseChanged;
        public event Action<WinResult> GameWon;

        public bool AddPlayer(string playerId)
        {
            if (State.Phase != GamePhase.Lobby || string.IsNullOrWhiteSpace(playerId) || _players.ContainsKey(playerId)) return false;
            var player = new PlayerState(playerId);
            _players.Add(playerId, player);
            PlayerAdded?.Invoke(player);
            return true;
        }

        public bool TryGetPlayer(string playerId, out PlayerState player) => _players.TryGetValue(playerId ?? string.Empty, out player);

        public bool AssignRole(string playerId, string roleId, TeamType team)
        {
            if (State.Phase != GamePhase.Lobby || !TryGetPlayer(playerId, out var player)) return false;
            player.AssignRole(roleId, team);
            return true;
        }

        public bool UpdatePlayerTeam(string playerId, TeamType team)
        {
            if (State.Phase == GamePhase.Finished || !TryGetPlayer(playerId, out var player) || !player.IsConnected) return false;
            player.SetTeam(team);
            return true;
        }

        public bool DisconnectPlayer(string playerId)
        {
            if (!TryGetPlayer(playerId, out var player)) return false;
            player.Disconnect();
            _votes.Remove(playerId);
            return true;
        }

        public bool EliminatePlayer(string playerId)
        {
            if (State.Phase == GamePhase.Lobby || State.Phase == GamePhase.Finished || !TryGetPlayer(playerId, out var player) || !player.IsAlive) return false;
            player.Eliminate();
            PlayerEliminated?.Invoke(player);
            EvaluateAndFinishIfWon();
            return true;
        }

        public bool RevivePlayer(string playerId)
        {
            if (State.Phase == GamePhase.Lobby || State.Phase == GamePhase.Finished || !TryGetPlayer(playerId, out var player) || player.IsAlive || !player.IsConnected) return false;
            player.Revive();
            return true;
        }

        public void StartGame(int totalTasks)
        {
            if (_players.Values.Count(p => p.IsConnected) < 2) throw new InvalidOperationException("At least two connected players are required.");
            _votes.Clear();
            State.Start(totalTasks);
            PhaseChanged?.Invoke(State.Phase);
        }

        public bool EnterMeeting()
        {
            _votes.Clear();
            var changed = State.EnterMeeting();
            if (changed) PhaseChanged?.Invoke(State.Phase);
            return changed;
        }

        public bool ExitMeeting()
        {
            _votes.Clear();
            var changed = State.ExitMeeting();
            if (changed) PhaseChanged?.Invoke(State.Phase);
            return changed;
        }

        public bool RegisterVote(string voterId, string targetId)
        {
            if (State.Phase != GamePhase.Meeting || !TryGetPlayer(voterId, out var voter) || !voter.IsAlive || !voter.IsConnected) return false;
            if (!string.IsNullOrEmpty(targetId) && (!TryGetPlayer(targetId, out var target) || !target.IsAlive || !target.IsConnected)) return false;
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
            EliminatePlayer(ranked[0].Id);
            return new VoteResult(ranked[0].Id, false, skipVotes);
        }

        public bool CompleteTask()
        {
            var completed = State.CompleteTask();
            if (completed) EvaluateAndFinishIfWon();
            return completed;
        }

        public WinResult EvaluateWin()
        {
            if (State.TotalTasks > 0 && State.CompletedTasks >= State.TotalTasks)
                return new WinResult(TeamType.Crewmate, WinReason.TasksCompleted);

            var connected = _players.Values.Where(p => p.IsConnected).ToArray();
            var alive = connected.Where(p => p.IsAlive).ToArray();
            var gameHasImpostorTeam = connected.Any(p => p.Team == TeamType.Impostor);

            if (gameHasImpostorTeam)
            {
                var impostors = alive.Count(p => p.Team == TeamType.Impostor);
                var nonImpostors = alive.Count(p => p.Team != TeamType.Impostor);

                if (impostors == 0 && alive.Length > 0)
                    return new WinResult(TeamType.Crewmate, WinReason.ImpostorsEliminated);
                if (impostors > 0 && impostors >= nonImpostors)
                    return new WinResult(TeamType.Impostor, WinReason.CrewmatesEliminated);
            }

            return WinResult.None;
        }

        public WinResult DeclareNeutralWinner(string playerId)
        {
            if (!TryGetPlayer(playerId, out var player) || player.Team != TeamType.Neutral || !player.IsConnected) return WinResult.None;
            var result = new WinResult(TeamType.Neutral, WinReason.NeutralObjective, playerId);
            FinishWithResult(result);
            return result;
        }

        public bool EvaluateCrewmateTaskWin() => State.TotalTasks > 0 && State.CompletedTasks >= State.TotalTasks;
        public bool IsPlayerAlive(string playerId) => TryGetPlayer(playerId, out var player) && player.IsAlive && player.IsConnected;

        private void EvaluateAndFinishIfWon()
        {
            var result = EvaluateWin();
            if (result.HasWinner) FinishWithResult(result);
        }

        private void FinishWithResult(WinResult result)
        {
            if (State.Phase == GamePhase.Finished) return;
            State.FinishGame();
            PhaseChanged?.Invoke(State.Phase);
            GameWon?.Invoke(result);
        }
    }
}
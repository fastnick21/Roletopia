using Roletopia.CoreEngine;
using Roletopia.Networking;
using Roletopia.RoleSystem;
using Roletopia.Runtime;

var failures = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) failures.Add(name);
}

var engine = new GameEngine();
Check(engine.AddPlayer("a"), "add player a");
Check(engine.AddPlayer("b"), "add player b");
Check(engine.AddPlayer("c"), "add player c");
Check(engine.AddPlayer("d"), "add player d");
Check(!engine.AddPlayer("a"), "reject duplicate player");
Check(engine.AssignRole("b", "Impostor", TeamType.Impostor), "mark base impostor b");
Check(engine.AssignRole("c", "Impostor", TeamType.Impostor), "mark base impostor c");

var roles = new RoleRegistry();
var assignments = new RoleAssignmentService(roles).Assign(engine, new[] { RoleType.Sheriff, RoleType.Ninja }, 42);
Check(assignments.Count == 2, "assign enabled roles");

var sheriffId = assignments.Single(x => x.Value == RoleType.Sheriff).Key;
var ninjaId = assignments.Single(x => x.Value == RoleType.Ninja).Key;
var otherImpostorId = engine.Players.Single(p => p.Team == TeamType.Impostor && p.Id != ninjaId).Id;

engine.StartGame(2);
var sheriff = roles.Get(RoleType.Sheriff)!;
var now = DateTimeOffset.UtcNow;
var ability = sheriff.UseAbility(new RoleContext(engine, sheriffId, ninjaId, now));
Check(ability.Succeeded, "sheriff shot succeeds against impostor");
Check(ability.EliminatedPlayerId == ninjaId, "sheriff reports eliminated impostor");
Check(!engine.IsPlayerAlive(ninjaId), "sheriff eliminates impostor target");
Check(engine.State.Phase == GamePhase.InProgress, "game continues while another impostor lives");
var cooldownAttempt = sheriff.UseAbility(new RoleContext(engine, sheriffId, otherImpostorId, now.AddSeconds(1)));
Check(cooldownAttempt.Code == AbilityResultCode.OnCooldown, "sheriff cooldown enforced");

var misfireEngine = new GameEngine();
misfireEngine.AddPlayer("sheriff");
misfireEngine.AddPlayer("crew");
misfireEngine.AddPlayer("imp");
misfireEngine.AssignRole("sheriff", RoleType.Sheriff.ToString(), TeamType.Crewmate);
misfireEngine.AssignRole("imp", "Impostor", TeamType.Impostor);
misfireEngine.StartGame(0);
var misfireRegistry = new RoleRegistry();
var misfireResult = misfireRegistry.Get(RoleType.Sheriff)!.UseAbility(
    new RoleContext(misfireEngine, "sheriff", "crew", now, sheriffMisfireKillsSelf: true));
Check(misfireResult.Succeeded, "sheriff misfire resolves");
Check(misfireResult.EliminatedPlayerId == "sheriff", "misfire reports sheriff as victim");
Check(!misfireEngine.IsPlayerAlive("sheriff"), "misfire kills sheriff");
Check(misfireEngine.IsPlayerAlive("crew"), "misfire leaves innocent target alive");

var sheriffWinEngine = new GameEngine();
sheriffWinEngine.AddPlayer("sheriff");
sheriffWinEngine.AddPlayer("imp");
sheriffWinEngine.AssignRole("sheriff", RoleType.Sheriff.ToString(), TeamType.Crewmate);
sheriffWinEngine.AssignRole("imp", "Impostor", TeamType.Impostor);
WinResult? sheriffWin = null;
sheriffWinEngine.GameWon += result => sheriffWin = result;
sheriffWinEngine.StartGame(0);
var sheriffWinRegistry = new RoleRegistry();
var winningShot = sheriffWinRegistry.Get(RoleType.Sheriff)!.UseAbility(
    new RoleContext(sheriffWinEngine, "sheriff", "imp", now));
Check(winningShot.Succeeded, "sheriff winning shot succeeds");
Check(sheriffWinEngine.State.Phase == GamePhase.Finished, "sheriff shot can finish game");
Check(sheriffWin?.WinningTeam == TeamType.Crewmate, "sheriff shares crewmate win condition");
Check(sheriffWin?.Reason == WinReason.ImpostorsEliminated, "sheriff win is impostors eliminated");

var taskEngine = new GameEngine();
taskEngine.AddPlayer("crew");
taskEngine.AddPlayer("other");
taskEngine.StartGame(2);
Check(taskEngine.CompleteTask(), "complete first task");
Check(taskEngine.CompleteTask(), "complete second task");
Check(taskEngine.State.Phase == GamePhase.Finished, "task win finishes game");
Check(taskEngine.EvaluateCrewmateTaskWin(), "task win detected");

var voteEngine = new GameEngine();
voteEngine.AddPlayer("voter");
voteEngine.AddPlayer("target");
voteEngine.StartGame(0);
Check(voteEngine.EnterMeeting(), "enter meeting");
Check(voteEngine.RegisterVote("voter", "target"), "valid vote");
Check(!voteEngine.RegisterVote("unknown", "target"), "reject unknown voter");
var voteResult = voteEngine.ResolveVotes();
Check(voteResult.EjectedPlayerId == "target", "eject voted player");
Check(!voteEngine.IsPlayerAlive("target"), "ejected player dead");

var transport = new FakeTransport();
var network = new NetworkingService(transport);
Check(!network.QueueStateSync(null!), "reject null packet");
Check(network.QueueStateSync("{}"), "queue state packet");
Check(network.FlushBroadcastQueue() == 1, "flush packet count");
Check(transport.BroadcastCount == 1, "transport received packet");

var runtimeEngine = new GameEngine();
var adapter = new FakeRuntimeAdapter(
    new Dictionary<string, TeamType>
    {
        ["host"] = TeamType.Crewmate,
        ["guest"] = TeamType.Impostor
    });
var runtimeRegistry = new RoleRegistry();
var coordinator = new RuntimeCoordinator(runtimeEngine, new RoleAssignmentService(runtimeRegistry), adapter);
coordinator.Settings.DisableAllRoles();
coordinator.Settings.GetRole(RoleType.Sheriff).Enabled = true;
coordinator.Settings.GetRole(RoleType.Sheriff).Count = 1;
coordinator.Settings.GetRole(RoleType.Sheriff).GetSetting("cooldown")!.Value = 5;
Check(coordinator.PrepareLobby(), "runtime prepares host lobby");
Check(coordinator.AssignConfiguredRoles(), "runtime assigns configured role");
Check(adapter.AssignedRoles.Count == 1, "runtime sends one role to game adapter");
Check(adapter.AssignedRoles.TryGetValue("host", out var assignedRole) && assignedRole == RoleType.Sheriff, "runtime assigns sheriff only to crewmate");
runtimeEngine.StartGame(0);
Check(coordinator.CanUseRoleAbilities, "role abilities enabled during gameplay");
var runtimeShot = coordinator.UseRoleAbility("host", "guest", now);
Check(runtimeShot.Succeeded && runtimeShot.EliminatedPlayerId == "guest", "runtime executes configured sheriff shot");
Check(runtimeEngine.State.Phase == GamePhase.Finished, "runtime sheriff shot triggers crew win");
coordinator.ApplyWinResult(runtimeEngine.EvaluateWin());
Check(adapter.WinApplyCount == 0, "finished engine does not synthesize duplicate win result");
Check(coordinator.ApplyHostToggle(false), "host can disable Roletopia");
Check(!coordinator.Settings.RoletopiaEnabled, "disabled setting stored");
Check(adapter.HudCleared && !adapter.HudVisible, "disabled mode clears custom HUD");
Check(!coordinator.AssignConfiguredRoles(), "disabled mode blocks custom role assignment");
Check(coordinator.ApplyHostToggle(true), "host can re-enable Roletopia");

var guestEngine = new GameEngine();
var guestAdapter = new FakeRuntimeAdapter(
    new Dictionary<string, TeamType>
    {
        ["crew"] = TeamType.Crewmate,
        ["imp"] = TeamType.Impostor
    },
    isHost: false);
var guestCoordinator = new RuntimeCoordinator(guestEngine, new RoleAssignmentService(new RoleRegistry()), guestAdapter);
Check(guestCoordinator.PrepareLobby(), "guest prepares local role state");
Check(guestCoordinator.AssignConfiguredRoles(), "guest deterministically assigns local Sheriff state");
Check(guestAdapter.AssignedRoles.Count == 1, "guest stores local Sheriff assignment");
Check(guestAdapter.ResetCount >= 2, "role assignments reset between lobby and game assignment");
guestEngine.StartGame(0);
Check(guestCoordinator.CanUseRoleAbilities, "guest Sheriff ability state activates in game");
Check(guestEngine.EnterMeeting(), "guest test enters meeting");
Check(!guestCoordinator.CanUseRoleAbilities, "Sheriff abilities disabled during meetings");

var syncEngine = new GameEngine();
var syncAdapter = new FakeRuntimeAdapter(new Dictionary<string, TeamType>
{
    ["crew"] = TeamType.Crewmate,
    ["imp"] = TeamType.Impostor
});
var syncCoordinator = new RuntimeCoordinator(syncEngine, new RoleAssignmentService(new RoleRegistry()), syncAdapter);
syncCoordinator.PrepareLobby();
syncCoordinator.AssignConfiguredRoles();
syncEngine.StartGame(0);
WinResult? observedWin = null;
syncEngine.GameWon += result =>
{
    observedWin = result;
    syncCoordinator.ApplyWinResult(result);
};
Check(syncCoordinator.NotifyPlayerEliminated("imp"), "observed Among Us murder updates Roletopia engine");
Check(observedWin?.WinningTeam == TeamType.Crewmate, "observed final impostor death produces crew win");
Check(syncAdapter.WinApplyCount == 1, "host adapter receives live win result once");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke tests failed: " + string.Join(", ", failures));
    return 1;
}

Console.WriteLine("All Roletopia smoke tests passed.");
return 0;

sealed class FakeTransport : INetworkTransport
{
    public int BroadcastCount { get; private set; }
    public void SendToClient(string clientId, SyncPacket packet) { }
    public void Broadcast(SyncPacket packet) => BroadcastCount++;
}

sealed class FakeRuntimeAdapter : IAmongUsRuntimeAdapter
{
    private readonly IReadOnlyDictionary<string, TeamType> _teams;
    private readonly bool _isHost;

    public FakeRuntimeAdapter(IReadOnlyDictionary<string, TeamType> teams, bool isHost = true)
    {
        _teams = teams;
        _isHost = isHost;
        ConnectedPlayerIds = teams.Keys.ToArray();
    }

    public bool IsHost => _isHost;
    public IReadOnlyCollection<string> ConnectedPlayerIds { get; }
    public Dictionary<string, RoleType> AssignedRoles { get; } = new();
    public bool HudVisible { get; private set; }
    public bool HudCleared { get; private set; }
    public int ResetCount { get; private set; }
    public int WinApplyCount { get; private set; }
    public TeamType GetPlayerTeam(string playerId) => _teams.TryGetValue(playerId, out var team) ? team : TeamType.Crewmate;
    public bool IsRoleAssigned(string playerId, RoleType role) => AssignedRoles.TryGetValue(playerId, out var assigned) && assigned == role;
    public void ShowHostMessage(string message) { }
    public void AssignRole(string playerId, RoleType role) => AssignedRoles[playerId] = role;
    public void ResetRoleAssignments()
    {
        AssignedRoles.Clear();
        ResetCount++;
    }
    public void ClearRoletopiaHud()
    {
        HudCleared = true;
        ResetRoleAssignments();
    }
    public void SetRoletopiaHudVisible(bool visible) => HudVisible = visible;
    public void BroadcastSettings(HostModSettings settings) { }
    public void ApplyWinResult(WinResult result)
    {
        if (_isHost && result?.HasWinner == true) WinApplyCount++;
    }
}

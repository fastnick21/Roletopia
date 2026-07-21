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

var roles = new RoleRegistry();
var assignments = new RoleAssignmentService(roles).Assign(engine, new[] { RoleType.Sheriff, RoleType.Ninja }, 42);
Check(assignments.Count == 2, "assign enabled roles");

var sheriffId = assignments.Single(x => x.Value == RoleType.Sheriff).Key;
var ninjaId = assignments.Single(x => x.Value == RoleType.Ninja).Key;
var targetId = engine.Players.Select(p => p.Id).First(id => id != sheriffId && id != ninjaId);

engine.StartGame(2);
var sheriff = roles.Get(RoleType.Sheriff)!;
var now = DateTimeOffset.UtcNow;
var ability = sheriff.UseAbility(new RoleContext(engine, sheriffId, targetId, now));
Check(ability.Succeeded, "sheriff ability succeeds");
Check(!engine.IsPlayerAlive(targetId), "ability eliminates target");
var cooldownAttempt = sheriff.UseAbility(new RoleContext(engine, sheriffId, ninjaId, now.AddSeconds(1)));
Check(cooldownAttempt.Code == AbilityResultCode.OnCooldown, "ability cooldown enforced");

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
var adapter = new FakeRuntimeAdapter("host", "guest");
var coordinator = new RuntimeCoordinator(runtimeEngine, new RoleAssignmentService(new RoleRegistry()), adapter);
coordinator.Settings.DisableAllRoles();
coordinator.Settings.GetRole(RoleType.Sheriff).Enabled = true;
coordinator.Settings.GetRole(RoleType.Sheriff).Count = 1;
Check(coordinator.PrepareLobby(), "runtime prepares host lobby");
Check(coordinator.AssignConfiguredRoles(), "runtime assigns configured role");
Check(adapter.AssignedRoles.Count == 1, "runtime sends one role to game adapter");
Check(coordinator.ApplyHostToggle(false), "host can disable Roletopia");
Check(!coordinator.Settings.RoletopiaEnabled, "disabled setting stored");
Check(adapter.HudCleared && !adapter.HudVisible, "disabled mode clears custom HUD");
Check(!coordinator.AssignConfiguredRoles(), "disabled mode blocks custom role assignment");
Check(coordinator.ApplyHostToggle(true), "host can re-enable Roletopia");

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
    public FakeRuntimeAdapter(params string[] players) => ConnectedPlayerIds = players;
    public bool IsHost => true;
    public IReadOnlyCollection<string> ConnectedPlayerIds { get; }
    public Dictionary<string, RoleType> AssignedRoles { get; } = new();
    public bool HudVisible { get; private set; }
    public bool HudCleared { get; private set; }
    public void ShowHostMessage(string message) { }
    public void AssignRole(string playerId, RoleType role) => AssignedRoles[playerId] = role;
    public void ClearRoletopiaHud() => HudCleared = true;
    public void SetRoletopiaHudVisible(bool visible) => HudVisible = visible;
    public void BroadcastSettings(HostModSettings settings) { }
}
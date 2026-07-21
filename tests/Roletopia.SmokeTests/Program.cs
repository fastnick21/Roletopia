using Roletopia.CoreEngine;
using Roletopia.Networking;

var failures = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) failures.Add(name);
}

var engine = new GameEngine();
Check(engine.AddPlayer("a"), "add player a");
Check(engine.AddPlayer("b"), "add player b");
Check(!engine.AddPlayer("a"), "reject duplicate player");
engine.StartGame(2);
Check(engine.CompleteTask(), "complete first task");
Check(engine.CompleteTask(), "complete second task");
Check(engine.EvaluateCrewmateTaskWin(), "task win");
Check(engine.EnterMeeting(), "enter meeting");
Check(engine.RegisterVote("a", "b"), "valid vote");
Check(!engine.RegisterVote("unknown", "b"), "reject unknown voter");
var result = engine.ResolveVotes();
Check(result.EjectedPlayerId == "b", "eject voted player");
Check(!engine.IsPlayerAlive("b"), "ejected player dead");

var transport = new FakeTransport();
var network = new NetworkingService(transport);
Check(!network.QueueStateSync(null!), "reject null packet");
Check(network.QueueStateSync("{}"), "queue state packet");
Check(network.FlushBroadcastQueue() == 1, "flush packet count");
Check(transport.BroadcastCount == 1, "transport received packet");

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

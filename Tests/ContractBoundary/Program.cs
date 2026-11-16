using System.Text.Json;
using Crystal.Agent.Contracts;
using Crystal.GameCore;

var registration = new AgentRegistrationRequest(
    "zone-1",
    "Test Zone",
    "agent-1",
    "0.1.0",
    1,
    1,
    DateTimeOffset.Parse("2026-11-13T00:00:00Z"),
    ["content", "metrics"]);

string json = JsonSerializer.Serialize(registration, AgentContractJson.Options);
AgentRegistrationRequest? roundTrip = JsonSerializer.Deserialize<AgentRegistrationRequest>(
    json,
    AgentContractJson.Options);

Require(roundTrip is not null, "Agent registration JSON deserialization failed.");
Require(roundTrip!.ZoneId == registration.ZoneId && roundTrip.AgentId == registration.AgentId,
    "Agent registration JSON round trip failed.");
Require(AgentCommandTypes.StageContent == "content.stage", "Agent command wire value changed.");
Require(AgentApiRoutes.Register == "/api/v1/zones/register", "Agent route contract changed.");

var snapshot = new ContentSnapshotHandle<string>(1, "1.0.0", new string('a', 64), 1, "snapshot");
Require(snapshot.Snapshot == "snapshot", "GameCore content snapshot contract failed.");

Console.WriteLine("Contract boundary checks passed.");
return 0;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

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
Require(AgentApiRoutes.ReleaseArtifact == "/api/v1/releases/{releaseId}/artifact",
    "Release artifact route contract changed.");
Require(AgentCommandStatuses.Final.Contains(AgentCommandStatuses.Succeeded),
    "Agent command final statuses changed.");

var commandRequest = new CreateZoneCommandRequest(
    AgentCommandTypes.ActivateContent,
    JsonSerializer.SerializeToElement(new ReleaseCommandPayload(42), AgentContractJson.Options),
    null);
string commandJson = JsonSerializer.Serialize(commandRequest, AgentContractJson.Options);
CreateZoneCommandRequest? commandRoundTrip = JsonSerializer.Deserialize<CreateZoneCommandRequest>(
    commandJson,
    AgentContractJson.Options);
Require(commandRoundTrip is not null &&
        commandRoundTrip.Payload is { } payload &&
        payload.GetProperty("releaseId").GetInt64() == 42,
    "Zone command payload JSON round trip failed.");

var zone = new ZoneDto(
    "zone-1",
    "Test Zone",
    "agent-1",
    "0.1.0",
    1,
    1,
    AgentStatuses.Online,
    DateTimeOffset.Parse("2026-11-17T00:00:00Z"),
    DateTimeOffset.Parse("2026-11-17T00:01:00Z"),
    42,
    "1.0.0",
    new string('a', 64),
    null,
    null,
    null,
    0,
    0,
    0,
    ["process-supervisor"],
    null,
    DateTimeOffset.Parse("2026-11-17T00:00:00Z"),
    DateTimeOffset.Parse("2026-11-17T00:01:00Z"));
ZoneDto? zoneRoundTrip = JsonSerializer.Deserialize<ZoneDto>(
    JsonSerializer.Serialize(zone, AgentContractJson.Options),
    AgentContractJson.Options);
Require(zoneRoundTrip?.ActiveReleaseId == 42, "Zone JSON round trip failed.");

var snapshot = new ContentSnapshotHandle<string>(1, "1.0.0", new string('a', 64), 1, "snapshot");
Require(snapshot.Snapshot == "snapshot", "GameCore content snapshot contract failed.");

Console.WriteLine("Contract boundary checks passed.");
return 0;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Crystal.Content;
using Server.MirDatabase;

namespace Crystal.LegacyRuntime;

public static class LegacyRespawnProjection
{
    public static ContentEntity CreateEntity(MapInfo map, RespawnInfo respawn, int ordinal)
    {
        var localIndex = (long)map.Index * 1_000_000L + ordinal + 1L;
        var payload = LegacyJsonSerializer.Serialize(respawn);
        var node = JsonNode.Parse(payload.GetRawText())!.AsObject();
        node["mapIndex"] = map.Index;
        node["mapFileName"] = map.FileName;
        node["legacyRespawnIndex"] = respawn.RespawnIndex;
        return new ContentEntity
        {
            EntityType = ContentEntityTypes.Respawn,
            LocalIndex = localIndex,
            ContentKey = $"respawn.map{map.Index}.{ordinal + 1}",
            Data = ContentJson.Canonicalize(JsonSerializer.SerializeToElement(node))
        };
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Crystal.Content;
using Server.MirDatabase;

namespace Crystal.LegacyImporter;

public static class BuffContentImporter
{
    public static IReadOnlyList<ContentEntity> Map()
    {
        return BuffInfo.Load()
            .OrderBy(buff => buff.Type)
            .Select(CreateEntity)
            .ToArray();
    }

    private static ContentEntity CreateEntity(BuffInfo buff)
    {
        var data = LegacyJsonSerializer.Serialize(buff);
        var node = JsonNode.Parse(data.GetRawText())!.AsObject();
        node["Name"] = buff.Type.ToString();

        return new ContentEntity
        {
            EntityType = ContentEntityTypes.Buff,
            LocalIndex = (long)(byte)buff.Type,
            ContentKey = $"buff.{buff.Type}",
            Data = ContentJson.Canonicalize(JsonSerializer.SerializeToElement(node))
        };
    }
}

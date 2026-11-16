using System.Text.Json;

namespace Crystal.Control.Api.Content;

public sealed record ContentEditorSchemaDefinition(
    string EntityType,
    int SchemaVersion,
    ContentEditorSchemaDto Schema,
    Func<JsonElement, long, string?, Dictionary<string, string[]>> ValidateStructure,
    Func<JsonElement, IEnumerable<string>> ValidateCompleteness);

public static class ContentEditorSchemas
{
    private static readonly IReadOnlyDictionary<string, ContentEditorSchemaDefinition> Definitions =
        new Dictionary<string, ContentEditorSchemaDefinition>(StringComparer.Ordinal)
        {
            [ContentItemEditorSchema.EntityType] = new(
                ContentItemEditorSchema.EntityType,
                ContentItemEditorSchema.SchemaVersion,
                ContentItemEditorSchema.Schema,
                ContentItemEditorSchema.ValidateStructure,
                ContentItemEditorSchema.ValidateCompleteness),
            [ContentMonsterEditorSchema.EntityType] = new(
                ContentMonsterEditorSchema.EntityType,
                ContentMonsterEditorSchema.SchemaVersion,
                ContentMonsterEditorSchema.Schema,
                ContentMonsterEditorSchema.ValidateStructure,
                ContentMonsterEditorSchema.ValidateCompleteness)
        };

    public static bool TryGet(string entityType, out ContentEditorSchemaDefinition definition)
    {
        if (Definitions.TryGetValue(entityType, out var found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }
}

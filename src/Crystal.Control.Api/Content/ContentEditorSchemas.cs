using System.Text.Json;
using System.Text.Json.Nodes;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

public sealed record ContentEditorSchemaDefinition(
    string EntityType,
    int SchemaVersion,
    ContentEditorSchemaDto Schema,
    Func<JsonElement, long, string?, Dictionary<string, string[]>> ValidateStructure,
    Func<JsonElement, IEnumerable<string>> ValidateCompleteness,
    Func<JsonElement, long, ContentEditorIdentity>? ResolveIdentity = null,
    Func<JsonElement, JsonElement>? Normalize = null,
    Func<JsonElement, IEnumerable<ContentEditorReferenceValue>>? GetReferences = null,
    string? LabelPath = null,
    IReadOnlyList<string>? SearchPaths = null);

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
                ContentMonsterEditorSchema.ValidateCompleteness),
            [ContentMapEditorSchema.EntityType] = new(
                ContentMapEditorSchema.EntityType,
                ContentMapEditorSchema.SchemaVersion,
                ContentMapEditorSchema.Schema,
                ContentMapEditorSchema.ValidateStructure,
                ContentMapEditorSchema.ValidateCompleteness),
            [ContentNpcEditorSchema.EntityType] = new(
                ContentNpcEditorSchema.EntityType,
                ContentNpcEditorSchema.SchemaVersion,
                ContentNpcEditorSchema.Schema,
                ContentNpcEditorSchema.ValidateStructure,
                ContentNpcEditorSchema.ValidateCompleteness,
                ResolveIdentity: ContentNpcEditorSchema.ResolveIdentity,
                Normalize: ContentNpcEditorSchema.Normalize,
                GetReferences: ContentNpcEditorSchema.GetReferences,
                LabelPath: "Name"),
            [ContentScriptEditorSchema.EntityType] = new(
                ContentScriptEditorSchema.EntityType,
                ContentScriptEditorSchema.SchemaVersion,
                ContentScriptEditorSchema.Schema,
                ContentScriptEditorSchema.ValidateStructure,
                ContentScriptEditorSchema.ValidateCompleteness,
                ResolveIdentity: ContentScriptEditorSchema.ResolveIdentity,
                Normalize: ContentScriptEditorSchema.Normalize,
                GetReferences: ContentScriptEditorSchema.GetReferences,
                LabelPath: "fileNameKey",
                SearchPaths: ["fileName", "relativePath", "source"]),
            [ContentDropEditorSchema.EntityType] = new(
                ContentDropEditorSchema.EntityType,
                ContentDropEditorSchema.SchemaVersion,
                ContentDropEditorSchema.Schema,
                ContentDropEditorSchema.ValidateStructure,
                ContentDropEditorSchema.ValidateCompleteness,
                ResolveIdentity: ContentDropEditorSchema.ResolveIdentity,
                Normalize: ContentDropEditorSchema.Normalize,
                GetReferences: ContentDropEditorSchema.GetReferences,
                LabelPath: "monsterName"),
            [ContentMagicEditorSchema.EntityType] = new(
                ContentMagicEditorSchema.EntityType,
                ContentMagicEditorSchema.SchemaVersion,
                ContentMagicEditorSchema.Schema,
                ContentMagicEditorSchema.ValidateStructure,
                ContentMagicEditorSchema.ValidateCompleteness,
                ResolveIdentity: ContentMagicEditorSchema.ResolveIdentity,
                Normalize: ContentMagicEditorSchema.Normalize,
                LabelPath: "Name"),
            [ContentQuestEditorSchema.EntityType] = new(
                ContentQuestEditorSchema.EntityType,
                ContentQuestEditorSchema.SchemaVersion,
                ContentQuestEditorSchema.Schema,
                ContentQuestEditorSchema.ValidateStructure,
                ContentQuestEditorSchema.ValidateCompleteness,
                ResolveIdentity: ContentQuestEditorSchema.ResolveIdentity,
                Normalize: ContentQuestEditorSchema.Normalize,
                GetReferences: ContentQuestEditorSchema.GetReferences,
                LabelPath: "Name"),
            [ContentGameShopEditorSchema.EntityType] = new(
                ContentGameShopEditorSchema.EntityType,
                ContentGameShopEditorSchema.SchemaVersion,
                ContentGameShopEditorSchema.Schema,
                ContentGameShopEditorSchema.ValidateStructure,
                ContentGameShopEditorSchema.ValidateCompleteness,
                ResolveIdentity: ContentGameShopEditorSchema.ResolveIdentity,
                Normalize: ContentGameShopEditorSchema.Normalize,
                GetReferences: ContentGameShopEditorSchema.GetReferences,
                LabelPath: "Category",
                SearchPaths: ["Category", "Class"]),
            [ContentRecipeEditorSchema.EntityType] = new(
                ContentRecipeEditorSchema.EntityType,
                ContentRecipeEditorSchema.SchemaVersion,
                ContentRecipeEditorSchema.Schema,
                ContentRecipeEditorSchema.ValidateStructure,
                ContentRecipeEditorSchema.ValidateCompleteness,
                ResolveIdentity: ContentRecipeEditorSchema.ResolveIdentity,
                Normalize: ContentRecipeEditorSchema.Normalize,
                GetReferences: ContentRecipeEditorSchema.GetReferences,
                LabelPath: "name")
        };

    public static bool TryGet(string entityType, out ContentEditorSchemaDefinition schema)
    {
        return Definitions.TryGetValue(entityType, out schema!);
    }

    public static IReadOnlyList<ContentEditorSchemaDto> All => Definitions.Values
        .Select(definition => definition.Schema)
        .OrderBy(schema => schema.EntityType, StringComparer.Ordinal)
        .ToArray();

    public static JsonElement Normalize(string entityType, JsonElement data)
    {
        return Definitions.TryGetValue(entityType, out var schema) && schema.Normalize is not null
            ? schema.Normalize(data)
            : data;
    }

    public static Dictionary<string, string[]> ValidateRawStructure(string entityType, JsonElement data)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (Definitions.TryGetValue(entityType, out var schema))
        {
            ContentEditorValidation.ValidateFields(
                data,
                ContentEditorValidation.AllFields(schema.Schema),
                errors);
        }

        return errors;
    }

    public static ContentEditorIdentity ResolveIdentity(string entityType, JsonElement data, long nextLocalIndex)
    {
        if (Definitions.TryGetValue(entityType, out var schema) && schema.ResolveIdentity is not null)
        {
            return schema.ResolveIdentity(data, nextLocalIndex);
        }

        return new ContentEditorIdentity(nextLocalIndex, $"{entityType}.{nextLocalIndex}");
    }

    public static JsonElement ApplyIdentity(string entityType, JsonElement data, ContentEditorIdentity identity)
    {
        var node = JsonNode.Parse(data.GetRawText()) as JsonObject
                   ?? throw new JsonException("Entity data must be a JSON object.");
        switch (entityType)
        {
            case ContentEntityTypes.Item:
            case ContentEntityTypes.Monster:
            case ContentEntityTypes.Map:
            case ContentEntityTypes.Npc:
            case ContentEntityTypes.Quest:
                node["Index"] = identity.LocalIndex;
                break;
            case ContentEntityTypes.GameShop:
                node["GIndex"] = identity.LocalIndex;
                break;
            case ContentEntityTypes.Drop:
                node["monsterIndex"] = identity.LocalIndex;
                break;
            case ContentEntityTypes.Magic:
                if (ContentMagicEditorSchema.TryReadSpell(data, out var spell, out var spellName))
                {
                    node["Spell"] = spellName;
                    if (identity.LocalIndex != spell)
                    {
                        throw new DraftConflictException(
                            $"Magic identity index {identity.LocalIndex} does not match Spell value {spell}.");
                    }
                }

                break;
        }

        return JsonSerializer.SerializeToElement(node, ContentJson.CompactOptions);
    }

    public static bool IsContentKeyValid(
        string entityType,
        JsonElement data,
        long localIndex,
        string contentKey)
    {
        if (!Definitions.TryGetValue(entityType, out var schema)) return true;
        var identity = schema.ResolveIdentity?.Invoke(data, localIndex)
                       ?? new ContentEditorIdentity(localIndex, $"{entityType}.{localIndex}");
        if (string.Equals(contentKey, identity.ContentKey, StringComparison.Ordinal)) return true;

        return entityType switch
        {
            ContentEntityTypes.Recipe => string.Equals(
                contentKey,
                BuildRecipeLegacyKey(data, localIndex),
                StringComparison.Ordinal),
            ContentEntityTypes.Script => string.Equals(
                contentKey,
                BuildScriptLegacyKey(data, localIndex),
                StringComparison.Ordinal),
            _ => false
        };
    }

    private static string BuildRecipeLegacyKey(JsonElement data, long localIndex)
    {
        var name = data.ValueKind == JsonValueKind.Object &&
                   data.TryGetProperty("output", out var output) &&
                   output.ValueKind == JsonValueKind.Object
            ? ContentEditorValidation.ReadString(output, "itemName")
            : null;
        name ??= ContentEditorValidation.ReadString(data, "name");
        return $"recipe.{name ?? localIndex.ToString()}";
    }

    private static string BuildScriptLegacyKey(JsonElement data, long localIndex)
    {
        var fileName = ContentEditorValidation.ReadString(data, "fileName")
                       ?? ContentEditorValidation.ReadString(data, "relativePath")
                       ?? localIndex.ToString();
        return ContentScriptEditorSchema.LegacyContentKeyForFileName(fileName);
    }

}

using System.Text.Json;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

public static class ContentValidationRules
{
    public static IEnumerable<ReleaseValidationIssueDto> ValidateEntitySemantics(
        IReadOnlyList<ContentEntity> entities)
    {
        var indices = entities
            .GroupBy(entity => entity.EntityType, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entity => entity.LocalIndex).ToHashSet(),
                StringComparer.Ordinal);
        var scriptKeys = BuildScriptKeyLookup(entities);

        var mapFileNames = entities
            .Where(entity =>
                entity.EntityType == ContentEntityTypes.Map &&
                entity.Data.ValueKind == JsonValueKind.Object &&
                entity.Data.TryGetProperty("FileName", out var fileName) &&
                fileName.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(fileName.GetString()))
            .Select(entity => new
            {
                Entity = entity,
                FileName = entity.Data.GetProperty("FileName").GetString()!.Trim()
            });

        foreach (var duplicate in mapFileNames
                     .GroupBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var contentKeys = duplicate.Select(item => item.Entity.ContentKey).Order(StringComparer.Ordinal).ToArray();
            foreach (var item in duplicate)
            {
                var collidingKeys = contentKeys
                    .Where(key => !string.Equals(key, item.Entity.ContentKey, StringComparison.Ordinal));
                yield return new ReleaseValidationIssueDto(
                    "map-file-name-duplicate",
                    $"Map file name '{item.FileName}' is also used by {string.Join(", ", collidingKeys)}.",
                    item.Entity.ContentKey);
            }
        }

        foreach (var entity in entities)
        {
            if (entity.Data.ValueKind != JsonValueKind.Object) continue;

            if (ContentEditorSchemas.TryGet(entity.EntityType, out var editorSchema))
            {
                foreach (var message in editorSchema.ValidateCompleteness(entity.Data))
                {
                    yield return new ReleaseValidationIssueDto(
                        $"{entity.EntityType}-completeness",
                        message,
                        entity.ContentKey);
                }

                if (!ContentEditorSchemas.IsContentKeyValid(
                        entity.EntityType,
                        entity.Data,
                        entity.LocalIndex,
                        entity.ContentKey))
                {
                    var expected = ContentEditorSchemas.ResolveIdentity(
                        entity.EntityType,
                        entity.Data,
                        entity.LocalIndex);
                    yield return new ReleaseValidationIssueDto(
                        $"{entity.EntityType}-content-key",
                        $"Content key for '{entity.EntityType}' must be '{expected.ContentKey}'.",
                        entity.ContentKey);
                }

                if (editorSchema.GetReferences is not null)
                {
                    foreach (var reference in editorSchema.GetReferences(entity.Data))
                    {
                        if (!reference.Required) continue;
                        if (!ReferenceExists(reference, indices, scriptKeys))
                        {
                            yield return new ReleaseValidationIssueDto(
                                "reference-missing",
                                $"Reference '{reference.Path}' points to missing {reference.EntityType} " +
                                $"{(reference.Key ?? reference.Index.ToString())}.",
                                entity.ContentKey);
                        }
                    }
                }
            }

            if (entity.EntityType == ContentEntityTypes.Script &&
                entity.Data.TryGetProperty("validation", out var validation))
            {
                if (validation.ValueKind == JsonValueKind.Object &&
                    validation.TryGetProperty("errorCount", out var errorCount) &&
                    errorCount.TryGetInt32(out var errors) &&
                    errors > 0)
                {
                    yield return new ReleaseValidationIssueDto(
                        "script-validation-error",
                        $"NPC script has {errors} parse error(s).",
                        entity.ContentKey);
                }
            }

            if (entity.EntityType == ContentEntityTypes.Asset &&
                entity.Data.TryGetProperty("assetType", out var assetType) &&
                assetType.ValueKind == JsonValueKind.String &&
                string.Equals(assetType.GetString(), "map", StringComparison.Ordinal) &&
                entity.Data.TryGetProperty("map", out var map) &&
                map.ValueKind == JsonValueKind.Object &&
                map.TryGetProperty("valid", out var valid) &&
                valid.ValueKind == JsonValueKind.False)
            {
                yield return new ReleaseValidationIssueDto(
                    "map-asset-invalid",
                    "Map asset metadata is marked invalid.",
                    entity.ContentKey);
            }
        }

        foreach (var duplicate in entities
                     .Where(entity => entity.EntityType == ContentEntityTypes.Drop)
                     .Select(entity => new
                     {
                         Entity = entity,
                         Target = ContentEditorValidation.ReadLong(entity.Data, "monsterIndex")
                     })
                     .Where(item => item.Target is not null)
                     .GroupBy(item => item.Target!.Value)
                     .Where(group => group.Count() > 1))
        {
            foreach (var item in duplicate)
            {
                yield return new ReleaseValidationIssueDto(
                    "drop-duplicate",
                    $"Monster {duplicate.Key} has more than one drop table.",
                    item.Entity.ContentKey);
            }
        }

        foreach (var duplicate in entities
                     .Where(entity => entity.EntityType == ContentEntityTypes.Recipe)
                     .Select(entity => new
                     {
                         Entity = entity,
                         Output = ReadRecipeOutputIndex(entity.Data)
                     })
                     .Where(item => item.Output is > 0)
                     .GroupBy(item => item.Output!.Value)
                     .Where(group => group.Count() > 1))
        {
            foreach (var item in duplicate)
            {
                yield return new ReleaseValidationIssueDto(
                    "recipe-duplicate",
                    $"Item {duplicate.Key} is produced by more than one recipe.",
                    item.Entity.ContentKey);
            }
        }

        foreach (var duplicate in entities
                     .Where(entity => entity.EntityType == ContentEntityTypes.Script)
                     .Select(entity => new
                     {
                         Entity = entity,
                         Key = ContentEditorValidation.ReadString(entity.Data, "fileNameKey")
                     })
                     .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                     .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var item in duplicate)
            {
                yield return new ReleaseValidationIssueDto(
                    "script-duplicate",
                    $"Script file '{duplicate.Key}' is defined more than once.",
                    item.Entity.ContentKey);
            }
        }
    }

    private static bool ReferenceExists(
        ContentEditorReferenceValue reference,
        IReadOnlyDictionary<string, HashSet<long>> indices,
        IReadOnlyDictionary<string, string> scriptKeys)
    {
        if (reference.EntityType == ContentEntityTypes.Script && !string.IsNullOrWhiteSpace(reference.Key))
        {
            return scriptKeys.ContainsKey(NormalizeReferenceKey(reference.Key));
        }

        return reference.Index > 0 &&
               indices.TryGetValue(reference.EntityType, out var available) &&
               available.Contains(reference.Index);
    }

    private static IReadOnlyDictionary<string, string> BuildScriptKeyLookup(
        IEnumerable<ContentEntity> entities)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities.Where(entity => entity.EntityType == ContentEntityTypes.Script))
        {
            AddScriptKey(ContentEditorValidation.ReadString(entity.Data, "fileNameKey"));
            AddScriptKey(ContentEditorValidation.ReadString(entity.Data, "relativePath"));
            AddScriptKey(ContentEditorValidation.ReadString(entity.Data, "fileName"));
            AddScriptKey(entity.ContentKey);
        }

        return result;

        void AddScriptKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var normalized = NormalizeReferenceKey(value);
            result.TryAdd(normalized, value);
            if (normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                result.TryAdd(normalized[..^4], value);
            }
        }
    }

    private static string NormalizeReferenceKey(string value)
    {
        return value.Trim()
            .Replace('\\', '/')
            .Replace(".txt", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('/');
    }

    private static long? ReadRecipeOutputIndex(JsonElement data)
    {
        return data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty("output", out var output) &&
               output.ValueKind == JsonValueKind.Object
            ? ContentEditorValidation.ReadLong(output, "itemIndex")
            : null;
    }
}

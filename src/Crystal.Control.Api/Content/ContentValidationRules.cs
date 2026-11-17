using System.Text.Json;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

public static class ContentValidationRules
{
    public static IEnumerable<ReleaseValidationIssueDto> ValidateEntitySemantics(
        IReadOnlyList<ContentEntity> entities)
    {
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

                var expectedContentKey = $"{entity.EntityType}.{entity.LocalIndex}";
                if (!string.Equals(entity.ContentKey, expectedContentKey, StringComparison.Ordinal))
                {
                    yield return new ReleaseValidationIssueDto(
                        $"{entity.EntityType}-content-key",
                        $"Content key for '{entity.EntityType}' must be '{expectedContentKey}'.",
                        entity.ContentKey);
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
    }
}

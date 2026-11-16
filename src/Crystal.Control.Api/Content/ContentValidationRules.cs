using System.Text.Json;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

public static class ContentValidationRules
{
    public static IEnumerable<ReleaseValidationIssueDto> ValidateEntitySemantics(
        IReadOnlyList<ContentEntity> entities)
    {
        foreach (var entity in entities)
        {
            if (entity.Data.ValueKind != JsonValueKind.Object) continue;

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

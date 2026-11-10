namespace Crystal.Content;

public sealed record ContentValidationIssue(string Code, string Message, string? ContentKey = null);

public static class ContentSnapshotValidator
{
    public static IReadOnlyList<ContentValidationIssue> Validate(ContentSnapshot snapshot)
    {
        var issues = new List<ContentValidationIssue>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entity in snapshot.Entities)
        {
            counts[entity.EntityType] = counts.GetValueOrDefault(entity.EntityType) + 1;

            if (entity.Data.ValueKind is not (System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array))
            {
                issues.Add(new ContentValidationIssue(
                    "invalid-data",
                    "Entity data must be an object or array.",
                    entity.ContentKey));
            }
        }

        foreach (var (entityType, expected) in snapshot.Manifest.Counts)
        {
            var actual = counts.GetValueOrDefault(entityType);
            if (actual != expected)
            {
                issues.Add(new ContentValidationIssue(
                    "count-mismatch",
                    $"Entity count mismatch for '{entityType}': manifest={expected}, actual={actual}."));
            }
        }

        foreach (var (entityType, file) in snapshot.Manifest.Entities)
        {
            if (!counts.ContainsKey(entityType))
            {
                issues.Add(new ContentValidationIssue(
                    "missing-entity-file",
                    $"Manifest references '{file.File}' but no entities of type '{entityType}' were loaded."));
            }
        }

        return issues;
    }
}

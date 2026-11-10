using System.Text.Json;
using Crystal.Content;
using Server.MirEnvir;

namespace Crystal.LegacyImporter;

public sealed record LegacyContentExportVerification(
    LegacyBinaryExportResult Output,
    LegacyDatabaseIdentity Identity,
    LegacyDatabaseIdentity? ReloadedIdentity);

public static class LegacyContentExportService
{
    public static LegacyContentExportVerification Export(
        Envir envir,
        IReadOnlyList<ContentEntity> expectedEntities,
        string outputPath,
        bool verify,
        bool force)
    {
        ArgumentNullException.ThrowIfNull(envir);
        ArgumentNullException.ThrowIfNull(expectedEntities);

        var identity = LegacyBinaryExporter.Capture(envir, expectedEntities);
        var output = LegacyBinaryExporter.Write(envir, outputPath, force);

        LegacyDatabaseIdentity? reloadedIdentity = null;
        if (verify)
        {
            using var reloaded = LegacyDatabaseSession.Open(output.Path);
            var reloadedMapped = LegacyContentMapper.Map(reloaded.Envir, reloaded);
            reloadedIdentity = LegacyBinaryExporter.Capture(reloaded.Envir, reloadedMapped.Entities);
            if (!identity.SemanticallyEquals(reloadedIdentity))
            {
                var entityDifference = LegacyBinaryExporter.FindFirstEntityDifference(
                    expectedEntities,
                    reloadedMapped.Entities);
                throw new InvalidDataException(
                    $"Exported database failed semantic reload validation: source={identity.SemanticHash}, " +
                    $"reloaded={reloadedIdentity.SemanticHash}, " +
                    $"entityComparison={entityDifference ?? "none"}, " +
                    $"sourceIndices={JsonSerializer.Serialize(identity.Indices)}, " +
                    $"reloadedIndices={JsonSerializer.Serialize(reloadedIdentity.Indices)}, " +
                    $"sourceListCounts={JsonSerializer.Serialize(identity.ListCounts)}, " +
                    $"reloadedListCounts={JsonSerializer.Serialize(reloadedIdentity.ListCounts)}.");
            }
        }

        return new LegacyContentExportVerification(output, identity, reloadedIdentity);
    }
}

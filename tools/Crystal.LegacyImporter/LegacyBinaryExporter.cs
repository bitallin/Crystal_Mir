using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Crystal.Content;
using Server.MirEnvir;

namespace Crystal.LegacyImporter;

public sealed record LegacyDatabaseIndices(
    int Map,
    int Item,
    int Monster,
    int Npc,
    int Quest,
    int GameShop,
    int Conquest,
    int Respawn);

public sealed record LegacyDatabaseListCounts(
    int Maps,
    int Items,
    int Monsters,
    int Npcs,
    int Quests,
    int Magics,
    int GameShop,
    int Conquests,
    int GtMaps,
    int RespawnTickOptions);

public sealed class LegacyDatabaseIdentity
{
    public required string SemanticHash { get; init; }

    public required IReadOnlyDictionary<string, int> Counts { get; init; }

    public required LegacyDatabaseIndices Indices { get; init; }

    public required LegacyDatabaseListCounts ListCounts { get; init; }

    public bool SemanticallyEquals(LegacyDatabaseIdentity other)
    {
        return SemanticHash == other.SemanticHash &&
               Indices == other.Indices &&
               ListCounts == other.ListCounts;
    }
}

public sealed record LegacyBinaryExportResult(string Path, string Sha256, long SizeBytes);

public static class LegacyBinaryExporter
{
    private const string FingerprintPrefix = "crystal-legacy-database-semantic-v1";

    public static LegacyDatabaseIdentity Capture(Envir envir, IEnumerable<ContentEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(envir);
        ArgumentNullException.ThrowIfNull(entities);

        var ordered = ContentSnapshotWriter.ValidateAndOrder(entities);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, FingerprintPrefix);
        AppendInt(hash, envir.MapIndex);
        AppendInt(hash, envir.ItemIndex);
        AppendInt(hash, envir.MonsterIndex);
        AppendInt(hash, envir.NPCIndex);
        AppendInt(hash, envir.QuestIndex);
        AppendInt(hash, envir.GameshopIndex);
        AppendInt(hash, envir.ConquestIndex);
        AppendInt(hash, envir.RespawnIndex);
        AppendInt(hash, envir.MapInfoList.Count);
        AppendInt(hash, envir.ItemInfoList.Count);
        AppendInt(hash, envir.MonsterInfoList.Count);
        AppendInt(hash, envir.NPCInfoList.Count);
        AppendInt(hash, envir.QuestInfoList.Count);
        AppendInt(hash, envir.MagicInfoList.Count);
        AppendInt(hash, envir.GameShopList.Count);
        AppendInt(hash, envir.ConquestInfoList.Count);
        AppendInt(hash, envir.GTMapList.Count);
        AppendInt(hash, envir.RespawnTick.BaseSpawnRate);
        AppendLong(hash, unchecked((long)envir.RespawnTick.CurrentTickcounter));
        AppendInt(hash, envir.RespawnTick.Respawn.Count);
        foreach (var option in envir.RespawnTick.Respawn)
        {
            AppendInt(hash, option.UserCount);
            AppendLong(hash, BitConverter.DoubleToInt64Bits(option.DelayLoss));
        }

        foreach (var gtMap in envir.GTMapList)
        {
            AppendInt(hash, gtMap.Index);
            AppendInt(hash, gtMap.Key);
            AppendString(hash, gtMap.Name);
            AppendString(hash, gtMap.Owner);
            AppendString(hash, gtMap.Leader);
            AppendString(hash, gtMap.Leader2);
            AppendInt(hash, gtMap.Price);
            AppendInt(hash, gtMap.Days);
            AppendInt(hash, gtMap.Begin);
        }

        foreach (var entity in ordered)
        {
            counts[entity.EntityType] = counts.GetValueOrDefault(entity.EntityType) + 1;
            AppendString(hash, entity.EntityType);
            AppendLong(hash, entity.LocalIndex);
            AppendString(hash, entity.ContentKey);
            AppendInt(hash, entity.SchemaVersion);
            AppendString(hash, ContentJson.SerializeCanonical(entity.Data));
        }

        return new LegacyDatabaseIdentity
        {
            SemanticHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            Counts = counts,
            Indices = new LegacyDatabaseIndices(
                envir.MapIndex,
                envir.ItemIndex,
                envir.MonsterIndex,
                envir.NPCIndex,
                envir.QuestIndex,
                envir.GameshopIndex,
                envir.ConquestIndex,
                envir.RespawnIndex),
            ListCounts = new LegacyDatabaseListCounts(
                envir.MapInfoList.Count,
                envir.ItemInfoList.Count,
                envir.MonsterInfoList.Count,
                envir.NPCInfoList.Count,
                envir.QuestInfoList.Count,
                envir.MagicInfoList.Count,
                envir.GameShopList.Count,
                envir.ConquestInfoList.Count,
                envir.GTMapList.Count,
                envir.RespawnTick.Respawn.Count)
        };
    }

    public static LegacyBinaryExportResult Write(Envir envir, string outputPath, bool force)
    {
        ArgumentNullException.ThrowIfNull(envir);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var fullPath = Path.GetFullPath(outputPath);
        if (Directory.Exists(fullPath))
        {
            throw new IOException($"Export target is a directory: {fullPath}");
        }

        if (!force && File.Exists(fullPath))
        {
            throw new IOException($"Export target already exists: {fullPath}. Use --force to replace it.");
        }

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException($"Export target has no parent directory: {fullPath}");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    WriteDatabase(envir, writer);
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: force);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }

        var file = new FileInfo(fullPath);
        using var exported = File.OpenRead(fullPath);
        var sha256 = Convert.ToHexString(SHA256.HashData(exported)).ToLowerInvariant();
        return new LegacyBinaryExportResult(fullPath, sha256, file.Length);
    }

    public static string? FindFirstEntityDifference(
        IEnumerable<ContentEntity> expected,
        IEnumerable<ContentEntity> actual)
    {
        var left = ContentSnapshotWriter.ValidateAndOrder(expected);
        var right = ContentSnapshotWriter.ValidateAndOrder(actual);

        if (left.Count != right.Count)
        {
            return $"entity count differs: source={left.Count}, reloaded={right.Count}";
        }

        for (var index = 0; index < left.Count; index++)
        {
            var source = left[index];
            var reloaded = right[index];
            if (source.EntityType != reloaded.EntityType ||
                source.LocalIndex != reloaded.LocalIndex ||
                source.ContentKey != reloaded.ContentKey ||
                source.SchemaVersion != reloaded.SchemaVersion)
            {
                return $"entity identity differs at position {index}: " +
                       $"{source.EntityType}/{source.LocalIndex}/{source.ContentKey} vs " +
                       $"{reloaded.EntityType}/{reloaded.LocalIndex}/{reloaded.ContentKey}";
            }

            var sourceData = ContentJson.SerializeCanonical(source.Data);
            var reloadedData = ContentJson.SerializeCanonical(reloaded.Data);
            if (sourceData != reloadedData)
            {
                using var sourceDocument = System.Text.Json.JsonDocument.Parse(sourceData);
                using var reloadedDocument = System.Text.Json.JsonDocument.Parse(reloadedData);
                var difference = FindJsonDifference(
                    sourceDocument.RootElement,
                    reloadedDocument.RootElement,
                    "$");
                return $"entity data differs: {source.EntityType}/{source.ContentKey}; {difference}";
            }
        }

        return null;
    }

    private static string? FindJsonDifference(
        System.Text.Json.JsonElement source,
        System.Text.Json.JsonElement reloaded,
        string path)
    {
        if (source.ValueKind != reloaded.ValueKind)
        {
            return $"{path}: kind {source.ValueKind} -> {reloaded.ValueKind}";
        }

        if (source.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var sourceProperties = source.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            var reloadedProperties = reloaded.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);

            foreach (var name in sourceProperties.Keys.Union(reloadedProperties.Keys).Order(StringComparer.Ordinal))
            {
                if (!sourceProperties.TryGetValue(name, out var sourceValue))
                {
                    return $"{path}.{name}: missing in source";
                }

                if (!reloadedProperties.TryGetValue(name, out var reloadedValue))
                {
                    return $"{path}.{name}: missing after reload";
                }

                var difference = FindJsonDifference(sourceValue, reloadedValue, $"{path}.{name}");
                if (difference is not null) return difference;
            }

            return $"{path}: object values differ";
        }

        if (source.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var sourceItems = source.EnumerateArray().ToArray();
            var reloadedItems = reloaded.EnumerateArray().ToArray();
            if (sourceItems.Length != reloadedItems.Length)
            {
                return $"{path}: array length {sourceItems.Length} -> {reloadedItems.Length}";
            }

            for (var index = 0; index < sourceItems.Length; index++)
            {
                var difference = FindJsonDifference(sourceItems[index], reloadedItems[index], $"{path}[{index}]");
                if (difference is not null) return difference;
            }

            return $"{path}: array values differ";
        }

        return source.GetRawText() == reloaded.GetRawText()
            ? null
            : $"{path}: {source.GetRawText()} -> {reloaded.GetRawText()}";
    }

    private static void WriteDatabase(Envir envir, BinaryWriter writer)
    {
        writer.Write(Envir.Version);
        writer.Write(Envir.CustomVersion);
        writer.Write(envir.MapIndex);
        writer.Write(envir.ItemIndex);
        writer.Write(envir.MonsterIndex);
        writer.Write(envir.NPCIndex);
        writer.Write(envir.QuestIndex);
        writer.Write(envir.GameshopIndex);
        writer.Write(envir.ConquestIndex);
        writer.Write(envir.RespawnIndex);

        writer.Write(envir.MapInfoList.Count);
        foreach (var map in envir.MapInfoList)
        {
            map.Save(writer);
        }

        writer.Write(envir.ItemInfoList.Count);
        foreach (var item in envir.ItemInfoList)
        {
            item.Save(writer);
        }

        writer.Write(envir.MonsterInfoList.Count);
        foreach (var monster in envir.MonsterInfoList)
        {
            monster.Save(writer);
        }

        writer.Write(envir.NPCInfoList.Count);
        foreach (var npc in envir.NPCInfoList)
        {
            npc.Save(writer);
        }

        writer.Write(envir.QuestInfoList.Count);
        foreach (var quest in envir.QuestInfoList)
        {
            quest.Save(writer);
        }

        (envir.DragonInfo
            ?? throw new InvalidOperationException("Legacy database has no dragon content.")).Save(writer);

        writer.Write(envir.MagicInfoList.Count);
        foreach (var magic in envir.MagicInfoList)
        {
            magic.Save(writer);
        }

        writer.Write(envir.GameShopList.Count);
        foreach (var item in envir.GameShopList)
        {
            item.Save(writer);
        }

        writer.Write(envir.ConquestInfoList.Count);
        foreach (var conquest in envir.ConquestInfoList)
        {
            conquest.Save(writer);
        }

        envir.RespawnTick.Save(writer);

        writer.Write(envir.GTMapList.Count);
        foreach (var gtMap in envir.GTMapList)
        {
            gtMap.Save(writer);
        }
    }

    private static void AppendInt(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendLong(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}

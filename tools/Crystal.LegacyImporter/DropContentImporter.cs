using System.Text.Json;
using Crystal.Content;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Crystal.LegacyImporter;

public sealed record DropContentImportResult(
    IReadOnlyList<ContentEntity> Entities,
    int FileCount,
    int DropCount);

public static class DropContentImporter
{
    public static DropContentImportResult Map(Envir envir, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(envir);

        var root = Path.GetFullPath(contentRoot);
        var dropRoot = Path.Combine(root, "Envir", "Drops");
        if (!Directory.Exists(dropRoot))
        {
            throw new DirectoryNotFoundException(
                $"Drop content directory not found: {dropRoot}");
        }

        var entities = new List<ContentEntity>();
        var originalDirectory = Environment.CurrentDirectory;
        var previousItems = Envir.Main.ItemInfoList;
        var fileCount = 0;
        var dropCount = 0;

        try
        {
            Environment.CurrentDirectory = root;
            // DropInfo resolves item names through Envir.Main. Point it at the loaded database so
            // imported drops keep their item references instead of silently losing them.
            Envir.Main.ItemInfoList = envir.ItemInfoList;

            foreach (var monster in envir.MonsterInfoList.OrderBy(monster => monster.Index))
            {
                var configuredPath = string.IsNullOrWhiteSpace(monster.DropPath)
                    ? monster.Name
                    : monster.DropPath;
                var relativePath = ContentPath.Normalize(configuredPath) + ".txt";
                var dropPath = Path.Combine(dropRoot, relativePath);
                if (!File.Exists(dropPath)) continue;

                var drops = new List<DropInfo>();
                DropInfo.Load(drops, monster.Name, dropPath, type: 0, createIfNotExists: false);

                var serializedDrops = drops
                    .Select(drop => LegacyJsonSerializer.Serialize(drop))
                    .ToArray();
                var data = JsonSerializer.SerializeToElement(new
                {
                    monsterIndex = monster.Index,
                    monsterName = monster.Name,
                    dropPath = monster.DropPath,
                    fileName = Path.GetRelativePath(root, dropPath).Replace('\\', '/'),
                    drops = serializedDrops
                }, ContentJson.CompactOptions);

                entities.Add(new ContentEntity
                {
                    EntityType = ContentEntityTypes.Drop,
                    LocalIndex = monster.Index,
                    ContentKey = $"drop.monster.{monster.Index}",
                    Data = ContentJson.Canonicalize(data)
                });

                fileCount++;
                dropCount += drops.Count;
            }
        }
        finally
        {
            Envir.Main.ItemInfoList = previousItems;
            Environment.CurrentDirectory = originalDirectory;
        }

        return new DropContentImportResult(entities, fileCount, dropCount);
    }
}

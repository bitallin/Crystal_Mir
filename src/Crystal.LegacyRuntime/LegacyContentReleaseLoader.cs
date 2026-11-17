using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crystal.Content;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Crystal.LegacyRuntime;

public sealed class LegacyContentReleaseWorkspace : IDisposable
{
    private readonly string _originalDirectory;
    private bool _disposed;

    private LegacyContentReleaseWorkspace(string rootPath, string originalDirectory)
    {
        RootPath = rootPath;
        _originalDirectory = originalDirectory;
    }

    public string RootPath { get; }

    public string DatabasePath => Path.Combine(RootPath, "Server.MirDB");

    public static LegacyContentReleaseWorkspace Create(string contentRoot)
    {
        var sourceRoot = Path.GetFullPath(contentRoot);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"Content root does not exist: {sourceRoot}");
        }

        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"crystal-zone-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(sourceRoot))
            {
                var name = Path.GetFileName(entry);
                if (string.Equals(name, "Server.MirDB", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Envir", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Logs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = Path.Combine(rootPath, name);
                if (Directory.Exists(entry) &&
                    string.Equals(name, "Configs", StringComparison.OrdinalIgnoreCase))
                {
                    CopyDirectory(entry, target);
                }
                else if (Directory.Exists(entry))
                {
                    Directory.CreateSymbolicLink(target, entry);
                }
                else
                {
                    File.Copy(entry, target, overwrite: false);
                }
            }

            var sourceEnvir = Path.Combine(sourceRoot, "Envir");
            var targetEnvir = Path.Combine(rootPath, "Envir");
            Directory.CreateDirectory(targetEnvir);
            if (Directory.Exists(sourceEnvir))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(sourceEnvir))
                {
                    var name = Path.GetFileName(entry);
                    if (IsMaterializedDirectory(name)) continue;

                    var target = Path.Combine(targetEnvir, name);
                    if (Directory.Exists(entry))
                    {
                        Directory.CreateSymbolicLink(target, entry);
                    }
                    else
                    {
                        File.Copy(entry, target, overwrite: false);
                    }
                }
            }

            foreach (var directory in new[] { "Drops", "Recipe", "NPCs", "Quests" })
            {
                Directory.CreateDirectory(Path.Combine(targetEnvir, directory));
            }

            Directory.CreateDirectory(Path.Combine(rootPath, "Logs"));
            return new LegacyContentReleaseWorkspace(rootPath, originalDirectory);
        }
        catch
        {
            TryDelete(rootPath);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (string.Equals(
                Path.GetFullPath(Environment.CurrentDirectory),
                Path.GetFullPath(RootPath),
                StringComparison.Ordinal))
        {
            Environment.CurrentDirectory = _originalDirectory;
        }

        TryDelete(RootPath);
    }

    private static bool IsMaterializedDirectory(string name)
    {
        return name is "Drops" or "Recipe" or "NPCs" or "Quests";
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: false);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

public static class LegacyContentReleaseLoader
{
    public static LegacyContentReleaseWorkspace Prepare(
        string releaseDirectory,
        string contentRoot,
        string publicKeyPath,
        string keyId)
    {
        if (!File.Exists(publicKeyPath))
        {
            throw new FileNotFoundException("Content public key not found.", publicKeyPath);
        }

        var snapshot = ContentSnapshotReader.Read(releaseDirectory);
        var verification = ContentSnapshotSigner.Verify(
            snapshot.Manifest,
            File.ReadAllText(publicKeyPath),
            keyId);
        if (!verification.IsValid)
        {
            throw new CryptographicException(
                $"Content release signature verification failed: {verification.Error}");
        }

        var issues = ContentSnapshotValidator.Validate(snapshot);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                "Content release validation failed: " +
                string.Join("; ", issues.Select(issue => issue.Message)));
        }

        ValidateRuntimeReferences(snapshot, contentRoot);
        var workspace = LegacyContentReleaseWorkspace.Create(contentRoot);
        try
        {
            using (LegacyRebuildEnvironment.Open())
            {
                var envir = LegacyContentRebuilder.Build(snapshot);
                LegacyBinaryExporter.Write(envir, workspace.DatabasePath, force: false);
            }

            LegacyContentFileMaterializer.Materialize(snapshot, workspace.RootPath);
            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static void ValidateRuntimeReferences(ContentSnapshot snapshot, string contentRoot)
    {
        var maps = BuildIndex(snapshot, ContentEntityTypes.Map);
        var items = BuildIndex(snapshot, ContentEntityTypes.Item);
        var monsters = BuildIndex(snapshot, ContentEntityTypes.Monster);
        var npcs = BuildIndex(snapshot, ContentEntityTypes.Npc);
        var quests = BuildIndex(snapshot, ContentEntityTypes.Quest);
        var scriptsByPath = new Dictionary<string, ContentEntity>(StringComparer.OrdinalIgnoreCase);

        foreach (var script in snapshot.Entities.Where(entity => entity.EntityType == ContentEntityTypes.Script))
        {
            var relativePath = ReadString(script.Data, "relativePath");
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                scriptsByPath[NormalizeScriptIdentity(relativePath)] = script;
            }

            var fileName = ReadString(script.Data, "fileName");
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                scriptsByPath[NormalizeScriptIdentity(fileName)] = script;
            }
        }

        var root = Path.GetFullPath(contentRoot);
        foreach (var (entityType, index) in maps)
        {
            var fileName = ReadString(index.Data, "FileName");
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidDataException($"Map {entityType} has an empty FileName.");
            }

            var mapFile = fileName.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : fileName + ".map";
            var mapsRoot = Path.Combine(root, "Maps");
            var path = ResolveChildPath(mapsRoot, mapFile);
            if (!File.Exists(path) && Directory.Exists(mapsRoot))
            {
                path = Directory.EnumerateFiles(mapsRoot)
                    .FirstOrDefault(candidate => string.Equals(
                        Path.GetFileName(candidate),
                        Path.GetFileName(mapFile),
                        StringComparison.OrdinalIgnoreCase)) ?? path;
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Map file for map {fileName} was not found in the content root.",
                    path);
            }
        }

        foreach (var npc in npcs.Values)
        {
            var mapIndex = ReadLong(npc.Data, "MapIndex");
            if (mapIndex is > 0 && !maps.ContainsKey(mapIndex.Value))
            {
                throw new InvalidDataException($"NPC {npc.ContentKey} references missing map {mapIndex}.");
            }

            var scriptName = ReadString(npc.Data, "FileName");
            if (!string.IsNullOrWhiteSpace(scriptName) &&
                !scriptsByPath.ContainsKey(NormalizeScriptIdentity(scriptName)))
            {
                throw new InvalidDataException(
                    $"NPC {npc.ContentKey} references missing script '{scriptName}'.");
            }
        }

        foreach (var drop in snapshot.Entities.Where(entity => entity.EntityType == ContentEntityTypes.Drop))
        {
            var monsterIndex = ReadLong(drop.Data, "monsterIndex");
            if (monsterIndex is not > 0 || !monsters.ContainsKey(monsterIndex.Value))
            {
                throw new InvalidDataException(
                    $"Drop {drop.ContentKey} references missing monster {monsterIndex}.");
            }

            foreach (var itemIndex in EnumerateDropItemReferences(drop.Data))
            {
                if (!items.ContainsKey(itemIndex))
                {
                    throw new InvalidDataException(
                        $"Drop {drop.ContentKey} references missing item {itemIndex}.");
                }
            }
        }

        foreach (var recipe in snapshot.Entities.Where(entity => entity.EntityType == ContentEntityTypes.Recipe))
        {
            if (!TryReadReferenceIndex(recipe.Data, "output", out var outputIndex) || !items.ContainsKey(outputIndex))
            {
                throw new InvalidDataException(
                    $"Recipe {recipe.ContentKey} references a missing output item.");
            }

            foreach (var field in new[] { "ingredients", "tools" })
            {
                if (!recipe.Data.TryGetProperty(field, out var values) || values.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var value in values.EnumerateArray())
                {
                    if (TryReadReferenceIndex(value, null, out var itemIndex) && !items.ContainsKey(itemIndex))
                    {
                        throw new InvalidDataException(
                            $"Recipe {recipe.ContentKey} references missing item {itemIndex}.");
                    }
                }
            }
        }

        foreach (var quest in quests.Values)
        {
            foreach (var field in new[] { "NpcIndex", "FinishNpcIndex" })
            {
                var npcIndex = ReadLong(quest.Data, field);
                if (npcIndex is > 0 && !npcs.ContainsKey(npcIndex.Value))
                {
                    throw new InvalidDataException(
                        $"Quest {quest.ContentKey} references missing NPC {npcIndex}.");
                }
            }

            foreach (var field in new[] { "KillTasks", "ItemTasks", "CarryItems", "FixedRewards", "SelectRewards" })
            {
                if (!quest.Data.TryGetProperty(field, out var values) || values.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var value in values.EnumerateArray())
                {
                    var referenceType = field == "KillTasks" ? "Monster" : "Item";
                    if (!TryReadReferenceIndex(value, referenceType, out var referenceIndex)) continue;
                    var exists = field == "KillTasks"
                        ? monsters.ContainsKey(referenceIndex)
                        : items.ContainsKey(referenceIndex);
                    if (!exists)
                    {
                        throw new InvalidDataException(
                            $"Quest {quest.ContentKey} references missing {referenceType.ToLowerInvariant()} {referenceIndex}.");
                    }
                }
            }
        }
    }

    private static Dictionary<long, ContentEntity> BuildIndex(ContentSnapshot snapshot, string entityType)
    {
        return snapshot.Entities
            .Where(entity => entity.EntityType == entityType)
            .ToDictionary(entity => entity.LocalIndex);
    }

    private static string NormalizeScriptIdentity(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        const string prefix = "Envir/NPCs/";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[prefix.Length..];
        }
        else if (normalized.StartsWith("NPCs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["NPCs/".Length..];
        }

        if (normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.Trim('/');
    }

    private static IEnumerable<long> EnumerateDropItemReferences(JsonElement data)
    {
        if (!data.TryGetProperty("drops", out var drops) || drops.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var drop in drops.EnumerateArray())
        {
            foreach (var index in EnumerateDropItemReferences(drop))
            {
                yield return index;
            }
        }
    }

    private static bool TryReadReferenceIndex(JsonElement value, string? propertyName, out long index)
    {
        index = 0;
        var reference = value;
        if (propertyName is not null)
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty(propertyName, out reference))
            {
                return false;
            }
        }

        if (reference.ValueKind != JsonValueKind.Object) return false;
        if (reference.TryGetProperty("index", out var element) && element.TryGetInt64(out index))
        {
            return index > 0;
        }

        return reference.TryGetProperty("itemIndex", out element) &&
               element.TryGetInt64(out index) &&
               index > 0;
    }

    private static long? ReadLong(JsonElement data, string name)
    {
        return data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty(name, out var value) &&
               value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static string ReadString(JsonElement data, string name)
    {
        return data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    internal static string ResolveChildPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal) &&
            !string.Equals(candidate, fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Content path escapes its root: {relativePath}");
        }

        return candidate;
    }
}

public static class LegacyContentFileMaterializer
{
    public static void Materialize(ContentSnapshot snapshot, string runtimeRoot)
    {
        var root = Path.GetFullPath(runtimeRoot);
        var items = snapshot.Entities
            .Where(entity => entity.EntityType == ContentEntityTypes.Item)
            .ToDictionary(entity => entity.LocalIndex, entity => ContentData.ReadString(entity.Data, "Name"));
        var monsters = snapshot.Entities
            .Where(entity => entity.EntityType == ContentEntityTypes.Monster)
            .ToDictionary(entity => entity.LocalIndex, entity => ContentData.ReadString(entity.Data, "Name"));

        var writtenPaths = new HashSet<string>(StringComparer.Ordinal);
        var dropSignatures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in snapshot.Entities.OrderBy(entity => entity.EntityType, StringComparer.Ordinal)
                     .ThenBy(entity => entity.LocalIndex))
        {
            switch (entity.EntityType)
            {
                case ContentEntityTypes.Drop:
                    MaterializeDrop(entity, root, items, monsters, writtenPaths, dropSignatures);
                    break;
                case ContentEntityTypes.Recipe:
                    MaterializeRecipe(entity, root, writtenPaths);
                    break;
                case ContentEntityTypes.Script:
                    MaterializeScript(entity, root, writtenPaths);
                    break;
                case ContentEntityTypes.Quest:
                    MaterializeQuest(entity, root, items, monsters, writtenPaths);
                    break;
            }
        }
    }

    private static void MaterializeDrop(
        ContentEntity entity,
        string root,
        IReadOnlyDictionary<long, string> items,
        IReadOnlyDictionary<long, string> monsters,
        ISet<string> writtenPaths,
        IDictionary<string, string> dropSignatures)
    {
        var fileName = ContentData.ReadString(entity.Data, "fileName");
        var dropPath = ContentData.ReadString(entity.Data, "dropPath");
        var monsterIndex = ContentData.ReadLong(entity.Data, "monsterIndex") ?? entity.LocalIndex;
        var relative = !string.IsNullOrWhiteSpace(fileName)
            ? fileName.Replace('\\', '/')
            : $"Envir/Drops/{(string.IsNullOrWhiteSpace(dropPath) ? monsters.GetValueOrDefault(monsterIndex, entity.ContentKey) : dropPath.Replace('\\', '/'))}.txt";
        if (!entity.Data.TryGetProperty("drops", out var drops) || drops.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Drop {entity.ContentKey} has no drops array.");
        }

        var signature = ContentJson.Sha256Hex(Encoding.UTF8.GetBytes(ContentJson.SerializeCanonical(drops)));
        var path = LegacyContentReleaseLoader.ResolveChildPath(root, relative.Replace('\\', '/').TrimStart('/'));
        if (dropSignatures.TryGetValue(path, out var existingSignature))
        {
            if (!string.Equals(existingSignature, signature, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Multiple release drop tables materialize to {relative} with different contents.");
            }

            return;
        }

        dropSignatures[path] = signature;
        if (!writtenPaths.Add(path))
        {
            throw new InvalidDataException($"Multiple release entities materialize to the same file: {relative}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = new List<string>
        {
            $"; Generated from signed content release: {entity.ContentKey}",
            string.Empty
        };

        foreach (var drop in drops.EnumerateArray())
        {
            WriteDrop(lines, drop, items, 0);
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteDrop(
        ICollection<string> lines,
        JsonElement drop,
        IReadOnlyDictionary<long, string> items,
        int indent)
    {
        var padding = new string(' ', indent * 2);
        var chance = ContentData.ReadLong(drop, "Chance") ?? 0;
        if (chance < 0)
        {
            throw new InvalidDataException("Drop chance must be non-negative.");
        }

        if (drop.TryGetProperty("GroupedDrop", out var group) && group.ValueKind == JsonValueKind.Object)
        {
            var marker = "GROUP";
            if (ContentData.ReadBool(group, "Random")) marker += "*";
            if (ContentData.ReadBool(group, "First")) marker += "^";
            lines.Add($"{padding}1/{chance} {marker}");
            lines.Add($"{padding}{{");
            if (group.TryGetProperty("Items", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray())
                {
                    WriteDrop(lines, child, items, indent + 1);
                }
            }

            lines.Add($"{padding}}}");
            return;
        }

        var gold = ContentData.ReadLong(drop, "Gold") ?? 0;
        if (gold > 0)
        {
            lines.Add($"{padding}1/{chance} Gold {gold}");
            return;
        }

        if (!TryReadReferenceIndex(drop, "Item", out var itemIndex) ||
            !items.TryGetValue(itemIndex, out var itemName) ||
            string.IsNullOrWhiteSpace(itemName))
        {
            throw new InvalidDataException($"Drop entry references missing item {itemIndex}.");
        }

        var suffix = ContentData.ReadBool(drop, "QuestRequired") ? " Q" : string.Empty;
        lines.Add($"{padding}1/{chance} {itemName}{suffix}");
    }

    private static void MaterializeRecipe(
        ContentEntity entity,
        string root,
        ISet<string> writtenPaths)
    {
        var name = ContentData.ReadString(entity.Data, "name");
        var fileName = ContentData.ReadString(entity.Data, "fileName");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException($"Recipe {entity.ContentKey} has an empty name.");
        }

        var output = entity.Data.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Object
            ? outputElement
            : throw new InvalidDataException($"Recipe {entity.ContentKey} has no output item.");
        var outputName = ContentData.ReadString(output, "itemName");
        if (!string.Equals(outputName, name, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Recipe {entity.ContentKey} file identity '{name}' does not match output item '{outputName}'.");
        }

        var relative = string.IsNullOrWhiteSpace(fileName) ? $"Envir/Recipe/{name}.txt" : fileName;
        var path = PreparePath(root, relative, writtenPaths);
        var lines = new List<string>
        {
            "[Recipe]",
            $"Amount {Math.Max(1, ContentData.ReadLong(output, "count") ?? 1)}",
            $"Chance {Math.Clamp(ContentData.ReadLong(entity.Data, "chance") ?? 100, 0, 100)}",
            $"Gold {Math.Max(0, ContentData.ReadLong(entity.Data, "gold") ?? 0)}",
            string.Empty,
            "[Tools]"
        };
        WriteRecipeItems(lines, entity.Data, "tools");
        lines.Add(string.Empty);
        lines.Add("[Ingredients]");
        WriteRecipeItems(lines, entity.Data, "ingredients");
        lines.Add(string.Empty);
        lines.Add("[Criteria]");

        if (ContentData.ReadLong(entity.Data, "requiredLevel") is > 0 and var requiredLevel)
        {
            lines.Add($"Level {requiredLevel}");
        }

        WriteStringCriteria(lines, entity.Data, "requiredClasses", "Class");
        if (ContentData.ReadString(entity.Data, "requiredGender") is { Length: > 0 } gender)
        {
            lines.Add($"Gender {gender}");
        }

        WriteNumberCriteria(lines, entity.Data, "requiredFlags", "Flag");
        WriteNumberCriteria(lines, entity.Data, "requiredQuests", "Quest");
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteRecipeItems(ICollection<string> lines, JsonElement data, string property)
    {
        if (!data.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return;
        foreach (var value in values.EnumerateArray())
        {
            var name = ContentData.ReadString(value, "itemName");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException($"Recipe {property} entry has no item name.");
            }

            var count = ContentData.ReadLong(value, "count") ?? 1;
            var durability = ContentData.ReadLong(value, "currentDura") ?? 0;
            lines.Add(durability > 0
                ? $"{name} {count} {durability}"
                : $"{name} {count}");
        }
    }

    private static void WriteStringCriteria(ICollection<string> lines, JsonElement data, string property, string keyword)
    {
        if (!data.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text)
            {
                lines.Add($"{keyword} {text}");
            }
        }
    }

    private static void WriteNumberCriteria(ICollection<string> lines, JsonElement data, string property, string keyword)
    {
        if (!data.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return;
        foreach (var value in values.EnumerateArray())
        {
            if (value.TryGetInt64(out var number))
            {
                lines.Add($"{keyword} {number}");
            }
        }
    }

    private static void MaterializeScript(
        ContentEntity entity,
        string root,
        ISet<string> writtenPaths)
    {
        var relativePath = ContentData.ReadString(entity.Data, "relativePath");
        var fileName = ContentData.ReadString(entity.Data, "fileName");
        var pathPart = !string.IsNullOrWhiteSpace(relativePath) ? relativePath : fileName;
        pathPart = pathPart.Replace('\\', '/').Trim();
        const string prefix = "Envir/NPCs/";
        if (pathPart.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            pathPart = pathPart[prefix.Length..];
        }
        else if (pathPart.StartsWith("NPCs/", StringComparison.OrdinalIgnoreCase))
        {
            pathPart = pathPart["NPCs/".Length..];
        }

        if (pathPart.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            pathPart = pathPart[..^4];
        }

        var source = ContentData.ReadString(entity.Data, "source");
        var expectedHash = ContentData.ReadString(entity.Data, "sha256");
        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(expectedHash) &&
            !string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Script {entity.ContentKey} source hash does not match its sha256 field.");
        }

        var path = PreparePath(root, $"Envir/NPCs/{pathPart}.txt", writtenPaths);
        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void MaterializeQuest(
        ContentEntity entity,
        string root,
        IReadOnlyDictionary<long, string> items,
        IReadOnlyDictionary<long, string> monsters,
        ISet<string> writtenPaths)
    {
        var fileName = ContentData.ReadString(entity.Data, "FileName");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidDataException($"Quest {entity.ContentKey} has an empty FileName.");
        }

        var path = PreparePath(root, $"Envir/Quests/{fileName.Replace('\\', '/')}.txt", writtenPaths);
        var lines = new List<string>();
        WriteTextSection(lines, entity.Data, "Description", "@Description");
        WriteTextSection(lines, entity.Data, "TaskDescription", "@TaskDescription");
        WriteTextSection(lines, entity.Data, "CompletionDescription", "@Completion");
        WriteTextSection(lines, entity.Data, "ReturnDescription", "@ReturnDescription");
        WriteTaskSection(lines, entity.Data, "CarryItems", "@CarryItems", items, "Item");
        WriteTaskSection(lines, entity.Data, "KillTasks", "@KillTasks", monsters, "Monster");
        WriteTaskSection(lines, entity.Data, "ItemTasks", "@ItemTasks", items, "Item");
        WriteFlagSection(lines, entity.Data, "FlagTasks");
        WriteTaskSection(lines, entity.Data, "FixedRewards", "@FixedRewards", items, "Item");
        WriteTaskSection(lines, entity.Data, "SelectRewards", "@SelectRewards", items, "Item");
        WriteNumberSection(lines, entity.Data, "ExpReward", "@ExpReward");
        WriteNumberSection(lines, entity.Data, "GoldReward", "@GoldReward");
        WriteNumberSection(lines, entity.Data, "CreditReward", "@CreditReward");
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteTextSection(ICollection<string> lines, JsonElement data, string property, string header)
    {
        lines.Add($"[{header}]");
        if (data.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text)
                {
                    lines.Add(text.StartsWith('[') ? " " + text : text);
                }
            }
        }

        lines.Add(string.Empty);
    }

    private static void WriteTaskSection(
        ICollection<string> lines,
        JsonElement data,
        string property,
        string header,
        IReadOnlyDictionary<long, string> names,
        string referenceProperty)
    {
        lines.Add($"[{header}]");
        if (data.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
            {
                if (!TryReadReferenceIndex(value, referenceProperty, out var index) ||
                    !names.TryGetValue(index, out var name) ||
                    string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidDataException($"Quest {header} entry references missing index {index}.");
                }

                var count = Math.Max(1, ContentData.ReadLong(value, "Count") ?? 1);
                var message = ContentData.ReadString(value, "Message").Replace('"', '\'');
                lines.Add(string.IsNullOrWhiteSpace(message)
                    ? $"{name} {count}"
                    : $"{name} {count} \"{message}\"");
            }
        }

        lines.Add(string.Empty);
    }

    private static void WriteFlagSection(ICollection<string> lines, JsonElement data, string property)
    {
        lines.Add("[@FlagTasks]");
        if (data.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
            {
                var number = ContentData.ReadLong(value, "Number") ?? 0;
                var message = ContentData.ReadString(value, "Message").Replace('"', '\'');
                lines.Add(string.IsNullOrWhiteSpace(message)
                    ? number.ToString()
                    : $"{number} \"{message}\"");
            }
        }

        lines.Add(string.Empty);
    }

    private static void WriteNumberSection(ICollection<string> lines, JsonElement data, string property, string header)
    {
        lines.Add($"[{header}]");
        lines.Add((ContentData.ReadLong(data, property) ?? 0).ToString());
        lines.Add(string.Empty);
    }

    private static string PreparePath(string root, string relativePath, ISet<string> writtenPaths)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var path = LegacyContentReleaseLoader.ResolveChildPath(root, normalized);
        if (!writtenPaths.Add(path))
        {
            throw new InvalidDataException($"Multiple release entities materialize to the same file: {normalized}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static bool TryReadReferenceIndex(JsonElement value, string? propertyName, out long index)
    {
        index = 0;
        var reference = value;
        if (propertyName is not null &&
            (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out reference)))
        {
            return false;
        }

        if (reference.ValueKind != JsonValueKind.Object) return false;
        if (reference.TryGetProperty("index", out var element) && element.TryGetInt64(out index))
        {
            return index > 0;
        }

        return reference.TryGetProperty("itemIndex", out element) &&
               element.TryGetInt64(out index) &&
               index > 0;
    }
}

internal static class ContentData
{
    public static string ReadString(JsonElement data, string property)
    {
        return data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    public static long? ReadLong(JsonElement data, string property)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number)) return number;
        return null;
    }

    public static bool ReadBool(JsonElement data, string property)
    {
        return data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }
}

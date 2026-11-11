using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Crystal.Content;
using Server.MirEnvir;

namespace Crystal.LegacyImporter;

public sealed record ScriptContentImportResult(
    IReadOnlyList<ContentEntity> Entities,
    int FileCount,
    int ReferencedNpcCount,
    int ErrorCount,
    int WarningCount);

public static class ScriptContentImporter
{
    public static ScriptContentImportResult Map(Envir envir, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(envir);

        var root = Path.GetFullPath(contentRoot);
        var scriptRoot = Path.Combine(root, "Envir", "NPCs");
        if (!Directory.Exists(scriptRoot))
        {
            return new ScriptContentImportResult([], 0, 0, 0, 0);
        }

        var npcIndexesByFileName = envir.NPCInfoList
            .Where(npc => !string.IsNullOrWhiteSpace(npc.FileName))
            .GroupBy(npc => NormalizeScriptFileName(npc.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(npc => npc.Index).Order().ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var files = Directory
            .GetFiles(scriptRoot, "*.txt", SearchOption.AllDirectories)
            .Select(path =>
            {
                var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
                return new ScriptFile(
                    path,
                    relativePath,
                    StripScriptRoot(relativePath));
            })
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var entities = new List<ContentEntity>(files.Length);
        var contentKeys = new HashSet<string>(StringComparer.Ordinal);
        var referencedNpcIndexes = new HashSet<int>();
        var localIndex = 0L;
        var errorCount = 0;
        var warningCount = 0;

        foreach (var file in files)
        {
            var source = NormalizeNewlines(File.ReadAllText(file.Path, Encoding.UTF8));
            var bytes = Encoding.UTF8.GetBytes(source);
            var sha256 = ContentJson.Sha256Hex(bytes);
            var parsed = NpcScriptParser.Parse(source);
            errorCount += parsed.ErrorCount;
            warningCount += parsed.WarningCount;
            var includes = parsed.Includes
                .Select(directive => new ScriptIncludeData(
                    directive.Target,
                    directive.Argument ?? string.Empty))
                .ToArray();
            var inserts = parsed.Inserts
                .Select(directive => directive.Target)
                .ToArray();
            var npcIndexes = npcIndexesByFileName.GetValueOrDefault(file.FileName) ?? [];
            referencedNpcIndexes.UnionWith(npcIndexes);

            var baseContentKey = $"script.{ToContentKeySegment(file.FileName)}";
            var contentKey = MakeUniqueContentKey(baseContentKey, file.RelativePath, contentKeys);
            var data = new ScriptContentData(
                file.FileName,
                file.FileName.ToLowerInvariant(),
                file.RelativePath,
                "CrystalNpc",
                source,
                sha256,
                bytes.Length,
                parsed.Ast.LineCount,
                parsed.PageKeys,
                parsed.Ast,
                new ScriptValidationData(
                    parsed.IsValid,
                    parsed.ErrorCount,
                    parsed.WarningCount,
                    parsed.Ast.Pages.Count,
                    parsed.Ast.StatementCount,
                    parsed.CompiledSha256,
                    parsed.Issues),
                includes,
                inserts,
                parsed.Calls,
                npcIndexes);

            entities.Add(new ContentEntity
            {
                EntityType = ContentEntityTypes.Script,
                LocalIndex = ++localIndex,
                ContentKey = contentKey,
                Data = ContentJson.Canonicalize(
                    JsonSerializer.SerializeToElement(data, ContentJson.CompactOptions))
            });
        }

        return new ScriptContentImportResult(
            entities,
            files.Length,
            referencedNpcIndexes.Count,
            errorCount,
            warningCount);
    }

    private static string StripScriptRoot(string relativePath)
    {
        const string prefix = "Envir/NPCs/";
        if (!relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"NPC script path is outside Envir/NPCs: {relativePath}");
        }

        return relativePath[prefix.Length..^".txt".Length].Replace('\\', '/');
    }

    private static string NormalizeScriptFileName(string fileName)
    {
        return fileName.Trim().Replace('\\', '/').Trim('/');
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string ToContentKeySegment(string fileName)
    {
        var segments = NormalizeScriptFileName(fileName)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Regex.Replace(segment, @"[^A-Za-z0-9_-]+", "_"))
            .Select(segment => segment.Trim('_'))
            .Where(segment => segment.Length > 0);
        var key = string.Join(".", segments);
        return key.Length == 0 ? "script" : key;
    }

    private static string MakeUniqueContentKey(
        string baseContentKey,
        string relativePath,
        ISet<string> existing)
    {
        if (existing.Add(baseContentKey))
        {
            return baseContentKey;
        }

        var suffix = ContentJson.Sha256Hex(Encoding.UTF8.GetBytes(relativePath))[..12];
        var key = $"{baseContentKey}.{suffix}";
        if (!existing.Add(key))
        {
            throw new InvalidDataException($"Duplicate script content key for {relativePath}.");
        }

        return key;
    }

    private sealed record ScriptFile(
        string Path,
        string RelativePath,
        string FileName);

    private sealed record ScriptContentData(
        string FileName,
        string FileNameKey,
        string RelativePath,
        string Language,
        string Source,
        string Sha256,
        int SizeBytes,
        int LineCount,
        IReadOnlyList<string> Pages,
        NpcScriptAst Ast,
        ScriptValidationData Validation,
        IReadOnlyList<ScriptIncludeData> Includes,
        IReadOnlyList<string> Inserts,
        IReadOnlyList<string> Calls,
        IReadOnlyList<int> NpcIndexes);

    private sealed record ScriptIncludeData(string Path, string Page);

    private sealed record ScriptValidationData(
        bool Valid,
        int ErrorCount,
        int WarningCount,
        int PageCount,
        int StatementCount,
        string CompiledSha256,
        IReadOnlyList<NpcScriptValidationIssue> Issues);
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

public static class ContentScriptEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "script";

    private static readonly IReadOnlyList<ContentEditorFieldDto> Fields =
    [
        ContentEditorValidation.Field("fileName", "脚本文件名", "string", readOnly: true),
        ContentEditorValidation.Field("fileNameKey", "脚本键", "string", readOnly: true),
        ContentEditorValidation.Field("relativePath", "相对路径", "string", required: true, defaultValue: ""),
        ContentEditorValidation.Field("language", "脚本语言", "string", required: true, defaultValue: "CrystalNpc"),
        ContentEditorValidation.Field("source", "脚本源码", "code", required: true, multiline: true, defaultValue: ""),
        ContentEditorValidation.Field("sha256", "源码 SHA256", "readonly"),
        ContentEditorValidation.Field("sizeBytes", "字节数", "readonly"),
        ContentEditorValidation.Field("lineCount", "行数", "readonly"),
        ContentEditorValidation.Field("pages", "页面", "readonly"),
        ContentEditorValidation.Field("ast", "AST", "readonly"),
        ContentEditorValidation.Field("validation", "校验结果", "readonly"),
        ContentEditorValidation.Field("includes", "INCLUDE", "readonly"),
        ContentEditorValidation.Field("inserts", "INSERT", "readonly"),
        ContentEditorValidation.Field("calls", "CALL", "readonly"),
        ContentEditorValidation.Field("npcIndexes", "引用 NPC", "readonly")
    ];

    public static ContentEditorSchemaDto Schema { get; } = new(
        EntityType,
        SchemaVersion,
        "NPC 脚本",
        "以文本源码为事实来源；保存时由服务端重新解析页面、语句、引用、SHA256 和编译校验。",
        [
            new ContentEditorSectionDto("identity", "文件身份", "脚本路径和语言。", Fields.Take(4).ToArray()),
            new ContentEditorSectionDto("source", "源码", "文本源码与服务端派生摘要。", Fields.Skip(4).Take(4).ToArray()),
            new ContentEditorSectionDto("derived", "只读结构", "页面、AST、引用和问题列表。", Fields.Skip(8).ToArray())
        ]);

    public static Dictionary<string, string[]> ValidateStructure(JsonElement data, long localIndex, string? contentKey)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (data.ValueKind != JsonValueKind.Object)
        {
            errors["data"] = ["data must be a JSON object."];
            return errors;
        }

        ContentEditorValidation.ValidateFields(data, Fields, errors);

        if (!string.IsNullOrWhiteSpace(contentKey))
        {
            var identity = ResolveIdentity(data, localIndex);
            if (!string.Equals(contentKey, identity.ContentKey, StringComparison.Ordinal) &&
                !string.Equals(contentKey, LegacyContentKey(data, localIndex), StringComparison.Ordinal))
            {
                errors["contentKey"] = [$"contentKey must be '{identity.ContentKey}'."];
            }
        }

        return errors;
    }

    public static IEnumerable<string> ValidateCompleteness(JsonElement data)
    {
        foreach (var issue in ContentEditorValidation.ValidateRequiredFields(data, Fields))
        {
            yield return issue;
        }

        if (ContentEditorValidation.ReadString(data, "source") is { Length: 0 })
        {
            yield return "Script source must not be empty.";
        }

        if (ContentEditorValidation.ReadString(data, "relativePath") is { } path && string.IsNullOrWhiteSpace(path))
        {
            yield return "Script relative path must not be empty.";
        }
    }

    public static ContentEditorIdentity ResolveIdentity(JsonElement data, long nextLocalIndex)
    {
        var relativePath = ContentEditorValidation.ReadString(data, "relativePath")
                           ?? ContentEditorValidation.ReadString(data, "fileName")
                           ?? string.Empty;
        var keyPath = ContentEditorJson.NormalizeScriptPath(relativePath);
        var safePath = string.Join('.', keyPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => ContentEditorJson.SafeKeySegment(segment, "script")));
        return new ContentEditorIdentity(
            nextLocalIndex,
            $"{EntityType}.{(string.IsNullOrWhiteSpace(safePath) ? nextLocalIndex.ToString() : safePath)}");
    }

    public static JsonElement Normalize(JsonElement data)
    {
        return ContentEditorJson.Normalize(data, node =>
        {
            var relativePath = ContentEditorJson.String(node, "relativePath")
                               ?? ContentEditorJson.String(node, "fileName")
                               ?? string.Empty;
            var path = ContentEditorJson.NormalizeScriptPath(relativePath);
            var fileName = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
            var language = ContentEditorJson.String(node, "language");
            if (string.IsNullOrWhiteSpace(language))
            {
                language = "CrystalNpc";
            }

            var source = ContentEditorJson.String(node, "source") ?? string.Empty;
            var parsed = NpcScriptParser.Parse(source, language);

            node["relativePath"] = path;
            node["fileName"] = fileName;
            node["fileNameKey"] = path.ToLowerInvariant();
            node["language"] = language;
            node["source"] = source;
            node["sha256"] = ContentJson.Sha256Hex(source);
            node["sizeBytes"] = Encoding.UTF8.GetByteCount(source);
            node["lineCount"] = source.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n').Length;
            node["pages"] = new JsonArray(parsed.PageKeys.Select(page => JsonValue.Create(page)).ToArray());
            node["ast"] = JsonSerializer.SerializeToNode(parsed.Ast, ContentJson.CompactOptions);
            node["includes"] = new JsonArray(parsed.Includes.Select(include => (JsonNode)new JsonObject
            {
                ["path"] = include.Target?.Replace('\\', '/'),
                ["page"] = include.Argument
            }).ToArray());
            node["inserts"] = new JsonArray(parsed.Inserts
                .Select(insert => JsonValue.Create(insert.Target?.Replace('\\', '/')))
                .ToArray());
            node["calls"] = new JsonArray(parsed.Calls.Select(call => JsonValue.Create(call)).ToArray());
            node["validation"] = new JsonObject
            {
                ["valid"] = parsed.Issues.All(issue => issue.Severity != "error"),
                ["errorCount"] = parsed.Issues.Count(issue => issue.Severity == "error"),
                ["warningCount"] = parsed.Issues.Count(issue => issue.Severity == "warning"),
                ["pageCount"] = parsed.PageKeys.Count,
                ["statementCount"] = parsed.Ast.StatementCount,
                ["compiledSha256"] = parsed.CompiledSha256,
                ["issues"] = new JsonArray(parsed.Issues.Select(issue => (JsonNode)new JsonObject
                {
                    ["code"] = issue.Code,
                    ["severity"] = issue.Severity,
                    ["message"] = issue.Message,
                    ["line"] = issue.Line,
                    ["page"] = issue.Page
                }).ToArray())
            };
        });
    }

    public static IEnumerable<ContentEditorReferenceValue> GetReferences(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("includes", out var includes) ||
            includes.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        var index = 0;
        foreach (var include in includes.EnumerateArray())
        {
            if (include.ValueKind == JsonValueKind.Object &&
                ContentEditorValidation.ReadString(include, "path") is { Length: > 0 } path)
            {
                yield return new ContentEditorReferenceValue(
                    $"includes[{index}].path",
                    EntityType,
                    0,
                    Required: false,
                    Key: path.Replace('\\', '/'));
            }

            index++;
        }
    }

    public static string LegacyContentKeyForFileName(string fileName)
    {
        var normalized = fileName.Trim().Replace('\\', '/').Trim('/');
        if (normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        const string scriptRoot = "Envir/NPCs/";
        if (normalized.StartsWith(scriptRoot, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[scriptRoot.Length..];
        }

        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Regex.Replace(segment, @"[^A-Za-z0-9_-]+", "_").Trim('_'))
            .Where(segment => segment.Length > 0)
            .ToArray();
        return segments.Length == 0 ? "script.script" : $"script.{string.Join('.', segments)}";
    }

    private static string LegacyContentKey(JsonElement data, long localIndex)
    {
        var fileName = ContentEditorValidation.ReadString(data, "fileName")
                       ?? ContentEditorValidation.ReadString(data, "relativePath")
                       ?? localIndex.ToString();
        return LegacyContentKeyForFileName(fileName);
    }
}

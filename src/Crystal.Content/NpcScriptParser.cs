using System.Text.Json;
using System.Text.RegularExpressions;

namespace Crystal.Content;

public static class NpcScriptParser
{
    private static readonly Regex PagePattern = new(
        @"^\s*\[([^\]]+)\]\s*$",
        RegexOptions.Compiled);

    private static readonly Regex IncludePattern = new(
        @"^\s*#INCLUDE\s*\[([^\]]+)\]\s*(.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InsertPattern = new(
        @"^\s*#INSERT\s*\[([^\]]+)\]\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BlockPattern = new(
        @"^\s*#(IF|SAY|ACT|ELSESAY|ELSEACT)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ButtonTargetPattern = new(
        @"<[^<>]*?\/(@[A-Za-z0-9_-]+)",
        RegexOptions.Compiled);

    private static readonly Regex CommandTargetPattern = new(
        @"^\s*(GOTO|GROUPGOTO|CALL)\s+(?:""([^""]+)""|([^\s]+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> BuiltInPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "@MAIN", "@BUY", "@SELL", "@BUYSELL", "@REPAIR", "@SREPAIR", "@REFINE",
        "@REFINECHECK", "@REFINECOLLECT", "@REPLACEWEDDINGRING", "@BUYBACK",
        "@STORAGE", "@CONSIGN", "@MARKET", "@CRAFT", "@CREATEGUILD", "@REQUESTWAR",
        "@SENDPARCEL", "@COLLECTPARCEL", "@AWAKENING", "@DISASSEMBLE", "@DOWNGRADE",
        "@RESET", "@PEARLBUY", "@BUYUSED", "@BUYNEW", "@BUYSELLNEW", "@CREATEHERO",
        "@MANAGEHERO", "@GUILDTERRITORY", "@EXIT"
    };

    public static NpcScriptParseResult Parse(string source, string language = "CrystalNpc")
    {
        ArgumentNullException.ThrowIfNull(source);

        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (normalized.Length > 0 && normalized[0] == '\uFEFF')
        {
            normalized = normalized[1..];
        }

        var lines = normalized.Split('\n');
        var pages = new List<NpcScriptPageNode>();
        var directives = new List<NpcScriptDirectiveNode>();
        var issues = new List<NpcScriptValidationIssue>();
        var pageBuilder = (NpcScriptPageBuilder?)null;
        var currentBlock = (NpcScriptBlockBuilder?)null;
        var inIfBlock = false;
        var statementCount = 0;
        var seenPageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var lineNumber = lineIndex + 1;
            var line = lines[lineIndex];

            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(';'))
            {
                if (currentBlock is { Kind: "say" or "elsesay" })
                {
                    AddTextStatement(currentBlock, lineNumber, line);
                }

                continue;
            }

            var include = IncludePattern.Match(line);
            if (include.Success)
            {
                var directive = new NpcScriptDirectiveNode(
                    "include",
                    include.Groups[1].Value.Trim(),
                    NullIfEmpty(include.Groups[2].Value),
                    lineNumber);
                directives.Add(directive);
                ValidateDirectiveTarget(directive, issues);
                continue;
            }

            var insert = InsertPattern.Match(line);
            if (insert.Success)
            {
                var directive = new NpcScriptDirectiveNode(
                    "insert",
                    insert.Groups[1].Value.Trim(),
                    null,
                    lineNumber);
                directives.Add(directive);
                ValidateDirectiveTarget(directive, issues);
                continue;
            }

            var pageMatch = PagePattern.Match(line);
            if (pageMatch.Success)
            {
                if (pageBuilder is not null)
                {
                    pages.Add(FinalizePage(pageBuilder, issues));
                }
                var key = pageMatch.Groups[1].Value.Trim();
                if (key.Length == 0)
                {
                    issues.Add(new NpcScriptValidationIssue(
                        "empty-page-key",
                        "error",
                        "Page header has an empty key.",
                        lineNumber,
                        null));
                    pageBuilder = null;
                    currentBlock = null;
                    inIfBlock = false;
                    continue;
                }

                var normalizedKey = NormalizePageKey(key);
                if (!seenPageKeys.Add(normalizedKey))
                {
                    issues.Add(new NpcScriptValidationIssue(
                        "duplicate-page",
                        "error",
                        $"Duplicate page key '{key}'.",
                        lineNumber,
                        key));
                }

                pageBuilder = new NpcScriptPageBuilder(key, lineNumber);
                currentBlock = null;
                inIfBlock = false;
                continue;
            }

            if (pageBuilder is null)
            {
                issues.Add(new NpcScriptValidationIssue(
                    "content-outside-page",
                    "error",
                    "Content appears before the first page header.",
                    lineNumber,
                    null));
                continue;
            }

            if (line.TrimStart().StartsWith('#'))
            {
                var block = BlockPattern.Match(line);
                if (!block.Success)
                {
                    issues.Add(new NpcScriptValidationIssue(
                        "unknown-directive",
                        "error",
                        $"Unknown script directive '{line.Trim()}'.",
                        lineNumber,
                        pageBuilder.Key));
                    continue;
                }

                var kind = block.Groups[1].Value.ToLowerInvariant();
                if (kind is "elsesay" or "elseact" && !inIfBlock)
                {
                    issues.Add(new NpcScriptValidationIssue(
                        "else-without-if",
                        "warning",
                        $"#{kind.ToUpperInvariant()} appears without a preceding #IF block.",
                        lineNumber,
                        pageBuilder.Key));
                }

                if (kind == "if")
                {
                    inIfBlock = true;
                }

                currentBlock = pageBuilder.GetOrAddBlock(kind, lineNumber);
                continue;
            }

            if (currentBlock is null)
            {
                currentBlock = pageBuilder.GetOrAddBlock("say", lineNumber);
            }

            if (currentBlock.Kind is "say" or "elsesay")
            {
                AddTextStatement(currentBlock, lineNumber, line);
                foreach (Match target in ButtonTargetPattern.Matches(line))
                {
                    currentBlock.AddReference("button", target.Groups[1].Value);
                }
            }
            else
            {
                var command = ParseCommand(currentBlock, lineNumber, line);
                statementCount++;
                if (command.Target is not null)
                {
                    currentBlock.AddReference("command", command.Target);
                }

            }
        }

        if (pageBuilder is not null)
        {
            pages.Add(FinalizePage(pageBuilder, issues));
        }

        if (pages.Count == 0)
        {
            issues.Add(new NpcScriptValidationIssue(
                "no-pages",
                "error",
                "Script contains no page headers.",
                1,
                null));
        }

        var pageKeys = pages.Select(page => page.Key).ToArray();
        var knownPages = pageKeys.Select(NormalizePageKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            foreach (var block in page.Blocks)
            {
                foreach (var reference in block.References)
                {
                    ValidateTarget(reference.Target, reference.Kind, page.Key, reference.Line, knownPages, issues);
                }
            }
        }

        var includes = directives.Where(directive => directive.Kind == "include").ToArray();
        var inserts = directives.Where(directive => directive.Kind == "insert").ToArray();
        var calls = pages
            .SelectMany(page => page.Blocks)
            .SelectMany(block => block.Statements)
            .Where(statement => statement.Command == "CALL" && statement.Target is not null)
            .Select(statement => statement.Target!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(target => target, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var ast = new NpcScriptAst(
            language,
            pages,
            directives,
            lines.Length,
            statementCount);
        var compiledSha256 = ContentJson.Sha256Hex(
            ContentJson.Canonicalize(JsonSerializer.SerializeToElement(ast, ContentJson.CompactOptions))
                .GetRawText());

        return new NpcScriptParseResult(
            ast,
            pageKeys,
            includes,
            inserts,
            calls,
            issues,
            compiledSha256);
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static void AddTextStatement(NpcScriptBlockBuilder block, int line, string text)
    {
        block.Statements.Add(new NpcScriptStatementNode("text", line, null, null, text));
    }

    private static NpcScriptStatementNode ParseCommand(
        NpcScriptBlockBuilder block,
        int line,
        string text)
    {
        var trimmed = text.Trim();
        var command = trimmed.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        command = command?.ToUpperInvariant();
        var targetMatch = CommandTargetPattern.Match(trimmed);
        var target = targetMatch.Success
            ? NullIfEmpty(targetMatch.Groups[2].Success
                ? targetMatch.Groups[2].Value
                : targetMatch.Groups[3].Value)
            : null;
        var statement = new NpcScriptStatementNode("command", line, command, target, trimmed);
        block.Statements.Add(statement);
        return statement;
    }

    private static void ValidateTarget(
        string target,
        string referenceKind,
        string page,
        int line,
        IReadOnlySet<string> knownPages,
        ICollection<NpcScriptValidationIssue> issues)
    {
        if (!target.StartsWith('@')) return;
        if (target.Contains('%') || target.Contains('$')) return;

        var normalized = NormalizePageKey(target);
        if (knownPages.Contains(normalized) || BuiltInPages.Contains(normalized)) return;

        issues.Add(new NpcScriptValidationIssue(
            $"unresolved-{referenceKind}-target",
            "error",
            $"Page target '{target}' referenced by {referenceKind} does not exist.",
            line,
            page));
    }

    private static void ValidateDirectiveTarget(
        NpcScriptDirectiveNode directive,
        ICollection<NpcScriptValidationIssue> issues)
    {
        if (directive.Target.Length == 0)
        {
            issues.Add(new NpcScriptValidationIssue(
                "empty-include-target",
                "error",
                $"#{directive.Kind.ToUpperInvariant()} target is empty.",
                directive.Line,
                null));
        }
    }

    private static NpcScriptPageNode FinalizePage(
        NpcScriptPageBuilder? pageBuilder,
        ICollection<NpcScriptValidationIssue> issues)
    {
        if (pageBuilder is null)
        {
            throw new ArgumentNullException(nameof(pageBuilder));
        }

        var blocks = pageBuilder.BuildBlocks();
        if (blocks.Count == 0)
        {
            issues.Add(new NpcScriptValidationIssue(
                "empty-page",
                "warning",
                $"Page '{pageBuilder.Key}' has no content.",
                pageBuilder.Line,
                pageBuilder.Key));
        }

        return new NpcScriptPageNode(pageBuilder.Key, pageBuilder.Line, blocks);
    }

    private static string NormalizePageKey(string key)
    {
        var trimmed = key.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed.StartsWith('@') ? trimmed.ToUpperInvariant() : $"@{trimmed.ToUpperInvariant()}";
    }

    private sealed class NpcScriptPageBuilder
    {
        private readonly List<NpcScriptBlockBuilder> _blocks = new();
        private readonly Dictionary<string, NpcScriptBlockBuilder> _openBlocks = new(StringComparer.Ordinal);

        public NpcScriptPageBuilder(string key, int line)
        {
            Key = key;
            Line = line;
        }

        public string Key { get; }

        public int Line { get; }

        public NpcScriptBlockBuilder GetOrAddBlock(string kind, int line)
        {
            if (_openBlocks.TryGetValue(kind, out var existing) && existing.Kind == kind)
            {
                return existing;
            }

            var block = new NpcScriptBlockBuilder(kind, line);
            _blocks.Add(block);
            _openBlocks[kind] = block;
            return block;
        }

        public IReadOnlyList<NpcScriptBlockNode> BuildBlocks()
        {
            return _blocks
                .Select(block => new NpcScriptBlockNode(
                    block.Kind,
                    block.Line,
                    block.Statements.ToArray(),
                    block.References.Select(reference => new NpcScriptReferenceNode(
                        reference.Kind,
                        reference.Target,
                        reference.Line)).ToArray()))
                .ToArray();
        }
    }

    private sealed class NpcScriptBlockBuilder
    {
        private readonly List<(string Kind, string Target, int Line)> _references = new();

        public NpcScriptBlockBuilder(string kind, int line)
        {
            Kind = kind;
            Line = line;
        }

        public string Kind { get; }

        public int Line { get; }

        public List<NpcScriptStatementNode> Statements { get; } = new();

        public IReadOnlyList<(string Kind, string Target, int Line)> References => _references;

        public void AddReference(string kind, string target, int? line = null)
        {
            _references.Add((kind, target, line ?? Line));
        }
    }
}

namespace Crystal.Content;

public sealed record NpcScriptAst(
    string Language,
    IReadOnlyList<NpcScriptPageNode> Pages,
    IReadOnlyList<NpcScriptDirectiveNode> Directives,
    int LineCount,
    int StatementCount);

public sealed record NpcScriptPageNode(
    string Key,
    int Line,
    IReadOnlyList<NpcScriptBlockNode> Blocks);

public sealed record NpcScriptBlockNode(
    string Kind,
    int Line,
    IReadOnlyList<NpcScriptStatementNode> Statements,
    IReadOnlyList<NpcScriptReferenceNode> References);

public sealed record NpcScriptReferenceNode(
    string Kind,
    string Target,
    int Line);

public sealed record NpcScriptStatementNode(
    string Kind,
    int Line,
    string? Command,
    string? Target,
    string? Text);

public sealed record NpcScriptDirectiveNode(
    string Kind,
    string Target,
    string? Argument,
    int Line);

public sealed record NpcScriptValidationIssue(
    string Code,
    string Severity,
    string Message,
    int? Line,
    string? Page);

public sealed record NpcScriptParseResult(
    NpcScriptAst Ast,
    IReadOnlyList<string> PageKeys,
    IReadOnlyList<NpcScriptDirectiveNode> Includes,
    IReadOnlyList<NpcScriptDirectiveNode> Inserts,
    IReadOnlyList<string> Calls,
    IReadOnlyList<NpcScriptValidationIssue> Issues,
    string CompiledSha256)
{
    public int ErrorCount => Issues.Count(issue => issue.Severity == "error");

    public int WarningCount => Issues.Count(issue => issue.Severity == "warning");

    public bool IsValid => ErrorCount == 0;
}
